using TinyTumble.Core.Network;
using TinyTumble.Networking.Steam;

namespace TinyTumble.Networking.Matchmaking
{
    /// <summary>Каталог под транспорт сессии: Steam для Steam, локальный для IP (только редактор и development-сборка).</summary>
    public sealed class LobbyDirectories
    {
        private readonly SteamLobbyDirectory _steam;
        private readonly LocalLobbyDirectory _local;

        public LobbyDirectories(SteamLobbyDirectory steam, LocalLobbyDirectory local)
        {
            _steam = steam;
            _local = local;
        }

        /// <returns>null, если искать этим транспортом негде.</returns>
        public ILobbyDirectory For(EndpointKind kind)
        {
            if (_steam.Supports(kind))
            {
                return _steam;
            }

            return _local.Supports(kind) ? _local : null;
        }
    }
}
