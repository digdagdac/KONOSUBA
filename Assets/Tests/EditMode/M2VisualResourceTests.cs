using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Overbless.Editor.Evidence;
using Overbless.Runtime;
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
        private const string MonsterAnimationIndexPath =
            "Docs/AI_Usage/generations/monster_directional_animation_index_v002.json";
        private const string MonsterAnimationGenerationPath =
            "Docs/AI_Usage/generations/monster_directional_animations_v002.json";
        private const string ArcherMotionV003IndexPath =
            "Docs/AI_Usage/generations/archer_motion_v003_index.json";
        private const string MonsterAnimationReviewPath =
            "Docs/AI_Usage/edits/monster_directional_animation_review_v002.json";
        private const string MonsterAnimationLiveReviewPath =
            "Docs/AI_Usage/edits/monster_directional_animation_live_review_v002.json";
        private const string MonsterMotionCurationPath =
            "Docs/AI_Usage/edits/monster_motion_cycle_curation_v003.json";
        private const string ArcherAttackExecuteSourceWindowCurationPath =
            "Docs/AI_Usage/edits/archer_attack_execute_source_window_curation_v004.json";
        private const string MonsterAnimationReviewMediaRoot =
            "Docs/AI_Usage/reviews/monster_animation_v002/";
        private const float MonsterAnimationTimingTolerancePercent = 10f;
        private static readonly string[] MonsterAnimationTimingMetrics =
        {
            "chase_to_range",
            "retreat_to_safe_band",
            "preparation_to_judgment",
            "judgment_to_next_eligible_warning_plain",
            "judgment_to_next_judgment_plain",
            "judgment_to_next_eligible_warning_haste",
            "judgment_to_next_judgment_haste"
        };
        private const string MonsterAnimationPromptPath =
            "Docs/AI_Usage/prompts/monster_directional_animation_prompts_v002.json";
        private const string MonsterAnimationSourceRoot =
            "Docs/AI_Usage/sources/monster_animation_v002";
        private const string MonsterAnimationAtlasRoot =
            "Assets/_Project/Art/M1Production/Characters/Animation";

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
        private static readonly MonsterAnimationRoleSpec[] MonsterAnimationRoles =
        {
            new MonsterAnimationRoleSpec(
                "dasher",
                "Assets/_Project/Art/M1Production/Characters/Animation/chr_dasher_animation_atlas_v002.png",
                "Assets/_Project/Data/Animations/DasherDirectionalAnimationSet.asset"),
            new MonsterAnimationRoleSpec(
                "archer",
                null,
                "Assets/_Project/Data/Animations/ArcherDirectionalAnimationSet.asset",
                true),
            new MonsterAnimationRoleSpec(
                "minion",
                "Assets/_Project/Art/M1Production/Characters/Animation/chr_minion_animation_atlas_v002.png",
                "Assets/_Project/Data/Animations/MinionDirectionalAnimationSet.asset")
        };

        private static readonly MonsterAnimationRoleSpec ArcherMotionV003Role =
            new MonsterAnimationRoleSpec(
                "archer",
                null,
                "Assets/_Project/Data/Animations/ArcherDirectionalAnimationSet.asset",
                true,
                "MotionsV003",
                "v003");

        private static readonly MonsterAnimationStateSpec[] MonsterAnimationStates =
        {
            new MonsterAnimationStateSpec("idle", CharacterAnimationState.Idle, 4, 4f, true),
            new MonsterAnimationStateSpec("walk", CharacterAnimationState.Walk, 4, 6f, true),
            new MonsterAnimationStateSpec("run", CharacterAnimationState.Run, 4, 9f, true),
            new MonsterAnimationStateSpec("attack_charge", CharacterAnimationState.AttackCharge, 6, 8f, false),
            new MonsterAnimationStateSpec("attack_execute", CharacterAnimationState.AttackExecute, 6, 14f, false),
            new MonsterAnimationStateSpec("recover", CharacterAnimationState.Recover, 4, 7f, false),
            new MonsterAnimationStateSpec("hit", CharacterAnimationState.Hit, 3, 12f, false),
            new MonsterAnimationStateSpec("death", CharacterAnimationState.Death, 6, 8f, false)
        };

        private static readonly string[] MonsterAnimationDirections =
        {
            "south",
            "north",
            "east",
            "west",
            "southeast",
            "southwest",
            "northeast",
            "northwest"
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
        [Test]
        public void MonsterV002GenerationReviewAndProvenanceStayLinked()
        {
            var requiredPaths = new[]
            {
                MonsterAnimationIndexPath,
                MonsterAnimationGenerationPath,
                MonsterAnimationReviewPath,
                MonsterAnimationPromptPath,
                MonsterMotionCurationPath,
                ArcherAttackExecuteSourceWindowCurationPath
            };
            for (var pathIndex = 0; pathIndex < requiredPaths.Length; pathIndex++)
            {
                var requiredPath = requiredPaths[pathIndex];
                Assert.That(File.Exists(ResolveProjectPath(requiredPath)), Is.True, $"Monster v002 evidence is missing: {requiredPath}");
            }

            var index = ReadJsonDocument(MonsterAnimationIndexPath);
            var generation = ReadJsonDocument(MonsterAnimationGenerationPath);
            var review = ReadJsonDocument(MonsterAnimationReviewPath);
            Assert.That(GetRequiredString(index, "schema"), Is.EqualTo("overbless.monster-directional-animation-index/v2"));
            Assert.That(GetRequiredString(index, "version"), Is.EqualTo("v002"));
            Assert.That(GetRequiredString(generation, "generation_id"), Is.EqualTo("monster_directional_animations_v002"));
            Assert.That(GetRequiredString(review, "review_id"), Is.EqualTo("monster_directional_animation_review_v002"));
            Assert.That(GetRequiredString(generation, "status"), Is.EqualTo("machine-pass-live-review-pending"));
            Assert.That(GetRequiredString(review, "status"), Is.EqualTo("machine-pass-live-review-pending"));
            Assert.That(GetRequiredString(review, "generation_record"), Is.EqualTo(MonsterAnimationGenerationPath));
            Assert.That(GetRequiredString(review, "atlas_index"), Is.EqualTo(MonsterAnimationIndexPath));
            Assert.That(
                GetRequiredString(GetRequiredProperty(generation, "atlas_index"), "sha256"),
                Is.EqualTo(ComputeSha256(ResolveProjectPath(MonsterAnimationIndexPath))));
            Assert.That(
                GetRequiredString(GetRequiredProperty(generation, "prompt_file"), "path"),
                Is.EqualTo(MonsterAnimationPromptPath));
            Assert.That(
                GetRequiredString(GetRequiredProperty(generation, "prompt_file"), "sha256"),
                Is.EqualTo(ComputeSha256(ResolveProjectPath(MonsterAnimationPromptPath))));

            var sourceContract = GetRequiredProperty(index, "source_contract");
            Assert.That(GetRequiredInteger(sourceContract, "source_sheet_count"), Is.EqualTo(15));
            CollectionAssert.AreEqual(
                new[] { "south", "north", "east", "southeast", "northeast" },
                GetStringArray(GetRequiredProperty(sourceContract, "direct_directions"), "direct_directions"));
            AssertIntArray(sourceContract, "source_sheet_size", new[] { 1536, 1024 });
            AssertIntArray(sourceContract, "source_grid", new[] { 8, 5 });
            CollectionAssert.AreEqual(
                new[] { "dasher", "archer", "minion" },
                GetStringArray(GetRequiredProperty(sourceContract, "roles"), "roles"));

            var derivation = GetRequiredProperty(index, "direction_derivation");
            CollectionAssert.AreEqual(
                new[] { "south", "north", "east", "southeast", "northeast" },
                GetStringArray(GetRequiredProperty(derivation, "authored_direct_directions"), "authored_direct_directions"));
            var derivedDirections = GetRequiredProperty(derivation, "derived_directions");
            Assert.That(GetRequiredString(derivedDirections, "west"), Is.EqualTo("east"));
            Assert.That(GetRequiredString(derivedDirections, "southwest"), Is.EqualTo("southeast"));
            Assert.That(GetRequiredString(derivedDirections, "northwest"), Is.EqualTo("northeast"));
            Assert.That(derivedDirections.Properties.Count, Is.EqualTo(3));

            var sourceSheets = GetRequiredArray(index, "source_sheets");
            Assert.That(sourceSheets.Count, Is.EqualTo(15));
            var indexedSources = new Dictionary<string, CanonicalJsonValue>(StringComparer.Ordinal);
            for (var sourceIndex = 0; sourceIndex < sourceSheets.Count; sourceIndex++)
            {
                var source = sourceSheets[sourceIndex];
                var sourcePath = GetRequiredString(source, "path");
                Assert.That(indexedSources.ContainsKey(sourcePath), Is.False, $"Monster v002 source repeats '{sourcePath}'.");
                indexedSources.Add(sourcePath, source);
                Assert.That(File.Exists(ResolveProjectPath(sourcePath)), Is.True, $"Monster v002 source is missing: {sourcePath}");
                Assert.That(
                    ComputeSha256(ResolveProjectPath(sourcePath)),
                    Is.EqualTo(GetRequiredString(source, "sha256")),
                    $"Monster v002 source bytes drifted: {sourcePath}");
                AssertIntArray(source, "size", new[] { 1536, 1024 });
                AssertIntArray(source, "grid", new[] { 8, 5 });
            }
            var directDirections = new[] { "south", "north", "east", "southeast", "northeast" };
            for (var roleIndex = 0; roleIndex < MonsterAnimationRoles.Length; roleIndex++)
            {
                var role = MonsterAnimationRoles[roleIndex];
                for (var directionIndex = 0; directionIndex < directDirections.Length; directionIndex++)
                {
                    var direction = directDirections[directionIndex];
                    var sourcePath =
                        $"{MonsterAnimationSourceRoot}/{role.Role}_{direction}_motion_sheet_source.png";
                    Assert.That(
                        indexedSources.TryGetValue(sourcePath, out var source),
                        Is.True,
                        $"Monster v002 source index is missing '{sourcePath}'.");
                    Assert.That(GetRequiredString(source, "role"), Is.EqualTo(role.Role));
                    Assert.That(GetRequiredString(source, "direction"), Is.EqualTo(direction));
                }
            }

            var sourceFiles = Directory.GetFiles(
                ResolveProjectPath(MonsterAnimationSourceRoot),
                "*.png",
                SearchOption.TopDirectoryOnly);
            var sourcePaths = new List<string>();
            for (var sourceFileIndex = 0; sourceFileIndex < sourceFiles.Length; sourceFileIndex++)
            {
                sourcePaths.Add(ToProjectRelativePath(sourceFiles[sourceFileIndex]));
            }

            CollectionAssert.AreEquivalent(indexedSources.Keys, sourcePaths);
            AssertMonsterGenerationSourcesMatchIndex(generation, indexedSources);
            var curation = ReadJsonDocument(MonsterMotionCurationPath);
            Assert.That(
                GetRequiredString(GetRequiredProperty(generation, "motion_cycle_curation"), "path"),
                Is.EqualTo(MonsterMotionCurationPath));
            Assert.That(GetRequiredString(curation, "schema"), Is.EqualTo("overbless.monster-motion-cycle-curation/v1"));
            Assert.That(GetRequiredString(curation, "revision"), Is.EqualTo("v003"));
            Assert.That(
                GetRequiredString(GetRequiredProperty(curation, "source"), "selectionMethod"),
                Is.EqualTo("deterministic pixel-duplicate audit of the normalized source cells"));
            AssertArcherAttackExecuteSourceWindowCuration(index, generation);
        }

        private static void AssertArcherAttackExecuteSourceWindowCuration(
            CanonicalJsonValue index,
            CanonicalJsonValue generation)
        {
            Assert.That(File.Exists(ResolveProjectPath(ArcherAttackExecuteSourceWindowCurationPath)), Is.True);
            var record = ReadJsonDocument(ArcherAttackExecuteSourceWindowCurationPath);
            Assert.That(GetRequiredString(record, "schema"), Is.EqualTo("overbless.monster-source-window-curation/v1"));
            Assert.That(GetRequiredString(record, "scope"), Is.EqualTo("Archer east and southeast AttackExecute frames a to c only"));
            Assert.That(
                GetRequiredString(GetRequiredProperty(generation, "attack_execute_source_window_curation"), "record"),
                Is.EqualTo(ArcherAttackExecuteSourceWindowCurationPath));

            var overrides = GetRequiredArray(index, "source_window_overrides");
            Assert.That(overrides.Count, Is.EqualTo(6));
            for (var overrideIndex = 0; overrideIndex < overrides.Count; overrideIndex++)
            {
                var sourceWindow = overrides[overrideIndex];
                Assert.That(GetRequiredString(sourceWindow, "role"), Is.EqualTo("archer"));
                Assert.That(
                    GetRequiredString(sourceWindow, "direction"),
                    Is.EqualTo("east").Or.EqualTo("southeast"));
                Assert.That(GetRequiredString(sourceWindow, "state"), Is.EqualTo("attack_execute"));
                var frameIndex = GetRequiredInteger(sourceWindow, "frame");
                Assert.That(frameIndex, Is.InRange(0, 2));
                AssertIntArray(sourceWindow, "source_rect", new[] { 20 + frameIndex * 192, 614, 212 + frameIndex * 192, 819 });
            }
        }

        [Test]
        public void MonsterV002AtlasesKeepIndexedPixelsTopologyImportAndRuntimeClipContracts()
        {
            var index = ReadJsonDocument(MonsterAnimationIndexPath);
            var atlasContract = GetRequiredProperty(index, "atlas_contract");
            AssertIntArray(atlasContract, "cell_size", new[] { 128, 128 });
            AssertIntArray(atlasContract, "monolithic_atlas_size", new[] { 8192, 1024 });
            Assert.That(GetRequiredInteger(atlasContract, "monolithic_max_frames_per_direction"), Is.EqualTo(8));
            CollectionAssert.AreEqual(
                MonsterAnimationDirections,
                GetStringArray(GetRequiredProperty(atlasContract, "directions"), "directions"));
            CollectionAssert.AreEqual(
                new[] { "dasher", "minion" },
                GetStringArray(GetRequiredProperty(atlasContract, "monolithic_atlas_roles"), "monolithic_atlas_roles"));
            CollectionAssert.AreEqual(
                new[] { "archer" },
                GetStringArray(GetRequiredProperty(atlasContract, "motion_sheet_roles"), "motion_sheet_roles"));
            Assert.That(GetRequiredInteger(GetRequiredProperty(atlasContract, "motion_sheet_layout"), "rows"), Is.EqualTo(1));
            AssertMonsterStateContracts(GetRequiredArray(atlasContract, "states"));

            var charactersByRole = GetMonsterCharactersByRole(index);
            var framesByName = GetMonsterFramesByName(index, out var classificationCounts);
            Assert.That(framesByName.Count, Is.EqualTo(888));
            Assert.That(classificationCounts["authored"], Is.EqualTo(360));
            Assert.That(classificationCounts["derived"], Is.EqualTo(216));
            Assert.That(classificationCounts["inherited"], Is.EqualTo(312));
            var indexedClassificationCounts = GetRequiredProperty(index, "frame_classification_counts");
            Assert.That(GetRequiredInteger(indexedClassificationCounts, "authored"), Is.EqualTo(360));
            Assert.That(GetRequiredInteger(indexedClassificationCounts, "derived"), Is.EqualTo(216));
            Assert.That(GetRequiredInteger(indexedClassificationCounts, "inherited"), Is.EqualTo(312));

            var atlasFiles = Directory.GetFiles(
                ResolveProjectPath(MonsterAnimationAtlasRoot),
                "*_animation_atlas_v002.png",
                SearchOption.TopDirectoryOnly);
            var atlasPaths = new List<string>();
            for (var atlasFileIndex = 0; atlasFileIndex < atlasFiles.Length; atlasFileIndex++)
            {
                atlasPaths.Add(ToProjectRelativePath(atlasFiles[atlasFileIndex]));
            }

            CollectionAssert.AreEquivalent(GetMonsterAnimationAtlasPaths(), atlasPaths);
            for (var roleIndex = 0; roleIndex < MonsterAnimationRoles.Length; roleIndex++)
            {
                var role = MonsterAnimationRoles[roleIndex];
                Assert.That(
                    charactersByRole.TryGetValue(role.Role, out var character),
                    Is.True,
                    $"Monster v002 index is missing role '{role.Role}'.");
                AssertMonsterAtlasContract(
                    role,
                    character,
                    framesByName,
                    !string.Equals(role.Role, "archer", StringComparison.Ordinal));
            }

            Assert.That((int)CharacterAnimationState.Idle, Is.EqualTo(0));
            Assert.That((int)CharacterAnimationState.Walk, Is.EqualTo(1));
            Assert.That((int)CharacterAnimationState.Dash, Is.EqualTo(2));
            Assert.That((int)CharacterAnimationState.BlessCast, Is.EqualTo(3));
            Assert.That((int)CharacterAnimationState.AttackCharge, Is.EqualTo(4));
            Assert.That((int)CharacterAnimationState.AttackExecute, Is.EqualTo(5));
            Assert.That((int)CharacterAnimationState.Recover, Is.EqualTo(6));
            Assert.That(Enum.IsDefined(typeof(CharacterAnimationState), 7), Is.False);
            Assert.That((int)CharacterAnimationState.Hit, Is.EqualTo(8));
            Assert.That((int)CharacterAnimationState.Death, Is.EqualTo(9));
            Assert.That((int)CharacterAnimationState.Run, Is.EqualTo(10));
        }

        [Test]
        public void ArcherMotionV003SheetsKeepIndexedPixelsImportAndRuntimeClipContracts()
        {
            Assert.That(File.Exists(ResolveProjectPath(ArcherMotionV003IndexPath)), Is.True);
            var index = ReadJsonDocument(ArcherMotionV003IndexPath);
            Assert.That(GetRequiredString(index, "schema"), Is.EqualTo("overbless.archer-motion-v003-index/v1"));
            Assert.That(GetRequiredString(index, "role"), Is.EqualTo(ArcherMotionV003Role.Role));
            Assert.That(GetRequiredString(index, "version"), Is.EqualTo(ArcherMotionV003Role.MotionVersion));

            var cell = GetRequiredProperty(index, "cell");
            Assert.That(GetRequiredInteger(cell, "width"), Is.EqualTo(128));
            Assert.That(GetRequiredInteger(cell, "height"), Is.EqualTo(128));
            var pivot = GetRequiredArray(cell, "pivot");
            Assert.That(pivot.Count, Is.EqualTo(2));
            Assert.That(pivot[0].NumberValue, Is.EqualTo(0.5d).Within(0.0001d));
            Assert.That(pivot[1].NumberValue, Is.EqualTo(0d).Within(0.0001d));
            CollectionAssert.AreEqual(
                new[] { "south", "north", "east", "southeast", "northeast" },
                GetStringArray(GetRequiredProperty(index, "direct_directions"), "direct_directions"));

            var mirrors = GetRequiredProperty(index, "derived_mirrors");
            Assert.That(GetRequiredString(mirrors, "west"), Is.EqualTo("east"));
            Assert.That(GetRequiredString(mirrors, "southwest"), Is.EqualTo("southeast"));
            Assert.That(GetRequiredString(mirrors, "northwest"), Is.EqualTo("northeast"));
            Assert.That(mirrors.Properties.Count, Is.EqualTo(3));

            var sourceRuns = GetRequiredArray(index, "source_runs");
            Assert.That(sourceRuns.Count, Is.EqualTo(5));
            for (var sourceIndex = 0; sourceIndex < sourceRuns.Count; sourceIndex++)
            {
                var sourceRun = sourceRuns[sourceIndex];
                Assert.That(File.Exists(ResolveProjectPath(GetRequiredString(sourceRun, "atlas"))), Is.True);
                Assert.That(File.Exists(ResolveProjectPath(GetRequiredString(sourceRun, "manifest"))), Is.True);
            }

            var sheetsByState = new Dictionary<string, CanonicalJsonValue>(StringComparer.Ordinal);
            var outputs = GetRequiredArray(index, "outputs");
            Assert.That(outputs.Count, Is.EqualTo(MonsterAnimationStates.Length));
            for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                var output = outputs[outputIndex];
                var state = GetRequiredString(output, "state");
                Assert.That(sheetsByState.ContainsKey(state), Is.False, $"Archer v003 index repeats state '{state}'.");
                sheetsByState.Add(state, output);
            }

            var spritesByName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                var expectedState = MonsterAnimationStates[stateIndex];
                Assert.That(sheetsByState.TryGetValue(expectedState.Name, out var sheet), Is.True);
                var path = ArcherMotionV003Role.MotionSheetPath(expectedState.Name);
                Assert.That(GetRequiredString(sheet, "path"), Is.EqualTo(path));
                Assert.That(GetRequiredInteger(sheet, "frames"), Is.EqualTo(expectedState.FrameCount));
                Assert.That(GetRequiredFloat(sheet, "fps"), Is.EqualTo(expectedState.FramesPerSecond).Within(0.0001f));
                Assert.That(GetRequiredBoolean(sheet, "loop"), Is.EqualTo(expectedState.Loop));
                Assert.That(GetRequiredInteger(sheet, "width"), Is.EqualTo(128 * 8 * expectedState.FrameCount));
                Assert.That(GetRequiredInteger(sheet, "height"), Is.EqualTo(128));

                var filePath = ResolveProjectPath(path);
                Assert.That(File.Exists(filePath), Is.True, $"Archer v003 motion sheet is missing: {path}");
                Assert.That(File.Exists(filePath + ".meta"), Is.True, $"Archer v003 motion sheet metadata is missing: {path}.meta");
                Assert.That(ComputeSha256(filePath), Is.EqualTo(GetRequiredString(sheet, "sha256")));
                var bytes = File.ReadAllBytes(filePath);
                AssertStaticRgbaPngHeader(bytes, path, 128 * 8 * expectedState.FrameCount, 128);
                AssertMonsterMotionSheetImporter(path);

                var sheetSprites = GetMonsterSprites(path);
                Assert.That(sheetSprites.Count, Is.EqualTo(8 * expectedState.FrameCount));
                for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                {
                    var direction = MonsterAnimationDirections[directionIndex];
                    for (var frameIndex = 0; frameIndex < expectedState.FrameCount; frameIndex++)
                    {
                        var spriteName = GetMonsterFrameName(
                            ArcherMotionV003Role.Role,
                            expectedState.Name,
                            direction,
                            frameIndex,
                            ArcherMotionV003Role.MotionVersion);
                        Assert.That(sheetSprites.TryGetValue(spriteName, out var sprite), Is.True);
                        Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(path));
                        Assert.That(sprite.rect.x, Is.EqualTo((directionIndex * expectedState.FrameCount + frameIndex) * 128f));
                        Assert.That(sprite.rect.y, Is.EqualTo(0f));
                        Assert.That(sprite.rect.width, Is.EqualTo(128f));
                        Assert.That(sprite.rect.height, Is.EqualTo(128f));
                        Assert.That(spritesByName.ContainsKey(spriteName), Is.False);
                        spritesByName.Add(spriteName, sprite);
                    }
                }
            }

            Assert.That(spritesByName.Count, Is.EqualTo(296));
            AssertArcherMotionV003RuntimeAnimationSet(spritesByName);
        }

        [Test]
        public void MonsterV002LocomotionLoopsContainOnlyDistinctFrames()
        {
            var index = ReadJsonDocument(MonsterAnimationIndexPath);
            var framesByName = GetMonsterFramesByName(index, out _);

            for (var roleIndex = 0; roleIndex < MonsterAnimationRoles.Length; roleIndex++)
            {
                var role = MonsterAnimationRoles[roleIndex];
                for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
                {
                    var state = MonsterAnimationStates[stateIndex];
                    if (!state.Loop || state.Name == "idle")
                    {
                        continue;
                    }

                    for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                    {
                        var direction = MonsterAnimationDirections[directionIndex];
                        var hashes = new HashSet<string>(StringComparer.Ordinal);
                        for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                        {
                            var frameName = GetMonsterFrameName(role.Role, state.Name, direction, frameIndex);
                            Assert.That(
                                framesByName.TryGetValue(frameName, out var frame),
                                Is.True,
                                $"Monster v002 index is missing looping frame '{frameName}'.");
                            Assert.That(
                                hashes.Add(GetRequiredString(frame, "pixel_sha256")),
                                Is.True,
                                $"Looping clip {role.Role}/{state.Name}/{direction} repeats frame {frameIndex}.");
                        }
                    }
                }
            }
        }
        private static void AssertMonsterGenerationSourcesMatchIndex(
            CanonicalJsonValue generation,
            IReadOnlyDictionary<string, CanonicalJsonValue> indexedSources)
        {
            var generatedSources = GetRequiredArray(generation, "accepted_sources");
            Assert.That(generatedSources.Count, Is.EqualTo(15));
            var generatedHashesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var sourceIndex = 0; sourceIndex < generatedSources.Count; sourceIndex++)
            {
                var source = generatedSources[sourceIndex];
                var sourcePath = GetRequiredString(source, "path");
                Assert.That(generatedHashesByPath.ContainsKey(sourcePath), Is.False, $"Generation record repeats source '{sourcePath}'.");
                generatedHashesByPath.Add(sourcePath, GetRequiredString(source, "sha256"));
            }

            CollectionAssert.AreEquivalent(indexedSources.Keys, generatedHashesByPath.Keys);
            foreach (var sourcePath in indexedSources.Keys)
            {
                Assert.That(
                    generatedHashesByPath[sourcePath],
                    Is.EqualTo(GetRequiredString(indexedSources[sourcePath], "sha256")),
                    $"Generation provenance hash drifted for '{sourcePath}'.");
            }

            var generatedOutputs = GetRequiredArray(generation, "outputs");
            Assert.That(generatedOutputs.Count, Is.EqualTo(10));
            var generatedOutputHashesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var outputIndex = 0; outputIndex < generatedOutputs.Count; outputIndex++)
            {
                var output = generatedOutputs[outputIndex];
                var outputPath = GetRequiredString(output, "path");
                Assert.That(generatedOutputHashesByPath.ContainsKey(outputPath), Is.False, $"Generation record repeats output '{outputPath}'.");
                generatedOutputHashesByPath.Add(outputPath, GetRequiredString(output, "sha256"));
            }

            CollectionAssert.AreEquivalent(GetMonsterAnimationOutputPaths(), generatedOutputHashesByPath.Keys);
            foreach (var outputPath in generatedOutputHashesByPath.Keys)
            {
                Assert.That(File.Exists(ResolveProjectPath(outputPath)), Is.True, $"Generated monster output is missing: {outputPath}");
                Assert.That(
                    ComputeSha256(ResolveProjectPath(outputPath)),
                    Is.EqualTo(generatedOutputHashesByPath[outputPath]),
                    $"Generated monster output bytes drifted: {outputPath}");
            }
        }

        [Test]
        public void MonsterV002LiveReviewRecordAttachesMachineEvidenceWithoutClaimingHumanApproval()
        {
            var record = ReadJsonDocument(MonsterAnimationLiveReviewPath);
            Assert.That(
                GetRequiredString(record, "review_id"),
                Is.EqualTo("monster_directional_animation_live_review_v002"));
            Assert.That(
                GetRequiredString(record, "status"),
                Is.EqualTo("machine-live-review-complete-human-approval-pending"));

            var extends = GetRequiredProperty(record, "extends");
            Assert.That(GetRequiredString(extends, "path"), Is.EqualTo(MonsterAnimationReviewPath));
            Assert.That(
                GetRequiredString(extends, "sha256"),
                Is.EqualTo(ComputeSha256(ResolveProjectPath(MonsterAnimationReviewPath))),
                "The extended review record must stay byte identical; corrections belong in a new record.");

            var candidate = GetRequiredProperty(record, "candidate");
            Assert.That(GetRequiredString(candidate, "unity_version"), Is.EqualTo(Application.unityVersion));
            Assert.That(GetRequiredString(candidate, "commit").Length, Is.EqualTo(40));
            Assert.That(GetRequiredString(GetRequiredProperty(record, "baseline"), "commit").Length, Is.EqualTo(40));

            var tooling = GetRequiredArray(record, "tooling");
            Assert.That(tooling.Count, Is.GreaterThanOrEqualTo(2));
            for (var toolIndex = 0; toolIndex < tooling.Count; toolIndex++)
            {
                var toolPath = GetRequiredString(tooling[toolIndex], "path");
                Assert.That(File.Exists(ResolveProjectPath(toolPath)), Is.True, $"Recorded tool is missing: {toolPath}");
                Assert.That(
                    GetRequiredString(tooling[toolIndex], "sha256"),
                    Is.EqualTo(ComputeSha256(ResolveProjectPath(toolPath))),
                    $"Recorded tool bytes drifted: {toolPath}");
            }

            var suites = GetRequiredArray(record, "test_suites");
            Assert.That(suites.Count, Is.EqualTo(2));
            var suitePlatforms = new List<string>();
            for (var suiteIndex = 0; suiteIndex < suites.Count; suiteIndex++)
            {
                var suite = suites[suiteIndex];
                suitePlatforms.Add(GetRequiredString(suite, "platform"));
                Assert.That(GetRequiredInteger(suite, "failed"), Is.EqualTo(0));
                Assert.That(
                    GetRequiredInteger(suite, "passed"),
                    Is.EqualTo(GetRequiredInteger(suite, "total")),
                    "A recorded suite must have every case passing.");
                Assert.That(GetRequiredString(suite, "result"), Is.EqualTo("Passed"));
            }

            CollectionAssert.AreEquivalent(new[] { "EditMode", "PlayMode" }, suitePlatforms);
            AssertLiveReviewTimingMetrics(record);
            AssertLiveReviewVisualMatrix(record);
            AssertLiveReviewScenarios(record);

            Assert.That(
                GetRequiredProperty(record, "reviewer").Kind,
                Is.EqualTo(CanonicalJsonKind.Null),
                "Machine evidence must never record a reviewer identity.");
            Assert.That(GetRequiredArray(record, "human_review_remaining").Count, Is.GreaterThan(0));
        }

        private static void AssertLiveReviewTimingMetrics(CanonicalJsonValue record)
        {
            var metrics = GetRequiredArray(record, "timing_metrics");
            Assert.That(metrics.Count, Is.EqualTo(MonsterAnimationTimingMetrics.Length));
            var largestDelta = 0f;
            for (var metricIndex = 0; metricIndex < metrics.Count; metricIndex++)
            {
                var metric = metrics[metricIndex];
                Assert.That(
                    GetRequiredString(metric, "metric"),
                    Is.EqualTo(MonsterAnimationTimingMetrics[metricIndex]),
                    "Timing metrics must stay in the reviewed order.");
                var baselineSeconds = GetRequiredFloat(metric, "baseline_seconds");
                var candidateSeconds = GetRequiredFloat(metric, "candidate_seconds");
                var deltaPercent = GetRequiredFloat(metric, "delta_percent");
                Assert.That(baselineSeconds, Is.GreaterThan(0f));
                Assert.That(candidateSeconds, Is.GreaterThan(0f));
                Assert.That(
                    deltaPercent,
                    Is.EqualTo(((candidateSeconds - baselineSeconds) / baselineSeconds) * 100f).Within(0.01f),
                    "Recorded delta must match its own baseline and candidate seconds.");
                Assert.That(
                    Mathf.Abs(deltaPercent),
                    Is.LessThanOrEqualTo(MonsterAnimationTimingTolerancePercent),
                    $"Timing metric '{GetRequiredString(metric, "metric")}' left the ten percent band.");
                Assert.That(GetRequiredString(metric, "status"), Is.EqualTo("within-ten-percent"));
                largestDelta = Mathf.Max(largestDelta, Mathf.Abs(deltaPercent));
            }

            var summary = GetRequiredProperty(record, "timing_summary");
            Assert.That(
                GetRequiredFloat(summary, "tolerance_percent"),
                Is.EqualTo(MonsterAnimationTimingTolerancePercent).Within(0.0001f));
            Assert.That(
                GetRequiredFloat(summary, "largest_absolute_delta_percent"),
                Is.EqualTo(largestDelta).Within(0.01f));
            Assert.That(GetRequiredBoolean(summary, "all_within_tolerance"), Is.True);
        }

        private static void AssertLiveReviewVisualMatrix(CanonicalJsonValue record)
        {
            var matrix = GetRequiredArray(record, "visual_matrix");
            Assert.That(matrix.Count, Is.EqualTo(4));
            var seenCells = new List<string>();
            for (var cellIndex = 0; cellIndex < matrix.Count; cellIndex++)
            {
                var cell = matrix[cellIndex];
                var build = GetRequiredString(cell, "build");
                var resolution = GetRequiredString(cell, "resolution");
                seenCells.Add($"{build}@{resolution}");
                CollectionAssert.AreEqual(
                    new[] { "dasher", "archer", "minion" },
                    GetStringArray(GetRequiredProperty(cell, "roles"), "roles"));
                CollectionAssert.AreEqual(
                    new[] { "walk", "run", "attack" },
                    GetStringArray(GetRequiredProperty(cell, "states"), "states"));
                Assert.That(
                    GetRequiredString(cell, "status"),
                    Is.EqualTo("machine-captured-human-approval-pending"));

                var separatorIndex = resolution.IndexOf('x');
                Assert.That(separatorIndex, Is.GreaterThan(0), $"Resolution must be WIDTHxHEIGHT: {resolution}");
                var expectedSize = new[]
                {
                    int.Parse(resolution.Substring(0, separatorIndex), CultureInfo.InvariantCulture),
                    int.Parse(resolution.Substring(separatorIndex + 1), CultureInfo.InvariantCulture)
                };
                AssertIntArray(cell, "canvas_css", expectedSize);
                AssertIntArray(cell, "canvas_backing", expectedSize);
                Assert.That(GetRequiredFloat(cell, "maximum_frame_change"), Is.GreaterThan(0f));
                Assert.That(GetRequiredInteger(cell, "distinct_screenshot_hashes"), Is.GreaterThan(1));

                var media = GetRequiredArray(cell, "media");
                Assert.That(media.Count, Is.GreaterThanOrEqualTo(2), "Each matrix cell needs attached review media.");
                for (var mediaIndex = 0; mediaIndex < media.Count; mediaIndex++)
                {
                    var mediaPath = GetRequiredString(media[mediaIndex], "path");
                    Assert.That(
                        mediaPath.StartsWith(MonsterAnimationReviewMediaRoot, StringComparison.Ordinal),
                        Is.True,
                        $"Review media must live under {MonsterAnimationReviewMediaRoot}: {mediaPath}");
                    var resolved = ResolveProjectPath(mediaPath);
                    Assert.That(File.Exists(resolved), Is.True, $"Review media is missing: {mediaPath}");
                    Assert.That(
                        ComputeSha256(resolved),
                        Is.EqualTo(GetRequiredString(media[mediaIndex], "sha256")),
                        $"Review media bytes drifted: {mediaPath}");
                    Assert.That(
                        GetRequiredInteger(media[mediaIndex], "bytes"),
                        Is.EqualTo((int)new FileInfo(resolved).Length),
                        $"Review media size drifted: {mediaPath}");
                }
            }

            CollectionAssert.AreEquivalent(
                new[] { "M1@1280x720", "M1@1920x1080", "M2@1280x720", "M2@1920x1080" },
                seenCells);
        }

        private static void AssertLiveReviewScenarios(CanonicalJsonValue record)
        {
            var priorReview = ReadJsonDocument(MonsterAnimationReviewPath);
            var priorScenarios = GetRequiredArray(priorReview, "runtime_scenarios");
            var expectedIds = new List<string>();
            for (var scenarioIndex = 0; scenarioIndex < priorScenarios.Count; scenarioIndex++)
            {
                expectedIds.Add(GetRequiredString(priorScenarios[scenarioIndex], "id"));
            }

            var scenarios = GetRequiredArray(record, "runtime_scenarios");
            var recordedIds = new List<string>();
            for (var scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
            {
                var scenario = scenarios[scenarioIndex];
                recordedIds.Add(GetRequiredString(scenario, "id"));
                Assert.That(
                    GetRequiredArray(scenario, "automated_coverage").Count,
                    Is.GreaterThan(0),
                    "Every scenario must name the automated coverage that backs it.");
                Assert.That(
                    GetRequiredString(scenario, "status"),
                    Is.EqualTo("machine-covered-human-approval-pending"));
            }

            CollectionAssert.AreEquivalent(expectedIds, recordedIds);
        }

        private static void AssertMonsterStateContracts(IReadOnlyList<CanonicalJsonValue> states)
        {
            Assert.That(states.Count, Is.EqualTo(MonsterAnimationStates.Length));
            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                var expected = MonsterAnimationStates[stateIndex];
                var state = states[stateIndex];
                Assert.That(GetRequiredString(state, "name"), Is.EqualTo(expected.Name));
                Assert.That(GetRequiredInteger(state, "frames"), Is.EqualTo(expected.FrameCount));
                var framesPerSecond = GetRequiredProperty(state, "fps");
                if (framesPerSecond.Kind == CanonicalJsonKind.Number)
                {
                    Assert.That(framesPerSecond.NumberValue, Is.EqualTo(expected.FramesPerSecond).Within(0.0001f));
                }
                else
                {
                    Assert.That(expected.State, Is.EqualTo(CharacterAnimationState.AttackExecute));
                    Assert.That(framesPerSecond.Kind, Is.EqualTo(CanonicalJsonKind.Object));
                    Assert.That(
                        GetRequiredFloat(framesPerSecond, "default"),
                        Is.EqualTo(expected.FramesPerSecond).Within(0.0001f));
                    Assert.That(GetRequiredFloat(framesPerSecond, "minion"), Is.EqualTo(24f).Within(0.0001f));
                }

                Assert.That(GetRequiredBoolean(state, "loop"), Is.EqualTo(expected.Loop));
            }
        }

        private static Dictionary<string, CanonicalJsonValue> GetMonsterCharactersByRole(CanonicalJsonValue index)
        {
            var characters = GetRequiredArray(index, "characters");
            Assert.That(characters.Count, Is.EqualTo(MonsterAnimationRoles.Length));
            var charactersByRole = new Dictionary<string, CanonicalJsonValue>(StringComparer.Ordinal);
            for (var characterIndex = 0; characterIndex < characters.Count; characterIndex++)
            {
                var character = characters[characterIndex];
                var role = GetRequiredString(character, "role");
                Assert.That(charactersByRole.ContainsKey(role), Is.False, $"Monster v002 index repeats role '{role}'.");
                charactersByRole.Add(role, character);
            }

            return charactersByRole;
        }

        private static Dictionary<string, CanonicalJsonValue> GetMonsterFramesByName(
            CanonicalJsonValue index,
            out Dictionary<string, int> classificationCounts)
        {
            classificationCounts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "authored", 0 },
                { "derived", 0 },
                { "inherited", 0 }
            };
            var frames = GetRequiredArray(index, "frames");
            var framesByName = new Dictionary<string, CanonicalJsonValue>(StringComparer.Ordinal);
            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                var frame = frames[frameIndex];
                var name = GetRequiredString(frame, "name");
                Assert.That(framesByName.ContainsKey(name), Is.False, $"Monster v002 index repeats frame '{name}'.");
                framesByName.Add(name, frame);

                var classification = GetRequiredString(frame, "classification");
                Assert.That(
                    classificationCounts.ContainsKey(classification),
                    Is.True,
                    $"Monster v002 frame '{name}' has an unknown classification '{classification}'.");
                classificationCounts[classification]++;
            }

            return framesByName;
        }

        private static string[] GetMonsterAnimationAtlasPaths()
        {
            var paths = new List<string>();
            for (var roleIndex = 0; roleIndex < MonsterAnimationRoles.Length; roleIndex++)
            {
                var role = MonsterAnimationRoles[roleIndex];
                if (!role.UsesMotionSheets)
                {
                    paths.Add(role.AtlasPath);
                }
            }

            return paths.ToArray();
        }

        private static string[] GetMonsterAnimationOutputPaths()
        {
            var paths = new List<string>();
            for (var roleIndex = 0; roleIndex < MonsterAnimationRoles.Length; roleIndex++)
            {
                var role = MonsterAnimationRoles[roleIndex];
                if (!role.UsesMotionSheets)
                {
                    paths.Add(role.AtlasPath);
                    continue;
                }

                for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
                {
                    paths.Add(role.MotionSheetPath(MonsterAnimationStates[stateIndex].Name));
                }
            }

            return paths.ToArray();
        }

        private static void AssertMonsterAtlasContract(
            MonsterAnimationRoleSpec role,
            CanonicalJsonValue character,
            IReadOnlyDictionary<string, CanonicalJsonValue> framesByName,
            bool assertRuntimeClipBindings)
        {
            Assert.That(GetRequiredString(character, "role"), Is.EqualTo(role.Role));
            AssertMonsterCharacterStateContracts(role, GetRequiredArray(character, "states"));
            var frameCounts = GetRequiredProperty(character, "frame_counts");
            Assert.That(GetRequiredInteger(frameCounts, "authored"), Is.EqualTo(120));
            Assert.That(GetRequiredInteger(frameCounts, "derived"), Is.EqualTo(72));
            Assert.That(GetRequiredInteger(frameCounts, "inherited"), Is.EqualTo(104));

            if (role.UsesMotionSheets)
            {
                AssertMonsterMotionSheetContract(role, character, framesByName, assertRuntimeClipBindings);
                return;
            }

            Assert.That(GetRequiredString(character, "atlas_path"), Is.EqualTo(role.AtlasPath));
            AssertIntArray(character, "atlas_size", new[] { 8192, 1024 });

            var atlasPath = ResolveProjectPath(role.AtlasPath);
            Assert.That(File.Exists(atlasPath), Is.True, $"Monster v002 atlas is missing: {role.AtlasPath}");
            Assert.That(File.Exists(atlasPath + ".meta"), Is.True, $"Monster v002 atlas metadata is missing: {role.AtlasPath}.meta");
            Assert.That(
                ComputeSha256(atlasPath),
                Is.EqualTo(GetRequiredString(character, "atlas_sha256")),
                $"Monster v002 atlas bytes drifted: {role.AtlasPath}");

            var atlasBytes = File.ReadAllBytes(atlasPath);
            AssertStaticRgbaPngHeader(atlasBytes, role.AtlasPath, 8192, 1024);
            AssertMonsterAtlasImporter(role);
            var spritesByName = GetMonsterAtlasSprites(role);
            Assert.That(spritesByName.Count, Is.EqualTo(296), $"Monster v002 atlas has the wrong sprite count: {role.AtlasPath}");
            if (assertRuntimeClipBindings)
            {
                AssertMonsterRuntimeAnimationSet(role, character, spritesByName);
            }
            AssertMonsterAtlasFramePixels(role, atlasBytes, spritesByName, framesByName);
        }

        private static void AssertMonsterMotionSheetContract(
            MonsterAnimationRoleSpec role,
            CanonicalJsonValue character,
            IReadOnlyDictionary<string, CanonicalJsonValue> framesByName,
            bool assertRuntimeClipBindings)
        {
            Assert.That(GetRequiredString(character, "output_topology"), Is.EqualTo("per-state-motion-sheets"));
            var sheets = GetRequiredArray(character, "motion_sheets");
            Assert.That(sheets.Count, Is.EqualTo(MonsterAnimationStates.Length));

            var spritesByName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var sheetsByState = new Dictionary<string, CanonicalJsonValue>(StringComparer.Ordinal);
            for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
            {
                var sheet = sheets[sheetIndex];
                var stateName = GetRequiredString(sheet, "state");
                Assert.That(sheetsByState.ContainsKey(stateName), Is.False, $"Monster motion sheet repeats state '{stateName}'.");
                sheetsByState.Add(stateName, sheet);
            }

            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                var expectedState = MonsterAnimationStates[stateIndex];
                Assert.That(
                    sheetsByState.TryGetValue(expectedState.Name, out var sheet),
                    Is.True,
                    $"Monster motion sheet is missing state '{expectedState.Name}'.");

                var path = role.MotionSheetPath(expectedState.Name);
                Assert.That(GetRequiredString(sheet, "path"), Is.EqualTo(path));
                AssertIntArray(sheet, "size", new[] { 128 * 8 * expectedState.FrameCount, 128 });
                CollectionAssert.AreEqual(
                    MonsterAnimationDirections,
                    GetStringArray(GetRequiredProperty(sheet, "directions"), "directions"));
                Assert.That(GetRequiredInteger(sheet, "frames_per_direction"), Is.EqualTo(expectedState.FrameCount));
                Assert.That(GetRequiredInteger(sheet, "rows"), Is.EqualTo(1));

                var filePath = ResolveProjectPath(path);
                Assert.That(File.Exists(filePath), Is.True, $"Monster motion sheet is missing: {path}");
                Assert.That(File.Exists(filePath + ".meta"), Is.True, $"Monster motion sheet metadata is missing: {path}.meta");
                Assert.That(ComputeSha256(filePath), Is.EqualTo(GetRequiredString(sheet, "sha256")));
                var bytes = File.ReadAllBytes(filePath);
                AssertStaticRgbaPngHeader(bytes, path, 128 * 8 * expectedState.FrameCount, 128);
                AssertMonsterMotionSheetImporter(path);

                var sheetSprites = GetMonsterSprites(path);
                Assert.That(sheetSprites.Count, Is.EqualTo(8 * expectedState.FrameCount));
                foreach (var pair in sheetSprites)
                {
                    Assert.That(spritesByName.ContainsKey(pair.Key), Is.False, $"Monster motion sheets repeat sprite '{pair.Key}'.");
                    spritesByName.Add(pair.Key, pair.Value);
                }

                AssertMonsterMotionSheetFramePixels(role, expectedState, path, bytes, sheetSprites, framesByName);
            }

            Assert.That(spritesByName.Count, Is.EqualTo(296));
            if (assertRuntimeClipBindings)
            {
                AssertMonsterRuntimeAnimationSet(role, character, spritesByName);
            }
        }

        private static void AssertMonsterCharacterStateContracts(
            MonsterAnimationRoleSpec role,
            IReadOnlyList<CanonicalJsonValue> states)
        {
            Assert.That(states.Count, Is.EqualTo(MonsterAnimationStates.Length));
            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                var expected = MonsterAnimationStates[stateIndex];
                var state = states[stateIndex];
                var expectedFramesPerSecond = expected.FramesPerSecond;
                if (role.Role == "minion" && expected.Name == "attack_execute")
                {
                    expectedFramesPerSecond = 24f;
                }

                Assert.That(GetRequiredString(state, "name"), Is.EqualTo(expected.Name));
                Assert.That(GetRequiredInteger(state, "frames"), Is.EqualTo(expected.FrameCount));
                Assert.That(GetRequiredFloat(state, "fps"), Is.EqualTo(expectedFramesPerSecond).Within(0.0001f));
                Assert.That(GetRequiredBoolean(state, "loop"), Is.EqualTo(expected.Loop));
            }
        }

        private static void AssertMonsterAtlasImporter(MonsterAnimationRoleSpec role)
        {
            Assert.That(
                AssetDatabase.AssetPathToGUID(role.AtlasPath),
                Is.Not.Empty,
                $"Monster v002 atlas has no stable asset GUID: {role.AtlasPath}");
            var importer = AssetImporter.GetAtPath(role.AtlasPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"Monster v002 atlas has no TextureImporter: {role.AtlasPath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.streamingMipmaps, Is.False);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.isReadable, Is.False);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom));
            Assert.That(Vector2.Distance(settings.spritePivot, new Vector2(0.5f, 0f)), Is.LessThanOrEqualTo(0.0001f));
        }

        private static void AssertMonsterMotionSheetImporter(string path)
        {
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.Not.Empty, $"Monster motion sheet has no stable asset GUID: {path}");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"Monster motion sheet has no TextureImporter: {path}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(128f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.streamingMipmaps, Is.False);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.isReadable, Is.False);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom));
            Assert.That(Vector2.Distance(settings.spritePivot, new Vector2(0.5f, 0f)), Is.LessThanOrEqualTo(0.0001f));
        }

        private static Dictionary<string, Sprite> GetMonsterAtlasSprites(MonsterAnimationRoleSpec role)
        {
            return GetMonsterSprites(role.AtlasPath);
        }

        private static Dictionary<string, Sprite> GetMonsterSprites(string assetPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var spritesByName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
            {
                var sprite = assets[assetIndex] as Sprite;
                if (sprite == null)
                {
                    continue;
                }

                Assert.That(
                    spritesByName.ContainsKey(sprite.name),
                    Is.False,
                    $"Monster v002 sprite source repeats sprite '{sprite.name}': {assetPath}");
                spritesByName.Add(sprite.name, sprite);
            }

            return spritesByName;
        }

        private static void AssertMonsterRuntimeAnimationSet(
            MonsterAnimationRoleSpec role,
            CanonicalJsonValue character,
            IReadOnlyDictionary<string, Sprite> spritesByName)
        {
            var animationSet = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(role.AnimationSetPath);
            Assert.That(animationSet, Is.Not.Null, $"Monster v002 animation set is missing: {role.AnimationSetPath}");
            Assert.That(animationSet.Role, Is.EqualTo(role.Role));
            Assert.That(animationSet.ClipCount, Is.EqualTo(64));
            animationSet.Validate();

            var animationSetText = ReadAssetText(role.AnimationSetPath);
            AssertTextExcludes(animationSetText, role.AnimationSetPath, "state: 7");
            var v001AtlasPath = GetRequiredString(GetRequiredProperty(character, "inherited_v001_source"), "path");
            var v001AtlasGuid = AssetDatabase.AssetPathToGUID(v001AtlasPath);
            Assert.That(v001AtlasGuid, Is.Not.Empty, $"Monster v001 atlas has no GUID: {v001AtlasPath}");
            AssertTextExcludes(animationSetText, role.AnimationSetPath, v001AtlasGuid);

            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                var expectedState = MonsterAnimationStates[stateIndex];
                var expectedFramesPerSecond = expectedState.FramesPerSecond;
                if (role.Role == "minion" && expectedState.Name == "attack_execute")
                {
                    expectedFramesPerSecond = 24f;
                }

                for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                {
                    var direction = MonsterAnimationDirections[directionIndex];
                    var clip = animationSet.GetClip(
                        expectedState.State,
                        GetMonsterAnimationDirection(direction));
                    Assert.That(clip.FrameCount, Is.EqualTo(expectedState.FrameCount));
                    Assert.That(clip.FramesPerSecond, Is.EqualTo(expectedFramesPerSecond).Within(0.0001f));
                    Assert.That(clip.Loop, Is.EqualTo(expectedState.Loop));
                    for (var frameIndex = 0; frameIndex < expectedState.FrameCount; frameIndex++)
                    {
                        var spriteName = GetMonsterFrameName(
                            role.Role,
                            expectedState.Name,
                            direction,
                            frameIndex);
                        Assert.That(
                            spritesByName.TryGetValue(spriteName, out var sprite),
                            Is.True,
                            $"Monster v002 atlas is missing sprite '{spriteName}'.");
                        Assert.That(
                            clip.GetFrame(frameIndex),
                            Is.SameAs(sprite),
                            $"Monster v002 clip {expectedState.Name}/{direction} references a stale or wrong sprite.");
                    }
                }
            }
        }

        private static void AssertArcherMotionV003RuntimeAnimationSet(
            IReadOnlyDictionary<string, Sprite> spritesByName)
        {
            var animationSet = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(ArcherMotionV003Role.AnimationSetPath);
            Assert.That(animationSet, Is.Not.Null, $"Archer v003 animation set is missing: {ArcherMotionV003Role.AnimationSetPath}");
            Assert.That(animationSet.Role, Is.EqualTo(ArcherMotionV003Role.Role));
            Assert.That(animationSet.ClipCount, Is.EqualTo(64));
            animationSet.Validate();

            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                var expectedState = MonsterAnimationStates[stateIndex];
                for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                {
                    var direction = MonsterAnimationDirections[directionIndex];
                    var clip = animationSet.GetClip(
                        expectedState.State,
                        GetMonsterAnimationDirection(direction));
                    Assert.That(clip.FrameCount, Is.EqualTo(expectedState.FrameCount));
                    Assert.That(clip.FramesPerSecond, Is.EqualTo(expectedState.FramesPerSecond).Within(0.0001f));
                    Assert.That(clip.Loop, Is.EqualTo(expectedState.Loop));
                    for (var frameIndex = 0; frameIndex < expectedState.FrameCount; frameIndex++)
                    {
                        var spriteName = GetMonsterFrameName(
                            ArcherMotionV003Role.Role,
                            expectedState.Name,
                            direction,
                            frameIndex,
                            ArcherMotionV003Role.MotionVersion);
                        Assert.That(spritesByName.TryGetValue(spriteName, out var sprite), Is.True);
                        Assert.That(
                            clip.GetFrame(frameIndex),
                            Is.SameAs(sprite),
                            $"Archer v003 clip {expectedState.Name}/{direction} references a stale or wrong sprite.");
                    }
                }
            }
        }

        private static void AssertMonsterMotionSheetFramePixels(
            MonsterAnimationRoleSpec role,
            MonsterAnimationStateSpec expectedState,
            string motionPath,
            byte[] motionBytes,
            IReadOnlyDictionary<string, Sprite> spritesByName,
            IReadOnlyDictionary<string, CanonicalJsonValue> framesByName)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, motionBytes, false), Is.True);
                Assert.That(texture.width, Is.EqualTo(128 * 8 * expectedState.FrameCount));
                Assert.That(texture.height, Is.EqualTo(128));
                var pixels = texture.GetPixels32();
                for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                {
                    var direction = MonsterAnimationDirections[directionIndex];
                    for (var frameIndex = 0; frameIndex < expectedState.FrameCount; frameIndex++)
                    {
                        var frameName = GetMonsterFrameName(role.Role, expectedState.Name, direction, frameIndex);
                        Assert.That(framesByName.TryGetValue(frameName, out var frame), Is.True);
                        Assert.That(GetRequiredString(frame, "output_path"), Is.EqualTo(motionPath));
                        var expectedRect = new[]
                        {
                            (directionIndex * expectedState.FrameCount + frameIndex) * 128,
                            0,
                            128,
                            128
                        };
                        AssertIntArray(frame, "rect", expectedRect);
                        Assert.That(GetRequiredString(frame, "alpha"), Is.EqualTo("binary"));
                        Assert.That(spritesByName.TryGetValue(frameName, out var sprite), Is.True);
                        Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(motionPath));
                        Assert.That(sprite.rect.x, Is.EqualTo(expectedRect[0]));
                        Assert.That(sprite.rect.y, Is.EqualTo(0f));
                        Assert.That(sprite.rect.width, Is.EqualTo(128f));
                        Assert.That(sprite.rect.height, Is.EqualTo(128f));
                        AssertMonsterFramePixelEvidence(
                            frameName,
                            frame,
                            GetIndexedFramePixels(pixels, texture.width, texture.height, frame));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void AssertMonsterAtlasFramePixels(
            MonsterAnimationRoleSpec role,
            byte[] atlasBytes,
            IReadOnlyDictionary<string, Sprite> spritesByName,
            IReadOnlyDictionary<string, CanonicalJsonValue> framesByName)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, atlasBytes, false),
                    Is.True,
                    $"Unity cannot decode monster v002 atlas: {role.AtlasPath}");
                Assert.That(texture.width, Is.EqualTo(8192));
                Assert.That(texture.height, Is.EqualTo(1024));
                var atlasPixels = texture.GetPixels32();
                var frameCount = 0;
                for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
                {
                    var expectedState = MonsterAnimationStates[stateIndex];
                    for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                    {
                        var direction = MonsterAnimationDirections[directionIndex];
                        for (var frameIndex = 0; frameIndex < expectedState.FrameCount; frameIndex++)
                        {
                            var frameName = GetMonsterFrameName(
                                role.Role,
                                expectedState.Name,
                                direction,
                                frameIndex);
                            Assert.That(
                                framesByName.TryGetValue(frameName, out var frame),
                                Is.True,
                                $"Monster v002 index is missing frame '{frameName}'.");
                            AssertMonsterFrameContract(
                                role,
                                expectedState,
                                direction,
                                directionIndex,
                                frameIndex,
                                frame,
                                spritesByName);
                            var pixels = GetIndexedFramePixels(atlasPixels, texture.width, texture.height, frame);
                            AssertMonsterFramePixelEvidence(frameName, frame, pixels);

                            var mirrorSourceDirection = GetMonsterMirrorSourceDirection(direction);
                            var expectedClassification =
                                expectedState.Name == "idle" ||
                                expectedState.Name == "hit" ||
                                expectedState.Name == "death"
                                    ? "inherited"
                                    : mirrorSourceDirection == null
                                        ? "authored"
                                        : "derived";
                            var classification = GetRequiredString(frame, "classification");
                            Assert.That(classification, Is.EqualTo(expectedClassification));
                            if (expectedClassification == "derived")
                            {
                                var mirrorSourceName = GetMonsterFrameName(
                                    role.Role,
                                    expectedState.Name,
                                    mirrorSourceDirection,
                                    frameIndex);
                                Assert.That(
                                    GetRequiredString(frame, "mirror_source"),
                                    Is.EqualTo(mirrorSourceName));
                                Assert.That(
                                    framesByName.TryGetValue(mirrorSourceName, out var mirrorSource),
                                    Is.True,
                                    $"Monster v002 mirror source is missing: {mirrorSourceName}");
                                Assert.That(GetRequiredString(mirrorSource, "classification"), Is.EqualTo("authored"));
                                Assert.That(GetRequiredString(mirrorSource, "role"), Is.EqualTo(role.Role));
                                Assert.That(GetRequiredString(mirrorSource, "state"), Is.EqualTo(expectedState.Name));
                                Assert.That(GetRequiredString(mirrorSource, "direction"), Is.EqualTo(mirrorSourceDirection));
                                Assert.That(GetRequiredInteger(mirrorSource, "frame"), Is.EqualTo(frameIndex));
                                var sourcePixels = GetIndexedFramePixels(
                                    atlasPixels,
                                    texture.width,
                                    texture.height,
                                    mirrorSource);
                                AssertExactHorizontalPixelMirror(frameName, pixels, mirrorSourceName, sourcePixels);
                            }
                            else
                            {
                                Assert.That(
                                    GetRequiredProperty(frame, "mirror_source").Kind,
                                    Is.EqualTo(CanonicalJsonKind.Null),
                                    $"Only derived monster v002 frames may declare a mirror source: {frameName}");
                            }

                            frameCount++;
                        }
                    }
                }

                Assert.That(frameCount, Is.EqualTo(296));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void AssertMonsterFrameContract(
            MonsterAnimationRoleSpec role,
            MonsterAnimationStateSpec expectedState,
            string direction,
            int directionIndex,
            int frameIndex,
            CanonicalJsonValue frame,
            IReadOnlyDictionary<string, Sprite> spritesByName)
        {
            var frameName = GetMonsterFrameName(role.Role, expectedState.Name, direction, frameIndex);
            Assert.That(GetRequiredString(frame, "name"), Is.EqualTo(frameName));
            Assert.That(GetRequiredString(frame, "role"), Is.EqualTo(role.Role));
            Assert.That(GetRequiredString(frame, "state"), Is.EqualTo(expectedState.Name));
            Assert.That(GetRequiredString(frame, "direction"), Is.EqualTo(direction));
            Assert.That(GetRequiredInteger(frame, "frame"), Is.EqualTo(frameIndex));
            Assert.That(
                GetRequiredString(frame, "frame_letter"),
                Is.EqualTo("abcdefgh"[frameIndex].ToString()));
            Assert.That(GetRequiredInteger(frame, "frame_count"), Is.EqualTo(expectedState.FrameCount));
            var expectedFramesPerSecond = expectedState.FramesPerSecond;
            if (role.Role == "minion" && expectedState.Name == "attack_execute")
            {
                expectedFramesPerSecond = 24f;
            }

            Assert.That(GetRequiredFloat(frame, "fps"), Is.EqualTo(expectedFramesPerSecond).Within(0.0001f));
            Assert.That(GetRequiredBoolean(frame, "loop"), Is.EqualTo(expectedState.Loop));
            var expectedRect = new[]
            {
                (directionIndex * 8 * 128) + (frameIndex * 128),
                Array.IndexOf(MonsterAnimationStates, expectedState) * 128,
                128,
                128
            };
            AssertIntArray(frame, "rect", expectedRect);
            Assert.That(GetRequiredString(frame, "alpha"), Is.EqualTo("binary"));
            Assert.That(
                spritesByName.TryGetValue(frameName, out var sprite),
                Is.True,
                $"Monster v002 atlas is missing indexed sprite '{frameName}'.");
            Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo(role.AtlasPath));
            Assert.That(sprite.rect.x, Is.EqualTo(expectedRect[0]));
            Assert.That(sprite.rect.y, Is.EqualTo(1024 - expectedRect[1] - expectedRect[3]));
            Assert.That(sprite.rect.width, Is.EqualTo(expectedRect[2]));
            Assert.That(sprite.rect.height, Is.EqualTo(expectedRect[3]));
        }

        private static byte[] GetIndexedFramePixels(
            Color32[] atlasPixels,
            int atlasWidth,
            int atlasHeight,
            CanonicalJsonValue frame)
        {
            var rect = GetRequiredArray(frame, "rect");
            Assert.That(rect.Count, Is.EqualTo(4));
            var x = GetArrayInteger(rect, 0, "rect");
            var topY = GetArrayInteger(rect, 1, "rect");
            var width = GetArrayInteger(rect, 2, "rect");
            var height = GetArrayInteger(rect, 3, "rect");
            Assert.That(width, Is.EqualTo(128));
            Assert.That(height, Is.EqualTo(128));
            Assert.That(x, Is.GreaterThanOrEqualTo(0));
            Assert.That(topY, Is.GreaterThanOrEqualTo(0));
            Assert.That(x + width, Is.LessThanOrEqualTo(atlasWidth));
            Assert.That(topY + height, Is.LessThanOrEqualTo(atlasHeight));

            var pixels = new byte[width * height * 4];
            var pixelOffset = 0;
            for (var topRow = 0; topRow < height; topRow++)
            {
                var unityRow = atlasHeight - 1 - topY - topRow;
                var atlasOffset = (unityRow * atlasWidth) + x;
                for (var column = 0; column < width; column++)
                {
                    var pixel = atlasPixels[atlasOffset + column];
                    pixels[pixelOffset++] = pixel.r;
                    pixels[pixelOffset++] = pixel.g;
                    pixels[pixelOffset++] = pixel.b;
                    pixels[pixelOffset++] = pixel.a;
                }
            }

            return pixels;
        }

        private static void AssertMonsterFramePixelEvidence(
            string frameName,
            CanonicalJsonValue frame,
            byte[] pixels)
        {
            Assert.That(
                CanonicalJson.Sha256Hex(pixels),
                Is.EqualTo(GetRequiredString(frame, "pixel_sha256")),
                $"Monster v002 frame pixels drifted: {frameName}");

            var minimumX = 128;
            var minimumY = 128;
            var maximumX = -1;
            var maximumY = -1;
            for (var pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex += 4)
            {
                var alpha = pixels[pixelIndex + 3];
                if (alpha != 0 && alpha != 255)
                {
                    Assert.Fail($"Monster v002 frame alpha must be binary: {frameName} at byte {pixelIndex}.");
                }

                if (alpha == 0)
                {
                    continue;
                }

                var pixel = pixelIndex / 4;
                var x = pixel % 128;
                var y = pixel / 128;
                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }

            Assert.That(maximumX, Is.GreaterThanOrEqualTo(0), $"Monster v002 frame has no opaque pixels: {frameName}");
            var opaqueBounds = new[] { minimumX, minimumY, maximumX + 1, maximumY + 1 };
            AssertIntArray(frame, "opaque_bounds", opaqueBounds);
            Assert.That(GetRequiredInteger(frame, "opaque_foot_y"), Is.EqualTo(opaqueBounds[3]));
        }

        private static void AssertExactHorizontalPixelMirror(
            string derivedName,
            byte[] derivedPixels,
            string sourceName,
            byte[] sourcePixels)
        {
            Assert.That(derivedPixels.Length, Is.EqualTo(128 * 128 * 4));
            Assert.That(sourcePixels.Length, Is.EqualTo(derivedPixels.Length));
            for (var y = 0; y < 128; y++)
            {
                for (var x = 0; x < 128; x++)
                {
                    var derivedOffset = ((y * 128) + x) * 4;
                    var sourceOffset = ((y * 128) + (127 - x)) * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        if (derivedPixels[derivedOffset + channel] != sourcePixels[sourceOffset + channel])
                        {
                            Assert.Fail(
                                $"Monster v002 frame '{derivedName}' is not an exact horizontal pixel mirror of " +
                                $"'{sourceName}' at ({x}, {y}), channel {channel}.");
                        }
                    }
                }
            }
        }

        private static string GetMonsterFrameName(string role, string state, string direction, int frameIndex)
        {
            return GetMonsterFrameName(role, state, direction, frameIndex, "v002");
        }

        private static string GetMonsterFrameName(
            string role,
            string state,
            string direction,
            int frameIndex,
            string version)
        {
            Assert.That(frameIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(frameIndex, Is.LessThan(8));
            var frameLetter = "abcdefgh"[frameIndex];
            return $"chr_{role}_{state}_{direction}_{frameLetter}_{version}";
        }
        private static string GetMonsterMirrorSourceDirection(string direction)
        {
            switch (direction)
            {
                case "west":
                    return "east";
                case "southwest":
                    return "southeast";
                case "northwest":
                    return "northeast";
                default:
                    return null;
            }
        }

        private static CharacterDirection GetMonsterAnimationDirection(string direction)
        {
            switch (direction)
            {
                case "south":
                    return CharacterDirection.South;
                case "north":
                    return CharacterDirection.North;
                case "east":
                    return CharacterDirection.East;
                case "west":
                    return CharacterDirection.West;
                case "southeast":
                    return CharacterDirection.SouthEast;
                case "southwest":
                    return CharacterDirection.SouthWest;
                case "northeast":
                    return CharacterDirection.NorthEast;
                case "northwest":
                    return CharacterDirection.NorthWest;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown monster animation direction.");
            }
        }

        private static int GetArrayInteger(
            IReadOnlyList<CanonicalJsonValue> values,
            int index,
            string propertyName)
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(index, Is.LessThan(values.Count));
            var value = values[index];
            Assert.That(value.Kind, Is.EqualTo(CanonicalJsonKind.Number), $"'{propertyName}' must contain numbers.");
            Assert.That(value.NumberValue, Is.EqualTo(Math.Round(value.NumberValue)), $"'{propertyName}' must contain integers.");
            return (int)value.NumberValue;
        }

        private static void AssertStaticRgbaPngHeader(
            byte[] bytes,
            string assetPath,
            int expectedWidth,
            int expectedHeight)
        {
            Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(33), $"PNG is too short: {assetPath}");
            var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (var signatureIndex = 0; signatureIndex < signature.Length; signatureIndex++)
            {
                Assert.That(bytes[signatureIndex], Is.EqualTo(signature[signatureIndex]), $"PNG signature is invalid: {assetPath}");
            }

            Assert.That(ReadBigEndianInt32(bytes, 8, assetPath), Is.EqualTo(13), $"PNG IHDR length is invalid: {assetPath}");
            Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("IHDR"), $"PNG lacks an IHDR chunk: {assetPath}");
            Assert.That(ReadBigEndianInt32(bytes, 16, assetPath), Is.EqualTo(expectedWidth));
            Assert.That(ReadBigEndianInt32(bytes, 20, assetPath), Is.EqualTo(expectedHeight));
            Assert.That(bytes[24], Is.EqualTo(8), $"PNG bit depth must be 8: {assetPath}");
            Assert.That(bytes[25], Is.EqualTo(6), $"PNG color type must be RGBA: {assetPath}");
            Assert.That(bytes[26], Is.Zero, $"PNG compression method is invalid: {assetPath}");
            Assert.That(bytes[27], Is.Zero, $"PNG filter method is invalid: {assetPath}");
            Assert.That(bytes[28], Is.Zero, $"PNG interlace method is invalid: {assetPath}");
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
        private static float GetRequiredFloat(CanonicalJsonValue objectValue, string propertyName)
        {
            var property = GetRequiredProperty(objectValue, propertyName);
            Assert.That(property.Kind, Is.EqualTo(CanonicalJsonKind.Number), $"'{propertyName}' must be a number.");
            Assert.That(double.IsNaN(property.NumberValue), Is.False, $"'{propertyName}' must be finite.");
            Assert.That(double.IsInfinity(property.NumberValue), Is.False, $"'{propertyName}' must be finite.");
            Assert.That(property.NumberValue, Is.GreaterThanOrEqualTo(float.MinValue));
            Assert.That(property.NumberValue, Is.LessThanOrEqualTo(float.MaxValue));
            return (float)property.NumberValue;
        }

        private static bool GetRequiredBoolean(CanonicalJsonValue objectValue, string propertyName)
        {
            var property = GetRequiredProperty(objectValue, propertyName);
            Assert.That(property.Kind, Is.EqualTo(CanonicalJsonKind.Boolean), $"'{propertyName}' must be a boolean.");
            return property.BooleanValue;
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
        private readonly struct MonsterAnimationRoleSpec
        {
            public MonsterAnimationRoleSpec(
                string role,
                string atlasPath,
                string animationSetPath,
                bool usesMotionSheets = false,
                string motionFolder = "Motions",
                string motionVersion = "v002")
            {
                Role = role;
                AtlasPath = atlasPath;
                AnimationSetPath = animationSetPath;
                UsesMotionSheets = usesMotionSheets;
                MotionFolder = motionFolder;
                MotionVersion = motionVersion;
            }

            public string Role { get; }
            public string AtlasPath { get; }
            public string AnimationSetPath { get; }
            public bool UsesMotionSheets { get; }
            public string MotionFolder { get; }
            public string MotionVersion { get; }
            public string MotionSheetPath(string stateName)
            {
                return $"Assets/_Project/Art/M1Production/Characters/Animation/{MotionFolder}/chr_{Role}_{stateName}_motion_{MotionVersion}.png";
            }
        }

        private readonly struct MonsterAnimationStateSpec
        {
            public MonsterAnimationStateSpec(
                string name,
                CharacterAnimationState state,
                int frameCount,
                float framesPerSecond,
                bool loop)
            {
                Name = name;
                State = state;
                FrameCount = frameCount;
                FramesPerSecond = framesPerSecond;
                Loop = loop;
            }

            public string Name { get; }
            public CharacterAnimationState State { get; }
            public int FrameCount { get; }
            public float FramesPerSecond { get; }
            public bool Loop { get; }
        }
    }
}
