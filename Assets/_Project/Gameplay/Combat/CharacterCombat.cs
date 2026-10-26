using System.Collections.Generic;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Messages;
using TinyTumble.Gameplay.Characters;
using UnityEngine;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// Действия персонажа, которые решает только хост (раздел 4 GDD):
    /// толчок с замахом и окном попадания, парирование таймингом, захват предмета или игрока,
    /// бросок с зарядкой, эмоции. Клиент-владелец показывает замах и эмоцию сразу у себя,
    /// а попадания приходят от хоста.
    /// Управление: «Толчок» — толчок; с чем-то в руках удержание «Толчка» заряжает бросок, отпускание бросает.
    /// «Захват» — схватить ближайшее перед собой или положить то, что в руках. «Парирование» — короткое окно.
    /// </summary>
    public sealed class CharacterCombat
    {
        private const int OverlapCapacity = 16;
        private const int EmoteCount = 4;

        private static readonly Collider[] s_overlap = new Collider[OverlapCapacity];

        private static readonly PlayerButtons[] s_emoteButtons =
        {
            PlayerButtons.Emote1, PlayerButtons.Emote2, PlayerButtons.Emote3, PlayerButtons.Emote4,
        };

        private readonly PlayerCharacter _owner;
        private readonly CombatConfig _config;
        private readonly List<Rigidbody> _hitThisSwing = new List<Rigidbody>(OverlapCapacity);

        private float _swingStart = float.NegativeInfinity;
        private bool _swinging;
        private float _pushReadyTime;
        private float _parryUntil = float.NegativeInfinity;
        private bool _parryPending;
        private float _actionLockUntil = float.NegativeInfinity;
        private bool _charging;
        private float _chargeStart;
        private float _emoteReadyTime;

        public CharacterCombat(PlayerCharacter owner, CombatConfig config)
        {
            _owner = owner;
            _config = config;
        }

        public IGrabbable Held { get; private set; }

        public bool IsHolding => Held != null;

        public bool IsCarryingHeavy => Held != null && Held.IsHeavy;

        public float SpeedMultiplier => IsCarryingHeavy ? 1f - _config.HeavyCarrySlowdown : 1f;

        /// <summary>Идёт замах или окно попадания толчка. Боты по этому решают, парировать ли.</summary>
        public bool IsSwinging => _swinging;

        public bool IsChargingThrow => _charging;

        public bool IsParrying(float now)
        {
            return now <= _parryUntil;
        }

        public void Step(in PlayerInputFrame frame, float now, bool canAct)
        {
            if (!canAct)
            {
                CancelActions(now);
                return;
            }

            StepParry(frame, now);
            if (IsHolding)
            {
                StepHolding(frame, now);
            }
            else
            {
                StepPush(frame, now);
                StepGrab(frame, now);
            }

            StepEmotes(frame, now);
        }

        /// <summary>По персонажу попали, его схватили или он выбыл: замах и зарядка обрываются, ношу роняет.</summary>
        public void CancelActions(float now)
        {
            _swinging = false;
            _charging = false;
            _parryUntil = float.NegativeInfinity;
            _parryPending = false;
            if (IsHolding)
            {
                Drop(now);
            }
        }

        /// <summary>Наше парирование отбило толчок: штрафа за промах не будет.</summary>
        public void OnParrySucceeded()
        {
            _parryPending = false;
        }

        /// <summary>Удерживаемое вырвалось или исчезло само: просто забываем его, не отпуская ещё раз.</summary>
        public void ForgetHeld(IGrabbable grabbable)
        {
            if (Held == grabbable)
            {
                Held = null;
                _charging = false;
            }
        }

        /// <summary>Куда ставить удерживаемое: над головой, чуть впереди.</summary>
        public Vector3 HoldPosition(float bottomOffset)
        {
            return _owner.Body.position
                + Vector3.up * (_owner.Height + _config.HoldHeight + bottomOffset)
                + _owner.Facing * _config.HoldForward;
        }

        public void Drop(float now)
        {
            if (Held == null)
            {
                return;
            }

            IGrabbable held = Held;
            Held = null;
            _charging = false;
            held.OnReleased(_owner, _owner.Facing * _config.DropSpeed + _owner.Body.linearVelocity, thrown: false, now);
        }

        private void StepParry(in PlayerInputFrame frame, float now)
        {
            if (_parryPending && now > _parryUntil)
            {
                // Окно прошло впустую: короткое наказание, чтобы парирование не спамили.
                _parryPending = false;
                _actionLockUntil = now + _config.ParryMissRecovery;
            }

            if (frame.WasPressed(PlayerButtons.Parry) && now >= _actionLockUntil && !_parryPending && !IsHolding)
            {
                _parryUntil = now + _config.ParryWindow;
                _parryPending = true;
            }
        }

        private void StepPush(in PlayerInputFrame frame, float now)
        {
            if (frame.WasPressed(PlayerButtons.Push) && !_swinging && now >= _pushReadyTime && now >= _actionLockUntil)
            {
                _swinging = true;
                _swingStart = now;
                _pushReadyTime = now + _config.PushCooldown;
                _hitThisSwing.Clear();
                _owner.BroadcastSwing();
            }

            if (!_swinging)
            {
                return;
            }

            float elapsed = now - _swingStart;
            if (elapsed < _config.PushWindup)
            {
                return;
            }

            if (elapsed > _config.PushWindup + _config.PushActiveTime)
            {
                _swinging = false;
                return;
            }

            ResolvePushHits(now);
        }

        private void ResolvePushHits(float now)
        {
            Vector3 origin = _owner.Body.position + Vector3.up * (_owner.Height * 0.5f);
            Vector3 center = origin + _owner.Facing * _config.PushReach;
            int count = Physics.OverlapSphereNonAlloc(center, _config.PushRadius, s_overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = s_overlap[i].attachedRigidbody;
                if (body == null || body == _owner.Body || _hitThisSwing.Contains(body))
                {
                    continue;
                }

                _hitThisSwing.Add(body);
                Vector3 point = s_overlap[i].ClosestPoint(center);

                if (body.TryGetComponent(out PlayerCharacter target))
                {
                    if (!target.IsTargetable || ReferenceEquals(Held, target))
                    {
                        continue;
                    }

                    if (target.Combat.IsParrying(now))
                    {
                        // Парирование: атакующего отбрасывает, цель не страдает.
                        target.Combat.OnParrySucceeded();
                        _swinging = false;
                        Vector3 back = CombatRules.PushDirection(target.Facing, target.Body.position, _owner.Body.position);
                        _owner.ReceiveHit(_config.ParryReflectShake, back, target, HitKind.Parried, forceRagdoll: true, point);
                        return;
                    }

                    Vector3 direction = CombatRules.PushDirection(_owner.Facing, _owner.Body.position, target.Body.position);
                    target.ReceiveHit(_config.PushShake, direction, _owner, HitKind.Push, forceRagdoll: false, point);
                }
                else if (body.TryGetComponent(out GrabbableItem item))
                {
                    Vector3 direction = CombatRules.PushDirection(_owner.Facing, _owner.Body.position, body.position);
                    item.Push(direction * _config.PushItemSpeed + Vector3.up * (_config.PushItemSpeed * _config.KnockbackLift));
                }
            }
        }

        private void StepGrab(in PlayerInputFrame frame, float now)
        {
            if (!frame.WasPressed(PlayerButtons.Grab) || now < _actionLockUntil || _swinging)
            {
                return;
            }

            IGrabbable target = FindGrabTarget(now);
            if (target == null)
            {
                return;
            }

            Held = target;
            _charging = false;
            target.OnGrabbed(_owner, now);
        }

        private void StepHolding(in PlayerInputFrame frame, float now)
        {
            if (frame.WasPressed(PlayerButtons.Grab))
            {
                Drop(now);
                return;
            }

            if (frame.WasPressed(PlayerButtons.Push) && !_charging)
            {
                _charging = true;
                _chargeStart = now;
            }

            // Отпустили кнопку — бросок. Нажатие и отпускание в одном тике — бросок минимальной силы.
            bool released = frame.WasReleased(PlayerButtons.Push) || (_charging && !frame.IsHeld(PlayerButtons.Push));
            if (_charging && released)
            {
                Throw(now - _chargeStart, now);
            }
        }

        private void Throw(float chargeSeconds, float now)
        {
            IGrabbable held = Held;
            Held = null;
            _charging = false;
            float speed = CombatRules.ThrowSpeed(chargeSeconds, _config);
            Vector3 velocity = CombatRules.ThrowVelocity(_owner.Facing, speed, _config);
            held.OnReleased(_owner, velocity, thrown: true, now);
            _owner.BroadcastSwing();
        }

        private IGrabbable FindGrabTarget(float now)
        {
            Vector3 origin = _owner.Body.position + Vector3.up * (_owner.Height * 0.5f);
            Vector3 center = origin + _owner.Facing * _config.GrabReach;
            int count = Physics.OverlapSphereNonAlloc(center, _config.GrabRadius, s_overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            IGrabbable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = s_overlap[i].attachedRigidbody;
                if (body == null || body == _owner.Body || !body.TryGetComponent(out IGrabbable grabbable))
                {
                    continue;
                }

                if (!grabbable.CanBeGrabbedBy(_owner, now))
                {
                    continue;
                }

                float distance = (body.position - center).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = grabbable;
                }
            }

            return best;
        }

        private void StepEmotes(in PlayerInputFrame frame, float now)
        {
            if (now < _emoteReadyTime)
            {
                return;
            }

            for (int i = 0; i < EmoteCount; i++)
            {
                if (frame.WasPressed(s_emoteButtons[i]))
                {
                    _emoteReadyTime = now + _config.EmoteCooldown;
                    _owner.BroadcastEmote(i);
                    return;
                }
            }
        }

        /// <summary>Индекс эмоции по нажатым кнопкам кадра или -1. Нужен клиенту-владельцу для мгновенного отклика.</summary>
        public static int PressedEmote(in PlayerInputFrame frame)
        {
            for (int i = 0; i < EmoteCount; i++)
            {
                if (frame.WasPressed(s_emoteButtons[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
