using System;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TinyTumble.Networking.Spawning
{
    /// <summary>
    /// NGO создаёт сетевые объекты на клиентах сам, мимо VContainer, и [Inject] в NetworkBehaviour не срабатывал бы.
    /// Этот обработчик подменяет создание: префаб инстанцируется через текущий DI-скоуп.
    /// На хосте объект создаёт игровой код (тоже через DI), а NGO вызывает только <see cref="Destroy"/>.
    /// </summary>
    public sealed class InjectingPrefabHandler : INetworkPrefabInstanceHandler
    {
        private readonly GameObject _prefab;
        private readonly NetworkObjectResolver _resolver;

        public InjectingPrefabHandler(GameObject prefab, NetworkObjectResolver resolver)
        {
            _prefab = prefab;
            _resolver = resolver;
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            GameObject instance = _resolver.Instantiate(_prefab, position, rotation);
            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Object.Destroy(instance);
                throw new InvalidOperationException($"Network prefab '{_prefab.name}' has no {nameof(NetworkObject)}.");
            }

            return networkObject;
        }

        public void Destroy(NetworkObject networkObject)
        {
            if (networkObject != null)
            {
                Object.Destroy(networkObject.gameObject);
            }
        }
    }
}
