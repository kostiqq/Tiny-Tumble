using System;
using System.Net;
using System.Net.Sockets;

namespace TinyTumble.Core.Network
{
    /// <summary>Как адресуется хост: от этого зависит, какой транспорт к нему подключится.</summary>
    public enum EndpointKind : byte
    {
        None = 0,

        /// <summary>IPv4 и порт, Unity Transport напрямую.</summary>
        Ip = 1,

        /// <summary>SteamID хоста, транспорт на SteamNetworkingSockets (ещё не подключён).</summary>
        Steam = 2,
    }

    /// <summary>
    /// Адрес хоста без строк: тип, 64-битное значение и порт. Так он без аллокаций ложится в сетевые
    /// структуры (план миграции) и одинаково описывает IP, SteamID, а позже и код Relay.
    /// У <see cref="EndpointKind.Ip"/> порт 0 значит «порт сессии по умолчанию» (см. <see cref="WithDefaultPort"/>).
    /// </summary>
    public readonly struct SessionEndpoint : IEquatable<SessionEndpoint>
    {
        public const uint LoopbackIpv4 = 0x7F000001;

        public readonly EndpointKind Kind;

        /// <summary>Ip — IPv4 в младших 32 битах (первый октет старший); Steam — SteamID.</summary>
        public readonly ulong Address;

        public readonly ushort Port;

        private SessionEndpoint(EndpointKind kind, ulong address, ushort port)
        {
            Kind = kind;
            Address = address;
            Port = port;
        }

        public static SessionEndpoint None => default;

        public bool IsValid => Kind != EndpointKind.None;

        public uint Ipv4 => (uint)Address;

        public static SessionEndpoint Ip(uint ipv4, ushort port)
        {
            return new SessionEndpoint(EndpointKind.Ip, ipv4, port);
        }

        public static SessionEndpoint Steam(ulong steamId)
        {
            return steamId != 0 ? new SessionEndpoint(EndpointKind.Steam, steamId, 0) : default;
        }

        /// <summary>Сборка из полей, прочитанных из сети. Неизвестный тип даёт пустой адрес.</summary>
        public static SessionEndpoint FromRaw(byte kind, ulong address, ushort port)
        {
            switch ((EndpointKind)kind)
            {
                case EndpointKind.Ip:
                    return Ip((uint)address, port);
                case EndpointKind.Steam:
                    return Steam(address);
                default:
                    return default;
            }
        }

        /// <summary>IP-адрес без порта получает порт по умолчанию; остальные адреса не меняются.</summary>
        public SessionEndpoint WithDefaultPort(ushort defaultPort)
        {
            return Kind == EndpointKind.Ip && Port == 0 ? Ip(Ipv4, defaultPort) : this;
        }

        /// <summary>
        /// Адрес, введённый человеком: "1.2.3.4", "1.2.3.4:7778", "localhost". Без порта — порт 0 (по умолчанию).
        /// Имена хостов не резолвим: UTP принимает только IP.
        /// </summary>
        public static bool TryParse(string text, out SessionEndpoint endpoint)
        {
            endpoint = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            string host = text;
            ushort port = 0;
            int colon = text.LastIndexOf(':');
            if (colon >= 0)
            {
                host = text.Substring(0, colon);
                if (!ushort.TryParse(text.Substring(colon + 1), out port) || port == 0)
                {
                    return false;
                }
            }

            uint ipv4;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                ipv4 = LoopbackIpv4;
            }
            else if (!TryParseIpv4(host, out ipv4))
            {
                return false;
            }

            endpoint = Ip(ipv4, port);
            return true;
        }

        /// <summary>Только IPv4 в точечной записи; IPv6 и имена отклоняются.</summary>
        public static bool TryParseIpv4(string text, out uint ipv4)
        {
            ipv4 = 0;
            if (!IPAddress.TryParse(text, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] bytes = parsed.GetAddressBytes();
            ipv4 = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            return true;
        }

        /// <summary>IPv4 в точечной записи. Для логов и настройки транспорта, не для кода в каждом кадре.</summary>
        public static string Ipv4ToText(uint ipv4)
        {
            return ((ipv4 >> 24) & 0xFF).ToString() + "." + ((ipv4 >> 16) & 0xFF).ToString() + "."
                + ((ipv4 >> 8) & 0xFF).ToString() + "." + (ipv4 & 0xFF).ToString();
        }

        public bool Equals(SessionEndpoint other)
        {
            return Kind == other.Kind && Address == other.Address && Port == other.Port;
        }

        public override bool Equals(object obj)
        {
            return obj is SessionEndpoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((byte)Kind, Address, Port);
        }

        public static bool operator ==(SessionEndpoint left, SessionEndpoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SessionEndpoint left, SessionEndpoint right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case EndpointKind.Ip:
                    return Port != 0 ? Ipv4ToText(Ipv4) + ":" + Port.ToString() : Ipv4ToText(Ipv4);
                case EndpointKind.Steam:
                    return "steam:" + Address.ToString();
                default:
                    return "none";
            }
        }
    }
}
