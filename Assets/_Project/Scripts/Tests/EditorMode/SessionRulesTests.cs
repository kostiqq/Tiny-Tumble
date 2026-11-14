using System.Collections.Generic;
using NUnit.Framework;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Debugging;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Session;
using TinyTumble.Networking.Steam;
using TinyTumble.Networking.Transport;

namespace TinyTumble.Tests
{
    public sealed class SessionRulesTests
    {
        private const int Version = 3;

        private const ulong Token = 77;
        private const ulong Key = 0xABCDEF0123456789;
        private const ulong SteamId = 76561198000000042;

        private static readonly SessionEndpoint IpSuccessor = SessionEndpoint.Ip(0, 7777);

        [Test]
        public void Payload_RoundTrips()
        {
            byte[] bytes = new ConnectionPayload(Version, 2, Key, SessionEndpoint.Ip(0, 7779), Token).ToBytes();

            Assert.IsTrue(ConnectionPayload.TryParse(bytes, out ConnectionPayload parsed));
            Assert.AreEqual(Version, parsed.ProtocolVersion);
            Assert.AreEqual(2, parsed.LocalPlayerCount);
            Assert.AreEqual(Key, parsed.PlayerKey);
            Assert.AreEqual(SessionEndpoint.Ip(0, 7779), parsed.Successor);
            Assert.AreEqual(Token, parsed.SessionToken);
        }

        [Test]
        public void Payload_CarriesSteamSuccessor()
        {
            byte[] bytes = new ConnectionPayload(Version, 1, Key, SessionEndpoint.Steam(SteamId), 0).ToBytes();

            Assert.IsTrue(ConnectionPayload.TryParse(bytes, out ConnectionPayload parsed));
            Assert.AreEqual(EndpointKind.Steam, parsed.Successor.Kind);
            Assert.AreEqual(SteamId, parsed.Successor.Address);
            Assert.AreEqual(Key, parsed.PlayerKey, "Fields after the successor are not shifted.");
            Assert.AreEqual(0UL, parsed.SessionToken);
        }

        [Test]
        public void Admission_AcceptsMatchingClient()
        {
            Assert.AreEqual(DisconnectReasons.None, Evaluate(new ConnectionPayload(Version, 2, Key, IpSuccessor, 0), freeSlots: 2));
        }

        [TestCase(null)]
        [TestCase(new byte[] { 1, 2 })]
        public void Admission_RejectsGarbagePayload(byte[] raw)
        {
            bool valid = ConnectionPayload.TryParse(raw, out ConnectionPayload payload);
            Assert.AreEqual(DisconnectReasons.VersionMismatch,
                ConnectionAdmission.Evaluate(payload, valid, Version, Token, false, 4, true));
        }

        [Test]
        public void Admission_RejectsOtherVersion()
        {
            Assert.AreEqual(DisconnectReasons.VersionMismatch, Evaluate(new ConnectionPayload(Version + 1, 1, Key, IpSuccessor, 0)));
        }

        [Test]
        public void Admission_RejectsDuringMatch()
        {
            Assert.AreEqual(DisconnectReasons.MatchInProgress, Evaluate(new ConnectionPayload(Version, 1, Key, IpSuccessor, 0), accepting: false));
        }

        [Test]
        public void Admission_RejectsWhenNotEnoughSlots()
        {
            Assert.AreEqual(DisconnectReasons.LobbyFull, Evaluate(new ConnectionPayload(Version, 3, Key, IpSuccessor, 0), freeSlots: 2));
        }

        [Test]
        public void Admission_ClientWithoutPlayers_StillNeedsOneSlot()
        {
            var payload = new ConnectionPayload(Version, 0, Key, IpSuccessor, 0);
            Assert.AreEqual(DisconnectReasons.LobbyFull, Evaluate(payload, freeSlots: 0));
            Assert.AreEqual(DisconnectReasons.None, Evaluate(payload, freeSlots: 1));
        }

        [Test]
        public void Admission_ReturningPlayer_GetsInDuringMatchAndWhenFull()
        {
            var payload = new ConnectionPayload(Version, 1, Key, IpSuccessor, Token);
            Assert.AreEqual(DisconnectReasons.None, Evaluate(payload, freeSlots: 0, accepting: false, reserved: true));
        }

        [Test]
        public void Admission_RejectsClientLookingForAnotherSession()
        {
            var payload = new ConnectionPayload(Version, 1, Key, IpSuccessor, Token + 1);
            Assert.AreEqual(DisconnectReasons.SessionMismatch, Evaluate(payload, reserved: true));
        }

        [Test]
        public void Rejections_StopRecovery_ButDropsDoNot()
        {
            Assert.IsTrue(DisconnectReasons.IsRejection(DisconnectReasons.LobbyFull));
            Assert.IsTrue(DisconnectReasons.IsRejection(DisconnectReasons.SessionMismatch));
            Assert.IsFalse(DisconnectReasons.IsRejection(DisconnectReasons.HostLeft));
            Assert.IsFalse(DisconnectReasons.IsRejection(DisconnectReasons.ConnectFailed));
            Assert.IsFalse(DisconnectReasons.IsKnown("Disconnected due to host shutting down."));
        }

        private static string Evaluate(in ConnectionPayload payload, int freeSlots = 4, bool accepting = true, bool reserved = false)
        {
            return ConnectionAdmission.Evaluate(payload, true, Version, Token, reserved, freeSlots, accepting);
        }

        [TestCase("192.168.0.10", "192.168.0.10", (ushort)7777)]
        [TestCase(" 10.0.0.2:7780 ", "10.0.0.2", (ushort)7780)]
        [TestCase("localhost", "127.0.0.1", (ushort)7777)]
        [TestCase("LOCALHOST:9000", "127.0.0.1", (ushort)9000)]
        public void Address_Parses(string text, string expectedIp, ushort expectedPort)
        {
            Assert.IsTrue(SessionEndpoint.TryParse(text, out SessionEndpoint endpoint));
            endpoint = endpoint.WithDefaultPort(7777);
            Assert.AreEqual(EndpointKind.Ip, endpoint.Kind);
            Assert.AreEqual(expectedIp, SessionEndpoint.Ipv4ToText(endpoint.Ipv4));
            Assert.AreEqual(expectedPort, endpoint.Port);
        }

        [Test]
        public void Address_WithoutPort_KeepsZeroUntilDefaultApplied()
        {
            Assert.IsTrue(SessionEndpoint.TryParse("10.0.0.2", out SessionEndpoint endpoint));
            Assert.AreEqual(0, endpoint.Port, "0 means 'the session's default port'.");
            Assert.AreEqual("10.0.0.2", endpoint.ToString());
            Assert.AreEqual("10.0.0.2:7777", endpoint.WithDefaultPort(7777).ToString());

            // Явный порт и не-IP адреса порт по умолчанию не трогает.
            Assert.IsTrue(SessionEndpoint.TryParse("10.0.0.2:9000", out SessionEndpoint explicitPort));
            Assert.AreEqual(9000, explicitPort.WithDefaultPort(7777).Port);
            SessionEndpoint steam = SessionEndpoint.Steam(42);
            Assert.AreEqual(steam, steam.WithDefaultPort(7777));
        }

        [Test]
        public void Endpoint_FromRaw_RejectsUnknownKind()
        {
            Assert.IsFalse(SessionEndpoint.FromRaw(200, 1, 2).IsValid);
            Assert.IsFalse(SessionEndpoint.Steam(0).IsValid, "SteamID 0 is not a host.");
            Assert.AreEqual(SessionEndpoint.Ip(SessionEndpoint.LoopbackIpv4, 7777),
                SessionEndpoint.FromRaw((byte)EndpointKind.Ip, SessionEndpoint.LoopbackIpv4, 7777));
        }

        [Test]
        public void Address_ParsesSteamId()
        {
            Assert.IsTrue(SessionEndpoint.TryParse(" steam:76561198000000042 ", out SessionEndpoint endpoint));
            Assert.AreEqual(SessionEndpoint.Steam(SteamId), endpoint);
            Assert.AreEqual("steam:76561198000000042", endpoint.ToString());
        }

        [TestCase("steam:")]
        [TestCase("steam:0")]
        [TestCase("steam:-5")]
        [TestCase("steam:abc")]
        public void Address_RejectsBadSteamId(string text)
        {
            Assert.IsFalse(SessionEndpoint.TryParse(text, out _));
        }

        [Test]
        public void ConnectLobby_ParsesSteamLaunchArgument()
        {
            Assert.IsTrue(SteamInvites.TryParseConnectLobby(new[] { "game.exe", "+connect_lobby", "109775241021923456" }, out ulong lobby));
            Assert.AreEqual(109775241021923456UL, lobby);

            Assert.IsFalse(SteamInvites.TryParseConnectLobby(new[] { "game.exe" }, out _));
            Assert.IsFalse(SteamInvites.TryParseConnectLobby(new[] { "game.exe", "+connect_lobby" }, out _), "No id after the key.");
            Assert.IsFalse(SteamInvites.TryParseConnectLobby(new[] { "+connect_lobby", "0" }, out _));
            Assert.IsFalse(SteamInvites.TryParseConnectLobby(null, out _));
        }

        [TestCase("auto", HostTransportPreference.Auto)]
        [TestCase(" IP ", HostTransportPreference.Ip)]
        [TestCase("steam", HostTransportPreference.Steam)]
        public void HostPreference_Parses(string text, HostTransportPreference expected)
        {
            Assert.IsTrue(NetworkDebugCommands.TryParseHostPreference(text, out HostTransportPreference preference));
            Assert.AreEqual(expected, preference);
        }

        [Test]
        public void HostPreference_RejectsUnknown()
        {
            Assert.IsFalse(NetworkDebugCommands.TryParseHostPreference("relay", out _));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("example.com")]
        [TestCase("1.2.3.4:0")]
        [TestCase("1.2.3.4:99999")]
        [TestCase("1.2.3.4:abc")]
        [TestCase("::1")]
        public void Address_RejectsInvalid(string text)
        {
            Assert.IsFalse(SessionEndpoint.TryParse(text, out _));
        }

        [Test]
        public void Simulator_ParsesArguments()
        {
            Assert.IsTrue(NetworkDebugCommands.TryParseSimulator("120 20 3.5", out uint delay, out uint jitter, out float loss));
            Assert.AreEqual(120, delay);
            Assert.AreEqual(20, jitter);
            Assert.AreEqual(3.5f, loss);

            Assert.IsTrue(NetworkDebugCommands.TryParseSimulator("60,10", out delay, out jitter, out loss));
            Assert.AreEqual(60, delay);
            Assert.AreEqual(10, jitter);
            Assert.AreEqual(0f, loss);

            Assert.IsTrue(NetworkDebugCommands.TryParseSimulator("off", out delay, out jitter, out loss));
            Assert.AreEqual(0, delay);

            Assert.IsFalse(NetworkDebugCommands.TryParseSimulator("fast", out _, out _, out _));
            Assert.IsFalse(NetworkDebugCommands.TryParseSimulator("10 10 150", out _, out _, out _));
        }

        [Test]
        public void LoadTracker_WaitsForEveryClientOfTheCurrentEpoch()
        {
            var tracker = new ClientLoadTracker();
            tracker.Begin(5, new List<ulong> { 1, 2 });
            Assert.IsFalse(tracker.IsComplete);

            Assert.IsFalse(tracker.Report(1, 4), "A late report of the previous transition is ignored.");
            Assert.IsTrue(tracker.Report(1, 5));
            Assert.IsFalse(tracker.Report(1, 5), "Duplicate report.");
            Assert.IsFalse(tracker.IsComplete);

            tracker.Remove(2);
            Assert.IsTrue(tracker.IsComplete, "A disconnected client must not block the others.");
        }

        [Test]
        public void LoadTracker_WithoutClients_IsComplete()
        {
            var tracker = new ClientLoadTracker();
            tracker.Begin(1, new List<ulong>());
            Assert.IsTrue(tracker.IsComplete);
        }
    }
}
