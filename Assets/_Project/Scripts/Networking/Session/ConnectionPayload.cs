using System;
using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Данные запроса на подключение (little-endian):
    /// версия протокола (4) + число локальных игроков (1) + ключ игрока (8)
    /// + адрес преемника: тип (1), значение (8), порт (2) + метка сессии, к которой клиент возвращается, или 0 (8).
    /// </summary>
    public readonly struct ConnectionPayload
    {
        private const int Size = 32;

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

        public ConnectionPayload(int protocolVersion, int localPlayerCount, ulong playerKey, SessionEndpoint successor, ulong sessionToken)
        {
            ProtocolVersion = protocolVersion;
            LocalPlayerCount = localPlayerCount;
            PlayerKey = playerKey;
            Successor = successor;
            SessionToken = sessionToken;
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
            payload = new ConnectionPayload(version, bytes[4], ReadULong(bytes, 5), successor, ReadULong(bytes, 24));
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

    /// <summary>
    /// Правила допуска клиента в сессию. Отдельно от NGO, чтобы проверять EditMode-тестами.
    /// </summary>
    public static class ConnectionAdmission
    {
        /// <param name="sessionToken">Метка этой сессии.</param>
        /// <param name="hasReservation">За этим игроком держится слот (он отвалился и вернулся).</param>
        /// <returns>Пустая строка, если допущен, иначе код из <see cref="DisconnectReasons"/>.</returns>
        public static string Evaluate(in ConnectionPayload payload, bool payloadValid, int protocolVersion, ulong sessionToken,
            bool hasReservation, int freeSlots, bool acceptingPlayers)
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
            if (requested > freeSlots)
            {
                return DisconnectReasons.LobbyFull;
            }

            return DisconnectReasons.None;
        }
    }
}
