using TinyTumble.Core.Network;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Lobby
{
    /// <summary>
    /// Поиск игры в лобби: строка состояния с таймером до старта с ботами, «Найти игру», «Отменить поиск»
    /// и «Уйти своей пати» (лидер пати, которая гостит у чужого хоста). Кнопки, которые сейчас ни к чему,
    /// прячутся через <see cref="CanvasGroup"/>: выключенный объект не получал бы Update.
    /// Состояние опрашивается каждый кадр (несколько чтений), а текст пересобирается, только когда меняется
    /// состояние или целая секунда таймера.
    /// </summary>
    public sealed class LobbyMatchmakingPanel : MonoBehaviour
    {
        [SerializeField] private Text _status;
        [SerializeField] private Button _findButton;
        [SerializeField] private CanvasGroup _findGroup;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private CanvasGroup _cancelGroup;
        [SerializeField] private Button _regroupButton;
        [SerializeField] private CanvasGroup _regroupGroup;

        [SerializeField] private string _searchingText = "Ищем игру… Матч с ботами через {0} с";
        [SerializeField] private string _joiningText = "Нашли лобби, переезжаем всей пати…";
        [SerializeField] private string _waitingText = "Ждём игроков. Матч через {0} с, пустые места займут боты";
        [SerializeField] private string _memberText = "Лидер ищет игру. Матч через {0} с";
        [SerializeField] private string _matchmadeText = "Игра найдена поиском";
        [SerializeField] private string _partyFullText = "Пати в сборе — можно начинать";

        private IMatchmaking _matchmaking;
        private IParty _party;
        private INetworkSession _session;
        private ILobby _lobby;

        private MatchmakingState _shownState = (MatchmakingState)(-1);
        private int _shownSeconds = int.MinValue;
        private bool _shownLeader;
        private bool _shownMatchmade;
        private bool _shownFull;

        /// <summary>Текущая строка состояния (для автотеста).</summary>
        public string Status => _status != null ? _status.text : string.Empty;

        /// <summary>Кнопку «Отменить поиск» видно (для автотеста).</summary>
        public bool IsCancelShown => _cancelGroup != null && _cancelGroup.interactable;

        [Inject]
        public void Construct(IMatchmaking matchmaking, IParty party, INetworkSession session, ILobby lobby)
        {
            _matchmaking = matchmaking;
            _party = party;
            _session = session;
            _lobby = lobby;
        }

        private void Reset()
        {
            _status = GetComponentInChildren<Text>();
        }

        private void Awake()
        {
            SetShown(_findGroup, false);
            SetShown(_cancelGroup, false);
            SetShown(_regroupGroup, false);
        }

        private void OnEnable()
        {
            _findButton.onClick.AddListener(OnFind);
            _cancelButton.onClick.AddListener(OnCancel);
            _regroupButton.onClick.AddListener(OnRegroup);
        }

        private void OnDisable()
        {
            _findButton.onClick.RemoveListener(OnFind);
            _cancelButton.onClick.RemoveListener(OnCancel);
            _regroupButton.onClick.RemoveListener(OnRegroup);
        }

        private void Update()
        {
            if (_matchmaking == null)
            {
                return;
            }

            MatchmakingState state = _matchmaking.State;
            bool searchingHere = state != MatchmakingState.Idle;
            SetShown(_findGroup, !searchingHere && _matchmaking.CanStartSearch);
            SetShown(_cancelGroup, searchingHere && state != MatchmakingState.Joining);
            SetShown(_regroupGroup, !searchingHere && _party.CanRegroup);
            RefreshStatus(state);
        }

        private void RefreshStatus(MatchmakingState state)
        {
            float secondsLeft = _matchmaking.SecondsLeft;
            int seconds = secondsLeft >= 0f ? Mathf.CeilToInt(secondsLeft) : -1;
            bool leader = _party.IsLeader;
            bool matchmade = _matchmaking.IsMatchmadeSession;
            bool full = _session.IsHost && _lobby.Slots.Count >= _lobby.Capacity;
            if (state == _shownState && seconds == _shownSeconds && leader == _shownLeader && matchmade == _shownMatchmade && full == _shownFull)
            {
                return;
            }

            _shownState = state;
            _shownSeconds = seconds;
            _shownLeader = leader;
            _shownMatchmade = matchmade;
            _shownFull = full;
            _status.text = StatusText(state, seconds, matchmade, full);
        }

        private string StatusText(MatchmakingState state, int seconds, bool matchmade, bool full)
        {
            switch (state)
            {
                case MatchmakingState.Searching:
                    return string.Format(_searchingText, Mathf.Max(0, seconds));
                case MatchmakingState.Joining:
                    return _joiningText;
                case MatchmakingState.WaitingAsHost:
                    return string.Format(_waitingText, Mathf.Max(0, seconds));
            }

            // Поиск ведёт кто-то другой (лидер или хост сессии): видим только таймер.
            if (seconds >= 0)
            {
                return string.Format(matchmade && _session.IsRemoteClient ? _waitingText : _memberText, seconds);
            }

            if (matchmade)
            {
                return _matchmadeText;
            }

            return full ? _partyFullText : string.Empty;
        }

        private void OnFind()
        {
            if (_matchmaking != null)
            {
                _matchmaking.StartSearch();
            }
        }

        private void OnCancel()
        {
            if (_matchmaking != null)
            {
                _matchmaking.Cancel();
            }
        }

        private void OnRegroup()
        {
            if (_party != null)
            {
                _party.Regroup();
            }
        }

        private static void SetShown(CanvasGroup group, bool shown)
        {
            if (group == null || group.interactable == shown)
            {
                return;
            }

            group.alpha = shown ? 1f : 0f;
            group.interactable = shown;
            group.blocksRaycasts = shown;
        }
    }
}
