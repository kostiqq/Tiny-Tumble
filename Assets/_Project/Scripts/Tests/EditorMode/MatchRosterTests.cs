using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using TinyTumble.Core.Messages;
using TinyTumble.Gameplay.Match;

namespace TinyTumble.Tests
{
    public sealed class MatchRosterTests
    {
        private sealed class FakePublisher<T> : IPublisher<T>
        {
            public List<T> Published { get; } = new List<T>();

            public void Publish(T message)
            {
                Published.Add(message);
            }
        }

        private FakePublisher<PlayerEliminated> _eliminated;
        private FakePublisher<MatchEnded> _ended;
        private MatchRoster _roster;

        [SetUp]
        public void SetUp()
        {
            _eliminated = new FakePublisher<PlayerEliminated>();
            _ended = new FakePublisher<MatchEnded>();
            _roster = new MatchRoster(_eliminated, _ended);
        }

        [Test]
        public void LastOneStanding_Wins()
        {
            int player = _roster.Add(isBot: false);
            int bot = _roster.Add(isBot: true);

            _roster.Eliminate(bot);

            Assert.AreEqual(1, _eliminated.Published.Count);
            Assert.IsTrue(_eliminated.Published[0].IsBot);
            Assert.AreEqual(1, _ended.Published.Count);
            Assert.AreEqual(player, _ended.Published[0].WinnerId);
        }

        [Test]
        public void SoloParticipant_EndsOnlyWhenEliminated()
        {
            int solo = _roster.Add(isBot: false);
            Assert.IsEmpty(_ended.Published);

            _roster.Eliminate(solo);

            Assert.AreEqual(1, _ended.Published.Count);
            Assert.AreEqual(MatchEnded.NoWinner, _ended.Published[0].WinnerId);
        }

        [Test]
        public void RepeatedElimination_IsIgnored()
        {
            _roster.Add(isBot: false);
            _roster.Add(isBot: false);
            int third = _roster.Add(isBot: true);

            _roster.Eliminate(third);
            _roster.Eliminate(third);

            Assert.AreEqual(1, _eliminated.Published.Count);
            Assert.IsEmpty(_ended.Published);
        }

        [Test]
        public void MatchEndsOnce()
        {
            _roster.Add(isBot: false);
            _roster.Add(isBot: true);

            _roster.End(MatchEnded.NoWinner);
            _roster.End(MatchEnded.NoWinner);
            _roster.Eliminate(0);

            Assert.AreEqual(1, _ended.Published.Count);
            Assert.IsEmpty(_eliminated.Published);
        }
    }
}
