using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace TinyTumble.Core.Assets
{
    /// <summary>
    /// Загрузка контента по ключу Addressables. Ассеты считаются по ссылкам:
    /// каждый успешный LoadAsync требует ровно одного Release с тем же ключом.
    /// </summary>
    public interface IAssetProvider
    {
        UniTask<T> LoadAsync<T>(string key, CancellationToken cancellationToken) where T : UnityEngine.Object;

        void Release(string key);

        /// <summary>
        /// Загружает все ассеты с меткой. Каждый успешный вызов требует ровно одного <see cref="ReleaseAll"/> с той же меткой.
        /// </summary>
        UniTask<IReadOnlyList<T>> LoadAllAsync<T>(string label, CancellationToken cancellationToken) where T : UnityEngine.Object;

        void ReleaseAll(string label);

        /// <summary>Текущее число ссылок на ключ (0, если не загружен). Для диагностики и тестов.</summary>
        int GetReferenceCount(string key);

        UniTask<ContentSceneHandle> LoadSceneAdditiveAsync(string key, CancellationToken cancellationToken);

        /// <summary>Выгружает сцену. Если сцена уже выгружена вместе с другой (LoadSceneMode.Single), только забывает хэндл.</summary>
        UniTask UnloadSceneAsync(ContentSceneHandle handle);
    }

    /// <summary>
    /// Непрозрачный хэндл сцены контента: слои выше Infrastructure не знают про Addressables.
    /// </summary>
    public readonly struct ContentSceneHandle
    {
        public readonly int Id;
        public readonly Scene Scene;

        public ContentSceneHandle(int id, Scene scene)
        {
            Id = id;
            Scene = scene;
        }

        public bool IsValid => Id != 0;
    }
}
