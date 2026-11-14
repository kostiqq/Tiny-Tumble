using System;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Logging;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Искусственные задержка, джиттер и потери для проверки сети (команды net.sim и net.drop, ключ -tt-netsim).
    /// Работает в редакторе и development-сборках: только там UnityTransport включает слой симулятора.
    /// Задержка добавляется на отправку каждой стороной, поэтому RTT ≈ 2 × задержка, если включить у обоих.
    /// </summary>
    public sealed class NetworkSimulatorControl
    {
        private const string LogTag = "Net";
        private const float FullLoss = 100f;

        private readonly NetworkRuntime _runtime;
        private readonly IGameLog _log;
        private NetworkSimulatorParameter _parameters;
        private bool _enabled;
        private bool _blackout;

        public NetworkSimulatorControl(NetworkRuntime runtime, IGameLog log)
        {
            _runtime = runtime;
            _log = log;
        }

        public bool IsEnabled => _enabled || _blackout;

        public bool IsBlackout => _blackout;

        public uint DelayMs => _parameters.SendDelayMS;

        public uint JitterMs => _parameters.SendJitterMS;

        public float LossPercent => _parameters.SendPacketLossPercent;

        /// <summary>Запоминает параметры; применит к текущей сессии сразу, к следующей — при старте.</summary>
        public void Set(uint delayMs, uint jitterMs, float lossPercent)
        {
            _parameters = new NetworkSimulatorParameter
            {
                SendDelayMS = delayMs,
                SendJitterMS = jitterMs,
                SendPacketLossPercent = lossPercent,
            };
            _enabled = delayMs > 0 || jitterMs > 0 || lossPercent > 0f;
            _log.Info(LogTag, $"Network simulator: delay {delayMs} ms, jitter {jitterMs} ms, loss {lossPercent}%.");
            ApplyToRunning();
        }

        /// <summary>
        /// Полный обрыв в обе стороны на заданное время, как при пропавшем Wi-Fi. Обе стороны заметят обрыв
        /// по таймауту, а после восстановления клиент сможет переподключиться.
        /// </summary>
        public async UniTask BlackoutAsync(float seconds)
        {
            _blackout = true;
            _log.Info(LogTag, $"Network blackout for {seconds:0.#} s.");
            ApplyToRunning();
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: true);
            }
            finally
            {
                _blackout = false;
                _log.Info(LogTag, "Network blackout over.");
                ApplyToRunning();
            }
        }

        /// <summary>Вызывается сессией сразу после старта транспорта.</summary>
        public void Apply(UnityTransport transport)
        {
            if (IsEnabled)
            {
                ApplyParameters(transport);
            }
        }

        private void ApplyToRunning()
        {
            if (_runtime.IsRunning)
            {
                ApplyParameters(_runtime.Transport);
            }
        }

        private void ApplyParameters(UnityTransport transport)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Симулятор — слой Unity Transport. Через Steam его нет: драйвер UTP даже не создан.
            if (_runtime.Manager.NetworkConfig.NetworkTransport != transport)
            {
                _log.Warning(LogTag, "Network simulator works only over IP (Unity Transport); the current session uses another transport.");
                return;
            }

            NetworkSimulatorParameter parameters = _parameters;
            if (_blackout)
            {
                parameters.SendPacketLossPercent = FullLoss;
                parameters.ReceivePacketLossPercent = FullLoss;
            }

            transport.GetNetworkDriver().ModifyNetworkSimulatorParameters(parameters);
#else
            _log.Warning(LogTag, "Network simulator is available only in the editor and development builds.");
#endif
        }
    }
}
