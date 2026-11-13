using System;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Party;
using Unity.Netcode;
using VContainer;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Сетевой объект сессии: живёт всю сессию поверх смены сцен и хранит то, что нужно опоздавшему клиенту,
    /// — состав слотов, состояние игры хоста, план миграции хоста и состояние поиска игры. Логики здесь нет:
    /// всё передаётся в <see cref="SessionLobby"/>.
    /// </summary>
    public sealed class SessionNetworkState : NetworkBehaviour
    {
        private readonly NetworkVariable<SlotTable> _slots = new NetworkVariable<SlotTable>();
        private readonly NetworkVariable<SessionPhase> _phase = new NetworkVariable<SessionPhase>();
        private readonly NetworkVariable<MigrationPlan> _plan = new NetworkVariable<MigrationPlan>();
        private readonly NetworkVariable<MatchmakingInfo> _matchmaking = new NetworkVariable<MatchmakingInfo>();

        private SessionLobby _lobby;

        public SlotTable Slots => _slots.Value;

        public SessionPhase Phase => _phase.Value;

        public MigrationPlan Plan => _plan.Value;

        public MatchmakingInfo Matchmaking => _matchmaking.Value;

        [Inject]
        public void Construct(SessionLobby lobby)
        {
            _lobby = lobby;
        }

        public override void OnNetworkSpawn()
        {
            // Сцены меняются обычным SceneManager (сценами NGO мы не пользуемся), поэтому объект сессии
            // уносим из сцены сами, иначе первый же переход уничтожит его у клиента.
            DontDestroyOnLoad(gameObject);

            if (_lobby == null)
            {
                throw new InvalidOperationException($"{nameof(SessionNetworkState)} was not injected. Is the network prefab handler registered?");
            }

            _slots.OnValueChanged += OnSlotsChanged;
            _phase.OnValueChanged += OnPhaseChanged;
            _plan.OnValueChanged += OnPlanChanged;
            _matchmaking.OnValueChanged += OnMatchmakingChanged;
            _lobby.Attach(this);
        }

        public override void OnNetworkDespawn()
        {
            _slots.OnValueChanged -= OnSlotsChanged;
            _phase.OnValueChanged -= OnPhaseChanged;
            _plan.OnValueChanged -= OnPlanChanged;
            _matchmaking.OnValueChanged -= OnMatchmakingChanged;
            if (_lobby != null)
            {
                _lobby.Detach(this);
            }
        }

        public void SetSlots(SlotTable slots)
        {
            _slots.Value = slots;
        }

        public void SetPhase(SessionPhase phase)
        {
            _phase.Value = phase;
        }

        public void SetPlan(MigrationPlan plan)
        {
            _plan.Value = plan;
        }

        public void SetMatchmaking(MatchmakingInfo info)
        {
            _matchmaking.Value = info;
        }

        /// <summary>Хост уходит сам: клиентам не нужно ждать его возвращения, сразу к преемнику.</summary>
        [Rpc(SendTo.NotServer)]
        public void HostLeavingRpc()
        {
            _lobby.OnHostLeaving();
        }

        /// <summary>Лидер пати просит хоста увести её. SessionEndpoint живёт в Core без NGO, поэтому идут его поля.</summary>
        [Rpc(SendTo.Server)]
        public void RequestPartyMoveRpc(byte kind, ulong address, ushort port, RpcParams rpcParams = default)
        {
            _lobby.OnPartyMoveRequested(rpcParams.Receive.SenderClientId, SessionEndpoint.FromRaw(kind, address, port));
        }

        /// <summary>Хост: пати переезжает. Получают все клиенты, выполняют участники этой пати.</summary>
        [Rpc(SendTo.NotServer)]
        public void PartyMoveRpc(ulong partyId, ulong leaderKey, byte partySize, byte kind, ulong address, ushort port,
            byte leaderKind, ulong leaderAddress, ushort leaderPort, bool matchmade, byte reason)
        {
            SessionEndpoint target = SessionEndpoint.FromRaw(kind, address, port);
            SessionEndpoint leader = SessionEndpoint.FromRaw(leaderKind, leaderAddress, leaderPort);
            _lobby.OnPartyMove(new PartyMove(partyId, target, leaderKey, partySize, leader, matchmade, (PartyMoveReason)reason));
        }

        /// <summary>Хост меряет RTT до клиента: отправляет своё время, клиент сразу возвращает его.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        public void PingRpc(float hostTime, RpcParams rpcParams)
        {
            PongRpc(hostTime);
        }

        [Rpc(SendTo.Server)]
        public void PongRpc(float hostTime, RpcParams rpcParams = default)
        {
            _lobby.OnPong(rpcParams.Receive.SenderClientId, hostTime);
        }

        /// <summary>Хост: отправить пинг одному клиенту.</summary>
        public void SendPing(ulong clientId, float hostTime)
        {
            PingRpc(hostTime, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>Клиент уходит сам (в меню), а не теряет связь.</summary>
        [Rpc(SendTo.Server)]
        public void ClientLeavingRpc(RpcParams rpcParams = default)
        {
            _lobby.OnClientLeaving(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server)]
        public void RequestSlotRpc(byte localIndex, RpcParams rpcParams = default)
        {
            _lobby.OnSlotRequested(rpcParams.Receive.SenderClientId, localIndex);
        }

        [Rpc(SendTo.Server)]
        public void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            _lobby.OnReadyReported(rpcParams.Receive.SenderClientId, ready);
        }

        [Rpc(SendTo.Server)]
        public void ReportLoadedRpc(int epoch, RpcParams rpcParams = default)
        {
            _lobby.OnLoadedReported(rpcParams.Receive.SenderClientId, epoch);
        }

        private void OnSlotsChanged(SlotTable previous, SlotTable current)
        {
            _lobby.OnSlotsChanged();
        }

        private void OnPhaseChanged(SessionPhase previous, SessionPhase current)
        {
            _lobby.OnPhaseChanged(current);
        }

        private void OnPlanChanged(MigrationPlan previous, MigrationPlan current)
        {
            _lobby.OnPlanChanged(current);
        }

        private void OnMatchmakingChanged(MatchmakingInfo previous, MatchmakingInfo current)
        {
            _lobby.OnMatchmakingChanged(current);
        }
    }

    /// <summary>
    /// Состояние игры хоста. Эпоха растёт с каждым переходом и отличает отчёты «загрузил» разных переходов.
    /// </summary>
    public struct SessionPhase : INetworkSerializable, IEquatable<SessionPhase>
    {
        private byte _state;
        private int _epoch;

        public SessionPhase(GameState state, int epoch)
        {
            _state = (byte)state;
            _epoch = epoch;
        }

        public GameState State => (GameState)_state;

        public int Epoch => _epoch;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _state);
            serializer.SerializeValue(ref _epoch);
        }

        public bool Equals(SessionPhase other)
        {
            return _state == other._state && _epoch == other._epoch;
        }

        public override bool Equals(object obj)
        {
            return obj is SessionPhase other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_state, _epoch);
        }
    }
}
