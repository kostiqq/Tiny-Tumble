using NUnit.Framework;
using TinyTumble.Core.Network;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Party;
using TinyTumble.Networking.Session;
using Unity.Collections;
using Unity.Netcode;

namespace TinyTumble.Tests
{
    /// <summary>Пати: лидерство в слотах, допуск пати целиком, формат запроса на подключение.</summary>
    public sealed class PartyTests
    {
        private const int Version = 4;
        private const ulong Token = 77;

        private const ulong Host = 0;
        private const ulong ClientA = 1;
        private const ulong ClientB = 2;
        private const ulong ClientC = 3;
        private const ulong HostKey = 100;
        private const ulong KeyA = 101;
        private const ulong KeyB = 102;
        private const ulong KeyC = 103;

        private const ulong HostParty = 0xA0;
        private const ulong GuestParty = 0xB0;

        private static readonly SessionEndpoint IpSuccessor = SessionEndpoint.Ip(0, 7778);

        // ---------- Слоты ----------

        [Test]
        public void Assign_SecondLocalPlayer_TakesMachineParty()
        {
            var table = new SlotTable();
            table.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);

            // Второй геймпад той же машины: аргументы другие, но пати у машины одна.
            table.TryAssign(KeyA, ClientA, 1, HostParty, false, out int second);

            Assert.AreEqual(GuestParty, table[second].PartyId);
            Assert.IsTrue(table[second].PartyLeader);
            Assert.AreEqual(2, table.CountParty(GuestParty));
        }

        [Test]
        public void Disconnect_LeaderPassesLeadToConnectedMember()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, HostParty, true, out _);
            table.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);
            table.TryAssign(KeyB, ClientB, 0, GuestParty, false, out _);

            table.Disconnect(ClientA);

            Assert.AreEqual(KeyB, table.LeaderOf(GuestParty), "The party needs a leader who is here to move it.");
            Assert.AreEqual(HostKey, table.LeaderOf(HostParty), "Other parties are not touched.");
        }

        [Test]
        public void Disconnect_LoneLeaderKeepsLead()
        {
            var table = new SlotTable();
            table.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);

            table.Disconnect(ClientA);

            Assert.AreEqual(KeyA, table.LeaderOf(GuestParty));
        }

        [Test]
        public void RemovePlayer_PassesLeadToRemainingMember()
        {
            var table = new SlotTable();
            table.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);
            table.TryAssign(KeyB, ClientB, 0, GuestParty, false, out _);
            table.Disconnect(ClientB);

            table.RemovePlayer(KeyA);

            Assert.AreEqual(KeyB, table.LeaderOf(GuestParty), "Even a disconnected member leads once the leader is gone for good.");
        }

        [Test]
        public void ForNewHost_TakesOverThePartyTheOldHostLed()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, HostParty, true, out _);
            table.TryAssign(KeyA, ClientA, 0, HostParty, false, out _);
            table.TryAssign(KeyB, ClientB, 0, HostParty, false, out _);

            SlotTable migrated = table.ForNewHost(KeyB, Host);

            Assert.AreEqual(KeyB, migrated.LeaderOf(HostParty), "The new host leads the party even if it is not first by slot.");
            Assert.IsTrue(migrated.TryGetParty(HostKey, out ulong oldHostParty, out bool oldHostLeads));
            Assert.AreEqual(HostParty, oldHostParty, "The old host stays in the party and may come back.");
            Assert.IsFalse(oldHostLeads);
        }

        [Test]
        public void ForNewHost_GuestPartyKeepsItsLeader()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, HostParty, true, out _);
            table.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);
            table.TryAssign(KeyB, ClientB, 0, GuestParty, false, out _);
            table.TryAssign(KeyC, ClientC, 0, HostParty, false, out _);

            // Преемником стал участник гостевой пати: пати ушедшего хоста ведёт её оставшийся участник.
            SlotTable migrated = table.ForNewHost(KeyB, Host);

            Assert.AreEqual(KeyA, migrated.LeaderOf(GuestParty));
            Assert.AreEqual(KeyC, migrated.LeaderOf(HostParty));
        }

        [Test]
        public void Party_SurvivesNetworkSerialization()
        {
            var table = new SlotTable();
            table.TryAssign(HostKey, Host, 0, HostParty, true, out _);
            table.TryAssign(KeyA, ClientA, 0, GuestParty, false, out _);

            SlotTable copy;
            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteNetworkSerializable(table);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out copy);
                }
            }

            Assert.IsTrue(table.Equals(copy));
            Assert.AreEqual(HostKey, copy.LeaderOf(HostParty));
            Assert.AreEqual(1, copy.CountParty(GuestParty));
        }

        [Test]
        public void Equality_SeesLeadershipChange()
        {
            var a = new SlotTable();
            a.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);
            a.TryAssign(KeyB, ClientB, 0, GuestParty, false, out _);
            SlotTable b = a;

            b.Disconnect(ClientA);
            a.Disconnect(ClientA);
            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(KeyB, b.LeaderOf(GuestParty));

            var c = new SlotTable();
            c.TryAssign(KeyA, ClientA, 0, GuestParty, false, out _);
            var d = new SlotTable();
            d.TryAssign(KeyA, ClientA, 0, GuestParty, true, out _);
            Assert.IsFalse(c.Equals(d), "NetworkVariable syncs only when the value differs.");
        }

        // ---------- Запрос на подключение ----------

        [Test]
        public void Payload_CarriesParty()
        {
            var ticket = new PartyTicket(GuestParty, 3, true);
            byte[] bytes = new ConnectionPayload(Version, 2, KeyA, IpSuccessor, Token, ticket).ToBytes();

            Assert.IsTrue(ConnectionPayload.TryParse(bytes, out ConnectionPayload parsed));
            Assert.AreEqual(GuestParty, parsed.Party.PartyId);
            Assert.AreEqual(3, parsed.Party.Size);
            Assert.IsTrue(parsed.Party.IsLeader);
            Assert.AreEqual(Token, parsed.SessionToken, "Fields before the party are not shifted.");
            Assert.AreEqual(IpSuccessor, parsed.Successor);
        }

        [Test]
        public void Payload_WithoutParty_JoinsHostParty()
        {
            byte[] bytes = new ConnectionPayload(Version, 1, KeyA, IpSuccessor, 0).ToBytes();

            Assert.IsTrue(ConnectionPayload.TryParse(bytes, out ConnectionPayload parsed));
            Assert.AreEqual(0UL, parsed.Party.PartyId);
            Assert.IsFalse(ConnectionAdmission.IsForeignParty(parsed, HostParty));
        }

        // ---------- Допуск ----------

        [Test]
        public void Admission_FirstOfParty_NeedsRoomForWholeParty()
        {
            var payload = Payload(new PartyTicket(GuestParty, 3, true));

            Assert.AreEqual(DisconnectReasons.PartyTooBig, Evaluate(payload, freeSlots: 2));
            Assert.AreEqual(DisconnectReasons.None, Evaluate(payload, freeSlots: 3));
        }

        [Test]
        public void Admission_LaterMember_UsesSlotsHeldForItsParty()
        {
            var payload = Payload(new PartyTicket(GuestParty, 3, false));

            // Пришли 2 из 3, хост держит последнее место: оно — для третьего, а не для постороннего.
            Assert.AreEqual(DisconnectReasons.None, Evaluate(payload, freeSlots: 1, heldForParty: 1));
            Assert.AreEqual(DisconnectReasons.LobbyFull, Evaluate(Payload(PartyTicket.JoinHostParty), freeSlots: 1, heldForOthers: 1));
        }

        [Test]
        public void Admission_FriendJoiningHostParty_IgnoresPartySize()
        {
            // Клиент с id пати хоста возвращается в неё (пати собирается у лидера): места на «всю пати» не нужны.
            var payload = Payload(new PartyTicket(HostParty, 3, false));

            Assert.AreEqual(DisconnectReasons.None, Evaluate(payload, freeSlots: 1));
        }

        [Test]
        public void Admission_PartyOfOne_GetsLobbyFull()
        {
            Assert.AreEqual(DisconnectReasons.LobbyFull, Evaluate(Payload(new PartyTicket(GuestParty, 1, true)), freeSlots: 0));
        }

        [Test]
        public void PartyTooBig_IsARejection()
        {
            Assert.IsTrue(DisconnectReasons.IsKnown(DisconnectReasons.PartyTooBig));
            Assert.IsTrue(DisconnectReasons.IsRejection(DisconnectReasons.PartyTooBig), "Retrying the same host is pointless.");
        }

        // ---------- Пати машины ----------

        [Test]
        public void State_AdoptsPartyFromSlots_AndStartsNewOnDemand()
        {
            var state = new PartyState();
            Assert.AreNotEqual(0UL, state.PartyId);
            Assert.IsTrue(state.IsLeader, "Alone, we lead our own party.");

            state.Adopt(GuestParty, false);
            Assert.AreEqual(GuestParty, state.PartyId);
            Assert.IsFalse(state.IsLeader);

            state.Adopt(0, true);
            Assert.AreEqual(GuestParty, state.PartyId, "Zero is 'no party' and is ignored.");

            PartyTicket ticket = state.Ticket(2);
            Assert.AreEqual(GuestParty, ticket.PartyId);
            Assert.AreEqual(2, ticket.Size);

            state.StartNew();
            Assert.AreNotEqual(GuestParty, state.PartyId);
            Assert.IsTrue(state.IsLeader);
        }

        private static ConnectionPayload Payload(PartyTicket party)
        {
            return new ConnectionPayload(Version, 1, KeyA, IpSuccessor, 0, party);
        }

        private static string Evaluate(in ConnectionPayload payload, int freeSlots, int heldForParty = 0, int heldForOthers = 0)
        {
            return ConnectionAdmission.Evaluate(payload, true, Version, Token, false, freeSlots, true, HostParty, heldForParty, heldForOthers);
        }
    }
}
