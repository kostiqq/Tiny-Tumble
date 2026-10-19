using System;
using System.Collections.Generic;
using System.IO;
using TinyTumble.Bootstrap.Scopes;
using TinyTumble.Core.Assets;
using TinyTumble.Core.Configs;
using TinyTumble.Core.Flow;
using TinyTumble.Gameplay.Characters;
using TinyTumble.Infrastructure.Debugging;
using TinyTumble.UI.Flow;
using TinyTumble.UI.Loading;
using TinyTumble.UI.Lobby;
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
    /// Собирает каркас этапа 1: конфиги, корневой префаб, VContainerSettings, группы Addressables,
    /// тестовую арену, заглушку персонажа, сцены и Build Settings. Идемпотентен: повторный запуск
    /// пересоздаёт сгенерированные объекты и не трогает остальное.
    /// Запуск: меню Tiny Tumble → Setup → Stage 1, или batchmode
    /// -executeMethod TinyTumble.Bootstrap.Editor.ProjectSetup.RunBatch
    /// </summary>
    public static class ProjectSetup
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ConfigsFolder = "Assets/Configs";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string SettingsFolder = "Assets/Settings";
        private const string MaterialsFolder = "Assets/Art/Materials";
        private const string ArenasFolder = "Assets/AddressableAssets/Arenas";
        private const string CharactersFolder = "Assets/AddressableAssets/Characters";

        private const string RootPrefabPath = PrefabsFolder + "/RootLifetimeScope.prefab";
        private const string LegacyBootstrapPrefabPath = PrefabsFolder + "/Bootstrap.prefab";
        private const string VContainerSettingsPath = SettingsFolder + "/VContainerSettings.asset";
        private const string ArenaScenePath = ArenasFolder + "/" + AddressKeys.TestArena + ".unity";
        private const string CharacterPrefabPath = CharactersFolder + "/" + AddressKeys.StubCharacter + ".prefab";
        private const string ArenaMaterialPath = MaterialsFolder + "/M_ArenaStub.mat";
        private const string CharacterMaterialPath = MaterialsFolder + "/M_CharacterStub.mat";

        private const string ScopeObjectName = "[Scope]";
        private const string UiObjectName = "[UI]";

        private static readonly string[] s_addressableGroups = { "Core", "Arenas", "Characters", "Cosmetics", "Bosses", "Audio" };

        private static readonly string[] s_buildScenes =
        {
            SceneNames.Boot, SceneNames.MainMenu, SceneNames.Lobby, SceneNames.Match, SceneNames.Results,
        };

        private static readonly Color s_tableColor = new Color(0.72f, 0.53f, 0.34f);
        private static readonly Color s_loadingBackground = new Color(0.08f, 0.07f, 0.10f, 1f);

        [MenuItem("Tiny Tumble/Setup/Stage 1")]
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
            EnsureFolder(ArenasFolder);
            EnsureFolder(CharactersFolder);

            var matchConfig = EnsureAsset<MatchConfig>(ConfigsFolder + "/MatchConfig.asset");
            var characterConfig = EnsureAsset<CharacterConfig>(ConfigsFolder + "/CharacterConfig.asset");
            var combatConfig = EnsureAsset<CombatConfig>(ConfigsFolder + "/CombatConfig.asset");
            var botConfig = EnsureAsset<BotConfig>(ConfigsFolder + "/BotConfig.asset");

            Material arenaMaterial = EnsureLitMaterial(ArenaMaterialPath, s_tableColor);
            Material characterMaterial = EnsureLitMaterial(CharacterMaterialPath, Color.white);

            RootLifetimeScope rootPrefab = CreateRootPrefab(matchConfig, characterConfig, combatConfig, botConfig);
            SetupVContainerSettings(rootPrefab);

            CreateCharacterPrefab(characterMaterial);
            CreateArenaScene(arenaMaterial);
            SetupAddressables();

            SetupBootScene();
            SetupMainMenuScene();
            SetupLobbyScene();
            SetupMatchScene();
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

            Debug.Log("[Setup] Stage 1 setup complete.");
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

        // ---------- Корень ----------

        private static RootLifetimeScope CreateRootPrefab(
            MatchConfig matchConfig,
            CharacterConfig characterConfig,
            CombatConfig combatConfig,
            BotConfig botConfig)
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

        private static void CreateCharacterPrefab(Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = AddressKeys.StubCharacter;
            try
            {
                var renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                var body = cube.AddComponent<Rigidbody>();
                body.mass = 1f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.constraints = RigidbodyConstraints.FreezeRotation;

                var character = cube.AddComponent<StubCharacter>();
                var serialized = new SerializedObject(character);
                serialized.FindProperty("_rigidbody").objectReferenceValue = body;
                serialized.FindProperty("_renderer").objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(cube, CharacterPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(cube);
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

            AddEntry(settings, groups["Arenas"], ArenaScenePath, AddressKeys.TestArena, AddressKeys.ArenaLabel);
            AddEntry(settings, groups["Characters"], CharacterPrefabPath, AddressKeys.StubCharacter, AddressKeys.CharacterLabel);

            EditorUtility.SetDirty(settings);
        }

        private static void AddEntry(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address, string label)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException($"Asset not found: {path}");
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, true);
            entry.address = address;
            entry.SetLabel(label, true, true, true);
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
            CreateFlowButton(ui.transform, "Играть", GameState.Lobby);
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

            CreateFlowButton(ui.transform, "Начать матч", GameState.Match);
            CreateScope<LobbyLifetimeScope>(ui);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetupMatchScene()
        {
            Scene scene = OpenOrCreateScene(SceneNames.Match);

            Camera camera = FindMainCamera(scene);
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 11f, -10f), Quaternion.Euler(48f, 0f, 0f));
            }

            GameObject ui = CreateUiRoot();
            Button button = CreateButton(ui.transform, "Завершить матч");
            SetRect((RectTransform)button.transform, new Vector2(1f, 1f), new Vector2(360f, 90f), new Vector2(-200f, -65f));
            var endButton = button.gameObject.AddComponent<MatchEndButton>();
            var serialized = new SerializedObject(endButton);
            serialized.FindProperty("_button").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateScope<MatchLifetimeScope>(ui);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetupResultsScene()
        {
            Scene scene = OpenOrCreateScene(SceneNames.Results);
            GameObject ui = CreateUiRoot();
            CreateTitle(ui.transform, "Итоги");
            CreateFlowButton(ui.transform, "В лобби", GameState.Lobby);
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
                if (root.name == ScopeObjectName || root.name == UiObjectName)
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

        private static void CreateScope<T>(GameObject autoInjectTarget) where T : LifetimeScope
        {
            var scopeObject = new GameObject(ScopeObjectName);
            var scope = scopeObject.AddComponent<T>();
            if (autoInjectTarget == null)
            {
                return;
            }

            var serialized = new SerializedObject(scope);
            SerializedProperty list = serialized.FindProperty("autoInjectGameObjects");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = autoInjectTarget;
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

        private static void CreateFlowButton(Transform parent, string label, GameState target)
        {
            Button button = CreateButton(parent, label);
            SetRect((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(480f, 110f), new Vector2(0f, 200f));

            var flowButton = button.gameObject.AddComponent<GameFlowButton>();
            var serialized = new SerializedObject(flowButton);
            serialized.FindProperty("_button").objectReferenceValue = button;
            serialized.FindProperty("_target").intValue = (int)target;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
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
