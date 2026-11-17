using TinyTumble.Core.Configs;
using TinyTumble.Networking.Sync;
using Unity.Netcode;
using VContainer;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Состояние персонажа по сети. Хост раз в несколько тиков рассылает снимок (таз, направление, повороты костей,
    /// состояние) по ненадёжному каналу: устаревший снимок не нужен, а свежий всё равно придёт следом.
    /// Клиенты копят снимки и показывают персонажа с задержкой; свой — с меньшей задержкой, чем чужие.
    /// Вместо NetworkTransform: нужен весь скелет, а не один трансформ.
    /// </summary>
    public sealed class CharacterNetSync : NetworkBehaviour
    {
        private const int DefaultInterval = 2;
        private const float DefaultDelay = 0.1f;

        private readonly SnapshotInterpolator _interpolator = new SnapshotInterpolator();
        private int _interval = DefaultInterval;
        private float _delay = DefaultDelay;
        private float _ownDelay = DefaultDelay;
        private int _ticksSinceSend = int.MaxValue;

        /// <summary>Хост: растёт при телепорте (возрождение), чтобы клиенты не интерполировали через весь стол.</summary>
        public byte TeleportCounter { get; private set; }

        [Inject]
        public void Construct(SessionConfig config)
        {
            _interval = config.SnapshotIntervalTicks;
            _delay = config.InterpolationDelay;
            _ownDelay = config.OwnInterpolationDelay;
        }

        public override void OnNetworkSpawn()
        {
            _interpolator.Clear();
            _ticksSinceSend = int.MaxValue;
        }

        /// <summary>Хост, раз за тик: отправит снимок, если подошла очередь.</summary>
        public void ServerTick(in BodySnapshot snapshot)
        {
            if (!IsServer)
            {
                return;
            }

            if (_ticksSinceSend < _interval - 1)
            {
                _ticksSinceSend++;
                return;
            }

            _ticksSinceSend = 0;
            StateRpc(snapshot);
        }

        public void MarkTeleported()
        {
            TeleportCounter++;
            // Новое положение должно уйти сразу, а не через интервал.
            _ticksSinceSend = int.MaxValue;
        }

        /// <summary>Клиент: пара снимков вокруг момента «сейчас минус задержка» и доля между ними.</summary>
        public bool TrySample(float deltaTime, out BodySnapshot from, out BodySnapshot to, out float t)
        {
            return _interpolator.TrySample(deltaTime, IsOwner ? _ownDelay : _delay, out from, out to, out t);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void StateRpc(BodySnapshot snapshot)
        {
            _interpolator.Add(snapshot);
        }
    }
}
