using VContainer;
using VContainer.Unity;

public class GamelifetimeScope
{
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<AddressableAssetLoader>(Lifetime.Singleton).As<IAssetLoader>();
            builder.Register<ISceneLoader, AsyncSceneLoader>(Lifetime.Singleton);
        }
    }
}
