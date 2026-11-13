using Cysharp.Threading.Tasks;
using TinyTumble.Core.Flow;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Flow
{
    /// <summary>
    /// Кнопка перехода в другое состояние игры. Повторные нажатия во время перехода отсекает GameFlow.
    /// </summary>
    public sealed class GameFlowButton : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private GameState _target = GameState.MainMenu;

        private IGameFlow _gameFlow;

        public GameState Target => _target;

        [Inject]
        public void Construct(IGameFlow gameFlow)
        {
            _gameFlow = gameFlow;
        }

        private void Reset()
        {
            _button = GetComponent<Button>();
        }

        private void Awake()
        {
            _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            if (_gameFlow == null)
            {
                Debug.LogError($"{nameof(GameFlowButton)} on '{name}' was not injected. Add its root to the scene LifetimeScope Auto Inject list.", this);
                return;
            }

            _gameFlow.GoToAsync(_target).Forget();
        }
    }
}
