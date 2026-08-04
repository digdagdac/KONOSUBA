using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Overbless.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Authors the deterministic M1 validation slice from runtime-owned components and data.
    /// It deliberately contains no gameplay logic; runtime scripts remain the only gameplay owner.
    /// </summary>
    public static class M1ContentBootstrap
    {
        public const int Seed = M1RoomDefinition.RequiredSeed;
        public const string ScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";

        private const string ProjectRoot = "Assets/_Project";
        private const string ArtRoot = ProjectRoot + "/Art/M1Representative";
        private const string ProductionArtRoot = ProjectRoot + "/Art/M1Production";
        private const string ProductionCharactersRoot = ProductionArtRoot + "/Characters";
        private const string ProductionPickupsRoot = ProductionArtRoot + "/Pickups";
        private const string ProductionEnvironmentRoot = ProductionArtRoot + "/Environment";
        private const string ProductionUiRoot = ProductionArtRoot + "/UI";
        private const string PlayerProductionSpritePath = ProductionCharactersRoot + "/chr_player_idle_south_a_v001.png";
        private const string DasherProductionSpritePath = ProductionCharactersRoot + "/chr_dasher_idle_south_a_v001.png";
        private const string ArcherProductionSpritePath = ProductionCharactersRoot + "/chr_archer_idle_south_a_v001.png";
        private const string MinionProductionSpritePath = ProductionCharactersRoot + "/chr_minion_idle_south_a_v001.png";
        private const string SoulProductionSpritePath = ProductionPickupsRoot + "/ui_icon_soul_pickup_a_v001.png";
        private const string ExitProductionSpritePath = ProductionEnvironmentRoot + "/env_exit_closed_south_a_v001.png";
        private const string TileProductionSpritePath = ProductionEnvironmentRoot + "/env_dungeon_floor_tile_a_v001.png";
        private const string HasteProductionSpritePath = ProductionUiRoot + "/ui_icon_bless_haste_a_v001.png";
        private const string GiantProductionSpritePath = ProductionUiRoot + "/ui_icon_bless_giant_a_v001.png";
        private const string UiLineMaterialPath = ProductionUiRoot + "/mat_m1_ui_line_unlit_v001.mat";
        private const string M2ProductionArtRoot = ProjectRoot + "/Art/M2Production";
        private const string M2ProductionUiRoot = M2ProductionArtRoot + "/UI";
        private const string M2ProductionVfxRoot = M2ProductionArtRoot + "/VFX";
        private const string M2ProductionEnvironmentRoot = M2ProductionArtRoot + "/Environment";
        private const string EchoProductionSpritePath = M2ProductionUiRoot + "/ui_icon_bless_echo_a_v002.png";
        private const string EchoStatusProductionSpritePath = M2ProductionUiRoot + "/ui_icon_echo_status_a_v002.png";
        private const string EchoLineProductionSpritePath = M2ProductionVfxRoot + "/vfx_echo_line_telegraph_a_v002.png";
        private const string EchoDoubleProductionSpritePath = M2ProductionVfxRoot + "/vfx_echo_double_silhouette_a_v002.png";
        private const string WorldPillarProductionSpritePath = M2ProductionEnvironmentRoot + "/env_static_world_pillar_south_a_v002.png";
        private const string DataRoot = ProjectRoot + "/Data";
        private const string PrefabRoot = ProjectRoot + "/Prefabs/M1";
        private const string BlessingDataRoot = DataRoot + "/Blessings";
        private const string EnemyDataRoot = DataRoot + "/Enemies";
        private const string RoomDataRoot = DataRoot + "/Rooms";
        private const string AudioDataRoot = DataRoot + "/Audio";
        private const string M2PrefabRoot = ProjectRoot + "/Prefabs/M2";
        private const string Room02DataPath = RoomDataRoot + "/Room_02.asset";
        private const string Room03DataPath = RoomDataRoot + "/Room_03.asset";
        private const string Room02ScenePath = ProjectRoot + "/Scenes/Room_02.unity";
        private const string Room03ScenePath = ProjectRoot + "/Scenes/Room_03.unity";

        /// <summary>Flow screens that turn the three rooms into one continuous run.</summary>
        public const string TitleScenePath = ProjectRoot + "/Scenes/Title.unity";

        public const string ResultScenePath = ProjectRoot + "/Scenes/Result.unity";
        public const string TitleSceneName = "Title";
        public const string ResultSceneName = "Result";
        public const string FirstRoomSceneName = "M1_GuidedValidation";
        public const string SecondRoomSceneName = "Room_02";
        public const string ThirdRoomSceneName = "Room_03";
        public const string TitleKeyVisualPath = ProductionUiRoot + "/ui_title_key_visual_a_v001.png";

        /// <summary>
        /// Set to true once the delivered title key visual already contains the Korean
        /// logotype, so the engine headline does not print a second title over the art.
        /// The engine font has no Hangul glyphs, which is why the logotype belongs to the art.
        /// </summary>
        public const bool HideEngineTitleWhenLogotypeBaked = false;
        private const string AudioRoot = ProjectRoot + "/Audio/M1Functional";
        private const string SettingsRoot = "Assets/Settings";
        private const string RenderingSettingsRoot = SettingsRoot + "/Rendering";
        private const string InputSettingsRoot = SettingsRoot + "/Input";
        private const string Renderer2DDataPath = RenderingSettingsRoot + "/Renderer2DData.asset";
        private const string UniversalRenderPipelineAssetPath = RenderingSettingsRoot + "/UniversalRenderPipelineAsset.asset";
        private const string InputActionsPath = InputSettingsRoot + "/Overbless.inputactions";

        private const int PlayerLayer = 8;
        private const int EnemyLayer = 9;
        private const int WorldLayer = 12;
        private const int PickupLayer = 13;
        private const int ExitLayer = 14;
        private const float CameraOrthographicSize = 5.0625f;
        private const float SpritePixelsPerUnit = 128f;
        private static readonly Vector2 CharacterSpritePivot = new Vector2(0.5f, 0f);
        private static readonly Vector2 CenteredSpritePivot = new Vector2(0.5f, 0.5f);


        private static readonly string[] RequiredDirectories =
        {
            ProjectRoot,
            ProjectRoot + "/Art",
            ArtRoot,
            ProductionArtRoot,
            ProductionCharactersRoot,
            ProductionPickupsRoot,
            ProductionEnvironmentRoot,
            ProductionUiRoot,
            DataRoot,
            BlessingDataRoot,
            EnemyDataRoot,
            RoomDataRoot,
            AudioDataRoot,
            PrefabRoot,
            ProjectRoot + "/Scenes",
            SettingsRoot,
            RenderingSettingsRoot,
            InputSettingsRoot
        };

        private static readonly FunctionalAudioEvent[] AudioEvents =
        {
            FunctionalAudioEvent.DasherReady,
            FunctionalAudioEvent.ArcherReady,
            FunctionalAudioEvent.AttackLocked,
            FunctionalAudioEvent.PlayerHit,
            FunctionalAudioEvent.SoulCollected,
            FunctionalAudioEvent.ExitOpened,
            FunctionalAudioEvent.BlessingApplied,
            FunctionalAudioEvent.BlessingRejected,
            FunctionalAudioEvent.EnemyDefeated,
            FunctionalAudioEvent.FriendlyFireKill
        };
        private static readonly InputActionDefinition[] InputActionDefinitions =
        {
            new InputActionDefinition("Move", "b836ac86-8d79-4e49-878d-104729000001", "Value", "Vector2"),
            new InputActionDefinition("MousePosition", "b836ac86-8d79-4e49-878d-104729000002", "Value", "Vector2"),
            new InputActionDefinition("Dash", "b836ac86-8d79-4e49-878d-104729000003", "Button", "Button"),
            new InputActionDefinition("FirstBlessing", "b836ac86-8d79-4e49-878d-104729000004", "Button", "Button"),
            new InputActionDefinition("SecondBlessing", "b836ac86-8d79-4e49-878d-104729000005", "Button", "Button"),
            new InputActionDefinition("Apply", "b836ac86-8d79-4e49-878d-104729000006", "Button", "Button"),
            new InputActionDefinition("Cancel", "b836ac86-8d79-4e49-878d-104729000007", "Button", "Button"),
            new InputActionDefinition("Restart", "b836ac86-8d79-4e49-878d-104729000008", "Button", "Button"),
            new InputActionDefinition("Pause", "b836ac86-8d79-4e49-878d-104729000009", "Button", "Button")
        };

        private static readonly InputBindingDefinition[] InputBindingDefinitions =
        {
            new InputBindingDefinition("2DVector", "c836ac86-8d79-4e49-878d-104729000001", "2DVector", "Move", true, false),
            new InputBindingDefinition("up", "c836ac86-8d79-4e49-878d-104729000002", "<Keyboard>/w", "Move", false, true),
            new InputBindingDefinition("down", "c836ac86-8d79-4e49-878d-104729000003", "<Keyboard>/s", "Move", false, true),
            new InputBindingDefinition("left", "c836ac86-8d79-4e49-878d-104729000004", "<Keyboard>/a", "Move", false, true),
            new InputBindingDefinition("right", "c836ac86-8d79-4e49-878d-104729000005", "<Keyboard>/d", "Move", false, true),
            new InputBindingDefinition("2DVector", "c836ac86-8d79-4e49-878d-104729000006", "2DVector", "Move", true, false),
            new InputBindingDefinition("up", "c836ac86-8d79-4e49-878d-104729000007", "<Keyboard>/upArrow", "Move", false, true),
            new InputBindingDefinition("down", "c836ac86-8d79-4e49-878d-104729000008", "<Keyboard>/downArrow", "Move", false, true),
            new InputBindingDefinition("left", "c836ac86-8d79-4e49-878d-104729000009", "<Keyboard>/leftArrow", "Move", false, true),
            new InputBindingDefinition("right", "c836ac86-8d79-4e49-878d-10472900000a", "<Keyboard>/rightArrow", "Move", false, true),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-10472900000b", "<Mouse>/position", "MousePosition", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-10472900000c", "<Keyboard>/space", "Dash", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-10472900000d", "<Keyboard>/1", "FirstBlessing", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-10472900000e", "<Keyboard>/2", "SecondBlessing", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-10472900000f", "<Mouse>/leftButton", "Apply", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-104729000010", "<Mouse>/rightButton", "Cancel", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-104729000011", "<Keyboard>/escape", "Pause", false, false),
            new InputBindingDefinition("", "c836ac86-8d79-4e49-878d-104729000012", "<Keyboard>/r", "Restart", false, false)
        };

        [MenuItem("Overbless/M1/Create Or Update Guided Validation Content")]
        public static void CreateOrUpdate()
        {
            GuardOpenScenes();
            EnsureDirectories();
            ConfigureUniversalRenderPipeline();
            CreateInputActionsAsset();
            var sprites = CreateM1Sprites();
            var animations = M1DirectionalAnimationBootstrap.CreateOrUpdate();
            var playerConfig = CreatePlayerConfig();
            var enemyDefinitions = CreateEnemyDefinitions();
            var roomDefinition = CreateRoomDefinition();
            var audioCatalog = CreateAudioCatalog();
            var prefabs = CreateM1Prefabs(sprites, animations, playerConfig, enemyDefinitions);

            CreateScene(sprites, roomDefinition, audioCatalog, prefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        [MenuItem("Overbless/M2/Create Or Update Two-Room Content")]
        public static void CreateOrUpdateM2()
        {
            GuardOpenScenes();
            EnsureM2Directories();

            var sprites = CreateM2Sprites();
            var animations = M1DirectionalAnimationBootstrap.CreateOrUpdate();
            var playerConfig = CreatePlayerConfig();
            var enemyDefinitions = CreateEnemyDefinitions();
            var room02 = CreateRoomDefinition(Room02DataPath, M1RoomVariant.Room02);
            var room03 = CreateRoomDefinition(Room03DataPath, M1RoomVariant.Room03);
            var audioCatalog = CreateAudioCatalog();
            var identityCatalog = M2CharacterIdentityBootstrap.CreateOrUpdateCatalog();
            var prefabs = CreateM2Prefabs(sprites, animations, playerConfig, enemyDefinitions);
            var pillar = SavePrefab(
                M2PrefabRoot + "/WorldPillar.prefab",
                () => CreateWorldPillarPrefab(sprites.WorldPillar));

            AssetDatabase.SaveAssets();


            room02 = RequireAsset<M1RoomDefinition>(Room02DataPath);
            audioCatalog = RequireAsset<FunctionalAudioCatalog>(AudioDataRoot + "/FunctionalAudioCatalog.asset");
            identityCatalog = RequireAsset<CharacterIdentityCatalog>(M2CharacterIdentityBootstrap.CatalogPath);
            prefabs = LoadPrefabSet(M2PrefabRoot);
            room02.Validate();
            CreateM2Scene(
                Room02ScenePath,
                "Room_02",
                "ROOM  02",
                room02,
                audioCatalog,
                identityCatalog,
                prefabs,
                sprites,
                "Room_03",
                null);

            room03 = RequireAsset<M1RoomDefinition>(Room03DataPath);
            audioCatalog = RequireAsset<FunctionalAudioCatalog>(AudioDataRoot + "/FunctionalAudioCatalog.asset");
            identityCatalog = RequireAsset<CharacterIdentityCatalog>(M2CharacterIdentityBootstrap.CatalogPath);
            prefabs = LoadPrefabSet(M2PrefabRoot);
            pillar = RequireAsset<GameObject>(M2PrefabRoot + "/WorldPillar.prefab");
            room03.Validate();
            CreateM2Scene(
                Room03ScenePath,
                "Room_03",
                "ROOM  03",
                room03,
                audioCatalog,
                identityCatalog,
                prefabs,
                sprites,
                ResultSceneName,
                pillar);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Intended for -executeMethod in Unity batch mode.
        public static void CreateM2ForBatchMode()
        {
            CreateOrUpdateM2();
        }

        /// <summary>
        /// Builds the two flow screens that turn the rooms into one run: a title screen that
        /// starts the first room and a result screen that closes the run and returns to the
        /// title. Both are generated content, like every other scene in this project.
        /// </summary>
        [MenuItem("Overbless/Contest/Create Title And Result Screens")]
        public static void CreateOrUpdateFlowScreens()
        {
            GuardOpenScenes();
            EnsureDirectories();
            var sprites = CreateM1Sprites();
            var keyVisual = AssetDatabase.LoadAssetAtPath<Sprite>(TitleKeyVisualPath);

            CreateFlowScreen(
                TitleScenePath,
                TitleSceneName,
                keyVisual,
                sprites.Player,
                "OVERBLESS",
                "THE SAINT WHO CANNOT ATTACK",
                new[]
                {
                    "YOU CANNOT DAMAGE ANYTHING. YOU CAN ONLY MAKE ENEMIES STRONGER.",
                    "BLESS AN ENEMY, STAND WHERE ITS ATTACK WILL CROSS ANOTHER ENEMY, THEN LEAVE.",
                    "COLLECT 3 SOULS FROM THEIR MISTAKES AND REACH THE EXIT. THREE ROOMS.",
                    "WASD MOVE    SPACE DASH    1 / 2 / 3 BLESS    LMB APPLY    RMB CANCEL    R RESTART    ESC PAUSE"
                },
                "CLICK OR PRESS ANY KEY TO START",
                FirstRoomSceneName);

            CreateFlowScreen(
                ResultScenePath,
                ResultSceneName,
                keyVisual,
                sprites.Player,
                "RUN COMPLETE",
                "THREE ROOMS SURVIVED BY OVERBLESSING THEM",
                new[]
                {
                    "EVERY SOUL YOU CARRIED OUT WAS PAID FOR BY AN ENEMY YOU MADE STRONGER.",
                    "VERA CHARGED IN A STRAIGHT LINE. LUME KEPT HER LANE. MOKO COPIED WHOEVER STOOD CLOSEST.",
                    "NONE OF THEM WERE WEAKENED. THEY WERE AIMED."
                },
                "CLICK OR PRESS ANY KEY TO RETURN TO THE TITLE",
                TitleSceneName);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Intended for -executeMethod in Unity batch mode.
        public static void CreateFlowScreensForBatchMode()
        {
            CreateOrUpdateFlowScreens();
        }

        /// <summary>
        /// Creates one flow screen. The key visual is optional: until it is produced the
        /// screen stands in with a dark plate and the player's authoritative combat sprite,
        /// the same representative approach the character cards use.
        /// </summary>
        private static void CreateFlowScreen(
            string scenePath,
            string sceneName,
            Sprite keyVisual,
            Sprite representativeSprite,
            string headline,
            string subtitle,
            string[] bodyLines,
            string prompt,
            string nextScene)
        {
            if (representativeSprite == null)
            {
                throw new InvalidOperationException("A flow screen requires the player sprite as its representative art.");
            }

            if (bodyLines == null || bodyLines.Length == 0)
            {
                throw new InvalidOperationException("A flow screen requires body copy.");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(sceneName);
            var camera = CreateCamera(root.transform);

            var screen = new GameObject(
                "Screen",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(TrustedInputScreen));
            screen.transform.SetParent(root.transform, false);

            var canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.pixelPerfect = true;
            canvas.sortingLayerName = "UI";
            canvas.sortingOrder = 100;

            var scaler = screen.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException("A flow screen requires Unity's LegacyRuntime font.");
            }

            var plate = CreateStretchedImage(screen.transform, "KeyVisual", new Color32(7, 11, 20, 255));
            if (keyVisual != null)
            {
                plate.sprite = keyVisual;
                plate.color = Color.white;
                plate.preserveAspect = true;
            }
            else
            {
                CreateHudIcon(
                    screen.transform,
                    "RepresentativePortrait",
                    representativeSprite,
                    new Vector2(1180f, -300f),
                    new Vector2(420f, 470f),
                    new Color32(255, 255, 255, 235));
            }

            var paleText = new Color32(236, 246, 251, 255);
            var mutedText = new Color32(158, 182, 197, 255);
            var cyan = new Color32(64, 214, 236, 255);

            var hideHeadline = keyVisual != null &&
                HideEngineTitleWhenLogotypeBaked &&
                string.Equals(sceneName, TitleSceneName, StringComparison.Ordinal);
            if (!hideHeadline)
            {
                CreateHudText(screen.transform, "Headline", headline, font, 108, TextAnchor.UpperLeft, paleText, new Vector2(96f, -140f), new Vector2(1100f, 140f));
            }

            CreateHudText(screen.transform, "Subtitle", subtitle, font, 34, TextAnchor.UpperLeft, cyan, new Vector2(100f, -280f), new Vector2(1100f, 60f));

            var bodyPanel = CreateHudPanel(
                screen.transform,
                "Body",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(96f, 168f),
                new Vector2(1728f, 210f),
                new Color32(6, 11, 20, 226));
            for (var index = 0; index < bodyLines.Length; index++)
            {
                CreateHudText(
                    bodyPanel,
                    "Line" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    bodyLines[index],
                    font,
                    index == bodyLines.Length - 1 ? 20 : 24,
                    TextAnchor.MiddleLeft,
                    index == bodyLines.Length - 1 ? mutedText : paleText,
                    new Vector2(28f, -22f - index * 46f),
                    new Vector2(1672f, 40f));
            }

            var promptText = CreateHudText(
                screen.transform,
                "Prompt",
                prompt,
                font,
                30,
                TextAnchor.MiddleCenter,
                cyan,
                new Vector2(96f, -930f),
                new Vector2(1728f, 48f));

            var trustedScreen = screen.GetComponent<TrustedInputScreen>();
            var serialized = new SerializedObject(trustedScreen);
            serialized.FindProperty("nextScene").stringValue = nextScene;
            SetObject(serialized, "promptText", promptText);
            Apply(serialized, trustedScreen);

            ValidateSceneAudioListener(scene, camera);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException($"Unity failed to save the flow screen to '{scenePath}'.");
            }
        }

        private static Image CreateStretchedImage(Transform parent, string name, Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }


        // Intended for -executeMethod in Unity batch mode.
        public static void CreateForBatchMode()
        {
            CreateOrUpdate();
        }

        private static void GuardOpenScenes()
        {
            if (!Application.isBatchMode)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    throw new OperationCanceledException("M1 content generation was cancelled to preserve unsaved scene changes.");
                }

                return;
            }

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    throw new InvalidOperationException("Batch M1 content generation refuses to replace a dirty open scene.");
                }
            }
        }
        private static void EnsureDirectories()
        {
            foreach (var directory in RequiredDirectories)
            {
                EnsureAssetDirectory(directory);
            }
        }
        private static void EnsureM2Directories()
        {
            EnsureAssetDirectory(ProjectRoot + "/Prefabs");
            EnsureAssetDirectory(M2PrefabRoot);
            EnsureAssetDirectory(RoomDataRoot);
            EnsureAssetDirectory(ProjectRoot + "/Scenes");
        }


        private static void EnsureAssetDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name) || !AssetDatabase.IsValidFolder(parent))
            {
                throw new InvalidOperationException($"Cannot create required asset directory '{assetPath}'.");
            }

            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
            {
                throw new InvalidOperationException($"Unity failed to create required asset directory '{assetPath}'.");
            }
        }
        private static void ConfigureUniversalRenderPipeline()
        {
            var rendererData = GetOrCreateAsset<Renderer2DData>(Renderer2DDataPath);
            var pipelineAsset = GetOrCreateAsset<UniversalRenderPipelineAsset>(UniversalRenderPipelineAssetPath);
            var serializedPipeline = new SerializedObject(pipelineAsset);
            var rendererDataList = RequireProperty(serializedPipeline, "m_RendererDataList");
            if (!rendererDataList.isArray)
            {
                throw new InvalidOperationException("Universal Render Pipeline renderer data must be an array.");
            }

            rendererDataList.arraySize = 1;
            var rendererDataEntry = rendererDataList.GetArrayElementAtIndex(0);
            RequireType(rendererDataEntry, SerializedPropertyType.ObjectReference, "m_RendererDataList[0]");
            rendererDataEntry.objectReferenceValue = rendererData;
            SetInt(serializedPipeline, "m_DefaultRendererIndex", 0);
            Apply(serializedPipeline, pipelineAsset);

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
        }

        private static void CreateInputActionsAsset()
        {
            var inputActionsPath = Path.GetFullPath(InputActionsPath);
            File.WriteAllText(inputActionsPath, BuildInputActionsJson(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(InputActionsPath, ImportAssetOptions.ForceUpdate);

            if (AssetDatabase.LoadMainAssetAtPath(InputActionsPath) == null)
            {
                throw new InvalidOperationException($"Unity failed to import deterministic input actions at '{InputActionsPath}'.");
            }
        }

        private static string BuildInputActionsJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"name\":\"Overbless\",\"maps\":[{\"name\":\"Player\",\"id\":\"a836ac86-8d79-4e49-878d-104729000001\",\"actions\":[");

            for (var index = 0; index < InputActionDefinitions.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var action = InputActionDefinitions[index];
                builder.Append("{\"name\":");
                AppendInputJsonString(builder, action.Name);
                builder.Append(",\"type\":");
                AppendInputJsonString(builder, action.Type);
                builder.Append(",\"id\":");
                AppendInputJsonString(builder, action.Id);
                builder.Append(",\"expectedControlType\":");
                AppendInputJsonString(builder, action.ExpectedControlType);
                builder.Append(",\"processors\":\"\",\"interactions\":\"\",\"initialStateCheck\":false}");
            }

            builder.Append("],\"bindings\":[");
            for (var index = 0; index < InputBindingDefinitions.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                var binding = InputBindingDefinitions[index];
                builder.Append("{\"name\":");
                AppendInputJsonString(builder, binding.Name);
                builder.Append(",\"id\":");
                AppendInputJsonString(builder, binding.Id);
                builder.Append(",\"path\":");
                AppendInputJsonString(builder, binding.Path);
                builder.Append(",\"interactions\":\"\",\"processors\":\"\",\"groups\":\"\",\"action\":");
                AppendInputJsonString(builder, binding.Action);
                builder.Append(",\"isComposite\":");
                builder.Append(binding.IsComposite ? "true" : "false");
                builder.Append(",\"isPartOfComposite\":");
                builder.Append(binding.IsPartOfComposite ? "true" : "false");
                builder.Append('}');
            }

            builder.Append("]}],\"controlSchemes\":[]}");
            return builder.ToString();
        }

        private static void AppendInputJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var character in value)
            {
                if (character == '"' || character == '\\')
                {
                    builder.Append('\\');
                }

                builder.Append(character);
            }

            builder.Append('"');
        }

        private static M1SpriteSet CreateM1Sprites()
        {
            return new M1SpriteSet(
                LoadRequiredProductionSprite(PlayerProductionSpritePath, CharacterSpritePivot),
                LoadRequiredProductionSprite(DasherProductionSpritePath, CharacterSpritePivot),
                LoadRequiredProductionSprite(ArcherProductionSpritePath, CharacterSpritePivot),
                LoadRequiredProductionSprite(MinionProductionSpritePath, CharacterSpritePivot),
                LoadRequiredProductionSprite(SoulProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(ExitProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(TileProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(HasteProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(GiantProductionSpritePath, CenteredSpritePivot));
        }

        private static M2SpriteSet CreateM2Sprites()
        {
            return new M2SpriteSet(
                CreateM1Sprites(),
                LoadRequiredProductionSprite(EchoProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(EchoStatusProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(EchoLineProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(EchoDoubleProductionSpritePath, CenteredSpritePivot),
                LoadRequiredProductionSprite(WorldPillarProductionSpritePath, CharacterSpritePivot));
        }

        private static Sprite LoadRequiredProductionSprite(string assetPath, Vector2 pivot)
        {
            if (!File.Exists(Path.GetFullPath(assetPath)))
            {
                throw new FileNotFoundException("Required production sprite is missing.", assetPath);
            }

            return ImportExistingSprite(assetPath, pivot);
        }

        private static Sprite ImportExistingSprite(string assetPath, Vector2 pivot)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"'{assetPath}' did not import as a texture.");
            }

            ConfigureSpriteImporter(importer, pivot);
            importer.SaveAndReimport();
            return LoadSprite(assetPath);
        }

        private static void ConfigureSpriteImporter(TextureImporter importer, Vector2 pivot)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = SpritePixelsPerUnit;
            var importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            importerSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            importerSettings.spritePivot = pivot;
            importer.SetTextureSettings(importerSettings);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
        }

        private static Sprite LoadSprite(string assetPath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                var importedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (var index = 0; index < importedAssets.Length; index++)
                {
                    if (importedAssets[index] is Sprite importedSprite)
                    {
                        sprite = importedSprite;
                        break;
                    }
                }
            }

            if (sprite == null)
            {
                throw new InvalidOperationException($"Unity did not create a sprite from '{assetPath}'.");
            }

            return sprite;
        }

        private static PlayerConfig CreatePlayerConfig()
        {
            var config = GetOrCreateAsset<PlayerConfig>(DataRoot + "/PlayerConfig.asset");
            var serialized = new SerializedObject(config);
            SetFloat(serialized, "movementSpeed", 5f);
            SetFloat(serialized, "dashDistance", 3.5f);
            SetFloat(serialized, "dashDuration", 0.18f);
            SetFloat(serialized, "dashInvulnerabilityDuration", 0.22f);
            SetFloat(serialized, "dashCooldown", 1.2f);
            Apply(serialized, config);
            return config;
        }

        private static EnemyDefinitions CreateEnemyDefinitions()
        {
            var dasher = GetOrCreateAsset<EnemyDefinition>(EnemyDataRoot + "/Enemy_Dasher.asset");
            ConfigureEnemyDefinition(dasher, 12, 1f, 1.5f, 3f, 1, 8f, 8f, 0.7f, 0.75f, 0.4f, 10f, 8f, 4f);

            var archer = GetOrCreateAsset<EnemyDefinition>(EnemyDataRoot + "/Enemy_Archer.asset");
            ConfigureEnemyDefinition(archer, 8, 0.5f, 0.75f, 3.5f, 1, 10f, 10f, 0.35f, 0.7f, 0.35f, 8f, 9f, 4f);

            var minion = GetOrCreateAsset<EnemyDefinition>(EnemyDataRoot + "/Enemy_Minion.asset");
            ConfigureEnemyDefinition(minion, 5, 0.8333333f, 1.25f, 4f, 1, 8f, 1f, 0.5f, 0.75f, 0.35f, 6f, 6f, 0f);
            return new EnemyDefinitions(dasher, archer, minion);
        }

        private static void ConfigureEnemyDefinition(
            EnemyDefinition definition,
            int maximumHealth,
            float walkSpeed,
            float runSpeed,
            float attackCooldown,
            int attackDamage,
            float engagementRange,
            float attackRange,
            float attackWidth,
            float warningDuration,
            float recoveryDuration,
            float chargeSpeed,
            float projectileSpeed,
            float preferredDistance)
        {
            var serialized = new SerializedObject(definition);
            SetInt(serialized, "maximumHealth", maximumHealth);
            SetFloat(serialized, "walkSpeed", walkSpeed);
            SetFloat(serialized, "runSpeed", runSpeed);
            SetFloat(serialized, "attackCooldown", attackCooldown);
            SetInt(serialized, "attackDamage", attackDamage);
            SetFloat(serialized, "engagementRange", engagementRange);
            SetFloat(serialized, "attackRange", attackRange);
            SetFloat(serialized, "attackWidth", attackWidth);
            SetFloat(serialized, "warningDuration", warningDuration);
            SetFloat(serialized, "recoveryDuration", recoveryDuration);
            SetFloat(serialized, "chargeSpeed", chargeSpeed);
            SetFloat(serialized, "projectileSpeed", projectileSpeed);
            SetFloat(serialized, "preferredDistance", preferredDistance);
            SetInt(serialized, "damageTargetMask", (1 << PlayerLayer) | (1 << EnemyLayer));
            SetInt(serialized, "worldCollisionMask", 1 << WorldLayer);
            Apply(serialized, definition);
        }

        private static M1RoomDefinition CreateRoomDefinition()
        {
            return CreateRoomDefinition(
                RoomDataRoot + "/Room_M1_GuidedValidation.asset",
                M1RoomVariant.M1GuidedValidation);
        }

        private static M1RoomDefinition CreateRoomDefinition(string assetPath, M1RoomVariant roomVariant)
        {
            var room = GetOrCreateAsset<M1RoomDefinition>(assetPath);
            var serialized = new SerializedObject(room);
            SetInt(serialized, "seed", Seed);
            SetFloat(serialized, "fixedTimeStep", M1RoomDefinition.RequiredFixedTimeStep);
            SetRect(serialized, "bounds", new Rect(-8f, -4.5f, 16f, 9f));
            SetInt(serialized, "soulsRequiredForExit", M1RoomDefinition.RequiredSoulCount);
            SetFloat(serialized, "dasherWarningTriggerRange", 8f);
            SetEnum(serialized, "dasherInitialPhase", AttackPhase.Idle);
            SetEnum(serialized, "archerAInitialPhase", AttackPhase.Idle);
            SetEnum(serialized, "archerBInitialPhase", AttackPhase.Idle);
            SetEnum(serialized, "firstDasherTarget", M1RoomActor.Player);
            SetEnum(serialized, "roomVariant", roomVariant);

            var spawns = RequireProperty(serialized, "spawns");
            ConfigureRoomSpawns(spawns, roomVariant);
            Apply(serialized, room);
            room.Validate();
            return room;
        }

        private static void ConfigureRoomSpawns(SerializedProperty spawns, M1RoomVariant roomVariant)
        {
            var template = M1RoomPackCatalog.GetSpawnTemplate(roomVariant);
            spawns.arraySize = template.Length;
            for (var index = 0; index < template.Length; index++)
            {
                var spawn = template[index];
                ConfigureSpawn(
                    spawns.GetArrayElementAtIndex(index),
                    spawn.Actor,
                    spawn.Position,
                    spawn.Facing,
                    spawn.HasFacing);
            }
        }

        private static void ConfigureSpawn(
            SerializedProperty spawn,
            M1RoomActor actor,
            Vector2 position,
            Vector2 facing,
            bool hasFacing)
        {
            SetEnum(spawn, "actor", actor);
            SetVector2(spawn, "position", position);
            SetVector2(spawn, "facing", facing);
            SetBool(spawn, "hasFacing", hasFacing);
        }

        private static FunctionalAudioCatalog CreateAudioCatalog()
        {
            var catalog = GetOrCreateAsset<FunctionalAudioCatalog>(AudioDataRoot + "/FunctionalAudioCatalog.asset");
            var serialized = new SerializedObject(catalog);
            var entries = RequireProperty(serialized, "entries");
            entries.arraySize = AudioEvents.Length;

            for (var index = 0; index < AudioEvents.Length; index++)
            {
                var eventType = AudioEvents[index];
                var entry = entries.GetArrayElementAtIndex(index);
                SetEnum(entry, "eventType", eventType);
                SetObject(entry, "clip", LoadRequiredAudioClip(eventType));
            }

            Apply(serialized, catalog);
            return catalog;
        }

        private static AudioClip LoadRequiredAudioClip(FunctionalAudioEvent eventType)
        {
            var path = AudioRoot + "/" + eventType + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"M1 functional audio clip '{path}' has no AudioImporter.");
            }

            var sampleSettings = importer.defaultSampleSettings;
            if (!sampleSettings.preloadAudioData ||
                importer.loadInBackground ||
                sampleSettings.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                importer.loadInBackground = false;
                sampleSettings.preloadAudioData = true;
                sampleSettings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = sampleSettings;
                importer.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                throw new InvalidOperationException($"M1 functional audio clip '{path}' is missing. Run the approved procedural audio generator before content bootstrap.");
            }

            return clip;
        }

        private static PrefabSet CreateM1Prefabs(
            M1SpriteSet sprites,
            M1DirectionalAnimationAssets animations,
            PlayerConfig playerConfig,
            EnemyDefinitions enemyDefinitions)
        {
            var player = SavePrefab(
                PrefabRoot + "/Player.prefab",
                () => CreatePlayerPrefab(sprites.Player, animations.Player, playerConfig));
            var dasher = SavePrefab(
                PrefabRoot + "/Dasher.prefab",
                () => CreateEnemyPrefab("Dasher", sprites.Dasher, animations.Dasher, CharacterAnimationDriver.MajorEnemy, enemyDefinitions.Dasher, typeof(DasherAI), false, sprites.Haste, sprites.Giant, null, null, null));
            var archer = SavePrefab(
                PrefabRoot + "/Archer.prefab",
                () => CreateEnemyPrefab("Archer", sprites.Archer, animations.Archer, CharacterAnimationDriver.MajorEnemy, enemyDefinitions.Archer, typeof(ArcherAI), false, sprites.Haste, sprites.Giant, null, null, null));
            var minion = SavePrefab(
                PrefabRoot + "/Minion.prefab",
                () => CreateEnemyPrefab("Minion", sprites.Minion, animations.Minion, CharacterAnimationDriver.Minion, enemyDefinitions.Minion, typeof(MinionAI), false, sprites.Haste, sprites.Giant, null, null, null));
            var soul = SavePrefab(PrefabRoot + "/SoulFragment.prefab", () => CreateSoulPrefab(sprites.Soul));
            var exit = SavePrefab(PrefabRoot + "/ExitGate.prefab", () => CreateExitPrefab(sprites.Exit));
            return new PrefabSet(player, dasher, archer, minion, soul, exit);
        }

        private static PrefabSet CreateM2Prefabs(
            M2SpriteSet sprites,
            M1DirectionalAnimationAssets animations,
            PlayerConfig playerConfig,
            EnemyDefinitions enemyDefinitions)
        {
            var player = SavePrefab(
                M2PrefabRoot + "/Player.prefab",
                () => CreatePlayerPrefab(sprites.M1.Player, animations.Player, playerConfig));
            var dasher = SavePrefab(
                M2PrefabRoot + "/Dasher.prefab",
                () => CreateEnemyPrefab("Dasher", sprites.M1.Dasher, animations.Dasher, CharacterAnimationDriver.MajorEnemy, enemyDefinitions.Dasher, typeof(DasherAI), true, sprites.M1.Haste, sprites.M1.Giant, sprites.EchoStatus, sprites.EchoLine, sprites.EchoDouble));
            var archer = SavePrefab(
                M2PrefabRoot + "/Archer.prefab",
                () => CreateEnemyPrefab("Archer", sprites.M1.Archer, animations.Archer, CharacterAnimationDriver.MajorEnemy, enemyDefinitions.Archer, typeof(ArcherAI), true, sprites.M1.Haste, sprites.M1.Giant, sprites.EchoStatus, sprites.EchoLine, sprites.EchoDouble));
            var minion = SavePrefab(
                M2PrefabRoot + "/Minion.prefab",
                () => CreateEnemyPrefab("Minion", sprites.M1.Minion, animations.Minion, CharacterAnimationDriver.Minion, enemyDefinitions.Minion, typeof(MinionAI), true, sprites.M1.Haste, sprites.M1.Giant, sprites.EchoStatus, sprites.EchoLine, sprites.EchoDouble));
            var soul = SavePrefab(M2PrefabRoot + "/SoulFragment.prefab", () => CreateSoulPrefab(sprites.M1.Soul));
            var exit = SavePrefab(M2PrefabRoot + "/ExitGate.prefab", () => CreateExitPrefab(sprites.M1.Exit));
            return new PrefabSet(player, dasher, archer, minion, soul, exit);
        }

        private static GameObject CreatePlayerPrefab(
            Sprite sprite,
            DirectionalAnimationSet animationSet,
            PlayerConfig config)
        {
            var root = new GameObject("Player", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D));
            root.layer = PlayerLayer;
            ConfigureSprite(root.GetComponent<SpriteRenderer>(), sprite, "Actors", 10);
            root.GetComponent<CircleCollider2D>().radius = 0.38f;
            ConfigureKinematicBody(root.GetComponent<Rigidbody2D>());

            var health = root.AddComponent<Health>();
            ConfigureHealth(health, 1, 6);
            var input = root.AddComponent<PlayerInputRouter>();
            var dash = root.AddComponent<DashAbility>();
            var controller = root.AddComponent<PlayerController>();
            var lifeCycle = root.AddComponent<PlayerLifeCycle>();
            var targeting = root.AddComponent<BlessingTargeting>();
            ConfigureBlessingTargetingInput(targeting, input);

            ConfigureDash(dash, config, root.transform, health);
            ConfigurePlayerController(controller, config, root.transform, input, dash);
            ConfigurePlayerLifeCycle(lifeCycle, root.transform, health, input, controller, dash);
            ConfigureDirectionalAnimator(
                root.AddComponent<DirectionalSpriteAnimator>(),
                CharacterAnimationDriver.Player,
                animationSet,
                root.GetComponent<SpriteRenderer>(),
                health,
                dash,
                targeting,
                null);

            return root;
        }

        private static GameObject CreateEnemyPrefab(
            string name,
            Sprite sprite,
            DirectionalAnimationSet animationSet,
            CharacterAnimationDriver animationDriver,
            EnemyDefinition definition,
            Type enemyType,
            bool echoEnabled,
            Sprite hasteSprite,
            Sprite giantSprite,
            Sprite echoSprite,
            Sprite echoLineSprite,
            Sprite echoProjectileSprite)
        {
            var root = new GameObject(name, typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D));
            root.layer = EnemyLayer;
            ConfigureSprite(root.GetComponent<SpriteRenderer>(), sprite, "Actors", 10);
            root.GetComponent<CircleCollider2D>().radius = 0.4f;
            ConfigureKinematicBody(root.GetComponent<Rigidbody2D>());

            var health = root.AddComponent<Health>();
            ConfigureHealth(health, 1, definition.MaximumHealth);
            var enemy = root.AddComponent(enemyType) as EnemyBase;
            if (enemy == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException($"'{enemyType.FullName}' is not an M1 enemy runtime component.");
            }

            var serialized = new SerializedObject(enemy);
            SetObject(serialized, "definition", definition);
            SetObject(serialized, "health", health);
            SetObject(serialized, "spawnTransform", root.transform);
            SetVector2(serialized, "initialIntendedFacing", Vector2.down);
            Apply(serialized, enemy);
            ConfigureDirectionalAnimator(
                root.AddComponent<DirectionalSpriteAnimator>(),
                animationDriver,
                animationSet,
                root.GetComponent<SpriteRenderer>(),
                health,
                null,
                null,
                enemy);

            var telegraph = new GameObject("AttackTelegraph", typeof(LineRenderer), typeof(AttackStatePresenter));
            telegraph.transform.SetParent(root.transform, false);
            var line = telegraph.GetComponent<LineRenderer>();
            line.sharedMaterial = GetOrCreateUiLineMaterial();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, root.transform.position);
            line.SetPosition(1, root.transform.position + Vector3.up);
            line.startWidth = 0.04f;
            line.endWidth = 0.04f;
            line.sortingLayerName = "Telegraph";
            line.sortingOrder = 20;
            line.enabled = false;
            var presenter = telegraph.GetComponent<AttackStatePresenter>();
            var presenterSerialized = new SerializedObject(presenter);
            SetObject(presenterSerialized, "line", line);
            Apply(presenterSerialized, presenter);
            if (enemyType == typeof(ArcherAI))
            {
                CreateArcherProjectileVisual(root.transform);
                if (echoEnabled)
                {
                    CreateEchoProjectileVisual(root.transform, echoLineSprite, echoProjectileSprite);
                }
            }

            CreateEnemyHealthBar(root.transform, health);
            CreateEnemyBlessingIndicator(root.transform, hasteSprite, giantSprite, echoEnabled ? echoSprite : null);

            return root;
        }
        private static void CreateArcherProjectileVisual(Transform parent)
        {
            var projectile = new GameObject("ArcherProjectileVisual", typeof(LineRenderer), typeof(ArcherProjectilePresenter));
            projectile.transform.SetParent(parent, false);
            var line = projectile.GetComponent<LineRenderer>();
            line.sharedMaterial = GetOrCreateUiLineMaterial();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.SetPosition(0, parent.position);
            line.SetPosition(1, parent.position + Vector3.up);
            line.startWidth = 0.08f;
            line.endWidth = 0.08f;
            line.startColor = new Color32(91, 235, 255, 255);
            line.endColor = Color.white;
            line.sortingLayerName = "VFX";
            line.sortingOrder = 19;
            line.enabled = false;
            var presenter = projectile.GetComponent<ArcherProjectilePresenter>();
            var serialized = new SerializedObject(presenter);
            SetObject(serialized, "line", line);
            Apply(serialized, presenter);
        }
        private static void CreateEchoProjectileVisual(
            Transform parent,
            Sprite lineSprite,
            Sprite projectileSprite)
        {
            var root = new GameObject("EchoProjectileVisual", typeof(EchoProjectilePresenter));
            root.transform.SetParent(parent, false);

            var pendingLineObject = new GameObject("PendingLine", typeof(SpriteRenderer));
            pendingLineObject.transform.SetParent(root.transform, false);
            var pendingLineRenderer = pendingLineObject.GetComponent<SpriteRenderer>();
            ConfigureSprite(pendingLineRenderer, lineSprite, "Telegraph", 24);
            pendingLineRenderer.enabled = false;

            var projectileObject = new GameObject("ProjectileBody", typeof(SpriteRenderer));
            projectileObject.transform.SetParent(root.transform, false);
            var projectileRenderer = projectileObject.GetComponent<SpriteRenderer>();
            ConfigureSprite(projectileRenderer, projectileSprite, "VFX", 25);
            projectileRenderer.enabled = false;

            var serialized = new SerializedObject(root.GetComponent<EchoProjectilePresenter>());
            SetObject(serialized, "pendingLineRenderer", pendingLineRenderer);
            SetObject(serialized, "projectileRenderer", projectileRenderer);
            Apply(serialized, root.GetComponent<EchoProjectilePresenter>());
        }


        private static void CreateEnemyBlessingIndicator(
            Transform parent,
            Sprite hasteSprite,
            Sprite giantSprite,
            Sprite echoSprite)
        {
            var root = new GameObject("BlessingIndicator", typeof(BlessingIndicator));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 1.18f, 0f);

            var hasteObject = new GameObject("Haste", typeof(SpriteRenderer));
            hasteObject.transform.SetParent(root.transform, false);
            hasteObject.transform.localPosition = new Vector3(-0.22f, 0f, 0f);
            var hasteRenderer = hasteObject.GetComponent<SpriteRenderer>();
            ConfigureSprite(hasteRenderer, hasteSprite, "VFX", 22);
            hasteRenderer.enabled = false;

            var giantObject = new GameObject("Giant", typeof(SpriteRenderer));
            giantObject.transform.SetParent(root.transform, false);
            giantObject.transform.localPosition = new Vector3(0.22f, 0f, 0f);
            var giantRenderer = giantObject.GetComponent<SpriteRenderer>();
            ConfigureSprite(giantRenderer, giantSprite, "VFX", 22);
            giantRenderer.enabled = false;

            SpriteRenderer echoRenderer = null;
            if (echoSprite != null)
            {
                var echoObject = new GameObject("Echo", typeof(SpriteRenderer));
                echoObject.transform.SetParent(root.transform, false);
                echoRenderer = echoObject.GetComponent<SpriteRenderer>();
                ConfigureSprite(echoRenderer, echoSprite, "VFX", 23);
                echoRenderer.enabled = false;
            }

            var serialized = new SerializedObject(root.GetComponent<BlessingIndicator>());
            SetObject(serialized, "hasteRenderer", hasteRenderer);
            SetObject(serialized, "giantRenderer", giantRenderer);
            if (echoRenderer != null)
            {
                SetObject(serialized, "echoRenderer", echoRenderer);
            }

            Apply(serialized, root.GetComponent<BlessingIndicator>());
        }
        private static void CreateEnemyHealthBar(Transform parent, Health health)
        {
            var root = new GameObject("HealthBar", typeof(WorldHealthBar));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0.92f, 0f);

            var backgroundObject = new GameObject("Background", typeof(LineRenderer));
            backgroundObject.transform.SetParent(root.transform, false);
            var background = backgroundObject.GetComponent<LineRenderer>();
            ConfigureHealthBarLine(background, 0.09f, new Color32(7, 12, 22, 240), 20);

            var fillObject = new GameObject("Fill", typeof(LineRenderer));
            fillObject.transform.SetParent(root.transform, false);
            var fill = fillObject.GetComponent<LineRenderer>();
            ConfigureHealthBarLine(fill, 0.055f, new Color32(92, 230, 185, 255), 21);

            var serialized = new SerializedObject(root.GetComponent<WorldHealthBar>());
            SetObject(serialized, "health", health);
            SetObject(serialized, "backgroundLine", background);
            SetObject(serialized, "fillLine", fill);
            Apply(serialized, root.GetComponent<WorldHealthBar>());
        }

        private static void ConfigureHealthBarLine(LineRenderer line, float width, Color color, int sortingOrder)
        {
            line.sharedMaterial = GetOrCreateUiLineMaterial();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.TransformZ;
            line.sortingLayerName = "UI";
            line.sortingOrder = sortingOrder;
            line.SetPosition(0, new Vector3(-0.36f, 0f, 0f));
            line.SetPosition(1, new Vector3(0.36f, 0f, 0f));
        }

        private static Material GetOrCreateUiLineMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(UiLineMaterialPath);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("M1 UI line material requires the 'Sprites/Default' shader.");
            }

            material = new Material(shader)
            {
                name = "M1UiLineUnlit"
            };
            AssetDatabase.CreateAsset(material, UiLineMaterialPath);
            return material;
        }

        private static GameObject CreateSoulPrefab(Sprite sprite)
        {
            var root = new GameObject("SoulFragment", typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(SoulFragment));
            root.layer = PickupLayer;
            ConfigureSprite(root.GetComponent<SpriteRenderer>(), sprite, "VFX", 15);
            var trigger = root.GetComponent<CircleCollider2D>();
            trigger.radius = 0.28f;
            trigger.isTrigger = true;
            var serialized = new SerializedObject(root.GetComponent<SoulFragment>());
            SetObject(serialized, "collectionTrigger", trigger);
            Apply(serialized, root.GetComponent<SoulFragment>());
            return root;
        }

        private static GameObject CreateExitPrefab(Sprite sprite)
        {
            var root = new GameObject("ExitGate", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(ExitGate));
            root.layer = ExitLayer;
            ConfigureSprite(root.GetComponent<SpriteRenderer>(), sprite, "VFX", 14);
            var trigger = root.GetComponent<BoxCollider2D>();
            trigger.size = new Vector2(1f, 1f);
            trigger.isTrigger = true;
            var serialized = new SerializedObject(root.GetComponent<ExitGate>());
            SetObject(serialized, "entryTrigger", trigger);
            Apply(serialized, root.GetComponent<ExitGate>());
            return root;
        }
        private static GameObject CreateWorldPillarPrefab(Sprite sprite)
        {
            var root = new GameObject("WorldPillar", typeof(BoxCollider2D));
            root.layer = WorldLayer;
            root.isStatic = true;

            var visual = new GameObject("Visual", typeof(SpriteRenderer));
            visual.layer = WorldLayer;
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(1.2f, 1.8f, 1f);

            var renderer = visual.GetComponent<SpriteRenderer>();
            ConfigureSprite(renderer, sprite, "World", 10);
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;

            var collider = root.GetComponent<BoxCollider2D>();
            collider.size = new Vector2(1.2f, 1.8f);
            collider.offset = new Vector2(0f, 0.28f);
            collider.isTrigger = false;
            return root;
        }

        private static void ConfigureSprite(SpriteRenderer renderer, Sprite sprite, string sortingLayer, int sortingOrder)
        {
            renderer.sprite = sprite;
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = sortingOrder;
            renderer.drawMode = SpriteDrawMode.Simple;
        }

        private static void ConfigureKinematicBody(Rigidbody2D body)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        private static void ConfigureHealth(Health health, int entityId, int maximumHealth)
        {
            var serialized = new SerializedObject(health);
            SetInt(serialized, "entityId", entityId);
            SetInt(serialized, "maximumHealth", maximumHealth);
            SetBool(serialized, "startsInvulnerable", false);
            Apply(serialized, health);
        }

        private static void ConfigureDash(DashAbility dash, PlayerConfig config, Transform playerTransform, Health health)
        {
            var serialized = new SerializedObject(dash);
            SetObject(serialized, "config", config);
            SetObject(serialized, "playerTransform", playerTransform);
            SetObject(serialized, "health", health);
            Apply(serialized, dash);
        }

        private static void ConfigurePlayerController(
            PlayerController controller,
            PlayerConfig config,
            Transform playerTransform,
            PlayerInputRouter input,
            DashAbility dash)
        {
            var serialized = new SerializedObject(controller);
            SetObject(serialized, "config", config);
            SetObject(serialized, "playerTransform", playerTransform);
            SetObject(serialized, "inputRouter", input);
            SetObject(serialized, "dashAbility", dash);
            Apply(serialized, controller);
        }

        private static void ConfigurePlayerLifeCycle(
            PlayerLifeCycle lifeCycle,
            Transform playerTransform,
            Health health,
            PlayerInputRouter input,
            PlayerController controller,
            DashAbility dash)
        {
            var serialized = new SerializedObject(lifeCycle);
            SetObject(serialized, "playerTransform", playerTransform);
            SetObject(serialized, "health", health);
            SetObject(serialized, "inputRouter", input);
            SetObject(serialized, "playerController", controller);
            SetObject(serialized, "dashAbility", dash);
            Apply(serialized, lifeCycle);
        }

        private static void ConfigureDirectionalAnimator(
            DirectionalSpriteAnimator animator,
            CharacterAnimationDriver driver,
            DirectionalAnimationSet animationSet,
            SpriteRenderer spriteRenderer,
            Health health,
            DashAbility dashAbility,
            BlessingTargeting blessingTargeting,
            EnemyBase enemy)
        {
            var serialized = new SerializedObject(animator);
            SetEnum(serialized, "driver", driver);
            SetEnum(serialized, "initialDirection", CharacterDirection.South);
            SetObject(serialized, "spriteRenderer", spriteRenderer);
            SetObject(serialized, "animationSet", animationSet);
            SetObject(serialized, "health", health);
            SetObject(serialized, "dashAbility", dashAbility);
            SetObject(serialized, "blessingTargeting", blessingTargeting);
            SetObject(serialized, "enemy", enemy);
            Apply(serialized, animator);
        }

        private static GameObject SavePrefab(string assetPath, Func<GameObject> createRoot)
        {
            var root = createRoot();
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Unity failed to save prefab '{assetPath}'.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateScene(
            M1SpriteSet sprites,
            M1RoomDefinition roomDefinition,
            FunctionalAudioCatalog audioCatalog,
            PrefabSet prefabs)
        {
            var roomDefinitionPath = AssetDatabase.GetAssetPath(roomDefinition);
            var audioCatalogPath = AssetDatabase.GetAssetPath(audioCatalog);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("M1_GuidedValidation");
            var world = new GameObject("World");
            world.transform.SetParent(root.transform, false);
            CreateWorldPresentation(world.transform, sprites.Tile);
            CreateBounds(world.transform);

            var camera = CreateCamera(root.transform);
            var playerSpawn = roomDefinition.GetSpawn(M1RoomActor.Player);
            var player = InstantiatePrefab(prefabs.Player, root.transform, "Player", playerSpawn.Position);
            var playerAnimator = player.GetComponent<DirectionalSpriteAnimator>();
            if (playerSpawn.HasFacing)
            {
                playerAnimator.SetInitialFacing(playerSpawn.Facing);
                EditorUtility.SetDirty(playerAnimator);
            }

            var playerHealth = player.GetComponent<Health>();
            var playerInput = player.GetComponent<PlayerInputRouter>();
            var playerLifeCycle = player.GetComponent<PlayerLifeCycle>();
            var blessingTargeting = player.GetComponent<BlessingTargeting>();

            var dasherSpawn = roomDefinition.GetSpawn(M1RoomActor.Dasher);
            var archerASpawn = roomDefinition.GetSpawn(M1RoomActor.ArcherA);
            var archerBSpawn = roomDefinition.GetSpawn(M1RoomActor.ArcherB);
            var minionASpawn = roomDefinition.GetSpawn(M1RoomActor.MinionA);
            var minionBSpawn = roomDefinition.GetSpawn(M1RoomActor.MinionB);
            var dasher = InstantiateEnemy(prefabs.Dasher, root.transform, "Dasher", 101, player.transform, dasherSpawn.Position, dasherSpawn.HasFacing ? dasherSpawn.Facing : Vector2.zero);
            var archerA = InstantiateEnemy(prefabs.Archer, root.transform, "Archer_A", 102, player.transform, archerASpawn.Position, archerASpawn.HasFacing ? archerASpawn.Facing : Vector2.zero);
            var archerB = InstantiateEnemy(prefabs.Archer, root.transform, "Archer_B", 103, player.transform, archerBSpawn.Position, archerBSpawn.HasFacing ? archerBSpawn.Facing : Vector2.zero);
            var minionA = InstantiateEnemy(prefabs.Minion, root.transform, "Minion_A", 104, player.transform, minionASpawn.Position, minionASpawn.HasFacing ? minionASpawn.Facing : Vector2.zero);
            var minionB = InstantiateEnemy(prefabs.Minion, root.transform, "Minion_B", 105, player.transform, minionBSpawn.Position, minionBSpawn.HasFacing ? minionBSpawn.Facing : Vector2.zero);
            var enemies = new[] { dasher, archerA, archerB, minionA, minionB };
            ConfigureBlessingOwnerAndTargets(blessingTargeting, playerHealth, enemies);

            var exit = InstantiatePrefab(prefabs.Exit, root.transform, "ExitGate", new Vector2(7f, -3.5f)).GetComponent<ExitGate>();
            var souls = new GameObject("Souls");
            souls.transform.SetParent(root.transform, false);
            var systems = new GameObject("Systems");
            systems.transform.SetParent(root.transform, false);
            var room = ConfigureRoomLifecycle(
                systems,
                roomDefinition,
                enemies,
                prefabs.Soul.GetComponent<SoulFragment>(),
                souls.transform,
                exit,
                blessingTargeting);
            var restartController = ConfigureRestartController(systems, playerLifeCycle, enemies, blessingTargeting, room);
            var audioEmitter = ConfigureAudioAndWebStart(systems, audioCatalog, playerInput);
            ConfigureRuntimeBinder(systems, playerHealth, blessingTargeting, enemies, room, false);
            ConfigureFunctionalAudioBridge(systems, audioEmitter, playerHealth, enemies, room, restartController, blessingTargeting);
            ConfigurePauseController(systems, playerInput, blessingTargeting, restartController);

            // The guided room is the first room of the submitted run, so its exit continues
            // into Room_02 instead of ending in place.
            ConfigureRoomSequence(systems, exit, SecondRoomSceneName);
            var guidedPack = M1RoomPackCatalog.GetPack(M1RoomVariant.M1GuidedValidation);
            CreateHud(
                root.transform,
                playerHealth,
                player.GetComponent<DashAbility>(),
                blessingTargeting,
                room,
                camera,
                sprites,
                guidedPack.RoomLabel,
                guidedPack.ObjectiveTitle,
                guidedPack.ObjectiveDetail,
                false,
                null,
                systems.GetComponent<WebStartGate>(),
                playerLifeCycle);

            ValidateSceneAudioListener(scene, camera);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Unity failed to save the M1 scene to '{ScenePath}'.");
            }

            BindPersistentSceneAssets(
                scene,
                ScenePath,
                room,
                roomDefinitionPath,
                audioEmitter,
                audioCatalogPath);
        }
        private static void CreateM2Scene(
            string scenePath,
            string sceneName,
            string roomLabel,
            M1RoomDefinition roomDefinition,
            FunctionalAudioCatalog audioCatalog,
            CharacterIdentityCatalog identityCatalog,
            PrefabSet prefabs,
            M2SpriteSet sprites,
            string nextScene,
            GameObject pillarPrefab)
        {
            var roomDefinitionPath = AssetDatabase.GetAssetPath(roomDefinition);
            var audioCatalogPath = AssetDatabase.GetAssetPath(audioCatalog);
            var identityCatalogPath = AssetDatabase.GetAssetPath(identityCatalog);
            if (string.IsNullOrEmpty(identityCatalogPath))
            {
                throw new InvalidOperationException("The M2 character identity catalog must be a persisted asset.");
            }

            roomDefinition.Validate();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(sceneName);
            var world = new GameObject("World");
            world.transform.SetParent(root.transform, false);
            CreateWorldPresentation(world.transform, sprites.M1.Tile);
            CreateBounds(world.transform);

            if (pillarPrefab != null)
            {
                var pillar = InstantiatePrefab(
                    pillarPrefab,
                    world.transform,
                    "WorldPillar",
                    new Vector2(-3.8f, -1.8f));
                pillar.isStatic = true;
            }

            var camera = CreateCamera(root.transform);
            var playerSpawn = roomDefinition.GetSpawn(M1RoomActor.Player);
            var player = InstantiatePrefab(prefabs.Player, root.transform, "Player", playerSpawn.Position);
            var playerAnimator = player.GetComponent<DirectionalSpriteAnimator>();
            if (playerSpawn.HasFacing)
            {
                playerAnimator.SetInitialFacing(playerSpawn.Facing);
                EditorUtility.SetDirty(playerAnimator);
            }

            var playerHealth = player.GetComponent<Health>();
            var playerInput = player.GetComponent<PlayerInputRouter>();
            var playerLifeCycle = player.GetComponent<PlayerLifeCycle>();
            var blessingTargeting = player.GetComponent<BlessingTargeting>();

            var dasherSpawn = roomDefinition.GetSpawn(M1RoomActor.Dasher);
            var archerASpawn = roomDefinition.GetSpawn(M1RoomActor.ArcherA);
            var archerBSpawn = roomDefinition.GetSpawn(M1RoomActor.ArcherB);
            var minionASpawn = roomDefinition.GetSpawn(M1RoomActor.MinionA);
            var minionBSpawn = roomDefinition.GetSpawn(M1RoomActor.MinionB);
            var dasher = InstantiateEnemy(prefabs.Dasher, root.transform, "Dasher", 101, player.transform, dasherSpawn.Position, dasherSpawn.HasFacing ? dasherSpawn.Facing : Vector2.zero);
            var archerA = InstantiateEnemy(prefabs.Archer, root.transform, "Archer_A", 102, player.transform, archerASpawn.Position, archerASpawn.HasFacing ? archerASpawn.Facing : Vector2.zero);
            var archerB = InstantiateEnemy(prefabs.Archer, root.transform, "Archer_B", 103, player.transform, archerBSpawn.Position, archerBSpawn.HasFacing ? archerBSpawn.Facing : Vector2.zero);
            var minionA = InstantiateEnemy(prefabs.Minion, root.transform, "Minion_A", 104, player.transform, minionASpawn.Position, minionASpawn.HasFacing ? minionASpawn.Facing : Vector2.zero);
            var minionB = InstantiateEnemy(prefabs.Minion, root.transform, "Minion_B", 105, player.transform, minionBSpawn.Position, minionBSpawn.HasFacing ? minionBSpawn.Facing : Vector2.zero);
            var enemies = new[] { dasher, archerA, archerB, minionA, minionB };
            ConfigureBlessingOwnerAndTargets(blessingTargeting, playerHealth, enemies);

            var exit = InstantiatePrefab(prefabs.Exit, root.transform, "ExitGate", new Vector2(7f, -3.5f)).GetComponent<ExitGate>();
            var souls = new GameObject("Souls");
            souls.transform.SetParent(root.transform, false);
            var systems = new GameObject("Systems");
            systems.transform.SetParent(root.transform, false);
            var room = ConfigureRoomLifecycle(
                systems,
                roomDefinition,
                enemies,
                prefabs.Soul.GetComponent<SoulFragment>(),
                souls.transform,
                exit,
                blessingTargeting);
            var restartController = ConfigureRestartController(systems, playerLifeCycle, enemies, blessingTargeting, room);
            ConfigureRoomSequence(systems, exit, nextScene);
            var audioEmitter = ConfigureAudioAndWebStart(systems, audioCatalog, playerInput);
            ConfigureRuntimeBinder(systems, playerHealth, blessingTargeting, enemies, room, true);
            ConfigureFunctionalAudioBridge(systems, audioEmitter, playerHealth, enemies, room, restartController, blessingTargeting);
            ConfigurePauseController(systems, playerInput, blessingTargeting, restartController);
            var pack = M1RoomPackCatalog.GetPack(roomDefinition.RoomVariant);
            if (!string.Equals(roomLabel, pack.RoomLabel, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Room label '{roomLabel}' does not match pack label '{pack.RoomLabel}' for {roomDefinition.RoomVariant}.");
            }

            var hud = CreateHud(
                root.transform,
                playerHealth,
                player.GetComponent<DashAbility>(),
                blessingTargeting,
                room,
                camera,
                sprites.M1,
                pack.RoomLabel,
                pack.ObjectiveTitle,
                pack.ObjectiveDetail,
                true,
                sprites.Echo,
                systems.GetComponent<WebStartGate>(),
                playerLifeCycle);
            var appealPresenter = ConfigureCharacterAppeal(
                hud,
                identityCatalogPath,
                systems.GetComponent<WebStartGate>(),
                playerLifeCycle,
                blessingTargeting,
                exit,
                enemies);

            ValidateSceneAudioListener(scene, camera);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException($"Unity failed to save the M2 scene to '{scenePath}'.");
            }

            BindPersistentSceneAssets(
                scene,
                scenePath,
                room,
                roomDefinitionPath,
                audioEmitter,
                audioCatalogPath);
            BindPersistentIdentityCatalog(scene, scenePath, appealPresenter, identityCatalogPath);
        }

        /// <summary>
        /// Re-points the character card at the catalog asset on disk after the scene is
        /// saved, the same reason the room definition and audio catalog are rebound: an
        /// asset created in this session must not linger as an in-memory reference.
        /// </summary>
        private static void BindPersistentIdentityCatalog(
            Scene scene,
            string scenePath,
            CharacterAppealPresenter presenter,
            string identityCatalogPath)
        {
            var persistedIdentities = RequireAsset<CharacterIdentityCatalog>(identityCatalogPath);
            persistedIdentities.Validate();

            var serialized = new SerializedObject(presenter);
            SetObject(serialized, "catalog", persistedIdentities);
            Apply(serialized, presenter);

            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException(
                    $"Unity failed to persist the character identity catalog reference in scene '{scenePath}'.");
            }
        }

        private static void BindPersistentSceneAssets(
            Scene scene,
            string scenePath,
            M1RoomLifecycle room,
            string roomDefinitionPath,
            FunctionalAudioEmitter audioEmitter,
            string audioCatalogPath)
        {
            var persistedDefinition = RequireAsset<M1RoomDefinition>(roomDefinitionPath);
            var persistedCatalog = RequireAsset<FunctionalAudioCatalog>(audioCatalogPath);

            var roomSerialized = new SerializedObject(room);
            SetObject(roomSerialized, "definition", persistedDefinition);
            Apply(roomSerialized, room);

            var audioSerialized = new SerializedObject(audioEmitter);
            SetObject(audioSerialized, "catalog", persistedCatalog);
            Apply(audioSerialized, audioEmitter);

            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException(
                    $"Unity failed to persist external data references in scene '{scenePath}'.");
            }

            roomSerialized.Update();
            audioSerialized.Update();
            if (RequireProperty(roomSerialized, "definition").objectReferenceValue == null ||
                RequireProperty(audioSerialized, "catalog").objectReferenceValue == null)
            {
                throw new InvalidOperationException(
                    $"Scene '{scenePath}' lost a required persistent data reference during serialization.");
            }
        }
        private static void ValidateSceneAudioListener(Scene scene, Camera intendedCamera)
        {
            if (!scene.IsValid() || intendedCamera == null || intendedCamera.gameObject.scene != scene)
            {
                throw new InvalidOperationException("M1 scene requires an intended camera in the target scene.");
            }

            var intendedListener = intendedCamera.GetComponent<AudioListener>();
            if (intendedListener == null)
            {
                throw new InvalidOperationException("M1 intended camera requires an AudioListener.");
            }

            var listeners = new List<AudioListener>();
            var enabledListenerCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    listeners.Add(listener);
                    if (listener.enabled)
                    {
                        enabledListenerCount++;
                    }
                }
            }

            if (listeners.Count != 1 ||
                listeners[0] != intendedListener ||
                !intendedListener.enabled ||
                enabledListenerCount != 1)
            {
                throw new InvalidOperationException("M1 scene requires the intended camera listener to be the sole enabled AudioListener.");
            }
        }


        private static void CreateWorldPresentation(Transform parent, Sprite tileSprite)
        {
            var floor = new GameObject("PixelFloor", typeof(SpriteRenderer));
            floor.transform.SetParent(parent, false);
            var floorRenderer = floor.GetComponent<SpriteRenderer>();
            ConfigureSprite(floorRenderer, tileSprite, "Background", -20);
            floorRenderer.drawMode = SpriteDrawMode.Tiled;
            floorRenderer.size = new Vector2(16f, 9f);
        }

        private static void CreateBounds(Transform parent)
        {
            CreateBound(parent, "NorthBound", new Vector2(0f, 4.5f), new Vector2(16f, 0.2f));
            CreateBound(parent, "SouthBound", new Vector2(0f, -4.5f), new Vector2(16f, 0.2f));
            CreateBound(parent, "WestBound", new Vector2(-8f, 0f), new Vector2(0.2f, 9f));
            CreateBound(parent, "EastBound", new Vector2(8f, 0f), new Vector2(0.2f, 9f));
        }

        private static void CreateBound(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var bound = new GameObject(name, typeof(BoxCollider2D));
            bound.layer = WorldLayer;
            bound.transform.SetParent(parent, false);
            bound.transform.localPosition = position;
            bound.GetComponent<BoxCollider2D>().size = size;
        }

        private static Camera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(FixedAspectViewport));
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraOrthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(14, 18, 28, 255);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.tag = "MainCamera";
            return camera;
        }

        private static GameObject InstantiatePrefab(GameObject prefab, Transform parent, string name, Vector2 position)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Unity failed to instantiate '{prefab.name}'.");
            }

            instance.name = name;
            instance.transform.localPosition = new Vector3(position.x, position.y, 0f);
            return instance;
        }

        private static EnemyBase InstantiateEnemy(
            GameObject prefab,
            Transform parent,
            string name,
            int entityId,
            Transform playerTarget,
            Vector2 position,
            Vector2 facing)
        {
            var instance = InstantiatePrefab(prefab, parent, name, position);
            var initialFacing = facing.sqrMagnitude > 0.000001f
                ? facing
                : (Vector2)(playerTarget.position - instance.transform.position);
            var animator = instance.GetComponent<DirectionalSpriteAnimator>();
            animator.SetInitialFacing(initialFacing);
            EditorUtility.SetDirty(animator);

            var health = instance.GetComponent<Health>();
            ConfigureHealth(health, entityId, health.MaximumHealth);
            var enemy = instance.GetComponent<EnemyBase>();
            if (enemy == null)
            {
                throw new InvalidOperationException($"'{name}' is missing its EnemyBase runtime component.");
            }

            var serialized = new SerializedObject(enemy);
            SetObject(serialized, "playerTarget", playerTarget);
            SetObject(serialized, "spawnTransform", instance.transform);
            SetVector2(serialized, "initialIntendedFacing", initialFacing.normalized);
            Apply(serialized, enemy);
            return enemy;
        }

        private static void ConfigureBlessingOwnerAndTargets(
            BlessingTargeting targeting,
            Health ownerHealth,
            params EnemyBase[] enemies)
        {
            if (targeting == null || ownerHealth == null)
            {
                throw new InvalidOperationException("M1 player must include blessing targeting and health components.");
            }

            // Owner and target registration are intentionally runtime state. M1SceneRuntimeBinder
            // initializes them after Unity has constructed the serialized scene graph.
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.Health == null || enemy.EntityId == 0)
                {
                    throw new InvalidOperationException("M1 blessing targets require initialized enemy health references.");
                }
            }
        }

        private static M1RoomLifecycle ConfigureRoomLifecycle(
            GameObject systems,
            M1RoomDefinition definition,
            EnemyBase[] enemies,
            SoulFragment soulPrefab,
            Transform soulParent,
            ExitGate exit,
            BlessingTargeting targeting)
        {
            var room = systems.AddComponent<M1RoomLifecycle>();
            var enemyHealths = new Health[enemies.Length];
            for (var index = 0; index < enemies.Length; index++)
            {
                enemyHealths[index] = enemies[index].Health;
            }

            var serialized = new SerializedObject(room);
            SetObject(serialized, "definition", definition);
            SetObjectArray(serialized, "enemyHealths", enemyHealths);
            SetObject(serialized, "soulFragmentPrefab", soulPrefab);
            SetObject(serialized, "soulParent", soulParent);
            SetObject(serialized, "exitGate", exit);
            SetObject(serialized, "blessingTargeting", targeting);
            Apply(serialized, room);
            return room;
        }

        private static RoomRestartController ConfigureRestartController(
            GameObject systems,
            PlayerLifeCycle player,
            EnemyBase[] enemies,
            BlessingTargeting targeting,
            M1RoomLifecycle room)
        {
            var controller = systems.AddComponent<RoomRestartController>();
            var serialized = new SerializedObject(controller);
            SetObject(serialized, "playerLifeCycle", player);
            SetObjectArray(serialized, "enemies", enemies);
            SetObject(serialized, "blessingTargeting", targeting);
            SetObject(serialized, "roomLifecycle", room);
            Apply(serialized, controller);
            return controller;
        }
        private static void ConfigureRoomSequence(GameObject systems, ExitGate exit, string nextScene)
        {
            var controller = systems.AddComponent<RoomSequenceController>();
            var serialized = new SerializedObject(controller);
            SetObject(serialized, "exitGate", exit);
            var nextSceneProperty = RequireProperty(serialized, "nextScene");
            RequireType(nextSceneProperty, SerializedPropertyType.String, "nextScene");
            nextSceneProperty.stringValue = nextScene ?? string.Empty;
            Apply(serialized, controller);
        }


        private static FunctionalAudioEmitter ConfigureAudioAndWebStart(
            GameObject systems,
            FunctionalAudioCatalog catalog,
            PlayerInputRouter input)
        {
            var audioObject = new GameObject("FunctionalAudio", typeof(AudioSource), typeof(FunctionalAudioEmitter));
            audioObject.transform.SetParent(systems.transform, false);
            var source = audioObject.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            var emitter = audioObject.GetComponent<FunctionalAudioEmitter>();
            var emitterSerialized = new SerializedObject(emitter);
            SetObject(emitterSerialized, "catalog", catalog);
            SetObject(emitterSerialized, "audioSource", source);
            Apply(emitterSerialized, emitter);

            var gate = systems.AddComponent<WebStartGate>();
            var gateSerialized = new SerializedObject(gate);
            SetObject(gateSerialized, "inputRouter", input);
            SetObject(gateSerialized, "audioEmitter", emitter);
            Apply(gateSerialized, gate);
            return emitter;
        }

        private static void ConfigureRuntimeBinder(
            GameObject systems,
            Health playerHealth,
            BlessingTargeting targeting,
            EnemyBase[] enemies,
            M1RoomLifecycle room,
            bool echoEnabled)
        {
            var targetingSerialized = new SerializedObject(targeting);
            SetBool(targetingSerialized, "echoEnabled", echoEnabled);
            Apply(targetingSerialized, targeting);

            var binder = systems.AddComponent<M1SceneRuntimeBinder>();
            var serialized = new SerializedObject(binder);
            SetObject(serialized, "playerHealth", playerHealth);
            SetObject(serialized, "blessingTargeting", targeting);
            SetObjectArray(serialized, "enemies", enemies);
            SetObject(serialized, "roomLifecycle", room);
            Apply(serialized, binder);
        }

        /// <summary>
        /// Routes blessing input through PlayerInputRouter. Both components live on
        /// the player root, so the reference is authored into the prefab.
        /// </summary>
        private static void ConfigureBlessingTargetingInput(
            BlessingTargeting targeting,
            PlayerInputRouter input)
        {
            var serialized = new SerializedObject(targeting);
            SetObject(serialized, "inputRouter", input);
            Apply(serialized, targeting);
        }

        private static void ConfigureFunctionalAudioBridge(
            GameObject systems,
            FunctionalAudioEmitter emitter,
            Health playerHealth,
            EnemyBase[] enemies,
            M1RoomLifecycle room,
            RoomRestartController restartController,
            BlessingTargeting targeting)
        {
            var bridge = systems.AddComponent<M1FunctionalAudioBridge>();
            var serialized = new SerializedObject(bridge);
            SetObject(serialized, "emitter", emitter);
            SetObject(serialized, "playerHealth", playerHealth);
            SetObjectArray(serialized, "enemies", enemies);
            SetObject(serialized, "roomLifecycle", room);
            SetObject(serialized, "restartController", restartController);
            SetObject(serialized, "blessingTargeting", targeting);
            Apply(serialized, bridge);
        }

        private static void ConfigurePauseController(
            GameObject systems,
            PlayerInputRouter input,
            BlessingTargeting targeting,
            RoomRestartController restartController)
        {
            var pauseController = systems.AddComponent<PauseController>();
            var serialized = new SerializedObject(pauseController);
            SetObject(serialized, "inputRouter", input);
            SetObject(serialized, "blessingTargeting", targeting);
            SetObject(serialized, "restartController", restartController);
            Apply(serialized, pauseController);
        }

        /// <summary>Builds the HUD and returns its canvas transform for M2-only additions.</summary>
        private static Transform CreateHud(
            Transform parent,
            Health playerHealth,
            DashAbility dashAbility,
            BlessingTargeting blessingTargeting,
            M1RoomLifecycle roomLifecycle,
            Camera worldCamera,
            M1SpriteSet sprites,
            string roomLabel,
            string objectiveTitle,
            string objectiveDetail,
            bool echoEnabled,
            Sprite echoSprite,
            WebStartGate startGate,
            PlayerLifeCycle playerLifeCycle)
        {
            var hud = new GameObject(
                "HUD",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(HUDController));
            hud.transform.SetParent(parent, false);

            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = worldCamera;
            canvas.planeDistance = 1f;
            canvas.pixelPerfect = true;
            canvas.sortingLayerName = "UI";
            canvas.sortingOrder = 100;

            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException("M1 HUD requires Unity's LegacyRuntime font.");
            }

            if (echoEnabled && echoSprite == null)
            {
                throw new InvalidOperationException("M2 HUD requires an Echo sprite.");
            }

            var panelColor = new Color32(10, 17, 30, 238);
            var paleText = new Color32(225, 239, 244, 255);
            var mutedText = new Color32(148, 173, 186, 255);
            var cyan = new Color32(55, 211, 242, 255);
            var orange = new Color32(255, 137, 72, 255);
            var purple = new Color32(179, 121, 255, 255);

            var playerPanel = CreateHudPanel(
                hud.transform,
                "PlayerStatus",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -24f),
                new Vector2(468f, 170f),
                panelColor);
            CreateHudIcon(playerPanel, "PlayerPortrait", sprites.Player, new Vector2(20f, -28f), new Vector2(100f, 112f), paleText);
            var healthText = CreateHudText(playerPanel, "LifeText", "LIFE  6 / 6", font, 28, TextAnchor.MiddleLeft, paleText, new Vector2(140f, -20f), new Vector2(292f, 34f));
            var healthFill = CreateHudBar(playerPanel, "LifeBar", new Vector2(140f, -58f), new Vector2(292f, 26f), new Color32(70, 224, 205, 255));
            var dashText = CreateHudText(playerPanel, "DashText", "DASH  READY", font, 22, TextAnchor.MiddleLeft, cyan, new Vector2(140f, -96f), new Vector2(292f, 28f));
            var dashFill = CreateHudBar(playerPanel, "DashBar", new Vector2(140f, -130f), new Vector2(292f, 14f), cyan);

            var objectivePanel = CreateHudPanel(
                hud.transform,
                "Objective",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -24f),
                new Vector2(720f, 78f),
                panelColor);
            CreateHudText(objectivePanel, "Title", objectiveTitle, font, 27, TextAnchor.MiddleCenter, paleText, new Vector2(20f, -10f), new Vector2(680f, 34f));
            CreateHudText(objectivePanel, "Detail", objectiveDetail, font, 18, TextAnchor.MiddleCenter, mutedText, new Vector2(20f, -44f), new Vector2(680f, 24f));

            var roomPanel = CreateHudPanel(
                hud.transform,
                "RoomStatus",
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-24f, -24f),
                new Vector2(390f, 170f),
                panelColor);
            CreateHudText(roomPanel, "RoomText", roomLabel, font, 28, TextAnchor.MiddleRight, paleText, new Vector2(24f, -18f), new Vector2(342f, 36f));
            CreateHudIcon(roomPanel, "SoulIcon", sprites.Soul, new Vector2(24f, -63f), new Vector2(58f, 58f), paleText);
            var soulText = CreateHudText(roomPanel, "SoulText", "SOULS  0 / 3", font, 25, TextAnchor.MiddleLeft, paleText, new Vector2(96f, -69f), new Vector2(270f, 42f));
            var exitText = CreateHudText(roomPanel, "ExitText", "EXIT  LOCKED  0/3", font, 21, TextAnchor.MiddleRight, orange, new Vector2(24f, -122f), new Vector2(342f, 30f));

            Transform blessingPanel;
            if (echoEnabled)
            {
                blessingPanel = CreateHudPanel(
                    hud.transform,
                    "BlessingSelection",
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 24f),
                    new Vector2(820f, 170f),
                    panelColor);
            }
            else
            {
                blessingPanel = CreateHudPanel(
                    hud.transform,
                    "BlessingSelection",
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 24f),
                    new Vector2(552f, 170f),
                    panelColor);
            }

            Text hasteStatusText;
            var hasteFrame = CreateBlessingCard(
                blessingPanel,
                "HasteCard",
                sprites.Haste,
                "1",
                "HASTE",
                "SPEED + ATTACK RATE",
                new Vector2(16f, -18f),
                cyan,
                font,
                out hasteStatusText);
            Text giantStatusText;
            var giantFrame = CreateBlessingCard(
                blessingPanel,
                "GiantCard",
                sprites.Giant,
                "2",
                "GIANT",
                "SIZE + ATTACK POWER",
                new Vector2(284f, -18f),
                orange,
                font,
                out giantStatusText);

            Text echoStatusText = null;
            Image echoFrame = null;
            if (echoEnabled)
            {
                echoFrame = CreateBlessingCard(
                    blessingPanel,
                    "EchoCard",
                    echoSprite,
                    "3 ECHO",
                    string.Empty,
                    "REPEAT LOCKED ATTACK",
                    new Vector2(552f, -18f),
                    purple,
                    font,
                    out echoStatusText);
            }

            var selectionText = CreateHudText(
                blessingPanel,
                "SelectionHint",
                echoEnabled
                    ? "1 / 2 / 3 SELECT BLESSING  |  SPACE DASH  |  R RESTART"
                    : "1 / 2 SELECT BLESSING  |  SPACE DASH  |  R RESTART",
                font,
                18,
                TextAnchor.MiddleCenter,
                mutedText,
                new Vector2(20f, -132f),
                echoEnabled ? new Vector2(780f, 26f) : new Vector2(512f, 26f));

            var controller = hud.GetComponent<HUDController>();
            var serialized = new SerializedObject(controller);
            SetObject(serialized, "playerHealth", playerHealth);
            SetObject(serialized, "dashAbility", dashAbility);
            SetObject(serialized, "blessingTargeting", blessingTargeting);
            SetObject(serialized, "roomLifecycle", roomLifecycle);
            SetObject(serialized, "healthFill", healthFill);
            SetObject(serialized, "dashFill", dashFill);
            SetObject(serialized, "healthText", healthText);
            SetObject(serialized, "dashText", dashText);
            SetObject(serialized, "soulText", soulText);
            SetObject(serialized, "exitText", exitText);
            SetObject(serialized, "selectionText", selectionText);
            SetObject(serialized, "hasteStatusText", hasteStatusText);
            SetObject(serialized, "giantStatusText", giantStatusText);
            SetObject(serialized, "hasteFrame", hasteFrame);
            SetObject(serialized, "giantFrame", giantFrame);
            if (echoEnabled)
            {
                SetObject(serialized, "echoStatusText", echoStatusText);
                SetObject(serialized, "echoFrame", echoFrame);
            }
            Apply(serialized, controller);
            ConfigureStartAndOutcomeOverlays(hud.transform, font, startGate, playerLifeCycle);
            return hud.transform;
        }

        /// <summary>
        /// Adds the two panels a first-time player needs: what the room is waiting for before
        /// the first trusted input, and what to press after a defeat. Both are presentation
        /// only and are created for every room.
        /// </summary>
        private static void ConfigureStartAndOutcomeOverlays(
            Transform hud,
            Font font,
            WebStartGate startGate,
            PlayerLifeCycle playerLifeCycle)
        {
            if (startGate == null || playerLifeCycle == null)
            {
                throw new InvalidOperationException("A room HUD requires its web start gate and player life cycle.");
            }

            var paleText = new Color32(233, 244, 250, 255);
            var mutedText = new Color32(160, 184, 198, 255);
            var warmText = new Color32(255, 150, 96, 255);

            var startHolder = new GameObject("StartPrompt", typeof(RectTransform), typeof(StartGatePrompt));
            startHolder.transform.SetParent(hud, false);
            ConfigureHudRect(
                startHolder.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(880f, 150f));
            var startPanel = CreateHudPanel(
                startHolder.transform,
                "Panel",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(880f, 150f),
                new Color32(8, 14, 24, 242));
            CreateHudText(startPanel, "Line1", "CLICK TO BEGIN", font, 42, TextAnchor.MiddleCenter, paleText, new Vector2(20f, -18f), new Vector2(840f, 52f));
            CreateHudText(startPanel, "Line2", "THE ROOM STAYS STILL UNTIL YOU DO", font, 20, TextAnchor.MiddleCenter, mutedText, new Vector2(20f, -78f), new Vector2(840f, 30f));
            CreateHudText(startPanel, "Line3", "WASD MOVE   SPACE DASH   1 / 2 BLESS   LMB APPLY   R RESTART", font, 18, TextAnchor.MiddleCenter, mutedText, new Vector2(20f, -112f), new Vector2(840f, 26f));

            var startSerialized = new SerializedObject(startHolder.GetComponent<StartGatePrompt>());
            SetObject(startSerialized, "startGate", startGate);
            SetObject(startSerialized, "promptRoot", startPanel.gameObject);
            Apply(startSerialized, startHolder.GetComponent<StartGatePrompt>());

            var outcomeHolder = new GameObject("RunOutcome", typeof(RectTransform), typeof(RunOutcomePresenter));
            outcomeHolder.transform.SetParent(hud, false);
            ConfigureHudRect(
                outcomeHolder.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(820f, 190f));
            var defeatPanel = CreateHudPanel(
                outcomeHolder.transform,
                "DefeatPanel",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(820f, 190f),
                new Color32(26, 8, 12, 244));
            CreateHudText(defeatPanel, "Headline", "YOU WERE HIT ONE TIME TOO MANY", font, 34, TextAnchor.MiddleCenter, warmText, new Vector2(20f, -20f), new Vector2(780f, 48f));
            CreateHudText(defeatPanel, "Detail", "AN OVERBLESSED ENEMY DOES NOT FORGET WHO BLESSED IT", font, 19, TextAnchor.MiddleCenter, mutedText, new Vector2(20f, -78f), new Vector2(780f, 30f));
            CreateHudText(defeatPanel, "Action", "PRESS  R  TO RESTART THIS ROOM", font, 26, TextAnchor.MiddleCenter, paleText, new Vector2(20f, -124f), new Vector2(780f, 42f));

            var outcomeSerialized = new SerializedObject(outcomeHolder.GetComponent<RunOutcomePresenter>());
            SetObject(outcomeSerialized, "playerLifeCycle", playerLifeCycle);
            SetObject(outcomeSerialized, "defeatRoot", defeatPanel.gameObject);
            Apply(outcomeSerialized, outcomeHolder.GetComponent<RunOutcomePresenter>());

            defeatPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Adds the M2-only character card to an existing HUD. M1 never calls this, so the
        /// guided scene contains no identity card object at all, the same physical
        /// exclusion the Echo card uses.
        /// </summary>
        /// <remarks>
        /// The catalog is resolved by path rather than by reference because creating the
        /// scene can release a freshly authored asset instance from memory.
        /// </remarks>
        private static CharacterAppealPresenter ConfigureCharacterAppeal(
            Transform hud,
            string identityCatalogPath,
            WebStartGate webStartGate,
            PlayerLifeCycle playerLifeCycle,
            BlessingTargeting blessingTargeting,
            ExitGate exitGate,
            EnemyBase[] enemies)
        {
            if (string.IsNullOrEmpty(identityCatalogPath))
            {
                throw new InvalidOperationException("M2 HUD requires a character identity catalog path.");
            }

            if (webStartGate == null)
            {
                throw new InvalidOperationException("M2 character card requires the web start gate.");
            }

            var identityCatalog = RequireAsset<CharacterIdentityCatalog>(identityCatalogPath);
            identityCatalog.Validate();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException("M2 character card requires Unity's LegacyRuntime font.");
            }

            var holder = new GameObject("CharacterAppeal", typeof(RectTransform), typeof(CharacterAppealPresenter));
            holder.transform.SetParent(hud, false);
            ConfigureHudRect(
                holder.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(760f, 224f));

            var card = CreateHudPanel(
                holder.transform,
                "IdentityPanel",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 96f),
                new Vector2(760f, 224f),
                new Color32(64, 214, 236, 255));
            var frameImage = card.GetComponent<Image>();

            var insetObject = new GameObject("Inset", typeof(RectTransform), typeof(Image));
            insetObject.transform.SetParent(card, false);
            var insetRect = insetObject.GetComponent<RectTransform>();
            insetRect.anchorMin = Vector2.zero;
            insetRect.anchorMax = Vector2.one;
            insetRect.offsetMin = new Vector2(6f, 6f);
            insetRect.offsetMax = new Vector2(-6f, -6f);
            var inset = insetObject.GetComponent<Image>();
            inset.color = new Color32(10, 17, 30, 246);
            inset.raycastTarget = false;

            var cardPaleText = new Color32(225, 239, 244, 255);
            var cardMutedText = new Color32(148, 173, 186, 255);
            var portrait = CreateHudIcon(
                card,
                "Portrait",
                identityCatalog.GetRequired(CharacterRole.Player).GetPortrait(CharacterExpression.Neutral),
                new Vector2(22f, -22f),
                new Vector2(160f, 180f),
                Color.white);
            var nameText = CreateHudText(card, "Name", "RIVELLA", font, 44, TextAnchor.MiddleLeft, cardPaleText, new Vector2(200f, -20f), new Vector2(540f, 54f));
            var roleText = CreateHudText(card, "Role", "AGE 22  ·  CYNICAL FORMER SAINT", font, 20, TextAnchor.MiddleLeft, cardMutedText, new Vector2(200f, -78f), new Vector2(540f, 28f));
            var habitText = CreateHudText(card, "Habit", string.Empty, font, 22, TextAnchor.UpperLeft, cardPaleText, new Vector2(200f, -112f), new Vector2(540f, 90f));

            var presenter = holder.GetComponent<CharacterAppealPresenter>();
            var presenterSerialized = new SerializedObject(presenter);
            SetObject(presenterSerialized, "catalog", identityCatalog);
            SetObject(presenterSerialized, "webStartGate", webStartGate);
            SetObject(presenterSerialized, "playerLifeCycle", playerLifeCycle);
            SetObject(presenterSerialized, "blessingTargeting", blessingTargeting);
            SetObject(presenterSerialized, "exitGate", exitGate);
            SetObjectArray(presenterSerialized, "enemies", enemies);
            SetObject(presenterSerialized, "cardRoot", card.gameObject);
            SetObject(presenterSerialized, "portraitImage", portrait);
            SetObject(presenterSerialized, "frameImage", frameImage);
            SetObject(presenterSerialized, "nameText", nameText);
            SetObject(presenterSerialized, "roleText", roleText);
            SetObject(presenterSerialized, "habitText", habitText);
            Apply(presenterSerialized, presenter);

            card.gameObject.SetActive(false);
            return presenter;
        }

        private static Transform CreateHudPanel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            var panelObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
            panelObject.transform.SetParent(parent, false);
            ConfigureHudRect(panelObject.GetComponent<RectTransform>(), anchor, pivot, anchoredPosition, size);
            var image = panelObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var outline = panelObject.GetComponent<Outline>();
            outline.effectColor = new Color32(67, 96, 113, 220);
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;
            return panelObject.transform;
        }

        private static Image CreateHudBar(Transform parent, string name, Vector2 position, Vector2 size, Color fillColor)
        {
            var background = CreateHudPanel(
                parent,
                name,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                size,
                new Color32(4, 8, 16, 245));

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(background, false);
            var rect = fillObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
            var image = fillObject.GetComponent<Image>();
            var fillSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (fillSprite == null)
            {
                throw new InvalidOperationException("M1 HUD requires Unity's built-in UI sprite for filled bars.");
            }

            image.sprite = fillSprite;
            image.color = fillColor;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.fillAmount = 1f;
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateBlessingCard(
            Transform parent,
            string name,
            Sprite icon,
            string key,
            string title,
            string detail,
            Vector2 position,
            Color frameColor,
            Font font,
            out Text statusText)
        {
            var frame = CreateHudPanel(
                parent,
                name,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                new Vector2(252f, 100f),
                frameColor);
            var frameImage = frame.GetComponent<Image>();

            var insetObject = new GameObject("Inset", typeof(RectTransform), typeof(Image));
            insetObject.transform.SetParent(frame, false);
            var insetRect = insetObject.GetComponent<RectTransform>();
            insetRect.anchorMin = Vector2.zero;
            insetRect.anchorMax = Vector2.one;
            insetRect.offsetMin = new Vector2(5f, 5f);
            insetRect.offsetMax = new Vector2(-5f, -5f);
            var inset = insetObject.GetComponent<Image>();
            inset.color = new Color32(15, 24, 39, 250);
            inset.raycastTarget = false;

            CreateHudIcon(frame, "Icon", icon, new Vector2(10f, -14f), new Vector2(60f, 60f), Color.white);
            CreateHudText(frame, "Title", string.IsNullOrEmpty(title) ? key : key + " " + title, font, 20, TextAnchor.MiddleLeft, Color.white, new Vector2(80f, -10f), new Vector2(156f, 30f));
            CreateHudText(frame, "Detail", detail, font, 12, TextAnchor.MiddleLeft, new Color32(148, 173, 186, 255), new Vector2(80f, -40f), new Vector2(156f, 36f));
            statusText = CreateHudText(frame, "Status", "READY", font, 12, TextAnchor.MiddleRight, frameColor, new Vector2(80f, -76f), new Vector2(156f, 16f));
            return frameImage;
        }

        private static Image CreateHudIcon(
            Transform parent,
            string name,
            Sprite sprite,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            ConfigureHudRect(
                imageObject.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                size);
            var image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateHudText(
            Transform parent,
            string name,
            string value,
            Font font,
            int fontSize,
            TextAnchor alignment,
            Color color,
            Vector2 position,
            Vector2 size)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            textObject.transform.SetParent(parent, false);
            ConfigureHudRect(
                textObject.GetComponent<RectTransform>(),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                size);
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            var shadow = textObject.GetComponent<Shadow>();
            shadow.effectColor = new Color32(0, 0, 0, 230);
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;
            return text;
        }

        private static void ConfigureHudRect(
            RectTransform rect,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static T GetOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                throw new InvalidOperationException($"'{assetPath}' exists but is not a {typeof(T).Name}.");
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static PrefabSet LoadPrefabSet(string prefabRoot)
        {
            return new PrefabSet(
                RequireAsset<GameObject>(prefabRoot + "/Player.prefab"),
                RequireAsset<GameObject>(prefabRoot + "/Dasher.prefab"),
                RequireAsset<GameObject>(prefabRoot + "/Archer.prefab"),
                RequireAsset<GameObject>(prefabRoot + "/Minion.prefab"),
                RequireAsset<GameObject>(prefabRoot + "/SoulFragment.prefab"),
                RequireAsset<GameObject>(prefabRoot + "/ExitGate.prefab"));
        }

        private static T RequireAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Required {typeof(T).Name} asset '{assetPath}' did not reload after persistence.");
            }

            return asset;
        }


        private static void SetObjectArray(SerializedObject serialized, string propertyPath, UnityEngine.Object[] values)
        {
            var property = RequireProperty(serialized, propertyPath);
            if (!property.isArray)
            {
                throw new InvalidOperationException($"'{propertyPath}' is not an array.");
            }

            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                var element = property.GetArrayElementAtIndex(index);
                RequireType(element, SerializedPropertyType.ObjectReference, propertyPath + "[" + index + "]");
                element.objectReferenceValue = values[index];
            }
        }

        private static void SetObject(SerializedObject serialized, string propertyPath, UnityEngine.Object value)
        {
            var property = RequireProperty(serialized, propertyPath);
            RequireType(property, SerializedPropertyType.ObjectReference, propertyPath);
            property.objectReferenceValue = value;
        }

        private static void SetObject(SerializedProperty parent, string propertyPath, UnityEngine.Object value)
        {
            var property = RequireProperty(parent, propertyPath);
            RequireType(property, SerializedPropertyType.ObjectReference, propertyPath);
            property.objectReferenceValue = value;
        }

        private static void SetInt(SerializedObject serialized, string propertyPath, int value)
        {
            var property = RequireProperty(serialized, propertyPath);
            if (property.propertyType != SerializedPropertyType.Integer && property.propertyType != SerializedPropertyType.LayerMask)
            {
                throw new InvalidOperationException($"'{propertyPath}' must be an integer or layer mask.");
            }

            property.intValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string propertyPath, float value)
        {
            var property = RequireProperty(serialized, propertyPath);
            RequireType(property, SerializedPropertyType.Float, propertyPath);
            property.floatValue = value;
        }

        private static void SetBool(SerializedObject serialized, string propertyPath, bool value)
        {
            var property = RequireProperty(serialized, propertyPath);
            RequireType(property, SerializedPropertyType.Boolean, propertyPath);
            property.boolValue = value;
        }

        private static void SetBool(SerializedProperty parent, string propertyPath, bool value)
        {
            var property = RequireProperty(parent, propertyPath);
            RequireType(property, SerializedPropertyType.Boolean, propertyPath);
            property.boolValue = value;
        }

        private static void SetVector2(SerializedObject serialized, string propertyPath, Vector2 value)
        {
            var property = RequireProperty(serialized, propertyPath);
            RequireType(property, SerializedPropertyType.Vector2, propertyPath);
            property.vector2Value = value;
        }

        private static void SetVector2(SerializedProperty parent, string propertyPath, Vector2 value)
        {
            var property = RequireProperty(parent, propertyPath);
            RequireType(property, SerializedPropertyType.Vector2, propertyPath);
            property.vector2Value = value;
        }

        private static void SetRect(SerializedObject serialized, string propertyPath, Rect value)
        {
            var property = RequireProperty(serialized, propertyPath);
            RequireType(property, SerializedPropertyType.Rect, propertyPath);
            property.rectValue = value;
        }

        private static void SetEnum<T>(SerializedObject serialized, string propertyPath, T value) where T : struct, Enum
        {
            var property = RequireProperty(serialized, propertyPath);
            RequireType(property, SerializedPropertyType.Enum, propertyPath);
            property.enumValueIndex = Convert.ToInt32(value);
        }

        private static void SetEnum<T>(SerializedProperty parent, string propertyPath, T value) where T : struct, Enum
        {
            var property = RequireProperty(parent, propertyPath);
            RequireType(property, SerializedPropertyType.Enum, propertyPath);
            property.enumValueIndex = Convert.ToInt32(value);
        }

        private static SerializedProperty RequireProperty(SerializedObject serialized, string propertyPath)
        {
            var property = serialized.FindProperty(propertyPath);
            if (property == null)
            {
                throw new InvalidOperationException($"Serialized property '{propertyPath}' is unavailable on {serialized.targetObject.GetType().Name}.");
            }

            return property;
        }

        private static SerializedProperty RequireProperty(SerializedProperty parent, string propertyPath)
        {
            var property = parent.FindPropertyRelative(propertyPath);
            if (property == null)
            {
                throw new InvalidOperationException($"Serialized property '{propertyPath}' is unavailable on '{parent.propertyPath}'.");
            }

            return property;
        }

        private static void RequireType(SerializedProperty property, SerializedPropertyType expected, string propertyPath)
        {
            if (property.propertyType != expected)
            {
                throw new InvalidOperationException($"Serialized property '{propertyPath}' must be {expected}, but is {property.propertyType}.");
            }
        }

        private static void Apply(SerializedObject serialized, UnityEngine.Object target)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private readonly struct InputActionDefinition
        {
            public InputActionDefinition(string name, string id, string type, string expectedControlType)
            {
                Name = name;
                Id = id;
                Type = type;
                ExpectedControlType = expectedControlType;
            }

            public string Name { get; }
            public string Id { get; }
            public string Type { get; }
            public string ExpectedControlType { get; }
        }

        private readonly struct InputBindingDefinition
        {
            public InputBindingDefinition(string name, string id, string path, string action, bool isComposite, bool isPartOfComposite)
            {
                Name = name;
                Id = id;
                Path = path;
                Action = action;
                IsComposite = isComposite;
                IsPartOfComposite = isPartOfComposite;
            }

            public string Name { get; }
            public string Id { get; }
            public string Path { get; }
            public string Action { get; }
            public bool IsComposite { get; }
            public bool IsPartOfComposite { get; }
        }
        private readonly struct M1SpriteSet
        {
            public M1SpriteSet(
                Sprite player,
                Sprite dasher,
                Sprite archer,
                Sprite minion,
                Sprite soul,
                Sprite exit,
                Sprite tile,
                Sprite haste,
                Sprite giant)
            {
                Player = player;
                Dasher = dasher;
                Archer = archer;
                Minion = minion;
                Soul = soul;
                Exit = exit;
                Tile = tile;
                Haste = haste;
                Giant = giant;
            }

            public Sprite Player { get; }
            public Sprite Dasher { get; }
            public Sprite Archer { get; }
            public Sprite Minion { get; }
            public Sprite Soul { get; }
            public Sprite Exit { get; }
            public Sprite Tile { get; }
            public Sprite Haste { get; }
            public Sprite Giant { get; }
        }

        private readonly struct M2SpriteSet
        {
            public M2SpriteSet(
                M1SpriteSet m1,
                Sprite echo,
                Sprite echoStatus,
                Sprite echoLine,
                Sprite echoDouble,
                Sprite worldPillar)
            {
                M1 = m1;
                Echo = echo;
                EchoStatus = echoStatus;
                EchoLine = echoLine;
                EchoDouble = echoDouble;
                WorldPillar = worldPillar;
            }

            public M1SpriteSet M1 { get; }
            public Sprite Echo { get; }
            public Sprite EchoStatus { get; }
            public Sprite EchoLine { get; }
            public Sprite EchoDouble { get; }
            public Sprite WorldPillar { get; }
        }

        private readonly struct EnemyDefinitions
        {
            public EnemyDefinitions(EnemyDefinition dasher, EnemyDefinition archer, EnemyDefinition minion)
            {
                Dasher = dasher;
                Archer = archer;
                Minion = minion;
            }

            public EnemyDefinition Dasher { get; }
            public EnemyDefinition Archer { get; }
            public EnemyDefinition Minion { get; }
        }

        private readonly struct PrefabSet
        {
            public PrefabSet(GameObject player, GameObject dasher, GameObject archer, GameObject minion, GameObject soul, GameObject exit)
            {
                Player = player;
                Dasher = dasher;
                Archer = archer;
                Minion = minion;
                Soul = soul;
                Exit = exit;
            }

            public GameObject Player { get; }
            public GameObject Dasher { get; }
            public GameObject Archer { get; }
            public GameObject Minion { get; }
            public GameObject Soul { get; }
            public GameObject Exit { get; }
        }
    }
}
