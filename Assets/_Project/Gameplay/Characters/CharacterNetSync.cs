using TinyTumble.Core.Configs;
using TinyTumble.Networking.Sync;
using Unity.Netcode;
using VContainer;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Состояние персонажа по сети. Хост раз в несколько тиков рассылает снимок (положение, поворот, скорость,
    /// состояние, номер применённого кадра ввода) по ненадёжному каналу: устаревший снимок не нужен, а свежий
    /// всё равно придёт следом. Клиенты копят снимки для интерполяции; владелец по ним сверяет предсказание.
    /// Вместо NetworkTransform, потому что у владельца тело должно двигаться само, а не за сервером.
    /// </summary>
    public sealed class CharacterNetSync : NetworkBehaviour
    {
        private const int DefaultInterval = 2;
        private const float DefaultDelay = 0.1f;

        private readonly SnapshotInterpolator _interpolator = new SnapshotInterpolator();
        private readonly PredictionHistory _history = new PredictionHistory();
        private PredictionCorrector _corrector = new PredictionCorrector(0.03f, 1.5f, 10f);
        private int _interval = DefaultInterval;
        private float _delay = DefaultDelay;
        private int _ticksSinceSend = int.MaxValue;
        private BodySnapshot _latest;
        private bool _hasLatest;
        private bool _hasUnreadLatest;

        /// <summary>Хост: растёт при телепорте (возрождение), чтобы клиенты не интерполировали через весь стол.</summary>
        public byte TeleportCounter { get; private set; }

        public bool HasState => _hasLatest;

        /// <summary>Самый свежий снимок от хоста.</summary>
        public BodySnapshot Latest => _latest;

        public PredictionHistory History => _history;

        public PredictionCorrector Corrector => _corrector;

        [Inject]
        public void Construct(SessionConfig config)
        {
            _interval = config.SnapshotIntervalTicks;
            _delay = config.InterpolationDelay;
            _corrector = new PredictionCorrector(config.PredictionTolerance, config.PredictionSnapDistance, config.PredictionCorrectionRate);
        }

        public override void OnNetworkSpawn()
        {
            _interpolator.Clear();
            _history.Clear();
            _corrector.Reset();
            _hasLatest = false;
            _hasUnreadLatest = false;
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

        /// <summary>Клиент: интерполированное состояние на момент «сейчас минус задержка».</summary>
        public bool TrySample(float deltaTime, out BodySnapshot snapshot)
        {
            return _interpolator.TrySample(deltaTime, _delay, out snapshot);
        }

        /// <summary>Владелец: новый снимок с прошлого вызова (для сверки предсказания).</summary>
        public bool TryTakeUnread(out BodySnapshot snapshot)
        {
            snapshot = _latest;
            if (!_hasUnreadLatest)
            {
                return false;
            }

            _hasUnreadLatest = false;
            return true;
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void StateRpc(BodySnapshot snapshot)
        {
            if (!_interpolator.Add(snapshot))
            {
                return;
            }

            _latest = snapshot;
            _hasLatest = true;
            _hasUnreadLatest = true;
        }
    }
}
