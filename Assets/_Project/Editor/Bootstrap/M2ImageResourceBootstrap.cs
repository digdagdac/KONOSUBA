using System;
using System.Collections.Generic;
using Overbless.Runtime;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Imports the user-approved offline M2 image package without creating gameplay data or scene bindings.
    /// </summary>
    public static class M2ImageResourceBootstrap
    {
        private const int CellSize = 128;
        private const int MaxFrames = 6;
        private const string Root = "Assets/_Project/Art/M2Preproduction";
        private const string GolemAtlasPath = Root + "/Characters/Animation/chr_golem_animation_atlas_v001.png";
        private const string FrameLetters = "abcdef";

        private static readonly DirectionSpec[] Directions =
        {
            new DirectionSpec("south"),
            new DirectionSpec("north"),
            new DirectionSpec("east"),
            new DirectionSpec("west"),
            new DirectionSpec("southeast"),
            new DirectionSpec("southwest"),
            new DirectionSpec("northeast"),
            new DirectionSpec("northwest")
        };

        private static readonly StateSpec[] GolemStates =
        {
            new StateSpec("idle", 4),
            new StateSpec("move", 6),
            new StateSpec("attack_charge", 6),
            new StateSpec("attack_execute", 4),
            new StateSpec("recover", 4),
            new StateSpec("hit", 3),
            new StateSpec("death", 6)
        };

        private static readonly SingleSpriteSpec[] SingleSprites =
        {
            new SingleSpriteSpec("UI/ui_icon_bless_echo_a_v001.png", false),
            new SingleSpriteSpec("UI/ui_icon_echo_status_a_v001.png", false),
            new SingleSpriteSpec("UI/ui_icon_resonance_enemy_badge_a_v001.png", false),
            new SingleSpriteSpec("UI/ui_icon_final_objective_crest_a_v001.png", false),
            new SingleSpriteSpec("UI/ui_icon_run_victory_crest_a_v001.png", false),
            new SingleSpriteSpec("UI/ui_icon_run_defeat_crest_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_echo_double_silhouette_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_echo_line_telegraph_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_echo_ring_telegraph_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_echo_apply_burst_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_resonance_aura_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_resonance_arrival_sigil_a_v001.png", false),
            new SingleSpriteSpec("VFX/vfx_resonance_spawn_burst_a_v001.png", false),
            new SingleSpriteSpec("Environment/env_cliff_edge_tile_a_v001.png", false),
            new SingleSpriteSpec("Environment/env_cliff_inner_corner_tile_a_v001.png", false),
            new SingleSpriteSpec("Environment/env_destructible_pillar_intact_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_destructible_pillar_damaged_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_destructible_pillar_rubble_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_spike_trap_inactive_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_spike_trap_warning_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_spike_trap_active_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_broken_wall_rubble_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_fractured_floor_tile_a_v001.png", false),
            new SingleSpriteSpec("Environment/env_final_room_floor_tile_a_v001.png", false),
            new SingleSpriteSpec("Environment/env_exit_open_pedestal_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_final_portal_closed_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_final_portal_opening_a_v001.png", true),
            new SingleSpriteSpec("Environment/env_final_portal_open_a_v001.png", true)
        };

        [MenuItem("Overbless/M2 Assets/Import Offline Image Resources")]
        public static void Import()
        {
            ConfigureGolemAtlas();
            for (var index = 0; index < SingleSprites.Length; index++)
            {
                ConfigureSingleSprite(SingleSprites[index]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Imported offline M2 image package: 1 atlas and {SingleSprites.Length} single sprites. No runtime binding was created.");
        }

        public static void ImportForBatchMode()
        {
            Import();
        }

        private static void ConfigureGolemAtlas()
        {
            AssetDatabase.ImportAsset(GolemAtlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = RequireImporter(GolemAtlasPath);
            ConfigureCommon(importer, 8192);
            importer.spriteImportMode = SpriteImportMode.Multiple;

            var metadata = new List<SpriteMetaData>();
            var atlasHeight = CellSize * GolemStates.Length;
            for (var stateIndex = 0; stateIndex < GolemStates.Length; stateIndex++)
            {
                var state = GolemStates[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                    {
                        metadata.Add(new SpriteMetaData
                        {
                            name = $"chr_golem_{state.Name}_{direction.Name}_{FrameLetters[frameIndex]}_v001",
                            rect = new Rect(
                                (directionIndex * MaxFrames + frameIndex) * CellSize,
                                atlasHeight - (stateIndex + 1) * CellSize,
                                CellSize,
                                CellSize),
                            alignment = (int)SpriteAlignment.Custom,
                            pivot = new Vector2(0.5f, 0f),
                            border = Vector4.zero
                        });
                    }
                }
            }

#pragma warning disable CS0618
            importer.spritesheet = metadata.ToArray();
#pragma warning restore CS0618
            SetPivot(importer, new Vector2(0.5f, 0f));
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(GolemAtlasPath);
            var expectedWidth = CellSize * MaxFrames * Directions.Length;
            var expectedHeight = CellSize * GolemStates.Length;
            if (texture == null || texture.width != expectedWidth || texture.height != expectedHeight)
            {
                throw new InvalidOperationException($"Golem atlas must be {expectedWidth}x{expectedHeight}: {GolemAtlasPath}");
            }

            var sprites = AssetDatabase.LoadAllAssetsAtPath(GolemAtlasPath);
            var spriteCount = 0;
            for (var index = 0; index < sprites.Length; index++)
            {
                if (sprites[index] is Sprite)
                {
                    spriteCount++;
                }
            }

            if (spriteCount != 264)
            {
                throw new InvalidOperationException($"Golem atlas requires 264 named sprites but imported {spriteCount}.");
            }
        }

        private static void ConfigureSingleSprite(SingleSpriteSpec spec)
        {
            var path = Root + "/" + spec.RelativePath;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = RequireImporter(path);
            ConfigureCommon(importer, 2048);
            importer.spriteImportMode = SpriteImportMode.Single;
            SetPivot(importer, spec.BottomAligned ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f));
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (texture == null || texture.width != CellSize || texture.height != CellSize || sprite == null)
            {
                throw new InvalidOperationException($"M2 single sprite must import as one {CellSize}x{CellSize} sprite: {path}");
            }
        }

        private static TextureImporter RequireImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"M2 image is missing or has no TextureImporter: {path}");
            }

            return importer;
        }

        private static void ConfigureCommon(TextureImporter importer, int maximumSize)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = CellSize;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maximumSize;
        }

        private static void SetPivot(TextureImporter importer, Vector2 pivot)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
        }

        private readonly struct DirectionSpec
        {
            public DirectionSpec(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        private readonly struct StateSpec
        {
            public StateSpec(string name, int frameCount)
            {
                Name = name;
                FrameCount = frameCount;
            }

            public string Name { get; }
            public int FrameCount { get; }
        }

        private readonly struct SingleSpriteSpec
        {
            public SingleSpriteSpec(string relativePath, bool bottomAligned)
            {
                RelativePath = relativePath;
                BottomAligned = bottomAligned;
            }

            public string RelativePath { get; }
            public bool BottomAligned { get; }
        }
    }
}
