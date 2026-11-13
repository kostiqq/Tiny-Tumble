using TinyTumble.Core.Configs;
using UnityEngine;

namespace TinyTumble.Gameplay.Match
{
    /// <summary>
    /// Настройки следующего матча, которые выбирают в лобби (число ботов, правило выпадения). Живут дольше матча,
    /// поэтому зарегистрированы в корне. Начальные значения — из <see cref="MatchConfig"/>.
    /// </summary>
    public sealed class MatchSetup
    {
        private const int MaxBots = 3;

        private int _botCount;

        public MatchSetup(MatchConfig config)
        {
            _botCount = config.BotCount;
            RespawnOnFall = config.RespawnOnFall;
        }

        /// <summary>Выпавший возвращается на стол, а не выбывает (до режимов этапа 6 — единственное правило выбора).</summary>
        public bool RespawnOnFall { get; set; }

        public int BotCount
        {
            get => _botCount;
            set => _botCount = Mathf.Clamp(value, 0, MaxBots);
        }
    }
}
