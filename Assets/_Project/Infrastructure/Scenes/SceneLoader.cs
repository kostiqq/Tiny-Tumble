using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TinyTumble.Infrastructure.Scenes
{
    public sealed class SceneLoader : ISceneLoader
    {
        public string ActiveSceneName => SceneManager.GetActiveScene().name;

        public async UniTask LoadAsync(string sceneName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                throw new ArgumentException("Scene name is empty.", nameof(sceneName));
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                throw new InvalidOperationException($"Scene '{sceneName}' is not in Build Settings.");
            }

            await operation.ToUniTask(cancellationToken: cancellationToken);

            // Экран загрузки ещё виден: удобный момент выгрузить ассеты прошлой сцены и собрать мусор,
            // чтобы GC не сработал посреди матча.
            await Resources.UnloadUnusedAssets().ToUniTask(cancellationToken: cancellationToken);
            GC.Collect();
        }
    }
}
