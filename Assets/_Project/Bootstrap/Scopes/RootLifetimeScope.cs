using System.IO;
using MessagePipe;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Messages;
using TinyTumble.Core.Save;
using TinyTumble.Core.Scenes;
using TinyTumble.Infrastructure.Assets;
using TinyTumble.Infrastructure.Debugging;
using TinyTumble.Infrastructure.Flow;
using TinyTumble.Infrastructure.Input;
using TinyTumble.Infrastructure.Logging;
using TinyTumble.Infrastructure.Save;
using TinyTumble.Infrastructure.Scenes;
using TinyTumble.UI.Loading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TinyTumble.Bootstrap.Scopes
{
    /// <summary>
    /// Корень всей игры. Префаб назначен в VContainerSettings, поэтому поднимается при Play из любой сцены
    /// и переживает смену сцен. Здесь только регистрации.
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        private const string SaveFolder = "Saves";

        [Header("Конфиги")]
        [SerializeField] private MatchConfig _matchConfig;
        [SerializeField] private CharacterConfig _characterConfig;
        [SerializeField] private CombatConfig _combatConfig;
        [SerializeField] private BotConfig _botConfig;

        [Header("Вью корня")]
        [SerializeField] private LoadingScreenView _loadingScreen;
        [SerializeField] private DebugConsoleView _debugConsoleView;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(_matchConfig);
            builder.RegisterInstance(_characterConfig);
            builder.RegisterInstance(_combatConfig);
            builder.RegisterInstance(_botConfig);

            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PlayerEliminated>(options);
            builder.RegisterMessageBroker<MatchEnded>(options);

            builder.Register<UnityGameLog>(Lifetime.Singleton).As<IGameLog>();
            builder.Register<JsonSaveService>(Lifetime.Singleton).As<ISaveService>()
                .WithParameter(Path.Combine(Application.persistentDataPath, SaveFolder));
            builder.RegisterEntryPoint<SettingsService>();

            builder.Register<SceneLoader>(Lifetime.Singleton).As<ISceneLoader>();
            builder.Register<AddressableAssetProvider>(Lifetime.Singleton).As<IAssetProvider>();
            builder.RegisterComponent(_loadingScreen).As<ILoadingScreen>();
            builder.Register<GameFlow>(Lifetime.Singleton).As<IGameFlow>();

            builder.Register<LocalPlayerService>(Lifetime.Singleton).As<ILocalPlayers>();

            builder.Register<DebugConsole>(Lifetime.Singleton).AsSelf().As<IDebugConsole>();
            builder.RegisterComponent(_debugConsoleView);
        }
    }
}
