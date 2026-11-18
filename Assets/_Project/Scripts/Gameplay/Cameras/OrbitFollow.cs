using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.Gameplay.Cameras
{
    /// <summary>
    /// Орбитальная камера за целью: поворот мышью и стиком, следование с запаздыванием и упреждением,
    /// подъезд к цели, если обзор закрыт. Только расчёт позы; кому её отдать, решает <see cref="MatchCamera"/>.
    /// </summary>
    public sealed class OrbitFollow
    {
        private const int ObstacleBufferSize = 16;

        private readonly CameraConfig _config;
        private readonly RaycastHit[] _hits = new RaycastHit[ObstacleBufferSize];

        private float _targetYaw;
        private float _targetPitch;
        private float _yaw;
        private float _pitch;
        private float _yawVelocity;
        private float _pitchVelocity;
        private float _stickRamp;
        private float _idleTime;

        private Vector3 _pivot;
        private Vector3 _pivotVelocity;
        private Vector3 _lookAhead;
        private Vector3 _lookAheadVelocity;
        private Vector3 _lastAnchor;
        private float _distance;
        private float _distanceVelocity;
        private bool _hasPose;

        public OrbitFollow(CameraConfig config)
        {
            _config = config;
        }

        /// <summary>Куда игрок повернул камеру, градусы. Без сглаживания: по нему считается «вперёд» для стика.</summary>
        public float TargetYaw => _targetYaw;

        public bool HasPose => _hasPose;

        public Vector3 Position { get; private set; }

        public Quaternion Rotation { get; private set; } = Quaternion.identity;

        /// <summary>Мгновенно ставит камеру к цели. <paramref name="facing"/> — куда смотрит персонаж.</summary>
        public void Reset(Vector3 anchor, Vector3 facing)
        {
            float yaw = _config.InitialYaw;
            Vector3 flat = new Vector3(facing.x, 0f, facing.z);
            if (_config.StartBehindCharacter && flat.sqrMagnitude > 0.0001f)
            {
                yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            }

            _targetYaw = yaw;
            _yaw = yaw;
            _targetPitch = Mathf.Clamp(_config.DefaultPitch, _config.MinPitch, _config.MaxPitch);
            _pitch = _targetPitch;
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
            _stickRamp = 0f;
            _idleTime = 0f;
            _distance = _config.DistanceForPitch(_pitch);
            _distanceVelocity = 0f;
            Snap(anchor);
            _hasPose = false;
        }

        /// <summary>Переносит камеру к цели сразу, не меняя поворот (возрождение, далёкая смена цели).</summary>
        public void Snap(Vector3 anchor)
        {
            _pivot = anchor + Vector3.up * _config.PivotHeight;
            _pivotVelocity = Vector3.zero;
            _lookAhead = Vector3.zero;
            _lookAheadVelocity = Vector3.zero;
            _lastAnchor = anchor;
        }

        /// <summary>
        /// Цель сменилась (наблюдение): камера плавно переезжает, если цель недалеко, иначе переносится сразу.
        /// Без этого скачок между целями выглядел бы как огромная скорость и дал бы рывок упреждения.
        /// </summary>
        public void Retarget(Vector3 anchor)
        {
            _lastAnchor = anchor;
            _lookAhead = Vector3.zero;
            _lookAheadVelocity = Vector3.zero;
        }

        /// <summary>Поворот от устройств игрока за кадр. <paramref name="mouseEnabled"/> — курсор захвачен.</summary>
        public void ApplyLook(IPlayerLookInput look, bool mouseEnabled, float deltaTime)
        {
            if (look == null)
            {
                _idleTime += deltaTime;
                return;
            }

            float yawDelta = 0f;
            float pitchDelta = 0f;

            Vector2 mouse = look.ReadMouseDelta();
            if (mouseEnabled && mouse != Vector2.zero)
            {
                Vector2 sensitivity = _config.MouseSensitivity;
                yawDelta += mouse.x * sensitivity.x;
                // Мышь вверх — взгляд вверх, то есть камера опускается и наклон уменьшается.
                pitchDelta -= (_config.InvertMouseY ? -mouse.y : mouse.y) * sensitivity.y;
            }

            Vector2 stick = StickResponse.Apply(look.ReadStick(), _config.StickDeadZone, _config.StickOuterZone, _config.StickResponseExponent);
            if (stick != Vector2.zero)
            {
                float acceleration = _config.StickAccelerationTime;
                _stickRamp = acceleration > 0f ? Mathf.Min(1f, _stickRamp + deltaTime / acceleration) : 1f;
                Vector2 speed = _config.StickSpeed * (_stickRamp * deltaTime);
                yawDelta += stick.x * speed.x;
                pitchDelta -= (_config.InvertStickY ? -stick.y : stick.y) * speed.y;
            }
            else
            {
                _stickRamp = 0f;
            }

            if (yawDelta != 0f || pitchDelta != 0f)
            {
                _idleTime = 0f;
                _targetYaw = Mathf.Repeat(_targetYaw + yawDelta + 180f, 360f) - 180f;
                _targetPitch = Mathf.Clamp(_targetPitch + pitchDelta, _config.MinPitch, _config.MaxPitch);
            }
            else
            {
                _idleTime += deltaTime;
            }
        }

        /// <summary>
        /// Пересчитывает позу. <paramref name="anchor"/> — центр тела цели, <paramref name="facing"/> — куда она смотрит.
        /// </summary>
        public void Tick(Vector3 anchor, Vector3 facing, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // Скорость цели по сдвигу между кадрами: у чужих персонажей на клиенте тело кинематическое, скорости у него нет.
            Vector3 velocity = (anchor - _lastAnchor) / deltaTime;
            velocity.y = 0f;
            _lastAnchor = anchor;

            UpdateRecenter(velocity, facing, deltaTime);

            // Сначала зажим: наклон могли сузить в инспекторе на лету.
            _targetPitch = Mathf.Clamp(_targetPitch, _config.MinPitch, _config.MaxPitch);
            float smooth = _config.RotationSmoothTime;
            if (smooth > 0f && _hasPose)
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, smooth, Mathf.Infinity, deltaTime);
                _pitch = Mathf.SmoothDamp(_pitch, _targetPitch, ref _pitchVelocity, smooth, Mathf.Infinity, deltaTime);
            }
            else
            {
                _yaw = _targetYaw;
                _pitch = _targetPitch;
            }

            Vector3 lookAheadTarget = Vector3.ClampMagnitude(velocity * _config.LookAheadTime, _config.MaxLookAhead);
            _lookAhead = Vector3.SmoothDamp(_lookAhead, lookAheadTarget, ref _lookAheadVelocity, _config.LookAheadSmoothTime, Mathf.Infinity, deltaTime);

            Vector3 desiredPivot = anchor + Vector3.up * _config.PivotHeight + _lookAhead;
            if ((desiredPivot - _pivot).sqrMagnitude > _config.SnapDistance * _config.SnapDistance)
            {
                Snap(anchor);
                desiredPivot = _pivot;
            }

            _pivot = FollowPivot(desiredPivot, deltaTime);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = _pivot + rotation * Vector3.right * _config.ShoulderOffset;
            Vector3 back = rotation * Vector3.back;

            float wanted = _config.DistanceForPitch(_pitch);
            float allowed = LimitByObstacles(pivot, back, wanted);
            if (allowed < _distance || !_hasPose)
            {
                // Подъезжаем сразу, иначе стена на секунду окажется перед камерой.
                _distance = allowed;
                _distanceVelocity = 0f;
            }
            else
            {
                _distance = Mathf.SmoothDamp(_distance, allowed, ref _distanceVelocity, _config.ObstacleReleaseTime, Mathf.Infinity, deltaTime);
            }

            Position = pivot + back * _distance;
            Rotation = rotation;
            _hasPose = true;
        }

        private void UpdateRecenter(Vector3 velocity, Vector3 facing, float deltaTime)
        {
            if (!_config.AutoRecenter || _idleTime < _config.RecenterDelay)
            {
                return;
            }

            float minSpeed = _config.RecenterMinSpeed;
            if (velocity.sqrMagnitude < minSpeed * minSpeed)
            {
                return;
            }

            Vector3 flat = new Vector3(facing.x, 0f, facing.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float behind = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            _targetYaw = Mathf.MoveTowardsAngle(_targetYaw, behind, _config.RecenterSpeed * deltaTime);
        }

        private Vector3 FollowPivot(Vector3 desired, float deltaTime)
        {
            Vector3 current = _pivot;
            var horizontal = new Vector2(current.x, current.z);
            var horizontalVelocity = new Vector2(_pivotVelocity.x, _pivotVelocity.z);
            float verticalVelocity = _pivotVelocity.y;

            if (_config.FollowDampingHorizontal > 0f)
            {
                horizontal = Vector2.SmoothDamp(horizontal, new Vector2(desired.x, desired.z), ref horizontalVelocity,
                    _config.FollowDampingHorizontal, Mathf.Infinity, deltaTime);
            }
            else
            {
                horizontal = new Vector2(desired.x, desired.z);
                horizontalVelocity = Vector2.zero;
            }

            float y = desired.y;
            if (_config.FollowDampingVertical > 0f)
            {
                y = Mathf.SmoothDamp(current.y, desired.y, ref verticalVelocity, _config.FollowDampingVertical, Mathf.Infinity, deltaTime);
            }
            else
            {
                verticalVelocity = 0f;
            }

            _pivotVelocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.y);
            return new Vector3(horizontal.x, y, horizontal.y);
        }

        private float LimitByObstacles(Vector3 pivot, Vector3 back, float wanted)
        {
            if (!_config.AvoidObstacles || wanted <= 0f)
            {
                return wanted;
            }

            int count = Physics.SphereCastNonAlloc(pivot, _config.ObstacleRadius, back, _hits, wanted,
                _config.ObstacleLayers, QueryTriggerInteraction.Ignore);

            float nearest = wanted;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                // distance 0 — сфера начала внутри коллайдера (тело самой цели): такие касания не считаем.
                if (hit.distance <= 0f || hit.distance >= nearest)
                {
                    continue;
                }

                if (_config.IgnoreRigidbodies && hit.collider.attachedRigidbody != null)
                {
                    continue;
                }

                nearest = hit.distance;
            }

            return Mathf.Clamp(nearest, Mathf.Min(_config.MinObstacleDistance, wanted), wanted);
        }
    }
}
