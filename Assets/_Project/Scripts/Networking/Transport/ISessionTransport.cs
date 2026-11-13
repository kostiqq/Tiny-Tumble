using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Transport
{
    /// <summary>
    /// Настройка транспорта NGO под тип адреса. <see cref="Session.NetworkSession"/> не знает, IP это, Steam
    /// или Relay: он просит транспорт приготовиться к роли и дальше работает с NetworkManager.
    /// Steam API и детали Unity Transport остаются за этим интерфейсом.
    /// Методы без типа адреса относятся к транспорту, выбранному последним вызовом TryConfigure*.
    /// </summary>
    public interface ISessionTransport
    {
        /// <summary>Через что поднимать новый хост, если ничего не задано явно.</summary>
        EndpointKind PreferredHostKind { get; }

        /// <summary>Транспорт для этого типа адреса есть и готов (например, Steam запущен).</summary>
        bool Supports(EndpointKind kind);

        /// <summary>Готовит транспорт принимать подключения. hostPort важен только для IP.</summary>
        bool TryConfigureHost(EndpointKind kind, ushort hostPort);

        /// <summary>Готовит подключение к хосту. false — адрес не поддерживается или неполон.</summary>
        bool TryConfigureClient(in SessionEndpoint host);

        /// <summary>
        /// Сколько ждать подключения и через сколько тишины считать связь потерянной. Стандартные таймауты
        /// транспортов слишком долгие для возврата после обрыва и миграции. Вызывать после TryConfigure*.
        /// </summary>
        void ConfigureTimeouts(float connectTimeout, float disconnectTimeout);

        /// <summary>
        /// Клиент: как другие найдут нас, если мы станем преемником. Уходит хосту в запросе на подключение.
        /// </summary>
        SessionEndpoint GetLocalSuccessor(ushort hostPort);

        /// <summary>
        /// Хост: проверенный адрес, по которому остальные найдут клиента, если он станет преемником.
        /// announced — то, что клиент сообщил о себе сам. None — клиент недостижим.
        /// </summary>
        SessionEndpoint GetSuccessorEndpoint(ulong clientId, in SessionEndpoint announced);
    }
}
