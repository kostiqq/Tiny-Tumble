using System.Collections.Generic;
using UnityEngine;

namespace TinyTumble.Gameplay.Characters
{
    /// <summary>
    /// Персонаж глазами ИИ: где стоит, в каком состоянии, замахивается ли. Без NGO и физики —
    /// ИИ не ссылается на сетевые сборки и работает только на хосте, где эти данные точные.
    /// </summary>
    public interface ICombatant
    {
        ulong Id { get; }

        Vector3 Position { get; }

        CharacterState State { get; }

        /// <summary>На столе и не выбыл: по нему можно попасть.</summary>
        bool IsTargetable { get; }

        /// <summary>Идёт замах толчка: самое время парировать.</summary>
        bool IsSwinging { get; }
    }

    /// <summary>Все бойцы матча. Реализует <see cref="Match.MatchCharacters"/>.</summary>
    public interface ICombatantRegistry
    {
        IReadOnlyList<ICombatant> Combatants { get; }
    }
}
