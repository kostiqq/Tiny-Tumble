using System.Collections.Generic;
using System.Text;
using TinyTumble.Core.Input;
using TinyTumble.Core.Network;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyTumble.UI.Lobby
{
    /// <summary>
    /// Слоты P1..P4 всей сессии: кто где сидит, чьи игроки на этой машине, кто готов. И подсказка, как войти.
    /// </summary>
    public sealed class LobbyPlayersView : MonoBehaviour
    {
        [SerializeField] private Text _label;
        [SerializeField, TextArea] private string _joinHint = "Нажмите A / Start на геймпаде или Enter / Пробел на клавиатуре, чтобы войти";
        [SerializeField] private string _emptySlot = "— свободно —";
        [SerializeField] private string _hostTag = "хост";
        [SerializeField] private string _localTag = "вы";
        [SerializeField] private string _remoteTag = "игрок по сети";
        [SerializeField] private string _readyTag = "готов";
        [SerializeField] private string _reconnectingTag = "связь потеряна, ждём возвращения";

        private readonly StringBuilder _builder = new StringBuilder();
        private ILobby _lobby;
        private ILocalPlayers _localPlayers;
        private INetworkSession _session;

        [Inject]
        public void Construct(ILobby lobby, ILocalPlayers localPlayers, INetworkSession session)
        {
            _lobby = lobby;
            _localPlayers = localPlayers;
            _session = session;
            _lobby.Changed += Refresh;
            _localPlayers.PlayerJoined += OnPlayerJoined;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_lobby != null)
            {
                _lobby.Changed -= Refresh;
            }

            if (_localPlayers != null)
            {
                _localPlayers.PlayerJoined -= OnPlayerJoined;
            }
        }

        private void OnPlayerJoined(LocalPlayer player)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_label == null || _lobby == null)
            {
                return;
            }

            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            ulong localClientId = _session.LocalClientId;

            _builder.Clear();
            _builder.Append(_joinHint).AppendLine().AppendLine();
            for (int index = 0; index < _lobby.Capacity; index++)
            {
                _builder.Append('P').Append(index + 1).Append(": ");
                if (TryGetSlot(slots, index, out PlayerSlot slot))
                {
                    if (!slot.IsConnected)
                    {
                        _builder.Append(_remoteTag).Append(", ").Append(_reconnectingTag);
                    }
                    else if (slot.ClientId == localClientId)
                    {
                        _builder.Append(_localTag).Append(" (").Append(DeviceName(slot.LocalIndex)).Append(')');
                    }
                    else
                    {
                        _builder.Append(_remoteTag);
                    }

                    if (slot.IsHost)
                    {
                        _builder.Append(", ").Append(_hostTag);
                    }
                    else if (slot.IsConnected && slot.IsReady)
                    {
                        _builder.Append(", ").Append(_readyTag);
                    }
                }
                else
                {
                    _builder.Append(_emptySlot);
                }

                _builder.AppendLine();
            }

            _label.text = _builder.ToString();
        }

        private static bool TryGetSlot(IReadOnlyList<PlayerSlot> slots, int index, out PlayerSlot slot)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Index == index)
                {
                    slot = slots[i];
                    return true;
                }
            }

            slot = default;
            return false;
        }

        private string DeviceName(int localIndex)
        {
            IReadOnlyList<LocalPlayer> players = _localPlayers.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Index == localIndex)
                {
                    return players[i].DeviceName;
                }
            }

            return "?";
        }
    }
}
