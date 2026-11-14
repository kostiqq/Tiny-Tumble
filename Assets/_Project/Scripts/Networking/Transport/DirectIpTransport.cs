using TinyTumble.Core.Network;
using TinyTumble.Networking.Session;
using UnityEngine;

namespace TinyTumble.Networking.Transport
{
    /// <summary>
    /// Unity Transport с прямым IP: локальная сеть, проброшенный порт и автотест на двух сборках.
    /// Через NAT в интернете преемник по IP может быть недостижим — для этого Steam (см. networking.md).
    /// </summary>
    public sealed class DirectIpTransport : ISessionTransport
    {
        private const string ListenAllAddress = "0.0.0.0";
        private const string LoopbackAddress = "127.0.0.1";
        private const int ConnectAttemptIntervalMs = 1000;

        private readonly NetworkRuntime _runtime;

        public DirectIpTransport(NetworkRuntime runtime)
        {
            _runtime = runtime;
        }

        public EndpointKind PreferredHostKind => EndpointKind.Ip;

        public bool Supports(EndpointKind kind)
        {
            return kind == EndpointKind.Ip;
        }

        public bool TryConfigureHost(EndpointKind kind, ushort hostPort)
        {
            if (kind != EndpointKind.Ip || hostPort == 0)
            {
                return false;
            }

            Activate();
            // Слушаем все интерфейсы: к хосту можно подключиться и из локальной сети.
            _runtime.Transport.SetConnectionData(LoopbackAddress, hostPort, ListenAllAddress);
            return true;
        }

        public bool TryConfigureClient(in SessionEndpoint host)
        {
            if (host.Kind != EndpointKind.Ip || host.Port == 0)
            {
                return false;
            }

            Activate();
            _runtime.Transport.SetConnectionData(SessionEndpoint.Ipv4ToText(host.Ipv4), host.Port);
            return true;
        }

        /// <summary>
        /// По умолчанию UTP замечает обрыв через 30 с и пытается подключиться до минуты. Попытки идут раз в
        /// секунду, пока не выйдет connectTimeout.
        /// </summary>
        public void ConfigureTimeouts(float connectTimeout, float disconnectTimeout)
        {
            var transport = _runtime.Transport;
            transport.DisconnectTimeoutMS = Mathf.RoundToInt(disconnectTimeout * 1000f);
            transport.ConnectTimeoutMS = ConnectAttemptIntervalMs;
            transport.MaxConnectAttempts = Mathf.Max(1, Mathf.CeilToInt(connectTimeout * 1000f / ConnectAttemptIntervalMs));
        }

        /// <summary>Свой внешний IP клиент не знает: сообщает только порт, адрес хост видит сам.</summary>
        public SessionEndpoint GetLocalSuccessor(ushort hostPort)
        {
            return SessionEndpoint.Ip(0, hostPort);
        }

        public SessionEndpoint GetSuccessorEndpoint(ulong clientId, in SessionEndpoint announced)
        {
            if (announced.Kind != EndpointKind.Ip || announced.Port == 0)
            {
                return SessionEndpoint.None;
            }

            // Адрес вида "1.2.3.4:5678", каким хост видит клиента. Строка создаётся раз на подключение.
            string endpoint = _runtime.Transport.GetEndpoint(clientId).ToString();
            int colon = endpoint.LastIndexOf(':');
            string host = colon > 0 ? endpoint.Substring(0, colon) : endpoint;
            return SessionEndpoint.TryParseIpv4(host, out uint ipv4) ? SessionEndpoint.Ip(ipv4, announced.Port) : SessionEndpoint.None;
        }

        private void Activate()
        {
            _runtime.Manager.NetworkConfig.NetworkTransport = _runtime.Transport;
        }
    }
}
