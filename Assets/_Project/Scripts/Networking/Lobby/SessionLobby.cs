using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Infrastructure.Flow;
using TinyTumble.Networking.Party;
using TinyTumble.Networking.Session;
using TinyTumble.Networking.Spawning;
using TinyTumble.Networking.Transport;
using Unity.Netcode;
using UnityEngine;
using VContainer.Unity;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Состав сессии и состояние игры хоста поверх <see cref="SessionNetworkState"/>.
    /// Хост: раздаёт слоты, держит слоты отвалившихся игроков, узнаёт вернувшихся по ключу, ведёт план
    /// миграции, принимает «Готов» и отчёты о загрузке. Клиент: просит слоты для своих локальных игроков,
    /// идёт за состоянием хоста и помнит последний состав и план — они нужны, если хост пропадёт.
    /// Пати: хост записывает пати игрока в его слоты, держит места под подключающуюся чужую пати целиком
    /// и рассылает команду переезда пати; ушедшие с пати игроки освобождают слоты сразу, без резерва.
    /// </summary>
    public sealed class SessionLobby : ILobby, ITickable, IDisposable
    {
        private const string LogTag = "Lobby";

        private readonly NetworkRuntime _runtime;
        private readonly ISessionTransport _transport;
        private readonly NetworkObjectResolver _resolver;
        private readonly GameFlow _localFlow;
        private readonly ILocalPlayers _localPlayers;
        private readonly PlayerIdentity _identity;
        private readonly PartyState _party;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;

        private readonly List<PlayerSlot> _slots = new List<PlayerSlot>(SlotTable.Capacity);
        private readonly List<ulong> _clientsBuffer = new List<ulong>(SlotTable.Capacity);
        private readonly List<ulong> _keysBuffer = new List<ulong>(SlotTable.Capacity);
        private readonly List<PeerInfo> _peersBuffer = new List<PeerInfo>(SlotTable.Capacity);
        private readonly ClientLoadTracker _loads = new ClientLoadTracker();

        // Только у хоста.
        private readonly Dictionary<ulong, ConnectionPayload> _approved = new Dictionary<ulong, ConnectionPayload>();
        private readonly Dictionary<ulong, PeerInfo> _peers = new Dictionary<ulong, PeerInfo>();
        private readonly Dictionary<ulong, float> _reservedSince = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, int> _loadedEpoch = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, PeerParty> _peerParties = new Dictionary<ulong, PeerParty>();
        private readonly Dictionary<ulong, PartyHold> _partyHolds = new Dictionary<ulong, PartyHold>();
        private readonly HashSet<ulong> _departingParties = new HashSet<ulong>();
        private HostSeed _hostSeed;
        private ulong _sessionToken;

        private SessionNetworkState _state;
        private bool _subscribedToManager;

        // Последнее, что клиент знал о сессии. Переживает её конец: по этому восстанавливаются после обрыва.
        private SlotTable _lastSlots;
        private MigrationPlan _lastPlan;
        private MatchmakingInfo _lastMatchmaking;
        private bool _hostAnnouncedLeaving;

        // Клиент: команда переезда пришла раньше нашего слота (см. OnPartyMove).
        private PartyMove _pendingMove;
        private bool _hasPendingMove;

        public SessionLobby(
            NetworkRuntime runtime,
            ISessionTransport transport,
            NetworkObjectResolver resolver,
            GameFlow localFlow,
            ILocalPlayers localPlayers,
            PlayerIdentity identity,
            PartyState party,
            SessionConfig config,
            IGameLog log)
        {
            _runtime = runtime;
            _transport = transport;
            _resolver = resolver;
            _localFlow = localFlow;
            _localPlayers = localPlayers;
            _identity = identity;
            _party = party;
            _config = config;
            _log = log;
        }

        public event Action Changed;

        /// <summary>Клиент: хост перешёл в новое состояние (или мы только что подключились и узнали текущее).</summary>
        public event Action<GameState> HostStateChanged;

        /// <summary>Хост: клиент загрузил текущее состояние. Матч показывает ему свои объекты.</summary>
        public event Action<ulong> ClientLoadedCurrentState;

        /// <summary>
        /// Пришла команда переезда пати (клиенту — от хоста, хосту — от самого себя). Получают все,
        /// выполняют участники этой пати (<see cref="PartyService"/>).
        /// </summary>
        public event Action<PartyMove> PartyMoveStarted;

        /// <summary>Изменилось состояние поиска игры в сессии.</summary>
        public event Action<MatchmakingInfo> MatchmakingChanged;

        /// <summary>Хост: клиент ответил на пинг; второй аргумент — RTT в секундах.</summary>
        public event Action<ulong, float> PongReceived;

        public IReadOnlyList<PlayerSlot> Slots => _slots;

        public int Capacity => SlotTable.Capacity;

        public bool IsAttached => _state != null;

        public bool IsLocalReady => _state == null || IsHost || _state.Slots.IsReady(_runtime.Manager.LocalClientId);

        public bool AreAllReady => _state == null || _state.Slots.AreAllReady(NetworkManager.ServerClientId);

        public int FreeSlots => _state != null ? _state.Slots.FreeCount : SlotTable.Capacity;

        /// <summary>Новых игроков пускаем только в лобби: подключаться посреди матча некуда.</summary>
        public bool IsAcceptingPlayers => _state == null || _state.Phase.State == GameState.Lobby;

        public GameState HostState => _state != null ? _state.Phase.State : GameState.None;

        public ulong SessionToken => IsHost ? _sessionToken : _lastPlan.SessionToken;

        /// <summary>Поиск игры в текущей сессии (у всех одинаковый).</summary>
        public MatchmakingInfo Matchmaking => _state != null ? _state.Matchmaking : default;

        /// <summary>Клиент: последнее известное состояние поиска (переживает конец сессии, нужно преемнику).</summary>
        public MatchmakingInfo LastKnownMatchmaking => _lastMatchmaking;

        /// <summary>Клиент: последний известный состав (переживает конец сессии).</summary>
        public SlotTable LastKnownSlots => _lastSlots;

        /// <summary>Клиент: последний известный план миграции (переживает конец сессии).</summary>
        public MigrationPlan LastKnownPlan => _lastPlan;

        /// <summary>Хост: пати, в которую вступают друзья, подключившиеся без своей пати.</summary>
        public ulong HostPartyId => _party.PartyId;

        /// <summary>Ключ игрока, который хост этой сессии (по последнему известному составу), или 0.</summary>
        public ulong HostPlayerKey => _lastSlots.KeyOfClient(NetworkManager.ServerClientId);

        private bool IsHost => _runtime.Manager.IsServer;

        public void SetLocalReady(bool ready)
        {
            if (_state == null || IsHost)
            {
                return;
            }

            _state.SetReadyRpc(ready);
        }

        // ---------- Хост ----------

        /// <summary>
        /// Хост: создаёт объект сессии сразу после StartHost. Для новой игры seed без состава; после миграции
        /// в нём состав старой сессии и её метка.
        /// </summary>
        public void SpawnState(SessionNetworkState prefab, HostSeed seed)
        {
            _hostSeed = seed;
            _sessionToken = seed.SessionToken;
            GameObject instance = _resolver.Instantiate(prefab.gameObject, Vector3.zero, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn(destroyWithScene: false);
        }

        /// <summary>Хост: за этим игроком держится слот (он отвалился и может вернуться).</summary>
        public bool HasReservation(ulong playerKey)
        {
            return _state != null && playerKey != 0 && _reservedSince.ContainsKey(playerKey);
        }

        /// <summary>
        /// Хост: сколько мест придержано для подключающихся пати — для пати клиента и для всех остальных.
        /// Места держатся, пока не подключатся все участники пати или не выйдет время.
        /// </summary>
        public void GetPartyHolds(ulong partyId, out int heldForParty, out int heldForOthers)
        {
            heldForParty = 0;
            heldForOthers = 0;
            if (_state == null)
            {
                return;
            }

            SlotTable slots = _state.Slots;
            foreach (KeyValuePair<ulong, PartyHold> pair in _partyHolds)
            {
                int held = Math.Max(0, pair.Value.Size - slots.CountParty(pair.Key));
                if (pair.Key == partyId)
                {
                    heldForParty = held;
                }
                else
                {
                    heldForOthers += held;
                }
            }
        }

        /// <summary>
        /// Хост: запомнить, кто подключается (вызывается из одобрения подключения). Первому из чужой пати
        /// хост придерживает места на всю пати.
        /// </summary>
        public void RegisterApproved(ulong clientId, in ConnectionPayload payload)
        {
            _approved[clientId] = payload;
            if (!ConnectionAdmission.IsForeignParty(payload, HostPartyId))
            {
                return;
            }

            ulong partyId = payload.Party.PartyId;
            _departingParties.Remove(partyId);
            if (!_partyHolds.ContainsKey(partyId))
            {
                int size = Math.Max(payload.Party.Size, Math.Max(1, payload.LocalPlayerCount));
                _partyHolds[partyId] = new PartyHold(size, Time.realtimeSinceStartup);
                _log.Info(LogTag, $"Party {partyId:X} is joining: holding {size} slot(s) for it.");
            }
        }

        // ---------- Пати ----------

        /// <summary>
        /// Хост: пати переезжает (к другому хосту или собирается у своего лидера). Участники получают команду
        /// и уходят; их слоты освобождаются сразу. Если уезжает пати самого хоста, остальные игроки сессии
        /// переходят к преемнику, как при уходе хоста, — без участников этой пати в плане.
        /// </summary>
        public bool StartPartyDeparture(PartyMove move)
        {
            if (_state == null || !IsHost || move.PartyId == 0)
            {
                return false;
            }

            // Адрес лидера кладём в саму команду: план миграции сейчас перестроится без уезжающих,
            // и участники не нашли бы в нём лидера.
            move = move.WithLeaderEndpoint(FindSuccessorEndpoint(move.LeaderKey));
            _departingParties.Add(move.PartyId);
            SessionEndpoint target = move.Target;
            SessionEndpoint leader = move.LeaderEndpoint;
            _state.PartyMoveRpc(move.PartyId, move.LeaderKey, (byte)move.PartySize, (byte)target.Kind, target.Address, target.Port,
                (byte)leader.Kind, leader.Address, leader.Port, move.Matchmade, (byte)move.Reason);
            _log.Info(LogTag, move.IsRegroup
                ? $"Party {move.PartyId:X} leaves to regroup at its leader."
                : $"Party {move.PartyId:X} moves to {target}.");

            if (move.PartyId == HostPartyId)
            {
                RaisePartyMove(move);
                RebuildPlan(move.PartyId);
                if (HasPeersOutside(move.PartyId))
                {
                    _state.HostLeavingRpc();
                }
            }

            return true;
        }

        /// <summary>Хост: отправить пати обратно к её лидеру (например, из-за пинга). Она уйдёт целиком.</summary>
        public bool SendPartyBack(ulong partyId, PartyMoveReason reason)
        {
            if (_state == null || !IsHost || partyId == HostPartyId)
            {
                return false;
            }

            SlotTable slots = _state.Slots;
            ulong leader = slots.LeaderOf(partyId);
            if (leader == 0)
            {
                return false;
            }

            return StartPartyDeparture(new PartyMove(partyId, SessionEndpoint.None, leader, slots.CountParty(partyId), default, false, reason));
        }

        /// <summary>Хост: в какой пати клиент и пришёл ли он по поиску.</summary>
        public bool TryGetPeerParty(ulong clientId, out ulong partyId, out bool matchmade)
        {
            if (_peerParties.TryGetValue(clientId, out PeerParty party))
            {
                partyId = party.PartyId;
                matchmade = party.Matchmade;
                return true;
            }

            partyId = 0;
            matchmade = false;
            return false;
        }

        /// <summary>Хост: состояние поиска для всей сессии.</summary>
        public void SetMatchmaking(MatchmakingInfo info)
        {
            if (_state != null && IsHost)
            {
                _state.SetMatchmaking(info);
                _lastMatchmaking = info;
            }
        }

        /// <summary>Хост: измерить RTT до клиента. Ответ придёт в <see cref="PongReceived"/>.</summary>
        public void SendPing(ulong clientId)
        {
            if (_state != null && IsHost && clientId != NetworkManager.ServerClientId)
            {
                _state.SendPing(clientId, Time.realtimeSinceStartup);
            }
        }

        /// <summary>Клиент-лидер: попросить хоста увести нашу пати (Target None — собраться у нас).</summary>
        public bool RequestPartyMove(SessionEndpoint target)
        {
            if (_state == null || IsHost)
            {
                return false;
            }

            _state.RequestPartyMoveRpc((byte)target.Kind, target.Address, target.Port);
            return true;
        }

        /// <summary>Сколько слотов занимает пати в текущем составе.</summary>
        public int CountPartySlots(ulong partyId)
        {
            return _state != null ? _state.Slots.CountParty(partyId) : 0;
        }

        /// <summary>
        /// Хост: переход в новое состояние. Вызывается до начала загрузки сцены у хоста,
        /// чтобы клиенты грузились параллельно.
        /// </summary>
        public void SetHostState(GameState state)
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            int epoch = _state.Phase.Epoch + 1;
            if (state == GameState.Lobby)
            {
                // Вернулись в лобби — готовность подтверждают заново.
                SlotTable slots = _state.Slots;
                slots.ResetReady();
                _state.SetSlots(slots);
            }

            CollectRemoteClients(_clientsBuffer);
            _loads.Begin(epoch, _clientsBuffer);
            _loadedEpoch.Clear();
            _state.SetPhase(new SessionPhase(state, epoch));
        }

        /// <summary>Хост: клиент уже загрузил текущее состояние (видимость объектов матча).</summary>
        public bool HasLoadedCurrentState(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                return true;
            }

            return _state != null && _loadedEpoch.TryGetValue(clientId, out int epoch) && epoch == _state.Phase.Epoch;
        }

        /// <summary>
        /// Хост: ждёт, пока все клиенты загрузят текущее состояние. Не успевших за таймаут отключает,
        /// чтобы один зависший клиент не держал остальных.
        /// </summary>
        public async UniTask WaitForClientsLoadedAsync(CancellationToken cancellationToken)
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            float deadline = Time.realtimeSinceStartup + _config.ClientLoadTimeout;
            while (!_loads.IsComplete && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.Yield(cancellationToken);
            }

            if (_loads.IsComplete)
            {
                return;
            }

            _clientsBuffer.Clear();
            _clientsBuffer.AddRange(_loads.Pending);
            for (int i = 0; i < _clientsBuffer.Count; i++)
            {
                ulong clientId = _clientsBuffer[i];
                _log.Warning(LogTag, $"Client {clientId} did not load in time and is disconnected.");
                _loads.Remove(clientId);
                _runtime.Manager.DisconnectClient(clientId, DisconnectReasons.LoadTimeout);
            }
        }

        /// <summary>Хост уходит сам: клиенты сразу пойдут к преемнику, не дожидаясь таймаута.</summary>
        public bool AnnounceLeaving()
        {
            if (_state == null || !IsHost || _peers.Count == 0)
            {
                return false;
            }

            _state.HostLeavingRpc();
            return true;
        }

        // ---------- Клиент ----------

        /// <summary>Клиент: текущее состояние загружено и готово к спавну. Хосту ничего делать не нужно.</summary>
        public void ReportLoaded()
        {
            if (_state == null || IsHost)
            {
                return;
            }

            _state.ReportLoadedRpc(_state.Phase.Epoch);
        }

        /// <summary>Клиент: хост объявил, что уходит. Флаг сбрасывается чтением.</summary>
        public bool ConsumeHostLeaving()
        {
            bool value = _hostAnnouncedLeaving;
            _hostAnnouncedLeaving = false;
            return value;
        }

        // ---------- Вызовы от SessionNetworkState ----------

        public void Attach(SessionNetworkState state)
        {
            if (_state != null && _state != state)
            {
                _log.Warning(LogTag, "Second session state spawned; the old one is replaced.");
            }

            _state = state;
            _hostAnnouncedLeaving = false;
            _localPlayers.PlayerJoined += OnLocalPlayerJoined;

            if (IsHost)
            {
                AttachAsHost();
            }
            else
            {
                AttachAsClient();
            }

            RebuildSlots();
        }

        public void Detach(SessionNetworkState state)
        {
            if (_state != state)
            {
                return;
            }

            _localPlayers.PlayerJoined -= OnLocalPlayerJoined;
            if (_subscribedToManager)
            {
                _subscribedToManager = false;
                _runtime.Manager.OnClientConnectedCallback -= OnClientConnected;
                _runtime.Manager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            _approved.Clear();
            _peers.Clear();
            _reservedSince.Clear();
            _loadedEpoch.Clear();
            _peerParties.Clear();
            _partyHolds.Clear();
            _departingParties.Clear();
            _hasPendingMove = false;
            _state = null;
            RebuildSlots();
        }

        public void OnSlotRequested(ulong clientId, int localIndex)
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            if (localIndex < 0 || localIndex >= SlotTable.Capacity)
            {
                _log.Warning(LogTag, $"Client {clientId} requested a slot for invalid local index {localIndex}.");
                return;
            }

            if (!TryGetPlayerKey(clientId, out ulong playerKey))
            {
                _log.Warning(LogTag, $"Slot request from unknown client {clientId}.");
                return;
            }

            ResolvePartyOf(clientId, out ulong partyId, out bool partyLeader);
            SlotTable slots = _state.Slots;
            if (!slots.TryAssign(playerKey, clientId, localIndex, partyId, partyLeader, out int slotIndex))
            {
                _log.Warning(LogTag, $"No free slot for client {clientId}, local player {localIndex}.");

                // Два клиента прошли допуск к последнему месту одновременно: второму места не досталось,
                // а клиент без единого слота в сессии не нужен. Дополнительного локального игрока просто не пускаем.
                if (!slots.HasClient(clientId) && clientId != NetworkManager.ServerClientId)
                {
                    _runtime.Manager.DisconnectClient(clientId, DisconnectReasons.LobbyFull);
                }

                return;
            }

            _state.SetSlots(slots);
            RebuildPlan();
            _log.Info(LogTag, $"P{slotIndex + 1} <- client {clientId}, local {localIndex}.");
        }

        public void OnReadyReported(ulong clientId, bool ready)
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            SlotTable slots = _state.Slots;
            slots.SetReady(clientId, ready);
            _state.SetSlots(slots);
        }

        public void OnLoadedReported(ulong clientId, int epoch)
        {
            if (_state == null || !IsHost || epoch != _state.Phase.Epoch)
            {
                return;
            }

            _loads.Report(clientId, epoch);
            _loadedEpoch[clientId] = epoch;
            RaiseClientLoaded(clientId);
        }

        public void OnSlotsChanged()
        {
            if (_state != null)
            {
                _lastSlots = _state.Slots;
                AdoptOwnParty(_lastSlots);
            }

            RebuildSlots();
            RaisePendingPartyMove();
        }

        public void OnPhaseChanged(SessionPhase phase)
        {
            if (!IsHost)
            {
                RaiseHostStateChanged(phase.State);
            }
        }

        public void OnPlanChanged(MigrationPlan plan)
        {
            _lastPlan = plan;
        }

        public void OnMatchmakingChanged(MatchmakingInfo info)
        {
            _lastMatchmaking = info;
            Action<MatchmakingInfo> handlers = MatchmakingChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<MatchmakingInfo>)handler).Invoke(info);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        public void OnPong(ulong clientId, float hostTime)
        {
            if (!IsHost)
            {
                return;
            }

            float rtt = Time.realtimeSinceStartup - hostTime;
            Action<ulong, float> handlers = PongReceived;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ulong, float>)handler).Invoke(clientId, rtt);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        public void OnHostLeaving()
        {
            _hostAnnouncedLeaving = true;
            _log.Info(LogTag, "Host announced leaving.");
        }

        /// <summary>Хост: клиент просит увести свою пати. Слушаемся только лидера и только в лобби.</summary>
        public void OnPartyMoveRequested(ulong clientId, SessionEndpoint target)
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            SlotTable slots = _state.Slots;
            ulong playerKey = slots.KeyOfClient(clientId);
            if (playerKey == 0 || !slots.TryGetParty(playerKey, out ulong partyId, out bool leader) || !leader)
            {
                _log.Warning(LogTag, $"Client {clientId} asked to move a party it does not lead.");
                return;
            }

            if (_state.Phase.State != GameState.Lobby)
            {
                _log.Warning(LogTag, $"Party {partyId:X} cannot move in the middle of {_state.Phase.State}.");
                return;
            }

            StartPartyDeparture(new PartyMove(partyId, target, playerKey, slots.CountParty(partyId)));
        }

        /// <summary>
        /// Клиент: хост разослал команду переезда пати. Если своего слота ещё нет, мы не знаем, в какой мы пати:
        /// RPC может обогнать обновление слотов (лидер скомандовал сразу после нашего входа). Тогда команда
        /// ждёт состава и выполняется, как только он придёт.
        /// </summary>
        public void OnPartyMove(PartyMove move)
        {
            if (IsHost)
            {
                return;
            }

            if (!_lastSlots.TryGetParty(_identity.Key, out _, out _))
            {
                _pendingMove = move;
                _hasPendingMove = true;
                return;
            }

            RaisePartyMove(move);
        }

        private void RaisePendingPartyMove()
        {
            if (!_hasPendingMove || !_lastSlots.TryGetParty(_identity.Key, out _, out _))
            {
                return;
            }

            _hasPendingMove = false;
            RaisePartyMove(_pendingMove);
        }

        public void Tick()
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            ExpirePartyHolds();
            if (_reservedSince.Count == 0)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            _keysBuffer.Clear();
            foreach (KeyValuePair<ulong, float> pair in _reservedSince)
            {
                if (now - pair.Value >= _config.ReservedSlotTimeout)
                {
                    _keysBuffer.Add(pair.Key);
                }
            }

            if (_keysBuffer.Count == 0)
            {
                return;
            }

            SlotTable slots = _state.Slots;
            for (int i = 0; i < _keysBuffer.Count; i++)
            {
                _reservedSince.Remove(_keysBuffer[i]);
                slots.RemovePlayer(_keysBuffer[i]);
                _log.Info(LogTag, $"Player {_keysBuffer[i]:X} did not come back, slot freed.");
            }

            _state.SetSlots(slots);
        }

        public void Dispose()
        {
            if (_state != null)
            {
                Detach(_state);
            }
        }

        // ---------- Внутреннее ----------

        private void AttachAsHost()
        {
            NetworkManager manager = _runtime.Manager;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            _subscribedToManager = true;

            GameState initialState = _hostSeed.InitialState != GameState.None ? _hostSeed.InitialState : _localFlow.Current;
            _state.SetPhase(new SessionPhase(initialState, 0));

            // После миграции поиск продолжается с тем же сроком; новая сессия начинает без поиска.
            MatchmakingInfo matchmaking = _hostSeed.HasSlots ? _hostSeed.Matchmaking : default;
            _state.SetMatchmaking(matchmaking);
            _lastMatchmaking = matchmaking;

            SlotTable slots = default;
            if (_hostSeed.HasSlots)
            {
                // Миграция: состав старой сессии. Все, кроме нас, — отключены и ждут переподключения.
                // Пати и лидерство идут из этого состава: свою пати хост берёт из своих слотов.
                slots = _hostSeed.Slots.ForNewHost(_identity.Key, NetworkManager.ServerClientId);
                AdoptOwnParty(slots);
                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < SlotTable.Capacity; i++)
                {
                    SlotEntry entry = slots[i];
                    if (entry.Occupied && !entry.Connected)
                    {
                        _reservedSince[entry.PlayerKey] = now;
                    }
                }
            }

            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            for (int i = 0; i < players.Count; i++)
            {
                slots.TryAssign(_identity.Key, NetworkManager.ServerClientId, players[i].Index, _party.PartyId, _party.IsLeader, out _);
            }

            _state.SetSlots(slots);
            _lastSlots = slots;
            RebuildPlan();
            _hostSeed = default;
        }

        private void AttachAsClient()
        {
            _lastSlots = _state.Slots;
            _lastPlan = _state.Plan;
            _lastMatchmaking = _state.Matchmaking;
            AdoptOwnParty(_lastSlots);

            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            for (int i = 0; i < players.Count; i++)
            {
                _state.RequestSlotRpc((byte)players[i].Index);
            }

            GameState hostState = _state.Phase.State;

            // Переподключились, не выходя из сцены хоста (например, посреди матча): она уже загружена,
            // сообщаем сразу — иначе хост не покажет нам объекты матча.
            if (_localFlow.Current == hostState && !_localFlow.IsTransitioning)
            {
                ReportLoaded();
            }

            // Опоздавший клиент узнаёт, где сейчас хост, из начального значения переменной.
            RaiseHostStateChanged(hostState);
        }

        private void OnLocalPlayerJoined(LocalPlayer player)
        {
            if (_state == null)
            {
                return;
            }

            if (IsHost)
            {
                OnSlotRequested(NetworkManager.ServerClientId, player.Index);
            }
            else
            {
                _state.RequestSlotRpc((byte)player.Index);
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (_state == null || clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            if (!_approved.TryGetValue(clientId, out ConnectionPayload payload))
            {
                _log.Warning(LogTag, $"Client {clientId} connected without approval data.");
                return;
            }

            _approved.Remove(clientId);
            SessionEndpoint successor = _transport.GetSuccessorEndpoint(clientId, payload.Successor);
            if (!successor.IsValid)
            {
                _log.Warning(LogTag, $"Client {clientId} is unreachable for the others and is left out of the migration plan.");
            }

            _peers[clientId] = new PeerInfo(clientId, payload.PlayerKey, successor);
            _peerParties[clientId] = ConnectionAdmission.IsForeignParty(payload, HostPartyId)
                ? new PeerParty(payload.Party.PartyId, payload.Party.IsLeader, payload.Party.IsMatchmade)
                : new PeerParty(HostPartyId, false, false);

            SlotTable slots = _state.Slots;
            if (_reservedSince.Remove(payload.PlayerKey) && slots.Reconnect(payload.PlayerKey, clientId) > 0)
            {
                _state.SetSlots(slots);
                _log.Info(LogTag, $"Player {payload.PlayerKey:X} is back as client {clientId}.");
            }

            RebuildPlan();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            // При закрытии своей сессии NGO отключает всех клиентов: это не обрыв, резервировать нечего.
            if (_state == null || clientId == NetworkManager.ServerClientId || _runtime.Manager.ShutdownInProgress)
            {
                return;
            }

            _approved.Remove(clientId);
            _peers.Remove(clientId);
            _peerParties.Remove(clientId);
            _loads.Remove(clientId);
            _loadedEpoch.Remove(clientId);

            SlotTable slots = _state.Slots;
            ulong leavingKey = slots.KeyOfClient(clientId);
            if (leavingKey != 0 && slots.TryGetParty(leavingKey, out ulong partyId, out _) && _departingParties.Contains(partyId))
            {
                // Ушёл вместе со своей пати: ждать его обратно незачем.
                slots.RemovePlayer(leavingKey);
                _state.SetSlots(slots);
                _log.Info(LogTag, $"Client {clientId} left with party {partyId:X}, slots freed.");
                RebuildPlan();
                return;
            }

            ulong playerKey = slots.Disconnect(clientId);
            if (playerKey != 0)
            {
                _reservedSince[playerKey] = Time.realtimeSinceStartup;
                _state.SetSlots(slots);
                _log.Info(LogTag, $"Client {clientId} dropped, slots reserved for {_config.ReservedSlotTimeout:0} s.");
            }

            RebuildPlan();
        }

        private bool TryGetPlayerKey(ulong clientId, out ulong playerKey)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                playerKey = _identity.Key;
                return true;
            }

            if (_peers.TryGetValue(clientId, out PeerInfo peer))
            {
                playerKey = peer.PlayerKey;
                return true;
            }

            playerKey = 0;
            return false;
        }

        /// <param name="excludedParty">Пати, которая уезжает: её участники не станут преемниками.</param>
        private void RebuildPlan(ulong excludedParty = 0)
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            _peersBuffer.Clear();
            foreach (KeyValuePair<ulong, PeerInfo> pair in _peers)
            {
                if (excludedParty != 0 && _peerParties.TryGetValue(pair.Key, out PeerParty party) && party.PartyId == excludedParty)
                {
                    continue;
                }

                _peersBuffer.Add(pair.Value);
            }

            MigrationPlan plan = MigrationPlan.Build(_sessionToken, _state.Slots, _peersBuffer);
            _state.SetPlan(plan);
            _lastPlan = plan;
        }

        /// <summary>Пати, в которую хост запишет слоты клиента: своя у пришедшей пати, иначе пати хоста.</summary>
        private void ResolvePartyOf(ulong clientId, out ulong partyId, out bool partyLeader)
        {
            if (clientId != NetworkManager.ServerClientId && _peerParties.TryGetValue(clientId, out PeerParty party))
            {
                partyId = party.PartyId;
                partyLeader = party.IsLeader;
                return;
            }

            partyId = HostPartyId;
            partyLeader = clientId == NetworkManager.ServerClientId && _party.IsLeader;
        }

        /// <summary>Хост: адрес преемника подключённого игрока (None для самого хоста и недостижимых).</summary>
        private SessionEndpoint FindSuccessorEndpoint(ulong playerKey)
        {
            foreach (KeyValuePair<ulong, PeerInfo> pair in _peers)
            {
                if (pair.Value.PlayerKey == playerKey)
                {
                    return pair.Value.SuccessorEndpoint;
                }
            }

            return SessionEndpoint.None;
        }

        private bool HasPeersOutside(ulong partyId)
        {
            foreach (KeyValuePair<ulong, PeerParty> pair in _peerParties)
            {
                if (pair.Value.PartyId != partyId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Пока мы в сессии, наша пати и лидерство — те, что записаны в наших слотах.</summary>
        private void AdoptOwnParty(SlotTable slots)
        {
            if (slots.TryGetParty(_identity.Key, out ulong partyId, out bool leader))
            {
                _party.Adopt(partyId, leader);
            }
        }

        private void ExpirePartyHolds()
        {
            if (_partyHolds.Count == 0)
            {
                return;
            }

            // Пати подключилась целиком или не дошла за время подключения: места больше не держим.
            float timeout = _config.ConnectTimeout * 2f;
            float now = Time.realtimeSinceStartup;
            SlotTable slots = _state.Slots;
            _keysBuffer.Clear();
            foreach (KeyValuePair<ulong, PartyHold> pair in _partyHolds)
            {
                if (slots.CountParty(pair.Key) >= pair.Value.Size || now - pair.Value.Since >= timeout)
                {
                    _keysBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _keysBuffer.Count; i++)
            {
                _partyHolds.Remove(_keysBuffer[i]);
            }
        }

        private void CollectRemoteClients(List<ulong> result)
        {
            result.Clear();
            IReadOnlyList<ulong> connected = _runtime.Manager.ConnectedClientsIds;
            for (int i = 0; i < connected.Count; i++)
            {
                if (connected[i] != NetworkManager.ServerClientId)
                {
                    result.Add(connected[i]);
                }
            }
        }

        private void RebuildSlots()
        {
            _slots.Clear();
            if (_state != null)
            {
                SlotTable table = _state.Slots;
                for (int i = 0; i < SlotTable.Capacity; i++)
                {
                    SlotEntry entry = table[i];
                    if (entry.Occupied)
                    {
                        _slots.Add(new PlayerSlot(i, entry.ClientId, entry.LocalIndex, entry.Ready, entry.Connected, entry.PartyId, entry.PartyLeader));
                    }
                }
            }

            RaiseChanged();
        }

        // События вызываются изнутри обработки сетевого сообщения NGO. Исключение подписчика (например,
        // UI уже уничтоженной сцены) прервало бы применение остальных переменных того же сообщения,
        // поэтому каждого подписчика вызываем отдельно и только логируем ошибку.
        private void RaiseChanged()
        {
            Action handlers = Changed;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler).Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void RaisePartyMove(PartyMove move)
        {
            Action<PartyMove> handlers = PartyMoveStarted;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<PartyMove>)handler).Invoke(move);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void RaiseHostStateChanged(GameState state)
        {
            Action<GameState> handlers = HostStateChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<GameState>)handler).Invoke(state);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void RaiseClientLoaded(ulong clientId)
        {
            Action<ulong> handlers = ClientLoadedCurrentState;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ulong>)handler).Invoke(clientId);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }

    /// <summary>Хост: в какой пати подключённый клиент (из его запроса на подключение).</summary>
    internal readonly struct PeerParty
    {
        public readonly ulong PartyId;
        public readonly bool IsLeader;

        /// <summary>Пришёл по поиску: хост проверит его пинг.</summary>
        public readonly bool Matchmade;

        public PeerParty(ulong partyId, bool isLeader, bool matchmade)
        {
            PartyId = partyId;
            IsLeader = isLeader;
            Matchmade = matchmade;
        }
    }

    /// <summary>Хост: сколько мест держится для подключающейся пати и с какого момента.</summary>
    internal readonly struct PartyHold
    {
        public readonly int Size;
        public readonly float Since;

        public PartyHold(int size, float since)
        {
            Size = size;
            Since = since;
        }
    }

    /// <summary>С чего начинает новый хост: метка сессии и, после миграции, состав прежней сессии.</summary>
    public readonly struct HostSeed
    {
        public readonly ulong SessionToken;
        public readonly bool HasSlots;
        public readonly SlotTable Slots;

        /// <summary>None — состояние берётся из текущей сцены хоста.</summary>
        public readonly GameState InitialState;

        /// <summary>Поиск игры прежней сессии: новый хост продолжает ждать игроков с тем же сроком.</summary>
        public readonly MatchmakingInfo Matchmaking;

        public HostSeed(ulong sessionToken, bool hasSlots, SlotTable slots, GameState initialState, MatchmakingInfo matchmaking = default)
        {
            SessionToken = sessionToken;
            HasSlots = hasSlots;
            Slots = slots;
            InitialState = initialState;
            Matchmaking = matchmaking;
        }

        public static HostSeed NewSession(ulong sessionToken)
        {
            return new HostSeed(sessionToken, false, default, GameState.None);
        }

        /// <summary>Миграция: прежний состав, все возвращаются в лобби.</summary>
        public static HostSeed Migrated(ulong sessionToken, SlotTable slots, MatchmakingInfo matchmaking = default)
        {
            return new HostSeed(sessionToken, true, slots, GameState.Lobby, matchmaking);
        }
    }
}
