using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Движение, ragdoll и «желе» персонажа. Общий для всех: персонажи — косметика, а не классы (раздел 8 GDD).
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "Tiny Tumble/Configs/Character")]
    public sealed class CharacterConfig : ScriptableObject
    {
        [Header("Тело")]
        [Tooltip("Рост капсулы, м. Pivot персонажа — у ног.")]
        [SerializeField, Min(0.01f)] private float _characterHeight = 1f;
        [SerializeField, Min(0.01f)] private float _bodyRadius = 0.3f;
        [SerializeField, Min(0.01f)] private float _mass = 1f;

        [Header("Движение")]
        [SerializeField, Min(0f)] private float _moveSpeed = 5f;
        [Tooltip("Насколько быстро скорость на земле догоняет желаемую, 1/сек.")]
        [SerializeField, Min(0f)] private float _acceleration = 12f;
        [Tooltip("То же в воздухе: контроль в прыжке слабее, чем на земле.")]
        [SerializeField, Min(0f)] private float _airAcceleration = 5f;
        [Tooltip("Скорость разворота лицом к направлению движения, град/сек.")]
        [SerializeField, Min(0f)] private float _turnSpeed = 900f;
        [Tooltip("Склоны круче этого угла не считаются опорой: с них съезжают.")]
        [SerializeField, Range(0f, 89f)] private float _maxSlopeAngle = 45f;

        [Header("Прыжок")]
        [SerializeField, Min(0f)] private float _jumpVelocity = 6f;
        [Tooltip("Сколько после схода с края ещё можно прыгнуть.")]
        [SerializeField, Min(0f)] private float _coyoteTime = 0.1f;
        [Tooltip("Нажатие прыжка чуть раньше приземления срабатывает при касании.")]
        [SerializeField, Min(0f)] private float _jumpBuffer = 0.12f;
        [Tooltip("Множитель гравитации при падении: прыжок короче и «тяжелее» на спуске.")]
        [SerializeField, Min(1f)] private float _fallGravityMultiplier = 1.8f;
        [Tooltip("Насколько ниже ног ещё ищется опора.")]
        [SerializeField, Min(0f)] private float _groundProbeDistance = 0.12f;

        [Header("Рывок")]
        [SerializeField, Min(0f)] private float _dashCooldown = 3f;
        [Tooltip("Дистанция рывка в ростах персонажа (GDD: ~2).")]
        [SerializeField, Min(0f)] private float _dashDistanceInHeights = 2f;
        [SerializeField, Min(0.01f)] private float _dashDuration = 0.2f;

        [Header("Ragdoll")]
        [Tooltip("Дольше этого персонаж не лежит, даже если ещё катится.")]
        [SerializeField, Min(0f)] private float _maxRagdollTime = 1f;
        [Tooltip("Меньше этого не лежит: падение должно читаться.")]
        [SerializeField, Min(0f)] private float _minRagdollTime = 0.35f;
        [Tooltip("Ниже этой скорости лежащий считается успокоившимся и встаёт.")]
        [SerializeField, Min(0f)] private float _ragdollSettleSpeed = 1.2f;
        [Tooltip("Сколько длится подъём (возврат в вертикаль).")]
        [SerializeField, Min(0.01f)] private float _getUpDuration = 0.25f;
        [SerializeField, Min(0f)] private float _grabImmunityAfterRagdoll = 1f;

        [Header("Желе (визуал)")]
        [Tooltip("Жёсткость пружины наклона и сплющивания.")]
        [SerializeField, Min(0f)] private float _wobbleStiffness = 180f;
        [Tooltip("Затухание пружины: меньше — дольше качается.")]
        [SerializeField, Min(0f)] private float _wobbleDamping = 9f;
        [Tooltip("Наклон в градусах на 1 м/с² ускорения.")]
        [SerializeField, Min(0f)] private float _leanPerAcceleration = 1.6f;
        [SerializeField, Range(0f, 60f)] private float _maxLean = 25f;
        [Tooltip("Сплющивание при приземлении на 1 м/с вертикальной скорости.")]
        [SerializeField, Min(0f)] private float _squashPerLandingSpeed = 0.05f;
        [SerializeField, Range(0f, 0.8f)] private float _maxSquash = 0.35f;
        [Tooltip("За сколько секунд визуал догоняет тело после сетевой коррекции.")]
        [SerializeField, Min(0.01f)] private float _visualCorrectionTime = 0.12f;

        public float CharacterHeight => _characterHeight;
        public float BodyRadius => _bodyRadius;
        public float Mass => _mass;
        public float MoveSpeed => _moveSpeed;
        public float Acceleration => _acceleration;
        public float AirAcceleration => _airAcceleration;
        public float TurnSpeed => _turnSpeed;
        public float MaxSlopeAngle => _maxSlopeAngle;
        public float JumpVelocity => _jumpVelocity;
        public float CoyoteTime => _coyoteTime;
        public float JumpBuffer => _jumpBuffer;
        public float FallGravityMultiplier => _fallGravityMultiplier;
        public float GroundProbeDistance => _groundProbeDistance;
        public float DashCooldown => _dashCooldown;
        public float DashDistance => _dashDistanceInHeights * _characterHeight;
        public float DashDuration => _dashDuration;
        public float DashSpeed => DashDistance / _dashDuration;
        public float MaxRagdollTime => _maxRagdollTime;
        public float MinRagdollTime => Mathf.Min(_minRagdollTime, _maxRagdollTime);
        public float RagdollSettleSpeed => _ragdollSettleSpeed;
        public float GetUpDuration => _getUpDuration;
        public float GrabImmunityAfterRagdoll => _grabImmunityAfterRagdoll;
        public float WobbleStiffness => _wobbleStiffness;
        public float WobbleDamping => _wobbleDamping;
        public float LeanPerAcceleration => _leanPerAcceleration;
        public float MaxLean => _maxLean;
        public float SquashPerLandingSpeed => _squashPerLandingSpeed;
        public float MaxSquash => _maxSquash;
        public float VisualCorrectionTime => _visualCorrectionTime;
    }
}
