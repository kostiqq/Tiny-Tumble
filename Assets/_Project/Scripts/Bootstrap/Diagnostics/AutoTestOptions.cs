using System;
using System.Globalization;

namespace TinyTumble.Bootstrap.Diagnostics
{
    /// <summary>
    /// Ключи командной строки сетевого автотеста (только development-сборки и редактор):
    ///   -tt-autotest host|client      роль
    ///   -tt-address 127.0.0.1:7777    куда подключаться клиенту
    ///   -tt-port 7778                 порт, на котором эта копия поднимет хост (нужен разный для копий на одной машине)
    ///   -tt-clients 1                 сколько клиентов в сессии (знают и хост, и клиенты)
    ///   -tt-duration 6                сколько секунд идёт матч в сценарии full
    ///   -tt-netsim 60,10,2            задержка мс, джиттер мс, потери % (на каждой стороне)
    ///   -tt-bots 1                    сколько ботов в матче (хост); по умолчанию — из MatchConfig.
    ///                                 Выпавшие в автотесте всегда возрождаются: бот дерётся всерьёз, а тест проверяет сеть,
    ///                                 и исход драки не должен досрочно заканчивать матч
    ///   -tt-party leader|member       роль клиента в сценарии partymove
    ///   -tt-target 127.0.0.1:7777     куда лидер уводит пати в сценарии partymove
    ///   -tt-search 20                 matchmake, latejoin: сколько искать игроков до старта с ботами (и ждать между матчами), с
    ///   -tt-delay 22                  latejoin: эта копия начинает искать позже остальных, через столько секунд
    ///   -tt-scenario full|hostleave|hostcrash|clientdrop|steamhost|partymove|matchmake|latejoin
    ///       full       — лобби → матч → итоги → лобби, проверка движения своего и чужого кубика;
    ///       hostleave  — хост выходит в меню посреди матча, клиент становится новым хостом;
    ///       hostcrash  — процесс хоста убит посреди матча; первый по плану клиент становится хостом, остальные к нему переходят;
    ///       clientdrop — у клиента на 8 с пропадает сеть посреди матча; он возвращается, досматривает матч и попадает в лобби в свой слот;
    ///       steamhost  — только хост, нужен Steam: хост через Steam, лобби для приглашений открылось и закрылось;
    ///       partymove  — три копии: хост (цель), лидер пати (сначала сам хост своей пати) и участник; пати переезжает
    ///                    к хосту и возвращается к лидеру;
    ///       matchmake  — несколько копий (-tt-clients — сколько всего) ищут игру локальным матчмейкером, сходятся в одно
    ///                    лобби, по сроку поиска начинается матч, пустые места занимают боты;
    ///       latejoin   — как matchmake, но одна копия (-tt-delay) приходит между матчами: лобби после матча снова
    ///                    открывается, опоздавший занимает место бота, второй матч идёт с ним.
    /// </summary>
    public sealed class AutoTestOptions
    {
        public const string RoleKey = "-tt-autotest";

        public bool IsHost { get; private set; }
        public string Address { get; private set; } = "127.0.0.1";
        public ushort Port { get; private set; }
        public int Clients { get; private set; } = 1;
        public float MatchDuration { get; private set; } = 6f;
        public string NetworkSimulator { get; private set; }
        public AutoTestScenario Scenario { get; private set; } = AutoTestScenario.Full;

        /// <summary>partymove: этот клиент — лидер пати (хостит её и уводит), иначе участник.</summary>
        public bool IsPartyLeader { get; private set; }

        /// <summary>matchmake: срок поиска до старта с ботами, с.</summary>
        public float SearchSeconds { get; private set; } = 20f;

        /// <summary>latejoin: через сколько секунд эта копия начинает искать; 0 — сразу.</summary>
        public float StartDelay { get; private set; }

        /// <summary>partymove: куда лидер уводит пати.</summary>
        public string Target { get; private set; } = "127.0.0.1:7777";

        /// <summary>-1 — не задано: как в MatchConfig.</summary>
        public int Bots { get; private set; } = -1;

        public static bool TryParse(string[] args, out AutoTestOptions options)
        {
            options = null;
            string role = GetValue(args, RoleKey);
            if (role == null)
            {
                return false;
            }

            options = new AutoTestOptions { IsHost = string.Equals(role, "host", StringComparison.OrdinalIgnoreCase) };

            string address = GetValue(args, "-tt-address");
            if (!string.IsNullOrEmpty(address))
            {
                options.Address = address;
            }

            if (ushort.TryParse(GetValue(args, "-tt-port"), out ushort port) && port > 0)
            {
                options.Port = port;
            }

            if (int.TryParse(GetValue(args, "-tt-clients"), out int clients) && clients > 0)
            {
                options.Clients = clients;
            }

            if (float.TryParse(GetValue(args, "-tt-duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration) && duration > 0f)
            {
                options.MatchDuration = duration;
            }

            options.NetworkSimulator = GetValue(args, "-tt-netsim");

            if (int.TryParse(GetValue(args, "-tt-bots"), out int bots) && bots >= 0)
            {
                options.Bots = bots;
            }

            if (float.TryParse(GetValue(args, "-tt-search"), NumberStyles.Float, CultureInfo.InvariantCulture, out float search) && search > 0f)
            {
                options.SearchSeconds = search;
            }

            if (float.TryParse(GetValue(args, "-tt-delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out float delay) && delay > 0f)
            {
                options.StartDelay = delay;
            }

            options.IsPartyLeader = string.Equals(GetValue(args, "-tt-party"), "leader", StringComparison.OrdinalIgnoreCase);
            string target = GetValue(args, "-tt-target");
            if (!string.IsNullOrEmpty(target))
            {
                options.Target = target;
            }

            string scenario = GetValue(args, "-tt-scenario");
            if (!string.IsNullOrEmpty(scenario))
            {
                if (!Enum.TryParse(scenario, true, out AutoTestScenario parsed))
                {
                    throw new ArgumentException($"Unknown -tt-scenario '{scenario}'.");
                }

                options.Scenario = parsed;
            }

            return true;
        }

        private static string GetValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }

    public enum AutoTestScenario
    {
        Full,
        HostLeave,
        HostCrash,
        ClientDrop,

        /// <summary>Только хост, нужен запущенный Steam: хост через Steam, лобби для приглашений открылось и закрылось.</summary>
        SteamHost,

        /// <summary>Хост + лидер пати + участник: пати переезжает к хосту целиком и возвращается к лидеру.</summary>
        PartyMove,

        /// <summary>Все копии ищут игру, сходятся в одно лобби и по сроку поиска играют матч с ботами на пустых местах.</summary>
        Matchmake,

        /// <summary>Двое находят друг друга и играют с ботами; третий приходит между матчами и играет второй матч.</summary>
        LateJoin,
    }
}
