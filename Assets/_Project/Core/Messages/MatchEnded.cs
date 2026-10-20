namespace TinyTumble.Core.Messages
{
    /// <summary>Матч завершён: по правилам, кнопкой или командой match.end.</summary>
    public readonly struct MatchEnded
    {
        public const int NoWinner = -1;

        /// <summary>Id участника-победителя или <see cref="NoWinner"/>.</summary>
        public readonly int WinnerId;

        public MatchEnded(int winnerId)
        {
            WinnerId = winnerId;
        }
    }
}
