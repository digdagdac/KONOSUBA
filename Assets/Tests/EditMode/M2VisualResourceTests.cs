using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Overbless.Editor.Evidence;
using UnityEditor;
using UnityEngine;

namespace Overbless.Tests.EditMode
{
    public sealed class M2VisualResourceTests
    {
        private const string RuntimeVisualIndexPath =
            "Docs/AI_Usage/generations/m2_runtime_visual_index_v002.json";
        private const string M1AnimationIndexPath =
            "Docs/AI_Usage/generations/m1_directional_animation_index_v001.json";
        private const string M2ProductionRoot = "Assets/_Project/Art/M2Production";
        private const string M2PreproductionRoot = "Assets/_Project/Art/M2Preproduction";
        private const string M1GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";

        private static readonly RuntimeVisualSpec[] RuntimeVisuals =
        {
            new RuntimeVisualSpec(
                "static_world_pillar",
                "Assets/_Project/Art/M2Production/Environment/env_static_world_pillar_south_a_v002.png",
                new Vector2(0.5f, 0f)),
            new RuntimeVisualSpec(
                "echo_bless_icon",
                "Assets/_Project/Art/M2Production/UI/ui_icon_bless_echo_a_v002.png",
                new Vector2(0.5f, 0.5f)),
            new RuntimeVisualSpec(
                "echo_status_icon",
                "Assets/_Project/Art/M2Production/UI/ui_icon_echo_status_a_v002.png",
                new Vector2(0.5f, 0.5f)),
            new RuntimeVisualSpec(
                "echo_double_silhouette",
                "Assets/_Project/Art/M2Production/VFX/vfx_echo_double_silhouette_a_v002.png",
                new Vector2(0.5f, 0.5f)),
            new RuntimeVisualSpec(
                "echo_line_telegraph",
                "Assets/_Project/Art/M2Production/VFX/vfx_echo_line_telegraph_a_v002.png",
                new Vector2(0.5f, 0.5f))
        };

        private static readonly string[] M1PrefabPaths =
        {
            "Assets/_Project/Prefabs/M1/Player.prefab",
            "Assets/_Project/Prefabs/M1/Dasher.prefab",
            "Assets/_Project/Prefabs/M1/Archer.prefab",
            "Assets/_Project/Prefabs/M1/Minion.prefab",
            "Assets/_Project/Prefabs/M1/SoulFragment.prefab",
            "Assets/_Project/Prefabs/M1/ExitGate.prefab"
        };

        private static readonly string[] M2BindingAssetPaths =
        {
            "Assets/_Project/Scenes/Room_02.unity",
            "Assets/_Project/Scenes/Room_03.unity",
            "Assets/_Project/Prefabs/M2/Player.prefab",
            "Assets/_Project/Prefabs/M2/Dasher.prefab",
            "Assets/_Project/Prefabs/M2/Archer.prefab",
            "Assets/_Project/Prefabs/M2/Minion.prefab",
            "Assets/_Project/Prefabs/M2/SoulFragment.prefab",
            "Assets/_Project/Prefabs/M2/ExitGate.prefab",
            "Assets/_Project/Prefabs/M2/WorldPillar.prefab"
        };

        private static readonly string[] ExcludedM2BindingTokens =
        {
            "golem",
            "cliff",
            "trap",
            "destructible",
            "resonance",
            "Room_Final",
            "final_objective",
            "echo_ring",
            "echo_apply"
        };

        [Test]
        public void RuntimeVisualIndex_ListsExactlyFiveAuthorizedBinaryRgbaOutputs()
        {
            var index = ReadJsonDocument(RuntimeVisualIndexPath);
            Assert.That(GetRequiredString(index, "schema"), Is.EqualTo("overbless.m2-runtime-visual-index/v2"));
            Assert.That(GetRequiredString(index, "version"), Is.EqualTo("v002"));
            Assert.That(GetRequiredString(index, "runtime_authorization"), Is.EqualTo("local-unsealed-only"));
            Assert.That(GetRequiredString(index, "m2_entry_gate_status"), Is.EqualTo("not-evaluated"));
            Assert.That(
                FindProperty(index, "m2_entry_gate_claim"),
                Is.Null,
                "The local resource index must not claim an M2 entry-gate result.");

            var expectedPaths = GetRuntimeVisualPaths();
            CollectionAssert.AreEquivalent(
                expectedPaths,
                GetStringArray(GetRequiredProperty(index, "declared_output_paths"), "declared_output_paths"));

            var productionFiles = Directory.GetFiles(
                ResolveProjectPath(M2ProductionRoot),
                "*",
                SearchOption.AllDirectories);
            var productionPaths = new List<string>();
            for (var indexFile = 0; indexFile < productionFiles.Length; indexFile++)
            {
                if (string.Equals(Path.GetExtension(productionFiles[indexFile]), ".png", StringComparison.OrdinalIgnoreCase))
                {
                    productionPaths.Add(ToProjectRelativePath(productionFiles[indexFile]));
                }
            }

            CollectionAssert.AreEquivalent(
                expectedPaths,
                productionPaths,
                "M2Production must contain exactly the five approved runtime PNG outputs.");

            var entries = GetRequiredArray(index, "sprites");
            Assert.That(entries.Count, Is.EqualTo(RuntimeVisuals.Length));
            var entriesByPath = new Dictionary<string, CanonicalJsonValue>(StringComparer.Ordinal);
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                var outputPath = GetRequiredString(entry, "output_path");
                Assert.That(
                    entriesByPath.ContainsKey(outputPath),
                    Is.False,
                    $"Runtime visual index contains duplicate output path '{outputPath}'.");
                entriesByPath.Add(outputPath, entry);
            }

            for (var visualIndex = 0; visualIndex < RuntimeVisuals.Length; visualIndex++)
            {
                var visual = RuntimeVisuals[visualIndex];
                Assert.That(
                    entriesByPath.TryGetValue(visual.AssetPath, out var entry),
                    Is.True,
                    $"Runtime visual index is missing '{visual.AssetPath}'.");
                Assert.That(GetRequiredString(entry, "name"), Is.EqualTo(visual.Name));
                Assert.That(GetRequiredString(entry, "runtime_authorization"), Is.EqualTo("local-unsealed-only"));
                Assert.That(GetRequiredString(entry, "alpha"), Is.EqualTo("binary"));
                AssertIntArray(entry, "size", new[] { 128, 128 });

                var filePath = ResolveProjectPath(visual.AssetPath);
                Assert.That(File.Exists(filePath), Is.True, $"Runtime visual is missing: {visual.AssetPath}");
                Assert.That(
                    ComputeSha256(filePath),
                    Is.EqualTo(GetRequiredString(entry, "output_sha256")),
                    $"Runtime visual bytes drifted from its recorded SHA-256: {visual.AssetPath}");

                var opaqueBounds = ValidateStaticBinaryRgbaPng(filePath, visual.AssetPath);
                AssertIntArray(entry, "opaque_bounds", opaqueBounds);
                Assert.That(GetRequiredInteger(entry, "opaque_foot_y"), Is.EqualTo(opaqueBounds[3]));
            }
        }

        [Test]
        public void RuntimeVisualImportSettingsAndBindingsRemainContained()
        {
            for (var visualIndex = 0; visualIndex < RuntimeVisuals.Length; visualIndex++)
            {
                AssertRuntimeVisualImporter(RuntimeVisuals[visualIndex]);
            }

            AssertM1AssetsAreIsolatedFromM2Visuals();
            AssertM2BindingsContainOnlyApprovedRuntimeVisuals();
            AssertM2ScenesReferenceDistinctM2PrefabGuids();
        }

        [Test]
        public void M1AnimationAtlases_KeepRecordedBytesAndImportedSpriteTopology()
        {
            var index = ReadJsonDocument(M1AnimationIndexPath);
            var characters = GetRequiredArray(index, "characters");
            Assert.That(characters.Count, Is.EqualTo(4));

            var recordedAtlases = new Dictionary<string, M1AnimationAtlasRecord>(StringComparer.Ordinal);
            for (var characterIndex = 0; characterIndex < characters.Count; characterIndex++)
            {
                var character = characters[characterIndex];
                var atlasPath = GetRequiredString(character, "atlas_file");
                var expectedHash = GetRequiredString(character, "atlas_sha256");
                Assert.That(
                    recordedAtlases.ContainsKey(atlasPath),
                    Is.False,
                    $"M1 animation index repeats atlas '{atlasPath}'.");
                recordedAtlases.Add(
                    atlasPath,
                    new M1AnimationAtlasRecord(
                        atlasPath,
                        expectedHash,
                        CountIndexedAnimationFrames(character)));
            }

            var atlasFiles = Directory.GetFiles(
                ResolveProjectPath("Assets/_Project/Art/M1Production/Characters/Animation"),
                "*_animation_atlas_v001.png",
                SearchOption.TopDirectoryOnly);
            var atlasPaths = new List<string>();
            for (var atlasIndex = 0; atlasIndex < atlasFiles.Length; atlasIndex++)
            {
                atlasPaths.Add(ToProjectRelativePath(atlasFiles[atlasIndex]));
            }

            CollectionAssert.AreEquivalent(recordedAtlases.Keys, atlasPaths);

            var totalSpriteCount = 0;
            foreach (var record in recordedAtlases.Values)
            {
                var filePath = ResolveProjectPath(record.AssetPath);
                Assert.That(File.Exists(filePath + ".meta"), Is.True, $"M1 atlas metadata is missing: {record.AssetPath}.meta");
                Assert.That(
                    ComputeSha256(filePath),
                    Is.EqualTo(record.Sha256),
                    $"M1 animation atlas bytes changed: {record.AssetPath}");
                Assert.That(
                    AssetDatabase.AssetPathToGUID(record.AssetPath),
                    Is.Not.Empty,
                    $"M1 animation atlas has no stable asset GUID: {record.AssetPath}");

                var importer = AssetImporter.GetAtPath(record.AssetPath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, $"M1 animation atlas has no TextureImporter: {record.AssetPath}");
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
                Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f));
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(importer.mipmapEnabled, Is.False);
                Assert.That(importer.streamingMipmaps, Is.False);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(importer.isReadable, Is.False);

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom));
                Assert.That(Vector2.Distance(settings.spritePivot, new Vector2(0.5f, 0f)), Is.LessThanOrEqualTo(0.0001f));

                var assets = AssetDatabase.LoadAllAssetsAtPath(record.AssetPath);
                var spriteCount = 0;
                for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    if (assets[assetIndex] is Sprite)
                    {
                        spriteCount++;
                    }
                }

                Assert.That(
                    spriteCount,
                    Is.EqualTo(record.SpriteCount),
                    $"M1 atlas sprite topology drifted: {record.AssetPath}");
                totalSpriteCount += spriteCount;
            }

            Assert.That(totalSpriteCount, Is.EqualTo(944), "The authoritative M1 atlas frame topology must remain unchanged.");
        }

        private static void AssertRuntimeVisualImporter(RuntimeVisualSpec visual)
        {
            var importer = AssetImporter.GetAtPath(visual.AssetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"Runtime visual has no TextureImporter: {visual.AssetPath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.streamingMipmaps, Is.False);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.isReadable, Is.False);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom));
            Assert.That(
                Vector2.Distance(settings.spritePivot, visual.Pivot),
                Is.LessThanOrEqualTo(0.0001f),
                $"Runtime visual pivot drifted: {visual.AssetPath}");
        }

        private static void AssertM1AssetsAreIsolatedFromM2Visuals()
        {
            var m2Guids = new List<string>();
            for (var visualIndex = 0; visualIndex < RuntimeVisuals.Length; visualIndex++)
            {
                var assetPath = RuntimeVisuals[visualIndex].AssetPath;
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                Assert.That(guid, Is.Not.Empty, $"Runtime visual has no GUID: {assetPath}");
                m2Guids.Add(guid);
            }

            var m1Assets = new List<string> { M1GuidedScenePath };
            m1Assets.AddRange(M1PrefabPaths);
            for (var assetIndex = 0; assetIndex < m1Assets.Count; assetIndex++)
            {
                var assetPath = m1Assets[assetIndex];
                var text = ReadAssetText(assetPath);
                AssertTextExcludes(text, assetPath, "M2Production");
                AssertTextExcludes(text, assetPath, "M2Preproduction");
                AssertTextExcludes(text, assetPath, "EchoCard");
                AssertTextExcludes(text, assetPath, "EchoProjectilePresenter");
                AssertTextExcludes(text, assetPath, "EchoProjectileVisual");
                for (var guidIndex = 0; guidIndex < m2Guids.Count; guidIndex++)
                {
                    AssertTextExcludes(text, assetPath, m2Guids[guidIndex]);
                }
            }
        }

        private static void AssertM2ScenesReferenceDistinctM2PrefabGuids()
        {
            var m2PrefabPaths = new[]
            {
                "Assets/_Project/Prefabs/M2/Player.prefab",
                "Assets/_Project/Prefabs/M2/Dasher.prefab",
                "Assets/_Project/Prefabs/M2/Archer.prefab",
                "Assets/_Project/Prefabs/M2/Minion.prefab",
                "Assets/_Project/Prefabs/M2/SoulFragment.prefab",
                "Assets/_Project/Prefabs/M2/ExitGate.prefab"
            };
            var room02Text = ReadAssetText("Assets/_Project/Scenes/Room_02.unity");
            var room03Text = ReadAssetText("Assets/_Project/Scenes/Room_03.unity");
            for (var prefabIndex = 0; prefabIndex < m2PrefabPaths.Length; prefabIndex++)
            {
                var m2Guid = AssetDatabase.AssetPathToGUID(m2PrefabPaths[prefabIndex]);
                var m1Guid = AssetDatabase.AssetPathToGUID(M1PrefabPaths[prefabIndex]);
                Assert.That(m2Guid, Is.Not.Empty, $"M2 prefab has no GUID: {m2PrefabPaths[prefabIndex]}");
                Assert.That(m1Guid, Is.Not.Empty, $"M1 prefab has no GUID: {M1PrefabPaths[prefabIndex]}");
                StringAssert.Contains(m2Guid, room02Text, $"Room_02 must instantiate {m2PrefabPaths[prefabIndex]}.");
                StringAssert.Contains(m2Guid, room03Text, $"Room_03 must instantiate {m2PrefabPaths[prefabIndex]}.");
                StringAssert.DoesNotContain(m1Guid, room02Text, $"Room_02 must not instantiate {M1PrefabPaths[prefabIndex]}.");
                StringAssert.DoesNotContain(m1Guid, room03Text, $"Room_03 must not instantiate {M1PrefabPaths[prefabIndex]}.");
            }

            var pillarGuid = AssetDatabase.AssetPathToGUID("Assets/_Project/Prefabs/M2/WorldPillar.prefab");
            Assert.That(pillarGuid, Is.Not.Empty);
            StringAssert.DoesNotContain(pillarGuid, room02Text, "Room_02 must remain pillar-free.");
            StringAssert.Contains(pillarGuid, room03Text, "Room_03 must instantiate the M2 WorldPillar prefab.");
        }

        private static void AssertM2BindingsContainOnlyApprovedRuntimeVisuals()
        {
            for (var assetIndex = 0; assetIndex < M2BindingAssetPaths.Length; assetIndex++)
            {
                var assetPath = M2BindingAssetPaths[assetIndex];
                Assert.That(File.Exists(ResolveProjectPath(assetPath)), Is.True, $"M2 binding asset is missing: {assetPath}");
                var text = ReadAssetText(assetPath);
                AssertTextExcludes(text, assetPath, "M2Preproduction");
                for (var tokenIndex = 0; tokenIndex < ExcludedM2BindingTokens.Length; tokenIndex++)
                {
                    AssertTextExcludes(text, assetPath, ExcludedM2BindingTokens[tokenIndex]);
                }
            }

            var dependencies = AssetDatabase.GetDependencies(M2BindingAssetPaths, true);
            var productionDependencies = new HashSet<string>(StringComparer.Ordinal);
            var preproductionDependencies = new List<string>();
            for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
            {
                var dependency = dependencies[dependencyIndex];
                if (dependency.StartsWith(M2ProductionRoot + "/", StringComparison.Ordinal))
                {
                    productionDependencies.Add(dependency);
                }

                if (dependency.StartsWith(M2PreproductionRoot + "/", StringComparison.Ordinal))
                {
                    preproductionDependencies.Add(dependency);
                }
            }

            CollectionAssert.AreEquivalent(
                GetRuntimeVisualPaths(),
                productionDependencies,
                "M2 scenes and prefabs may bind only the five approved v002 runtime visuals.");
            Assert.That(
                preproductionDependencies,
                Is.Empty,
                "M2 runtime scenes and prefabs must not bind M2Preproduction resources.");
        }

        private static int[] ValidateStaticBinaryRgbaPng(string filePath, string assetPath)
        {
            var bytes = File.ReadAllBytes(filePath);
            Assert.That(bytes.Length, Is.GreaterThan(33), $"PNG is too short: {assetPath}");
            var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (var signatureIndex = 0; signatureIndex < signature.Length; signatureIndex++)
            {
                Assert.That(bytes[signatureIndex], Is.EqualTo(signature[signatureIndex]), $"PNG signature is invalid: {assetPath}");
            }

            var offset = signature.Length;
            var sawHeader = false;
            var sawEnd = false;
            while (offset < bytes.Length)
            {
                Assert.That(bytes.Length - offset, Is.GreaterThanOrEqualTo(12), $"PNG chunk is truncated: {assetPath}");
                var chunkLength = ReadBigEndianInt32(bytes, offset, assetPath);
                var chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                var nextOffsetLong = (long)offset + 12 + chunkLength;
                Assert.That(nextOffsetLong, Is.LessThanOrEqualTo(bytes.Length), $"PNG chunk exceeds file length: {assetPath}");
                var nextOffset = (int)nextOffsetLong;

                if (chunkType == "IHDR")
                {
                    Assert.That(sawHeader, Is.False, $"PNG has multiple IHDR chunks: {assetPath}");
                    Assert.That(offset, Is.EqualTo(signature.Length), $"IHDR must be the first PNG chunk: {assetPath}");
                    Assert.That(chunkLength, Is.EqualTo(13), $"PNG IHDR length is invalid: {assetPath}");
                    Assert.That(ReadBigEndianInt32(bytes, offset + 8, assetPath), Is.EqualTo(128));
                    Assert.That(ReadBigEndianInt32(bytes, offset + 12, assetPath), Is.EqualTo(128));
                    Assert.That(bytes[offset + 16], Is.EqualTo(8), $"PNG bit depth must be 8: {assetPath}");
                    Assert.That(bytes[offset + 17], Is.EqualTo(6), $"PNG color type must be RGBA: {assetPath}");
                    sawHeader = true;
                }
                else if (chunkType == "acTL")
                {
                    Assert.Fail($"Runtime visual PNG must be static, not animated: {assetPath}");
                }
                else if (chunkType == "IEND")
                {
                    Assert.That(sawHeader, Is.True, $"PNG ended before IHDR: {assetPath}");
                    Assert.That(chunkLength, Is.Zero, $"PNG IEND chunk must be empty: {assetPath}");
                    sawEnd = true;
                    offset = nextOffset;
                    break;
                }

                offset = nextOffset;
            }

            Assert.That(sawHeader, Is.True, $"PNG lacks an IHDR chunk: {assetPath}");
            Assert.That(sawEnd, Is.True, $"PNG lacks an IEND chunk: {assetPath}");
            Assert.That(offset, Is.EqualTo(bytes.Length), $"PNG contains bytes after IEND: {assetPath}");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, bytes, false), Is.True, $"Unity cannot decode PNG: {assetPath}");
                Assert.That(texture.width, Is.EqualTo(128));
                Assert.That(texture.height, Is.EqualTo(128));

                var pixels = texture.GetPixels32();
                var minX = texture.width;
                var minTopY = texture.height;
                var maxX = -1;
                var maxTopY = -1;
                for (var y = 0; y < texture.height; y++)
                {
                    for (var x = 0; x < texture.width; x++)
                    {
                        var alpha = pixels[y * texture.width + x].a;
                        Assert.That(
                            alpha == 0 || alpha == 255,
                            Is.True,
                            $"PNG alpha must be binary: {assetPath} at ({x}, {y}).");
                        if (alpha == 0)
                        {
                            continue;
                        }

                        var topY = texture.height - 1 - y;
                        minX = Math.Min(minX, x);
                        minTopY = Math.Min(minTopY, topY);
                        maxX = Math.Max(maxX, x);
                        maxTopY = Math.Max(maxTopY, topY);
                    }
                }

                Assert.That(maxX, Is.GreaterThanOrEqualTo(0), $"PNG has no opaque pixels: {assetPath}");
                return new[] { minX, minTopY, maxX + 1, maxTopY + 1 };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static int CountIndexedAnimationFrames(CanonicalJsonValue character)
        {
            var states = GetRequiredArray(character, "states");
            var count = 0;
            for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
            {
                var directions = GetRequiredProperty(states[stateIndex], "directions");
                Assert.That(directions.Kind, Is.EqualTo(CanonicalJsonKind.Object));
                for (var directionIndex = 0; directionIndex < directions.Properties.Count; directionIndex++)
                {
                    var frames = directions.Properties[directionIndex].Value;
                    Assert.That(frames.Kind, Is.EqualTo(CanonicalJsonKind.Array));
                    count += frames.Items.Count;
                }
            }

            return count;
        }

        private static CanonicalJsonValue ReadJsonDocument(string assetPath)
        {
            var bytes = File.ReadAllBytes(ResolveProjectPath(assetPath));
            Assert.That(CanonicalJson.TryParseUtf8(bytes, out var document, out var error), Is.True, error);
            Assert.That(document.Kind, Is.EqualTo(CanonicalJsonKind.Object), $"JSON root must be an object: {assetPath}");
            return document;
        }

        private static CanonicalJsonValue GetRequiredProperty(CanonicalJsonValue objectValue, string propertyName)
        {
            Assert.That(objectValue, Is.Not.Null);
            Assert.That(objectValue.Kind, Is.EqualTo(CanonicalJsonKind.Object));
            Assert.That(
                objectValue.TryGetSingleProperty(propertyName, out var property),
                Is.True,
                $"JSON object is missing unique property '{propertyName}'.");
            return property;
        }

        private static CanonicalJsonValue FindProperty(CanonicalJsonValue objectValue, string propertyName)
        {
            Assert.That(objectValue, Is.Not.Null);
            Assert.That(objectValue.Kind, Is.EqualTo(CanonicalJsonKind.Object));
            CanonicalJsonValue result = null;
            for (var propertyIndex = 0; propertyIndex < objectValue.Properties.Count; propertyIndex++)
            {
                var property = objectValue.Properties[propertyIndex];
                if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(result, Is.Null, $"JSON object repeats property '{propertyName}'.");
                result = property.Value;
            }

            return result;
        }

        private static string GetRequiredString(CanonicalJsonValue objectValue, string propertyName)
        {
            var property = GetRequiredProperty(objectValue, propertyName);
            Assert.That(property.Kind, Is.EqualTo(CanonicalJsonKind.String), $"'{propertyName}' must be a string.");
            Assert.That(property.StringValue, Is.Not.Empty, $"'{propertyName}' must not be empty.");
            return property.StringValue;
        }

        private static int GetRequiredInteger(CanonicalJsonValue objectValue, string propertyName)
        {
            var property = GetRequiredProperty(objectValue, propertyName);
            Assert.That(property.Kind, Is.EqualTo(CanonicalJsonKind.Number), $"'{propertyName}' must be a number.");
            Assert.That(property.NumberValue, Is.EqualTo(Math.Round(property.NumberValue)), $"'{propertyName}' must be an integer.");
            Assert.That(property.NumberValue, Is.GreaterThanOrEqualTo(int.MinValue));
            Assert.That(property.NumberValue, Is.LessThanOrEqualTo(int.MaxValue));
            return (int)property.NumberValue;
        }

        private static IReadOnlyList<CanonicalJsonValue> GetRequiredArray(CanonicalJsonValue objectValue, string propertyName)
        {
            var property = GetRequiredProperty(objectValue, propertyName);
            Assert.That(property.Kind, Is.EqualTo(CanonicalJsonKind.Array), $"'{propertyName}' must be an array.");
            return property.Items;
        }

        private static List<string> GetStringArray(CanonicalJsonValue value, string propertyName)
        {
            Assert.That(value.Kind, Is.EqualTo(CanonicalJsonKind.Array), $"'{propertyName}' must be an array.");
            var values = new List<string>();
            for (var index = 0; index < value.Items.Count; index++)
            {
                var item = value.Items[index];
                Assert.That(item.Kind, Is.EqualTo(CanonicalJsonKind.String), $"'{propertyName}' must contain strings.");
                Assert.That(item.StringValue, Is.Not.Empty, $"'{propertyName}' contains an empty path.");
                values.Add(item.StringValue);
            }

            return values;
        }

        private static void AssertIntArray(CanonicalJsonValue objectValue, string propertyName, IReadOnlyList<int> expected)
        {
            var values = GetRequiredArray(objectValue, propertyName);
            Assert.That(values.Count, Is.EqualTo(expected.Count), $"'{propertyName}' has the wrong item count.");
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.That(values[index].Kind, Is.EqualTo(CanonicalJsonKind.Number), $"'{propertyName}' must contain numbers.");
                Assert.That(values[index].NumberValue, Is.EqualTo(expected[index]), $"'{propertyName}' differs at index {index}.");
            }
        }

        private static void AssertTextExcludes(string text, string assetPath, string excludedValue)
        {
            Assert.That(
                text.IndexOf(excludedValue, StringComparison.OrdinalIgnoreCase),
                Is.LessThan(0),
                $"'{assetPath}' contains excluded M2 binding text '{excludedValue}'.");
        }

        private static string ReadAssetText(string assetPath)
        {
            var fullPath = ResolveProjectPath(assetPath);
            Assert.That(File.Exists(fullPath), Is.True, $"Asset is missing: {assetPath}");
            return File.ReadAllText(fullPath, Encoding.UTF8);
        }

        private static string[] GetRuntimeVisualPaths()
        {
            var paths = new string[RuntimeVisuals.Length];
            for (var index = 0; index < RuntimeVisuals.Length; index++)
            {
                paths[index] = RuntimeVisuals[index].AssetPath;
            }

            return paths;
        }

        private static string ResolveProjectPath(string projectRelativePath)
        {
            Assert.That(projectRelativePath, Is.Not.Empty);
            Assert.That(Path.IsPathRooted(projectRelativePath), Is.False, $"Path must be project-relative: {projectRelativePath}");
            var root = ProjectRoot;
            var fullPath = Path.GetFullPath(Path.Combine(root, projectRelativePath));
            Assert.That(
                fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                Is.True,
                $"Path escapes the project root: {projectRelativePath}");
            return fullPath;
        }

        private static string ToProjectRelativePath(string fullPath)
        {
            var root = ProjectRoot + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(fullPath);
            Assert.That(
                normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase),
                Is.True,
                $"File is outside the project root: {fullPath}");
            return normalizedPath.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }

        private static string ComputeSha256(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                return CanonicalJson.Sha256Hex(stream, out _);
            }
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset, string assetPath)
        {
            Assert.That(bytes.Length - offset, Is.GreaterThanOrEqualTo(4), $"PNG integer is truncated: {assetPath}");
            var value = ((long)bytes[offset] << 24) |
                        ((long)bytes[offset + 1] << 16) |
                        ((long)bytes[offset + 2] << 8) |
                        bytes[offset + 3];
            Assert.That(value, Is.LessThanOrEqualTo(int.MaxValue), $"PNG chunk length is too large: {assetPath}");
            return (int)value;
        }

        private readonly struct RuntimeVisualSpec
        {
            public RuntimeVisualSpec(string name, string assetPath, Vector2 pivot)
            {
                Name = name;
                AssetPath = assetPath;
                Pivot = pivot;
            }

            public string Name { get; }
            public string AssetPath { get; }
            public Vector2 Pivot { get; }
        }

        private readonly struct M1AnimationAtlasRecord
        {
            public M1AnimationAtlasRecord(string assetPath, string sha256, int spriteCount)
            {
                AssetPath = assetPath;
                Sha256 = sha256;
                SpriteCount = spriteCount;
            }

            public string AssetPath { get; }
            public string Sha256 { get; }
            public int SpriteCount { get; }
        }
    }
}
