using System;
using System.Collections.Generic;
using TinyTumble.Gameplay.Characters;
using UnityEngine;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Персонажи матча на этой машине. У хоста их создаёт матч, у клиентов — NGO, поэтому персонажи
    /// регистрируются сами при спавне.
    /// </summary>
    public sealed class MatchCharacters : IMatchCharactersView, ICombatantRegistry
    {
        private readonly List<PlayerCharacter> _characters = new List<PlayerCharacter>();
        private readonly List<PlayerCharacter> _buffer = new List<PlayerCharacter>();
        private readonly List<ICombatant> _combatants = new List<ICombatant>();

        public IReadOnlyList<PlayerCharacter> All => _characters;

        public IReadOnlyList<ICombatant> Combatants => _combatants;

        /// <summary>Персонаж заспавнен на этой машине (камера, HUD).</summary>
        public event Action<PlayerCharacter> Added;

        /// <summary>Персонаж убран: выбыл, отключился или матч кончился.</summary>
        public event Action<PlayerCharacter> Removed;

        public event Action<ICharacterInfo> CharacterAdded;

        public event Action<ICharacterInfo> CharacterRemoved;

        public int Count => _characters.Count;

        public ICharacterInfo this[int index] => _characters[index];

        public void Add(PlayerCharacter character)
        {
            if (_characters.Contains(character))
            {
                return;
            }

            _characters.Add(character);
            _combatants.Add(character);
            Raise(Added, character);
            Raise<ICharacterInfo>(CharacterAdded, character);
        }

        public void Remove(PlayerCharacter character)
        {
            if (_characters.Remove(character))
            {
                _combatants.Remove(character);
                Raise(Removed, character);
                Raise<ICharacterInfo>(CharacterRemoved, character);
            }
        }

        public bool TryGet(ulong networkObjectId, out PlayerCharacter character)
        {
            for (int i = 0; i < _characters.Count; i++)
            {
                if (_characters[i] != null && _characters[i].NetworkObjectId == networkObjectId)
                {
                    character = _characters[i];
                    return true;
                }
            }

            character = null;
            return false;
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
                PlayerCharacter character = _buffer[i];
                if (character != null && character.IsSpawned && character.IsServer)
                {
                    character.MarkRemovedByMatch();
                    character.NetworkObject.Despawn();
                }
            }

            _buffer.Clear();
        }

        // Вызывается из колбэков спавна NGO: исключение подписчика не должно сорвать спавн.
        private static void Raise<T>(Action<T> handlers, T character)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)handler).Invoke(character);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
