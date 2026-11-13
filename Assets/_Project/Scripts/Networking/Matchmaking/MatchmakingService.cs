using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Party;
using TinyTumble.Networking.Session;
using TinyTumble.Networking.Transport;
using UnityEngine;
using VContainer.Unity;

namespace TinyTumble.Networking.Matchmaking
{
    /// <summary>
    /// «Найти игру» по модели «хост или присоединение» (networking.md, «Пати и матчмейкинг»).
    /// Лидер пати (хост своей сессии, в которой только его пати) ищет открытые лобби: места на всю пати, версия,
    /// пинг. Нашёл — пати переезжает туда (<see cref="PartyService"/>); хост отказал или вернул по пингу — следующий.
    /// Не нашёл — наше лобби открывается для всех, и мы ждём, время от времени ища лобби полнее нашего
    /// (пока у нас только своя пати). Лобби заполнилось или время вышло — хост начинает матч, пустые места занимают боты.
    /// Срок и флаги поиска лежат в объекте сессии (<see cref="MatchmakingInfo"/>): их видят все и продолжает
    /// новый хост после миграции.
    /// Между матчами сессия, собранная поиском, снова открывается: лобби ждёт новых игроков
    /// <see cref="SessionConfig.BetweenMatchesSeconds"/>, они занимают места ботов, и матч начинается сам.
    /// </summary>
    public sealed class MatchmakingService : IMatchmaking, IInitializable, ITickable, IDisposable
    {
        private const string LogTag = "Matchmaking";

        private readonly NetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly PartyService _party;
        private readonly ISessionLauncher _launcher;
        private readonly IGameFlow _flow;
        private readonly ILocalPlayers _localPlayers;
        private readonly SessionTransports _transports;
        private readonly LobbyDirectories _directories;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;
        private readonly List<LobbyAd> _ads = new List<LobbyAd>(16);
        private readonly HashSet<ulong> _excluded = new HashSet<ulong>();

        private CancellationTokenSource _search;
        private ILobbyDirectory _directory;
        private long _deadlineTicks;
        private float _startedAt;
        private bool _resumeAfterBounce;
        private bool _startingMatch;
        private bool _reopenedThisLobby;
        private bool _wasInLobby;
        private float _lobbyEnteredAt;

        public MatchmakingService(
            NetworkSession session,
            SessionLobby lobby,
            PartyService party,
            ISessionLauncher launcher,
            IGameFlow flow,
            ILocalPlayers localPlayers,
            SessionTransports transports,
            LobbyDirectories directories,
            SessionConfig config,
            IGameLog log)
        {
            _session = session;
            _lobby = lobby;
            _party = party;
            _launcher = launcher;
            _flow = flow;
            _localPlayers = localPlayers;
            _transports = transports;
            _directories = directories;
            _config = config;
            _log = log;
        }

        public event Action<MatchmakingState> StateChanged;

        public MatchmakingState State { get; private set; }

        /// <summary>Сколько искать до старта с ботами (автотест укорачивает); 0 и меньше — из конфига.</summary>
        public float SearchTimeoutOverride { get; set; }

        public bool IsAvailable => CurrentDirectory() != null;

        public bool CanStartSearch => State == MatchmakingState.Idle && IsAvailable && (IsInMenu() || CanSearchInSession());

        public float SecondsLeft => _lobby.IsAttached ? _lobby.Matchmaking.SecondsLeft(DateTime.UtcNow.Ticks) : -1f;

        public bool IsMatchmadeSession => _lobby.IsAttached && _lobby.Matchmaking.Matchmade;

        public void Initialize()
        {
            _session.ShutdownRequested += OnShutdownRequested;
            _party.MoveStarted += OnPartyMoveStarted;
            _party.MoveFinished += OnPartyMoveFinished;
        }

        public void Dispose()
        {
            _session.ShutdownRequested -= OnShutdownRequested;
            _party.MoveStarted -= OnPartyMoveStarted;
            _party.MoveFinished -= OnPartyMoveFinished;
            StopLoop();
            if (_directory != null)
            {
                _directory.Unpublish();
            }
        }

        public bool StartSearch()
        {
            if (!CanStartSearch)
            {
                return false;
            }

            _excluded.Clear();
            _directory = CurrentDirectory();
            float timeout = SearchTimeoutOverride > 0f ? SearchTimeoutOverride : _config.SearchTimeout;
            _deadlineTicks = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(timeout).Ticks;
            _startedAt = Time.realtimeSinceStartup;
            _log.Info(LogTag, $"Searching for a game for up to {timeout:0} s.");

            // «Найти игру» нажали мышью, а игрока ещё нет: входим с клавиатуры, как при подключении из меню.
            // Иначе лобби объявится пустым, а пати без игроков приедет и не займёт ни одного слота.
            _localPlayers.TryJoinDefault();
            StartLoop(IsInMenu(), false);
            return true;
        }

        public void Cancel()
        {
            if (State == MatchmakingState.Idle)
            {
                return;
            }

            _log.Info(LogTag, "Search cancelled.");
            // Отменили в лобби — до следующего матча оно снова сам не откроется.
            _reopenedThisLobby = true;
            StopLoop();
            Unpublish();
            if (_session.IsHost && _lobby.IsAttached)
            {
                // Чужих в лобби нет — сессия снова обычная; если кто-то уже пришёл по поиску, она остаётся «найденной».
                _lobby.SetMatchmaking(new MatchmakingInfo(false, false, !OnlyOwnParty(), 0));
            }

            SetState(MatchmakingState.Idle);
        }

        public void Tick()
        {
            if (!_session.IsHost || !_lobby.IsAttached || _startingMatch)
            {
                _wasInLobby = false;
                return;
            }

            MatchmakingInfo info = _lobby.Matchmaking;
            bool inLobby = _lobby.HostState == GameState.Lobby && !_flow.IsTransitioning;
            if (inLobby && !_wasInLobby)
            {
                _lobbyEnteredAt = Time.realtimeSinceStartup;
                _reopenedThisLobby = false;
            }

            _wasInLobby = inLobby;

            // Хост сам начал матч кнопкой, пока ждал игроков: объявление снимаем, поиск закончен.
            if (!inLobby && _lobby.HostState != GameState.Lobby
                && (State == MatchmakingState.Searching || State == MatchmakingState.WaitingAsHost))
            {
                _log.Info(LogTag, "The match started: the lobby is closed for the search.");
                StopLoop();
                Unpublish();
                _lobby.SetMatchmaking(new MatchmakingInfo(false, false, true, 0));
                SetState(MatchmakingState.Idle);
                return;
            }

            // Сессия собрана поиском и вернулась в лобби: снова ждём новых игроков на места ботов.
            if (inLobby && State == MatchmakingState.Idle && info.Matchmade && !info.Searching && !_reopenedThisLobby && !_party.IsMoving)
            {
                _reopenedThisLobby = true;
                _directory = CurrentDirectory();
                if (_directory != null)
                {
                    float wait = SearchTimeoutOverride > 0f ? SearchTimeoutOverride : _config.BetweenMatchesSeconds;
                    _deadlineTicks = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(wait).Ticks;
                    _startedAt = Time.realtimeSinceStartup;
                    _excluded.Clear();
                    _log.Info(LogTag, $"Back in the lobby: open for new players for {wait:0} s.");
                    StartLoop(false, true);
                }

                return;
            }

            // Мы стали хостом после миграции, а сессия ещё ждала игроков: продолжаем с тем же сроком.
            if (State == MatchmakingState.Idle && info.Searching && !_party.IsMoving)
            {
                _directory = CurrentDirectory();
                if (_directory == null)
                {
                    return;
                }

                _deadlineTicks = info.EndsAtUtcTicks;
                _startedAt = Time.realtimeSinceStartup;
                _log.Info(LogTag, "We host a session that was waiting for players: carrying on.");
                StartLoop(false, info.Open);
                return;
            }

            if (State != MatchmakingState.Searching && State != MatchmakingState.WaitingAsHost)
            {
                return;
            }

            if (State == MatchmakingState.WaitingAsHost && _directory != null)
            {
                _directory.Publish(CountPlayers(), _lobby.FreeSlots);
            }

            // Полное лобби не стартует сразу после возврата из матча: дать прочитать итоги и уйти желающим.
            bool full = _lobby.FreeSlots == 0 && Time.realtimeSinceStartup - _lobbyEnteredAt >= _config.MinLobbySeconds;

            // Время вышло, но к нам как раз подключается пати: дождёмся её (места держатся не дольше 2 × ConnectTimeout).
            bool timeUp = DateTime.UtcNow.Ticks >= _deadlineTicks && !_lobby.HasPartyHolds;
            if ((full || timeUp) && inLobby)
            {
                StartMatchAsync(full).Forget();
            }
        }

        private void StartLoop(bool fromMenu, bool alreadyWaiting)
        {
            StopLoop();
            _search = new CancellationTokenSource();
            SearchAsync(fromMenu, alreadyWaiting, _search.Token).Forget();
        }

        private void StopLoop()
        {
            if (_search != null)
            {
                _search.Cancel();
                _search.Dispose();
                _search = null;
            }
        }

        private async UniTaskVoid SearchAsync(bool fromMenu, bool alreadyWaiting, CancellationToken token)
        {
            try
            {
                SetState(alreadyWaiting ? MatchmakingState.WaitingAsHost : MatchmakingState.Searching);
                if (fromMenu)
                {
                    // Из меню: сначала своё лобби (пати из одного себя), потом поиск. Если ничего не найдём, оно и откроется.
                    SessionStartResult hosted = await _launcher.HostAsync();
                    if (!hosted.Success)
                    {
                        _log.Warning(LogTag, $"Could not host before searching: {hosted.Reason}.");
                        SetState(MatchmakingState.Idle);
                        return;
                    }

                    await UniTask.WaitUntil(() => _lobby.IsAttached && _lobby.HostState == GameState.Lobby && !_flow.IsTransitioning,
                        cancellationToken: token);
                }

                PublishInfo(alreadyWaiting);
                await LoopAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Поиск отменили, матч начался или сессия закончилась.
            }
        }

        private async UniTask LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_session.IsHost)
                {
                    return;
                }

                bool waiting = State == MatchmakingState.WaitingAsHost;

                // Когда к нам уже пришли чужие, мы их хост: уезжать нельзя, только ждать.
                if (!waiting || OnlyOwnParty())
                {
                    bool wide = Time.realtimeSinceStartup - _startedAt >= _config.CloseSearchSeconds;
                    int partySlots = _lobby.CountPartySlots(_party.PartyId);
                    await _directory.SearchAsync(new LobbyQuery(_config.ProtocolVersion, partySlots, wide), _ads, token);

                    int ourPlayers = waiting ? CountPlayers() : -1;
                    if (MatchmakingRules.TryPick(_ads, partySlots, _config.MaxEstimatedPingMs, _excluded, _directory.OwnKey, ourPlayers, out LobbyAd ad))
                    {
                        if (await JoinAsync(ad, token))
                        {
                            return;
                        }

                        continue;
                    }
                }

                if (State != MatchmakingState.WaitingAsHost)
                {
                    SetState(MatchmakingState.WaitingAsHost);
                    PublishInfo(true);
                    _log.Info(LogTag, "No open lobby fits: our lobby is open, waiting for players.");
                }

                await UniTask.Delay(TimeSpan.FromSeconds(_config.SearchInterval), ignoreTimeScale: true, cancellationToken: token);
            }
        }

        /// <returns>true — пати у нового хоста; false — вернулись к себе (отказ, недоступен), ищем дальше.</returns>
        private async UniTask<bool> JoinAsync(LobbyAd ad, CancellationToken token)
        {
            SetState(MatchmakingState.Joining);
            Unpublish();
            _excluded.Add(ad.Key);
            PublishInfo(false);
            _log.Info(LogTag, $"Found a lobby at {ad.Host}: {ad.Players} player(s), {ad.FreeSlots} free, ping {ad.PingMs} ms. Moving the party.");

            int before = _party.CompletedMoves;
            if (!_party.MoveTo(ad.Host, true))
            {
                SetState(MatchmakingState.Searching);
                return false;
            }

            await UniTask.WaitUntil(() => _party.CompletedMoves > before, cancellationToken: token);
            if (_session.IsRemoteClient)
            {
                _log.Info(LogTag, "The party joined a found lobby.");
                SetState(MatchmakingState.Idle);
                return true;
            }

            if (!_session.IsHost)
            {
                // Пати не смогла ни переехать, ни собраться у нас: сессии нет.
                SetState(MatchmakingState.Idle);
                throw new OperationCanceledException();
            }

            // Хост не принял пати или недоступен: мы снова хост своей пати в новой сессии.
            _log.Info(LogTag, "The lobby did not take us, searching on.");
            await UniTask.WaitUntil(() => _lobby.IsAttached, cancellationToken: token);
            SetState(MatchmakingState.Searching);
            PublishInfo(false);
            return false;
        }

        private async UniTaskVoid StartMatchAsync(bool full)
        {
            _startingMatch = true;
            try
            {
                _log.Info(LogTag, full ? "The lobby is full: starting the match." : "Search time is up: starting the match, bots take the free slots.");
                StopLoop();
                Unpublish();
                _lobby.SetMatchmaking(new MatchmakingInfo(false, false, true, 0));
                SetState(MatchmakingState.Idle);
                await _flow.GoToAsync(GameState.Match);
            }
            finally
            {
                _startingMatch = false;
            }
        }

        private void OnShutdownRequested()
        {
            // Пати переезжает сама — это часть поиска (или возврат по пингу), его не прерываем.
            if (_party.IsMoving || State == MatchmakingState.Idle)
            {
                return;
            }

            _log.Info(LogTag, "The session closed: search stopped.");
            StopLoop();
            Unpublish();
            SetState(MatchmakingState.Idle);
        }

        private void OnPartyMoveStarted()
        {
            // Хост, к которому нас привёл поиск, вернул пати из-за пинга: продолжим поиск без него.
            PartyMove move = _party.CurrentMove;
            if (move.Reason == PartyMoveReason.HighPing && _party.IsLeader && State == MatchmakingState.Idle)
            {
                _resumeAfterBounce = true;
                if (_session.IsRemoteClient)
                {
                    _excluded.Add(KeyOf(_session.ServerEndpoint));
                }
            }
        }

        private void OnPartyMoveFinished()
        {
            if (!_resumeAfterBounce)
            {
                return;
            }

            _resumeAfterBounce = false;
            if (_session.IsHost && _directory != null && DateTime.UtcNow.Ticks < _deadlineTicks)
            {
                _log.Info(LogTag, "The host sent the party back (ping too high): searching on.");
                StartLoop(false, false);
            }
        }

        /// <summary>Ключ объявления хоста, к которому мы подключены, — среди последних найденных.</summary>
        private ulong KeyOf(SessionEndpoint host)
        {
            for (int i = 0; i < _ads.Count; i++)
            {
                if (_ads[i].Host == host)
                {
                    return _ads[i].Key;
                }
            }

            return 0;
        }

        private void PublishInfo(bool open)
        {
            if (_session.IsHost && _lobby.IsAttached)
            {
                _lobby.SetMatchmaking(new MatchmakingInfo(true, open, true, _deadlineTicks));
            }
        }

        private void Unpublish()
        {
            if (_directory != null)
            {
                _directory.Unpublish();
            }
        }

        private ILobbyDirectory CurrentDirectory()
        {
            EndpointKind kind = _session.Role.CurrentValue != SessionRole.None ? _session.TransportKind : _transports.PreferredHostKind;
            return _directories.For(kind);
        }

        private bool IsInMenu()
        {
            return _session.Role.CurrentValue == SessionRole.None && _flow.Current == GameState.MainMenu
                && !_flow.IsTransitioning && !_launcher.IsBusy;
        }

        private bool CanSearchInSession()
        {
            return _session.IsHost && _party.IsLeader && !_party.IsMoving && _lobby.IsAttached
                && _lobby.HostState == GameState.Lobby && _lobby.FreeSlots > 0 && OnlyOwnParty();
        }

        /// <summary>В сессии только наша пати: её можно увезти, никого не бросив.</summary>
        private bool OnlyOwnParty()
        {
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            ulong own = _party.PartyId;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].PartyId != own)
                {
                    return false;
                }
            }

            return true;
        }

        private int CountPlayers()
        {
            return _lobby.Capacity - _lobby.FreeSlots;
        }

        private void SetState(MatchmakingState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            try
            {
                StateChanged?.Invoke(state);
            }
            catch (Exception exception)
            {
                _log.Error(LogTag, exception.ToString());
            }
        }
    }
}
