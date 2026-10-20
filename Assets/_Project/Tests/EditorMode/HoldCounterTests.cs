using NUnit.Framework;
using TinyTumble.Core.Utils;

namespace TinyTumble.Tests
{
    public sealed class HoldCounterTests
    {
        [Test]
        public void StaysHeld_UntilLastRelease()
        {
            var counter = new HoldCounter();

            Assert.IsTrue(counter.Acquire());
            Assert.IsFalse(counter.Acquire());
            Assert.IsFalse(counter.Release());
            Assert.IsTrue(counter.IsHeld);
            Assert.IsTrue(counter.Release());
            Assert.IsFalse(counter.IsHeld);
        }

        [Test]
        public void ExtraRelease_DoesNotGoNegative()
        {
            var counter = new HoldCounter();

            Assert.IsFalse(counter.Release());
            Assert.AreEqual(0, counter.Count);
        }
    }
}
