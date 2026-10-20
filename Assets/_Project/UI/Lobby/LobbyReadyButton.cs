using TinyTumble.Core.Network;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Lobby
{
    /// <summary>
    /// «Готов» / «Не готов» у клиента. У хоста кнопка скрыта (<see cref="Session.SessionRoleVisibility"/>):
    /// он сам нажимает «Начать матч». Подписка живёт, пока кнопка включена.
    /// </summary>
    public sealed class LobbyReadyButton : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Text _label;
        [SerializeField] private string _readyText = "Готов";
        [SerializeField] private string _notReadyText = "Не готов";

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
            _label = GetComponentInChildren<Text>();
        }

        private void OnEnable()
        {
            _button.onClick.AddListener(OnClick);
            Subscribe();
        }

        private void OnDisable()
        {
            _button.onClick.RemoveListener(OnClick);
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

        private void OnClick()
        {
            if (_lobby != null)
            {
                _lobby.SetLocalReady(!_lobby.IsLocalReady);
            }
        }

        private void Refresh()
        {
            if (_label != null)
            {
                // Подпись — действие, которое сделает кнопка.
                _label.text = _lobby.IsLocalReady ? _notReadyText : _readyText;
            }
        }
    }
}
