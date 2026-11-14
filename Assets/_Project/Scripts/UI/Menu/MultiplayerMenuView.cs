using Cysharp.Threading.Tasks;
using TinyTumble.Core.Network;
using TinyTumble.UI.Session;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Menu
{
    /// <summary>
    /// Главное меню: «Играть» создаёт игру (хост; соло — тоже хост), «Найти игру» ищет лобби с другими игроками
    /// (сначала поднимает своё), «Подключиться» входит к хосту по IP. Показывает, почему закончилась прошлая сессия.
    /// </summary>
    public sealed class MultiplayerMenuView : MonoBehaviour
    {
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _findButton;
        [SerializeField] private InputField _addressField;
        [SerializeField] private Text _statusLabel;
        [SerializeField] private string _connectingText = "Подключение…";
        [SerializeField] private string _searchUnavailableText = "Поиск игры недоступен: запустите Steam.";

        private ISessionLauncher _launcher;
        private INetworkSession _session;
        private IMatchmaking _matchmaking;

        [Inject]
        public void Construct(ISessionLauncher launcher, INetworkSession session, IMatchmaking matchmaking)
        {
            _launcher = launcher;
            _session = session;
            _matchmaking = matchmaking;
            SetStatus(DisconnectReasonTexts.Get(_session.LastDisconnectReason));
        }

        private void Awake()
        {
            _hostButton.onClick.AddListener(OnHostClicked);
            _joinButton.onClick.AddListener(OnJoinClicked);
            if (_findButton != null)
            {
                _findButton.onClick.AddListener(OnFindClicked);
            }
        }

        private void OnDestroy()
        {
            _hostButton.onClick.RemoveListener(OnHostClicked);
            _joinButton.onClick.RemoveListener(OnJoinClicked);
            if (_findButton != null)
            {
                _findButton.onClick.RemoveListener(OnFindClicked);
            }
        }

        /// <summary>Нажать «Найти игру» так же, как мышью: через событие кнопки (для автотеста).</summary>
        public bool PressFindGame()
        {
            if (_findButton == null || !_findButton.interactable)
            {
                return false;
            }

            _findButton.onClick.Invoke();
            return true;
        }

        private void OnFindClicked()
        {
            if (!EnsureInjected() || _launcher.IsBusy)
            {
                return;
            }

            // Поиск сам поднимет лобби и переведёт в него; при неудаче объясняем почему.
            if (!_matchmaking.StartSearch())
            {
                SetStatus(_matchmaking.IsAvailable ? string.Empty : _searchUnavailableText);
            }
        }

        private void OnHostClicked()
        {
            if (!EnsureInjected() || _launcher.IsBusy)
            {
                return;
            }

            HostAsync().Forget();
        }

        private void OnJoinClicked()
        {
            if (!EnsureInjected() || _launcher.IsBusy)
            {
                return;
            }

            JoinAsync(_addressField != null ? _addressField.text : string.Empty).Forget();
        }

        private async UniTaskVoid HostAsync()
        {
            SetInteractable(false);
            SessionStartResult result = await _launcher.HostAsync();
            if (this == null)
            {
                return;
            }

            SetInteractable(true);
            if (!result.Success)
            {
                SetStatus(DisconnectReasonTexts.Get(result.Reason));
            }
        }

        private async UniTaskVoid JoinAsync(string address)
        {
            if (!SessionEndpoint.TryParse(address, out SessionEndpoint host))
            {
                SetStatus(DisconnectReasonTexts.Get(DisconnectReasons.InvalidAddress));
                return;
            }

            SetInteractable(false);
            SetStatus(_connectingText);
            SessionStartResult result = await _launcher.JoinAsync(host);
            // При успехе сцену сменит переход в лобби хоста, и этот объект уже может быть уничтожен.
            if (this == null)
            {
                return;
            }

            SetInteractable(true);
            SetStatus(result.Success ? string.Empty : DisconnectReasonTexts.Get(result.Reason));
        }

        private bool EnsureInjected()
        {
            if (_launcher != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(MultiplayerMenuView)} on '{name}' was not injected.", this);
            return false;
        }

        private void SetInteractable(bool interactable)
        {
            _hostButton.interactable = interactable;
            _joinButton.interactable = interactable;
            if (_findButton != null)
            {
                _findButton.interactable = interactable;
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }
    }
}
