using TinyTumble.Core.Configs;
using TinyTumble.Core.Input;
using TinyTumble.Gameplay.Characters;
using UnityEngine;

namespace TinyTumble.AI.Bots
{
    public sealed class WanderBotInputFactory : IBotInputFactory
    {
        private readonly BotConfig _config;
        private readonly System.Random _seeds = new System.Random();

        public WanderBotInputFactory(BotConfig config)
        {
            _config = config;
        }

        public IPlayerInput Create(Transform body)
        {
            return new WanderBotInput(body, _config, _seeds.Next());
        }
    }
}
