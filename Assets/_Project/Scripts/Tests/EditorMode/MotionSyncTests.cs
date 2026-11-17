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
                Rotation = QuaternionPacking.Pack(Quaternion.identity),
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
            Assert.IsTrue(interpolator.TrySample(Tick, Delay, out BodySnapshot from, out BodySnapshot to, out float t));
            Assert.AreEqual(0.4 - Delay, interpolator.RenderTime, 1e-6);
            Assert.AreEqual(0.3f, Vector3.Lerp(from.Position, to.Position, t).x, 1e-4f, "Position is interpolated at render time.");
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
            Assert.IsTrue(interpolator.TrySample(Tick, Delay, out BodySnapshot from, out BodySnapshot to, out float t));
            Assert.AreEqual(new Vector3(0f, 1f, 0f), Vector3.Lerp(from.Position, to.Position, t));
        }

        [Test]
        public void Interpolator_ClockCatchesUp_WhenBehind()
        {
            var interpolator = new SnapshotInterpolator();
            interpolator.Add(Snapshot(0.0, Vector3.zero));
            interpolator.TrySample(Tick, Delay, out _, out _, out _);
            double start = interpolator.RenderTime;

            // Пришёл снимок намного новее: часы ускоряются, но не прыгают (расхождение меньше порога).
            interpolator.Add(Snapshot(0.3, Vector3.one));
            interpolator.TrySample(Tick, Delay, out _, out _, out _);
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

            interpolator.TrySample(Tick, Delay, out BodySnapshot from, out _, out _);

            Assert.AreEqual(0.1, interpolator.RenderTime, 1e-6);
            Assert.AreEqual(2, from.State, "State changes when the playback reaches it.");
        }

        [Test]
        public void Interpolator_NoNewSnapshots_ExtrapolatesBriefly()
        {
            var interpolator = new SnapshotInterpolator();
            BodySnapshot moving = Snapshot(0.0, Vector3.zero);
            moving.Velocity = new Vector3(1f, 0f, 0f);
            interpolator.Add(moving);
            interpolator.TrySample(Tick, 0f, out _, out _, out _);

            for (int i = 0; i < 50; i++)
            {
                interpolator.TrySample(Tick, 0f, out _, out _, out _);
            }

            interpolator.TrySample(Tick, 0f, out BodySnapshot from, out BodySnapshot to, out float t);
            Assert.AreEqual(from.Position, to.Position, "Past the last snapshot both ends are the same extrapolated state.");
            Assert.LessOrEqual(from.Position.x, 0.1f + 1e-4f, "Extrapolation is capped.");
        }

        [Test]
        public void Quaternion_PacksInto32Bits_WithSmallError()
        {
            var random = new System.Random(7);
            for (int i = 0; i < 500; i++)
            {
                Quaternion rotation = Quaternion.Euler(
                    (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
                Quaternion unpacked = QuaternionPacking.Unpack(QuaternionPacking.Pack(rotation));
                Assert.Less(Quaternion.Angle(rotation, unpacked), 0.3f, $"Rotation {rotation.eulerAngles}");
            }
        }

        [Test]
        public void Quaternion_Packing_HandlesNegatedAndIdentity()
        {
            Quaternion rotation = Quaternion.Euler(30f, 60f, 90f);
            var negated = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);

            Assert.Less(Quaternion.Angle(rotation, QuaternionPacking.Unpack(QuaternionPacking.Pack(negated))), 0.3f, "q and -q are the same rotation.");
            Assert.Less(Quaternion.Angle(Quaternion.identity, QuaternionPacking.Unpack(QuaternionPacking.Pack(Quaternion.identity))), 0.01f);
        }

        [Test]
        public void Yaw_RoundTrips()
        {
            Vector3 facing = new Vector3(-0.6f, 0f, 0.8f);
            Vector3 unpacked = BodySnapshot.UnpackYaw(BodySnapshot.PackYaw(facing));
            Assert.Less(Vector3.Angle(facing, unpacked), 0.05f);
        }

        [Test]
        public void PackedPose_HoldsEveryBoneButTheRoot()
        {
            Assert.AreEqual(TinyTumble.Gameplay.Characters.RagdollBones.Count - 1, PackedPose.Count,
                "Every bone except the hips goes into the snapshot.");

            var pose = new PackedPose();
            for (int i = 0; i < PackedPose.Count; i++)
            {
                pose[i] = (uint)(i * 1000 + 1);
            }

            for (int i = 0; i < PackedPose.Count; i++)
            {
                Assert.AreEqual((uint)(i * 1000 + 1), pose[i]);
            }
        }
    }
}
