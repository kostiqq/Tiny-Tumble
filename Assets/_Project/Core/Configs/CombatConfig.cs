using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Бой без HP: «встряск» и тайминги из раздела 4 GDD.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatConfig", menuName = "Tiny Tumble/Configs/Combat")]
    public sealed class CombatConfig : ScriptableObject
    {
        [Header("Встряск, % за попадание")]
        [SerializeField, Min(0f)] private float _pushShake = 10f;
        [SerializeField, Min(0f)] private float _itemThrowShake = 15f;
        [SerializeField, Min(0f)] private float _playerThrowShake = 20f;

        [Header("Отлёт")]
        [Tooltip("Импульс отлёта при нулевом встряске.")]
        [SerializeField, Min(0f)] private float _baseKnockback = 4f;
        [Tooltip("Добавка к импульсу за каждый процент встряска.")]
        [SerializeField, Min(0f)] private float _knockbackPerShakePercent = 0.08f;

        [Header("Захват и бросок")]
        [SerializeField, Min(0f)] private float _maxThrowCharge = 1f;
        [Tooltip("Замедление несущего тяжёлый предмет, доля от скорости.")]
        [SerializeField, Range(0f, 1f)] private float _heavyCarrySlowdown = 0.3f;
        [SerializeField, Min(0f)] private float _singleGrabBreakTime = 1.5f;
        [SerializeField, Min(0f)] private float _doubleGrabHoldTime = 3f;

        [Header("Парирование")]
        [SerializeField, Min(0f)] private float _parryWindow = 0.2f;
        [SerializeField, Min(0f)] private float _parryMissRecovery = 0.6f;

        public float PushShake => _pushShake;
        public float ItemThrowShake => _itemThrowShake;
        public float PlayerThrowShake => _playerThrowShake;
        public float BaseKnockback => _baseKnockback;
        public float KnockbackPerShakePercent => _knockbackPerShakePercent;
        public float MaxThrowCharge => _maxThrowCharge;
        public float HeavyCarrySlowdown => _heavyCarrySlowdown;
        public float SingleGrabBreakTime => _singleGrabBreakTime;
        public float DoubleGrabHoldTime => _doubleGrabHoldTime;
        public float ParryWindow => _parryWindow;
        public float ParryMissRecovery => _parryMissRecovery;
    }
}
