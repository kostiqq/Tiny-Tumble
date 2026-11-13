using TinyTumble.Core.Network;

namespace TinyTumble.Networking.Party
{
    /// <summary>Команда пати переехать. Хост рассылает её всем; выполняют только участники этой пати.</summary>
    public readonly struct PartyMove
    {
        public readonly ulong PartyId;

        /// <summary>Куда переезжать. None — собраться у лидера: он становится хостом, остальные идут к нему.</summary>
        public readonly SessionEndpoint Target;

        public readonly ulong LeaderKey;

        /// <summary>Сколько слотов занимает пати: столько мест новый хост должен найти сразу.</summary>
        public readonly int PartySize;

        /// <summary>
        /// Где лидер поднимет хост, если пати будет собираться у него: его адрес преемника, каким его видит хост.
        /// None, если лидер и есть хост (к нему подключаются по прежнему адресу) или до него не достучаться.
        /// </summary>
        public readonly SessionEndpoint LeaderEndpoint;

        public PartyMove(ulong partyId, SessionEndpoint target, ulong leaderKey, int partySize, SessionEndpoint leaderEndpoint = default)
        {
            PartyId = partyId;
            Target = target;
            LeaderKey = leaderKey;
            PartySize = partySize;
            LeaderEndpoint = leaderEndpoint;
        }

        public bool IsRegroup => !Target.IsValid;

        public PartyMove WithLeaderEndpoint(SessionEndpoint leaderEndpoint)
        {
            return new PartyMove(PartyId, Target, LeaderKey, PartySize, leaderEndpoint);
        }
    }
}
