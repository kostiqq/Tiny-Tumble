using System;
using System.Collections.Generic;
using TinyTumble.Core.Network;
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
        /// Клиенты, до которых другим не достучаться, в план не входят: иначе такой клиент поднял бы свой хост,
        /// остальные пропустили бы его, и сессия раскололась бы надвое. Сам он при миграции только подключается.
        /// </summary>
        public static MigrationPlan Build(ulong sessionToken, SlotTable slots, IReadOnlyList<PeerInfo> peers)
        {
            var ordered = new List<PeerInfo>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                if (peers[i].SuccessorEndpoint.IsValid)
                {
                    ordered.Add(peers[i]);
                }
            }

            ordered.Sort((a, b) => Rank(slots, a).CompareTo(Rank(slots, b)));

            var plan = new MigrationPlan(sessionToken);
            for (int i = 0; i < ordered.Count; i++)
            {
                PeerInfo peer = ordered[i];
                if (!plan.TryAdd(new MigrationCandidate(peer.PlayerKey, peer.SuccessorEndpoint)))
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

        // SessionEndpoint лежит в Core без NGO, поэтому по сети идут его поля.
        private byte _kind;
        private ulong _address;
        private ushort _port;

        public MigrationCandidate(ulong playerKey, SessionEndpoint endpoint)
        {
            _playerKey = playerKey;
            _kind = (byte)endpoint.Kind;
            _address = endpoint.Address;
            _port = endpoint.Port;
        }

        public ulong PlayerKey => _playerKey;

        /// <summary>None, если хост не знал, как достучаться до этого клиента.</summary>
        public SessionEndpoint Endpoint => SessionEndpoint.FromRaw(_kind, _address, _port);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _playerKey);
            serializer.SerializeValue(ref _kind);
            serializer.SerializeValue(ref _address);
            serializer.SerializeValue(ref _port);
        }

        public bool Equals(MigrationCandidate other)
        {
            return _playerKey == other._playerKey && _kind == other._kind && _address == other._address && _port == other._port;
        }

        public override bool Equals(object obj)
        {
            return obj is MigrationCandidate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_playerKey, _kind, _address, _port);
        }
    }

    /// <summary>Что хост знает о подключённом клиенте.</summary>
    public readonly struct PeerInfo
    {
        public readonly ulong ClientId;
        public readonly ulong PlayerKey;

        /// <summary>
        /// Куда подключаться к клиенту, если он станет преемником. Для IP это адрес, каким его видит хост:
        /// через NAT в интернете он может быть недоступен другим (см. networking.md).
        /// </summary>
        public readonly SessionEndpoint SuccessorEndpoint;

        public PeerInfo(ulong clientId, ulong playerKey, SessionEndpoint successorEndpoint)
        {
            ClientId = clientId;
            PlayerKey = playerKey;
            SuccessorEndpoint = successorEndpoint;
        }
    }
}
