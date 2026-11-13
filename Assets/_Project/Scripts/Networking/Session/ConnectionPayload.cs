using System;
using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Данные запроса на подключение (little-endian):
    /// версия протокола (4) + число локальных игроков (1) + ключ игрока (8)
    /// + адрес преемника: тип (1), значение (8), порт (2) + метка сессии, к которой клиент возвращается, или 0 (8)
    /// + пати: id (8), сколько слотов занимает вся пати (1), флаги (1: бит 0 — лидер).
    /// </summary>
    public readonly struct ConnectionPayload
    {
        private const int Size = 42;
        private const byte LeaderFlag = 1;

        public readonly int ProtocolVersion;
        public readonly int LocalPlayerCount;
        public readonly ulong PlayerKey;

        /// <summary>
        /// Как клиент сам видит свой адрес на случай, если станет хостом при миграции: SteamID для Steam,
        /// для IP только порт (IP хост берёт из соединения). Хост проверяет его через транспорт.
        /// </summary>
        public readonly SessionEndpoint Successor;

        /// <summary>0 — новое подключение; иначе клиент восстанавливает именно эту сессию.</summary>
        public readonly ulong SessionToken;

        /// <summary>Пати, с которой клиент приходит. 0 — входит в пати хоста (друг по приглашению или адресу).</summary>
        public readonly PartyTicket Party;

        public ConnectionPayload(int protocolVersion, int localPlayerCount, ulong playerKey, SessionEndpoint successor, ulong sessionToken)
            : this(protocolVersion, localPlayerCount, playerKey, successor, sessionToken, PartyTicket.JoinHostParty)
        {
        }

        public ConnectionPayload(int protocolVersion, int localPlayerCount, ulong playerKey, SessionEndpoint successor, ulong sessionToken,
            PartyTicket party)
        {
            ProtocolVersion = protocolVersion;
            LocalPlayerCount = localPlayerCount;
            PlayerKey = playerKey;
            Successor = successor;
            SessionToken = sessionToken;
            Party = party;
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[Size];
            WriteInt(bytes, 0, ProtocolVersion);
            bytes[4] = (byte)LocalPlayerCount;
            WriteULong(bytes, 5, PlayerKey);
            bytes[13] = (byte)Successor.Kind;
            WriteULong(bytes, 14, Successor.Address);
            bytes[22] = (byte)Successor.Port;
            bytes[23] = (byte)(Successor.Port >> 8);
            WriteULong(bytes, 24, SessionToken);
            WriteULong(bytes, 32, Party.PartyId);
            bytes[40] = (byte)Math.Min(Party.Size, byte.MaxValue);
            bytes[41] = Party.IsLeader ? LeaderFlag : (byte)0;
            return bytes;
        }

        public static bool TryParse(byte[] bytes, out ConnectionPayload payload)
        {
            if (bytes == null || bytes.Length != Size)
            {
                payload = default;
                return false;
            }

            int version = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
            ushort port = (ushort)(bytes[22] | (bytes[23] << 8));
            SessionEndpoint successor = SessionEndpoint.FromRaw(bytes[13], ReadULong(bytes, 14), port);
            var party = new PartyTicket(ReadULong(bytes, 32), bytes[40], (bytes[41] & LeaderFlag) != 0);
            payload = new ConnectionPayload(version, bytes[4], ReadULong(bytes, 5), successor, ReadULong(bytes, 24), party);
            return true;
        }

        private static ulong ReadULong(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++)
            {
                value |= (ulong)bytes[offset + i] << (8 * i);
            }

            return value;
        }

        private static void WriteInt(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteULong(byte[] bytes, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                bytes[offset + i] = (byte)(value >> (8 * i));
            }
        }
    }

    /// <summary>С какой пати клиент подключается.</summary>
    public readonly struct PartyTicket
    {
        /// <summary>0 — клиент входит в пати хоста.</summary>
        public readonly ulong PartyId;

        /// <summary>Сколько слотов займёт вся пати. Хост держит места под неё целиком, пока подключаются остальные.</summary>
        public readonly int Size;

        public readonly bool IsLeader;

        public PartyTicket(ulong partyId, int size, bool isLeader)
        {
            PartyId = partyId;
            Size = size;
            IsLeader = isLeader;
        }

        /// <summary>Друг подключается по приглашению или адресу и становится участником пати хоста.</summary>
        public static PartyTicket JoinHostParty => default;
    }

    /// <summary>
    /// Правила допуска клиента в сессию. Отдельно от NGO, чтобы проверять EditMode-тестами.
    /// </summary>
    public static class ConnectionAdmission
    {
        /// <summary>Клиент приходит со своей пати, а не вступает в пати хоста.</summary>
        public static bool IsForeignParty(in ConnectionPayload payload, ulong hostPartyId)
        {
            return payload.Party.PartyId != 0 && payload.Party.PartyId != hostPartyId;
        }

        /// <summary>Допуск без учёта пати (клиент вступает в пати хоста).</summary>
        public static string Evaluate(in ConnectionPayload payload, bool payloadValid, int protocolVersion, ulong sessionToken,
            bool hasReservation, int freeSlots, bool acceptingPlayers)
        {
            return Evaluate(payload, payloadValid, protocolVersion, sessionToken, hasReservation, freeSlots, acceptingPlayers, 0, 0, 0);
        }

        /// <param name="sessionToken">Метка этой сессии.</param>
        /// <param name="hasReservation">За этим игроком держится слот (он отвалился и вернулся).</param>
        /// <param name="freeSlots">Незанятые слоты (места, придержанные для пати, в них входят).</param>
        /// <param name="hostPartyId">Пати хоста: клиенты с этим id или с 0 вступают в неё.</param>
        /// <param name="heldForThisParty">Сколько мест хост уже держит для пати клиента (пришёл не первым).</param>
        /// <param name="heldForOtherParties">Сколько мест хост держит для других подключающихся пати.</param>
        /// <returns>Пустая строка, если допущен, иначе код из <see cref="DisconnectReasons"/>.</returns>
        public static string Evaluate(in ConnectionPayload payload, bool payloadValid, int protocolVersion, ulong sessionToken,
            bool hasReservation, int freeSlots, bool acceptingPlayers, ulong hostPartyId, int heldForThisParty, int heldForOtherParties)
        {
            if (!payloadValid || payload.ProtocolVersion != protocolVersion)
            {
                return DisconnectReasons.VersionMismatch;
            }

            // Клиент возвращается в конкретную сессию, а здесь другая (преемник уже ушёл и тут новая игра).
            if (payload.SessionToken != 0 && payload.SessionToken != sessionToken)
            {
                return DisconnectReasons.SessionMismatch;
            }

            // Своего слота ждут и посреди матча: вернувшийся досмотрит матч и сыграет следующий.
            if (hasReservation)
            {
                return DisconnectReasons.None;
            }

            if (!acceptingPlayers)
            {
                return DisconnectReasons.MatchInProgress;
            }

            // Клиент без локальных игроков всё равно займёт слот: при входе он присоединяет клавиатуру.
            int requested = payload.LocalPlayerCount > 0 ? payload.LocalPlayerCount : 1;
            int available = freeSlots - heldForOtherParties;

            // Первый пришедший из чужой пати решает за всю пати: места нужны всем сразу, иначе часть пати
            // войдёт, а остальным не хватит. Дальше хост держит эти места, пока подключаются остальные.
            bool firstOfParty = IsForeignParty(payload, hostPartyId) && heldForThisParty <= 0;
            int needed = firstOfParty ? Math.Max(requested, payload.Party.Size) : requested;
            if (needed > available)
            {
                return firstOfParty && payload.Party.Size > 1 ? DisconnectReasons.PartyTooBig : DisconnectReasons.LobbyFull;
            }

            return DisconnectReasons.None;
        }
    }
}
