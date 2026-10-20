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
        [SerializeField, Min(1)] private int _protocolVersion = 1;

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

        public int ProtocolVersion => _protocolVersion;
        public ushort Port => _port;
        public float ConnectTimeout => _connectTimeout;
        public float ClientLoadTimeout => _clientLoadTimeout;
        public float DisconnectTimeout => _disconnectTimeout;
        public float ReconnectWindow => _reconnectWindow;
        public float ReservedSlotTimeout => _reservedSlotTimeout;
        public float MigrationConnectTimeout => _migrationConnectTimeout;
    }
}
