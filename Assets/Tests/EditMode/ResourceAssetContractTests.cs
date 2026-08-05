using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEditor;
using UnityEngine;

namespace Overbless.Tests.EditMode
{
    public sealed class ResourceAssetContractTests
    {
        private const int CellSize = 128;
        private const string FrameLetters = "abcdefgh";
        private const string ArtRoot = "Assets/_Project/Art";
        private const string ManifestPath = "Docs/AI_Usage/asset_manifest.csv";
        private const string AudioRoot = "Assets/_Project/Audio/M1Functional";
        private const string AudioCatalogPath = "Assets/_Project/Data/Audio/FunctionalAudioCatalog.asset";
        private const string PlayerAtlasPath =
            "Assets/_Project/Art/M1Production/Characters/Animation/chr_player_animation_atlas_v001.png";
        private const string PlayerAnimationSetPath =
            "Assets/_Project/Data/Animations/PlayerDirectionalAnimationSet.asset";

        private static readonly DirectionSpec[] Directions =
        {
            new DirectionSpec(CharacterDirection.South, "south"),
            new DirectionSpec(CharacterDirection.North, "north"),
            new DirectionSpec(CharacterDirection.East, "east"),
            new DirectionSpec(CharacterDirection.West, "west"),
            new DirectionSpec(CharacterDirection.SouthEast, "southeast"),
            new DirectionSpec(CharacterDirection.SouthWest, "southwest"),
            new DirectionSpec(CharacterDirection.NorthEast, "northeast"),
            new DirectionSpec(CharacterDirection.NorthWest, "northwest")
        };

        private static readonly PlayerStateSpec[] PlayerStates =
        {
            new PlayerStateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new PlayerStateSpec(CharacterAnimationState.Walk, "move", 6, 10f, true),
            new PlayerStateSpec(CharacterAnimationState.Dash, "dash", 4, 14f, false),
            new PlayerStateSpec(CharacterAnimationState.BlessCast, "bless_cast", 6, 8f, true),
            new PlayerStateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new PlayerStateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        [Test]
        public void ProjectArtTexturesKeepStructuralImportAndManifestContracts()
        {
            var manifest = File.ReadAllText(ResolveProjectPath(ManifestPath), Encoding.UTF8);
            var artFiles = Directory.GetFiles(ResolveProjectPath(ArtRoot), "*.png", SearchOption.AllDirectories);
            Assert.That(artFiles.Length, Is.GreaterThan(0));

            for (var fileIndex = 0; fileIndex < artFiles.Length; fileIndex++)
            {
                var assetPath = ToProjectRelativePath(artFiles[fileIndex]);
                Assert.That(File.Exists(artFiles[fileIndex] + ".meta"), Is.True, $"Art metadata is missing: {assetPath}.meta");
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, $"Art asset has no TextureImporter: {assetPath}");
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), assetPath);
                Assert.That(importer.mipmapEnabled, Is.False, assetPath);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), assetPath);
                Assert.That(importer.crunchedCompression, Is.False, assetPath);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), assetPath);

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                Assert.That(texture, Is.Not.Null, $"Art texture cannot be loaded: {assetPath}");
                var isTitle = string.Equals(
                    assetPath,
                    "Assets/_Project/Art/M1Production/UI/ui_title_key_visual_a_v001.png",
                    StringComparison.Ordinal);
                if (isTitle)
                {
                    Assert.That(texture.width, Is.EqualTo(1920));
                    Assert.That(texture.height, Is.EqualTo(1080));
                    Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(1920f));
                    Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
                }
                else
                {
                    Assert.That(texture.width % CellSize, Is.EqualTo(0), assetPath);
                    Assert.That(texture.height % CellSize, Is.EqualTo(0), assetPath);
                    Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f), assetPath);
                    Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), assetPath);
                }

                if (!IsManifestExempt(assetPath))
                {
                    Assert.That(manifest.IndexOf(assetPath, StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                        $"Production or preproduction art has no provenance manifest entry: {assetPath}");
                }
            }

            var manifestArtPaths = Regex.Matches(manifest, @"Assets/_Project/Art/[A-Za-z0-9_./-]+\.png");
            Assert.That(manifestArtPaths.Count, Is.GreaterThan(0));
            for (var matchIndex = 0; matchIndex < manifestArtPaths.Count; matchIndex++)
            {
                var assetPath = manifestArtPaths[matchIndex].Value;
                Assert.That(File.Exists(ResolveProjectPath(assetPath)), Is.True,
                    $"The asset manifest points to a missing art file: {assetPath}");
                Assert.That(File.Exists(ResolveProjectPath(assetPath) + ".meta"), Is.True,
                    $"The asset manifest points to art without Unity metadata: {assetPath}");
            }
        }

        [Test]
        public void FunctionalAudioCatalogKeepsEveryEventFileAndManifestHashLinked()
        {
            var manifest = File.ReadAllText(ResolveProjectPath(ManifestPath), Encoding.UTF8);
            var catalog = AssetDatabase.LoadAssetAtPath<FunctionalAudioCatalog>(AudioCatalogPath);
            Assert.That(catalog, Is.Not.Null);

            var expectedEvents = (FunctionalAudioEvent[])Enum.GetValues(typeof(FunctionalAudioEvent));
            var audioFiles = Directory.GetFiles(ResolveProjectPath(AudioRoot), "*.wav", SearchOption.TopDirectoryOnly);
            Assert.That(audioFiles.Length, Is.EqualTo(expectedEvents.Length));
            for (var eventIndex = 0; eventIndex < expectedEvents.Length; eventIndex++)
            {
                var eventType = expectedEvents[eventIndex];
                var assetPath = AudioRoot + "/" + eventType + ".wav";
                Assert.That(File.Exists(ResolveProjectPath(assetPath)), Is.True, $"Audio event file is missing: {eventType}");
                Assert.That(File.Exists(ResolveProjectPath(assetPath) + ".meta"), Is.True, $"Audio event metadata is missing: {eventType}");
                Assert.That(manifest.IndexOf(assetPath, StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                    $"Audio event has no provenance manifest entry: {eventType}");
                Assert.That(manifest.IndexOf(ComputeSha256(ResolveProjectPath(assetPath)), StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                    $"Audio event manifest hash drifted: {eventType}");

                var clip = catalog.GetRequired(eventType);
                Assert.That(clip, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(assetPath),
                    $"Audio catalog maps {eventType} to the wrong clip.");
            }
        }

        [Test]
        public void PlayerAnimationSetKeepsEveryIndexedFrameAndRuntimeClipBinding()
        {
            var importer = AssetImporter.GetAtPath(PlayerAtlasPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.mipmapEnabled, Is.False);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PlayerAtlasPath);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(CellSize * 6 * Directions.Length));
            Assert.That(texture.height, Is.EqualTo(CellSize * PlayerStates.Length));

            var spritesByName = GetSpritesByName(PlayerAtlasPath);
            Assert.That(spritesByName.Count, Is.EqualTo(232));
            var animationSet = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(PlayerAnimationSetPath);
            Assert.That(animationSet, Is.Not.Null);
            Assert.That(animationSet.Role, Is.EqualTo("player"));
            Assert.That(animationSet.ClipCount, Is.EqualTo(PlayerStates.Length * Directions.Length));
            animationSet.Validate();

            var expectedSpriteCount = 0;
            for (var stateIndex = 0; stateIndex < PlayerStates.Length; stateIndex++)
            {
                var state = PlayerStates[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var clip = animationSet.GetClip(state.State, direction.Direction);
                    Assert.That(clip.FrameCount, Is.EqualTo(state.FrameCount));
                    Assert.That(clip.FramesPerSecond, Is.EqualTo(state.FramesPerSecond).Within(0.0001f));
                    Assert.That(clip.Loop, Is.EqualTo(state.Loop));
                    for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                    {
                        var frameName = $"chr_player_{state.Name}_{direction.Name}_{FrameLetters[frameIndex]}_v001";
                        Assert.That(spritesByName.TryGetValue(frameName, out var sprite), Is.True,
                            $"Player atlas is missing {frameName}.");
                        Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(PlayerAtlasPath));
                        Assert.That(sprite.rect.x, Is.EqualTo((directionIndex * 6 + frameIndex) * CellSize));
                        Assert.That(sprite.rect.y, Is.EqualTo((PlayerStates.Length - stateIndex - 1) * CellSize));
                        Assert.That(sprite.rect.width, Is.EqualTo(CellSize));
                        Assert.That(sprite.rect.height, Is.EqualTo(CellSize));
                        Assert.That(Vector2.Distance(sprite.pivot, new Vector2(64f, 0f)), Is.LessThanOrEqualTo(0.0001f));
                        Assert.That(clip.GetFrame(frameIndex), Is.SameAs(sprite),
                            $"Player clip {state.Name}/{direction.Name} references a stale or wrong sprite.");
                        expectedSpriteCount++;
                    }
                }
            }

            Assert.That(expectedSpriteCount, Is.EqualTo(spritesByName.Count));
        }

        private static bool IsManifestExempt(string assetPath)
        {
            return assetPath.StartsWith("Assets/_Project/Art/M1Representative/", StringComparison.Ordinal) ||
                   assetPath.StartsWith("Assets/_Project/Art/M1Production/Characters/Animation/Motions/", StringComparison.Ordinal);
        }

        private static Dictionary<string, Sprite> GetSpritesByName(string assetPath)
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
            {
                var sprite = assets[assetIndex] as Sprite;
                if (sprite == null)
                {
                    continue;
                }

                Assert.That(result.ContainsKey(sprite.name), Is.False, $"Sprite source repeats '{sprite.name}': {assetPath}");
                result.Add(sprite.name, sprite);
            }

            return result;
        }

        private static string ResolveProjectPath(string projectRelativePath)
        {
            Assert.That(Path.IsPathRooted(projectRelativePath), Is.False);
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var fullPath = Path.GetFullPath(Path.Combine(root, projectRelativePath));
            Assert.That(fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            return fullPath;
        }

        private static string ToProjectRelativePath(string fullPath)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) + Path.DirectorySeparatorChar;
            var normalized = Path.GetFullPath(fullPath);
            Assert.That(normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
            return normalized.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var bytes = sha256.ComputeHash(stream);
                var builder = new StringBuilder(bytes.Length * 2);
                for (var index = 0; index < bytes.Length; index++)
                {
                    builder.Append(bytes[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private readonly struct DirectionSpec
        {
            public DirectionSpec(CharacterDirection direction, string name)
            {
                Direction = direction;
                Name = name;
            }

            public CharacterDirection Direction { get; }
            public string Name { get; }
        }

        private readonly struct PlayerStateSpec
        {
            public PlayerStateSpec(CharacterAnimationState state, string name, int frameCount, float framesPerSecond, bool loop)
            {
                State = state;
                Name = name;
                FrameCount = frameCount;
                FramesPerSecond = framesPerSecond;
                Loop = loop;
            }

            public CharacterAnimationState State { get; }
            public string Name { get; }
            public int FrameCount { get; }
            public float FramesPerSecond { get; }
            public bool Loop { get; }
        }
    }
}
