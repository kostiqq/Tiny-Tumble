using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Бой без HP: «встряск», отлёт и тайминги из раздела 4 GDD. Скорости — в м/с, время — в секундах.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatConfig", menuName = "Tiny Tumble/Configs/Combat")]
    public sealed class CombatConfig : ScriptableObject
    {
        [Header("Встряск, % за попадание")]
        [Tooltip("Цели толчка.")]
        [SerializeField, Min(0f)] private float _pushShake = 10f;
        [Tooltip("Тому, в кого попал брошенный предмет.")]
        [SerializeField, Min(0f)] private float _itemThrowShake = 15f;
        [Tooltip("Брошенному игроку и тому, в кого он врезался.")]
        [SerializeField, Min(0f)] private float _playerThrowShake = 20f;
        [Tooltip("Потолок встряска, чтобы отлёт не рос бесконечно.")]
        [SerializeField, Min(0f)] private float _maxShake = 300f;

        [Header("Отлёт")]
        [Tooltip("Скорость отлёта при нулевом встряске.")]
        [SerializeField, Min(0f)] private float _baseKnockback = 4f;
        [Tooltip("Добавка к скорости отлёта за каждый процент встряска.")]
        [SerializeField, Min(0f)] private float _knockbackPerShakePercent = 0.08f;
        [Tooltip("Доля скорости отлёта, направленная вверх: без неё трение гасит толчок слишком быстро.")]
        [SerializeField, Range(0f, 1f)] private float _knockbackLift = 0.35f;
        [Tooltip("С этого встряска (после попадания) удар валит в ragdoll; слабее — только отшатывает.")]
        [SerializeField, Min(0f)] private float _ragdollShakeThreshold = 30f;
        [Tooltip("Сколько отшатнувшийся не управляет персонажем.")]
        [SerializeField, Min(0f)] private float _staggerTime = 0.3f;

        [Header("Толчок")]
        [Tooltip("Замах до начала окна попадания.")]
        [SerializeField, Min(0f)] private float _pushWindup = 0.06f;
        [Tooltip("Окно, в котором толчок попадает.")]
        [SerializeField, Min(0.01f)] private float _pushActiveTime = 0.14f;
        [SerializeField, Min(0f)] private float _pushCooldown = 0.45f;
        [Tooltip("Центр зоны попадания перед персонажем, м.")]
        [SerializeField, Min(0f)] private float _pushReach = 0.7f;
        [SerializeField, Min(0.01f)] private float _pushRadius = 0.55f;
        [Tooltip("Скорость, которую толчок сообщает предметам.")]
        [SerializeField, Min(0f)] private float _pushItemSpeed = 6f;

        [Header("Захват и бросок")]
        [Tooltip("Центр зоны захвата перед персонажем, м.")]
        [SerializeField, Min(0f)] private float _grabReach = 0.65f;
        [SerializeField, Min(0.01f)] private float _grabRadius = 0.6f;
        [Tooltip("Насколько выше головы держится схваченное, м.")]
        [SerializeField, Min(0f)] private float _holdHeight = 0.25f;
        [SerializeField, Min(0f)] private float _holdForward = 0.15f;
        [Tooltip("Сколько держать кнопку толчка для броска в полную силу.")]
        [SerializeField, Min(0f)] private float _maxThrowCharge = 1f;
        [SerializeField, Min(0f)] private float _minThrowSpeed = 6f;
        [SerializeField, Min(0f)] private float _maxThrowSpeed = 14f;
        [Tooltip("Доля скорости броска вверх.")]
        [SerializeField, Range(0f, 1f)] private float _throwLift = 0.3f;
        [Tooltip("Скорость, с которой «Захват» по удерживаемому просто кладёт его перед собой.")]
        [SerializeField, Min(0f)] private float _dropSpeed = 1.5f;
        [Tooltip("Замедление несущего тяжёлый предмет или игрока, доля от скорости.")]
        [SerializeField, Range(0f, 1f)] private float _heavyCarrySlowdown = 0.3f;
        [Tooltip("Сколько один игрок может держать другого: потом тот вырывается.")]
        [SerializeField, Min(0f)] private float _singleGrabBreakTime = 1.5f;
        [Tooltip("Цепочка захвата: вдвоём держат дольше. Отсчёт от первого захвата.")]
        [SerializeField, Min(0f)] private float _doubleGrabHoldTime = 3f;
        [Tooltip("Брошенное ещё столько не сталкивается с бросившим.")]
        [SerializeField, Min(0f)] private float _throwIgnoreTime = 0.3f;
        [Tooltip("Брошенное попадает по игрокам, пока быстрее этого.")]
        [SerializeField, Min(0f)] private float _thrownHitMinSpeed = 3f;
        [Tooltip("Брошенное перестаёт считаться снарядом через это время, даже если ещё летит.")]
        [SerializeField, Min(0f)] private float _thrownLifetime = 1.5f;

        [Header("Парирование")]
        [SerializeField, Min(0f)] private float _parryWindow = 0.2f;
        [Tooltip("Промах парирования: столько нельзя толкаться, хватать и парировать снова.")]
        [SerializeField, Min(0f)] private float _parryMissRecovery = 0.6f;
        [Tooltip("Встряск, который получает отбитый атакующий.")]
        [SerializeField, Min(0f)] private float _parryReflectShake = 0f;

        [Header("Эмоции")]
        [SerializeField, Min(0f)] private float _emoteCooldown = 1f;
        [SerializeField, Min(0.01f)] private float _emoteDuration = 0.8f;

        [Header("Сочность")]
        [Tooltip("Хит-стоп за 1 м/с скорости отлёта.")]
        [SerializeField, Min(0f)] private float _hitStopPerKnockback = 0.008f;
        [SerializeField, Min(0f)] private float _maxHitStop = 0.12f;
        [Tooltip("Сила тряски камеры за 1 м/с скорости отлёта.")]
        [SerializeField, Min(0f)] private float _cameraShakePerKnockback = 0.06f;
        [SerializeField, Min(0f)] private float _maxCameraShake = 1.2f;

        public float PushShake => _pushShake;
        public float ItemThrowShake => _itemThrowShake;
        public float PlayerThrowShake => _playerThrowShake;
        public float MaxShake => _maxShake;
        public float BaseKnockback => _baseKnockback;
        public float KnockbackPerShakePercent => _knockbackPerShakePercent;
        public float KnockbackLift => _knockbackLift;
        public float RagdollShakeThreshold => _ragdollShakeThreshold;
        public float StaggerTime => _staggerTime;
        public float PushWindup => _pushWindup;
        public float PushActiveTime => _pushActiveTime;
        public float PushCooldown => _pushCooldown;
        public float PushReach => _pushReach;
        public float PushRadius => _pushRadius;
        public float PushItemSpeed => _pushItemSpeed;
        public float GrabReach => _grabReach;
        public float GrabRadius => _grabRadius;
        public float HoldHeight => _holdHeight;
        public float HoldForward => _holdForward;
        public float MaxThrowCharge => _maxThrowCharge;
        public float MinThrowSpeed => _minThrowSpeed;
        public float MaxThrowSpeed => _maxThrowSpeed;
        public float ThrowLift => _throwLift;
        public float DropSpeed => _dropSpeed;
        public float HeavyCarrySlowdown => _heavyCarrySlowdown;
        public float SingleGrabBreakTime => _singleGrabBreakTime;
        public float DoubleGrabHoldTime => _doubleGrabHoldTime;
        public float ThrowIgnoreTime => _throwIgnoreTime;
        public float ThrownHitMinSpeed => _thrownHitMinSpeed;
        public float ThrownLifetime => _thrownLifetime;
        public float ParryWindow => _parryWindow;
        public float ParryMissRecovery => _parryMissRecovery;
        public float ParryReflectShake => _parryReflectShake;
        public float EmoteCooldown => _emoteCooldown;
        public float EmoteDuration => _emoteDuration;
        public float HitStopPerKnockback => _hitStopPerKnockback;
        public float MaxHitStop => _maxHitStop;
        public float CameraShakePerKnockback => _cameraShakePerKnockback;
        public float MaxCameraShake => _maxCameraShake;
    }
}
