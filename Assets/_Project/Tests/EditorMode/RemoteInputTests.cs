using NUnit.Framework;
using TinyTumble.Core.Input;
using TinyTumble.Networking.Input;
using UnityEngine;

namespace TinyTumble.Tests
{
    public sealed class RemoteInputTests
    {
        [Test]
        public void Buffer_KeepsTapsUntilSampled_AndLatestStick()
        {
            var buffer = new RemoteInputBuffer();
            buffer.Push(new PlayerInputFrame(new Vector2(1f, 0f), PlayerButtons.Jump, PlayerButtons.None, PlayerButtons.Jump));
            buffer.Push(new PlayerInputFrame(new Vector2(0f, 1f), PlayerButtons.None, PlayerButtons.Jump, PlayerButtons.None));

            PlayerInputFrame frame = buffer.Consume();

            Assert.AreEqual(new Vector2(0f, 1f), frame.Move, "The stick comes from the latest packet.");
            Assert.IsTrue(frame.WasPressed(PlayerButtons.Jump), "A tap between host ticks must not be lost.");
            Assert.IsTrue(frame.WasReleased(PlayerButtons.Jump));
            Assert.IsFalse(frame.IsHeld(PlayerButtons.Jump));

            PlayerInputFrame next = buffer.Consume();
            Assert.IsFalse(next.WasPressed(PlayerButtons.Jump), "Presses are consumed by the sample.");
            Assert.AreEqual(new Vector2(0f, 1f), next.Move, "Without new packets the stick stays.");
        }

        [Test]
        public void Buffer_Clear_ReleasesEverything()
        {
            var buffer = new RemoteInputBuffer();
            buffer.Push(new PlayerInputFrame(Vector2.right, PlayerButtons.Dash, PlayerButtons.None, PlayerButtons.Dash));
            buffer.Clear();

            PlayerInputFrame frame = buffer.Consume();
            Assert.AreEqual(Vector2.zero, frame.Move);
            Assert.AreEqual(PlayerButtons.None, frame.Held);
            Assert.AreEqual(PlayerButtons.None, frame.Pressed);
        }

        [Test]
        public void Packet_RoundTrips_AndClampsStick()
        {
            var source = new PlayerInputFrame(new Vector2(3f, 4f), PlayerButtons.Push, PlayerButtons.Grab, PlayerButtons.Parry);
            PlayerInputFrame frame = new InputPacket(source).ToFrame();

            Assert.AreEqual(1f, frame.Move.magnitude, 0.0001f, "Stick from the network is untrusted.");
            Assert.AreEqual(PlayerButtons.Push, frame.Pressed);
            Assert.AreEqual(PlayerButtons.Grab, frame.Released);
            Assert.AreEqual(PlayerButtons.Parry, frame.Held);
        }

        [Test]
        public void Packet_SameState_IgnoresEvents()
        {
            var idle = new InputPacket(new PlayerInputFrame(Vector2.up, PlayerButtons.None, PlayerButtons.None, PlayerButtons.None));
            var tap = new InputPacket(new PlayerInputFrame(Vector2.up, PlayerButtons.Jump, PlayerButtons.Jump, PlayerButtons.None));
            var moved = new InputPacket(new PlayerInputFrame(Vector2.down, PlayerButtons.None, PlayerButtons.None, PlayerButtons.None));

            Assert.IsFalse(idle.HasEvents);
            Assert.IsTrue(tap.HasEvents);
            Assert.IsTrue(idle.SameState(tap));
            Assert.IsFalse(idle.SameState(moved));
        }
    }
}
