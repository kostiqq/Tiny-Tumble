using UnityEngine;

namespace TinyTumble.Core.Configs
{
    /// <summary>
    /// Боты. Пока используется только блуждание; уровни сложности — из раздела 10 GDD, понадобятся вместе с боевым ИИ.
    /// </summary>
    [CreateAssetMenu(fileName = "BotConfig", menuName = "Tiny Tumble/Configs/Bot")]
    public sealed class BotConfig : ScriptableObject
    {
        [Header("Блуждание")]
        [Tooltip("Радиус вокруг центра арены, внутри которого бот выбирает точки. Меньше края стола, чтобы бот не падал сам.")]
        [SerializeField, Min(0f)] private float _wanderRadius = 3.5f;
        [SerializeField, Min(0.1f)] private float _minRetargetInterval = 1.5f;
        [SerializeField, Min(0.1f)] private float _maxRetargetInterval = 3.5f;
        [SerializeField, Min(0.01f)] private float _arriveDistance = 0.4f;

        [Header("Сложность: Лёгкий / Нормальный / Сложный")]
        [SerializeField] private Vector3 _reactionTimes = new Vector3(0.6f, 0.35f, 0.2f);
        [SerializeField] private Vector3 _parryChances = new Vector3(0f, 0.3f, 0.6f);

        public float WanderRadius => _wanderRadius;
        public float MinRetargetInterval => _minRetargetInterval;
        public float MaxRetargetInterval => _maxRetargetInterval;
        public float ArriveDistance => _arriveDistance;
        public Vector3 ReactionTimes => _reactionTimes;
        public Vector3 ParryChances => _parryChances;
    }
}
