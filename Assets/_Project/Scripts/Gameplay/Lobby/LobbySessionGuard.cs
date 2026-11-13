using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Lobby
{
    /// <summary>
    /// В лобби всегда есть сессия. Обычно её создаёт главное меню; если лобби открыли напрямую
    /// (Play из сцены, тест), поднимаем хост сами.
    /// </summary>
    public sealed class LobbySessionGuard : IAsyncStartable
    {
        private const string LogTag = "Lobby";

        private readonly INetworkSession _session;
        private readonly IGameLog _log;

        public LobbySessionGuard(INetworkSession session, IGameLog log)
        {
            _session = session;
            _log = log;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            if (_session.Role.CurrentValue != SessionRole.None)
            {
                return;
            }

            SessionStartResult result = await _session.StartHostAsync(cancellation);
            if (!result.Success && result.Reason != DisconnectReasons.AlreadyRunning)
            {
                _log.Error(LogTag, $"Cannot start a session: {result.Reason}.");
            }
        }
    }
}
