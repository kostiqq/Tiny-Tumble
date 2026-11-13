using System.Collections.Generic;

namespace TinyTumble.Networking.Matchmaking
{
    /// <summary>
    /// Правила выбора лобби и проверки пинга. Отдельно от сервиса, чтобы проверять EditMode-тестами.
    /// </summary>
    public static class MatchmakingRules
    {
        /// <summary>
        /// Лучшее лобби, куда пати может переехать: места на всю пати, оценка пинга не выше предела (неизвестный пинг
        /// допускается, но проигрывает известному), не мы и не исключённые (уже отказали или пинг оказался плохим).
        /// Порядок: больше игроков, ниже пинг, меньше ключ.
        /// Если наше лобби уже ждёт игроков (ourPlayers ≥ 0), переезжаем только туда, где игроков больше,
        /// или столько же, но ключ меньше: так два ждущих хоста сходятся в одно лобби, а не меняются местами.
        /// </summary>
        public static bool TryPick(IReadOnlyList<LobbyAd> ads, int partySlots, int maxPingMs, HashSet<ulong> excluded, ulong ownKey,
            int ourPlayers, out LobbyAd best)
        {
            best = default;
            bool found = false;
            for (int i = 0; i < ads.Count; i++)
            {
                LobbyAd ad = ads[i];
                if (ad.Key == ownKey || (excluded != null && excluded.Contains(ad.Key)) || !ad.Host.IsValid)
                {
                    continue;
                }

                if (ad.FreeSlots < partySlots || (ad.PingMs >= 0 && ad.PingMs > maxPingMs))
                {
                    continue;
                }

                if (ourPlayers >= 0 && !IsBetterThanOurs(ad, ourPlayers, ownKey))
                {
                    continue;
                }

                if (!found || Precedes(ad, best))
                {
                    best = ad;
                    found = true;
                }
            }

            return found;
        }

        public static bool IsBetterThanOurs(in LobbyAd ad, int ourPlayers, ulong ownKey)
        {
            return ad.Players > ourPlayers || (ad.Players == ourPlayers && ad.Key < ownKey);
        }

        /// <summary>
        /// Пинг пати слишком большой: медиана замеров выше предела. Медиана, а не среднее, чтобы один
        /// случайный всплеск не отправил пати обратно.
        /// </summary>
        public static bool IsPingTooHigh(float[] samples, int count, float maxRttSeconds)
        {
            if (count <= 0)
            {
                return false;
            }

            // Сортировка вставками на месте: замеров единицы, без аллокаций.
            for (int i = 1; i < count; i++)
            {
                float value = samples[i];
                int j = i - 1;
                while (j >= 0 && samples[j] > value)
                {
                    samples[j + 1] = samples[j];
                    j--;
                }

                samples[j + 1] = value;
            }

            float median = count % 2 == 1 ? samples[count / 2] : (samples[count / 2 - 1] + samples[count / 2]) * 0.5f;
            return median > maxRttSeconds;
        }

        private static bool Precedes(in LobbyAd a, in LobbyAd b)
        {
            if (a.Players != b.Players)
            {
                return a.Players > b.Players;
            }

            int pingA = a.PingMs >= 0 ? a.PingMs : int.MaxValue;
            int pingB = b.PingMs >= 0 ? b.PingMs : int.MaxValue;
            if (pingA != pingB)
            {
                return pingA < pingB;
            }

            return a.Key < b.Key;
        }
    }
}
