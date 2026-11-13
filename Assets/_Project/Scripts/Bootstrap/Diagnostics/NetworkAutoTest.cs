#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Messages;
using TinyTumble.Core.Network;
using TinyTumble.Core.Scenes;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using TinyTumble.Networking.Debugging;
using TinyTumble.Networking.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace TinyTumble.Bootstrap.Diagnostics
{
    /// <summary>
    /// Сквозной тест сети на нескольких запущенных сборках: всё идёт через тот же код, что и у игрока,
    /// ввод — с виртуальной клавиатуры. Сценарии описаны в <see cref="AutoTestOptions"/>.
    /// Итог — строка "[AutoTest] RESULT PASS|FAIL ..." в логе и код выхода 0/1.
    /// </summary>
    public sealed class NetworkAutoTest : IStartable, IDisposable
    {
        private const string LogTag = "AutoTest";
        private const float OverallTimeout = 150f;
        private const float StepTimeout = 60f;
        private const float MinTravel = 1f;
        private const int TargetFrameRate = 60;

        // Стол 10×10 м, скорость 5 м/с: короткого нажатия хватает на ~2 м и не хватает, чтобы упасть.
        private const float KeyHoldSeconds = 0.4f;
        private const float BeforeIncidentSeconds = 2f;
        private const float DropSeconds = 8f;
        private const float SpectateSeconds = 3f;
        private const float LingerSeconds = 3f;

        private readonly AutoTestOptions _options;
        private readonly ISessionLauncher _launcher;
        private readonly NetworkSession _session;
        private readonly ILobby _lobby;
        private readonly IGameFlow _flow;
        private readonly ILoadingScreen _loadingScreen;
        private readonly ILocalPlayers _localPlayers;
        private readonly IPublisher<MatchEnded> _matchEnded;
        private readonly NetworkSimulatorControl _simulator;
        private readonly MatchSetup _matchSetup;
        private readonly IGameLog _log;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<ulong> _clientsBuffer = new List<ulong>();
        private Keyboard _keyboard;

        public NetworkAutoTest(
            AutoTestOptions options,
            ISessionLauncher launcher,
            NetworkSession session,
            ILobby lobby,
            IGameFlow flow,
            ILoadingScreen loadingScreen,
            ILocalPlayers localPlayers,
            IPublisher<MatchEnded> matchEnded,
            NetworkSimulatorControl simulator,
            MatchSetup matchSetup,
            IGameLog log)
        {
            _options = options;
            _launcher = launcher;
            _session = session;
            _lobby = lobby;
            _flow = flow;
            _loadingScreen = loadingScreen;
            _localPlayers = localPlayers;
            _matchEnded = matchEnded;
            _simulator = simulator;
            _matchSetup = matchSetup;
            _log = log;
        }

        public void Start()
        {
            RunAsync().Forget();
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private async UniTaskVoid RunAsync()
        {
            CancellationToken token = _lifetime.Token;
            string failure;
            try
            {
                Prepare();
                UniTask<string> scenario = _options.IsHost ? RunHostAsync(token) : RunClientAsync(token);
                UniTask timeout = UniTask.Delay(TimeSpan.FromSeconds(OverallTimeout), ignoreTimeScale: true, cancellationToken: token);
                (bool finished, string result) = await UniTask.WhenAny(scenario, timeout);
                failure = finished ? result : "overall timeout";
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + ": " + exception.Message;
            }

            string role = _options.IsHost ? "host" : "client";
            if (failure == null)
            {
                _log.Info(LogTag, $"[AutoTest] RESULT PASS {role} ({_options.Scenario})");
            }
            else
            {
                _log.Error(LogTag, $"[AutoTest] RESULT FAIL {role} ({_options.Scenario}): {failure}");
            }

            // Дать остальным участникам увидеть наш уход в нужный момент и логу — записаться.
            await UniTask.Delay(TimeSpan.FromSeconds(LingerSeconds), ignoreTimeScale: true);
            Quit(failure == null ? 0 : 1);
        }

        private void Prepare()
        {
            Application.targetFrameRate = TargetFrameRate;
            // У сборки в batchmode нет фокуса окна, а без фокуса Input System отбрасывает ввод.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>("AutoTestKeyboard");
            _keyboard.MakeCurrent();

            if (_options.Port != 0)
            {
                _session.HostPort = _options.Port;
            }

            if (_options.Bots >= 0)
            {
                _matchSetup.BotCount = _options.Bots;
            }

            // Бот дерётся всерьёз; выпавший по вине бота игрок не должен заканчивать матч посреди проверки сети.
            _matchSetup.RespawnOnFall = true;

            if (!string.IsNullOrEmpty(_options.NetworkSimulator))
            {
                if (!NetworkDebugCommands.TryParseSimulator(_options.NetworkSimulator, out uint delay, out uint jitter, out float loss))
                {
                    throw new ArgumentException($"Bad -tt-netsim '{_options.NetworkSimulator}'.");
                }

                _simulator.Set(delay, jitter, loss);
            }

            Step($"started as {(_options.IsHost ? "host" : "client")}, scenario {_options.Scenario}");
        }

        // ---------- Хост ----------

        private async UniTask<string> RunHostAsync(CancellationToken token)
        {
            await WaitForState(GameState.MainMenu, token);
            SessionStartResult hosted = await _launcher.HostAsync();
            if (!hosted.Success)
            {
                return "host failed: " + hosted.Reason;
            }

            await WaitForState(GameState.Lobby, token);
            _localPlayers.TryJoinDefault();
            Step("lobby open, waiting for clients");

            if (!await WaitUntil(() => CountReadyRemoteClients() >= _options.Clients, token))
            {
                return $"only {CountReadyRemoteClients()} of {_options.Clients} clients joined and got ready";
            }

            int humans = _lobby.Slots.Count;
            Step($"{humans} players in lobby, starting the match");
            await _flow.GoToAsync(GameState.Match);
            MatchEntryPoint match = await WaitForMatch(humans, token);
            if (match == null)
            {
                return "match did not start";
            }

            List<TrackedCharacter> tracked = Track(match);
            HoldKeyAsync(Key.D, token).Forget();
            string verdict = null;

            switch (_options.Scenario)
            {
                case AutoTestScenario.HostLeave:
                    await UniTask.Delay(TimeSpan.FromSeconds(BeforeIncidentSeconds), cancellationToken: token);
                    Step("leaving to the main menu in the middle of the match");
                    await _flow.GoToAsync(GameState.MainMenu);
                    return null;

                case AutoTestScenario.HostCrash:
                    await UniTask.Delay(TimeSpan.FromSeconds(BeforeIncidentSeconds), cancellationToken: token);
                    Step("crashing in the middle of the match");
                    // Без Shutdown и без прощального сообщения: для клиентов это неотличимо от падения.
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                    return null;

                case AutoTestScenario.ClientDrop:
                    verdict = await HostWatchesClientDropAsync(token);
                    break;

                default:
                    float matchEnd = Time.realtimeSinceStartup + _options.MatchDuration;
                    while (Time.realtimeSinceStartup < matchEnd)
                    {
                        UpdateTravel(tracked);
                        await UniTask.Yield(token);
                    }

                    verdict = CheckTravelled(tracked);
                    break;
            }

            LogPositions(match);
            _matchEnded.Publish(new MatchEnded(MatchEnded.NoWinner));
            await WaitForState(GameState.Results, token);
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
            await _flow.GoToAsync(GameState.Lobby);
            await WaitForState(GameState.Lobby, token);
            Step("back in lobby, waiting for clients to leave");

            // Клиенты выходят сами, увидев лобби. Их слоты остаются зарезервированными на случай возвращения.
            if (!await WaitUntil(() => CountConnectedRemoteSlots() == 0, token))
            {
                return verdict ?? "clients did not leave";
            }

            if (CountReservedSlots() == 0)
            {
                return verdict ?? "slots of clients that left must stay reserved";
            }

            Step("clients left, their slots are reserved");
            return verdict;
        }

        private async UniTask<string> HostWatchesClientDropAsync(CancellationToken token)
        {
            if (!await WaitUntil(() => CountReservedSlots() > 0, token))
            {
                return "client did not drop";
            }

            Step("client dropped, slot reserved");
            if (!await WaitUntil(() => CountReservedSlots() == 0 && CountConnectedRemoteSlots() > 0, token))
            {
                return "client did not come back";
            }

            Step("client came back into the same slot");
            // Пусть вернувшийся досмотрит кусок матча: он должен увидеть персонажей.
            await UniTask.Delay(TimeSpan.FromSeconds(SpectateSeconds + 1f), cancellationToken: token);
            return null;
        }

        // ---------- Клиент ----------

        private async UniTask<string> RunClientAsync(CancellationToken token)
        {
            await WaitForState(GameState.MainMenu, token);

            if (!SessionEndpoint.TryParse(_options.Address, out SessionEndpoint host))
            {
                return $"bad -tt-address '{_options.Address}'";
            }

            // Хост может стартовать позже клиента: пробуем подключиться, пока не выйдет.
            float deadline = Time.realtimeSinceStartup + StepTimeout;
            SessionStartResult joined;
            do
            {
                joined = await _launcher.JoinAsync(host);
                if (!joined.Success)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                }
            }
            while (!joined.Success && Time.realtimeSinceStartup < deadline);

            if (!joined.Success)
            {
                return "join failed: " + joined.Reason;
            }

            Step($"connected as client {_session.LocalClientId}");
            await WaitForState(GameState.Lobby, token);
            if (!await WaitUntil(() => OwnSlotIndex() >= 0, token))
            {
                return "host did not give us a slot";
            }

            int ownSlot = OwnSlotIndex();
            _lobby.SetLocalReady(true);
            Step($"in lobby as P{ownSlot + 1} and ready");

            MatchEntryPoint match = await WaitForMatch(2, token);
            if (match == null)
            {
                return "match did not start";
            }

            List<TrackedCharacter> tracked = Track(match);
            HoldKeyAsync(Key.W, token).Forget();

            switch (_options.Scenario)
            {
                case AutoTestScenario.HostLeave:
                case AutoTestScenario.HostCrash:
                    return await ClientSurvivesHostLossAsync(ownSlot, token);

                case AutoTestScenario.ClientDrop:
                    return await ClientDropsAndComesBackAsync(ownSlot, token);
            }

            // Пока идёт матч, запоминаем самое дальнее положение: персонажи уничтожатся вместе с концом матча.
            while (_flow.Current == GameState.Match)
            {
                UpdateTravel(tracked);
                await UniTask.Yield(token);
            }

            string movement = CheckTravelled(tracked);

            await WaitForState(GameState.Results, token);
            Step("results");
            await WaitForState(GameState.Lobby, token);
            Step("back in lobby, leaving");
            await _flow.GoToAsync(GameState.MainMenu);
            return movement;
        }

        private async UniTask<string> ClientSurvivesHostLossAsync(int ownSlot, CancellationToken token)
        {
            if (!await WaitUntil(() => _session.IsRecovering, token))
            {
                return "never noticed that the host was gone";
            }

            Step("host is gone, recovering");
            if (!await WaitUntil(() => !_session.IsRecovering, token))
            {
                return "recovery did not finish";
            }

            if (_session.Role.CurrentValue == SessionRole.None)
            {
                return "recovery failed: " + _session.LastDisconnectReason;
            }

            await WaitForState(GameState.Lobby, token);
            bool isNewHost = _session.IsHost;
            Step(isNewHost ? "we are the new host" : "joined the new host");

            if (!await WaitUntil(() => OwnSlotIndex() == ownSlot, token))
            {
                return $"expected to keep P{ownSlot + 1}, got slot {OwnSlotIndex() + 1}";
            }

            // Остальные клиенты старого хоста должны перейти к новому.
            int otherClients = _options.Clients - 1;
            if (!await WaitUntil(() => CountConnectedPlayers() >= 1 + otherClients, token))
            {
                return $"expected {1 + otherClients} players with the new host, have {CountConnectedPlayers()}";
            }

            // Ушедший хост сохраняет слот: если вернётся, займёт его как клиент.
            if (CountReservedSlots() < 1)
            {
                return "the old host's slot was not kept";
            }

            Step($"session continues: P{ownSlot + 1}, {CountConnectedPlayers()} connected, {CountReservedSlots()} reserved");

            if (isNewHost && otherClients > 0)
            {
                // Новый хост дожидается, пока проверку закончат перешедшие к нему клиенты.
                await WaitUntil(() => CountConnectedRemoteSlots() == 0, token);
            }

            return null;
        }

        private async UniTask<string> ClientDropsAndComesBackAsync(int ownSlot, CancellationToken token)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(BeforeIncidentSeconds), cancellationToken: token);
            Step("network goes dark");
            _simulator.BlackoutAsync(DropSeconds).Forget();

            if (!await WaitUntil(() => _session.IsRecovering, token))
            {
                return "never noticed the drop";
            }

            Step("lost the host, reconnecting");
            if (!await WaitUntil(() => !_session.IsRecovering, token))
            {
                return "recovery did not finish";
            }

            if (!_session.IsRemoteClient)
            {
                return $"expected to be back as a client, role {_session.Role.CurrentValue}, reason {_session.LastDisconnectReason}";
            }

            Step("reconnected");
            if (_flow.Current == GameState.Match)
            {
                // Матч ещё идёт: мы его досматриваем, хост должен показать нам персонажей.
                MatchEntryPoint match = await WaitForMatch(1, token);
                if (match == null)
                {
                    return "did not see the match after reconnecting";
                }

                Step($"spectating: {match.Characters.Count} characters visible");
            }

            await WaitForState(GameState.Results, token);
            await WaitForState(GameState.Lobby, token);
            if (!await WaitUntil(() => OwnSlotIndex() == ownSlot, token))
            {
                return $"expected to keep P{ownSlot + 1}, got slot {OwnSlotIndex() + 1}";
            }

            Step($"back in lobby as P{ownSlot + 1}, leaving");
            await _flow.GoToAsync(GameState.MainMenu);
            return null;
        }

        // ---------- Слоты ----------

        private int OwnSlotIndex()
        {
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsConnected && slots[i].ClientId == _session.LocalClientId)
                {
                    return slots[i].Index;
                }
            }

            return -1;
        }

        private int CountReadyRemoteClients()
        {
            _clientsBuffer.Clear();
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                PlayerSlot slot = slots[i];
                if (slot.IsConnected && slot.ClientId != _session.LocalClientId && slot.IsReady && !_clientsBuffer.Contains(slot.ClientId))
                {
                    _clientsBuffer.Add(slot.ClientId);
                }
            }

            return _clientsBuffer.Count;
        }

        private int CountConnectedRemoteSlots()
        {
            int count = 0;
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsConnected && slots[i].ClientId != _session.LocalClientId)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountConnectedPlayers()
        {
            int count = 0;
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsConnected)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountReservedSlots()
        {
            int count = 0;
            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].IsConnected)
                {
                    count++;
                }
            }

            return count;
        }

        // ---------- Движение ----------

        /// <summary>Что известно о персонаже на старте: после конца матча сам объект уже уничтожен.</summary>
        private sealed class TrackedCharacter
        {
            public PlayerCharacter Character;
            public string Name;
            public bool IsBot;
            public bool IsOwn;
            public Vector3 Start;
            public float MaxTravel;
        }

        private static List<TrackedCharacter> Track(MatchEntryPoint match)
        {
            var result = new List<TrackedCharacter>();
            for (int i = 0; i < match.Characters.Count; i++)
            {
                PlayerCharacter character = match.Characters[i];
                result.Add(new TrackedCharacter
                {
                    Character = character,
                    Name = character.IsBot ? "bot" : $"P{character.Slot + 1} (owner {character.OwnerClientId})",
                    IsBot = character.IsBot,
                    IsOwn = character.IsOwner,
                    Start = Flat(character.transform.position),
                });
            }

            return result;
        }

        private static void UpdateTravel(List<TrackedCharacter> tracked)
        {
            for (int i = 0; i < tracked.Count; i++)
            {
                TrackedCharacter entry = tracked[i];
                if (entry.Character != null)
                {
                    float distance = Vector3.Distance(Flat(entry.Character.transform.position), entry.Start);
                    entry.MaxTravel = Mathf.Max(entry.MaxTravel, distance);
                }
            }
        }

        private string CheckTravelled(List<TrackedCharacter> tracked)
        {
            int humans = 0;
            for (int i = 0; i < tracked.Count; i++)
            {
                TrackedCharacter entry = tracked[i];
                if (entry.IsBot)
                {
                    continue;
                }

                humans++;
                string who = entry.IsOwn ? "own" : "remote";
                Step($"{entry.Name} [{who}] travelled up to {entry.MaxTravel:0.00} m");
                if (entry.MaxTravel < MinTravel)
                {
                    return $"{entry.Name} [{who}] moved only {entry.MaxTravel:0.00} m";
                }
            }

            return humans >= 2 ? null : $"expected at least 2 human characters, saw {humans}";
        }

        // ---------- Ожидания и ввод ----------

        private async UniTask<MatchEntryPoint> WaitForMatch(int minCharacters, CancellationToken token)
        {
            MatchEntryPoint match = null;
            bool ready = await WaitUntil(() =>
            {
                var scope = Object.FindAnyObjectByType<MatchLifetimeScope>();
                if (scope == null || scope.Container == null || _loadingScreen.IsVisible)
                {
                    return false;
                }

                match = scope.Container.Resolve<MatchEntryPoint>();
                return match.IsReady && CountHumans(match) >= minCharacters;
            }, token);

            if (ready)
            {
                Step($"match ready: {match.Characters.Count} characters");
                LogPositions(match);
            }

            return ready ? match : null;
        }

        private static int CountHumans(MatchEntryPoint match)
        {
            int count = 0;
            for (int i = 0; i < match.Characters.Count; i++)
            {
                if (!match.Characters[i].IsBot)
                {
                    count++;
                }
            }

            return count;
        }

        private async UniTask WaitForState(GameState state, CancellationToken token)
        {
            if (!await WaitUntil(() => _flow.Current == state && !_flow.IsTransitioning && !_loadingScreen.IsVisible, token))
            {
                throw new TimeoutException($"waiting for {state}, current {_flow.Current}");
            }
        }

        private static async UniTask<bool> WaitUntil(Func<bool> condition, CancellationToken token)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    return false;
                }

                await UniTask.Yield(token);
            }

            return true;
        }

        private async UniTask HoldKeyAsync(Key key, CancellationToken token)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            await UniTask.Delay(TimeSpan.FromSeconds(KeyHoldSeconds), ignoreTimeScale: true, cancellationToken: token);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
        }

        private void LogPositions(MatchEntryPoint match)
        {
            for (int i = 0; i < match.Characters.Count; i++)
            {
                PlayerCharacter character = match.Characters[i];
                string name = character.IsBot ? "bot" : "P" + (character.Slot + 1);
                Step($"  {name} owner {character.OwnerClientId} at {character.transform.position}");
            }
        }

        private static Vector3 Flat(Vector3 position)
        {
            position.y = 0f;
            return position;
        }

        private void Step(string message)
        {
            _log.Info(LogTag, "[AutoTest] " + message);
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
#endif
