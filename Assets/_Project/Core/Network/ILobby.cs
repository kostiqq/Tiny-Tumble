using System;
using System.Collections.Generic;

namespace TinyTumble.Core.Network
{
    /// <summary>
    /// Состав сессии: занятые слоты игроков и готовность. Модель для UI и матча,
    /// одинаково читается на хосте и на клиентах.
    /// </summary>
    public interface ILobby
    {
        /// <summary>Занятые слоты по возрастанию индекса.</summary>
        IReadOnlyList<PlayerSlot> Slots { get; }

        int Capacity { get; }

        /// <summary>Готовность этой машины. У хоста всегда true: он сам запускает матч.</summary>
        bool IsLocalReady { get; }

        /// <summary>Все подключённые клиенты нажали «Готов».</summary>
        bool AreAllReady { get; }

        /// <summary>Слоты или готовность изменились.</summary>
        event Action Changed;

        void SetLocalReady(bool ready);
    }

    /// <summary>
    /// Место игрока в сессии: сетевой клиент + индекс локального игрока на нём.
    /// Один клиент может занимать несколько слотов (локальный кооп по сети).
    /// </summary>
    public readonly struct PlayerSlot
    {
        /// <summary>Сетевой id хоста: у NGO сервер всегда 0.</summary>
        public const ulong HostClientId = 0;

        /// <summary>0..3: номер игрока P1..P4 и его цвет.</summary>
        public readonly int Index;

        public readonly ulong ClientId;

        /// <summary>Индекс <see cref="Input.LocalPlayer"/> на машине-владельце.</summary>
        public readonly int LocalIndex;

        public readonly bool IsReady;

        /// <summary>false — игрок отвалился, хост держит за ним слот, пока он переподключается.</summary>
        public readonly bool IsConnected;

        public bool IsHost => IsConnected && ClientId == HostClientId;

        public PlayerSlot(int index, ulong clientId, int localIndex, bool isReady, bool isConnected)
        {
            Index = index;
            ClientId = clientId;
            LocalIndex = localIndex;
            IsReady = isReady;
            IsConnected = isConnected;
        }
    }
}
