using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Spawning;
using TinyTumble.Networking.Transport;
using Unity.Netcode;
using UnityEngine;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Сессия NGO. Как адресуется хост (IP, позже Steam и Relay), решает <see cref="ISessionTransport"/>;
    /// игровой код работает только с <see cref="INetworkSession"/>.
    /// Обрыв связи у клиента не заканчивает сессию сразу: его получает <see cref="SessionRecovery"/>
    /// (переподключение или миграция хоста), и только если она не удалась — <see cref="ConnectionLost"/>.
    /// </summary>
    public sealed class NetworkSession : INetworkSession, IDisposable
    {
        private const string LogTag = "Net";

        private readonly NetworkRuntime _runtime;
        private readonly ISessionTransport _transport;
        private readonly NetworkPrefabRegistry _prefabs;
        private readonly SessionLobby _lobby;
        private readonly SessionConfig _config;
        private readonly ILocalPlayers _localPlayers;
        private readonly PlayerIdentity _identity;
        private readonly NetworkSimulatorControl _simulator;
        private readonly IGameLog _log;
        private readonly ReactiveProperty<SessionRole> _role = new ReactiveProperty<SessionRole>(SessionRole.None);

        private bool _subscribed;
        private bool _connecting;
        private bool _connectFinished;
        private string _connectFailure;
        private SessionEndpoint _server;

        public NetworkSession(
            NetworkRuntime runtime,
            ISessionTransport transport,
            NetworkPrefabRegistry prefabs,
            SessionLobby lobby,
            SessionConfig config,
            ILocalPlayers localPlayers,
            PlayerIdentity identity,
            NetworkSimulatorControl simulator,
            IGameLog log)
        {
            _runtime = runtime;
            _transport = transport;
            _prefabs = prefabs;
            _lobby = lobby;
            _config = config;
            _localPlayers = localPlayers;
            _identity = identity;
            _simulator = simulator;
            _log = log;
            HostPort = config.Port;
        }

        public event Action<ulong> ClientConnected;

        public event Action<ulong> ClientDisconnected;

        public event Action<string> ConnectionLost;

        /// <summary>Клиент потерял связь с хостом не по своей воле и не потому, что хост его не пускает.</summary>
        public event Action<DropInfo> Dropped;

        /// <summary>Сессию закрыли сознательно (меню, тест): восстановление нужно прервать.</summary>
        public event Action ShutdownRequested;

        public ReadOnlyReactiveProperty<SessionRole> Role => _role;

        public bool IsHost => _role.Value == SessionRole.Host;

        public bool IsRemoteClient => _role.Value == SessionRole.Client;

        public bool IsRecovering { get; private set; }

        public ulong LocalClientId => _runtime.IsRunning ? _runtime.Manager.LocalClientId : NetworkManager.ServerClientId;

        public string LastDisconnectReason { get; private set; } = DisconnectReasons.None;

        /// <summary>
        /// Порт, на котором эта машина поднимает хост по IP, в том числе став преемником. Сообщается хосту при
        /// подключении. По умолчанию из конфига; несколько копий игры на одной машине задают разные.
        /// </summary>
        public ushort HostPort { get; set; }

        public UniTask<SessionStartResult> StartHostAsync(CancellationToken cancellationToken)
        {
            return StartHostAsync(HostSeed.NewSession(NewSessionToken()), cancellationToken);
        }

        /// <summary>Хост с заданным началом: новая игра или продолжение сессии после миграции.</summary>
        public async UniTask<SessionStartResult> StartHostAsync(HostSeed seed, CancellationToken cancellationToken)
        {
            if (_role.Value != SessionRole.None || _connecting)
            {
                return SessionStartResult.Fail(DisconnectReasons.AlreadyRunning);
            }

            _connecting = true;
            try
            {
                await PrepareAsync(cancellationToken);

                NetworkManager manager = _runtime.Manager;
                _transport.ConfigureHost(HostPort);
                _transport.ConfigureTimeouts(_config.ConnectTimeout, _config.DisconnectTimeout);
                manager.NetworkConfig.ConnectionData = BuildPayload(0);
                Subscribe();

                if (!manager.StartHost())
                {
                    Unsubscribe();
                    return SessionStartResult.Fail(DisconnectReasons.StartFailed);
                }

                LastDisconnectReason = DisconnectReasons.None;
                _role.Value = SessionRole.Host;
                _simulator.Apply(_runtime.Transport);

                SessionNetworkState statePrefab = _prefabs.FindPrefab<SessionNetworkState>();
                if (statePrefab == null)
                {
                    _log.Error(LogTag, $"No network prefab with {nameof(SessionNetworkState)} (address '{Core.Assets.AddressKeys.SessionState}').");
                    Shutdown();
                    return SessionStartResult.Fail(DisconnectReasons.StartFailed);
                }

                _lobby.SpawnState(statePrefab, seed);
                _log.Info(LogTag, seed.HasSlots ? $"Hosting on port {HostPort} as the new host of the session." : $"Hosting on port {HostPort}.");
                return SessionStartResult.Ok;
            }
            finally
            {
                _connecting = false;
            }
        }

        public UniTask<SessionStartResult> StartClientAsync(SessionEndpoint host, CancellationToken cancellationToken)
        {
            return ConnectAsync(host.WithDefaultPort(_config.Port), _config.ConnectTimeout, 0, cancellationToken);
        }

        /// <summary>
        /// Подключение к хосту. sessionToken не 0 — возвращаемся в конкретную сессию (после обрыва или к преемнику).
        /// </summary>
        public async UniTask<SessionStartResult> ConnectAsync(SessionEndpoint host, float timeout, ulong sessionToken, CancellationToken cancellationToken)
        {
            if (_role.Value != SessionRole.None || _connecting)
            {
                return SessionStartResult.Fail(DisconnectReasons.AlreadyRunning);
            }

            if (!_transport.Supports(host.Kind))
            {
                return SessionStartResult.Fail(DisconnectReasons.InvalidAddress);
            }

            _connecting = true;
            try
            {
                await PrepareAsync(cancellationToken);

                NetworkManager manager = _runtime.Manager;
                if (!_transport.TryConfigureClient(host))
                {
                    return SessionStartResult.Fail(DisconnectReasons.InvalidAddress);
                }

                _transport.ConfigureTimeouts(timeout, _config.DisconnectTimeout);
                manager.NetworkConfig.ConnectionData = BuildPayload(sessionToken);
                _connectFinished = false;
                _connectFailure = null;
                Subscribe();

                if (!manager.StartClient())
                {
                    Unsubscribe();
                    return SessionStartResult.Fail(DisconnectReasons.StartFailed);
                }

                _simulator.Apply(_runtime.Transport);
                _log.Info(LogTag, $"Connecting to {host}...");

                float deadline = Time.realtimeSinceStartup + timeout;
                while (!_connectFinished && Time.realtimeSinceStartup < deadline)
                {
                    await UniTask.Yield(cancellationToken);
                }

                if (!_connectFinished || _connectFailure != null)
                {
                    string reason = _connectFailure ?? DisconnectReasons.ConnectFailed;
                    StopManager();
                    _role.Value = SessionRole.None;
                    LastDisconnectReason = reason;
                    _log.Info(LogTag, $"Connection to {host} failed: {reason}.");
                    return SessionStartResult.Fail(reason);
                }

                _server = host;
                LastDisconnectReason = DisconnectReasons.None;
                _log.Info(LogTag, $"Connected as client {manager.LocalClientId}.");
                return SessionStartResult.Ok;
            }
            catch (OperationCanceledException)
            {
                StopManager();
                _role.Value = SessionRole.None;
                throw;
            }
            finally
            {
                _connecting = false;
            }
        }

        public void Shutdown()
        {
            ShutdownRequested?.Invoke();
            if (_role.Value == SessionRole.None && !_connecting)
            {
                return;
            }

            StopManager();
            _role.Value = SessionRole.None;
            _log.Info(LogTag, "Session closed.");
        }

        public void Dispose()
        {
            Unsubscribe();
            _role.Dispose();
        }

        /// <summary>Восстановление началось или закончилось (для UI и тестов).</summary>
        public void SetRecovering(bool recovering)
        {
            IsRecovering = recovering;
        }

        /// <summary>Восстановление не удалось: сессия закончена окончательно.</summary>
        public void EndAfterFailedRecovery(string reason)
        {
            LastDisconnectReason = reason;
            _log.Warning(LogTag, $"Connection lost: {reason}.");
            ConnectionLost?.Invoke(reason);
        }

        private static ulong NewSessionToken()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            ulong token = BitConverter.ToUInt64(bytes, 0);
            return token != 0 ? token : 1;
        }

        private async UniTask PrepareAsync(CancellationToken cancellationToken)
        {
            NetworkManager manager = _runtime.Manager;
            // Shutdown в NGO завершается не мгновенно: новая сессия должна дождаться конца старой.
            while (manager.ShutdownInProgress || manager.IsListening)
            {
                await UniTask.Yield(cancellationToken);
            }

            await _prefabs.EnsureRegisteredAsync(cancellationToken);
        }

        private byte[] BuildPayload(ulong sessionToken)
        {
            return new ConnectionPayload(_config.ProtocolVersion, _localPlayers.Players.Count, _identity.Key, HostPort, sessionToken).ToBytes();
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            NetworkManager manager = _runtime.Manager;
            manager.ConnectionApprovalCallback = OnConnectionApproval;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            _subscribed = false;
            NetworkManager manager = _runtime.Manager;
            manager.ConnectionApprovalCallback = null;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            manager.OnTransportFailure -= OnTransportFailure;
        }

        private void StopManager()
        {
            Unsubscribe();
            NetworkManager manager = _runtime.Manager;
            if (manager.IsListening && !manager.ShutdownInProgress)
            {
                manager.Shutdown();
            }
        }

        private void OnConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }

            bool valid = ConnectionPayload.TryParse(request.Payload, out ConnectionPayload payload);
            string reason = ConnectionAdmission.Evaluate(payload, valid, _config.ProtocolVersion, _lobby.SessionToken,
                valid && _lobby.HasReservation(payload.PlayerKey), _lobby.FreeSlots, _lobby.IsAcceptingPlayers);
            response.Approved = string.IsNullOrEmpty(reason);
            response.Reason = reason;
            if (response.Approved)
            {
                _lobby.RegisterApproved(request.ClientNetworkId, payload);
            }
            else
            {
                _log.Info(LogTag, $"Client {request.ClientNetworkId} rejected: {reason}.");
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            NetworkManager manager = _runtime.Manager;
            if (_connecting && !manager.IsServer && clientId == manager.LocalClientId)
            {
                // Роль ставим сразу, а не в ожидающем цикле: объект сессии уже заспавнен и клиент вот-вот
                // начнёт грузить сцену хоста, где кто-то может спросить роль раньше следующего кадра.
                _connectFinished = true;
                _role.Value = SessionRole.Client;
                return;
            }

            if (manager.IsServer && clientId != NetworkManager.ServerClientId)
            {
                _log.Info(LogTag, $"Client {clientId} connected.");
                ClientConnected?.Invoke(clientId);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            NetworkManager manager = _runtime.Manager;
            if (manager.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId)
                {
                    _log.Info(LogTag, $"Client {clientId} disconnected.");
                    ClientDisconnected?.Invoke(clientId);
                }

                return;
            }

            // У клиента колбэк про себя самого означает потерю связи с хостом или отказ в подключении.
            // Свой код причины хост присылает при отказе и кике; иначе NGO пишет туда служебный текст.
            string serverReason = manager.DisconnectReason;
            bool known = DisconnectReasons.IsKnown(serverReason);
            if (!known && !string.IsNullOrEmpty(serverReason))
            {
                _log.Info(LogTag, $"Disconnect details: {serverReason}");
            }

            if (_connecting)
            {
                _connectFailure = known ? serverReason : DisconnectReasons.ConnectFailed;
                _connectFinished = true;
                return;
            }

            if (known && DisconnectReasons.IsRejection(serverReason))
            {
                // Хост нас выгнал сознательно: возвращаться некуда.
                EndClientSession();
                EndAfterFailedRecovery(serverReason);
                return;
            }

            DropClientSession();
        }

        private void OnTransportFailure()
        {
            if (_connecting)
            {
                _connectFailure = DisconnectReasons.ConnectFailed;
                _connectFinished = true;
                return;
            }

            if (_role.Value == SessionRole.Host)
            {
                StopManager();
                _role.Value = SessionRole.None;
                EndAfterFailedRecovery(DisconnectReasons.StartFailed);
                return;
            }

            DropClientSession();
        }

        private void DropClientSession()
        {
            if (_role.Value != SessionRole.Client)
            {
                return;
            }

            EndClientSession();
            var drop = new DropInfo(_server);
            if (Dropped == null)
            {
                EndAfterFailedRecovery(DisconnectReasons.HostLeft);
                return;
            }

            _log.Warning(LogTag, $"Lost connection to the host {_server}.");
            Dropped.Invoke(drop);
        }

        private void EndClientSession()
        {
            StopManager();
            _role.Value = SessionRole.None;
        }
    }

    /// <summary>Куда клиент был подключён, когда связь пропала.</summary>
    public readonly struct DropInfo
    {
        public readonly SessionEndpoint Host;

        public DropInfo(SessionEndpoint host)
        {
            Host = host;
        }
    }
}
