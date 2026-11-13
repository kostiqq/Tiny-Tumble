using VContainer;
using VContainer.Unity;

namespace TinyTumble.Bootstrap.Scopes
{
    /// <summary>
    /// Скоуп главного меню. Родитель не задан явно: VContainer подставляет корень из VContainerSettings.
    /// UI сцены внедряется через список Auto Inject Game Objects.
    /// </summary>
    public sealed class MainMenuLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
        }
    }
}
