using System;
using TinyTumble.Networking.Session;

namespace TinyTumble.Networking.Party
{
    /// <summary>
    /// Пати этой машины. Пока идёт сессия, источник правды — слоты (<see cref="Lobby.SessionLobby"/> переписывает
    /// сюда id и лидерство из своих слотов); между сессиями (переезд, восстановление) значение живёт здесь
    /// и уходит хосту в запросе на подключение.
    /// </summary>
    public sealed class PartyState
    {
        public PartyState()
        {
            StartNew();
        }

        public ulong PartyId { get; private set; }

        public bool IsLeader { get; private set; }

        /// <summary>Своя пати из одного себя: новая игра из меню или подключение к другу.</summary>
        public void StartNew()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            ulong id = BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8);
            PartyId = id != 0 ? id : 1;
            IsLeader = true;
        }

        /// <summary>Пати и лидерство из своих слотов в сессии.</summary>
        public void Adopt(ulong partyId, bool isLeader)
        {
            if (partyId == 0)
            {
                return;
            }

            PartyId = partyId;
            IsLeader = isLeader;
        }

        /// <summary>С чем подключаться, возвращаясь в сессию или переезжая: своя пати и её размер.</summary>
        public PartyTicket Ticket(int partySize, bool matchmade = false)
        {
            return new PartyTicket(PartyId, partySize, IsLeader, matchmade);
        }
    }
}
