using System;
using System.Globalization;
using Cysharp.Threading.Tasks;
using R3;
using Steamworks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Party;
using TinyTumble.Networking.Session;
using VContainer.Unity;

namespace TinyTumble.Networking.Steam
{
    /// <summary>
    /// Приглашения через Steam. Хост сессии по Steam создаёт лобби «только для друзей»; приглашённый (через
    /// оверлей, «Присоединиться» в списке друзей или запуск игры с +connect_lobby) входит в лобби, читает
    /// SteamID хоста из данных лобби и подключается к нему. Лобби здесь только «дверь»: состав и слоты
    /// по-прежнему ведёт NGO-сессия.
    /// После миграции владельцем лобби Steam становится случайный участник; он передаёт владение новому
    /// NGO-хосту, а тот записывает в данные лобби свой SteamID.
    /// Лидер пати, принявший приглашение посреди своей сессии, уводит туда всю пати (<see cref="IParty.MoveTo"/>).
    /// Остальные участники при переезде выходят из старого лобби Steam: оно принадлежало старой сессии.
    /// </summary>
    public sealed class SteamInvites : ISessionInvites, ITickable, IDisposable
    {
        private const string LogTag = "Steam";
        public const string HostKey = "tt_host";
        public const string VersionKey = "tt_ver";
        private const string ConnectLobbyArgument = "+connect_lobby";

        private readonly SteamService _steam;
        private readonly NetworkSession _session;
        private readonly ISessionLauncher _launcher;
        private readonly IGameFlow _flow;
        private readonly SessionConfig _config;
        private readonly PartyService _party;
        private readonly IGameLog _log;

        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<LobbyChatUpdate_t> _lobbyChanged;
        private CallResult<LobbyCreated_t> _lobbyCreated;
        private IDisposable _roleSubscription;
        private bool _setUp;
        private bool _creating;
        private bool _joining;
        private CSteamID _lobby = CSteamID.Nil;
        private CSteamID _pendingLobby = CSteamID.Nil;

        // Лобби, в которое мы вошли, чтобы увести туда пати: при переезде из него не выходим.
        private bool _lobbyIsMoveTarget;

        public SteamInvites(
            SteamService steam,
            NetworkSession session,
            ISessionLauncher launcher,
            IGameFlow flow,
            SessionConfig config,
            PartyService party,
            IGameLog log)
        {
            _steam = steam;
            _session = session;
            _launcher = launcher;
            _flow = flow;
            _config = config;
            _party = party;
            _log = log;
        }

        public bool CanInvite => _steam.IsReady && _lobby.IsValid();

        /// <summary>Лобби Steam текущей сессии (или Nil). Его же публикует каталог поиска игры.</summary>
        public CSteamID Lobby => _lobby;

        /// <summary>Мы владелец лобби: только владелец меняет его данные и тип.</summary>
        public bool OwnsLobby => _steam.IsReady && _lobby.IsValid() && SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID == _steam.LocalSteamId;

        public bool OpenInviteDialog()
        {
            if (!CanInvite)
            {
                return false;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
            return true;
        }

        public void Tick()
        {
            if (!_steam.IsReady)
            {
                return;
            }

            // Настраиваемся в первом кадре с готовым Steam: порядок IInitializable между сервисами не гарантирован.
            if (!_setUp)
            {
                SetUp();
            }

            TryJoinPendingLobby();
            LeaveLobbyIfSessionEnded();
        }

        public void Dispose()
        {
            if (_roleSubscription != null)
            {
                _roleSubscription.Dispose();
                _roleSubscription = null;
            }

            if (_setUp)
            {
                _party.MoveStarted -= OnPartyMoveStarted;
                _party.MoveFinished -= OnPartyMoveFinished;
            }

            if (_steam.IsReady)
            {
                LeaveLobby();
            }

            DisposeCallback(ref _joinRequested);
            DisposeCallback(ref _lobbyEntered);
            DisposeCallback(ref _lobbyChanged);
            if (_lobbyCreated != null)
            {
                _lobbyCreated.Dispose();
                _lobbyCreated = null;
            }
        }

        /// <summary>Аргумент запуска из Steam: "+connect_lobby 1234". Вынесено для тестов.</summary>
        public static bool TryParseConnectLobby(string[] args, out ulong lobbyId)
        {
            lobbyId = 0;
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], ConnectLobbyArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out lobbyId) && lobbyId != 0;
                }
            }

            return false;
        }

        private void SetUp()
        {
            _setUp = true;
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyChanged = Callback<LobbyChatUpdate_t>.Create(OnLobbyChanged);
            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _roleSubscription = _session.Role.Subscribe(OnRoleChanged);
            _party.MoveStarted += OnPartyMoveStarted;
            _party.MoveFinished += OnPartyMoveFinished;

            if (TryParseConnectLobby(Environment.GetCommandLineArgs(), out ulong lobbyId))
            {
                _log.Info(LogTag, $"Started from an invite to lobby {lobbyId}.");
                _pendingLobby = new CSteamID(lobbyId);
            }
        }

        private void OnRoleChanged(SessionRole role)
        {
            if (role == SessionRole.None || _session.TransportKind != EndpointKind.Steam)
            {
                return;
            }

            if (role == SessionRole.Host && !_lobby.IsValid())
            {
                CreateLobby();
                return;
            }

            // Мы стали хостом после миграции или клиентом нового хоста: поправить владельца и данные лобби.
            SyncOwnership();
        }

        private void CreateLobby()
        {
            if (_creating)
            {
                return;
            }

            _creating = true;
            _lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, SlotTable.Capacity));
        }

        private void OnPartyMoveStarted()
        {
            if (!_lobbyIsMoveTarget)
            {
                LeaveLobby();
            }
        }

        private void OnPartyMoveFinished()
        {
            _lobbyIsMoveTarget = false;

            // Переехать не вышло, и пати собралась у нас: лобби, в которое мы входили, — чужое, нужно своё.
            if (_session.Role.CurrentValue == SessionRole.Host && _session.TransportKind == EndpointKind.Steam && _lobby.IsValid()
                && SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID != _steam.LocalSteamId)
            {
                LeaveLobby();
                CreateLobby();
            }
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            _creating = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                _log.Warning(LogTag, $"Could not create a Steam lobby for invites: {(ioFailure ? "IO failure" : result.m_eResult.ToString())}.");
                return;
            }

            _lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (_session.Role.CurrentValue != SessionRole.Host)
            {
                // Пока лобби создавалось, сессия закончилась.
                LeaveLobby();
                return;
            }

            SteamMatchmaking.SetLobbyData(_lobby, VersionKey, _config.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            SteamMatchmaking.SetLobbyData(_lobby, HostKey, _steam.LocalSteamId.ToString(CultureInfo.InvariantCulture));
            _log.Info(LogTag, $"Steam lobby {_lobby.m_SteamID} is open for invites.");
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t request)
        {
            bool inSession = _session.Role.CurrentValue != SessionRole.None || _session.IsRecovering;
            if (_launcher.IsBusy || (inSession && !_party.CanMove))
            {
                // Участник пати (не лидер) в сессии по приглашению не уходит: пати держится вместе.
                _log.Info(LogTag, "Invite ignored: only the party leader can take the party elsewhere. Leave to the main menu to go alone.");
                return;
            }

            _pendingLobby = request.m_steamIDLobby;
        }

        private void TryJoinPendingLobby()
        {
            if (!_pendingLobby.IsValid() || _joining || _launcher.IsBusy || _flow.IsTransitioning)
            {
                return;
            }

            bool fromMenu = _flow.Current == GameState.MainMenu && _session.Role.CurrentValue == SessionRole.None;
            if (!fromMenu && !_party.CanMove)
            {
                return;
            }

            // Лидер уводит пати: лобби старой сессии ему больше не нужно.
            LeaveLobby();
            _joining = true;
            _log.Info(LogTag, $"Joining Steam lobby {_pendingLobby.m_SteamID}...");
            SteamMatchmaking.JoinLobby(_pendingLobby);
            _pendingLobby = CSteamID.Nil;
        }

        private void OnLobbyEntered(LobbyEnter_t entered)
        {
            var lobby = new CSteamID(entered.m_ulSteamIDLobby);
            if (entered.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                _joining = false;
                _log.Warning(LogTag, $"Could not enter Steam lobby {lobby.m_SteamID}: response {entered.m_EChatRoomEnterResponse}.");
                return;
            }

            _lobby = lobby;
            if (!_joining)
            {
                // Это наше собственное лобби: Steam сообщает о входе и создателю.
                return;
            }

            ConnectToLobbyHostAsync().Forget();
        }

        private async UniTaskVoid ConnectToLobbyHostAsync()
        {
            try
            {
                string version = SteamMatchmaking.GetLobbyData(_lobby, VersionKey);
                if (version != _config.ProtocolVersion.ToString(CultureInfo.InvariantCulture))
                {
                    _log.Warning(LogTag, $"Lobby uses protocol version '{version}', we have {_config.ProtocolVersion}.");
                    LeaveLobby();
                    return;
                }

                ulong host = ReadHost();
                if (host == 0)
                {
                    _log.Warning(LogTag, "The lobby does not name its host.");
                    LeaveLobby();
                    return;
                }

                if (_session.Role.CurrentValue != SessionRole.None)
                {
                    // Мы лидер пати посреди своей сессии: переезжаем к хосту лобби всей пати.
                    _lobbyIsMoveTarget = true;
                    if (!_party.MoveTo(SessionEndpoint.Steam(host)))
                    {
                        _lobbyIsMoveTarget = false;
                        _log.Warning(LogTag, "Could not start moving the party to the lobby host.");
                        LeaveLobby();
                    }

                    return;
                }

                SessionStartResult result = await _launcher.JoinAsync(SessionEndpoint.Steam(host));
                if (!result.Success)
                {
                    _log.Warning(LogTag, $"Could not join the host from the lobby: {result.Reason}.");
                    LeaveLobby();
                }
            }
            finally
            {
                _joining = false;
            }
        }

        private void OnLobbyChanged(LobbyChatUpdate_t update)
        {
            if (_lobby.IsValid() && update.m_ulSteamIDLobby == _lobby.m_SteamID)
            {
                SyncOwnership();
            }
        }

        /// <summary>
        /// Данные лобби меняет только владелец. Владелец-хост пишет свой SteamID; владелец-клиент (стал им,
        /// когда прежний хост ушёл) передаёт владение текущему NGO-хосту.
        /// </summary>
        private void SyncOwnership()
        {
            if (!_lobby.IsValid() || _session.TransportKind != EndpointKind.Steam)
            {
                return;
            }

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(_lobby);
            if (owner.m_SteamID != _steam.LocalSteamId)
            {
                return;
            }

            SessionRole role = _session.Role.CurrentValue;
            if (role == SessionRole.Host)
            {
                if (ReadHost() != _steam.LocalSteamId)
                {
                    SteamMatchmaking.SetLobbyData(_lobby, HostKey, _steam.LocalSteamId.ToString(CultureInfo.InvariantCulture));
                    _log.Info(LogTag, "The lobby now points to us as the host.");
                }

                return;
            }

            SessionEndpoint server = _session.ServerEndpoint;
            if (role == SessionRole.Client && server.Kind == EndpointKind.Steam && server.Address != _steam.LocalSteamId)
            {
                SteamMatchmaking.SetLobbyOwner(_lobby, new CSteamID(server.Address));
            }
        }

        private void LeaveLobbyIfSessionEnded()
        {
            if (_lobby.IsValid() && !_joining && !_creating && _session.Role.CurrentValue == SessionRole.None
                && !_session.IsRecovering && !_launcher.IsBusy && !_party.IsMoving)
            {
                LeaveLobby();
            }
        }

        private ulong ReadHost()
        {
            string text = SteamMatchmaking.GetLobbyData(_lobby, HostKey);
            if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong host) && host != 0)
            {
                return host;
            }

            // Данных ещё нет (лобби только что создано): хост — владелец.
            return SteamMatchmaking.GetLobbyOwner(_lobby).m_SteamID;
        }

        private void LeaveLobby()
        {
            if (!_lobby.IsValid())
            {
                return;
            }

            SteamMatchmaking.LeaveLobby(_lobby);
            _log.Info(LogTag, $"Left Steam lobby {_lobby.m_SteamID}.");
            _lobby = CSteamID.Nil;
        }

        private static void DisposeCallback<T>(ref Callback<T> callback) where T : struct
        {
            if (callback != null)
            {
                callback.Dispose();
                callback = null;
            }
        }
    }
}
