using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Assets;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Сцена арены, догружаемая к сцене Match. Владелец загрузки = владелец выгрузки.
    /// </summary>
    public sealed class MatchArena : IDisposable
    {
        private readonly IAssetProvider _assets;
        private ContentSceneHandle _handle;

        public MatchArena(IAssetProvider assets)
        {
            _assets = assets;
        }

        public bool IsLoaded => _handle.IsValid;

        public async UniTask LoadAsync(string key, CancellationToken cancellationToken)
        {
            if (IsLoaded)
            {
                throw new InvalidOperationException("Arena is already loaded.");
            }

            _handle = await _assets.LoadSceneAdditiveAsync(key, cancellationToken);
        }

        public UniTask UnloadAsync()
        {
            if (!IsLoaded)
            {
                return UniTask.CompletedTask;
            }

            ContentSceneHandle handle = _handle;
            _handle = default;
            return _assets.UnloadSceneAsync(handle);
        }

        public void Dispose()
        {
            // Обычно арену уже выгрузил MatchFlowController. Если из матча ушли иначе (например, тест или консоль),
            // сцену выгрузил SceneManager, и провайдер просто забудет хэндл.
            UnloadAsync().Forget();
        }
    }
}
