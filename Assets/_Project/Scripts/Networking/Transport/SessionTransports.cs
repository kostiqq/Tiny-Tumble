using TinyTumble.Core.Network;
using TinyTumble.Networking.Steam;

namespace TinyTumble.Networking.Transport
{
    /// <summary>Через что поднимать новый хост.</summary>
    public enum HostTransportPreference
    {
        /// <summary>Steam, если он запущен, иначе IP.</summary>
        Auto = 0,
        Ip = 1,
        Steam = 2,
    }

    /// <summary>
    /// Выбор транспорта по типу адреса: клиент идёт туда, куда указывает адрес хоста, хост поднимается по
    /// <see cref="HostPreference"/>. Остальные вызовы уходят транспорту, выбранному последней настройкой.
    /// </summary>
    public sealed class SessionTransports : ISessionTransport
    {
        private readonly DirectIpTransport _ip;
        private readonly SteamSessionTransport _steam;
        private ISessionTransport _active;

        public SessionTransports(DirectIpTransport ip, SteamSessionTransport steam)
        {
            _ip = ip;
            _steam = steam;
            _active = ip;
        }

        /// <summary>
        /// Автотест на одной машине ставит Ip: две копии игры под одним аккаунтом Steam друг к другу через Steam
        /// не подключатся. Консоль: net.host ip | steam.
        /// </summary>
        public HostTransportPreference HostPreference { get; set; } = HostTransportPreference.Auto;

        public EndpointKind PreferredHostKind
        {
            get
            {
                switch (HostPreference)
                {
                    case HostTransportPreference.Ip:
                        return EndpointKind.Ip;
                    case HostTransportPreference.Steam:
                        return EndpointKind.Steam;
                    default:
                        return _steam.Supports(EndpointKind.Steam) ? EndpointKind.Steam : EndpointKind.Ip;
                }
            }
        }

        public bool Supports(EndpointKind kind)
        {
            ISessionTransport transport = For(kind);
            return transport != null && transport.Supports(kind);
        }

        public bool TryConfigureHost(EndpointKind kind, ushort hostPort)
        {
            ISessionTransport transport = For(kind);
            if (transport == null || !transport.TryConfigureHost(kind, hostPort))
            {
                return false;
            }

            _active = transport;
            return true;
        }

        public bool TryConfigureClient(in SessionEndpoint host)
        {
            ISessionTransport transport = For(host.Kind);
            if (transport == null || !transport.TryConfigureClient(host))
            {
                return false;
            }

            _active = transport;
            return true;
        }

        public void ConfigureTimeouts(float connectTimeout, float disconnectTimeout)
        {
            _active.ConfigureTimeouts(connectTimeout, disconnectTimeout);
        }

        public SessionEndpoint GetLocalSuccessor(ushort hostPort)
        {
            return _active.GetLocalSuccessor(hostPort);
        }

        public SessionEndpoint GetSuccessorEndpoint(ulong clientId, in SessionEndpoint announced)
        {
            return _active.GetSuccessorEndpoint(clientId, announced);
        }

        private ISessionTransport For(EndpointKind kind)
        {
            switch (kind)
            {
                case EndpointKind.Ip:
                    return _ip;
                case EndpointKind.Steam:
                    return _steam;
                default:
                    return null;
            }
        }
    }
}
