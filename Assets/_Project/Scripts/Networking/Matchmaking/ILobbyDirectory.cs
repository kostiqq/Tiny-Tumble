using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Matchmaking
{
    /// <summary>
    /// Где хосты объявляют, что ждут игроков, и где их ищут. Steam — список лобби Steam; в редакторе и
    /// development-сборке есть локальный каталог для копий игры на одной машине (автотест без двух аккаунтов Steam).
    /// Логика поиска (<see cref="MatchmakingService"/>) от каталога не зависит.
    /// </summary>
    public interface ILobbyDirectory
    {
        /// <summary>Каталог ищет хостов, к которым подключаются этим транспортом.</summary>
        bool Supports(EndpointKind kind);

        /// <summary>Ключ нашего объявления: им же помечены объявления других хостов (ничьи при выборе, исключение себя).</summary>
        ulong OwnKey { get; }

        /// <summary>Находит открытые лобби и складывает их в results (список очищается).</summary>
        UniTask SearchAsync(LobbyQuery query, List<LobbyAd> results, CancellationToken cancellationToken);

        /// <summary>Объявить (или обновить) наше лобби: сколько в нём игроков и мест. Повторный вызов дёшев.</summary>
        void Publish(int players, int freeSlots);

        /// <summary>Убрать объявление.</summary>
        void Unpublish();
    }

    /// <summary>Что ищем.</summary>
    public readonly struct LobbyQuery
    {
        public readonly int ProtocolVersion;

        /// <summary>Свободных мест не меньше, чем слотов у нашей пати.</summary>
        public readonly int MinFreeSlots;

        /// <summary>Расширенный поиск: в Steam фильтр расстояния «по умолчанию» вместо «близко».</summary>
        public readonly bool Wide;

        public LobbyQuery(int protocolVersion, int minFreeSlots, bool wide)
        {
            ProtocolVersion = protocolVersion;
            MinFreeSlots = minFreeSlots;
            Wide = wide;
        }
    }

    /// <summary>Объявление хоста, который ждёт игроков.</summary>
    public readonly struct LobbyAd
    {
        public readonly SessionEndpoint Host;
        public readonly ulong Key;
        public readonly int Players;
        public readonly int FreeSlots;

        /// <summary>Оценка RTT до хоста в мс; меньше 0 — оценить не удалось.</summary>
        public readonly int PingMs;

        public LobbyAd(SessionEndpoint host, ulong key, int players, int freeSlots, int pingMs)
        {
            Host = host;
            Key = key;
            Players = players;
            FreeSlots = freeSlots;
            PingMs = pingMs;
        }
    }
}
