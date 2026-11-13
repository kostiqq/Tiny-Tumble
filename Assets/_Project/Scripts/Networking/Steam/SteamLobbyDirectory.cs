using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Steamworks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Matchmaking;
using VContainer.Unity;

namespace TinyTumble.Networking.Steam
{
    /// <summary>
    /// Каталог лобби через Steam. Объявление — то же лобби Steam, что создаётся для приглашений
    /// (<see cref="SteamInvites"/>): пока хост ждёт игроков, лобби публичное и в его данных стоят tt_open=1,
    /// число игроков и мест и ping location хоста. Ищущий фильтрует список лобби по игре (на App ID 480 список
    /// общий со всеми), версии, открытости и местам, сначала ближние (фильтр расстояния «близко»), потом
    /// «по умолчанию»; пинг до каждого оценивает по ping location без подключения. В лобби ищущий не входит:
    /// подключается к SteamID из tt_host.
    /// </summary>
    public sealed class SteamLobbyDirectory : ILobbyDirectory, ITickable, IDisposable
    {
        private const string LogTag = "Matchmaking";
        public const string GameKey = "tt_game";
        public const string GameValue = "tinytumble";
        private const string OpenKey = "tt_open";
        private const string PlayersKey = "tt_players";
        private const string FreeKey = "tt_free";
        private const string PingKey = "tt_ping";
        private const int MaxResults = 50;

        private readonly SteamService _steam;
        private readonly SteamInvites _invites;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;

        private CallResult<LobbyMatchList_t> _listResult;
        private UniTaskCompletionSource<int> _pendingSearch;
        private bool _wanted;
        private bool _applied;
        private int _players;
        private int _freeSlots;
        private CSteamID _appliedLobby = CSteamID.Nil;

        public SteamLobbyDirectory(SteamService steam, SteamInvites invites, SessionConfig config, IGameLog log)
        {
            _steam = steam;
            _invites = invites;
            _config = config;
            _log = log;
        }

        public ulong OwnKey => _invites.Lobby.m_SteamID;

        public bool Supports(EndpointKind kind)
        {
            return kind == EndpointKind.Steam && _steam.IsReady;
        }

        public async UniTask SearchAsync(LobbyQuery query, List<LobbyAd> results, CancellationToken cancellationToken)
        {
            results.Clear();
            if (!_steam.IsReady || _pendingSearch != null)
            {
                return;
            }

            if (_listResult == null)
            {
                _listResult = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            }

            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(SteamInvites.VersionKey,
                query.ProtocolVersion.ToString(CultureInfo.InvariantCulture), ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(OpenKey, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListNumericalFilter(FreeKey, query.MinFreeSlots, ELobbyComparison.k_ELobbyComparisonEqualToOrGreaterThan);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(query.Wide
                ? ELobbyDistanceFilter.k_ELobbyDistanceFilterDefault
                : ELobbyDistanceFilter.k_ELobbyDistanceFilterClose);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(MaxResults);

            _pendingSearch = new UniTaskCompletionSource<int>();
            _listResult.Set(SteamMatchmaking.RequestLobbyList());
            int count;
            try
            {
                count = await _pendingSearch.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                _pendingSearch = null;
            }

            ulong own = OwnKey;
            for (int i = 0; i < count; i++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
                if (!lobby.IsValid() || lobby.m_SteamID == own)
                {
                    continue;
                }

                ulong host = ParseULong(SteamMatchmaking.GetLobbyData(lobby, SteamInvites.HostKey));
                if (host == 0 || host == _steam.LocalSteamId)
                {
                    continue;
                }

                int players = ParseInt(SteamMatchmaking.GetLobbyData(lobby, PlayersKey));
                int free = ParseInt(SteamMatchmaking.GetLobbyData(lobby, FreeKey));
                int ping = EstimatePing(SteamMatchmaking.GetLobbyData(lobby, PingKey));
                results.Add(new LobbyAd(SessionEndpoint.Steam(host), lobby.m_SteamID, players, free, ping));
            }
        }

        public void Publish(int players, int freeSlots)
        {
            if (_wanted && _players == players && _freeSlots == freeSlots && _applied)
            {
                return;
            }

            _wanted = true;
            _players = players;
            _freeSlots = freeSlots;
            _applied = false;
        }

        public void Unpublish()
        {
            if (!_wanted)
            {
                return;
            }

            _wanted = false;
            _applied = false;
            CSteamID lobby = _invites.Lobby;
            if (_steam.IsReady && lobby.IsValid() && _invites.OwnsLobby)
            {
                SteamMatchmaking.SetLobbyData(lobby, OpenKey, "0");
                SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypeFriendsOnly);
            }

            _appliedLobby = CSteamID.Nil;
        }

        /// <summary>Лобби создаётся асинхронно и после миграции переходит к новому хосту: применяем объявление, когда оно наше.</summary>
        public void Tick()
        {
            if (!_wanted || _applied || !_steam.IsReady)
            {
                return;
            }

            CSteamID lobby = _invites.Lobby;
            if (!lobby.IsValid() || !_invites.OwnsLobby)
            {
                return;
            }

            if (_appliedLobby != lobby)
            {
                SteamMatchmaking.SetLobbyType(lobby, ELobbyType.k_ELobbyTypePublic);
                SteamMatchmaking.SetLobbyData(lobby, GameKey, GameValue);
                SteamMatchmaking.SetLobbyData(lobby, OpenKey, "1");
                string pingLocation = LocalPingLocation();
                if (pingLocation != null)
                {
                    SteamMatchmaking.SetLobbyData(lobby, PingKey, pingLocation);
                }

                _appliedLobby = lobby;
                _log.Info(LogTag, $"Steam lobby {lobby.m_SteamID} is public: waiting for players.");
            }

            SteamMatchmaking.SetLobbyData(lobby, PlayersKey, _players.ToString(CultureInfo.InvariantCulture));
            SteamMatchmaking.SetLobbyData(lobby, FreeKey, _freeSlots.ToString(CultureInfo.InvariantCulture));
            _applied = true;
        }

        public void Dispose()
        {
            if (_listResult != null)
            {
                _listResult.Dispose();
                _listResult = null;
            }

            if (_pendingSearch != null)
            {
                _pendingSearch.TrySetResult(0);
            }
        }

        private void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            if (_pendingSearch == null)
            {
                return;
            }

            if (ioFailure)
            {
                _log.Warning(LogTag, "Steam lobby search failed (IO failure).");
            }

            _pendingSearch.TrySetResult(ioFailure ? 0 : (int)result.m_nLobbiesMatching);
        }

        /// <summary>Наша ping location строкой или null, если Steam ещё не измерил её (сразу после запуска).</summary>
        private static string LocalPingLocation()
        {
            float age = SteamNetworkingUtils.GetLocalPingLocation(out SteamNetworkPingLocation_t location);
            if (age < 0f)
            {
                return null;
            }

            SteamNetworkingUtils.ConvertPingLocationToString(ref location, out string text, Constants.k_cchMaxSteamNetworkingPingLocationString);
            return text;
        }

        /// <summary>RTT до хоста по его ping location, мс; -1, если оценить нельзя.</summary>
        private static int EstimatePing(string pingLocation)
        {
            if (string.IsNullOrEmpty(pingLocation)
                || !SteamNetworkingUtils.ParsePingLocationString(pingLocation, out SteamNetworkPingLocation_t remote))
            {
                return -1;
            }

            return SteamNetworkingUtils.EstimatePingTimeFromLocalHost(ref remote);
        }

        private static ulong ParseULong(string text)
        {
            return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ? value : 0;
        }

        private static int ParseInt(string text)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }
    }
}
