using System.Collections.Generic;
using NUnit.Framework;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Session;

namespace TinyTumble.Tests
{
    public sealed class MigrationTests
    {
        private const ulong Token = 555;
        private const ulong HostKey = 100;
        private const ulong KeyA = 101;
        private const ulong KeyB = 102;
        private const ulong KeyC = 103;

        [Test]
        public void Plan_OrdersCandidatesBySlot_NotByConnectionOrder()
        {
            var slots = new SlotTable();
            slots.TryAssign(HostKey, 0, 0, out _);
            slots.TryAssign(KeyB, 2, 0, out _);
            slots.TryAssign(KeyA, 1, 0, out _);

            // Словарь пиров на хосте не упорядочен: план не должен от этого зависеть.
            var peers = new List<PeerInfo>
            {
                new PeerInfo(1, KeyA, Ip("10.0.0.2"), 7778),
                new PeerInfo(2, KeyB, Ip("10.0.0.3"), 7779),
            };

            MigrationPlan plan = MigrationPlan.Build(Token, slots, peers);

            Assert.AreEqual(Token, plan.SessionToken);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(KeyB, plan[0].PlayerKey, "P2 is first in line.");
            Assert.AreEqual("10.0.0.3", plan[0].Address);
            Assert.AreEqual(7779, plan[0].Port);
            Assert.AreEqual(KeyA, plan[1].PlayerKey);
        }

        [Test]
        public void Plan_PutsPeersWithoutSlotsLast_AndHoldsAtMostThree()
        {
            var slots = new SlotTable();
            slots.TryAssign(HostKey, 0, 0, out _);
            slots.TryAssign(KeyB, 2, 0, out _);

            var peers = new List<PeerInfo>
            {
                new PeerInfo(1, KeyA, Ip("10.0.0.2"), 7778),
                new PeerInfo(2, KeyB, Ip("10.0.0.3"), 7779),
                new PeerInfo(3, KeyC, Ip("10.0.0.4"), 7780),
                new PeerInfo(4, 104, Ip("10.0.0.5"), 7781),
            };

            MigrationPlan plan = MigrationPlan.Build(Token, slots, peers);

            Assert.AreEqual(MigrationPlan.MaxCandidates, plan.Count);
            Assert.AreEqual(KeyB, plan[0].PlayerKey);
        }

        [Test]
        public void Steps_ConnectToEarlierCandidates_ThenBecomeHost()
        {
            MigrationPlan plan = PlanOf(KeyA, KeyB, KeyC);

            List<RecoveryStep> forB = SessionRecovery.BuildSteps(plan, KeyB);
            Assert.AreEqual(2, forB.Count);
            Assert.IsFalse(forB[0].BecomeHost);
            Assert.AreEqual(7000, forB[0].Port, "First try the candidate ahead of us (A).");
            Assert.IsTrue(forB[1].BecomeHost, "Then take the role ourselves.");

            List<RecoveryStep> forA = SessionRecovery.BuildSteps(plan, KeyA);
            Assert.AreEqual(1, forA.Count);
            Assert.IsTrue(forA[0].BecomeHost, "The first candidate hosts right away.");
        }

        [Test]
        public void Steps_WhenNotInPlan_OnlyConnect()
        {
            MigrationPlan plan = PlanOf(KeyA, KeyB);

            List<RecoveryStep> steps = SessionRecovery.BuildSteps(plan, KeyC);

            Assert.AreEqual(2, steps.Count);
            Assert.IsFalse(steps[0].BecomeHost);
            Assert.IsFalse(steps[1].BecomeHost);
        }

        [Test]
        public void Plan_Equality_DetectsChanges()
        {
            MigrationPlan a = PlanOf(KeyA);
            MigrationPlan b = PlanOf(KeyA);
            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(PlanOf(KeyA, KeyB)));
            Assert.IsFalse(a.Equals(new MigrationPlan(Token + 1)));
        }

        [TestCase("127.0.0.1")]
        [TestCase("192.168.10.200")]
        [TestCase("255.255.255.255")]
        public void Ipv4_RoundTrips(string address)
        {
            Assert.IsTrue(Ipv4.TryParse(address, out uint packed));
            Assert.AreEqual(address, Ipv4.ToText(packed));
        }

        [Test]
        public void Ipv4_RejectsIpv6AndGarbage()
        {
            Assert.IsFalse(Ipv4.TryParse("::1", out _));
            Assert.IsFalse(Ipv4.TryParse("host", out _));
        }

        [Test]
        public void Identity_IsNeverZero_AndDiffersBetweenInstances()
        {
            var first = new PlayerIdentity();
            var second = new PlayerIdentity();
            Assert.AreNotEqual(0, first.Key, "0 means 'no key'.");
            Assert.AreNotEqual(first.Key, second.Key);
        }

        private static MigrationPlan PlanOf(params ulong[] keys)
        {
            var plan = new MigrationPlan(Token);
            for (int i = 0; i < keys.Length; i++)
            {
                plan.TryAdd(new MigrationCandidate(keys[i], Ip("127.0.0.1"), (ushort)(7000 + i)));
            }

            return plan;
        }

        private static uint Ip(string text)
        {
            Assert.IsTrue(Ipv4.TryParse(text, out uint address));
            return address;
        }
    }
}
