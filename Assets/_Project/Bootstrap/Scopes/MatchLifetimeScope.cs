using TinyTumble.AI.Bots;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Match;
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
            builder.Register<WanderBotInputFactory>(Lifetime.Scoped).As<IBotInputFactory>();

            builder.RegisterEntryPoint<MatchEntryPoint>(Lifetime.Scoped).AsSelf();
            builder.RegisterEntryPoint<MatchFlowController>(Lifetime.Scoped);
            builder.RegisterEntryPoint<MatchDebugCommands>(Lifetime.Scoped);
        }
    }
}
