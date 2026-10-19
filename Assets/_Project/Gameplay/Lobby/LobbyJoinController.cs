using System;
using TinyTumble.Core.Input;
using VContainer.Unity;

namespace TinyTumble.Gameplay.Lobby
{
    /// <summary>
    /// Присоединение новых устройств разрешено только пока открыто лобби.
    /// </summary>
    public sealed class LobbyJoinController : IStartable, IDisposable
    {
        private readonly ILocalPlayers _localPlayers;

        public LobbyJoinController(ILocalPlayers localPlayers)
        {
            _localPlayers = localPlayers;
        }

        public void Start()
        {
            _localPlayers.JoiningEnabled = true;
        }

        public void Dispose()
        {
            _localPlayers.JoiningEnabled = false;
        }
    }
}
