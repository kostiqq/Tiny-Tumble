using System.Collections.Generic;
using TinyTumble.Gameplay.Characters;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Персонажи матча на этой машине. У хоста их создаёт матч, у клиентов — NGO, поэтому персонажи
    /// регистрируются сами при спавне.
    /// </summary>
    public sealed class MatchCharacters
    {
        private readonly List<StubCharacter> _characters = new List<StubCharacter>();
        private readonly List<StubCharacter> _buffer = new List<StubCharacter>();

        public IReadOnlyList<StubCharacter> All => _characters;

        public void Add(StubCharacter character)
        {
            if (!_characters.Contains(character))
            {
                _characters.Add(character);
            }
        }

        public void Remove(StubCharacter character)
        {
            _characters.Remove(character);
        }

        /// <summary>
        /// Хост: убирает всех персонажей до смены сцены. Если клиенты уничтожат сетевые объекты вместе
        /// со сценой сами, NGO сочтёт это ошибкой.
        /// </summary>
        public void DespawnAll()
        {
            _buffer.Clear();
            _buffer.AddRange(_characters);
            for (int i = 0; i < _buffer.Count; i++)
            {
                StubCharacter character = _buffer[i];
                if (character != null && character.IsSpawned && character.IsServer)
                {
                    character.MarkRemovedByMatch();
                    character.NetworkObject.Despawn();
                }
            }

            _buffer.Clear();
        }
    }
}
