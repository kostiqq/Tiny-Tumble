using System;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>Состояние персонажа. Решает хост; клиенты получают его в снимках.</summary>
    public enum CharacterState : byte
    {
        /// <summary>Управляется вводом.</summary>
        Normal = 0,

        /// <summary>Слабое попадание: отшатнулся, на мгновение без управления, равновесие ослаблено.</summary>
        Stagger = 1,

        /// <summary>Сильное попадание или бросок: равновесие и позы отключены, тело падает и катится.</summary>
        Ragdoll = 2,

        /// <summary>Встаёт после ragdoll: равновесие и позы плавно набирают силу.</summary>
        GettingUp = 3,

        /// <summary>Его держат: таз следует за руками держащих, конечности болтаются.</summary>
        Grabbed = 4,
    }

    /// <summary>Что ещё нужно знать, чтобы выбрать позу персонажа.</summary>
    [Flags]
    public enum CharacterFlags : byte
    {
        None = 0,
        Holding = 1 << 0,
        CarryingHeavy = 1 << 1,
        ChargingThrow = 1 << 2,
    }
}
