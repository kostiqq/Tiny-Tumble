using MessagePipe;
using TinyTumble.Core.Messages;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Flow
{
    /// <summary>
    /// Временная кнопка «Завершить матч». UI не меняет состояние сам, а отправляет сообщение MatchEnded.
    /// </summary>
    public sealed class MatchEndButton : MonoBehaviour
    {
        [SerializeField] private Button _button;

        private IPublisher<MatchEnded> _matchEnded;

        [Inject]
        public void Construct(IPublisher<MatchEnded> matchEnded)
        {
            _matchEnded = matchEnded;
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
            if (_matchEnded == null)
            {
                Debug.LogError($"{nameof(MatchEndButton)} on '{name}' was not injected.", this);
                return;
            }

            _matchEnded.Publish(new MatchEnded(MatchEnded.NoWinner));
        }
    }
}
