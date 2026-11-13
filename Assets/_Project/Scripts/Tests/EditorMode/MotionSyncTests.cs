using NUnit.Framework;
using TinyTumble.Networking.Sync;
using UnityEngine;

namespace TinyTumble.Tests
{
    public sealed class MotionSyncTests
    {
        private const float Tick = 0.02f;
        private const float Delay = 0.1f;

        private static BodySnapshot Snapshot(double time, Vector3 position, byte teleport = 0, byte state = 0)
        {
            return new BodySnapshot
            {
                Time = time,
                Position = position,
                Rotation = Quaternion.identity,
                Velocity = Vector3.zero,
                State = state,
                Teleport = teleport,
            };
        }

        [Test]
        public void Interpolator_ShowsThePast_BetweenSnapshots()
        {
            var interpolator = new SnapshotInterpolator();
            for (int i = 0; i <= 10; i++)
            {
                interpolator.Add(Snapshot(i * 0.04, new Vector3(i * 0.04f, 0f, 0f)));
            }

            // Первый сэмпл ставит часы на «последний снимок минус задержка».
            Assert.IsTrue(interpolator.TrySample(Tick, Delay, out BodySnapshot sample));
            Assert.AreEqual(0.4 - Delay, interpolator.RenderTime, 1e-6);
            Assert.AreEqual(0.3f, sample.Position.x, 1e-4f, "Position is interpolated at render time.");
        }

        [Test]
        public void Interpolator_IgnoresOutOfOrderSnapshots()
        {
            var interpolator = new SnapshotInterpolator();
            Assert.IsTrue(interpolator.Add(Snapshot(1.0, Vector3.one)));
            Assert.IsFalse(interpolator.Add(Snapshot(0.96, Vector3.zero)), "Unreliable channel: an older snapshot arrived late.");
            Assert.AreEqual(1, interpolator.Count);
        }

        [Test]
        public void Interpolator_Teleport_DropsHistory()
        {
            var interpolator = new SnapshotInterpolator();
            interpolator.Add(Snapshot(0.0, new Vector3(5f, 0f, 0f)));
            interpolator.Add(Snapshot(0.04, new Vector3(5f, -6f, 0f)));
            interpolator.Add(Snapshot(0.08, new Vector3(0f, 1f, 0f), teleport: 1));

            Assert.AreEqual(1, interpolator.Count, "No interpolation across the table after a respawn.");
            Assert.IsTrue(interpolator.TrySample(Tick, Delay, out BodySnapshot sample));
            Assert.AreEqual(new Vector3(0f, 1f, 0f), sample.Position);
        }

        [Test]
        public void Interpolator_ClockCatchesUp_WhenBehind()
        {
            var interpolator = new SnapshotInterpolator();
            interpolator.Add(Snapshot(0.0, Vector3.zero));
            interpolator.TrySample(Tick, Delay, out _);
            double start = interpolator.RenderTime;

            // Пришёл снимок намного новее: часы ускоряются, но не прыгают (расхождение меньше порога).
            interpolator.Add(Snapshot(0.3, Vector3.one));
            interpolator.TrySample(Tick, Delay, out _);
            double advanced = interpolator.RenderTime - start;

            Assert.Greater(advanced, Tick, "Behind the target, the playback clock runs faster.");
            Assert.LessOrEqual(advanced, Tick * 1.1 + 1e-9, "But no faster than +10%.");
        }

        [Test]
        public void Interpolator_DiscreteFields_ComeFromEarlierSnapshot()
        {
            var interpolator = new SnapshotInterpolator();
            interpolator.Add(Snapshot(0.0, Vector3.zero, state: 0));
            interpolator.Add(Snapshot(0.04, Vector3.zero, state: 2));
            interpolator.Add(Snapshot(0.2, Vector3.zero, state: 2));

            interpolator.TrySample(Tick, Delay, out BodySnapshot sample);

            Assert.AreEqual(0.1, interpolator.RenderTime, 1e-6);
            Assert.AreEqual(2, sample.State, "State changes when the playback reaches it.");
        }

        [Test]
        public void History_ShiftsWithCorrections()
        {
            var history = new PredictionHistory();
            history.Record(10, new Vector3(1f, 0f, 0f), Vector3.right);
            history.Record(11, new Vector3(2f, 0f, 0f), Vector3.right);
            history.Shift(new Vector3(0f, 0f, 0.5f));

            Assert.IsTrue(history.TryGet(11, out Vector3 position, out Vector3 velocity));
            Assert.AreEqual(new Vector3(2f, 0f, 0.5f), position);
            Assert.AreEqual(Vector3.right, velocity);
            Assert.IsFalse(history.TryGet(12, out _, out _));
            Assert.IsFalse(history.TryGet(0, out _, out _), "0 means no frame.");
        }

        [Test]
        public void History_OldEntriesAreOverwritten()
        {
            var history = new PredictionHistory();
            history.Record(1, Vector3.one, Vector3.zero);
            history.Record(129, Vector3.zero, Vector3.zero);

            Assert.IsFalse(history.TryGet(1, out _, out _), "The ring keeps only recent frames.");
            Assert.IsTrue(history.TryGet(129, out _, out _));
        }

        [Test]
        public void Corrector_IgnoresTinyErrors_SnapsHugeOnes_SmoothsTheRest()
        {
            var corrector = new PredictionCorrector(tolerance: 0.03f, snapDistance: 1.5f, rate: 10f);

            Assert.AreEqual(CorrectionKind.None, corrector.Evaluate(new Vector3(0.01f, 0f, 0f), Vector3.zero, out _));
            Assert.AreEqual(CorrectionKind.Snap, corrector.Evaluate(new Vector3(3f, 0f, 0f), Vector3.zero, out Vector3 snapError));
            Assert.AreEqual(3f, snapError.x, 1e-5f);

            Assert.AreEqual(CorrectionKind.Smooth, corrector.Evaluate(new Vector3(0.5f, 0f, 0f), Vector3.zero, out _));
            Vector3 total = Vector3.zero;
            for (int i = 0; i < 200; i++)
            {
                total += corrector.Step(Tick);
            }

            Assert.AreEqual(0.5f, total.x, 1e-3f, "Smooth correction removes the whole error over time.");
            Assert.AreEqual(Vector3.zero, corrector.PendingError);
        }

        [Test]
        public void Corrector_StepIsAFraction_NotTheWholeError()
        {
            var corrector = new PredictionCorrector(0.03f, 1.5f, 10f);
            corrector.Evaluate(new Vector3(1f, 0f, 0f), Vector3.zero, out _);

            Vector3 step = corrector.Step(Tick);

            Assert.AreEqual(0.2f, step.x, 1e-5f, "rate × dt = 10 × 0.02.");
        }
    }
}
