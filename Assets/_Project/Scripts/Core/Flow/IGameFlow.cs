using Cysharp.Threading.Tasks;

namespace TinyTumble.Core.Flow
{
    /// <summary>
    /// Переходы между сценами верхнего уровня.
    /// </summary>
    public interface IGameFlow
    {
        /// <summary>Состояние, соответствующее активной сцене.</summary>
        GameState Current { get; }

        bool IsTransitioning { get; }

        /// <summary>
        /// Переход в состояние. Возвращает false, если переход отклонён: уже идёт другой переход
        /// или игра уже в этом состоянии. Переход живёт дольше вызвавшей его сцены, поэтому токен
        /// отмены вызывающего не принимается: используйте .Forget().
        /// </summary>
        UniTask<bool> GoToAsync(GameState target);
    }
}
