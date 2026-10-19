using System;
using Cysharp.Threading.Tasks;
using MessagePipe;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Messages;
using TinyTumble.Core.Scenes;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// По первому MatchEnded выгружает арену и переводит игру в Итоги. Повторные события игнорируются.
    /// </summary>
    public sealed class MatchFlowController : IStartable, IDisposable
    {
        private const string LogTag = "Match";

        private readonly ISubscriber<MatchEnded> _matchEnded;
        private readonly MatchArena _arena;
        private readonly IGameFlow _gameFlow;
        private readonly ILoadingScreen _loadingScreen;
        private readonly IGameLog _log;
        private IDisposable _subscription;
        private bool _leaving;

        public MatchFlowController(
            ISubscriber<MatchEnded> matchEnded,
            MatchArena arena,
            IGameFlow gameFlow,
            ILoadingScreen loadingScreen,
            IGameLog log)
        {
            _matchEnded = matchEnded;
            _arena = arena;
            _gameFlow = gameFlow;
            _loadingScreen = loadingScreen;
            _log = log;
        }

        public void Start()
        {
            _subscription = _matchEnded.Subscribe(OnMatchEnded);
        }

        public void Dispose()
        {
            if (_subscription != null)
            {
                _subscription.Dispose();
                _subscription = null;
            }
        }

        private void OnMatchEnded(MatchEnded message)
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;
            _log.Info(LogTag, message.WinnerId == MatchEnded.NoWinner ? "Match ended, no winner." : $"Match ended, winner: {message.WinnerId}.");
            LeaveAsync().Forget();
        }

        private async UniTaskVoid LeaveAsync()
        {
            // Экран закрывает стол сразу, пока выгружается арена; GameFlow возьмёт свой холд поверх.
            using (_loadingScreen.Hold())
            {
                await _arena.UnloadAsync();
                await _gameFlow.GoToAsync(GameState.Results);
            }
        }
    }
}
