using System;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>Состояние персонажа. Решает хост; клиенты получают его в снимках.</summary>
    public enum CharacterState : byte
    {
        /// <summary>Управляется вводом. Только в нём владелец предсказывает своё движение.</summary>
        Normal = 0,

        /// <summary>Слабое попадание: отшатнулся, на мгновение без управления, остаётся на ногах.</summary>
        Stagger = 1,

        /// <summary>Сильное попадание или бросок: физически падает и катится.</summary>
        Ragdoll = 2,

        /// <summary>Встаёт после ragdoll: плавно возвращается в вертикаль.</summary>
        GettingUp = 3,

        /// <summary>Его держат: следует за руками держащих.</summary>
        Grabbed = 4,
    }

    /// <summary>Что ещё нужно знать клиенту, чтобы предсказывать и показывать персонажа.</summary>
    [Flags]
    public enum CharacterFlags : byte
    {
        None = 0,
        Holding = 1 << 0,
        CarryingHeavy = 1 << 1,
        ChargingThrow = 1 << 2,
    }
}
