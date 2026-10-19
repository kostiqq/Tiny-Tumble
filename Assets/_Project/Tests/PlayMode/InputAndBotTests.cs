using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using TinyTumble.Infrastructure.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace TinyTumble.Tests
{
    public sealed class InputAndBotTests
    {
        private const float BotObservationSeconds = 2f;
        private const float MinBotTravel = 0.5f;
        private const float LoadTimeoutSeconds = 20f;

        [UnityTest]
        public IEnumerator DevicePlayerInput_KeepsTapBetweenSamples() => UniTask.ToCoroutine(async () =>
        {
            // В batchmode и при неактивном окне редактора Input System отбрасывает ввод без фокуса.
            InputSettings.BackgroundBehavior backgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            var keyboard = InputSystem.AddDevice<Keyboard>();
            var input = new DevicePlayerInput(new InputDevice[] { keyboard });
            try
            {
                // Нажатие и отпускание пробела в разных кадрах, но без сэмпла между ними.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.Space));
                await UniTask.Yield();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                await UniTask.Yield();

                PlayerInputFrame frame = input.Sample();

                Assert.Greater(frame.Move.x, 0.5f, "D must move right.");
                Assert.IsTrue(frame.WasPressed(PlayerButtons.Jump), "Tap must not be lost.");
                Assert.IsTrue(frame.WasReleased(PlayerButtons.Jump));
                Assert.IsFalse(frame.IsHeld(PlayerButtons.Jump));

                PlayerInputFrame next = input.Sample();
                Assert.IsFalse(next.WasPressed(PlayerButtons.Jump), "Presses must be consumed by the sample.");
            }
            finally
            {
                input.Dispose();
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = backgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorBehavior;
            }
        });

        [UnityTest]
        public IEnumerator Bot_WandersAcrossTheTable() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Match);

            MatchEntryPoint match = await WaitForMatch();
            StubCharacter bot = null;
            foreach (StubCharacter character in match.Characters)
            {
                bot = character;
            }

            Assert.IsNotNull(bot, "Bot was not spawned.");
            Vector3 start = bot.transform.position;
            float travelled = 0f;
            Vector3 previous = start;
            float end = Time.time + BotObservationSeconds;
            while (Time.time < end)
            {
                await UniTask.Yield();
                Vector3 current = bot.transform.position;
                travelled += Vector3.Distance(previous, current);
                previous = current;
            }

            var config = Object.FindAnyObjectByType<RootLifetimeScope>().Container.Resolve<MatchConfig>();
            Assert.Greater(travelled, MinBotTravel, "Bot did not move.");
            Assert.Greater(bot.transform.position.y, config.EliminationHeight, "Bot fell off the table.");
            Assert.IsTrue(bot.gameObject.activeSelf);
        });

        private static async UniTask<MatchEntryPoint> WaitForMatch()
        {
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var scope = Object.FindAnyObjectByType<MatchLifetimeScope>();
                if (scope != null && scope.Container != null)
                {
                    var match = scope.Container.Resolve<MatchEntryPoint>();
                    if (match.IsReady)
                    {
                        return match;
                    }
                }

                await UniTask.Yield();
            }

            Assert.Fail("Timed out waiting for the match to load.");
            return null;
        }
    }
}
