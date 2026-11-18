using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Messages;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using static TinyTumble.Tests.PlayModeHelpers;

namespace TinyTumble.Tests
{
    /// <summary>
    /// Бой в живом матче (соло-хост): бот сам находит игрока и толкает его, встряск растёт,
    /// а сбитый с ног персонаж встаёт в пределах лимита ragdoll.
    /// </summary>
    public sealed class CombatPlayTests
    {
        private const float BotFightTimeoutSeconds = 15f;
        private const float GetUpMargin = 1f;

        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(ResetSessionAsync);

        [UnityTest]
        public IEnumerator Bot_PushesIdlePlayer_AndShakeGrows() => UniTask.ToCoroutine(async () =>
        {
            // Матч из сцены входит с клавиатуры; в batchmode её может не быть.
            Keyboard keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();

            await SceneManager.LoadSceneAsync(SceneNames.Match);
            MatchEntryPoint match = await WaitForMatchReady();

            PlayerCharacter player = null;
            PlayerCharacter bot = null;
            foreach (PlayerCharacter character in match.Characters)
            {
                if (character.IsBot)
                {
                    bot = character;
                }
                else
                {
                    player = character;
                }
            }

            Assert.IsNotNull(player, "The solo host joins with the keyboard.");
            Assert.IsNotNull(bot, "A bot is in the match.");

            float deadline = Time.realtimeSinceStartup + BotFightTimeoutSeconds;
            while (player != null && player.Shake.CurrentValue <= 0f && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.Yield();
            }

            Assert.IsNotNull(player, "The player was removed before the bot reached them.");
            Assert.Greater(player.Shake.CurrentValue, 0f, "The bot should have found and pushed the idle player.");
        });

        [UnityTest]
        public IEnumerator HardHit_Ragdolls_AndGetsUpWithinLimit() => UniTask.ToCoroutine(async () =>
        {
            await SceneManager.LoadSceneAsync(SceneNames.Match);
            MatchEntryPoint match = await WaitForMatchReady();

            PlayerCharacter target = match.Characters[0];
            var config = FindRoot().Resolve<CharacterConfig>();
            Vector3 towardCenter = -new Vector3(target.Position.x, 0f, target.Position.z);

            target.ReceiveHit(10f, towardCenter, null, HitKind.Push, forceRagdoll: true, target.Position);

            Assert.AreEqual(CharacterState.Ragdoll, target.State);
            Assert.AreEqual(10f, target.Shake.CurrentValue, 1e-4f);

            float limit = config.MaxRagdollTime + config.MaxGetUpTime + GetUpMargin;
            float deadline = Time.time + limit;
            while (target != null && target.State != CharacterState.Normal && Time.time < deadline)
            {
                await UniTask.Yield();
            }

            Assert.IsNotNull(target, "Knocked toward the center, the character must stay on the table.");
            Assert.AreEqual(CharacterState.Normal, target.State, $"Ragdoll must end within {limit:0.0} s.");
            Assert.Greater(Vector3.Dot(target.Up, Vector3.up), 0.8f, "Back on the feet means the hips are upright.");
        });
    }
}
