using NUnit.Framework;
using TinyTumble.Core.Input;
using TinyTumble.Infrastructure.Input;
using UnityEngine;

namespace TinyTumble.Tests
{
    public sealed class InputAccumulatorTests
    {
        [Test]
        public void TapBetweenTicks_IsReportedOnNextTick()
        {
            var accumulator = new InputAccumulator();
            accumulator.RegisterPress(PlayerButtons.Jump);
            accumulator.RegisterRelease(PlayerButtons.Jump);

            PlayerInputFrame frame = accumulator.Consume(Vector2.zero, PlayerButtons.None);

            Assert.IsTrue(frame.WasPressed(PlayerButtons.Jump));
            Assert.IsTrue(frame.WasReleased(PlayerButtons.Jump));
            Assert.IsFalse(frame.IsHeld(PlayerButtons.Jump));
        }

        [Test]
        public void Consume_ClearsAccumulatedPresses()
        {
            var accumulator = new InputAccumulator();
            accumulator.RegisterPress(PlayerButtons.Dash);
            accumulator.Consume(Vector2.zero, PlayerButtons.Dash);

            PlayerInputFrame second = accumulator.Consume(Vector2.zero, PlayerButtons.Dash);

            Assert.IsFalse(second.WasPressed(PlayerButtons.Dash));
            Assert.IsTrue(second.IsHeld(PlayerButtons.Dash));
        }

        [Test]
        public void SeveralButtons_AreCombined()
        {
            var accumulator = new InputAccumulator();
            accumulator.RegisterPress(PlayerButtons.Push);
            accumulator.RegisterPress(PlayerButtons.Grab);

            PlayerInputFrame frame = accumulator.Consume(Vector2.zero, PlayerButtons.None);

            Assert.AreEqual(PlayerButtons.Push | PlayerButtons.Grab, frame.Pressed);
        }

        [Test]
        public void Move_IsClampedToUnitLength()
        {
            var accumulator = new InputAccumulator();

            PlayerInputFrame frame = accumulator.Consume(new Vector2(1f, 1f), PlayerButtons.None);

            Assert.That(frame.Move.magnitude, Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
