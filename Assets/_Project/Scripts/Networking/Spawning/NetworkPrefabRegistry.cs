using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Logging;
using TinyTumble.Networking.Session;
using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Spawning
{
    /// <summary>
    /// Сетевые префабы приходят из Addressables (метка <see cref="AddressKeys.NetworkLabel"/>), а NGO требует,
    /// чтобы список префабов совпадал у хоста и клиентов уже в момент подключения (ForceSamePrefabs).
    /// Поэтому до StartHost/StartClient все они загружаются и регистрируются вместе с DI-обработчиком.
    /// Загружаются один раз и живут до конца игры: их немного, а повторная регистрация между сессиями не нужна.
    /// </summary>
    public sealed class NetworkPrefabRegistry : IDisposable
    {
        private const string LogTag = "Net";

        private readonly NetworkRuntime _runtime;
        private readonly IAssetProvider _assets;
        private readonly NetworkObjectResolver _resolver;
        private readonly IGameLog _log;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<GameObject> _prefabs = new List<GameObject>();
        private UniTask _loading;
        private bool _loadingStarted;
        private bool _loaded;

        public NetworkPrefabRegistry(NetworkRuntime runtime, IAssetProvider assets, NetworkObjectResolver resolver, IGameLog log)
        {
            _runtime = runtime;
            _assets = assets;
            _resolver = resolver;
            _log = log;
        }

        public bool IsRegistered => _loaded;

        /// <summary>Повторные и параллельные вызовы ждут одну и ту же загрузку.</summary>
        public UniTask EnsureRegisteredAsync(CancellationToken cancellationToken)
        {
            if (!_loadingStarted)
            {
                _loadingStarted = true;
                _loading = LoadAsync().Preserve();
            }

            return _loading.AttachExternalCancellation(cancellationToken);
        }

        /// <summary>Первый зарегистрированный префаб с компонентом T, или null.</summary>
        public T FindPrefab<T>() where T : Component
        {
            for (int i = 0; i < _prefabs.Count; i++)
            {
                if (_prefabs[i].TryGetComponent(out T component))
                {
                    return component;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            if (_loaded)
            {
                _loaded = false;
                _assets.ReleaseAll(AddressKeys.NetworkLabel);
            }
        }

        private async UniTask LoadAsync()
        {
            IReadOnlyList<GameObject> prefabs;
            try
            {
                prefabs = await _assets.LoadAllAsync<GameObject>(AddressKeys.NetworkLabel, _lifetime.Token);
            }
            catch
            {
                // Следующая попытка старта сессии загрузит заново.
                _loadingStarted = false;
                throw;
            }

            _loaded = true;
            NetworkManager manager = _runtime.Manager;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (!prefab.TryGetComponent(out NetworkObject _))
                {
                    _log.Error(LogTag, $"Asset '{prefab.name}' has label '{AddressKeys.NetworkLabel}' but no NetworkObject. Skipped.");
                    continue;
                }

                manager.AddNetworkPrefab(prefab);
                manager.PrefabHandler.AddHandler(prefab, new InjectingPrefabHandler(prefab, _resolver));
                _prefabs.Add(prefab);
            }

            _log.Info(LogTag, $"Registered {_prefabs.Count} network prefabs.");
        }
    }
}
