using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Flow
{
    public sealed class SessionLauncher : ISessionLauncher, IDisposable
    {
        private readonly INetworkSession _session;
        private readonly IGameFlow _flow;
        private readonly ILocalPlayers _localPlayers;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public SessionLauncher(INetworkSession session, IGameFlow flow, ILocalPlayers localPlayers)
        {
            _session = session;
            _flow = flow;
            _localPlayers = localPlayers;
        }

        public bool IsBusy { get; private set; }

        public async UniTask<SessionStartResult> HostAsync()
        {
            if (IsBusy)
            {
                return SessionStartResult.Fail(DisconnectReasons.AlreadyRunning);
            }

            IsBusy = true;
            try
            {
                SessionStartResult result = await _session.StartHostAsync(_lifetime.Token);
                if (result.Success)
                {
                    await _flow.GoToAsync(GameState.Lobby);
                }

                return result;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async UniTask<SessionStartResult> JoinAsync(string address)
        {
            if (IsBusy)
            {
                return SessionStartResult.Fail(DisconnectReasons.AlreadyRunning);
            }

            IsBusy = true;
            try
            {
                // Кнопку нажали мышью, а игрока ещё нет: входим с клавиатуры, чтобы хост сразу выдал слот.
                // Геймпады присоединятся в лобби как обычно.
                _localPlayers.TryJoinDefault();

                // В лобби клиента переведёт NetworkGameFlow, как только узнает состояние хоста.
                return await _session.StartClientAsync(address, _lifetime.Token);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
