using NUnit.Framework;
using TinyTumble.Core.Input;
using TinyTumble.Networking.Input;
using UnityEngine;

namespace TinyTumble.Tests
{
    public sealed class RemoteInputTests
    {
        private static PlayerInputFrame Frame(Vector2 move, PlayerButtons pressed = PlayerButtons.None, PlayerButtons held = PlayerButtons.None)
        {
            return new PlayerInputFrame(move, pressed, PlayerButtons.None, held);
        }

        private static InputBundle Bundle(params InputPacket[] oldestFirst)
        {
            var bundle = new InputBundle();
            for (int i = 0; i < oldestFirst.Length; i++)
            {
                bundle.PushNewest(oldestFirst[i]);
            }

            return bundle;
        }

        [Test]
        public void Packet_RoundTrips_AndClampsStick()
        {
            var source = new PlayerInputFrame(new Vector2(3f, 4f), PlayerButtons.Push, PlayerButtons.Grab, PlayerButtons.Parry);
            var packet = new InputPacket(7, source);
            PlayerInputFrame frame = packet.ToFrame();

            Assert.AreEqual(7u, packet.Sequence);
            Assert.AreEqual(1f, frame.Move.magnitude, 0.01f, "Stick from the network is untrusted.");
            Assert.AreEqual(PlayerButtons.Push, frame.Pressed);
            Assert.AreEqual(PlayerButtons.Grab, frame.Released);
            Assert.AreEqual(PlayerButtons.Parry, frame.Held);
        }

        [Test]
        public void Packet_QuantizesStick_SoPredictionMatchesHost()
        {
            var packet = new InputPacket(1, Frame(new Vector2(0.333f, -0.5f)));
            PlayerInputFrame once = packet.ToFrame();
            PlayerInputFrame twice = new InputPacket(2, once).ToFrame();

            Assert.AreEqual(0.333f, once.Move.x, 1f / 127f);
            Assert.AreEqual(once.Move, twice.Move, "Quantization must be stable: the owner predicts with the value the host gets.");
        }

        [Test]
        public void Bundle_KeepsLastThree_NewestFirst()
        {
            InputBundle bundle = Bundle(
                new InputPacket(1, Frame(Vector2.zero)),
                new InputPacket(2, Frame(Vector2.zero)),
                new InputPacket(3, Frame(Vector2.zero)),
                new InputPacket(4, Frame(Vector2.zero)));

            Assert.AreEqual(InputBundle.Capacity, bundle.Count);
            Assert.AreEqual(4u, bundle[0].Sequence);
            Assert.AreEqual(2u, bundle[2].Sequence);
        }

        [Test]
        public void Queue_ConsumesOneFramePerTick_InOrder_AndSkipsDuplicates()
        {
            var queue = new RemoteInputQueue(maxBuffered: 8);
            var first = new InputPacket(1, Frame(Vector2.right, PlayerButtons.Jump));
            var second = new InputPacket(2, Frame(Vector2.up));
            queue.Receive(Bundle(first));
            // Следующий пакет повторяет первый кадр: дубликат не должен попасть в очередь второй раз.
            queue.Receive(Bundle(first, second));

            Assert.AreEqual(2, queue.Count);
            PlayerInputFrame a = queue.Consume();
            Assert.IsTrue(a.WasPressed(PlayerButtons.Jump));
            Assert.AreEqual(1u, queue.LastConsumed);

            PlayerInputFrame b = queue.Consume();
            Assert.AreEqual(Vector2.up, b.Move);
            Assert.IsFalse(b.WasPressed(PlayerButtons.Jump));
            Assert.AreEqual(2u, queue.LastConsumed);
        }

        [Test]
        public void Queue_LostPacket_IsRecoveredFromRedundancy()
        {
            var queue = new RemoteInputQueue(maxBuffered: 8);
            var jump = new InputPacket(1, Frame(Vector2.zero, PlayerButtons.Jump));
            var idle2 = new InputPacket(2, Frame(Vector2.zero));
            var idle3 = new InputPacket(3, Frame(Vector2.zero));

            // Пакеты с кадрами 1 и 2 потерялись, дошёл только третий — в нём есть и первые два.
            queue.Receive(Bundle(jump, idle2, idle3));

            Assert.IsTrue(queue.Consume().WasPressed(PlayerButtons.Jump), "A press survives two lost packets.");
        }

        [Test]
        public void Queue_Starved_RepeatsStateWithoutPresses()
        {
            var queue = new RemoteInputQueue(maxBuffered: 8);
            queue.Receive(Bundle(new InputPacket(1, Frame(Vector2.left, PlayerButtons.Dash, PlayerButtons.Grab))));
            queue.Consume();

            PlayerInputFrame starved = queue.Consume();
            Assert.AreEqual(Vector2.left, starved.Move, "While the next frame is late, the stick stays.");
            Assert.IsTrue(starved.IsHeld(PlayerButtons.Grab));
            Assert.IsFalse(starved.WasPressed(PlayerButtons.Dash), "A press must not repeat.");
            Assert.AreEqual(1u, queue.LastConsumed);
        }

        [Test]
        public void Queue_TooFarBehind_MergesFrames_KeepingPresses()
        {
            var queue = new RemoteInputQueue(maxBuffered: 2);
            queue.Receive(Bundle(new InputPacket(1, Frame(Vector2.zero, PlayerButtons.Jump))));
            queue.Receive(Bundle(new InputPacket(2, Frame(Vector2.zero))));
            queue.Receive(Bundle(new InputPacket(3, Frame(Vector2.zero, PlayerButtons.Push))));
            queue.Receive(Bundle(new InputPacket(4, Frame(Vector2.down))));

            PlayerInputFrame merged = queue.Consume();

            Assert.LessOrEqual(queue.Count, 1, "The host catches up instead of growing the input delay.");
            Assert.IsTrue(merged.WasPressed(PlayerButtons.Jump));
            Assert.IsTrue(merged.WasPressed(PlayerButtons.Push));
        }

        [Test]
        public void Queue_Clear_ReleasesEverything()
        {
            var queue = new RemoteInputQueue(maxBuffered: 4);
            queue.Receive(Bundle(new InputPacket(1, Frame(Vector2.right, PlayerButtons.Dash, PlayerButtons.Dash))));
            queue.Consume();
            queue.Clear();

            PlayerInputFrame frame = queue.Consume();
            Assert.AreEqual(Vector2.zero, frame.Move);
            Assert.AreEqual(PlayerButtons.None, frame.Held);
            Assert.AreEqual(0u, queue.LastConsumed);

            // После сброса (новый владелец) нумерация начинается заново.
            queue.Receive(Bundle(new InputPacket(1, Frame(Vector2.up))));
            Assert.AreEqual(1, queue.Count);
        }
    }
}
