using UnityEngine;

namespace TinyTumble.Core.Messages
{
    /// <summary>
    /// Попадание по персонажу, решённое хостом. Публикуется на каждой машине, когда о нём узнала: по нему
    /// играют частицы, хит-стоп и тряска камеры, позже — звук и счёт в HUD.
    /// </summary>
    public readonly struct CharacterHit
    {
        /// <summary>Персонажа атаковали, но в этом событии нет атакующего (брошенный предмет без хозяина и т.п.).</summary>
        public const ulong NoAttacker = ulong.MaxValue;

        /// <summary>NetworkObjectId персонажа, по которому попали.</summary>
        public readonly ulong VictimId;

        public readonly ulong AttackerId;

        public readonly HitKind Kind;

        public readonly Vector3 Point;

        /// <summary>Скорость отлёта, м/с: от неё зависят хит-стоп и тряска камеры.</summary>
        public readonly float Knockback;

        /// <summary>Встряск жертвы после попадания, %.</summary>
        public readonly float ShakeAfter;

        public CharacterHit(ulong victimId, ulong attackerId, HitKind kind, Vector3 point, float knockback, float shakeAfter)
        {
            VictimId = victimId;
            AttackerId = attackerId;
            Kind = kind;
            Point = point;
            Knockback = knockback;
            ShakeAfter = shakeAfter;
        }
    }

    public enum HitKind : byte
    {
        Push = 0,
        ThrownItem = 1,
        ThrownPlayer = 2,

        /// <summary>Атакующего отбросило парирование.</summary>
        Parried = 3,
    }
}
