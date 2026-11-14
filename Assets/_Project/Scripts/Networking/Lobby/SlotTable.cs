using System;
using Unity.Netcode;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Слоты игроков P1..P4 одним значением фиксированного размера. Синхронизируется одной NetworkVariable:
    /// изменение состава приходит клиентам целиком, без рассинхрона между отдельными полями.
    /// Слот принадлежит игроку (ключ <see cref="PlayerIdentity"/>), а сетевой id — его текущее подключение:
    /// после обрыва слот остаётся за игроком, а при переподключении получает новый id.
    /// У слота есть пати и метка её лидера: так состав пати видят все и он переживает миграцию хоста.
    /// Лидер у пати один (помечены все слоты его машины); если лидер пропал, его место занимает
    /// следующий по слоту участник — правило одно у всех, договариваться не нужно.
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

        /// <summary>Выдаёт слот игроку вне пати (тесты слотов).</summary>
        public bool TryAssign(ulong playerKey, ulong clientId, int localIndex, out int slotIndex)
        {
            return TryAssign(playerKey, clientId, localIndex, 0, false, out slotIndex);
        }

        /// <summary>
        /// Выдаёт слот локальному игроку. Если у этого игрока (ключ + локальный индекс) слот уже есть,
        /// возвращает его и привязывает к текущему подключению. Если у машины игрока уже есть слоты,
        /// пати и лидерство берутся оттуда, а не из аргументов: у одной машины одна пати.
        /// </summary>
        /// <returns>false, если свободных слотов нет.</returns>
        public bool TryAssign(ulong playerKey, ulong clientId, int localIndex, ulong partyId, bool partyLeader, out int slotIndex)
        {
            slotIndex = Find(playerKey, localIndex);
            if (slotIndex >= 0)
            {
                SlotEntry existing = this[slotIndex];
                this[slotIndex] = existing.WithConnection(clientId, existing.Ready, connected: true);
                return true;
            }

            if (TryGetParty(playerKey, out ulong ownParty, out bool ownLeader))
            {
                partyId = ownParty;
                partyLeader = ownLeader;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (!this[i].Occupied)
                {
                    this[i] = new SlotEntry(playerKey, clientId, (byte)localIndex, false, true, partyId, partyLeader);
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

        /// <summary>Пати игрока по любому его слоту.</summary>
        public bool TryGetParty(ulong playerKey, out ulong partyId, out bool partyLeader)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.PlayerKey == playerKey)
                {
                    partyId = entry.PartyId;
                    partyLeader = entry.PartyLeader;
                    return true;
                }
            }

            partyId = 0;
            partyLeader = false;
            return false;
        }

        /// <summary>Сколько слотов занимает пати (вместе с отключёнными участниками).</summary>
        public int CountParty(ulong partyId)
        {
            int count = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (partyId != 0 && this[i].Occupied && this[i].PartyId == partyId)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Ключ лидера пати или 0.</summary>
        public ulong LeaderOf(ulong partyId)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (partyId != 0 && entry.Occupied && entry.PartyId == partyId && entry.PartyLeader)
                {
                    return entry.PlayerKey;
                }
            }

            return 0;
        }

        /// <summary>Ключ игрока на этом подключении (подключённого) или 0. Для id сервера — ключ хоста.</summary>
        public ulong KeyOfClient(ulong clientId)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    return entry.PlayerKey;
                }
            }

            return 0;
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

        /// <summary>
        /// Игрок отвалился: слоты остаются за ним, но помечаются отключёнными. Если он вёл пати,
        /// лидерство переходит к подключённому участнику: лидер нужен на месте, чтобы пати могла переехать.
        /// </summary>
        /// <returns>Ключ игрока или 0, если у подключения не было слотов.</returns>
        public ulong Disconnect(ulong clientId)
        {
            ulong playerKey = 0;
            ulong partyId = 0;
            bool wasLeader = false;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    playerKey = entry.PlayerKey;
                    partyId = entry.PartyId;
                    wasLeader |= entry.PartyLeader;
                    this[i] = entry.WithConnection(NoClient, ready: false, connected: false);
                }
            }

            if (wasLeader)
            {
                ElectLeader(partyId, playerKey, 0, connectedOnly: true);
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
                    this[i] = entry.WithConnection(clientId, ready: false, connected: true);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Освобождает все слоты игрока (резерв истёк или его пати ушла). Лидерство передаётся.</summary>
        public int RemovePlayer(ulong playerKey)
        {
            int removed = 0;
            ulong partyId = 0;
            bool wasLeader = false;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.PlayerKey == playerKey)
                {
                    partyId = entry.PartyId;
                    wasLeader |= entry.PartyLeader;
                    this[i] = default;
                    removed++;
                }
            }

            if (wasLeader)
            {
                ElectLeader(partyId, playerKey, 0, connectedOnly: false);
            }

            return removed;
        }

        /// <summary>
        /// Таблица для нового хоста после миграции: его слоты — его (id 0), остальные игроки,
        /// включая ушедшего хоста, отключены и ждут переподключения. Готовность сбрасывается.
        /// Если ушедший хост вёл пати, её ведёт новый хост (когда он в этой пати) или следующий по слоту участник.
        /// </summary>
        public SlotTable ForNewHost(ulong newHostKey, ulong newHostClientId)
        {
            // Хост в старой таблице — подключение с id сервера, а он у NGO всегда тот же (0).
            ulong oldHostKey = KeyOfClient(newHostClientId);
            bool oldHostLed = oldHostKey != 0 && oldHostKey != newHostKey
                && TryGetParty(oldHostKey, out _, out bool leader) && leader;
            TryGetParty(oldHostKey, out ulong oldHostParty, out _);

            SlotTable result = this;
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (!entry.Occupied)
                {
                    continue;
                }

                bool isNewHost = entry.PlayerKey == newHostKey;
                result[i] = entry.WithConnection(isNewHost ? newHostClientId : NoClient, ready: false, connected: isNewHost);
            }

            if (oldHostLed)
            {
                result.ElectLeader(oldHostParty, oldHostKey, newHostKey, connectedOnly: false);
            }

            return result;
        }

        /// <summary>
        /// Назначает пати лидера вместо excludedKey: preferredKey, если он в этой пати, иначе участника
        /// с наименьшим слотом (только подключённого, если connectedOnly). Некого — лидерство не меняется.
        /// </summary>
        private void ElectLeader(ulong partyId, ulong excludedKey, ulong preferredKey, bool connectedOnly)
        {
            if (partyId == 0)
            {
                return;
            }

            ulong next = 0;
            for (int i = 0; i < Capacity && next == 0; i++)
            {
                SlotEntry entry = this[i];
                if (preferredKey != 0 && entry.Occupied && entry.PartyId == partyId && entry.PlayerKey == preferredKey)
                {
                    next = preferredKey;
                }
            }

            for (int i = 0; i < Capacity && next == 0; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.PartyId == partyId && entry.PlayerKey != excludedKey && (entry.Connected || !connectedOnly))
                {
                    next = entry.PlayerKey;
                }
            }

            if (next == 0)
            {
                return;
            }

            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.PartyId == partyId)
                {
                    this[i] = entry.WithLeader(entry.PlayerKey == next);
                }
            }
        }

        /// <summary>Готовность задаётся на машину целиком: «Готов» нажимают за всех её локальных игроков.</summary>
        public void SetReady(ulong clientId, bool ready)
        {
            for (int i = 0; i < Capacity; i++)
            {
                SlotEntry entry = this[i];
                if (entry.Occupied && entry.Connected && entry.ClientId == clientId)
                {
                    this[i] = entry.WithConnection(entry.ClientId, ready, connected: true);
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
                    this[i] = entry.WithConnection(entry.ClientId, ready: false, entry.Connected);
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
        private ulong _partyId;
        private bool _partyLeader;

        public SlotEntry(ulong playerKey, ulong clientId, byte localIndex, bool ready, bool connected, ulong partyId = 0, bool partyLeader = false)
        {
            _occupied = true;
            _playerKey = playerKey;
            _clientId = clientId;
            _localIndex = localIndex;
            _ready = ready;
            _connected = connected;
            _partyId = partyId;
            _partyLeader = partyLeader;
        }

        public bool Occupied => _occupied;
        public ulong PlayerKey => _playerKey;
        public ulong ClientId => _clientId;
        public byte LocalIndex => _localIndex;
        public bool Ready => _ready;
        public bool Connected => _connected;
        public ulong PartyId => _partyId;
        public bool PartyLeader => _partyLeader;

        /// <summary>Та же запись (игрок, пати) с другим подключением и готовностью.</summary>
        public SlotEntry WithConnection(ulong clientId, bool ready, bool connected)
        {
            SlotEntry result = this;
            result._clientId = clientId;
            result._ready = ready;
            result._connected = connected;
            return result;
        }

        public SlotEntry WithLeader(bool partyLeader)
        {
            SlotEntry result = this;
            result._partyLeader = partyLeader;
            return result;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _occupied);
            serializer.SerializeValue(ref _playerKey);
            serializer.SerializeValue(ref _clientId);
            serializer.SerializeValue(ref _localIndex);
            serializer.SerializeValue(ref _ready);
            serializer.SerializeValue(ref _connected);
            serializer.SerializeValue(ref _partyId);
            serializer.SerializeValue(ref _partyLeader);
        }

        public bool Equals(SlotEntry other)
        {
            return _occupied == other._occupied && _playerKey == other._playerKey && _clientId == other._clientId
                && _localIndex == other._localIndex && _ready == other._ready && _connected == other._connected
                && _partyId == other._partyId && _partyLeader == other._partyLeader;
        }

        public override bool Equals(object obj)
        {
            return obj is SlotEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_occupied, _playerKey, _clientId, _localIndex, _ready, _connected, _partyId, _partyLeader);
        }
    }
}
