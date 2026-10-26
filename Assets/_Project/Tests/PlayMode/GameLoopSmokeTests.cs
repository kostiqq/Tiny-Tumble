using System.Collections;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Network;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using TinyTumble.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using static TinyTumble.Tests.PlayModeHelpers;
using Object = UnityEngine.Object;

namespace TinyTumble.Tests
{
    /// <summary>
    /// Полный цикл Boot → Меню → Лобби → Матч → Итоги → Лобби → Меню без ошибок в консоли.
    /// Соло идёт через ту же сеть, что и онлайн: хост без подключений.
    /// Любой Debug.LogError или исключение роняет тест (поведение Unity Test Framework по умолчанию).
    /// </summary>
    public sealed class GameLoopSmokeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(ResetSessionAsync);

        [UnityTest]
        public IEnumerator FullLoop_ViaSceneButtons() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Boot);

            await WaitForState(GameState.MainMenu);
            var session = FindRoot().Resolve<INetworkSession>();
            Assert.AreEqual(SessionRole.None, session.Role.CurrentValue);
            Click(ButtonNames.Host);

            await WaitForState(GameState.Lobby);
            Assert.IsTrue(session.IsHost, "Play from the menu hosts a session.");
            Assert.IsFalse(IsButtonVisible(ButtonNames.Ready), "The host has no Ready button.");
            Click(ButtonNames.StartMatch);

            await WaitForState(GameState.Match);
            MatchEntryPoint match = await WaitForMatchReady();
            foreach (PlayerCharacter character in match.Characters)
            {
                Assert.IsTrue(character.IsSpawned, "Characters are network objects spawned by the host.");
            }

            Click(ButtonNames.EndMatch);

            await WaitForState(GameState.Results);
            Click(ButtonNames.ToLobby);

            await WaitForState(GameState.Lobby);
            Assert.IsTrue(session.IsHost, "The session survives the match.");
            Click(ButtonNames.Leave);

            await WaitForState(GameState.MainMenu);
            Assert.AreEqual(SessionRole.None, session.Role.CurrentValue, "Leaving to the menu closes the session.");
        });

        [UnityTest]
        public IEnumerator MatchEndConsoleCommand_LeadsToResults() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Match);
            await WaitForMatchReady();

            Assert.IsTrue(FindRoot().Resolve<INetworkSession>().IsHost, "A match opened directly starts a solo host.");
            Assert.IsTrue(FindRoot().Resolve<IDebugConsole>().Execute(MatchDebugCommands.EndCommand));

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
    }
}
