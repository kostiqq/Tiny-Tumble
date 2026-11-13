namespace TinyTumble.Core.Messages
{
    /// <summary>Участник матча выпал со стола.</summary>
    public readonly struct PlayerEliminated
    {
        /// <summary>Id участника матча (игрок или бот).</summary>
        public readonly int ParticipantId;

        public readonly bool IsBot;

        public PlayerEliminated(int participantId, bool isBot)
        {
            ParticipantId = participantId;
            IsBot = isBot;
        }
    }
}
