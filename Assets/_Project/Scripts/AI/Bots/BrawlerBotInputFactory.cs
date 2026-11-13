using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Characters;

namespace TinyTumble.AI.Bots
{
    public sealed class BrawlerBotInputFactory : IBotInputFactory
    {
        private readonly BotConfig _config;
        private readonly ICombatantRegistry _characters;
        private readonly System.Random _seeds = new System.Random();

        public BrawlerBotInputFactory(BotConfig config, ICombatantRegistry characters)
        {
            _config = config;
            _characters = characters;
        }

        public IPlayerInput Create(ICombatant self)
        {
            return new BrawlerBotInput(self, _characters, _config, _seeds.Next());
        }
    }
}
