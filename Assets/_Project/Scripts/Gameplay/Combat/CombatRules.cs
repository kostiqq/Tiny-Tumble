using TinyTumble.Core.Configs;
using UnityEngine;

namespace TinyTumble.Gameplay.Combat
{
    /// <summary>
    /// Формулы боя без HP (раздел 4 GDD). Отдельно от компонентов, чтобы проверять EditMode-тестами и
    /// одинаково применять к игрокам, ботам и предметам.
    /// </summary>
    public static class CombatRules
    {
        public static float AddShake(float current, float amount, CombatConfig config)
        {
            return Mathf.Clamp(current + Mathf.Max(0f, amount), 0f, config.MaxShake);
        }

        /// <summary>Скорость отлёта после попадания: растёт со встряском, как процент в Smash Bros.</summary>
        public static float KnockbackSpeed(float shakeAfterHit, CombatConfig config)
        {
            return config.BaseKnockback + config.KnockbackPerShakePercent * Mathf.Max(0f, shakeAfterHit);
        }

        /// <summary>Вектор скорости отлёта: по горизонтали от удара плюс подъём, чтобы трение не гасило толчок сразу.</summary>
        public static Vector3 KnockbackVelocity(Vector3 direction, float speed, CombatConfig config)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.forward;
            }

            return flat.normalized * speed + Vector3.up * (speed * config.KnockbackLift);
        }

        /// <summary>Слабые попадания только отшатывают, сильные (накопленный встряск) валят в ragdoll.</summary>
        public static bool ShouldRagdoll(float shakeAfterHit, CombatConfig config)
        {
            return shakeAfterHit >= config.RagdollShakeThreshold;
        }

        /// <summary>Сколько держат игрока: один — недолго, двое (цепочка захвата) — дольше.</summary>
        public static float HoldLimit(int holders, CombatConfig config)
        {
            if (holders <= 0)
            {
                return 0f;
            }

            return holders == 1 ? config.SingleGrabBreakTime : Mathf.Max(config.SingleGrabBreakTime, config.DoubleGrabHoldTime);
        }

        /// <summary>Скорость броска по времени зарядки: от минимальной до максимальной за MaxThrowCharge.</summary>
        public static float ThrowSpeed(float chargeSeconds, CombatConfig config)
        {
            float charge = config.MaxThrowCharge > 0f ? Mathf.Clamp01(chargeSeconds / config.MaxThrowCharge) : 1f;
            return Mathf.Lerp(config.MinThrowSpeed, config.MaxThrowSpeed, charge);
        }

        public static Vector3 ThrowVelocity(Vector3 facing, float speed, CombatConfig config)
        {
            Vector3 flat = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.forward;
            }

            return (flat.normalized + Vector3.up * config.ThrowLift).normalized * speed;
        }

        /// <summary>Ragdoll заканчивается, когда тело успокоилось на опоре, но не раньше минимума и не позже максимума.</summary>
        public static bool ShouldGetUp(float elapsed, bool grounded, float speed, CharacterConfig config)
        {
            if (elapsed >= config.MaxRagdollTime)
            {
                return true;
            }

            return elapsed >= config.MinRagdollTime && grounded && speed <= config.RagdollSettleSpeed;
        }

        /// <summary>Направление толчка: куда смотрит атакующий, с поправкой на то, где стоит цель.</summary>
        public static Vector3 PushDirection(Vector3 attackerFacing, Vector3 attackerPosition, Vector3 targetPosition)
        {
            Vector3 toTarget = Vector3.ProjectOnPlane(targetPosition - attackerPosition, Vector3.up);
            Vector3 facing = Vector3.ProjectOnPlane(attackerFacing, Vector3.up);
            Vector3 direction = facing.normalized + (toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.zero);
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }
    }
}
