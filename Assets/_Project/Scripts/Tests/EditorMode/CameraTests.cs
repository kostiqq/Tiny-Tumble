using NUnit.Framework;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Cameras;
using UnityEngine;

namespace TinyTumble.Tests
{
    public sealed class CameraTests
    {
        private CameraConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<CameraConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [TestCase(0f, 0f, 1f, 0f, 1f)]   // камера смотрит вдоль +Z: «вперёд» = +Z
        [TestCase(90f, 0f, 1f, 1f, 0f)]  // камера смотрит вдоль +X: «вперёд» = +X
        [TestCase(90f, 1f, 0f, 0f, -1f)] // ... и «вправо» = -Z
        [TestCase(180f, 0f, 1f, 0f, -1f)]
        public void MoveFrame_RotatesStickByCameraYaw(float yaw, float x, float y, float expectedX, float expectedZ)
        {
            var frame = new CameraMoveFrame(_config);
            frame.SetYaw(yaw);
            Vector2 world = frame.ToWorld(new Vector2(x, y));
            Assert.That(world.x, Is.EqualTo(expectedX).Within(1e-4f));
            Assert.That(world.y, Is.EqualTo(expectedZ).Within(1e-4f));
        }

        [Test]
        public void MoveFrame_KeepsStickLength()
        {
            var frame = new CameraMoveFrame(_config);
            frame.SetYaw(37f);
            Assert.That(frame.ToWorld(new Vector2(0.3f, 0.4f)).magnitude, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Orbit_StartsBehindCharacter_AtConfiguredDistance()
        {
            var orbit = new OrbitFollow(_config);
            Vector3 anchor = new Vector3(2f, 1f, 3f);
            orbit.Reset(anchor, Vector3.right);
            orbit.Tick(anchor, Vector3.right, 1f / 60f);

            Assert.That(orbit.TargetYaw, Is.EqualTo(90f).Within(1e-3f));
            Vector3 pivot = anchor + Vector3.up * _config.PivotHeight;
            Vector3 toCamera = orbit.Position - pivot;
            Assert.That(toCamera.x, Is.LessThan(0f), "Камера должна быть за спиной (персонаж смотрит в +X).");
            Assert.That(toCamera.y, Is.GreaterThan(0f), "Камера выше точки вращения.");
            float expected = _config.DistanceForPitch(_config.DefaultPitch);
            Assert.That(toCamera.magnitude, Is.EqualTo(expected).Within(0.05f));
        }

        [Test]
        public void Orbit_MouseRotatesAndPitchIsClamped()
        {
            var orbit = new OrbitFollow(_config);
            orbit.Reset(Vector3.zero, Vector3.forward);
            var look = new FakeLook { Mouse = new Vector2(100f, -100000f) };
            orbit.ApplyLook(look, mouseEnabled: true, 1f / 60f);
            orbit.Tick(Vector3.zero, Vector3.forward, 1f / 60f);

            Assert.That(orbit.TargetYaw, Is.EqualTo(100f * _config.MouseSensitivity.x).Within(1e-3f));
            // Мышь сильно вниз — камера поднимается до предела наклона.
            Assert.That(orbit.Rotation.eulerAngles.x, Is.LessThanOrEqualTo(_config.MaxPitch + 0.5f));
        }

        [Test]
        public void Orbit_IgnoresMouseWhenCursorReleased()
        {
            var orbit = new OrbitFollow(_config);
            orbit.Reset(Vector3.zero, Vector3.forward);
            orbit.ApplyLook(new FakeLook { Mouse = new Vector2(500f, 0f) }, mouseEnabled: false, 1f / 60f);
            Assert.That(orbit.TargetYaw, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Orbit_SnapsWhenTargetJumpsFar()
        {
            var orbit = new OrbitFollow(_config);
            orbit.Reset(Vector3.zero, Vector3.forward);
            orbit.Tick(Vector3.zero, Vector3.forward, 1f / 60f);

            Vector3 far = new Vector3(_config.SnapDistance * 3f, 0f, 0f);
            orbit.Tick(far, Vector3.forward, 1f / 60f);
            Vector3 pivot = far + Vector3.up * _config.PivotHeight;
            Assert.That((orbit.Position - pivot).magnitude,
                Is.EqualTo(_config.DistanceForPitch(_config.DefaultPitch)).Within(0.05f));
        }

        private sealed class FakeLook : IPlayerLookInput
        {
            public Vector2 Mouse;
            public Vector2 Stick;

            public Vector2 ReadMouseDelta()
            {
                return Mouse;
            }

            public Vector2 ReadStick()
            {
                return Stick;
            }

            public int ConsumeSpectateSteps()
            {
                return 0;
            }

            public bool ConsumeCursorToggle()
            {
                return false;
            }
        }
    }
}
