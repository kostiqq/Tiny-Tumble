using System.Collections.Generic;
using MessagePipe;
using TinyTumble.Core.Messages;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Участники матча и правило Last One Standing: когда на столе остаётся один (или никого), матч заканчивается.
    /// </summary>
    public sealed class MatchRoster
    {
        private readonly IPublisher<PlayerEliminated> _eliminatedPublisher;
        private readonly IPublisher<MatchEnded> _endedPublisher;
        private readonly List<int> _alive = new List<int>();
        private readonly List<bool> _isBot = new List<bool>();
        private bool _ended;

        public MatchRoster(IPublisher<PlayerEliminated> eliminatedPublisher, IPublisher<MatchEnded> endedPublisher)
        {
            _eliminatedPublisher = eliminatedPublisher;
            _endedPublisher = endedPublisher;
        }

        public int ParticipantCount => _isBot.Count;

        public int AliveCount => _alive.Count;

        public bool IsEnded => _ended;

        /// <returns>Id участника.</returns>
        public int Add(bool isBot)
        {
            int id = _isBot.Count;
            _isBot.Add(isBot);
            _alive.Add(id);
            return id;
        }

        public void Eliminate(int participantId)
        {
            if (_ended || !_alive.Remove(participantId))
            {
                return;
            }

            _eliminatedPublisher.Publish(new PlayerEliminated(participantId, _isBot[participantId]));

            // В одиночку (один участник) матч не заканчивается, пока он сам не выпадет.
            int survivorsToWin = ParticipantCount > 1 ? 1 : 0;
            if (_alive.Count <= survivorsToWin)
            {
                End(_alive.Count == 1 ? _alive[0] : MatchEnded.NoWinner);
            }
        }

        public void End(int winnerId)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            _endedPublisher.Publish(new MatchEnded(winnerId));
        }
    }
}
