using TinyTumble.Gameplay.Lobby;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Bootstrap.Scopes
{
    public sealed class LobbyLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<LobbyJoinController>();
            builder.RegisterEntryPoint<LobbySessionGuard>();
        }
    }
}
