using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Transport
{
    /// <summary>
    /// Настройка транспорта NGO под тип адреса. <see cref="Session.NetworkSession"/> не знает, IP это, Steam
    /// или Relay: он просит транспорт приготовиться к роли и дальше работает с NetworkManager.
    /// Steam API и детали Unity Transport остаются за этим интерфейсом.
    /// </summary>
    public interface ISessionTransport
    {
        /// <summary>Тип адреса, по которому к нашему хосту подключаются другие (и по которому мы станем преемником).</summary>
        EndpointKind HostKind { get; }

        bool Supports(EndpointKind kind);

        /// <summary>Готовит транспорт принимать подключения. hostPort важен только для IP.</summary>
        void ConfigureHost(ushort hostPort);

        /// <summary>Готовит подключение к хосту. false — адрес этим транспортом не поддерживается или неполон.</summary>
        bool TryConfigureClient(in SessionEndpoint host);

        /// <summary>
        /// Сколько ждать подключения и через сколько тишины считать связь потерянной. Стандартные таймауты
        /// транспортов слишком долгие для возврата после обрыва и миграции.
        /// </summary>
        void ConfigureTimeouts(float connectTimeout, float disconnectTimeout);

        /// <summary>
        /// Хост: по какому адресу остальные найдут этого клиента, если он станет преемником.
        /// announcedHostPort клиент прислал в запросе на подключение. None — недостижим.
        /// </summary>
        SessionEndpoint GetSuccessorEndpoint(ulong clientId, ushort announcedHostPort);
    }
}
