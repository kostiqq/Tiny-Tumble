using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Scenes;
using TinyTumble.Core.Utils;
using TinyTumble.Infrastructure.Flow;

namespace TinyTumble.Tests
{
    public sealed class GameFlowTests
    {
        private sealed class FakeSceneLoader : ISceneLoader
        {
            private UniTaskCompletionSource _pending;
            private string _pendingScene;

            public string ActiveSceneName { get; set; } = SceneNames.MainMenu;

            public List<string> Requested { get; } = new List<string>();

            public UniTask LoadAsync(string sceneName, CancellationToken cancellationToken)
            {
                Requested.Add(sceneName);
                _pending = new UniTaskCompletionSource();
                _pendingScene = sceneName;
                return _pending.Task;
            }

            public void CompleteLoad()
            {
                ActiveSceneName = _pendingScene;
                _pending.TrySetResult();
            }
        }

        private sealed class FakeLoadingScreen : ILoadingScreen
        {
            private sealed class Handle : IDisposable
            {
                private FakeLoadingScreen _owner;

                public Handle(FakeLoadingScreen owner)
                {
                    _owner = owner;
                }

                public void Dispose()
                {
                    _owner?._counter.Release();
                    _owner = null;
                }
            }

            private readonly HoldCounter _counter = new HoldCounter();

            public bool IsVisible => _counter.IsHeld;

            public IDisposable Hold()
            {
                _counter.Acquire();
                return new Handle(this);
            }
        }

        private sealed class FakeLog : IGameLog
        {
            public int Warnings { get; private set; }

            public int Errors { get; private set; }

            public void Info(string tag, string message)
            {
            }

            public void Warning(string tag, string message)
            {
                Warnings++;
            }

            public void Error(string tag, string message)
            {
                Errors++;
            }
        }

        private FakeSceneLoader _loader;
        private FakeLoadingScreen _loadingScreen;
        private FakeLog _log;
        private GameFlow _flow;

        [SetUp]
        public void SetUp()
        {
            _loader = new FakeSceneLoader();
            _loadingScreen = new FakeLoadingScreen();
            _log = new FakeLog();
            _flow = new GameFlow(_loader, _loadingScreen, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _flow.Dispose();
        }

        [Test]
        public void Current_FollowsActiveScene()
        {
            Assert.AreEqual(GameState.MainMenu, _flow.Current);

            _loader.ActiveSceneName = "SomeTestRunnerScene";

            Assert.AreEqual(GameState.None, _flow.Current);
        }

        [Test]
        public void SecondTransition_WhileFirstInProgress_IsRejected()
        {
            UniTask<bool> first = _flow.GoToAsync(GameState.Lobby);
            UniTask<bool> second = _flow.GoToAsync(GameState.Match);

            Assert.AreEqual(UniTaskStatus.Succeeded, second.Status);
            Assert.IsFalse(second.GetAwaiter().GetResult());
            Assert.AreEqual(1, _loader.Requested.Count);
            Assert.IsTrue(_flow.IsTransitioning);
            Assert.IsTrue(_loadingScreen.IsVisible);

            _loader.CompleteLoad();

            Assert.IsTrue(first.GetAwaiter().GetResult());
            Assert.IsFalse(_flow.IsTransitioning);
            Assert.IsFalse(_loadingScreen.IsVisible);
            Assert.AreEqual(GameState.Lobby, _flow.Current);
        }

        [Test]
        public void TransitionToCurrentState_IsRejected()
        {
            UniTask<bool> result = _flow.GoToAsync(GameState.MainMenu);

            Assert.IsFalse(result.GetAwaiter().GetResult());
            Assert.IsEmpty(_loader.Requested);
            Assert.AreEqual(1, _log.Warnings);
        }

        [Test]
        public void TransitionToNone_IsRejectedWithError()
        {
            UniTask<bool> result = _flow.GoToAsync(GameState.None);

            Assert.IsFalse(result.GetAwaiter().GetResult());
            Assert.AreEqual(1, _log.Errors);
        }

        [Test]
        public void AfterTransition_NextTransitionIsAllowed()
        {
            UniTask<bool> first = _flow.GoToAsync(GameState.Lobby);
            _loader.CompleteLoad();
            Assert.IsTrue(first.GetAwaiter().GetResult());

            UniTask<bool> second = _flow.GoToAsync(GameState.Match);
            _loader.CompleteLoad();

            Assert.IsTrue(second.GetAwaiter().GetResult());
            Assert.AreEqual(GameState.Match, _flow.Current);
        }

        [Test]
        public void EveryState_HasSceneAndRoundTrips()
        {
            foreach (GameState state in (GameState[])Enum.GetValues(typeof(GameState)))
            {
                if (state == GameState.None)
                {
                    continue;
                }

                Assert.AreEqual(state, GameStateExtensions.FromSceneName(state.ToSceneName()), state.ToString());
            }
        }
    }
}
