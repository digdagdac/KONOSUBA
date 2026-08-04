using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Applies the import contract for the title key visual once the image exists. The art is
    /// not produced yet, so this is a no-op until the file is dropped in, and it fails loudly
    /// rather than silently accepting an image of the wrong size.
    /// </summary>
    /// <remarks>
    /// Contract and generation instructions: <c>Docs/Submission/TITLE_ART_SPEC_KO.md</c>.
    /// </remarks>
    public static class TitleArtBootstrap
    {
        public const int RequiredWidth = 1920;
        public const int RequiredHeight = 1080;

        [MenuItem("Overbless/Contest/Import Title Key Visual")]
        public static void Import()
        {
            if (!TryImport(out var message))
            {
                Debug.Log(message);
                return;
            }

            Debug.Log(message);
        }

        // Intended for -executeMethod in Unity batch mode.
        public static void ImportForBatchMode()
        {
            Import();
        }

        /// <summary>
        /// Returns false when the key visual is absent, which is the expected state until the
        /// art is delivered. Throws when the file exists but breaks the contract.
        /// </summary>
        public static bool TryImport(out string message)
        {
            var assetPath = M1ContentBootstrap.TitleKeyVisualPath;
            if (!File.Exists(Path.GetFullPath(assetPath)))
            {
                message =
                    $"Title key visual '{assetPath}' is not present. The title screen keeps its representative plate. " +
                    "See Docs/Submission/TITLE_ART_SPEC_KO.md.";
                return false;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"'{assetPath}' did not import as a texture.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = RequiredWidth;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                throw new InvalidOperationException($"'{assetPath}' did not load after import.");
            }

            if (texture.width != RequiredWidth || texture.height != RequiredHeight)
            {
                throw new InvalidOperationException(
                    $"Title key visual must be exactly {RequiredWidth}x{RequiredHeight}, but '{assetPath}' is {texture.width}x{texture.height}.");
            }

            if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
            {
                throw new InvalidOperationException($"'{assetPath}' must import as a single Sprite.");
            }

            message =
                $"Imported the title key visual '{assetPath}'. Re-run " +
                "M1ContentBootstrap.CreateFlowScreensForBatchMode to bind it, then rebuild the submission player.";
            return true;
        }
    }
}
