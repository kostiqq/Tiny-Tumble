using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Движение персонажа. Общий для всех: персонажи — косметика, а не классы (раздел 8 GDD).
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "Tiny Tumble/Configs/Character")]
    public sealed class CharacterConfig : ScriptableObject
    {
        [Header("Движение")]
        [SerializeField, Min(0f)] private float _moveSpeed = 5f;
        [Tooltip("Насколько быстро скорость догоняет желаемую, 1/сек.")]
        [SerializeField, Min(0f)] private float _acceleration = 12f;
        [SerializeField, Min(0f)] private float _jumpVelocity = 6f;
        [Tooltip("Длина луча вниз для проверки опоры, от центра персонажа.")]
        [SerializeField, Min(0f)] private float _groundCheckDistance = 0.6f;

        [Header("Рывок")]
        [SerializeField, Min(0f)] private float _dashCooldown = 3f;
        [Tooltip("Дистанция рывка в ростах персонажа (GDD: ~2).")]
        [SerializeField, Min(0f)] private float _dashDistanceInHeights = 2f;
        [SerializeField, Min(0.01f)] private float _characterHeight = 1f;
        [SerializeField, Min(0.01f)] private float _dashDuration = 0.2f;

        [Header("Ragdoll")]
        [SerializeField, Min(0f)] private float _maxRagdollTime = 1f;
        [SerializeField, Min(0f)] private float _grabImmunityAfterRagdoll = 1f;

        public float MoveSpeed => _moveSpeed;
        public float Acceleration => _acceleration;
        public float JumpVelocity => _jumpVelocity;
        public float GroundCheckDistance => _groundCheckDistance;
        public float DashCooldown => _dashCooldown;
        public float DashDistance => _dashDistanceInHeights * _characterHeight;
        public float DashDuration => _dashDuration;
        public float DashSpeed => DashDistance / _dashDuration;
        public float MaxRagdollTime => _maxRagdollTime;
        public float GrabImmunityAfterRagdoll => _grabImmunityAfterRagdoll;
    }
}
