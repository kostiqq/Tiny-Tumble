using System.Collections.Generic;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Characters;
using UnityEngine;

namespace TinyTumble.AI.Bots
{
    /// <summary>
    /// Простой бот для прототипа: бродит, пока рядом никого нет; замечает ближайшего соперника, подходит
    /// и толкает, иногда делает к нему рывок; парирует чужой замах с шансом по сложности; от края стола
    /// уходит к центру. Решения принимаются не чаще времени реакции. Управляет персонажем через тот же
    /// <see cref="IPlayerInput"/>, что и игрок. Работает на хосте.
    /// </summary>
    public sealed class BrawlerBotInput : IPlayerInput
    {
        private readonly ICombatant _self;
        private readonly ICombatantRegistry _characters;
        private readonly BotConfig _config;
        private readonly System.Random _random;
        private readonly float _startTime;

        private Vector3 _wanderTarget;
        private float _retargetTime;
        private float _nextDecisionTime;
        private ICombatant _target;
        private ICombatant _lastParriedSwing;

        public BrawlerBotInput(ICombatant self, ICombatantRegistry characters, BotConfig config, int seed)
        {
            _self = self;
            _characters = characters;
            _config = config;
            _random = new System.Random(seed);
            _startTime = Time.time;
            _wanderTarget = self != null ? self.Position : Vector3.zero;
        }

        public PlayerInputFrame Sample()
        {
            if (_self == null || !_self.IsTargetable || _self.State != CharacterState.Normal)
            {
                return default;
            }

            float now = Time.time;
            Vector3 position = _self.Position;
            PlayerButtons pressed = PlayerButtons.None;

            // Край стола: сначала спасаемся, потом дерёмся.
            Vector3 flat = new Vector3(position.x, 0f, position.z);
            if (flat.magnitude > _config.EdgeRetreatRadius)
            {
                return Frame(-flat, PlayerButtons.None);
            }

            bool aggressive = now - _startTime >= _config.AggressionDelay;
            if (aggressive && now >= _nextDecisionTime)
            {
                _nextDecisionTime = now + _config.ReactionTime;
                _target = FindTarget(position);
                pressed = Decide(position);
            }

            if (_target != null && (!_target.IsTargetable || !aggressive))
            {
                _target = null;
            }

            Vector3 toGoal = _target != null ? _target.Position - position : WanderDirection(now, position);
            toGoal.y = 0f;

            // Вплотную к цели не толкаемся телом, а стоим и бьём.
            if (_target != null && toGoal.magnitude < _config.PushRange * 0.6f)
            {
                toGoal = toGoal.normalized * 0.01f;
            }

            return Frame(toGoal, pressed);
        }

        private PlayerButtons Decide(Vector3 position)
        {
            if (_target == null)
            {
                return PlayerButtons.None;
            }

            float distance = Vector3.Distance(Flat(position), Flat(_target.Position));

            // Цель замахивается на нас: парируем с шансом по сложности, один раз на замах.
            if (_target.IsSwinging && distance < _config.PushRange * 1.5f && _lastParriedSwing != _target)
            {
                _lastParriedSwing = _target;
                if (_random.NextDouble() < _config.ParryChance)
                {
                    return PlayerButtons.Parry;
                }
            }
            else if (!_target.IsSwinging)
            {
                _lastParriedSwing = null;
            }

            if (distance <= _config.PushRange)
            {
                return PlayerButtons.Push;
            }

            if (distance >= _config.DashRange && distance <= _config.AggroRange && _random.NextDouble() < _config.DashChance)
            {
                return PlayerButtons.Dash;
            }

            return PlayerButtons.None;
        }

        private ICombatant FindTarget(Vector3 position)
        {
            ICombatant best = null;
            float bestDistance = _config.AggroRange * _config.AggroRange;
            IReadOnlyList<ICombatant> all = _characters.Combatants;
            for (int i = 0; i < all.Count; i++)
            {
                ICombatant candidate = all[i];
                if (candidate == null || candidate == _self || !candidate.IsTargetable || candidate.State == CharacterState.Grabbed)
                {
                    continue;
                }

                float distance = (Flat(candidate.Position) - Flat(position)).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private Vector3 WanderDirection(float now, Vector3 position)
        {
            Vector3 toTarget = _wanderTarget - position;
            toTarget.y = 0f;
            if (now >= _retargetTime || toTarget.sqrMagnitude <= _config.ArriveDistance * _config.ArriveDistance)
            {
                PickWanderTarget(now);
                toTarget = _wanderTarget - position;
                toTarget.y = 0f;
            }

            return toTarget;
        }

        private void PickWanderTarget(float now)
        {
            // Равномерно по площади круга: корень из случайного радиуса.
            double angle = _random.NextDouble() * Mathf.PI * 2.0;
            float radius = Mathf.Sqrt((float)_random.NextDouble()) * _config.WanderRadius;
            _wanderTarget = new Vector3(Mathf.Cos((float)angle) * radius, 0f, Mathf.Sin((float)angle) * radius);

            float interval = Mathf.Lerp(_config.MinRetargetInterval, _config.MaxRetargetInterval, (float)_random.NextDouble());
            _retargetTime = now + interval;
        }

        private static PlayerInputFrame Frame(Vector3 direction, PlayerButtons pressed)
        {
            Vector2 move = direction.sqrMagnitude > 1e-6f ? new Vector2(direction.x, direction.z).normalized : Vector2.zero;
            if (direction.sqrMagnitude < 0.001f)
            {
                // Почти на месте: только разворот к цели, без шага.
                move *= 0.02f;
            }

            return new PlayerInputFrame(move, pressed, pressed, PlayerButtons.None);
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
