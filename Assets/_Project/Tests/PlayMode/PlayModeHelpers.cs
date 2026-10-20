using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Network;
using TinyTumble.Core.Scenes;
using TinyTumble.Gameplay.Match;
using TinyTumble.Networking.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace TinyTumble.Tests
{
    /// <summary>
    /// Общее для PlayMode-тестов. Корень живёт весь прогон, поэтому сессию, оставшуюся от прошлого теста,
    /// закрываем перед каждым тестом.
    /// </summary>
    public static class PlayModeHelpers
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public static IObjectResolver FindRoot()
        {
            var root = Object.FindAnyObjectByType<RootLifetimeScope>();
            Assert.IsNotNull(root, "RootLifetimeScope was not created. Check VContainerSettings in Preloaded Assets.");
            return root.Container;
        }

        /// <summary>Закрывает сессию прошлого теста и ждёт, пока NGO действительно остановится.</summary>
        public static async UniTask ResetSessionAsync()
        {
            var root = Object.FindAnyObjectByType<RootLifetimeScope>();
            if (root == null || root.Container == null)
            {
                return;
            }

            root.Container.Resolve<INetworkSession>().Shutdown();
            var runtime = root.Container.Resolve<NetworkRuntime>();
            await WaitUntil(() => !runtime.IsRunning && !runtime.Manager.ShutdownInProgress, "NGO shutdown");
        }

        public static async UniTask WaitForState(GameState state)
        {
            float deadline = Time.realtimeSinceStartup + (float)Timeout.TotalSeconds;
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

        public static async UniTask<MatchEntryPoint> WaitForMatchReady()
        {
            float deadline = Time.realtimeSinceStartup + (float)Timeout.TotalSeconds;
            while (true)
            {
                var scope = Object.FindAnyObjectByType<MatchLifetimeScope>();
                if (scope != null && scope.Container != null)
                {
                    var match = scope.Container.Resolve<MatchEntryPoint>();
                    var loading = FindRoot().Resolve<ILoadingScreen>();
                    if (match.IsReady && !loading.IsVisible)
                    {
                        Assert.GreaterOrEqual(match.Characters.Count, 1, "No characters spawned.");
                        return match;
                    }
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timed out waiting for the match to load.");
                }

                await UniTask.Yield();
            }
        }

        public static async UniTask WaitUntil(Func<bool> condition, string what)
        {
            float deadline = Time.realtimeSinceStartup + (float)Timeout.TotalSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out waiting for {what}.");
                }

                await UniTask.Yield();
            }
        }

        /// <summary>Нажимает активную кнопку с этим именем в активной сцене.</summary>
        public static void Click(string buttonName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (Button button in Object.FindObjectsByType<Button>())
            {
                if (button.gameObject.scene == active && button.name == buttonName)
                {
                    Assert.IsTrue(button.interactable, $"Button {buttonName} is not interactable.");
                    button.onClick.Invoke();
                    return;
                }
            }

            Assert.Fail($"No active button {buttonName} in scene {active.name}.");
        }

        public static bool IsButtonVisible(string buttonName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (Button button in Object.FindObjectsByType<Button>())
            {
                if (button.gameObject.scene == active && button.name == buttonName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
