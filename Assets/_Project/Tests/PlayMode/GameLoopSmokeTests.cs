using System;
using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Scenes;
using TinyTumble.Gameplay.Match;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace TinyTumble.Tests
{
    /// <summary>
    /// Критерий готовности этапа 1: цикл Boot → Меню → Лобби → Матч → Итоги → Лобби проходит без ошибок в консоли.
    /// Любой Debug.LogError или исключение роняет тест (поведение Unity Test Framework по умолчанию).
    /// </summary>
    public sealed class GameLoopSmokeTests
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

        [UnityTest]
        public IEnumerator FullLoop_ViaSceneButtons() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Boot);

            await WaitForState(GameState.MainMenu);
            ClickOnlyButton();

            await WaitForState(GameState.Lobby);
            ClickOnlyButton();

            await WaitForState(GameState.Match);
            await WaitForMatchReady();
            ClickOnlyButton();

            await WaitForState(GameState.Results);
            ClickOnlyButton();

            await WaitForState(GameState.Lobby);
        });

        [UnityTest]
        public IEnumerator MatchEndConsoleCommand_LeadsToResults() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Match);
            await WaitForMatchReady();

            IObjectResolver root = FindRoot();
            Assert.IsTrue(root.Resolve<IDebugConsole>().Execute(MatchDebugCommands.EndCommand));

            await WaitForState(GameState.Results);
        });

        [UnityTest]
        public IEnumerator DoubleTransition_IsIgnored() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.MainMenu);
            await WaitForState(GameState.MainMenu);

            var flow = FindRoot().Resolve<IGameFlow>();
            LogAssert.Expect(LogType.Warning, new Regex("another transition is in progress"));
            UniTask<bool> first = flow.GoToAsync(GameState.Lobby);
            UniTask<bool> second = flow.GoToAsync(GameState.Results);

            Assert.IsFalse(await second);
            Assert.IsTrue(await first);
            Assert.AreEqual(GameState.Lobby, flow.Current);
        });

        [UnityTest]
        public IEnumerator Root_IsCreated_WhenPlayingFromAnyScene() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Results);
            await UniTask.Yield();

            var sceneScope = Object.FindAnyObjectByType<ResultsLifetimeScope>();
            Assert.IsNotNull(sceneScope);
            Assert.IsNotNull(sceneScope.Parent, "Scene scope must get the root scope as parent.");
            Assert.IsInstanceOf<RootLifetimeScope>(sceneScope.Parent);
            Assert.AreEqual(1, Object.FindObjectsByType<RootLifetimeScope>().Length);
        });

        private static IObjectResolver FindRoot()
        {
            var root = Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "RootLifetimeScope was not created. Check VContainerSettings in Preloaded Assets.");
            return root.Container;
        }

        private static async UniTask WaitForState(GameState state)
        {
            float deadline = Time.realtimeSinceStartup + (float)s_timeout.TotalSeconds;
            while (true)
            {
                var root = Object.FindAnyObjectByType<RootLifetimeScope>();
                if (root != null && root.Container != null)
                {
                    var flow = root.Container.Resolve<IGameFlow>();
                    var loading = root.Container.Resolve<ILoadingScreen>();
                    if (flow.Current == state && !flow.IsTransitioning && !loading.IsVisible)
                    {
                        return;
                    }
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out waiting for {state}. Active scene: {SceneManager.GetActiveScene().name}");
                }

                await UniTask.Yield();
            }
        }

        private static async UniTask WaitForMatchReady()
        {
            float deadline = Time.realtimeSinceStartup + (float)s_timeout.TotalSeconds;
            while (true)
            {
                var scope = Object.FindAnyObjectByType<MatchLifetimeScope>();
                if (scope != null && scope.Container != null && scope.Container.Resolve<MatchEntryPoint>().IsReady)
                {
                    var root = Object.FindAnyObjectByType<RootLifetimeScope>();
                    if (!root.Container.Resolve<ILoadingScreen>().IsVisible)
                    {
                        MatchEntryPoint match = scope.Container.Resolve<MatchEntryPoint>();
                        Assert.GreaterOrEqual(match.Characters.Count, 1, "No characters spawned.");
                        return;
                    }
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timed out waiting for the match to load.");
                }

                await UniTask.Yield();
            }
        }

        private static void ClickOnlyButton()
        {
            Scene active = SceneManager.GetActiveScene();
            Button found = null;
            foreach (Button button in Object.FindObjectsByType<Button>())
            {
                if (button.gameObject.scene != active)
                {
                    continue;
                }

                Assert.IsNull(found, $"More than one button in scene {active.name}.");
                found = button;
            }

            Assert.IsNotNull(found, $"No button in scene {active.name}.");
            found.onClick.Invoke();
        }
    }
}
