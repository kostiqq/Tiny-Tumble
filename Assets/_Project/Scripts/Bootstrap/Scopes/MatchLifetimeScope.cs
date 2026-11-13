using TinyTumble.AI.Bots;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
using TinyTumble.Networking.Spawning;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Bootstrap.Scopes
{
    /// <summary>
    /// Скоуп одного матча: всё зарегистрированное здесь умирает вместе со сценой Match.
    /// </summary>
    public sealed class MatchLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<MatchArena>(Lifetime.Scoped);
            builder.Register<MatchRoster>(Lifetime.Scoped);
            builder.Register<MatchCharacters>(Lifetime.Scoped).AsSelf().As<IMatchCharactersView>().As<ICombatantRegistry>();
            builder.Register<MatchNetworkObjects>(Lifetime.Scoped);
            builder.Register<BrawlerBotInputFactory>(Lifetime.Scoped).As<IBotInputFactory>();

            // Персонажи, которых NGO создаёт на клиентах, получают зависимости этого скоупа.
            builder.RegisterEntryPoint<NetworkResolverScope>(Lifetime.Scoped);
            builder.RegisterEntryPoint<MatchEntryPoint>(Lifetime.Scoped).AsSelf();
            builder.RegisterEntryPoint<MatchFlowController>(Lifetime.Scoped);
            builder.RegisterEntryPoint<MatchDebugCommands>(Lifetime.Scoped);
        }
    }
}
