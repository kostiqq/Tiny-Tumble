using System;
using Cysharp.Threading.Tasks;
using MessagePipe;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Messages;
using TinyTumble.Core.Network;
using TinyTumble.Core.Scenes;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Хост: по первому MatchEnded убирает персонажей, выгружает арену и переводит всех в Итоги.
    /// Клиент матч не завершает: в Итоги его переведёт хост. Повторные события игнорируются.
    /// </summary>
    public sealed class MatchFlowController : IStartable, IDisposable
    {
        private const string LogTag = "Match";

        private readonly ISubscriber<MatchEnded> _matchEnded;
        private readonly MatchArena _arena;
        private readonly MatchCharacters _characters;
        private readonly IGameFlow _gameFlow;
        private readonly INetworkSession _session;
        private readonly ILoadingScreen _loadingScreen;
        private readonly IGameLog _log;
        private IDisposable _subscription;
        private bool _leaving;

        public MatchFlowController(
            ISubscriber<MatchEnded> matchEnded,
            MatchArena arena,
            MatchCharacters characters,
            IGameFlow gameFlow,
            INetworkSession session,
            ILoadingScreen loadingScreen,
            IGameLog log)
        {
            _matchEnded = matchEnded;
            _arena = arena;
            _characters = characters;
            _gameFlow = gameFlow;
            _session = session;
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

            if (_session.IsRemoteClient)
            {
                _log.Warning(LogTag, "Only the host can end the match.");
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
                _characters.DespawnAll();
                await _arena.UnloadAsync();
                await _gameFlow.GoToAsync(GameState.Results);
            }
        }
    }
}
