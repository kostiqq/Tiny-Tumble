using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Logging;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TinyTumble.Infrastructure.Assets
{
    /// <summary>
    /// Addressables со счётчиком ссылок по ключу: один хэндл на ключ, Release Addressables — когда счётчик дошёл до нуля.
    /// </summary>
    public sealed class AddressableAssetProvider : IAssetProvider, IDisposable
    {
        private const string LogTag = "Assets";

        private sealed class AssetEntry
        {
            public AsyncOperationHandle Handle;
            public int References;
        }

        private readonly IGameLog _log;
        private readonly Dictionary<string, AssetEntry> _assets = new Dictionary<string, AssetEntry>();
        private readonly Dictionary<int, AsyncOperationHandle<SceneInstance>> _scenes =
            new Dictionary<int, AsyncOperationHandle<SceneInstance>>();
        private int _nextSceneId = 1;
        private bool _disposed;

        public AddressableAssetProvider(IGameLog log)
        {
            _log = log;
        }

        public async UniTask<T> LoadAsync<T>(string key, CancellationToken cancellationToken) where T : Object
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Asset key is empty.", nameof(key));
            }

            if (!_assets.TryGetValue(key, out AssetEntry entry))
            {
                entry = new AssetEntry { Handle = Addressables.LoadAssetAsync<T>(key) };
                _assets.Add(key, entry);
            }

            // Ссылку берём до await: параллельный Release того же ключа не должен выгрузить ассет у нас из-под ног.
            entry.References++;

            try
            {
                await entry.Handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch
            {
                Release(key);
                throw;
            }

            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Release(key);
                throw new InvalidOperationException($"Failed to load asset '{key}'.", entry.Handle.OperationException);
            }

            if (!(entry.Handle.Result is T result))
            {
                Release(key);
                throw new InvalidCastException($"Asset '{key}' is not {typeof(T).Name}.");
            }

            return result;
        }

        public void Release(string key)
        {
            if (key == null || !_assets.TryGetValue(key, out AssetEntry entry))
            {
                _log.Warning(LogTag, $"Release of unknown key '{key}'.");
                return;
            }

            entry.References--;
            if (entry.References > 0)
            {
                return;
            }

            _assets.Remove(key);
            if (entry.Handle.IsValid())
            {
                Addressables.Release(entry.Handle);
            }
        }

        public int GetReferenceCount(string key)
        {
            return key != null && _assets.TryGetValue(key, out AssetEntry entry) ? entry.References : 0;
        }

        public async UniTask<ContentSceneHandle> LoadSceneAdditiveAsync(string key, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(key, LoadSceneMode.Additive);
            int id = _nextSceneId++;
            _scenes.Add(id, handle);

            try
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }
            catch
            {
                await UnloadSceneAsync(new ContentSceneHandle(id, default));
                throw;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                _scenes.Remove(id);
                throw new InvalidOperationException($"Failed to load scene '{key}'.", handle.OperationException);
            }

            return new ContentSceneHandle(id, handle.Result.Scene);
        }

        public async UniTask UnloadSceneAsync(ContentSceneHandle sceneHandle)
        {
            if (!_scenes.TryGetValue(sceneHandle.Id, out AsyncOperationHandle<SceneInstance> handle))
            {
                return;
            }

            _scenes.Remove(sceneHandle.Id);

            // Если сцену уже выгрузил SceneManager (переход LoadSceneMode.Single), Addressables сам освободил хэндл.
            if (!handle.IsValid())
            {
                return;
            }

            if (handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded && !handle.Result.Scene.isLoaded)
            {
                return;
            }

            await Addressables.UnloadSceneAsync(handle).ToUniTask();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (KeyValuePair<string, AssetEntry> pair in _assets)
            {
                if (pair.Value.Handle.IsValid())
                {
                    Addressables.Release(pair.Value.Handle);
                }
            }

            _assets.Clear();
            _scenes.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AddressableAssetProvider));
            }
        }
    }
}
