using System;
using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Камера матча. Один локальный игрок — камера за его персонажем с вращением мышью или правым стиком
    /// (ориентир — Party Animals); после выбывания — наблюдение за оставшимися. Несколько локальных игроков —
    /// общая камера, которая держит в кадре всех и центр стола.
    /// Поля меняются на лету в Play Mode: камера перечитывает конфиг при каждом изменении в инспекторе.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraConfig", menuName = "Tiny Tumble/Configs/Camera")]
    public sealed class CameraConfig : ScriptableObject
    {
        // ---------- Режим ----------

        [Header("Режим")]
        [Tooltip("Вкл — у одиночного игрока камера за его персонажем. Выкл — всегда общая камера на весь стол. "
            + "В локальном коопе камера общая в любом случае. Переключается и на лету в Play Mode.")]
        [SerializeField] private bool _followLocalPlayer = true;

        // ---------- Камера за игроком: кадр ----------

        [Header("За игроком: кадр")]
        [Tooltip("Вертикальный угол обзора, градусы.")]
        [SerializeField, Range(20f, 100f)] private float _followFieldOfView = 55f;

        [Tooltip("Ближняя плоскость отсечения, м.")]
        [SerializeField, Min(0.01f)] private float _nearClipPlane = 0.1f;

        [Tooltip("Точка, вокруг которой вращается камера: столько метров над центром тела персонажа.")]
        [SerializeField] private float _pivotHeight = 0.5f;

        [Tooltip("Сдвиг точки вращения вбок в осях камеры, м: плюс — персонаж левее центра экрана («через плечо»).")]
        [SerializeField] private float _shoulderOffset = 0f;

        [Tooltip("Расстояние до точки вращения при минимальном наклоне (камера смотрит почти вдоль стола), м.")]
        [SerializeField, Min(0.5f)] private float _distanceAtMinPitch = 6f;

        [Tooltip("Расстояние до точки вращения при максимальном наклоне (камера смотрит сверху), м.")]
        [SerializeField, Min(0.5f)] private float _distanceAtMaxPitch = 9f;

        [Tooltip("Как расстояние меняется между минимальным и максимальным наклоном: 1 — линейно.")]
        [SerializeField, Range(0.2f, 5f)] private float _distanceCurveExponent = 1f;

        // ---------- Камера за игроком: вращение ----------

        [Header("За игроком: наклон и поворот")]
        [Tooltip("Наклон камеры в начале матча, градусы вниз от горизонта.")]
        [SerializeField, Range(-30f, 89f)] private float _defaultPitch = 35f;

        [SerializeField, Range(-30f, 89f)] private float _minPitch = 12f;
        [SerializeField, Range(-30f, 89f)] private float _maxPitch = 70f;

        [Tooltip("В начале матча встать за спиной персонажа. Иначе — поворот из «Начальный поворот».")]
        [SerializeField] private bool _startBehindCharacter = true;

        [Tooltip("Поворот камеры в начале матча, если не за спиной: 0 — смотрит вдоль +Z мира.")]
        [SerializeField, Range(-180f, 180f)] private float _initialYaw = 0f;

        [Tooltip("Сглаживание поворота от мыши и стика, с. 0 — без сглаживания.")]
        [SerializeField, Min(0f)] private float _rotationSmoothTime = 0.04f;

        // ---------- Мышь ----------

        [Header("Мышь")]
        [Tooltip("Градусы поворота на пиксель движения мыши: X — по горизонтали, Y — по вертикали.")]
        [SerializeField] private Vector2 _mouseSensitivity = new Vector2(0.12f, 0.09f);

        [SerializeField] private bool _invertMouseY;

        [Tooltip("Захватывать курсор в режиме камеры за игроком. Escape отпускает и снова захватывает.")]
        [SerializeField] private bool _lockCursor = true;

        // ---------- Стик ----------

        [Header("Правый стик")]
        [Tooltip("Скорость поворота при полностью выжатом стике, градусы в секунду: X — по горизонтали, Y — по вертикали.")]
        [SerializeField] private Vector2 _stickSpeed = new Vector2(200f, 120f);

        [SerializeField] private bool _invertStickY;

        [SerializeField, Range(0f, 0.5f)] private float _stickDeadZone = 0.12f;
        [SerializeField, Range(0.5f, 1f)] private float _stickOuterZone = 0.95f;

        [Tooltip("Кривая отклика: 1 — линейно, больше — точнее на малых отклонениях.")]
        [SerializeField, Range(0.5f, 3f)] private float _stickResponseExponent = 1.6f;

        [Tooltip("За сколько секунд поворот разгоняется до полной скорости. 0 — сразу.")]
        [SerializeField, Min(0f)] private float _stickAccelerationTime = 0.15f;

        // ---------- Следование ----------

        [Header("Следование за целью")]
        [Tooltip("Запаздывание по горизонтали, с (SmoothDamp). 0 — жёстко.")]
        [SerializeField, Min(0f)] private float _followDampingHorizontal = 0.1f;

        [Tooltip("Запаздывание по вертикали, с: больше — меньше качает на прыжках.")]
        [SerializeField, Min(0f)] private float _followDampingVertical = 0.25f;

        [Tooltip("Смотреть вперёд по ходу движения: на сколько секунд движения вперёд сдвигается точка вращения.")]
        [SerializeField, Min(0f)] private float _lookAheadTime = 0.15f;

        [Tooltip("Предел упреждения, м.")]
        [SerializeField, Min(0f)] private float _maxLookAhead = 1.2f;

        [Tooltip("Сглаживание упреждения, с.")]
        [SerializeField, Min(0f)] private float _lookAheadSmoothTime = 0.3f;

        [Tooltip("Если цель за кадр сместилась дальше этого (возрождение, смена цели), камера переносится сразу, м.")]
        [SerializeField, Min(0.5f)] private float _snapDistance = 6f;

        // ---------- Автоповорот ----------

        [Header("Автоповорот за спину")]
        [Tooltip("Если игрок не трогает камеру, она сама плавно встаёт за спину бегущего персонажа.")]
        [SerializeField] private bool _autoRecenter;

        [Tooltip("Сколько секунд без ввода камеры до начала автоповорота.")]
        [SerializeField, Min(0f)] private float _recenterDelay = 2f;

        [Tooltip("Скорость автоповорота, градусы в секунду.")]
        [SerializeField, Min(0f)] private float _recenterSpeed = 90f;

        [Tooltip("Автоповорот только пока персонаж движется быстрее, м/с.")]
        [SerializeField, Min(0f)] private float _recenterMinSpeed = 1f;

        // ---------- Препятствия ----------

        [Header("Препятствия")]
        [Tooltip("Подъезжать ближе, если между персонажем и камерой предмет.")]
        [SerializeField] private bool _avoidObstacles = true;

        [Tooltip("Слои, которые закрывают обзор.")]
        [SerializeField] private LayerMask _obstacleLayers = 1;

        [Tooltip("Не считать препятствием тела с Rigidbody: персонажей и предметы, пробегающие между камерой и игроком.")]
        [SerializeField] private bool _ignoreRigidbodies = true;

        [Tooltip("Радиус «головы» камеры, м: на сколько она держится от стен.")]
        [SerializeField, Min(0.01f)] private float _obstacleRadius = 0.3f;

        [Tooltip("Ближе этого к точке вращения камера не подъезжает, м.")]
        [SerializeField, Min(0f)] private float _minObstacleDistance = 1f;

        [Tooltip("Отъезд назад, когда препятствие ушло, с. Подъезд при появлении препятствия — сразу.")]
        [SerializeField, Min(0f)] private float _obstacleReleaseTime = 0.35f;

        // ---------- Движение ----------

        [Header("Управление")]
        [Tooltip("Стик и WASD двигают персонажа относительно камеры. Выкл — в осях стола.")]
        [SerializeField] private bool _cameraRelativeMovement = true;

        // ---------- Наблюдение ----------

        [Header("Наблюдение после выбывания")]
        [Tooltip("Сколько секунд камера смотрит на место падения, прежде чем перейти к другому персонажу.")]
        [SerializeField, Min(0f)] private float _spectateDelay = 1.2f;

        [Tooltip("Наблюдать и за ботами. Выкл — только за игроками, пока они есть.")]
        [SerializeField] private bool _spectateBots = true;

        // ---------- Общая камера ----------

        [Header("Общая камера (локальный кооп)")]
        [Tooltip("Смещение камеры от центра группы, м.")]
        [SerializeField] private Vector3 _sharedOffset = new Vector3(0f, 11f, -10f);

        [SerializeField, Range(20f, 100f)] private float _sharedFieldOfView = 50f;

        [Tooltip("Доля экрана, которую занимает группа.")]
        [SerializeField, Range(0.1f, 1f)] private float _sharedFramingSize = 0.7f;

        [Tooltip("Насколько камера может приблизиться (минус) и отдалиться (плюс) от смещения, м.")]
        [SerializeField] private Vector2 _sharedDollyRange = new Vector2(-6f, 4f);

        [Tooltip("Сглаживание кадрирования группы, с.")]
        [SerializeField, Min(0f)] private float _sharedDamping = 1.5f;

        [SerializeField, Min(0f)] private float _sharedCharacterWeight = 1f;

        [Tooltip("Радиус вокруг персонажа, который должен попадать в кадр, м.")]
        [SerializeField, Min(0f)] private float _sharedCharacterRadius = 1.2f;

        [Tooltip("Вес центра стола в группе: не даёт камере наехать вплотную, когда все сбились в кучу.")]
        [SerializeField, Min(0f)] private float _sharedTableWeight = 0.6f;

        [SerializeField, Min(0f)] private float _sharedTableRadius = 4f;

        // ---------- Переходы ----------

        [Header("Переходы")]
        [Tooltip("Плавный переход между общей камерой и камерой за игроком, с.")]
        [SerializeField, Min(0f)] private float _blendTime = 0.8f;

        /// <summary>Только в редакторе: поле поменяли в инспекторе.</summary>
        public event Action Changed;

        public bool FollowLocalPlayer => _followLocalPlayer;

        public float FollowFieldOfView => _followFieldOfView;
        public float NearClipPlane => _nearClipPlane;
        public float PivotHeight => _pivotHeight;
        public float ShoulderOffset => _shoulderOffset;
        public float DistanceAtMinPitch => _distanceAtMinPitch;
        public float DistanceAtMaxPitch => _distanceAtMaxPitch;
        public float DistanceCurveExponent => _distanceCurveExponent;

        public float DefaultPitch => _defaultPitch;
        public float MinPitch => Mathf.Min(_minPitch, _maxPitch);
        public float MaxPitch => Mathf.Max(_minPitch, _maxPitch);
        public bool StartBehindCharacter => _startBehindCharacter;
        public float InitialYaw => _initialYaw;
        public float RotationSmoothTime => _rotationSmoothTime;

        public Vector2 MouseSensitivity => _mouseSensitivity;
        public bool InvertMouseY => _invertMouseY;
        public bool LockCursor => _lockCursor;

        public Vector2 StickSpeed => _stickSpeed;
        public bool InvertStickY => _invertStickY;
        public float StickDeadZone => _stickDeadZone;
        public float StickOuterZone => _stickOuterZone;
        public float StickResponseExponent => _stickResponseExponent;
        public float StickAccelerationTime => _stickAccelerationTime;

        public float FollowDampingHorizontal => _followDampingHorizontal;
        public float FollowDampingVertical => _followDampingVertical;
        public float LookAheadTime => _lookAheadTime;
        public float MaxLookAhead => _maxLookAhead;
        public float LookAheadSmoothTime => _lookAheadSmoothTime;
        public float SnapDistance => _snapDistance;

        public bool AutoRecenter => _autoRecenter;
        public float RecenterDelay => _recenterDelay;
        public float RecenterSpeed => _recenterSpeed;
        public float RecenterMinSpeed => _recenterMinSpeed;

        public bool AvoidObstacles => _avoidObstacles;
        public LayerMask ObstacleLayers => _obstacleLayers;
        public bool IgnoreRigidbodies => _ignoreRigidbodies;
        public float ObstacleRadius => _obstacleRadius;
        public float MinObstacleDistance => _minObstacleDistance;
        public float ObstacleReleaseTime => _obstacleReleaseTime;

        public bool CameraRelativeMovement => _cameraRelativeMovement;

        public float SpectateDelay => _spectateDelay;
        public bool SpectateBots => _spectateBots;

        public Vector3 SharedOffset => _sharedOffset;
        public float SharedFieldOfView => _sharedFieldOfView;
        public float SharedFramingSize => _sharedFramingSize;
        public Vector2 SharedDollyRange => _sharedDollyRange;
        public float SharedDamping => _sharedDamping;
        public float SharedCharacterWeight => _sharedCharacterWeight;
        public float SharedCharacterRadius => _sharedCharacterRadius;
        public float SharedTableWeight => _sharedTableWeight;
        public float SharedTableRadius => _sharedTableRadius;

        public float BlendTime => _blendTime;

        /// <summary>Расстояние до точки вращения при данном наклоне.</summary>
        public float DistanceForPitch(float pitch)
        {
            float range = MaxPitch - MinPitch;
            float t = range > 0.001f ? Mathf.Clamp01((pitch - MinPitch) / range) : 0f;
            return Mathf.Lerp(_distanceAtMinPitch, _distanceAtMaxPitch, Mathf.Pow(t, _distanceCurveExponent));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Changed?.Invoke();
        }
#endif
    }
}
