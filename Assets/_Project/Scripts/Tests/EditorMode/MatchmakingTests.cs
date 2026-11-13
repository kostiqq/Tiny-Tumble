using System;
using System.Collections.Generic;
using NUnit.Framework;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Matchmaking;
using TinyTumble.Networking.Session;
using Unity.Collections;
using Unity.Netcode;

namespace TinyTumble.Tests
{
    /// <summary>Поиск игры: выбор лобби, схождение ждущих хостов, пинг, допуск найденных поиском, формат объявлений.</summary>
    public sealed class MatchmakingTests
    {
        private const int Version = 5;
        private const ulong Token = 77;
        private const ulong OwnKey = 50;
        private const ulong HostParty = 0xA0;
        private const ulong GuestParty = 0xB0;
        private const int MaxPing = 120;

        // ---------- Выбор лобби ----------

        [Test]
        public void Pick_PrefersFullerLobby_ThenLowerPing_ThenSmallerKey()
        {
            var ads = new List<LobbyAd> { Ad(10, players: 1, ping: 20), Ad(11, players: 2, ping: 90), Ad(12, players: 2, ping: 40) };

            Assert.IsTrue(MatchmakingRules.TryPick(ads, 1, MaxPing, null, OwnKey, -1, out LobbyAd best));
            Assert.AreEqual(12UL, best.Key, "More players first, then the lower ping.");

            ads = new List<LobbyAd> { Ad(21, players: 2, ping: 40), Ad(20, players: 2, ping: 40) };
            Assert.IsTrue(MatchmakingRules.TryPick(ads, 1, MaxPing, null, OwnKey, -1, out best));
            Assert.AreEqual(20UL, best.Key, "A full tie is broken by the key, the same way for everyone.");
        }

        [Test]
        public void Pick_SkipsNoRoomForParty_HighPing_Excluded_AndSelf()
        {
            var excluded = new HashSet<ulong> { 13 };
            var ads = new List<LobbyAd>
            {
                Ad(10, players: 3, free: 1),
                Ad(11, players: 1, ping: MaxPing + 1),
                Ad(OwnKey, players: 1),
                Ad(13, players: 1),
            };

            Assert.IsFalse(MatchmakingRules.TryPick(ads, 2, MaxPing, excluded, OwnKey, -1, out _));
        }

        [Test]
        public void Pick_UnknownPingIsAllowed_ButLosesToKnown()
        {
            var ads = new List<LobbyAd> { Ad(10, players: 1, ping: -1), Ad(11, players: 1, ping: 80) };

            Assert.IsTrue(MatchmakingRules.TryPick(ads, 1, MaxPing, null, OwnKey, -1, out LobbyAd best));
            Assert.AreEqual(11UL, best.Key);

            ads.RemoveAt(1);
            Assert.IsTrue(MatchmakingRules.TryPick(ads, 1, MaxPing, null, OwnKey, -1, out best), "Steam may not know the ping yet.");
        }

        [Test]
        public void WaitingHosts_Converge_InsteadOfSwapping()
        {
            // Два хоста по одному игроку видят друг друга. Переезжает только тот, чей ключ больше.
            var seenByA = new List<LobbyAd> { Ad(20, players: 1) };
            var seenByB = new List<LobbyAd> { Ad(10, players: 1) };

            Assert.IsFalse(MatchmakingRules.TryPick(seenByA, 1, MaxPing, null, 10, 1, out _), "A (key 10) stays.");
            Assert.IsTrue(MatchmakingRules.TryPick(seenByB, 1, MaxPing, null, 20, 1, out LobbyAd target), "B (key 20) moves to A.");
            Assert.AreEqual(10UL, target.Key);
        }

        [Test]
        public void WaitingHost_MovesOnlyToFullerLobby()
        {
            var ads = new List<LobbyAd> { Ad(99, players: 3, free: 1), Ad(5, players: 1) };

            Assert.IsTrue(MatchmakingRules.TryPick(ads, 1, MaxPing, null, OwnKey, 2, out LobbyAd best));
            Assert.AreEqual(99UL, best.Key, "Three players beat our two, whatever the key.");

            Assert.IsFalse(MatchmakingRules.TryPick(new List<LobbyAd> { Ad(5, players: 1) }, 1, MaxPing, null, OwnKey, 2, out _),
                "A lobby emptier than ours is not worth moving to.");
        }

        // ---------- Пинг ----------

        [Test]
        public void PingTooHigh_UsesMedian()
        {
            Assert.IsFalse(MatchmakingRules.IsPingTooHigh(new[] { 0.05f, 0.9f, 0.06f }, 3, 0.2f), "One spike does not send a party back.");
            Assert.IsTrue(MatchmakingRules.IsPingTooHigh(new[] { 0.3f, 0.25f, 0.05f, 0.4f }, 4, 0.2f));
            Assert.IsFalse(MatchmakingRules.IsPingTooHigh(new float[4], 0, 0.2f), "No answers yet — no verdict.");
        }

        // ---------- Допуск ----------

        [Test]
        public void Admission_Matchmade_OnlyWhileHostSearches()
        {
            var payload = new ConnectionPayload(Version, 1, 1, SessionEndpoint.Ip(0, 7778), 0, new PartyTicket(GuestParty, 1, true, true));

            Assert.AreEqual(DisconnectReasons.LobbyClosed, Evaluate(payload, openForMatchmaking: false));
            Assert.AreEqual(DisconnectReasons.None, Evaluate(payload, openForMatchmaking: true));
        }

        [Test]
        public void Admission_FriendsComeIn_WhetherOrNotSearching()
        {
            var friend = new ConnectionPayload(Version, 1, 1, SessionEndpoint.Ip(0, 7778), 0);
            Assert.AreEqual(DisconnectReasons.None, Evaluate(friend, openForMatchmaking: false));
        }

        [Test]
        public void Payload_CarriesMatchmadeFlag()
        {
            var ticket = new PartyTicket(GuestParty, 2, false, true);
            byte[] bytes = new ConnectionPayload(Version, 1, 1, SessionEndpoint.Ip(0, 7778), 0, ticket).ToBytes();

            Assert.IsTrue(ConnectionPayload.TryParse(bytes, out ConnectionPayload parsed));
            Assert.IsTrue(parsed.Party.IsMatchmade);
            Assert.IsFalse(parsed.Party.IsLeader, "Flags do not leak into each other.");
        }

        [Test]
        public void NewCodes_AreRejections()
        {
            Assert.IsTrue(DisconnectReasons.IsRejection(DisconnectReasons.LobbyClosed));
            Assert.IsTrue(DisconnectReasons.IsRejection(DisconnectReasons.HighPing));
            Assert.IsTrue(DisconnectReasons.IsKnown(DisconnectReasons.HighPing));
        }

        // ---------- Состояние поиска в сессии ----------

        [Test]
        public void Info_SecondsLeft_AndSurvivesSerialization()
        {
            long now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc).Ticks;
            var info = new MatchmakingInfo(true, true, true, now + TimeSpan.FromSeconds(30).Ticks);

            Assert.AreEqual(30f, info.SecondsLeft(now), 0.01f);
            Assert.AreEqual(0f, info.SecondsLeft(now + TimeSpan.FromSeconds(40).Ticks), "Never negative while searching.");
            Assert.Less(default(MatchmakingInfo).SecondsLeft(now), 0f, "Not searching.");

            MatchmakingInfo copy;
            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteNetworkSerializable(info);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out copy);
                }
            }

            Assert.IsTrue(info.Equals(copy));
        }

        [Test]
        public void Migration_CarriesSearch()
        {
            var info = new MatchmakingInfo(true, true, true, 123);
            HostSeed seed = HostSeed.Migrated(Token, default, info);

            Assert.IsTrue(seed.Matchmaking.Equals(info));
            Assert.IsFalse(HostSeed.NewSession(Token).Matchmaking.Searching);
        }

        // ---------- Локальный каталог ----------

        [Test]
        public void LocalAd_RoundTrips()
        {
            string text = LocalLobbyDirectory.Format(Version, 0xDEADBEEF, 7779, 2, 2);

            Assert.IsTrue(LocalLobbyDirectory.TryParse(text, out int version, out LobbyAd ad));
            Assert.AreEqual(Version, version);
            Assert.AreEqual(0xDEADBEEFUL, ad.Key);
            Assert.AreEqual(SessionEndpoint.Ip(SessionEndpoint.LoopbackIpv4, 7779), ad.Host);
            Assert.AreEqual(2, ad.Players);
            Assert.AreEqual(2, ad.FreeSlots);
        }

        [TestCase("")]
        [TestCase("5;1;2")]
        [TestCase("5;x;7777;1;3")]
        public void LocalAd_RejectsGarbage(string text)
        {
            Assert.IsFalse(LocalLobbyDirectory.TryParse(text, out _, out _));
        }

        private static LobbyAd Ad(ulong key, int players, int free = -1, int ping = 30)
        {
            int freeSlots = free >= 0 ? free : 4 - players;
            return new LobbyAd(SessionEndpoint.Ip(SessionEndpoint.LoopbackIpv4, (ushort)(7000 + key)), key, players, freeSlots, ping);
        }

        private static string Evaluate(in ConnectionPayload payload, bool openForMatchmaking)
        {
            return ConnectionAdmission.Evaluate(payload, true, Version, Token, false, 3, true, HostParty, 0, 0, openForMatchmaking);
        }
    }
}
