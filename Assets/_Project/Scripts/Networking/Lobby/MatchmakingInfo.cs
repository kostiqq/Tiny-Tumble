using System;
using Unity.Netcode;

namespace TinyTumble.Networking.Lobby
{
    /// <summary>
    /// Поиск игры глазами всей сессии: идёт ли он, открыто ли лобби для найденных поиском, когда старт с ботами.
    /// Синхронизируется объектом сессии и переживает миграцию: новый хост продолжает ждать игроков с тем же сроком.
    /// Срок — абсолютное время UTC: часы машин расходятся на секунды, для таймера поиска это терпимо.
    /// </summary>
    public struct MatchmakingInfo : INetworkSerializable, IEquatable<MatchmakingInfo>
    {
        private bool _searching;
        private bool _open;
        private bool _matchmade;
        private long _endsAtUtcTicks;

        public MatchmakingInfo(bool searching, bool open, bool matchmade, long endsAtUtcTicks)
        {
            _searching = searching;
            _open = open;
            _matchmade = matchmade;
            _endsAtUtcTicks = endsAtUtcTicks;
        }

        /// <summary>Лидер ищет игру или хост ждёт игроков: в лобби идёт таймер.</summary>
        public bool Searching => _searching;

        /// <summary>Хост ждёт игроков: пришедших по поиску пускаем.</summary>
        public bool Open => _open;

        /// <summary>Сессия собирается поиском: в матче свободные места займут боты. Остаётся до конца сессии.</summary>
        public bool Matchmade => _matchmade;

        public long EndsAtUtcTicks => _endsAtUtcTicks;

        public float SecondsLeft(long nowUtcTicks)
        {
            if (!_searching)
            {
                return -1f;
            }

            return Math.Max(0f, (float)TimeSpan.FromTicks(_endsAtUtcTicks - nowUtcTicks).TotalSeconds);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _searching);
            serializer.SerializeValue(ref _open);
            serializer.SerializeValue(ref _matchmade);
            serializer.SerializeValue(ref _endsAtUtcTicks);
        }

        public bool Equals(MatchmakingInfo other)
        {
            return _searching == other._searching && _open == other._open && _matchmade == other._matchmade
                && _endsAtUtcTicks == other._endsAtUtcTicks;
        }

        public override bool Equals(object obj)
        {
            return obj is MatchmakingInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_searching, _open, _matchmade, _endsAtUtcTicks);
        }
    }
}
