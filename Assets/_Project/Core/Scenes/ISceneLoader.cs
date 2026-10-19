using System.Threading;
using Cysharp.Threading.Tasks;

namespace TinyTumble.Core.Scenes
{
    /// <summary>
    /// Загрузка сцен из Build Settings. Сцены контента (арены) грузятся через <see cref="Assets.IAssetProvider"/>.
    /// </summary>
    public interface ISceneLoader
    {
        string ActiveSceneName { get; }

        /// <summary>Заменяет все открытые сцены указанной (LoadSceneMode.Single).</summary>
        UniTask LoadAsync(string sceneName, CancellationToken cancellationToken);
    }
}
