using System;
using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Данные запроса на подключение (little-endian):
    /// версия протокола (4) + число локальных игроков (1) + ключ игрока (8) + порт, на котором клиент
    /// поднимет хост при миграции (2) + метка сессии, к которой он возвращается, или 0 (8).
    /// </summary>
    public readonly struct ConnectionPayload
    {
        private const int Size = 23;

        public readonly int ProtocolVersion;
        public readonly int LocalPlayerCount;
        public readonly ulong PlayerKey;
        public readonly ushort HostPort;

        /// <summary>0 — новое подключение; иначе клиент восстанавливает именно эту сессию.</summary>
        public readonly ulong SessionToken;

        public ConnectionPayload(int protocolVersion, int localPlayerCount, ulong playerKey, ushort hostPort, ulong sessionToken)
        {
            ProtocolVersion = protocolVersion;
            LocalPlayerCount = localPlayerCount;
            PlayerKey = playerKey;
            HostPort = hostPort;
            SessionToken = sessionToken;
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[Size];
            WriteInt(bytes, 0, ProtocolVersion);
            bytes[4] = (byte)LocalPlayerCount;
            WriteULong(bytes, 5, PlayerKey);
            bytes[13] = (byte)HostPort;
            bytes[14] = (byte)(HostPort >> 8);
            WriteULong(bytes, 15, SessionToken);
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
            ushort port = (ushort)(bytes[13] | (bytes[14] << 8));
            payload = new ConnectionPayload(version, bytes[4], BitConverter.ToUInt64(bytes, 5), port, BitConverter.ToUInt64(bytes, 15));
            return true;
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
