using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using UnityEngine;

namespace TinyTumble.AI.Bots
{
    /// <summary>
    /// Простейший бот: бродит между случайными точками вокруг центра арены.
    /// Управляет персонажем через тот же <see cref="IPlayerInput"/>, что и игрок.
    /// </summary>
    public sealed class WanderBotInput : IPlayerInput
    {
        private readonly Transform _body;
        private readonly BotConfig _config;
        private readonly System.Random _random;
        private Vector3 _target;
        private float _retargetTime;

        public WanderBotInput(Transform body, BotConfig config, int seed)
        {
            _body = body;
            _config = config;
            _random = new System.Random(seed);
            _target = body != null ? body.position : Vector3.zero;
        }

        public PlayerInputFrame Sample()
        {
            if (_body == null)
            {
                return default;
            }

            float time = Time.time;
            Vector3 position = _body.position;
            Vector3 toTarget = _target - position;
            toTarget.y = 0f;

            if (time >= _retargetTime || toTarget.sqrMagnitude <= _config.ArriveDistance * _config.ArriveDistance)
            {
                PickTarget(time);
                toTarget = _target - position;
                toTarget.y = 0f;
            }

            Vector2 move = toTarget.sqrMagnitude > 0f ? new Vector2(toTarget.x, toTarget.z).normalized : Vector2.zero;
            return new PlayerInputFrame(move, PlayerButtons.None, PlayerButtons.None, PlayerButtons.None);
        }

        private void PickTarget(float time)
        {
            // Равномерно по площади круга: корень из случайного радиуса.
            double angle = _random.NextDouble() * Mathf.PI * 2.0;
            float radius = Mathf.Sqrt((float)_random.NextDouble()) * _config.WanderRadius;
            _target = new Vector3(Mathf.Cos((float)angle) * radius, 0f, Mathf.Sin((float)angle) * radius);

            float interval = Mathf.Lerp(_config.MinRetargetInterval, _config.MaxRetargetInterval, (float)_random.NextDouble());
            _retargetTime = time + interval;
        }
    }
}
