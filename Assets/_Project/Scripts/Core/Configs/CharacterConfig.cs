using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Движение, равновесие, позы и ragdoll физического персонажа. Общий для всех: персонажи — косметика,
    /// а не классы (раздел 8 GDD). Размеры, доли масс и лимиты суставов конкретной модели — в <see cref="CharacterProfile"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "Tiny Tumble/Configs/Character")]
    public sealed class CharacterConfig : ScriptableObject
    {
        [Header("Тело")]
        [Tooltip("Масса всего персонажа, кг; делится между костями по долям из профиля модели.")]
        [SerializeField, Min(0.01f)] private float _mass = 1f;

        [Header("Движение")]
        [SerializeField, Min(0f)] private float _moveSpeed = 5f;
        [Tooltip("Насколько быстро скорость на земле догоняет желаемую, 1/сек.")]
        [SerializeField, Min(0f)] private float _acceleration = 12f;
        [Tooltip("То же в воздухе: контроль в прыжке слабее, чем на земле.")]
        [SerializeField, Min(0f)] private float _airAcceleration = 5f;
        [Tooltip("Предел ускорения от мотора, м/с². Ниже — тело «вязнет» и сильнее раскачивается при разгоне.")]
        [SerializeField, Min(0f)] private float _maxMoveAcceleration = 60f;
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
        [Tooltip("Насколько ниже обычной высоты таза ещё ищется опора, м.")]
        [SerializeField, Min(0f)] private float _groundProbeDistance = 0.12f;

        [Header("Рывок")]
        [SerializeField, Min(0f)] private float _dashCooldown = 3f;
        [Tooltip("Дистанция рывка, м (GDD: ~2 роста).")]
        [SerializeField, Min(0f)] private float _dashDistance = 2f;
        [SerializeField, Min(0.01f)] private float _dashDuration = 0.2f;

        [Header("Равновесие")]
        [Tooltip("Частота пружины, которая держит таз и грудь вертикально и разворачивает их, рад/с. Выше — твёрже стоит.")]
        [SerializeField, Min(0f)] private float _balanceFrequency = 16f;
        [Tooltip("Затухание пружины равновесия, доля от критического. Меньше 1 — покачивается после толчка.")]
        [SerializeField, Range(0f, 2f)] private float _balanceDamping = 0.5f;
        [Tooltip("Сила равновесия в Stagger, доля от обычной: отшатнулся и шатается.")]
        [SerializeField, Range(0f, 1f)] private float _staggerBalance = 0.3f;
        [Tooltip("Частота пружины, которая держит таз на высоте над опорой (ноги «пружинят»), рад/с.")]
        [SerializeField, Min(0f)] private float _rideFrequency = 12f;
        [Tooltip("Затухание пружины высоты, доля от критического.")]
        [SerializeField, Range(0f, 2f)] private float _rideDamping = 0.6f;

        [Header("Позы")]
        [Tooltip("Частота приводов суставов, которые тянут кости к процедурной позе, рад/с.")]
        [SerializeField, Min(0f)] private float _poseFrequency = 30f;
        [Tooltip("Затухание приводов суставов, доля от критического.")]
        [SerializeField, Range(0f, 2f)] private float _poseDamping = 0.7f;
        [Tooltip("Сила приводов в ragdoll, доля от обычной: тело обмякает, но не разваливается.")]
        [SerializeField, Range(0f, 1f)] private float _limpPose = 0.06f;
        [Tooltip("Сила приводов, пока персонажа держат: болтается.")]
        [SerializeField, Range(0f, 1f)] private float _grabbedPose = 0.25f;
        [Tooltip("Путь за полный цикл шагов (левой и правой), м.")]
        [SerializeField, Min(0.05f)] private float _strideLength = 0.7f;
        [Tooltip("Не больше стольких циклов шага в секунду: у коротких ног на полной скорости шаг иначе стал бы чаще, "
            + "чем успевают приводы суставов, и конечности болтались бы как попало.")]
        [SerializeField, Min(0.1f)] private float _maxStepRate = 2f;
        [Tooltip("Размах ноги вперёд-назад на полной скорости, град.")]
        [SerializeField, Range(0f, 80f)] private float _stepAngle = 30f;
        [Tooltip("Сгиб колена при выносе ноги, град.")]
        [SerializeField, Range(0f, 120f)] private float _kneeBend = 45f;
        [Tooltip("Размах рук при ходьбе, град.")]
        [SerializeField, Range(0f, 80f)] private float _armSwingAngle = 25f;

        [Header("Ragdoll")]
        [Tooltip("Дольше этого персонаж не лежит, даже если ещё катится.")]
        [SerializeField, Min(0f)] private float _maxRagdollTime = 1f;
        [Tooltip("Меньше этого не лежит: падение должно читаться.")]
        [SerializeField, Min(0f)] private float _minRagdollTime = 0.35f;
        [Tooltip("Ниже этой скорости лежащий считается успокоившимся и встаёт.")]
        [SerializeField, Min(0f)] private float _ragdollSettleSpeed = 1.2f;
        [Tooltip("За сколько секунд равновесие и позы набирают силу при подъёме.")]
        [SerializeField, Min(0.01f)] private float _getUpDuration = 0.6f;
        [Tooltip("Если за это время не выпрямился, подъём всё равно заканчивается (на всякий случай).")]
        [SerializeField, Min(0.01f)] private float _maxGetUpTime = 1.5f;
        [SerializeField, Min(0f)] private float _grabImmunityAfterRagdoll = 1f;

        public float Mass => _mass;
        public float MoveSpeed => _moveSpeed;
        public float Acceleration => _acceleration;
        public float AirAcceleration => _airAcceleration;
        public float MaxMoveAcceleration => _maxMoveAcceleration;
        public float TurnSpeed => _turnSpeed;
        public float MaxSlopeAngle => _maxSlopeAngle;
        public float JumpVelocity => _jumpVelocity;
        public float CoyoteTime => _coyoteTime;
        public float JumpBuffer => _jumpBuffer;
        public float FallGravityMultiplier => _fallGravityMultiplier;
        public float GroundProbeDistance => _groundProbeDistance;
        public float DashCooldown => _dashCooldown;
        public float DashDistance => _dashDistance;
        public float DashDuration => _dashDuration;
        public float DashSpeed => _dashDistance / _dashDuration;
        public float BalanceFrequency => _balanceFrequency;
        public float BalanceDamping => _balanceDamping;
        public float StaggerBalance => _staggerBalance;
        public float RideFrequency => _rideFrequency;
        public float RideDamping => _rideDamping;
        public float PoseFrequency => _poseFrequency;
        public float PoseDamping => _poseDamping;
        public float LimpPose => _limpPose;
        public float GrabbedPose => _grabbedPose;
        public float StrideLength => _strideLength;
        public float MaxStepRate => _maxStepRate;
        public float StepAngle => _stepAngle;
        public float KneeBend => _kneeBend;
        public float ArmSwingAngle => _armSwingAngle;
        public float MaxRagdollTime => _maxRagdollTime;
        public float MinRagdollTime => Mathf.Min(_minRagdollTime, _maxRagdollTime);
        public float RagdollSettleSpeed => _ragdollSettleSpeed;
        public float GetUpDuration => _getUpDuration;
        public float MaxGetUpTime => Mathf.Max(_maxGetUpTime, _getUpDuration);
        public float GrabImmunityAfterRagdoll => _grabImmunityAfterRagdoll;
    }
}
