using System;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Imports only the local-unsealed M2 runtime visual package. This does not create an M2 entry-gate decision.
    /// </summary>
    public static class M2RuntimeVisualBootstrap
    {
        private const int SpriteSize = 128;
        private const string Root = "Assets/_Project/Art/M2Production";

        private static readonly RuntimeSpriteSpec[] RuntimeSprites =
        {
            new RuntimeSpriteSpec("Environment/env_static_world_pillar_south_a_v002.png", new Vector2(0.5f, 0f)),
            new RuntimeSpriteSpec("UI/ui_icon_bless_echo_a_v002.png", new Vector2(0.5f, 0.5f)),
            new RuntimeSpriteSpec("UI/ui_icon_echo_status_a_v002.png", new Vector2(0.5f, 0.5f)),
            new RuntimeSpriteSpec("VFX/vfx_echo_double_silhouette_a_v002.png", new Vector2(0.5f, 0.5f)),
            new RuntimeSpriteSpec("VFX/vfx_echo_line_telegraph_a_v002.png", new Vector2(0.5f, 0.5f))
        };

        [MenuItem("Overbless/M2 Assets/Import Runtime Visuals (Local Unsealed QA)")]
        public static void Import()
        {
            for (var index = 0; index < RuntimeSprites.Length; index++)
            {
                ConfigureSprite(RuntimeSprites[index]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Imported 5 M2 runtime visuals for local unsealed technical QA. No M2 entry-gate decision is implied.");
        }

        public static void ImportForBatchMode()
        {
            Import();
        }

        private static void ConfigureSprite(RuntimeSpriteSpec spec)
        {
            var path = Root + "/" + spec.RelativePath;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = RequireImporter(path);
            ConfigureCommon(importer);
            importer.spriteImportMode = SpriteImportMode.Single;
            SetPivot(importer, spec.Pivot);
            importer.SaveAndReimport();
            ValidateImportedSprite(path, spec.Pivot);
        }

        private static TextureImporter RequireImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"M2 runtime visual is missing or has no TextureImporter: {path}");
            }

            return importer;
        }

        private static void ConfigureCommon(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = SpriteSize;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = SpriteSize;
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

        private static void ValidateImportedSprite(string path, Vector2 expectedPivot)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || texture.width != SpriteSize || texture.height != SpriteSize)
            {
                throw new InvalidOperationException($"M2 runtime visual must be {SpriteSize}x{SpriteSize}: {path}");
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            Sprite sprite = null;
            var spriteCount = 0;
            for (var index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite importedSprite)
                {
                    sprite = importedSprite;
                    spriteCount++;
                }
            }

            if (spriteCount != 1 || sprite == null || sprite.rect.width != SpriteSize || sprite.rect.height != SpriteSize)
            {
                throw new InvalidOperationException($"M2 runtime visual must import as exactly one {SpriteSize}x{SpriteSize} Sprite: {path}");
            }

            var normalizedPivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            if (normalizedPivot != expectedPivot)
            {
                throw new InvalidOperationException($"M2 runtime visual pivot mismatch: {path}");
            }
        }

        private readonly struct RuntimeSpriteSpec
        {
            public RuntimeSpriteSpec(string relativePath, Vector2 pivot)
            {
                RelativePath = relativePath;
                Pivot = pivot;
            }

            public string RelativePath { get; }
            public Vector2 Pivot { get; }
        }
    }
}
