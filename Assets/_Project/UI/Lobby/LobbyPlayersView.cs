using System.Collections.Generic;
using System.Text;
using TinyTumble.Core.Input;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Lobby
{
    /// <summary>
    /// Список присоединившихся игроков и подсказка, как войти.
    /// </summary>
    public sealed class LobbyPlayersView : MonoBehaviour
    {
        [SerializeField] private Text _label;
        [SerializeField, TextArea] private string _joinHint = "Нажмите A / Start на геймпаде или Enter / Пробел на клавиатуре, чтобы войти";
        [SerializeField] private string _emptySlot = "— свободно —";

        private readonly StringBuilder _builder = new StringBuilder();
        private ILocalPlayers _players;

        [Inject]
        public void Construct(ILocalPlayers players)
        {
            _players = players;
            _players.PlayerJoined += OnPlayerJoined;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_players != null)
            {
                _players.PlayerJoined -= OnPlayerJoined;
            }
        }

        private void OnPlayerJoined(LocalPlayer player)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_label == null || _players == null)
            {
                return;
            }

            IReadOnlyList<LocalPlayer> players = _players.Players;
            _builder.Clear();
            _builder.Append(_joinHint).AppendLine().AppendLine();
            for (int i = 0; i < _players.MaxPlayers; i++)
            {
                _builder.Append('P').Append(i + 1).Append(": ");
                if (i < players.Count)
                {
                    _builder.Append(players[i].DeviceName);
                }
                else
                {
                    _builder.Append(_emptySlot);
                }

                _builder.AppendLine();
            }

            _label.text = _builder.ToString();
        }
    }
}
