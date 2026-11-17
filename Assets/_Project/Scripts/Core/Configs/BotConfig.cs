using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Простые боты прототипа (веха A): бродят, подходят к ближайшему сопернику и толкают его.
    /// Уровни сложности — из раздела 10 GDD; полноценный ИИ — этап 7.
    /// </summary>
    [CreateAssetMenu(fileName = "BotConfig", menuName = "Tiny Tumble/Configs/Bot")]
    public sealed class BotConfig : ScriptableObject
    {
        public const int Easy = 0;
        public const int Normal = 1;
        public const int Hard = 2;

        [Header("Блуждание")]
        [Tooltip("Радиус вокруг центра арены, внутри которого бот выбирает точки. Меньше края стола, чтобы бот не падал сам.")]
        [SerializeField, Min(0f)] private float _wanderRadius = 3.5f;
        [SerializeField, Min(0.1f)] private float _minRetargetInterval = 1.5f;
        [SerializeField, Min(0.1f)] private float _maxRetargetInterval = 3.5f;
        [SerializeField, Min(0.01f)] private float _arriveDistance = 0.4f;

        [Header("Драка")]
        [Tooltip("Первые секунды матча бот только бродит: игрокам нужно время осмотреться.")]
        [SerializeField, Min(0f)] private float _aggressionDelay = 2.5f;
        [Tooltip("На таком расстоянии бот замечает соперника, м.")]
        [SerializeField, Min(0f)] private float _aggroRange = 8f;
        [Tooltip("С такого расстояния бот толкает, м. Толчок попадает касанием кисти, поэтому дистанция — длина руки с выпадом.")]
        [SerializeField, Min(0f)] private float _pushRange = 0.75f;
        [Tooltip("Рывок к сопернику, если тот дальше этого, м.")]
        [SerializeField, Min(0f)] private float _dashRange = 3f;
        [SerializeField, Range(0f, 1f)] private float _dashChance = 0.25f;
        [Tooltip("Дальше этого от центра бот разворачивается к центру, м.")]
        [SerializeField, Min(0f)] private float _edgeRetreatRadius = 3.8f;

        [Header("Сложность: Лёгкий / Нормальный / Сложный")]
        [SerializeField, Range(Easy, Hard)] private int _difficulty = Normal;
        [SerializeField] private Vector3 _reactionTimes = new Vector3(0.6f, 0.35f, 0.2f);
        [SerializeField] private Vector3 _parryChances = new Vector3(0f, 0.3f, 0.6f);

        public float WanderRadius => _wanderRadius;
        public float MinRetargetInterval => _minRetargetInterval;
        public float MaxRetargetInterval => _maxRetargetInterval;
        public float ArriveDistance => _arriveDistance;
        public float AggressionDelay => _aggressionDelay;
        public float AggroRange => _aggroRange;
        public float PushRange => _pushRange;
        public float DashRange => _dashRange;
        public float DashChance => _dashChance;
        public float EdgeRetreatRadius => _edgeRetreatRadius;
        public int Difficulty => _difficulty;
        public Vector3 ReactionTimes => _reactionTimes;
        public Vector3 ParryChances => _parryChances;

        public float ReactionTime => _reactionTimes[Mathf.Clamp(_difficulty, Easy, Hard)];

        public float ParryChance => _parryChances[Mathf.Clamp(_difficulty, Easy, Hard)];
    }
}
