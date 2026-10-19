using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Scenes;

namespace TinyTumble.Infrastructure.Flow
{
    public sealed class GameFlow : IGameFlow, IDisposable
    {
        private const string LogTag = "GameFlow";

        private readonly ISceneLoader _sceneLoader;
        private readonly ILoadingScreen _loadingScreen;
        private readonly IGameLog _log;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public GameFlow(ISceneLoader sceneLoader, ILoadingScreen loadingScreen, IGameLog log)
        {
            _sceneLoader = sceneLoader;
            _loadingScreen = loadingScreen;
            _log = log;
        }

        // Берём из активной сцены, а не храним: Play можно нажать в любой сцене, а тесты грузят Boot сами.
        public GameState Current => GameStateExtensions.FromSceneName(_sceneLoader.ActiveSceneName);

        public bool IsTransitioning { get; private set; }

        public async UniTask<bool> GoToAsync(GameState target)
        {
            string sceneName = target.ToSceneName();
            if (sceneName == null)
            {
                _log.Error(LogTag, $"State {target} has no scene.");
                return false;
            }

            // Защита от двойного перехода: двойной клик, кнопка + команда консоли, два события MatchEnded.
            if (IsTransitioning)
            {
                _log.Warning(LogTag, $"Transition to {target} ignored: another transition is in progress.");
                return false;
            }

            GameState from = Current;
            if (from == target)
            {
                _log.Warning(LogTag, $"Transition to {target} ignored: already there.");
                return false;
            }

            IsTransitioning = true;
            IDisposable hold = _loadingScreen.Hold();
            try
            {
                _log.Info(LogTag, $"{from} -> {target}");
                await _sceneLoader.LoadAsync(sceneName, _lifetime.Token);
                return true;
            }
            finally
            {
                IsTransitioning = false;
                hold.Dispose();
            }
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
