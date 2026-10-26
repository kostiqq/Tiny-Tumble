using NUnit.Framework;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Combat;
using UnityEngine;

namespace TinyTumble.Tests
{
    public sealed class CombatRulesTests
    {
        private CombatConfig _combat;
        private CharacterConfig _character;

        [SetUp]
        public void SetUp()
        {
            // Значения по умолчанию из конфигов — те же, что в GDD.
            _combat = ScriptableObject.CreateInstance<CombatConfig>();
            _character = ScriptableObject.CreateInstance<CharacterConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_combat);
            Object.DestroyImmediate(_character);
        }

        [Test]
        public void Shake_Accumulates_AndIsCapped()
        {
            float shake = 0f;
            shake = CombatRules.AddShake(shake, _combat.PushShake, _combat);
            shake = CombatRules.AddShake(shake, _combat.ItemThrowShake, _combat);
            shake = CombatRules.AddShake(shake, _combat.PlayerThrowShake, _combat);

            Assert.AreEqual(45f, shake, 1e-4f, "GDD: push +10%, item throw +15%, player throw +20%.");
            Assert.AreEqual(_combat.MaxShake, CombatRules.AddShake(shake, 10000f, _combat));
            Assert.AreEqual(shake, CombatRules.AddShake(shake, -5f, _combat), "A hit never heals.");
        }

        [Test]
        public void Knockback_GrowsWithShake()
        {
            float fresh = CombatRules.KnockbackSpeed(0f, _combat);
            float battered = CombatRules.KnockbackSpeed(100f, _combat);

            Assert.AreEqual(_combat.BaseKnockback, fresh, 1e-5f);
            Assert.AreEqual(_combat.BaseKnockback + 100f * _combat.KnockbackPerShakePercent, battered, 1e-5f);
            Assert.Greater(battered, fresh * 2f, "At 100% the same push sends you much further.");
        }

        [Test]
        public void KnockbackVelocity_IsHorizontalPlusLift()
        {
            Vector3 velocity = CombatRules.KnockbackVelocity(new Vector3(0f, -5f, 2f), 10f, _combat);

            Assert.AreEqual(0f, velocity.x, 1e-5f);
            Assert.AreEqual(10f, velocity.z, 1e-4f, "The vertical part of the direction is ignored.");
            Assert.AreEqual(10f * _combat.KnockbackLift, velocity.y, 1e-4f);
        }

        [Test]
        public void Ragdoll_OnlyAfterThreshold()
        {
            Assert.IsFalse(CombatRules.ShouldRagdoll(_combat.PushShake, _combat), "The first push only staggers.");
            Assert.IsTrue(CombatRules.ShouldRagdoll(_combat.RagdollShakeThreshold, _combat));
        }

        [Test]
        public void HoldLimit_ChainGrabHoldsLonger()
        {
            Assert.AreEqual(0f, CombatRules.HoldLimit(0, _combat));
            Assert.AreEqual(_combat.SingleGrabBreakTime, CombatRules.HoldLimit(1, _combat));
            Assert.AreEqual(_combat.DoubleGrabHoldTime, CombatRules.HoldLimit(2, _combat));
            Assert.Greater(CombatRules.HoldLimit(2, _combat), CombatRules.HoldLimit(1, _combat));
        }

        [Test]
        public void Throw_ChargeScalesSpeed_AndIsClamped()
        {
            Assert.AreEqual(_combat.MinThrowSpeed, CombatRules.ThrowSpeed(0f, _combat), 1e-5f);
            Assert.AreEqual(_combat.MaxThrowSpeed, CombatRules.ThrowSpeed(_combat.MaxThrowCharge, _combat), 1e-5f);
            Assert.AreEqual(_combat.MaxThrowSpeed, CombatRules.ThrowSpeed(_combat.MaxThrowCharge * 5f, _combat), 1e-5f);

            Vector3 velocity = CombatRules.ThrowVelocity(Vector3.right, 10f, _combat);
            Assert.AreEqual(10f, velocity.magnitude, 1e-4f);
            Assert.Greater(velocity.y, 0f, "Throws go slightly up.");
        }

        [Test]
        public void GetUp_WaitsForMinimum_SettlesOrTimesOut()
        {
            Assert.IsFalse(CombatRules.ShouldGetUp(0.05f, grounded: true, speed: 0f, _character), "Too short to read as a fall.");
            Assert.IsTrue(CombatRules.ShouldGetUp(_character.MinRagdollTime, grounded: true, speed: 0f, _character));
            Assert.IsFalse(CombatRules.ShouldGetUp(_character.MinRagdollTime, grounded: false, speed: 0f, _character), "Not in the air.");
            Assert.IsFalse(CombatRules.ShouldGetUp(_character.MinRagdollTime, grounded: true, speed: 10f, _character), "Not while rolling.");
            Assert.IsTrue(CombatRules.ShouldGetUp(_character.MaxRagdollTime, grounded: false, speed: 10f, _character),
                "The limit keeps ragdoll from being annoying.");
        }

        [Test]
        public void PushDirection_BlendsFacingAndTargetPosition()
        {
            Vector3 straight = CombatRules.PushDirection(Vector3.forward, Vector3.zero, new Vector3(0f, 0f, 1f));
            Vector3 aside = CombatRules.PushDirection(Vector3.forward, Vector3.zero, new Vector3(1f, 0f, 1f));

            Assert.AreEqual(Vector3.forward, straight);
            Assert.Greater(aside.x, 0f, "A target to the side flies a bit sideways.");
            Assert.AreEqual(1f, aside.magnitude, 1e-5f);
        }

        [Test]
        public void GrabLinks_DefaultIsEmpty_HoldsUpToTwo()
        {
            GrabLinks links = default;
            Assert.AreEqual(0, links.Count);
            Assert.AreEqual(GrabLinks.None, links[0]);
            Assert.IsFalse(links.Contains(0), "Object id 0 is a valid id, not a holder of an empty link.");

            Assert.IsTrue(links.TryAdd(0));
            Assert.IsTrue(links.TryAdd(5));
            Assert.IsFalse(links.TryAdd(7), "Chain grab: at most two holders.");
            Assert.IsFalse(links.TryAdd(5));
            Assert.IsTrue(links.Contains(0));

            Assert.IsTrue(links.Remove(0));
            Assert.AreEqual(1, links.Count);
            Assert.AreEqual(5ul, links[0]);
        }

        [Test]
        public void StickResponse_DeadZoneAndCurve_KeepDirection()
        {
            Assert.AreEqual(Vector2.zero, StickResponse.Apply(new Vector2(0.1f, 0f), 0.15f, 0.95f, 1.4f), "Drift inside the dead zone.");
            Assert.AreEqual(1f, StickResponse.Apply(new Vector2(0f, 0.97f), 0.15f, 0.95f, 1.4f).magnitude, 1e-5f, "Outer zone = full tilt.");

            Vector2 half = StickResponse.Apply(new Vector2(0.4f, 0.4f), 0.15f, 0.95f, 1.4f);
            Assert.AreEqual(half.x, half.y, 1e-6f, "Direction is preserved.");
            Assert.Less(half.magnitude, 0.5f, "The curve gives precision at small tilts.");
        }
    }
}
