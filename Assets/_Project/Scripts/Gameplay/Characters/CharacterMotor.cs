using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Движение физического персонажа (только хост): разгон ускорением всего тела, прыжок с «койот-временем» и буфером,
    /// рывок, склоны и движущиеся опоры. Таз держится над опорой пружиной («ноги»), корпус — вертикально
    /// и лицом к движению пружиной равновесия; кости ниже тянутся за ним суставами, отсюда раскачка.
    /// Скорость всё равно задаётся мотором, поэтому управление отзывчивое, а толчки сбивают с равновесия.
    /// </summary>
    public sealed class CharacterMotor
    {
        private const float JumpGroundIgnoreTime = 0.1f;
        private const float MinMoveInput = 0.01f;
        private const float ProbeRadius = 0.08f;
        private const int ProbeCapacity = 16;

        private readonly RagdollRig _rig;
        private readonly CharacterConfig _config;
        private readonly float _minGroundDot;
        private readonly RaycastHit[] _hits = new RaycastHit[ProbeCapacity];

        private Vector3 _facing = Vector3.forward;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _jumpBufferedUntil = float.NegativeInfinity;
        private float _ignoreGroundUntil = float.NegativeInfinity;
        private float _dashReadyTime;
        private float _dashEndTime = float.NegativeInfinity;
        private Vector3 _dashDirection;

        public CharacterMotor(RagdollRig rig, CharacterConfig config)
        {
            _rig = rig;
            _config = config;
            _minGroundDot = Mathf.Cos(config.MaxSlopeAngle * Mathf.Deg2Rad);
        }

        public Vector3 Facing => _facing;

        public bool IsGrounded { get; private set; }

        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>Скорость опоры под ногами (движущаяся платформа), иначе ноль.</summary>
        public Vector3 GroundVelocity { get; private set; }

        /// <summary>От таза до опоры, м; бесконечность, если опоры нет.</summary>
        public float GroundDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Скорость по горизонтали относительно опоры: по ней шагают ноги.</summary>
        public Vector3 PlanarVelocity
        {
            get
            {
                Vector3 velocity = _rig.Hips.linearVelocity - GroundVelocity;
                velocity.y = 0f;
                return velocity;
            }
        }

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
            GroundDistance = float.PositiveInfinity;
            if (now < _ignoreGroundUntil)
            {
                return;
            }

            // Луч начинается выше таза на высоту ног: даже если таз просел до пола (жёсткое приземление),
            // сфера не стартует внутри стола. Касание на старте луча Unity отдаёт с нулевой дистанцией,
            // его не отличить от своих костей — опора «пропала» бы, и пружина ног не подняла бы таз.
            Rigidbody hips = _rig.Hips;
            float lift = _rig.RideHeight;
            float reach = lift + _rig.RideHeight + _config.GroundProbeDistance;
            int count = Physics.SphereCastNonAlloc(hips.position + Vector3.up * lift, ProbeRadius, Vector3.down, _hits, reach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                // Луч начинается внутри своего таза: свои кости пропускаем.
                if (hit.distance <= 0f || (hit.rigidbody != null && _rig.Owns(hit.rigidbody)))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearest = i;
                }
            }

            if (nearest < 0)
            {
                return;
            }

            RaycastHit ground = _hits[nearest];
            GroundDistance = ground.distance + ProbeRadius - lift;
            if (Vector3.Dot(ground.normal, Vector3.up) < _minGroundDot)
            {
                return;
            }

            IsGrounded = true;
            _lastGroundedTime = now;
            GroundNormal = ground.normal;

            Rigidbody platform = ground.rigidbody;
            if (platform != null)
            {
                GroundVelocity = platform.GetPointVelocity(ground.point);

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
                _facing = Vector3.RotateTowards(_facing, move.normalized, _config.TurnSpeed * Mathf.Deg2Rad * deltaTime, 0f);
            }

            if (frame.WasPressed(PlayerButtons.Jump))
            {
                _jumpBufferedUntil = now + _config.JumpBuffer;
            }

            if (allowDash && frame.WasPressed(PlayerButtons.Dash) && now >= _dashReadyTime && !IsDashing(now))
            {
                _dashDirection = move.sqrMagnitude > MinMoveInput * MinMoveInput ? move.normalized : _facing;
                _facing = _dashDirection;
                _dashEndTime = now + _config.DashDuration;
                _dashReadyTime = now + _config.DashCooldown;
            }

            Rigidbody hips = _rig.Hips;
            Vector3 velocity = hips.linearVelocity;
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 groundPlanar = new Vector3(GroundVelocity.x, 0f, GroundVelocity.z);

            if (IsDashing(now))
            {
                // Рывок задаёт скорость всему телу сразу: иначе конечности отстали бы и «выдернули» таз.
                Vector3 dash = _dashDirection * _config.DashSpeed + groundPlanar;
                _rig.AddVelocity(dash - planar);
            }
            else
            {
                Vector3 target = move * (_config.MoveSpeed * speedMultiplier) + groundPlanar;
                float acceleration = IsGrounded ? _config.Acceleration : _config.AirAcceleration;
                float blend = 1f - Mathf.Exp(-acceleration * deltaTime);
                Vector3 desired = Vector3.Lerp(planar, target, blend);
                Vector3 push = Vector3.ClampMagnitude((desired - planar) / deltaTime, _config.MaxMoveAcceleration);
                // Ускорение всем костям — как сила в центре масс: верхнетяжёлое тело не опрокидывается
                // от разгона, а раскачивается только за счёт податливости суставов.
                _rig.AddAcceleration(push);
            }

            bool canJump = now - _lastGroundedTime <= _config.CoyoteTime;
            if (now <= _jumpBufferedUntil && canJump)
            {
                float jump = _config.JumpVelocity + Mathf.Max(0f, GroundVelocity.y);
                _rig.AddVelocity(Vector3.up * (jump - velocity.y));
                _jumpBufferedUntil = float.NegativeInfinity;
                _lastGroundedTime = float.NegativeInfinity;
                // Сразу после отрыва проба ещё видит пол: без паузы пружина ног вернула бы тело вниз.
                _ignoreGroundUntil = now + JumpGroundIgnoreTime;
                IsGrounded = false;
            }

            Coast();
        }

        /// <summary>Без управления: только тяжёлое падение.</summary>
        public void Coast()
        {
            if (!IsGrounded && _rig.Hips.linearVelocity.y < 0f)
            {
                // Падение тяжелее подъёма: прыжок и отлёт читаются чётче.
                _rig.AddAcceleration(Physics.gravity * (_config.FallGravityMultiplier - 1f));
            }
        }

        /// <summary>
        /// Пружины, которые держат персонажа на ногах: таз на высоте над опорой и корпус (таз, грудь, голова) вертикально лицом
        /// к <see cref="Facing"/>. <paramref name="balance"/> и <paramref name="ride"/> — доля силы (0 — обмяк).
        /// </summary>
        /// <param name="desired">Поза в осях персонажа: из неё берётся наклон груди (поклон, покачивание).</param>
        /// <param name="yawOffset">Доворот вокруг вертикали, град (пируэт).</param>
        public void Support(float balance, float ride, Quaternion[] desired, float yawOffset)
        {
            Rigidbody hips = _rig.Hips;
            if (hips.isKinematic)
            {
                return;
            }

            if (ride > 0f && IsGrounded)
            {
                float frequency = _config.RideFrequency;
                float error = _rig.RideHeight - GroundDistance;
                float relativeSpeed = hips.linearVelocity.y - GroundVelocity.y;
                float spring = frequency * frequency * error - 2f * _config.RideDamping * frequency * relativeSpeed;
                // Ноги держат вес всего тела; тянуть вниз сильнее гравитации пружина не должна.
                // Поддержка всем костям сразу: суставы не провисают под весом, корпус не клонит.
                float lift = Mathf.Max(0f, spring - Physics.gravity.y) * ride;
                _rig.AddAcceleration(Vector3.up * lift);
            }

            if (balance > 0f)
            {
                Quaternion frame = Quaternion.Euler(0f, yawOffset, 0f) * Quaternion.LookRotation(_facing, Vector3.up);
                Vector3 hipsAcceleration = DriveRotation(hips, frame * desired[RagdollBones.Hips], balance);
                // Разворот корпуса передаётся конечностям сразу: приводам суставов остаётся держать позу,
                // а не догонять корпус (иначе на резком развороте руки и ноги отстают и разлетаются).
                Vector3 yaw = Vector3.Project(hipsAcceleration, Vector3.up);
                for (int i = RagdollBones.LeftUpperArm; i < RagdollBones.Count; i++)
                {
                    _rig.Body(i).AddTorque(yaw, ForceMode.Acceleration);
                }

                DriveRotation(_rig.Body(RagdollBones.Chest), frame * desired[RagdollBones.Chest], balance);
                // Голова тяжёлая и висит на суставе: без своей пружины она раскачивает и опрокидывает корпус.
                DriveRotation(_rig.Body(RagdollBones.Head), frame * desired[RagdollBones.Head], balance);
            }
        }

        /// <summary>Пружина-демпфер к целевому повороту; ускорение не зависит от массы и инерции кости.</summary>
        /// <returns>Приложенное угловое ускорение.</returns>
        private Vector3 DriveRotation(Rigidbody body, Quaternion target, float strength)
        {
            Quaternion delta = target * Quaternion.Inverse(body.rotation);
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            delta.ToAngleAxis(out float angle, out Vector3 axis);
            Vector3 error = Vector3.zero;
            if (angle > 1e-3f && !float.IsInfinity(axis.x) && !float.IsNaN(axis.x))
            {
                error = axis.normalized * (angle * Mathf.Deg2Rad);
            }

            float frequency = _config.BalanceFrequency;
            Vector3 acceleration = frequency * frequency * error - 2f * _config.BalanceDamping * frequency * body.angularVelocity;
            acceleration *= strength;
            body.AddTorque(acceleration, ForceMode.Acceleration);
            return acceleration;
        }
    }
}
