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
        private HostSeed _hostSeed;
        private ulong _sessionToken;

        private SessionNetworkState _state;
        private bool _subscribedToManager;

        // Последнее, что клиент знал о сессии. Переживает её конец: по этому восстанавливаются после обрыва.
        private SlotTable _lastSlots;
        private MigrationPlan _lastPlan;
        private bool _hostAnnouncedLeaving;

        public SessionLobby(
            NetworkRuntime runtime,
            ISessionTransport transport,
            NetworkObjectResolver resolver,
            GameFlow localFlow,
            ILocalPlayers localPlayers,
            PlayerIdentity identity,
            SessionConfig config,
            IGameLog log)
        {
            _runtime = runtime;
            _transport = transport;
            _resolver = resolver;
            _localFlow = localFlow;
            _localPlayers = localPlayers;
            _identity = identity;
            _config = config;
            _log = log;
        }

        public event Action Changed;

        /// <summary>Клиент: хост перешёл в новое состояние (или мы только что подключились и узнали текущее).</summary>
        public event Action<GameState> HostStateChanged;

        /// <summary>Хост: клиент загрузил текущее состояние. Матч показывает ему свои объекты.</summary>
        public event Action<ulong> ClientLoadedCurrentState;

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

        /// <summary>Клиент: последний известный состав (переживает конец сессии).</summary>
        public SlotTable LastKnownSlots => _lastSlots;

        /// <summary>Клиент: последний известный план миграции (переживает конец сессии).</summary>
        public MigrationPlan LastKnownPlan => _lastPlan;

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

        /// <summary>Хост: запомнить, кто подключается (вызывается из одобрения подключения).</summary>
        public void RegisterApproved(ulong clientId, in ConnectionPayload payload)
        {
            _approved[clientId] = payload;
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

            SlotTable slots = _state.Slots;
            if (!slots.TryAssign(playerKey, clientId, localIndex, out int slotIndex))
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
            }

            RebuildSlots();
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

        public void OnHostLeaving()
        {
            _hostAnnouncedLeaving = true;
            _log.Info(LogTag, "Host announced leaving.");
        }

        public void Tick()
        {
            if (_state == null || _reservedSince.Count == 0 || !IsHost)
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

            SlotTable slots = default;
            if (_hostSeed.HasSlots)
            {
                // Миграция: состав старой сессии. Все, кроме нас, — отключены и ждут переподключения.
                slots = _hostSeed.Slots.ForNewHost(_identity.Key, NetworkManager.ServerClientId);
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
                slots.TryAssign(_identity.Key, NetworkManager.ServerClientId, players[i].Index, out _);
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
            SessionEndpoint successor = _transport.GetSuccessorEndpoint(clientId, payload.HostPort);
            if (!successor.IsValid)
            {
                _log.Warning(LogTag, $"Client {clientId} is unreachable for the others and is left out of the migration plan.");
            }

            _peers[clientId] = new PeerInfo(clientId, payload.PlayerKey, successor);

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
            _loads.Remove(clientId);
            _loadedEpoch.Remove(clientId);

            SlotTable slots = _state.Slots;
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

        private void RebuildPlan()
        {
            if (_state == null || !IsHost)
            {
                return;
            }

            _peersBuffer.Clear();
            foreach (KeyValuePair<ulong, PeerInfo> pair in _peers)
            {
                _peersBuffer.Add(pair.Value);
            }

            MigrationPlan plan = MigrationPlan.Build(_sessionToken, _state.Slots, _peersBuffer);
            _state.SetPlan(plan);
            _lastPlan = plan;
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
                        _slots.Add(new PlayerSlot(i, entry.ClientId, entry.LocalIndex, entry.Ready, entry.Connected));
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

    /// <summary>С чего начинает новый хост: метка сессии и, после миграции, состав прежней сессии.</summary>
    public readonly struct HostSeed
    {
        public readonly ulong SessionToken;
        public readonly bool HasSlots;
        public readonly SlotTable Slots;

        /// <summary>None — состояние берётся из текущей сцены хоста.</summary>
        public readonly GameState InitialState;

        public HostSeed(ulong sessionToken, bool hasSlots, SlotTable slots, GameState initialState)
        {
            SessionToken = sessionToken;
            HasSlots = hasSlots;
            Slots = slots;
            InitialState = initialState;
        }

        public static HostSeed NewSession(ulong sessionToken)
        {
            return new HostSeed(sessionToken, false, default, GameState.None);
        }

        /// <summary>Миграция: прежний состав, все возвращаются в лобби.</summary>
        public static HostSeed Migrated(ulong sessionToken, SlotTable slots)
        {
            return new HostSeed(sessionToken, true, slots, GameState.Lobby);
        }
    }
}
