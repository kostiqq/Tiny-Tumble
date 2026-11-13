using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Core.Scenes;
using TinyTumble.Networking.Lobby;
using UnityEngine;
using VContainer.Unity;

namespace TinyTumble.Networking.Session
{
    /// <summary>
    /// Что делает клиент, потерявший связь с хостом:
    /// 1. Если хост не объявлял, что уходит, — несколько секунд пытается вернуться к нему (обрыв мог быть у нас).
    /// 2. Идёт по плану миграции: первый кандидат поднимает хост с прежним составом, остальные подключаются к нему;
    ///    если кандидат не отвечает, следующий по списку берёт роль на себя. Список у всех одинаковый.
    /// 3. Не вышло — главное меню с причиной «хост ушёл».
    /// После миграции все оказываются в лобби: идущий матч прерывается, слоты и цвета сохраняются.
    /// </summary>
    public sealed class SessionRecovery : IInitializable, IDisposable
    {
        private const string LogTag = "Net";
        private const float ConnectAttemptTimeout = 3f;
        private const float RetryDelay = 0.5f;

        private readonly NetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly PlayerIdentity _identity;
        private readonly IGameFlow _flow;
        private readonly ILoadingScreen _loadingScreen;
        private readonly SessionConfig _config;
        private readonly IGameLog _log;
        private CancellationTokenSource _recovery;

        public SessionRecovery(
            NetworkSession session,
            SessionLobby lobby,
            PlayerIdentity identity,
            IGameFlow flow,
            ILoadingScreen loadingScreen,
            SessionConfig config,
            IGameLog log)
        {
            _session = session;
            _lobby = lobby;
            _identity = identity;
            _flow = flow;
            _loadingScreen = loadingScreen;
            _config = config;
            _log = log;
        }

        public void Initialize()
        {
            _session.Dropped += OnDropped;
            _session.ShutdownRequested += Cancel;
        }

        public void Dispose()
        {
            _session.Dropped -= OnDropped;
            _session.ShutdownRequested -= Cancel;
            Cancel();
        }

        private void OnDropped(DropInfo drop)
        {
            Cancel();
            _recovery = new CancellationTokenSource();
            RecoverAsync(drop, _recovery.Token).Forget();
        }

        private void Cancel()
        {
            if (_recovery != null)
            {
                _recovery.Cancel();
                _recovery.Dispose();
                _recovery = null;
            }
        }

        private async UniTaskVoid RecoverAsync(DropInfo drop, CancellationToken token)
        {
            bool hostLeft = _lobby.ConsumeHostLeaving();
            SlotTable slots = _lobby.LastKnownSlots;
            MigrationPlan plan = _lobby.LastKnownPlan;

            _session.SetRecovering(true);
            IDisposable hold = _loadingScreen.Hold(hostLeft ? "Хост вышел. Переносим игру к новому хосту…" : "Связь с хостом потеряна. Переподключаемся…");
            try
            {
                if (!hostLeft)
                {
                    _log.Info(LogTag, "Trying to get back to the host...");
                    if (await TryConnectAsync(drop.HostIp, drop.HostPort, plan.SessionToken, _config.ReconnectWindow, token))
                    {
                        _log.Info(LogTag, "Reconnected to the host.");
                        return;
                    }
                }

                List<RecoveryStep> steps = BuildSteps(plan, _identity.Key);
                for (int i = 0; i < steps.Count; i++)
                {
                    RecoveryStep step = steps[i];
                    if (step.BecomeHost)
                    {
                        if (await BecomeHostAsync(plan.SessionToken, slots, token))
                        {
                            return;
                        }

                        break;
                    }

                    _log.Info(LogTag, $"Looking for the new host at {step.Address}:{step.Port}...");
                    if (await TryConnectAsync(step.Address, step.Port, plan.SessionToken, _config.MigrationConnectTimeout, token))
                    {
                        _log.Info(LogTag, "Joined the new host.");
                        return;
                    }
                }

                _session.EndAfterFailedRecovery(DisconnectReasons.HostLeft);
            }
            catch (OperationCanceledException)
            {
                // Игрок сам ушёл в меню, пока мы восстанавливались.
            }
            finally
            {
                hold.Dispose();
                _session.SetRecovering(false);
            }
        }

        private async UniTask<bool> BecomeHostAsync(ulong sessionToken, SlotTable slots, CancellationToken token)
        {
            _log.Info(LogTag, "We are next in line: becoming the host.");
            SessionStartResult result = await _session.StartHostAsync(HostSeed.Migrated(sessionToken, slots), token);
            if (!result.Success)
            {
                _log.Warning(LogTag, $"Could not become the host: {result.Reason}.");
                return false;
            }

            // Новый хост ведёт всех в лобби: матч старого хоста продолжить нельзя.
            await _flow.GoToAsync(GameState.Lobby);
            return true;
        }

        /// <summary>Повторяет попытки до конца окна. Отказ хоста (сессия не та, нет мест) прекращает попытки сразу.</summary>
        private async UniTask<bool> TryConnectAsync(string ip, ushort port, ulong sessionToken, float window, CancellationToken token)
        {
            if (string.IsNullOrEmpty(ip) || port == 0)
            {
                return false;
            }

            float deadline = Time.realtimeSinceStartup + window;
            while (Time.realtimeSinceStartup < deadline)
            {
                float attempt = Mathf.Min(ConnectAttemptTimeout, Mathf.Max(0.5f, deadline - Time.realtimeSinceStartup));
                SessionStartResult result = await _session.ConnectAsync(ip, port, attempt, sessionToken, token);
                if (result.Success)
                {
                    return true;
                }

                if (DisconnectReasons.IsRejection(result.Reason))
                {
                    _log.Info(LogTag, $"{ip}:{port} refused us: {result.Reason}.");
                    return false;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(RetryDelay), ignoreTimeScale: true, cancellationToken: token);
            }

            return false;
        }

        /// <summary>
        /// Шаги по плану: подключаться к кандидатам по порядку, пока очередь не дойдёт до нас — тогда стать хостом.
        /// Если нас нет в плане, остаются только попытки подключиться.
        /// </summary>
        public static List<RecoveryStep> BuildSteps(MigrationPlan plan, ulong ownKey)
        {
            var steps = new List<RecoveryStep>(plan.Count);
            for (int i = 0; i < plan.Count; i++)
            {
                MigrationCandidate candidate = plan[i];
                if (candidate.PlayerKey == ownKey)
                {
                    steps.Add(RecoveryStep.Host());
                    break;
                }

                steps.Add(RecoveryStep.Connect(candidate.Address, candidate.Port));
            }

            return steps;
        }
    }

    public readonly struct RecoveryStep
    {
        public readonly bool BecomeHost;
        public readonly string Address;
        public readonly ushort Port;

        private RecoveryStep(bool becomeHost, string address, ushort port)
        {
            BecomeHost = becomeHost;
            Address = address;
            Port = port;
        }

        public static RecoveryStep Host()
        {
            return new RecoveryStep(true, null, 0);
        }

        public static RecoveryStep Connect(string address, ushort port)
        {
            return new RecoveryStep(false, address, port);
        }
    }
}
