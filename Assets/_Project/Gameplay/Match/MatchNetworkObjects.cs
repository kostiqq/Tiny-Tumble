using System.Collections.Generic;
using Unity.Netcode;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Хост: все сетевые объекты, которые матч заспавнил (персонажи, предметы). Показывает их клиенту,
    /// когда тот загрузил арену, и убирает до смены сцены.
    /// </summary>
    public sealed class MatchNetworkObjects
    {
        private readonly List<NetworkObject> _objects = new List<NetworkObject>();
        private readonly List<NetworkObject> _buffer = new List<NetworkObject>();

        public int Count => _objects.Count;

        public void Track(NetworkObject networkObject)
        {
            if (networkObject != null && !_objects.Contains(networkObject))
            {
                _objects.Add(networkObject);
            }
        }

        /// <summary>Вернувшийся после обрыва или опоздавший клиент загрузил арену: показываем ему объекты матча.</summary>
        public void ShowTo(ulong clientId)
        {
            RemoveDestroyed();
            for (int i = 0; i < _objects.Count; i++)
            {
                NetworkObject networkObject = _objects[i];
                if (networkObject.IsSpawned && !networkObject.IsNetworkVisibleTo(clientId))
                {
                    networkObject.NetworkShow(clientId);
                }
            }
        }

        /// <summary>Убирает всё, что ещё заспавнено. Персонажей перед этим убирает <see cref="MatchCharacters.DespawnAll"/>.</summary>
        public void DespawnAll()
        {
            _buffer.Clear();
            _buffer.AddRange(_objects);
            _objects.Clear();
            for (int i = 0; i < _buffer.Count; i++)
            {
                NetworkObject networkObject = _buffer[i];
                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn();
                }
            }

            _buffer.Clear();
        }

        private void RemoveDestroyed()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] == null || !_objects[i].IsSpawned)
                {
                    _objects.RemoveAt(i);
                }
            }
        }
    }
}
