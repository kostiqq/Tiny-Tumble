using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Gameplay.Cameras;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Gameplay.Combat;
using TinyTumble.Infrastructure.Debugging;
using TinyTumble.Networking.Input;
using TinyTumble.Networking.Lobby;
using TinyTumble.UI;
using TinyTumble.UI.Flow;
using TinyTumble.UI.Loading;
using TinyTumble.UI.Lobby;
using TinyTumble.UI.Match;
using TinyTumble.UI.Menu;
using TinyTumble.UI.Session;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace TinyTumble.Bootstrap.Editor
{
    /// <summary>
    /// Собирает каркас этапов 1–4: конфиги, корневой префаб, NetworkManager, VContainerSettings, группы Addressables,
    /// тестовую арену с kill-зоной, сетевые префабы (сессия, персонаж-капсула, предмет для броска), частицы попадания,
    /// камеру Cinemachine, сцены и Build Settings. Идемпотентен:
    /// повторный запуск пересоздаёт сгенерированные объекты и не трогает остальное.
    /// Запуск: меню Tiny Tumble → Setup → Generate Project Assets, или batchmode
    /// -executeMethod TinyTumble.Bootstrap.Editor.ProjectSetup.RunBatch
    /// </summary>
    public static class ProjectSetup
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ConfigsFolder = "Assets/Configs";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string SettingsFolder = "Assets/Settings";
        private const string MaterialsFolder = "Assets/Art/Materials";
        private const string PhysicsFolder = "Assets/Art/Physics";
        private const string FxFolder = "Assets/Prefabs/FX";
        private const string ItemsFolder = "Assets/AddressableAssets/Items";
        private const string ArenasFolder = "Assets/AddressableAssets/Arenas";
        private const string CharactersFolder = "Assets/AddressableAssets/Characters";
        private const string NetworkFolder = "Assets/AddressableAssets/Network";

        private const string RootPrefabPath = PrefabsFolder + "/RootLifetimeScope.prefab";
        private const string NetworkManagerPrefabPath = PrefabsFolder + "/NetworkManager.prefab";
        private const string SessionPrefabPath = NetworkFolder + "/" + AddressKeys.SessionState + ".prefab";
        private const string LegacyBootstrapPrefabPath = PrefabsFolder + "/Bootstrap.prefab";
        private const string VContainerSettingsPath = SettingsFolder + "/VContainerSettings.asset";
        private const string ArenaScenePath = ArenasFolder + "/" + AddressKeys.TestArena + ".unity";
        private const string CharacterPrefabPath = CharactersFolder + "/" + AddressKeys.Character + ".prefab";
        private const string LegacyCharacterPrefabPath = CharactersFolder + "/Character_Stub.prefab";
        private const string ItemPrefabPath = ItemsFolder + "/" + AddressKeys.ThrowableItem + ".prefab";
        private const string HitBurstPrefabPath = FxFolder + "/FX_HitBurst.prefab";
        private const string ArenaMaterialPath = MaterialsFolder + "/M_ArenaStub.mat";
        private const string CharacterMaterialPath = MaterialsFolder + "/M_CharacterStub.mat";
        private const string CharacterFaceMaterialPath = MaterialsFolder + "/M_CharacterFace.mat";
        private const string ItemMaterialPath = MaterialsFolder + "/M_ItemStub.mat";
        private const string CharacterPhysicsPath = PhysicsFolder + "/PM_Character.asset";
        private const string UrpParticlesMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        private const string ScopeObjectName = "[Scope]";
        private const string UiObjectName = "[UI]";
        private const string MatchObjectName = "[Match]";

        private static readonly string[] s_addressableGroups = { "Core", "Arenas", "Characters", "Cosmetics", "Bosses", "Audio" };

        private static readonly string[] s_buildScenes =
        {
            SceneNames.Boot, SceneNames.MainMenu, SceneNames.Lobby, SceneNames.Match, SceneNames.Results,
        };

        private static readonly Color s_tableColor = new Color(0.72f, 0.53f, 0.34f);
        private static readonly Color s_loadingBackground = new Color(0.08f, 0.07f, 0.10f, 1f);
        private static readonly Color s_faceColor = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color s_itemColor = new Color(0.55f, 0.80f, 0.95f);
        private static readonly Vector3 s_cameraOffset = new Vector3(0f, 11f, -10f);

        [MenuItem("Tiny Tumble/Setup/Generate Project Assets")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string startScene = SceneManager.GetActiveScene().path;

            EnsureFolder(ScenesFolder);
            EnsureFolder(ConfigsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PhysicsFolder);
            EnsureFolder(FxFolder);
            EnsureFolder(ItemsFolder);
            EnsureFolder(ArenasFolder);
            EnsureFolder(CharactersFolder);
            EnsureFolder(NetworkFolder);

            var matchConfig = EnsureAsset<MatchConfig>(ConfigsFolder + "/MatchConfig.asset");
            var characterConfig = EnsureAsset<CharacterConfig>(ConfigsFolder + "/CharacterConfig.asset");
            var combatConfig = EnsureAsset<CombatConfig>(ConfigsFolder + "/CombatConfig.asset");
            var botConfig = EnsureAsset<BotConfig>(ConfigsFolder + "/BotConfig.asset");
            var sessionConfig = EnsureAsset<SessionConfig>(ConfigsFolder + "/SessionConfig.asset");
            var inputConfig = EnsureAsset<InputConfig>(ConfigsFolder + "/InputConfig.asset");

            Material arenaMaterial = EnsureLitMaterial(ArenaMaterialPath, s_tableColor);
            Material characterMaterial = EnsureLitMaterial(CharacterMaterialPath, Color.white);
            Material faceMaterial = EnsureLitMaterial(CharacterFaceMaterialPath, s_faceColor);
            Material itemMaterial = EnsureLitMaterial(ItemMaterialPath, s_itemColor);
            PhysicsMaterial characterPhysics = EnsureCharacterPhysics();

            NetworkManager networkManagerPrefab = CreateNetworkManagerPrefab();
            RootLifetimeScope rootPrefab = CreateRootPrefab(matchConfig, characterConfig, combatConfig, botConfig, sessionConfig, inputConfig, networkManagerPrefab);
            SetupVContainerSettings(rootPrefab);

            CreateSessionPrefab();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LegacyCharacterPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyCharacterPrefabPath);
            }

            CreateCharacterPrefab(characterMaterial, faceMaterial, characterPhysics, characterConfig);
            CreateItemPrefab(itemMaterial);
            ParticleSystem hitBurst = CreateHitBurstPrefab();
            CreateArenaScene(arenaMaterial);
            SetupAddressables();

            SetupBootScene();
            SetupMainMenuScene();
            SetupLobbyScene();
            SetupMatchScene(hitBurst);
            SetupResultsScene();
            SetupBuildSettings();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(LegacyBootstrapPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyBootstrapPrefabPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Application.isBatchMode && !string.IsNullOrEmpty(startScene) && File.Exists(startScene))
            {
                EditorSceneManager.OpenScene(startScene);
            }

            Debug.Log("[Setup] Project assets generated.");
        }

        /// <summary>Точка входа для batchmode: код выхода 0 — успех, 1 — ошибка.</summary>
        public static void RunBatch()
        {
            try
            {
                Run();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        // ---------- Ассеты ----------

        private static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Material EnsureLitMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = GetDefaultLitShader();
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader GetDefaultLitShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null)
            {
                return pipeline.defaultMaterial.shader;
            }

            return Shader.Find("Standard");
        }

        private static PhysicsMaterial EnsureCharacterPhysics()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(CharacterPhysicsPath);
            if (material == null)
            {
                material = new PhysicsMaterial("PM_Character");
                AssetDatabase.CreateAsset(material, CharacterPhysicsPath);
            }

            // Трение гасит отлёт и катание в ragdoll; в обычном состоянии скоростью управляет мотор, а не трение.
            material.dynamicFriction = 0.5f;
            material.staticFriction = 0.5f;
            material.bounciness = 0.15f;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            material.bounceCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- Корень ----------

        private static NetworkManager CreateNetworkManagerPrefab()
        {
            var root = new GameObject("NetworkManager");
            try
            {
                var transport = root.AddComponent<UnityTransport>();
                var manager = root.AddComponent<NetworkManager>();

                manager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    PlayerPrefab = null,
                    ConnectionApproval = true,
                    // Сцены грузим сами (арены — сцены Addressables), хост объявляет переходы через объект сессии.
                    EnableSceneManagement = false,
                    // Префабы регистрируются в рантайме из Addressables; NGO проверит, что у хоста и клиента они совпадают.
                    ForceSamePrefabs = true,
                };
                manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
                manager.RunInBackground = true;

                PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<NetworkManager>(NetworkManagerPrefabPath);
        }

        private static RootLifetimeScope CreateRootPrefab(
            MatchConfig matchConfig,
            CharacterConfig characterConfig,
            CombatConfig combatConfig,
            BotConfig botConfig,
            SessionConfig sessionConfig,
            InputConfig inputConfig,
            NetworkManager networkManagerPrefab)
        {
            var root = new GameObject("RootLifetimeScope");
            try
            {
                var scope = root.AddComponent<RootLifetimeScope>();

                var eventSystemObject = new GameObject("EventSystem");
                eventSystemObject.transform.SetParent(root.transform, false);
                eventSystemObject.AddComponent<EventSystem>();
                var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
                inputModule.AssignDefaultActions();

                LoadingScreenView loadingScreen = CreateLoadingScreen(root.transform);

                var consoleObject = new GameObject("DebugConsole");
                consoleObject.transform.SetParent(root.transform, false);
                var console = consoleObject.AddComponent<DebugConsoleView>();

                var serialized = new SerializedObject(scope);
                serialized.FindProperty("_matchConfig").objectReferenceValue = matchConfig;
                serialized.FindProperty("_characterConfig").objectReferenceValue = characterConfig;
                serialized.FindProperty("_combatConfig").objectReferenceValue = combatConfig;
                serialized.FindProperty("_botConfig").objectReferenceValue = botConfig;
                serialized.FindProperty("_sessionConfig").objectReferenceValue = sessionConfig;
                serialized.FindProperty("_inputConfig").objectReferenceValue = inputConfig;
                serialized.FindProperty("_networkManagerPrefab").objectReferenceValue = networkManagerPrefab;
                serialized.FindProperty("_loadingScreen").objectReferenceValue = loadingScreen;
                serialized.FindProperty("_debugConsoleView").objectReferenceValue = console;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RootPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<RootLifetimeScope>(RootPrefabPath);
        }

        private static LoadingScreenView CreateLoadingScreen(Transform parent)
        {
            GameObject canvasObject = CreateCanvas("LoadingScreen", sortingOrder: 1000);
            canvasObject.transform.SetParent(parent, false);

            var group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            Stretch(background.GetComponent<RectTransform>());
            background.GetComponent<Image>().color = s_loadingBackground;

            Text label = CreateText(canvasObject.transform, "Label", "Загрузка…", 56, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);

            var view = canvasObject.AddComponent<LoadingScreenView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("_canvasGroup").objectReferenceValue = group;
            serialized.FindProperty("_label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        private static void SetupVContainerSettings(RootLifetimeScope rootPrefab)
        {
            var settings = AssetDatabase.LoadAssetAtPath<VContainerSettings>(VContainerSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<VContainerSettings>();
                AssetDatabase.CreateAsset(settings, VContainerSettingsPath);
            }

            settings.RootLifetimeScope = rootPrefab;
            EditorUtility.SetDirty(settings);

            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            preloaded.RemoveAll(asset => asset == null || asset is VContainerSettings);
            preloaded.Add(settings);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        // ---------- Контент ----------

        private static void CreateSessionPrefab()
        {
            var root = new GameObject(AddressKeys.SessionState);
            try
            {
                root.AddComponent<NetworkObject>();
                root.AddComponent<SessionNetworkState>();
                PrefabUtility.SaveAsPrefabAsset(root, SessionPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            EnsureNetworkObjectHash(SessionPrefabPath);
        }

        private static void CreateCharacterPrefab(Material material, Material faceMaterial, PhysicsMaterial physics, CharacterConfig config)
        {
            float height = config.CharacterHeight;
            float radius = config.BodyRadius;
            var root = new GameObject(AddressKeys.Character);
            try
            {
                // Pivot у ног (договорённость арт-пайплайна): капсула-коллайдер поднята на половину роста.
                var collider = root.AddComponent<CapsuleCollider>();
                collider.height = height;
                collider.radius = radius;
                collider.center = Vector3.up * (height * 0.5f);
                collider.sharedMaterial = physics;

                var body = root.AddComponent<Rigidbody>();
                body.mass = config.Mass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                // Тело бывает кинематическим (у клиентов, при захвате): Speculative работает в обоих режимах.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.angularDamping = 0.5f;

                root.AddComponent<NetworkObject>();
                // Движение синхронизирует CharacterNetSync (предсказание у владельца), а не NetworkTransform.
                var networkInput = root.AddComponent<NetworkPlayerInput>();
                var sync = root.AddComponent<CharacterNetSync>();

                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var presentation = visual.AddComponent<CharacterPresentation>();

                GameObject bodyMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bodyMesh.name = "Body";
                Object.DestroyImmediate(bodyMesh.GetComponent<Collider>());
                bodyMesh.transform.SetParent(visual.transform, false);
                // Примитив капсулы: высота 2, радиус 0.5.
                bodyMesh.transform.localPosition = Vector3.up * (height * 0.5f);
                bodyMesh.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                var bodyRenderer = bodyMesh.GetComponent<MeshRenderer>();
                bodyRenderer.sharedMaterial = material;

                // «Нос» показывает, куда смотрит персонаж: без него у капсулы нет лица.
                GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                nose.name = "Nose";
                Object.DestroyImmediate(nose.GetComponent<Collider>());
                nose.transform.SetParent(visual.transform, false);
                nose.transform.localPosition = new Vector3(0f, height * 0.68f, radius * 0.95f);
                nose.transform.localScale = new Vector3(radius * 0.6f, radius * 0.35f, radius * 0.5f);
                nose.GetComponent<MeshRenderer>().sharedMaterial = faceMaterial;

                var serializedPresentation = new SerializedObject(presentation);
                SetComponentArray(serializedPresentation.FindProperty("_renderers"), new Object[] { bodyRenderer });
                serializedPresentation.ApplyModifiedPropertiesWithoutUndo();

                var character = root.AddComponent<PlayerCharacter>();
                var serialized = new SerializedObject(character);
                serialized.FindProperty("_rigidbody").objectReferenceValue = body;
                serialized.FindProperty("_collider").objectReferenceValue = collider;
                serialized.FindProperty("_networkInput").objectReferenceValue = networkInput;
                serialized.FindProperty("_sync").objectReferenceValue = sync;
                serialized.FindProperty("_presentation").objectReferenceValue = presentation;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, CharacterPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            EnsureNetworkObjectHash(CharacterPrefabPath);
        }

        private static void CreateItemPrefab(Material material)
        {
            const float size = 0.4f;
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = AddressKeys.ThrowableItem;
            try
            {
                cube.transform.localScale = Vector3.one * size;
                cube.GetComponent<MeshRenderer>().sharedMaterial = material;

                var body = cube.AddComponent<Rigidbody>();
                body.mass = 0.5f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                cube.AddComponent<NetworkObject>();
                var networkTransform = cube.AddComponent<NetworkTransform>();
                networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                networkTransform.SyncScaleX = false;
                networkTransform.SyncScaleY = false;
                networkTransform.SyncScaleZ = false;
                networkTransform.Interpolate = true;
                // У не-авторитетной стороны делает тело кинематическим: предметы симулирует хост.
                cube.AddComponent<NetworkRigidbody>();

                var item = cube.AddComponent<GrabbableItem>();
                var serialized = new SerializedObject(item);
                serialized.FindProperty("_rigidbody").objectReferenceValue = body;
                serialized.FindProperty("_collider").objectReferenceValue = cube.GetComponent<BoxCollider>();
                serialized.FindProperty("_networkTransform").objectReferenceValue = networkTransform;
                serialized.FindProperty("_holdBottomOffset").floatValue = size * 0.5f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(cube, ItemPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }

            EnsureNetworkObjectHash(ItemPrefabPath);
        }

        private static ParticleSystem CreateHitBurstPrefab()
        {
            var root = new GameObject("FX_HitBurst");
            try
            {
                var particles = root.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                ParticleSystem.MainModule main = particles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.5f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.2f));
                main.gravityModifier = 0.6f;
                // Пул переставляет систему от удара к удару: частицы прошлого удара должны остаться на месте.
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 256;

                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTime = 0f;

                ParticleSystem.ShapeModule shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.1f;

                ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                Material particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(UrpParticlesMaterialPath);
                if (particleMaterial == null)
                {
                    particleMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                }

                renderer.sharedMaterial = particleMaterial;

                PrefabUtility.SaveAsPrefabAsset(root, HitBurstPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<ParticleSystem>(HitBurstPrefabPath);
        }

        /// <summary>
        /// GlobalObjectIdHash сетевого префаба NGO вычисляет в OnValidate по id ассета. У префаба, созданного
        /// скриптом, OnValidate мог отработать до сохранения ассета — пересчитываем и проверяем явно.
        /// </summary>
        private static void EnsureNetworkObjectHash(string prefabPath)
        {
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            var networkObject = AssetDatabase.LoadAssetAtPath<NetworkObject>(prefabPath);
            if (networkObject == null)
            {
                throw new InvalidOperationException($"No NetworkObject on {prefabPath}.");
            }

            MethodInfo onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onValidate != null)
            {
                onValidate.Invoke(networkObject, null);
            }

            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssetIfDirty(networkObject);

            if (networkObject.PrefabIdHash == 0)
            {
                throw new InvalidOperationException($"NetworkObject on {prefabPath} has no GlobalObjectIdHash.");
            }
        }

        private static void CreateArenaScene(Material material)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.position = new Vector3(0f, -0.5f, 0f);
            table.transform.localScale = new Vector3(10f, 1f, 10f);
            table.GetComponent<MeshRenderer>().sharedMaterial = material;
            table.isStatic = true;

            // Kill-зона под столом: всё, что упало, выбывает (персонажи) или возвращается (предметы).
            var killZone = new GameObject("KillZone");
            killZone.transform.position = new Vector3(0f, -3f, 0f);
            var trigger = killZone.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(60f, 2f, 60f);
            killZone.AddComponent<KillZone>();

            EditorSceneManager.SaveScene(scene, ArenaScenePath);
        }

        private static void SetupAddressables()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

            var groups = new Dictionary<string, AddressableAssetGroup>();
            foreach (string groupName in s_addressableGroups)
            {
                AddressableAssetGroup group = settings.FindGroup(groupName);
                if (group == null)
                {
                    group = settings.CreateGroup(groupName, false, false, true, null,
                        typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                }

                groups[groupName] = group;
            }

            settings.DefaultGroup = groups["Core"];

            // Пустая группа по умолчанию из шаблона Addressables не нужна: у нас своя раскладка.
            AddressableAssetGroup legacyDefault = settings.FindGroup("Default Local Group");
            if (legacyDefault != null && legacyDefault.entries.Count == 0)
            {
                settings.RemoveGroup(legacyDefault);
            }

            settings.AddLabel(AddressKeys.ArenaLabel);
            settings.AddLabel(AddressKeys.CharacterLabel);
            settings.AddLabel(AddressKeys.ItemLabel);
            settings.AddLabel(AddressKeys.NetworkLabel);

            AddEntry(settings, groups["Arenas"], ArenaScenePath, AddressKeys.TestArena, AddressKeys.ArenaLabel);
            AddEntry(settings, groups["Characters"], CharacterPrefabPath, AddressKeys.Character, AddressKeys.CharacterLabel, AddressKeys.NetworkLabel);
            // Предметы — часть набора арены (свои по комнатам), поэтому в группе арен.
            AddEntry(settings, groups["Arenas"], ItemPrefabPath, AddressKeys.ThrowableItem, AddressKeys.ItemLabel, AddressKeys.NetworkLabel);
            AddEntry(settings, groups["Core"], SessionPrefabPath, AddressKeys.SessionState, AddressKeys.NetworkLabel);

            EditorUtility.SetDirty(settings);
        }

        private static void AddEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address, params string[] labels)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException($"Asset not found: {path}");
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, true);
            entry.address = address;
            foreach (string label in labels)
            {
                entry.SetLabel(label, true, true, true);
            }
        }

        // ---------- Сцены ----------

        private static void SetupBootScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = s_loadingBackground;
            cameraObject.tag = "MainCamera";

            CreateScope<BootLifetimeScope>(null);
            EditorSceneManager.SaveScene(scene, ScenePath(SceneNames.Boot));
        }

        private static void SetupMainMenuScene()
        {
            Scene scene = OpenOrCreateScene(SceneNames.MainMenu);
            GameObject ui = CreateUiRoot();
            CreateTitle(ui.transform, "Tiny Tumble");

            Button host = CreateButton(ui.transform, ButtonNames.Host, "Играть");
            SetRect((RectTransform)host.transform, new Vector2(0.5f, 0f), new Vector2(480f, 110f), new Vector2(0f, 380f));

            InputField address = CreateInputField(ui.transform, "Address", "127.0.0.1", "IP хоста");
            SetRect((RectTransform)address.transform, new Vector2(0.5f, 0f), new Vector2(420f, 90f), new Vector2(-190f, 230f));

            Button join = CreateButton(ui.transform, ButtonNames.Join, "Подключиться");
            SetRect((RectTransform)join.transform, new Vector2(0.5f, 0f), new Vector2(340f, 90f), new Vector2(230f, 230f));

            Text status = CreateText(ui.transform, "Status", string.Empty, 32, TextAnchor.MiddleCenter);
            SetRect(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(1400f, 80f), new Vector2(0f, 110f));

            var menu = ui.AddComponent<MultiplayerMenuView>();
            var serialized = new SerializedObject(menu);
            serialized.FindProperty("_hostButton").objectReferenceValue = host;
            serialized.FindProperty("_joinButton").objectReferenceValue = join;
            serialized.FindProperty("_addressField").objectReferenceValue = address;
            serialized.FindProperty("_statusLabel").objectReferenceValue = status;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateScope<MainMenuLifetimeScope>(ui);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetupLobbyScene()
        {
            Scene scene = OpenOrCreateScene(SceneNames.Lobby);
            GameObject ui = CreateUiRoot();
            CreateTitle(ui.transform, "Лобби");

            Text players = CreateText(ui.transform, "Players", string.Empty, 32, TextAnchor.MiddleCenter);
            SetRect(players.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1400f, 420f), new Vector2(0f, 60f));
            var playersView = players.gameObject.AddComponent<LobbyPlayersView>();
            var serialized = new SerializedObject(playersView);
            serialized.FindProperty("_label").objectReferenceValue = players;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // «Начать матч» видит только хост, «Готов» — только клиент, поэтому они на одном месте.
            Button start = CreateFlowButton(ui.transform, ButtonNames.StartMatch, "Начать матч", GameState.Match, new Vector2(-280f, 200f));
            var gate = start.gameObject.AddComponent<LobbyStartGate>();
            serialized = new SerializedObject(gate);
            serialized.FindProperty("_button").objectReferenceValue = start;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Button ready = CreateButton(ui.transform, ButtonNames.Ready, "Готов");
            SetRect((RectTransform)ready.transform, new Vector2(0.5f, 0f), new Vector2(480f, 110f), new Vector2(-280f, 200f));
            var readyButton = ready.gameObject.AddComponent<LobbyReadyButton>();
            serialized = new SerializedObject(readyButton);
            serialized.FindProperty("_button").objectReferenceValue = ready;
            serialized.FindProperty("_label").objectReferenceValue = ready.GetComponentInChildren<Text>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateFlowButton(ui.transform, ButtonNames.Leave, "Выйти", GameState.MainMenu, new Vector2(280f, 200f));
            AddRoleVisibility(ui, new[] { start.gameObject }, new[] { ready.gameObject });

            CreateScope<LobbyLifetimeScope>(ui);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetupMatchScene(ParticleSystem hitBurst)
        {
            Scene scene = OpenOrCreateScene(SceneNames.Match);

            Camera camera = FindMainCamera(scene);
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(s_cameraOffset, Quaternion.Euler(48f, 0f, 0f));
                if (camera.GetComponent<CinemachineBrain>() == null)
                {
                    camera.gameObject.AddComponent<CinemachineBrain>();
                }
            }

            GameObject match = CreateMatchRig(hitBurst);

            GameObject ui = CreateUiRoot();
            Button button = CreateButton(ui.transform, ButtonNames.EndMatch, "Завершить матч");
            SetRect((RectTransform)button.transform, new Vector2(1f, 1f), new Vector2(360f, 90f), new Vector2(-200f, -65f));
            var endButton = button.gameObject.AddComponent<MatchEndButton>();
            var serialized = new SerializedObject(endButton);
            serialized.FindProperty("_button").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AddRoleVisibility(ui, new[] { button.gameObject }, new GameObject[0]);

            Text hud = CreateText(ui.transform, "Hud", string.Empty, 30, TextAnchor.UpperLeft);
            SetRect(hud.rectTransform, new Vector2(0f, 1f), new Vector2(900f, 400f), new Vector2(480f, -230f));
            var hudView = hud.gameObject.AddComponent<MatchHudView>();
            serialized = new SerializedObject(hudView);
            serialized.FindProperty("_label").objectReferenceValue = hud;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateScope<MatchLifetimeScope>(ui, match);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// Общая камера (Target Group + кадрирование группы + приём тряски) и «сочность» попаданий.
        /// </summary>
        private static GameObject CreateMatchRig(ParticleSystem hitBurst)
        {
            var root = new GameObject(MatchObjectName);

            var anchor = new GameObject("TableAnchor");
            anchor.transform.SetParent(root.transform, false);

            var groupObject = new GameObject("TargetGroup");
            groupObject.transform.SetParent(root.transform, false);
            var group = groupObject.AddComponent<CinemachineTargetGroup>();
            // Центр стола в группе: камера не наезжает вплотную, даже если все игроки сбились в кучу.
            group.Targets.Add(new CinemachineTargetGroup.Target { Object = anchor.transform, Weight = 0.6f, Radius = 4f });

            var cameraObject = new GameObject("MatchCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.SetPositionAndRotation(s_cameraOffset, Quaternion.Euler(48f, 0f, 0f));
            var virtualCamera = cameraObject.AddComponent<CinemachineCamera>();
            virtualCamera.Target.TrackingTarget = groupObject.transform;
            LensSettings lens = virtualCamera.Lens;
            lens.FieldOfView = 50f;
            virtualCamera.Lens = lens;

            var follow = cameraObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = s_cameraOffset;
            TrackerSettings tracker = follow.TrackerSettings;
            tracker.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings = tracker;
            cameraObject.AddComponent<CinemachineRotationComposer>();

            var framing = cameraObject.AddComponent<CinemachineGroupFraming>();
            framing.FramingSize = 0.7f;
            framing.SizeAdjustment = CinemachineGroupFraming.SizeAdjustmentModes.DollyOnly;
            framing.DollyRange = new Vector2(-6f, 4f);
            framing.Damping = 1.5f;
            cameraObject.AddComponent<CinemachineImpulseListener>();

            var matchCamera = root.AddComponent<MatchCamera>();
            var serialized = new SerializedObject(matchCamera);
            serialized.FindProperty("_group").objectReferenceValue = group;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var feedbackObject = new GameObject("CombatFeedback");
            feedbackObject.transform.SetParent(root.transform, false);
            var impulse = feedbackObject.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseDuration = 0.18f;
            impulse.DefaultVelocity = new Vector3(0.35f, -1f, 0f);
            var feedback = feedbackObject.AddComponent<CombatFeedback>();
            serialized = new SerializedObject(feedback);
            serialized.FindProperty("_hitBurstPrefab").objectReferenceValue = hitBurst;
            serialized.FindProperty("_impulse").objectReferenceValue = impulse;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static void SetupResultsScene()
        {
            Scene scene = OpenOrCreateScene(SceneNames.Results);
            GameObject ui = CreateUiRoot();
            CreateTitle(ui.transform, "Итоги");
            Button toLobby = CreateFlowButton(ui.transform, ButtonNames.ToLobby, "В лобби", GameState.Lobby, new Vector2(-280f, 200f));
            CreateFlowButton(ui.transform, ButtonNames.Leave, "В меню", GameState.MainMenu, new Vector2(280f, 200f));
            AddRoleVisibility(ui, new[] { toLobby.gameObject }, new GameObject[0]);
            CreateScope<ResultsLifetimeScope>(ui);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetupBuildSettings()
        {
            var scenes = new EditorBuildSettingsScene[s_buildScenes.Length];
            for (int i = 0; i < s_buildScenes.Length; i++)
            {
                scenes[i] = new EditorBuildSettingsScene(ScenePath(s_buildScenes[i]), true);
            }

            EditorBuildSettings.scenes = scenes;
        }

        private static Scene OpenOrCreateScene(string sceneName)
        {
            string path = ScenePath(sceneName);
            Scene scene;
            if (File.Exists(path))
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, path);
            }

            // Сгенерированное пересоздаём; объекты, расставленные руками, не трогаем.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == ScopeObjectName || root.name == UiObjectName || root.name == MatchObjectName)
                {
                    Object.DestroyImmediate(root);
                }
            }

            return scene;
        }

        private static string ScenePath(string sceneName)
        {
            return ScenesFolder + "/" + sceneName + ".unity";
        }

        private static Camera FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var camera = root.GetComponentInChildren<Camera>();
                if (camera != null)
                {
                    return camera;
                }
            }

            return null;
        }

        private static void CreateScope<T>(params GameObject[] autoInjectTargets) where T : LifetimeScope
        {
            var scopeObject = new GameObject(ScopeObjectName);
            var scope = scopeObject.AddComponent<T>();
            if (autoInjectTargets == null || autoInjectTargets.Length == 0 || autoInjectTargets[0] == null)
            {
                return;
            }

            var serialized = new SerializedObject(scope);
            SerializedProperty list = serialized.FindProperty("autoInjectGameObjects");
            list.arraySize = autoInjectTargets.Length;
            for (int i = 0; i < autoInjectTargets.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = autoInjectTargets[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- UI ----------

        private static GameObject CreateUiRoot()
        {
            return CreateCanvas(UiObjectName, sortingOrder: 0);
        }

        private static GameObject CreateCanvas(string name, int sortingOrder)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvasObject;
        }

        private static void CreateTitle(Transform parent, string title)
        {
            Text text = CreateText(parent, "Title", title, 96, TextAnchor.MiddleCenter);
            SetRect(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(1400f, 160f), new Vector2(0f, -180f));
        }

        private static Button CreateFlowButton(Transform parent, string name, string label, GameState target, Vector2 position)
        {
            Button button = CreateButton(parent, name, label);
            SetRect((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(480f, 110f), position);

            var flowButton = button.gameObject.AddComponent<GameFlowButton>();
            var serialized = new SerializedObject(flowButton);
            serialized.FindProperty("_button").objectReferenceValue = button;
            serialized.FindProperty("_target").intValue = (int)target;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        /// <summary>Кнопки хоста и клиента переключает всегда активный корень UI (см. SessionRoleVisibility).</summary>
        private static void AddRoleVisibility(GameObject ui, GameObject[] hostOnly, GameObject[] clientOnly)
        {
            var visibility = ui.AddComponent<SessionRoleVisibility>();
            var serialized = new SerializedObject(visibility);
            SetObjectArray(serialized.FindProperty("_hostOnly"), hostOnly);
            SetObjectArray(serialized.FindProperty("_clientOnly"), clientOnly);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetComponentArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void SetObjectArray(SerializedProperty property, GameObject[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static InputField CreateInputField(Transform parent, string name, string value, string placeholder)
        {
            var fieldObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldObject.transform.SetParent(parent, false);

            var image = fieldObject.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            image.type = Image.Type.Sliced;

            Text text = CreateText(fieldObject.transform, "Text", string.Empty, 40, TextAnchor.MiddleLeft);
            text.color = new Color(0.15f, 0.15f, 0.15f);
            text.supportRichText = false;
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(16f, 6f);
            text.rectTransform.offsetMax = new Vector2(-16f, -6f);

            Text hint = CreateText(fieldObject.transform, "Placeholder", placeholder, 40, TextAnchor.MiddleLeft);
            hint.color = new Color(0.15f, 0.15f, 0.15f, 0.5f);
            hint.fontStyle = FontStyle.Italic;
            Stretch(hint.rectTransform);
            hint.rectTransform.offsetMin = text.rectTransform.offsetMin;
            hint.rectTransform.offsetMax = text.rectTransform.offsetMax;

            var field = fieldObject.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 64;
            field.text = value;
            return field;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;

            Text text = CreateText(buttonObject.transform, "Label", label, 44, TextAnchor.MiddleCenter);
            text.color = new Color(0.15f, 0.15f, 0.15f);
            Stretch(text.rectTransform);

            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
