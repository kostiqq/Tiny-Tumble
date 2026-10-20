using System;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Infrastructure.Flow;
using TinyTumble.Networking.Lobby;
using VContainer.Unity;

namespace TinyTumble.Networking.Flow
{
    /// <summary>
    /// Переходы между сценами с учётом сессии. Состояниями лобби, матча и итогов управляет только хост:
    /// он объявляет переход через <see cref="SessionLobby"/>, клиенты грузят ту же сцену сами (свой протокол
    /// вместо сцен NGO, потому что арены — сцены Addressables). Уход в главное меню закрывает сессию.
    /// </summary>
    public sealed class NetworkGameFlow : IGameFlow, IInitializable, IDisposable
    {
        private const string LogTag = "GameFlow";
        private const float LeaveAnnouncementDelay = 0.2f;

        private readonly GameFlow _local;
        private readonly INetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly IGameLog _log;
        private GameState _pendingHostState;
        private bool _following;
        private bool _leavingToMenu;

        public NetworkGameFlow(GameFlow local, INetworkSession session, SessionLobby lobby, IGameLog log)
        {
            _local = local;
            _session = session;
            _lobby = lobby;
            _log = log;
        }

        public GameState Current => _local.Current;

        public bool IsTransitioning => _local.IsTransitioning || _following || _leavingToMenu;

        public void Initialize()
        {
            _lobby.HostStateChanged += OnHostStateChanged;
            _session.ConnectionLost += OnConnectionLost;
        }

        public void Dispose()
        {
            _lobby.HostStateChanged -= OnHostStateChanged;
            _session.ConnectionLost -= OnConnectionLost;
        }

        public async UniTask<bool> GoToAsync(GameState target)
        {
            if (target == GameState.MainMenu || target == GameState.Boot)
            {
                if (_session.IsHost && _lobby.AnnounceLeaving())
                {
                    // Даём сообщению «хост уходит» уйти в сеть до закрытия сессии: без него клиенты
                    // сначала прождали бы возвращения хоста и только потом пошли к преемнику.
                    await UniTask.Delay(TimeSpan.FromSeconds(LeaveAnnouncementDelay), ignoreTimeScale: true);
                }

                _session.Shutdown();
                return await _local.GoToAsync(target);
            }

            if (_session.IsRemoteClient)
            {
                _log.Warning(LogTag, $"Transition to {target} ignored: only the host changes the game state.");
                return false;
            }

            if (_session.IsHost && !_local.IsTransitioning && _local.Current != target)
            {
                // Объявляем до своей загрузки: клиенты грузят сцену параллельно с хостом.
                _lobby.SetHostState(target);
            }

            return await _local.GoToAsync(target);
        }

        private void OnHostStateChanged(GameState state)
        {
            if (state == GameState.None || state == GameState.Boot || state == GameState.MainMenu)
            {
                return;
            }

            _pendingHostState = state;
            if (!_following)
            {
                FollowHostAsync().Forget();
            }
        }

        private async UniTaskVoid FollowHostAsync()
        {
            _following = true;
            try
            {
                // Пока грузится одна сцена, хост мог успеть перейти дальше: догоняем последнее состояние.
                while (_pendingHostState != GameState.None)
                {
                    while (_local.IsTransitioning)
                    {
                        await UniTask.Yield();
                    }

                    GameState target = _pendingHostState;
                    _pendingHostState = GameState.None;
                    // Роль Client выставляется чуть позже, чем NGO спавнит объект сессии, поэтому проверяем «не хост».
                    if (_session.IsHost || _local.Current == target)
                    {
                        continue;
                    }

                    await _local.GoToAsync(target);
                }
            }
            finally
            {
                _following = false;
            }
        }

        private void OnConnectionLost(string reason)
        {
            _pendingHostState = GameState.None;
            ReturnToMenuAsync().Forget();
        }

        private async UniTaskVoid ReturnToMenuAsync()
        {
            if (_leavingToMenu)
            {
                return;
            }

            _leavingToMenu = true;
            try
            {
                while (_local.IsTransitioning)
                {
                    await UniTask.Yield();
                }

                if (_local.Current != GameState.MainMenu)
                {
                    await _local.GoToAsync(GameState.MainMenu);
                }
            }
            finally
            {
                _leavingToMenu = false;
            }
        }
    }
}
