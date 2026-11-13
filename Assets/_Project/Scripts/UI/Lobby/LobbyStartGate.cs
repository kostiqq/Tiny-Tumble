using TinyTumble.Core.Network;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Lobby
{
    /// <summary>
    /// Кнопка «Начать матч» доступна, когда все подключённые клиенты нажали «Готов».
    /// Подписка живёт, пока кнопка включена: у клиента она скрыта с рождения и OnDestroy не получит.
    /// </summary>
    public sealed class LobbyStartGate : MonoBehaviour
    {
        [SerializeField] private Button _button;

        private ILobby _lobby;
        private bool _subscribed;

        [Inject]
        public void Construct(ILobby lobby)
        {
            _lobby = lobby;
            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void Reset()
        {
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                _subscribed = false;
                _lobby.Changed -= Refresh;
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _lobby == null)
            {
                return;
            }

            _subscribed = true;
            _lobby.Changed += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            _button.interactable = _lobby.AreAllReady;
        }
    }
}
