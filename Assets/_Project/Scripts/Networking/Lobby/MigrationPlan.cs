using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Кто станет хостом, если нынешний пропадёт. Хост пересчитывает план при каждом изменении состава
    /// и рассылает его всем, поэтому после обрыва клиентам не нужно ни о чём договариваться: каждый идёт
    /// по одному и тому же списку. Порядок — по номеру слота (P2 раньше P3), он стабилен и виден всем.
    /// </summary>
    public struct MigrationPlan : INetworkSerializable, IEquatable<MigrationPlan>
    {
        /// <summary>Хост в план не входит, поэтому кандидатов на одного меньше, чем слотов.</summary>
        public const int MaxCandidates = SlotTable.Capacity - 1;

        private ulong _sessionToken;
        private byte _count;
        private MigrationCandidate _candidate0;
        private MigrationCandidate _candidate1;
        private MigrationCandidate _candidate2;

        public MigrationPlan(ulong sessionToken)
        {
            _sessionToken = sessionToken;
            _count = 0;
            _candidate0 = default;
            _candidate1 = default;
            _candidate2 = default;
        }

        /// <summary>
        /// Метка «линии» сессии. Сохраняется при миграции: клиент не присоединится к постороннему хосту,
        /// случайно оказавшемуся по адресу преемника.
        /// </summary>
        public ulong SessionToken => _sessionToken;

        public int Count => _count;

        public MigrationCandidate this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                switch (index)
                {
                    case 0:
                        return _candidate0;
                    case 1:
                        return _candidate1;
                    default:
                        return _candidate2;
                }
            }
        }

        public bool TryAdd(MigrationCandidate candidate)
        {
            switch (_count)
            {
                case 0:
                    _candidate0 = candidate;
                    break;
                case 1:
                    _candidate1 = candidate;
                    break;
                case 2:
                    _candidate2 = candidate;
                    break;
                default:
                    return false;
            }

            _count++;
            return true;
        }

        public int IndexOf(ulong playerKey)
        {
            for (int i = 0; i < _count; i++)
            {
                if (this[i].PlayerKey == playerKey)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Строит план: подключённые удалённые клиенты по возрастанию их первого слота, клиенты без слота — в конце.
        /// </summary>
        public static MigrationPlan Build(ulong sessionToken, SlotTable slots, IReadOnlyList<PeerInfo> peers)
        {
            var ordered = new List<PeerInfo>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                ordered.Add(peers[i]);
            }

            ordered.Sort((a, b) => Rank(slots, a).CompareTo(Rank(slots, b)));

            var plan = new MigrationPlan(sessionToken);
            for (int i = 0; i < ordered.Count; i++)
            {
                PeerInfo peer = ordered[i];
                if (!plan.TryAdd(new MigrationCandidate(peer.PlayerKey, peer.Ipv4, peer.HostPort)))
                {
                    break;
                }
            }

            return plan;
        }

        private static int Rank(SlotTable slots, PeerInfo peer)
        {
            int slot = slots.FirstSlotOf(peer.PlayerKey);
            return slot >= 0 ? slot : SlotTable.Capacity;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _sessionToken);
            serializer.SerializeValue(ref _count);
            _candidate0.NetworkSerialize(serializer);
            _candidate1.NetworkSerialize(serializer);
            _candidate2.NetworkSerialize(serializer);
        }

        public bool Equals(MigrationPlan other)
        {
            return _sessionToken == other._sessionToken && _count == other._count && _candidate0.Equals(other._candidate0)
                && _candidate1.Equals(other._candidate1) && _candidate2.Equals(other._candidate2);
        }

        public override bool Equals(object obj)
        {
            return obj is MigrationPlan other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_sessionToken, _count, _candidate0, _candidate1, _candidate2);
        }
    }

    /// <summary>Преемник: чей это игрок и куда к нему подключаться, если он станет хостом.</summary>
    public struct MigrationCandidate : INetworkSerializable, IEquatable<MigrationCandidate>
    {
        private ulong _playerKey;
        private uint _ipv4;
        private ushort _port;

        public MigrationCandidate(ulong playerKey, uint ipv4, ushort port)
        {
            _playerKey = playerKey;
            _ipv4 = ipv4;
            _port = port;
        }

        public ulong PlayerKey => _playerKey;
        public ushort Port => _port;
        public string Address => Ipv4.ToText(_ipv4);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _playerKey);
            serializer.SerializeValue(ref _ipv4);
            serializer.SerializeValue(ref _port);
        }

        public bool Equals(MigrationCandidate other)
        {
            return _playerKey == other._playerKey && _ipv4 == other._ipv4 && _port == other._port;
        }

        public override bool Equals(object obj)
        {
            return obj is MigrationCandidate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_playerKey, _ipv4, _port);
        }
    }

    /// <summary>Что хост знает о подключённом клиенте.</summary>
    public readonly struct PeerInfo
    {
        public readonly ulong ClientId;
        public readonly ulong PlayerKey;

        /// <summary>Адрес клиента, каким его видит хост. Через NAT в интернете он может быть недоступен другим (см. networking.md).</summary>
        public readonly uint Ipv4;

        /// <summary>Порт, на котором клиент поднимет хост, если станет преемником.</summary>
        public readonly ushort HostPort;

        public PeerInfo(ulong clientId, ulong playerKey, uint ipv4, ushort hostPort)
        {
            ClientId = clientId;
            PlayerKey = playerKey;
            Ipv4 = ipv4;
            HostPort = hostPort;
        }
    }

    /// <summary>IPv4 в одном uint: так адрес влезает в сетевую структуру без строк.</summary>
    public static class Ipv4
    {
        public static bool TryParse(string text, out uint address)
        {
            address = 0;
            if (!IPAddress.TryParse(text, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] bytes = parsed.GetAddressBytes();
            address = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            return true;
        }

        public static string ToText(uint address)
        {
            return $"{(address >> 24) & 0xFF}.{(address >> 16) & 0xFF}.{(address >> 8) & 0xFF}.{address & 0xFF}";
        }
    }
}
