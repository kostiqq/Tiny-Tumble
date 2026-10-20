using VContainer;
using VContainer.Unity;

namespace TinyTumble.Bootstrap.Scopes
{
    public sealed class BootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<BootEntryPoint>();
        }
    }
}
