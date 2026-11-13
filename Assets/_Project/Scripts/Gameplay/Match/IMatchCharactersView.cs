using System;
using R3;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>Персонаж глазами UI: без NGO и физики, только то, что показывает HUD.</summary>
    public interface ICharacterInfo
    {
        /// <summary>NetworkObjectId: стабилен, пока персонаж жив.</summary>
        ulong Id { get; }

        /// <summary>Слот игрока 0..3 или -1 у бота.</summary>
        int Slot { get; }

        bool IsBot { get; }

        /// <summary>Встряск, %.</summary>
        ReadOnlyReactiveProperty<float> Shake { get; }
    }

    /// <summary>Персонажи матча для UI. Реализует <see cref="MatchCharacters"/>.</summary>
    public interface IMatchCharactersView
    {
        int Count { get; }

        ICharacterInfo this[int index] { get; }

        event Action<ICharacterInfo> CharacterAdded;

        event Action<ICharacterInfo> CharacterRemoved;
    }
}
