using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Session;
using TinyTumble.Networking.Steam;
using TinyTumble.Networking.Transport;
using VContainer.Unity;

namespace TinyTumble.Networking.Debugging
{
    /// <summary>
    /// Команды консоли для сети: net.host, net.join, net.invite, net.leave, net.status, net.sim, net.drop.
    /// </summary>
    public sealed class NetworkDebugCommands : IStartable, IDisposable
    {
        private const string LogTag = "Net";
        private const float DefaultDropSeconds = 8f;

        private readonly IDebugConsole _console;
        private readonly ISessionLauncher _launcher;
        private readonly INetworkSession _session;
        private readonly SessionLobby _lobby;
        private readonly IGameFlow _flow;
        private readonly NetworkSimulatorControl _simulator;
        private readonly SessionTransports _transports;
        private readonly NetworkSession _networkSession;
        private readonly SteamService _steam;
        private readonly ISessionInvites _invites;
        private readonly IGameLog _log;
        private readonly List<IDisposable> _registrations = new List<IDisposable>();
        private readonly StringBuilder _builder = new StringBuilder();

        public NetworkDebugCommands(
            IDebugConsole console,
            ISessionLauncher launcher,
            INetworkSession session,
            SessionLobby lobby,
            IGameFlow flow,
            NetworkSimulatorControl simulator,
            SessionTransports transports,
            NetworkSession networkSession,
            SteamService steam,
            ISessionInvites invites,
            IGameLog log)
        {
            _console = console;
            _launcher = launcher;
            _session = session;
            _lobby = lobby;
            _flow = flow;
            _simulator = simulator;
            _transports = transports;
            _networkSession = networkSession;
            _steam = steam;
            _invites = invites;
            _log = log;
        }

        public void Start()
        {
            _registrations.Add(_console.Register("net.host", "создать игру: net.host [auto|ip|steam] (по умолчанию auto — Steam, если запущен)", OnHost));
            _registrations.Add(_console.Register("net.join", "подключиться: net.join 192.168.0.10[:7777] или net.join steam:<SteamID>", OnJoin));
            _registrations.Add(_console.Register("net.invite", "пригласить друзей Steam (оверлей)", OnInvite));
            _registrations.Add(_console.Register("net.leave", "выйти из сессии в главное меню", OnLeave));
            _registrations.Add(_console.Register("net.status", "роль, игроки, симулятор", OnStatus));
            _registrations.Add(_console.Register("net.sim", "симулятор сети: net.sim <задержка мс> <джиттер мс> <потери %>; net.sim off", OnSim));
            _registrations.Add(_console.Register("net.drop", "оборвать связь в обе стороны: net.drop <секунды> (по умолчанию 8)", OnDrop));
        }

        public void Dispose()
        {
            for (int i = 0; i < _registrations.Count; i++)
            {
                _registrations[i].Dispose();
            }

            _registrations.Clear();
        }

        private void OnHost(string arguments)
        {
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                if (!TryParseHostPreference(arguments, out HostTransportPreference preference))
                {
                    _log.Warning(LogTag, "Usage: net.host [auto|ip|steam]");
                    return;
                }

                _transports.HostPreference = preference;
            }

            _launcher.HostAsync().Forget();
        }

        private void OnInvite(string arguments)
        {
            if (!_invites.OpenInviteDialog())
            {
                _log.Warning(LogTag, _steam.IsReady
                    ? "Nothing to invite to yet: host a game over Steam first (net.host steam)."
                    : "Steam is not running: invites are unavailable.");
            }
        }

        public static bool TryParseHostPreference(string text, out HostTransportPreference preference)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "auto":
                    preference = HostTransportPreference.Auto;
                    return true;
                case "ip":
                    preference = HostTransportPreference.Ip;
                    return true;
                case "steam":
                    preference = HostTransportPreference.Steam;
                    return true;
                default:
                    preference = HostTransportPreference.Auto;
                    return false;
            }
        }

        private void OnJoin(string arguments)
        {
            string text = string.IsNullOrEmpty(arguments) ? "127.0.0.1" : arguments;
            if (!SessionEndpoint.TryParse(text, out SessionEndpoint host))
            {
                _log.Warning(LogTag, "Usage: net.join <ip[:port]> or net.join steam:<SteamID>");
                return;
            }

            _launcher.JoinAsync(host).Forget();
        }

        private void OnLeave(string arguments)
        {
            _flow.GoToAsync(GameState.MainMenu).Forget();
        }

        private void OnStatus(string arguments)
        {
            _builder.Clear();
            _builder.Append("Role: ").Append(_session.Role.CurrentValue)
                .Append(", client id: ").Append(_session.LocalClientId)
                .Append(", host state: ").Append(_lobby.HostState)
                .Append(", transport: ").Append(_networkSession.TransportKind)
                .Append(", host preference: ").Append(_transports.HostPreference);

            if (_steam.IsReady)
            {
                _builder.Append(", Steam: ").Append(_steam.LocalSteamId).Append(_invites.CanInvite ? ", invites open" : string.Empty);
            }

            if (_session.IsRecovering)
            {
                _builder.Append(", recovering");
            }

            if (!string.IsNullOrEmpty(_session.LastDisconnectReason))
            {
                _builder.Append(", last disconnect: ").Append(_session.LastDisconnectReason);
            }

            IReadOnlyList<PlayerSlot> slots = _lobby.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                PlayerSlot slot = slots[i];
                _builder.AppendLine().Append("  P").Append(slot.Index + 1)
                    .Append(": client ").Append(slot.ClientId)
                    .Append(", local ").Append(slot.LocalIndex)
                    .Append(slot.IsConnected ? string.Empty : ", reserved")
                    .Append(slot.IsReady ? ", ready" : string.Empty);
            }

            if (_simulator.IsEnabled)
            {
                _builder.AppendLine().Append("  Simulator: ").Append(_simulator.DelayMs).Append(" ms, jitter ")
                    .Append(_simulator.JitterMs).Append(" ms, loss ").Append(_simulator.LossPercent).Append('%');
            }

            _log.Info(LogTag, _builder.ToString());
        }

        private void OnSim(string arguments)
        {
            if (!TryParseSimulator(arguments, out uint delay, out uint jitter, out float loss))
            {
                _log.Warning(LogTag, "Usage: net.sim <delay ms> <jitter ms> <loss %>, or net.sim off");
                return;
            }

            _simulator.Set(delay, jitter, loss);
        }

        private void OnDrop(string arguments)
        {
            float seconds = DefaultDropSeconds;
            if (!string.IsNullOrWhiteSpace(arguments)
                && (!float.TryParse(arguments, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds <= 0f))
            {
                _log.Warning(LogTag, "Usage: net.drop <seconds>");
                return;
            }

            _simulator.BlackoutAsync(seconds).Forget();
        }

        /// <summary>"120 20 3", "120,20,3" или "off".</summary>
        public static bool TryParseSimulator(string text, out uint delayMs, out uint jitterMs, out float lossPercent)
        {
            delayMs = 0;
            jitterMs = 0;
            lossPercent = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 3 || !uint.TryParse(parts[0], out delayMs))
            {
                return false;
            }

            if (parts.Length > 1 && !uint.TryParse(parts[1], out jitterMs))
            {
                return false;
            }

            if (parts.Length > 2 && (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lossPercent)
                || lossPercent < 0f || lossPercent > 100f))
            {
                return false;
            }

            return true;
        }
    }
}
