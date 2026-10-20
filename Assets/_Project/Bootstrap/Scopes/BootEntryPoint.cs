using Cysharp.Threading.Tasks;
using TinyTumble.Core.Flow;
using VContainer.Unity;

namespace TinyTumble.Bootstrap.Scopes
{
    /// <summary>
    /// Точка входа сцены Boot. Корень к этому моменту уже собран и применил настройки; остаётся уйти в меню.
    /// </summary>
    public sealed class BootEntryPoint : IStartable
    {
        private readonly IGameFlow _gameFlow;

        public BootEntryPoint(IGameFlow gameFlow)
        {
            _gameFlow = gameFlow;
        }

        public void Start()
        {
            _gameFlow.GoToAsync(GameState.MainMenu).Forget();
        }
    }
}
