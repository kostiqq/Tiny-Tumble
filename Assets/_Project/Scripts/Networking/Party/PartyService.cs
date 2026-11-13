using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Core.Scenes;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Session;
using VContainer.Unity;

namespace TinyTumble.Networking.Party
{
    /// <summary>
    /// Пати и её переезды. Лидер командует (<see cref="MoveTo"/>, <see cref="Regroup"/>), хост сессии рассылает
    /// команду, и каждый участник сам закрывает старую сессию и подключается к новому хосту со своей пати —
    /// хост примет её целиком или откажет всей. Не вышло — пати собирается у лидера: он поднимает свою сессию
    /// тем же транспортом, остальные подключаются к нему по адресу, который знали из старой сессии.
    /// Переезжают только из лобби: сцена остаётся той же, меняется только сессия под ней.
    /// </summary>
    public sealed class PartyService : IParty, IInitializable, IDisposable
    {
        private const string LogTag = "Party";

        // Хост, чья пати уезжает, даёт командам уйти в сеть до закрытия своей сессии.
        private const float HostDepartureDelay = 0.2f;

        private readonly PartyState _state;
        private readonly NetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly PlayerIdentity _identity;
        private readonly ILoadingScreen _loadingScreen;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;
        private CancellationTokenSource _move;
        private bool _closingOwnSession;

        public PartyService(
            PartyState state,
            NetworkSession session,
            SessionLobby lobby,
            PlayerIdentity identity,
            ILoadingScreen loadingScreen,
            SessionConfig config,
            IGameLog log)
        {
            _state = state;
            _session = session;
            _lobby = lobby;
            _identity = identity;
            _loadingScreen = loadingScreen;
            _config = config;
            _log = log;
        }

        public event Action MoveStarted;

        public event Action MoveFinished;

        public ulong PartyId => _state.PartyId;

        public bool IsLeader => _state.IsLeader;

        public bool IsMoving { get; private set; }

        /// <summary>Сколько переездов закончилось с запуска (для автотеста).</summary>
        public int CompletedMoves { get; private set; }

        public bool CanMove => _state.IsLeader && !IsMoving && !_session.IsRecovering
            && _session.Role.CurrentValue != SessionRole.None && _lobby.IsAttached && _lobby.HostState == GameState.Lobby;

        public bool CanRegroup => CanMove && _session.IsRemoteClient;

        public void Initialize()
        {
            _lobby.PartyMoveStarted += OnPartyMove;
            _session.ShutdownRequested += OnShutdownRequested;
        }

        public void Dispose()
        {
            _lobby.PartyMoveStarted -= OnPartyMove;
            _session.ShutdownRequested -= OnShutdownRequested;
            CancelMove();
        }

        public bool MoveTo(SessionEndpoint host)
        {
            if (!CanMove || !host.IsValid)
            {
                return false;
            }

            host = host.WithDefaultPort(_config.Port);
            if (_session.IsRemoteClient && host == _session.ServerEndpoint)
            {
                _log.Info(LogTag, "The party is already there.");
                return false;
            }

            return Request(host);
        }

        public bool Regroup()
        {
            return CanRegroup && Request(SessionEndpoint.None);
        }

        private bool Request(SessionEndpoint target)
        {
            if (_session.IsHost)
            {
                ulong partyId = _state.PartyId;
                return _lobby.StartPartyDeparture(new PartyMove(partyId, target, _identity.Key, _lobby.CountPartySlots(partyId)));
            }

            return _lobby.RequestPartyMove(target);
        }

        private void OnPartyMove(PartyMove move)
        {
            if (move.PartyId != _state.PartyId || IsMoving)
            {
                if (IsMoving && move.PartyId == _state.PartyId)
                {
                    _log.Info(LogTag, "Already moving, the new command is ignored.");
                }

                return;
            }

            // Флаг ставим сразу, внутри сетевого сообщения: если старый хост пропадёт раньше, чем мы сами
            // закроем сессию, восстановление не должно принять это за обрыв.
            IsMoving = true;
            bool wasHost = _session.IsHost;
            bool isLeader = move.LeaderKey == _identity.Key;
            EndpointKind transport = _session.TransportKind;
            SessionEndpoint leader = isLeader ? SessionEndpoint.None : FindLeaderEndpoint(move);

            CancelMove();
            _move = new CancellationTokenSource();
            RaiseMoveStarted();
            RunMoveAsync(move, wasHost, isLeader, transport, leader, _move.Token).Forget();
        }

        /// <summary>
        /// Куда подключаться к лидеру, если пати будет собираться у него: к хосту — по адресу, которым мы к нему
        /// подключены; к клиенту — по его адресу преемника, который хост вложил в команду (там лидер и поднимет хост).
        /// </summary>
        private SessionEndpoint FindLeaderEndpoint(PartyMove move)
        {
            if (_session.IsRemoteClient && _lobby.HostPlayerKey == move.LeaderKey)
            {
                return _session.ServerEndpoint;
            }

            return move.LeaderEndpoint;
        }

        private async UniTaskVoid RunMoveAsync(PartyMove move, bool wasHost, bool isLeader, EndpointKind transport, SessionEndpoint leader,
            CancellationToken token)
        {
            IDisposable hold = _loadingScreen.Hold(move.IsRegroup ? "Пати собирается у лидера…" : "Переезжаем вместе с пати…");
            try
            {
                // Хост ждёт, пока команда уйдёт участникам; клиент выходит из обработчика сетевого сообщения.
                if (wasHost)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(HostDepartureDelay), ignoreTimeScale: true, cancellationToken: token);
                }
                else
                {
                    await UniTask.Yield(token);
                }

                CloseOwnSession();
                PartyTicket ticket = _state.Ticket(move.PartySize);

                if (!move.IsRegroup)
                {
                    _log.Info(LogTag, $"Moving the party to {move.Target}...");
                    SessionStartResult moved = await _session.ConnectWithRetriesAsync(move.Target, _config.ConnectTimeout, 0, ticket, token);
                    if (moved.Success)
                    {
                        _log.Info(LogTag, "The party moved.");
                        return;
                    }

                    _log.Warning(LogTag, $"Could not move to {move.Target}: {moved.Reason}. Regrouping at the leader.");
                }

                await RegroupAsync(isLeader, transport, leader, ticket, token);
            }
            catch (OperationCanceledException)
            {
                // Игрок ушёл в меню посреди переезда.
            }
            finally
            {
                hold.Dispose();
                IsMoving = false;
                CompletedMoves++;
                RaiseMoveFinished();
            }
        }

        private async UniTask RegroupAsync(bool isLeader, EndpointKind transport, SessionEndpoint leader, PartyTicket ticket, CancellationToken token)
        {
            if (isLeader)
            {
                SessionStartResult hosted = await _session.StartHostAsync(transport, token);
                if (hosted.Success)
                {
                    _log.Info(LogTag, "The party regroups at us.");
                    return;
                }

                _session.EndAfterFailedRecovery(hosted.Reason);
                return;
            }

            if (!leader.IsValid)
            {
                _log.Warning(LogTag, "Do not know how to reach the party leader.");
                _session.EndAfterFailedRecovery(DisconnectReasons.HostLeft);
                return;
            }

            // Лидер сначала пробует подключиться туда же, куда и мы, и только потом поднимает хост: ждём его дольше.
            float window = _config.ConnectTimeout + _config.MigrationConnectTimeout;
            SessionStartResult joined = await _session.ConnectWithRetriesAsync(leader, window, 0, ticket, token);
            if (joined.Success)
            {
                _log.Info(LogTag, "Rejoined the party leader.");
                return;
            }

            _session.EndAfterFailedRecovery(DisconnectReasons.IsRejection(joined.Reason) ? joined.Reason : DisconnectReasons.HostLeft);
        }

        private void CloseOwnSession()
        {
            _closingOwnSession = true;
            try
            {
                _session.Shutdown();
            }
            finally
            {
                _closingOwnSession = false;
            }
        }

        private void OnShutdownRequested()
        {
            if (!_closingOwnSession)
            {
                CancelMove();
            }
        }

        private void CancelMove()
        {
            if (_move != null)
            {
                _move.Cancel();
                _move.Dispose();
                _move = null;
            }
        }

        private void RaiseMoveStarted()
        {
            try
            {
                MoveStarted?.Invoke();
            }
            catch (Exception exception)
            {
                _log.Error(LogTag, exception.ToString());
            }
        }

        private void RaiseMoveFinished()
        {
            try
            {
                MoveFinished?.Invoke();
            }
            catch (Exception exception)
            {
                _log.Error(LogTag, exception.ToString());
            }
        }
    }
}
