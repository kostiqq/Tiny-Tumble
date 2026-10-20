using System.IO;
using MessagePipe;
using TinyTumble.Bootstrap.Diagnostics;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Debugging;
using TinyTumble.Core.Flow;
using TinyTumble.Core.Input;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Messages;
using TinyTumble.Core.Network;
using TinyTumble.Core.Save;
using TinyTumble.Core.Scenes;
using TinyTumble.Infrastructure.Assets;
using TinyTumble.Infrastructure.Debugging;
using TinyTumble.Infrastructure.Flow;
using TinyTumble.Infrastructure.Input;
using TinyTumble.Infrastructure.Logging;
using TinyTumble.Infrastructure.Save;
using TinyTumble.Infrastructure.Scenes;
using TinyTumble.Networking.Debugging;
using TinyTumble.Networking.Flow;
using TinyTumble.Networking.Lobby;
using TinyTumble.Networking.Session;
using TinyTumble.Networking.Spawning;
using TinyTumble.UI.Loading;
using Unity.Netcode;
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
        [SerializeField] private SessionConfig _sessionConfig;

        [Header("Сеть")]
        [Tooltip("Префаб с NetworkManager и UnityTransport. Создаётся отдельным корневым объектом: NGO не разрешает его вкладывать.")]
        [SerializeField] private NetworkManager _networkManagerPrefab;

        [Header("Вью корня")]
        [SerializeField] private LoadingScreenView _loadingScreen;
        [SerializeField] private DebugConsoleView _debugConsoleView;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(_matchConfig);
            builder.RegisterInstance(_characterConfig);
            builder.RegisterInstance(_combatConfig);
            builder.RegisterInstance(_botConfig);
            builder.RegisterInstance(_sessionConfig);

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
            // Локальные переходы делает GameFlow, а снаружи все видят сетевую обёртку: клиент идёт за хостом.
            builder.Register<GameFlow>(Lifetime.Singleton).AsSelf();

            builder.Register<LocalPlayerService>(Lifetime.Singleton).As<ILocalPlayers>();

            RegisterNetworking(builder);

            builder.Register<DebugConsole>(Lifetime.Singleton).AsSelf().As<IDebugConsole>();
            builder.RegisterComponent(_debugConsoleView);
        }

        private void RegisterNetworking(IContainerBuilder builder)
        {
            builder.Register<NetworkRuntime>(Lifetime.Singleton).WithParameter(_networkManagerPrefab);
            builder.Register<NetworkObjectResolver>(Lifetime.Singleton);
            // Пока корень жив, сетевые объекты без своего скоупа (объект сессии) внедряются им.
            builder.RegisterEntryPoint<NetworkResolverScope>();
            builder.Register<NetworkPrefabRegistry>(Lifetime.Singleton);
            builder.Register<NetworkSimulatorControl>(Lifetime.Singleton);
            builder.Register<PlayerIdentity>(Lifetime.Singleton);
            // ITickable: истечение резерва слотов отвалившихся игроков.
            builder.RegisterEntryPoint<SessionLobby>().AsSelf();
            builder.Register<NetworkSession>(Lifetime.Singleton).AsSelf().As<INetworkSession>();
            builder.RegisterEntryPoint<SessionRecovery>();
            builder.RegisterEntryPoint<NetworkGameFlow>(); // IGameFlow и IInitializable — через реализованные интерфейсы.
            builder.Register<SessionLauncher>(Lifetime.Singleton).As<ISessionLauncher>();
            builder.RegisterEntryPoint<NetworkDebugCommands>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Сквозной тест сети на двух сборках: включается ключом -tt-autotest host|client.
            if (AutoTestOptions.TryParse(System.Environment.GetCommandLineArgs(), out AutoTestOptions autoTest))
            {
                builder.RegisterInstance(autoTest);
                builder.RegisterEntryPoint<NetworkAutoTest>();
            }
#endif
        }
    }
}
