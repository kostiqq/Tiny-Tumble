using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Управляемое движение персонажа на Rigidbody: разгон, контроль в воздухе, прыжок с «койот-временем»
    /// и буфером нажатия, рывок, склоны и движущиеся опоры (вращающиеся и наклоняемые арены).
    /// Один и тот же код считает движение на хосте и предсказывает его у клиента-владельца:
    /// при одинаковом вводе результат совпадает.
    /// </summary>
    public sealed class CharacterMotor
    {
        private const float ProbeSkin = 0.05f;
        private const float ProbeRadiusScale = 0.9f;
        private const float JumpGroundIgnoreTime = 0.1f;
        private const float MinMoveInput = 0.01f;

        private readonly Rigidbody _body;
        private readonly CharacterConfig _config;
        private readonly float _minGroundDot;

        private Vector3 _facing = Vector3.forward;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _jumpBufferedUntil = float.NegativeInfinity;
        private float _ignoreGroundUntil = float.NegativeInfinity;
        private float _dashReadyTime;
        private float _dashEndTime = float.NegativeInfinity;
        private Vector3 _dashDirection;

        public CharacterMotor(Rigidbody body, CharacterConfig config)
        {
            _body = body;
            _config = config;
            _minGroundDot = Mathf.Cos(config.MaxSlopeAngle * Mathf.Deg2Rad);
        }

        public Vector3 Facing => _facing;

        public bool IsGrounded { get; private set; }

        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>Скорость опоры под ногами (движущаяся платформа), иначе ноль.</summary>
        public Vector3 GroundVelocity { get; private set; }

        public bool IsDashing(float now)
        {
            return now < _dashEndTime;
        }

        public float DashCooldownLeft(float now)
        {
            return Mathf.Max(0f, _dashReadyTime - now);
        }

        public void SetFacing(Vector3 direction)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude > 1e-6f)
            {
                _facing = flat.normalized;
            }
        }

        /// <summary>Сброс после телепорта или смены режима: таймеры прыжка и рывка не тянутся из прошлого.</summary>
        public void ResetTransient()
        {
            _lastGroundedTime = float.NegativeInfinity;
            _jumpBufferedUntil = float.NegativeInfinity;
            _ignoreGroundUntil = float.NegativeInfinity;
            _dashEndTime = float.NegativeInfinity;
        }

        public void ProbeGround(float now)
        {
            IsGrounded = false;
            GroundNormal = Vector3.up;
            GroundVelocity = Vector3.zero;
            if (now < _ignoreGroundUntil)
            {
                return;
            }

            float radius = _config.BodyRadius * ProbeRadiusScale;
            // Луч начинается внутри своей капсулы: такие коллайдеры SphereCast пропускает, себя мы не найдём.
            Vector3 origin = _body.position + Vector3.up * (_config.BodyRadius + ProbeSkin);
            float distance = _config.BodyRadius - radius + ProbeSkin + _config.GroundProbeDistance;
            if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (Vector3.Dot(hit.normal, Vector3.up) < _minGroundDot)
            {
                return;
            }

            IsGrounded = true;
            _lastGroundedTime = now;
            GroundNormal = hit.normal;

            Rigidbody platform = hit.rigidbody;
            if (platform != null && platform != _body)
            {
                Vector3 platformVelocity = platform.GetPointVelocity(hit.point);
                GroundVelocity = platformVelocity;

                // Вращающаяся опора поворачивает и персонажа, иначе он «буксует» на месте.
                float yaw = platform.angularVelocity.y * Mathf.Rad2Deg * Time.fixedDeltaTime;
                if (Mathf.Abs(yaw) > 0.001f)
                {
                    _facing = Quaternion.Euler(0f, yaw, 0f) * _facing;
                }
            }
        }

        /// <param name="speedMultiplier">Замедление (переноска тяжёлого) — доля от обычной скорости.</param>
        /// <param name="allowDash">Рывок доступен только в обычном состоянии.</param>
        public void Step(in PlayerInputFrame frame, float now, float deltaTime, float speedMultiplier, bool allowDash)
        {
            ProbeGround(now);

            var move = new Vector3(frame.Move.x, 0f, frame.Move.y);
            if (move.sqrMagnitude > MinMoveInput * MinMoveInput)
            {
                Vector3 desired = move.normalized;
                _facing = Vector3.RotateTowards(_facing, desired, _config.TurnSpeed * Mathf.Deg2Rad * deltaTime, 0f);
            }

            if (frame.WasPressed(PlayerButtons.Jump))
            {
                _jumpBufferedUntil = now + _config.JumpBuffer;
            }

            Vector3 velocity = _body.linearVelocity;

            if (allowDash && frame.WasPressed(PlayerButtons.Dash) && now >= _dashReadyTime && !IsDashing(now))
            {
                _dashDirection = move.sqrMagnitude > MinMoveInput * MinMoveInput ? move.normalized : _facing;
                _facing = _dashDirection;
                _dashEndTime = now + _config.DashDuration;
                _dashReadyTime = now + _config.DashCooldown;
            }

            if (IsDashing(now))
            {
                velocity.x = _dashDirection.x * _config.DashSpeed + GroundVelocity.x;
                velocity.z = _dashDirection.z * _config.DashSpeed + GroundVelocity.z;
            }
            else
            {
                Vector3 target = move * (_config.MoveSpeed * speedMultiplier) + new Vector3(GroundVelocity.x, 0f, GroundVelocity.z);
                float acceleration = IsGrounded ? _config.Acceleration : _config.AirAcceleration;
                float blend = 1f - Mathf.Exp(-acceleration * deltaTime);

                if (IsGrounded)
                {
                    // На склоне идём вдоль поверхности: без проекции вниз по склону персонаж подпрыгивал бы.
                    Vector3 alongSlope = Vector3.ProjectOnPlane(target, GroundNormal);
                    if (alongSlope.sqrMagnitude > 1e-6f)
                    {
                        alongSlope = alongSlope.normalized * target.magnitude;
                    }

                    alongSlope.y += GroundVelocity.y;
                    velocity = Vector3.Lerp(velocity, alongSlope, blend);
                }
                else
                {
                    velocity.x = Mathf.Lerp(velocity.x, target.x, blend);
                    velocity.z = Mathf.Lerp(velocity.z, target.z, blend);
                }
            }

            bool canJump = now - _lastGroundedTime <= _config.CoyoteTime;
            if (now <= _jumpBufferedUntil && canJump)
            {
                velocity.y = _config.JumpVelocity + Mathf.Max(0f, GroundVelocity.y);
                _jumpBufferedUntil = float.NegativeInfinity;
                _lastGroundedTime = float.NegativeInfinity;
                // Сразу после отрыва проба ещё видит пол: без паузы прыжок «прилипал» бы обратно.
                _ignoreGroundUntil = now + JumpGroundIgnoreTime;
                IsGrounded = false;
            }

            if (IsGrounded)
            {
                // Гравитация вдоль склона: без компенсации стоящий персонаж медленно съезжал бы.
                velocity -= Vector3.ProjectOnPlane(Physics.gravity, GroundNormal) * deltaTime;
            }
            else
            {
                ApplyFallGravity(ref velocity, deltaTime);
            }

            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            _body.MoveRotation(Quaternion.LookRotation(_facing, Vector3.up));
        }

        /// <summary>Падение тяжелее подъёма: прыжок и отлёт читаются чётче. Используется и без управления.</summary>
        public void ApplyFallGravity(ref Vector3 velocity, float deltaTime)
        {
            if (velocity.y < 0f)
            {
                velocity += Physics.gravity * ((_config.FallGravityMultiplier - 1f) * deltaTime);
            }
        }
    }
}
