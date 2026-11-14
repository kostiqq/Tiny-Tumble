using TinyTumble.Core.Network;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Lobby
{
    /// <summary>
    /// «Пригласить»: открывает оверлей Steam с приглашением в текущую сессию. Видна и хосту, и клиентам
    /// (в лобби Steam «только для друзей» приглашать может любой участник), но только пока пригласить есть куда:
    /// сессия идёт через Steam и лобби Steam открыто. Лобби создаётся асинхронно, а после миграции хоста
    /// пересоздаётся, поэтому доступность опрашивается каждый кадр — это одно чтение bool.
    /// Прячется через <see cref="CanvasGroup"/>: выключенный объект не получал бы Update.
    /// </summary>
    public sealed class LobbyInviteButton : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private CanvasGroup _group;

        private ISessionInvites _invites;
        private bool _shown = true;

        /// <summary>Кнопку видно и можно нажать (для автотеста).</summary>
        public bool IsShown => _shown;

        [Inject]
        public void Construct(ISessionInvites invites)
        {
            _invites = invites;
        }

        private void Reset()
        {
            _button = GetComponent<Button>();
            _group = GetComponent<CanvasGroup>();
        }

        private void Awake()
        {
            // До внедрения (и без Steam) кнопки не видно.
            SetShown(false);
        }

        private void OnEnable()
        {
            _button.onClick.AddListener(OnClick);
        }

        private void OnDisable()
        {
            _button.onClick.RemoveListener(OnClick);
        }

        private void Update()
        {
            SetShown(_invites != null && _invites.CanInvite);
        }

        private void OnClick()
        {
            if (_invites != null)
            {
                _invites.OpenInviteDialog();
            }
        }

        private void SetShown(bool shown)
        {
            if (_shown == shown)
            {
                return;
            }

            _shown = shown;
            _group.alpha = shown ? 1f : 0f;
            _group.interactable = shown;
            _group.blocksRaycasts = shown;
        }
    }
}
