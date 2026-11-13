using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Параметры сети. Порт и таймауты; транспорт (прямой IP, позже Relay и Steam) выбирается кодом.
    /// </summary>
    [CreateAssetMenu(fileName = "SessionConfig", menuName = "Tiny Tumble/Configs/Session")]
    public sealed class SessionConfig : ScriptableObject
    {
        [Tooltip("Увеличивайте при несовместимых изменениях сетевого протокола: клиент другой версии не войдёт.")]
        [SerializeField, Min(1)] private int _protocolVersion = 4;

        [SerializeField] private ushort _port = 7777;

        [Tooltip("Сколько ждать ответа хоста при подключении.")]
        [SerializeField, Min(1f)] private float _connectTimeout = 10f;

        [Tooltip("Сколько хост ждёт, пока клиенты загрузят арену. Опоздавших отключает.")]
        [SerializeField, Min(1f)] private float _clientLoadTimeout = 30f;

        [Header("Обрывы связи")]
        [Tooltip("Через сколько тишины соединение считается оборванным (у хоста и у клиента).")]
        [SerializeField, Min(1f)] private float _disconnectTimeout = 5f;

        [Tooltip("Сколько клиент пытается вернуться к прежнему хосту, прежде чем считать его ушедшим.")]
        [SerializeField, Min(1f)] private float _reconnectWindow = 10f;

        [Tooltip("Сколько хост держит слот и цвет отвалившегося игрока.")]
        [SerializeField, Min(1f)] private float _reservedSlotTimeout = 60f;

        [Tooltip("Сколько клиент ждёт очередного преемника при миграции хоста, прежде чем пробовать следующего.")]
        [SerializeField, Min(1f)] private float _migrationConnectTimeout = 6f;

        [Header("Синхронизация движения")]
        [Tooltip("Хост рассылает состояние персонажей раз в столько тиков физики (тик = 0.02 с).")]
        [SerializeField, Range(1, 10)] private int _snapshotIntervalTicks = 2;

        [Tooltip("На сколько секунд чужие персонажи показываются в прошлом: запас на джиттер, чтобы было что интерполировать.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _interpolationDelay = 0.1f;

        [Tooltip("Расхождение предсказания с хостом меньше этого не исправляется, м.")]
        [SerializeField, Min(0f)] private float _predictionTolerance = 0.03f;

        [Tooltip("Расхождение больше этого исправляется сразу, а не плавно, м.")]
        [SerializeField, Min(0.01f)] private float _predictionSnapDistance = 1.5f;

        [Tooltip("Доля расхождения, убираемая за секунду при плавной коррекции.")]
        [SerializeField, Min(0.1f)] private float _predictionCorrectionRate = 10f;

        [Tooltip("Хост держит у себя не больше стольких кадров ввода клиента; лишние склеивает, догоняя клиента.")]
        [SerializeField, Range(1, 16)] private int _maxBufferedInputs = 4;

        public int ProtocolVersion => _protocolVersion;
        public ushort Port => _port;
        public float ConnectTimeout => _connectTimeout;
        public float ClientLoadTimeout => _clientLoadTimeout;
        public float DisconnectTimeout => _disconnectTimeout;
        public float ReconnectWindow => _reconnectWindow;
        public float ReservedSlotTimeout => _reservedSlotTimeout;
        public float MigrationConnectTimeout => _migrationConnectTimeout;
        public int SnapshotIntervalTicks => _snapshotIntervalTicks;
        public float InterpolationDelay => _interpolationDelay;
        public float PredictionTolerance => _predictionTolerance;
        public float PredictionSnapDistance => _predictionSnapDistance;
        public float PredictionCorrectionRate => _predictionCorrectionRate;
        public int MaxBufferedInputs => _maxBufferedInputs;
    }
}
