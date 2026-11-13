using System;
using Unity.Netcode;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Слоты игроков P1..P4 одним значением фиксированного размера. Синхронизируется одной NetworkVariable:
    /// изменение состава приходит клиентам целиком, без рассинхрона между отдельными полями.
    /// Слот принадлежит игроку (ключ <see cref="PlayerIdentity"/>), а сетевой id — его текущее подключение:
    /// после обрыва слот остаётся за игроком, а при переподключении получает новый id.
    /// Значимый тип: меняйте копию и присваивайте обратно.
    /// </summary>
    public struct SlotTable : INetworkSerializable, IEquatable<SlotTable>
    {
        /// <summary>До четырёх игроков (GDD, раздел 3).</summary>
        public const int Capacity = 4;

        /// <summary>Сетевой id отключённого игрока: настоящий id появится при переподключении.</summary>
        public const ulong NoClient = ulong.MaxValue;

        private SlotEntry _slot0;
        private SlotEntry _slot1;
        private SlotEntry _slot2;
        private SlotEntry _slot3;

        public SlotEntry this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return _slot0;
                    case 1:
                        return _slot1;
                    case 2:
                        return _slot2;
                    case 3:
                        return _slot3;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            private set
            {
                switch (index)
                {
                    case 0:
                        _slot0 = value;
                        break;
                    case 1:
                        _slot1 = value;
                        break;
                    case 2:
                        _slot2 = value;
                        break;
                    case 3:
                        _slot3 = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public int OccupiedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (this[i].Occupied)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Зарезервированные за отвалившимися игроками слоты свободными не считаются.</summary>
        public int FreeCount => Capacity - OccupiedCount;

        /// <summary>
        /// Выдаёт слот локальному игроку. Если у этого игрока (ключ + локальный индекс) слот уже есть,
        /// возвращает его и привязывает к текущему подключению.
        /// </summary>
        /// <returns>false, если свободных слотов нет.</returns>
        public bool TryAssign(ulong playerKey, ulong clientId, int localIndex, out int slotIndex)
        {
            slotIndex = Find(playerKey, localIndex);
            if (slotIndex >= 0)
            {
                SlotEntry existing = this[slotIndex];
                this[slotIndex] = new SlotEntry(playerKey, clientId, existing.LocalIndex, existing.Ready, connected: true);
                return true;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (!this[i].Occupied)
                {
                    this[i] = new SlotEntry(playerKey, clientId, (byte)localIndex, ready: false, connected: true);
                    slotIndex = i;
                    return true;
                }
            }

            return false;
        }

        public int Find(ulong playerKey, int localIndex)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.PlayerKey == playerKey && entry.LocalIndex == localIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool HasPlayer(ulong playerKey)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (this[i].Occupied && this[i].PlayerKey == playerKey)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasClient(ulong clientId)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Наименьший индекс слота игрока или -1. По нему упорядочены преемники хоста.</summary>
        public int FirstSlotOf(ulong playerKey)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (this[i].Occupied && this[i].PlayerKey == playerKey)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Игрок отвалился: слоты остаются за ним, но помечаются отключёнными.</summary>
        /// <returns>Ключ игрока или 0, если у подключения не было слотов.</returns>
        public ulong Disconnect(ulong clientId)
        {
            ulong playerKey = 0;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    playerKey = entry.PlayerKey;
                    this[i] = new SlotEntry(entry.PlayerKey, NoClient, entry.LocalIndex, ready: false, connected: false);
                }
            }

            return playerKey;
        }

        /// <summary>Игрок вернулся: его слоты привязываются к новому подключению.</summary>
        /// <returns>Сколько слотов вернулось.</returns>
        public int Reconnect(ulong playerKey, ulong clientId)
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.PlayerKey == playerKey)
                {
                    this[i] = new SlotEntry(playerKey, clientId, entry.LocalIndex, ready: false, connected: true);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Освобождает все слоты игрока (резерв истёк).</summary>
        public int RemovePlayer(ulong playerKey)
        {
            int removed = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (this[i].Occupied && this[i].PlayerKey == playerKey)
                {
                    this[i] = default;
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Таблица для нового хоста после миграции: его слоты — его (id 0), остальные игроки,
        /// включая ушедшего хоста, отключены и ждут переподключения. Готовность сбрасывается.
        /// </summary>
        public SlotTable ForNewHost(ulong newHostKey, ulong newHostClientId)
        {
            SlotTable result = this;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (!entry.Occupied)
                {
                    continue;
                }

                bool isNewHost = entry.PlayerKey == newHostKey;
                result[i] = new SlotEntry(entry.PlayerKey, isNewHost ? newHostClientId : NoClient, entry.LocalIndex, ready: false, connected: isNewHost);
            }

            return result;
        }

        /// <summary>Готовность задаётся на машину целиком: «Готов» нажимают за всех её локальных игроков.</summary>
        public void SetReady(ulong clientId, bool ready)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    this[i] = new SlotEntry(entry.PlayerKey, entry.ClientId, entry.LocalIndex, ready, connected: true);
                }
            }
        }

        public void ResetReady()
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied)
                {
                    this[i] = new SlotEntry(entry.PlayerKey, entry.ClientId, entry.LocalIndex, ready: false, entry.Connected);
                }
            }
        }

        public bool IsReady(ulong clientId)
        {
            bool any = false;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    if (!entry.Ready)
                    {
                        return false;
                    }

                    any = true;
                }
            }

            return any;
        }

        /// <summary>
        /// Все подключённые клиенты, кроме хоста, готовы. Хост готов всегда: он сам нажимает «Начать».
        /// Отключённые игроки старт не держат: в матч они не попадут, а слот за ними сохранится.
        /// </summary>
        public bool AreAllReady(ulong hostClientId)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId != hostClientId && !entry.Ready)
                {
                    return false;
                }
            }

            return true;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            _slot0.NetworkSerialize(serializer);
            _slot1.NetworkSerialize(serializer);
            _slot2.NetworkSerialize(serializer);
            _slot3.NetworkSerialize(serializer);
        }

        public bool Equals(SlotTable other)
        {
            return _slot0.Equals(other._slot0) && _slot1.Equals(other._slot1)
                && _slot2.Equals(other._slot2) && _slot3.Equals(other._slot3);
        }

        public override bool Equals(object obj)
        {
            return obj is SlotTable other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_slot0, _slot1, _slot2, _slot3);
        }
    }

    public struct SlotEntry : INetworkSerializable, IEquatable<SlotEntry>
    {
        private bool _occupied;
        private ulong _playerKey;
        private ulong _clientId;
        private byte _localIndex;
        private bool _ready;
        private bool _connected;

        public SlotEntry(ulong playerKey, ulong clientId, byte localIndex, bool ready, bool connected)
        {
            _occupied = true;
            _playerKey = playerKey;
            _clientId = clientId;
            _localIndex = localIndex;
            _ready = ready;
            _connected = connected;
        }

        public bool Occupied => _occupied;
        public ulong PlayerKey => _playerKey;
        public ulong ClientId => _clientId;
        public byte LocalIndex => _localIndex;
        public bool Ready => _ready;
        public bool Connected => _connected;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _occupied);
            serializer.SerializeValue(ref _playerKey);
            serializer.SerializeValue(ref _clientId);
            serializer.SerializeValue(ref _localIndex);
            serializer.SerializeValue(ref _ready);
            serializer.SerializeValue(ref _connected);
        }

        public bool Equals(SlotEntry other)
        {
            return _occupied == other._occupied && _playerKey == other._playerKey && _clientId == other._clientId
                && _localIndex == other._localIndex && _ready == other._ready && _connected == other._connected;
        }

        public override bool Equals(object obj)
        {
            return obj is SlotEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_occupied, _playerKey, _clientId, _localIndex, _ready, _connected);
        }
    }
}
