using System;
using System.Collections.Generic;
using System.IO;
using Overbless.Runtime;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Bootstrap
{
    internal readonly struct M1DirectionalAnimationAssets
    {
        public M1DirectionalAnimationAssets(
            DirectionalAnimationSet player,
            DirectionalAnimationSet dasher,
            DirectionalAnimationSet archer,
            DirectionalAnimationSet minion)
        {
            Player = player;
            Dasher = dasher;
            Archer = archer;
            Minion = minion;
        }

        public DirectionalAnimationSet Player { get; }
        public DirectionalAnimationSet Dasher { get; }
        public DirectionalAnimationSet Archer { get; }
        public DirectionalAnimationSet Minion { get; }
    }

    internal static class M1DirectionalAnimationBootstrap
    {
        private const int CellSize = 128;
        private const string FrameLetters = "abcdefgh";
        private const string AtlasRoot = "Assets/_Project/Art/M1Production/Characters/Animation";
        private const string MotionsV004Root = AtlasRoot + "/MotionsV004";
        private const string DataRoot = "Assets/_Project/Data/Animations";
        private const string V004AutoApplyEditorPrefKey = "Overbless.M1.V004MonsterAnimationsApplied.v1";

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

        private static readonly StateSpec[] PlayerStates =
        {
            new StateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new StateSpec(CharacterAnimationState.Walk, "move", 6, 10f, true),
            new StateSpec(CharacterAnimationState.Dash, "dash", 4, 14f, false),
            new StateSpec(CharacterAnimationState.BlessCast, "bless_cast", 6, 8f, true),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly StateSpec[] MajorEnemyStates =
        {
            new StateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new StateSpec(CharacterAnimationState.Walk, "walk", 6, 8f, true),
            new StateSpec(CharacterAnimationState.Run, "run", 8, 12f, true),
            new StateSpec(CharacterAnimationState.AttackCharge, "attack_charge", 6, 8f, false),
            new StateSpec(CharacterAnimationState.AttackExecute, "attack_execute", 6, 14f, false),
            new StateSpec(CharacterAnimationState.Recover, "recover", 4, 7f, false),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly StateSpec[] MinionStates =
        {
            new StateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new StateSpec(CharacterAnimationState.Walk, "walk", 6, 8f, true),
            new StateSpec(CharacterAnimationState.Run, "run", 8, 12f, true),
            new StateSpec(CharacterAnimationState.AttackCharge, "attack_charge", 6, 8f, false),
            new StateSpec(CharacterAnimationState.AttackExecute, "attack_execute", 6, 24f, false),
            new StateSpec(CharacterAnimationState.Recover, "recover", 4, 7f, false),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly AtlasSpec[] Atlases =
        {
            new AtlasSpec("player", "v001", 6, PlayerStates)
        };

        // v004 stores one normalized, transparent PNG per animation frame. Keeping
        // motions separate avoids the source-sheet cropping and per-action scale
        // drift that made the v002 monolithic monster atlases unsuitable at runtime.
        private static readonly V004RoleSpec[] V004Roles =
        {
            new V004RoleSpec("dasher", 224f),
            new V004RoleSpec("archer", 224f),
            new V004RoleSpec("minion", 208f)
        };

        private static readonly V004StateSpec[] V004States =
        {
            new V004StateSpec(CharacterAnimationState.Idle, "Idle", 4, 4f, true),
            new V004StateSpec(CharacterAnimationState.Walk, "Run", 8, 8f, true),
            new V004StateSpec(CharacterAnimationState.Run, "Run", 8, 12f, true),
            new V004StateSpec(CharacterAnimationState.AttackCharge, "Attack", 5, 8f, false),
            new V004StateSpec(CharacterAnimationState.AttackExecute, "Attack", 5, 14f, false),
            new V004StateSpec(CharacterAnimationState.Recover, "Idle", 4, 7f, false),
            new V004StateSpec(CharacterAnimationState.Hit, "Hurt", 3, 12f, false),
            new V004StateSpec(CharacterAnimationState.Death, "Death", 5, 8f, false)
        };

        [MenuItem("Overbless/M1/Refresh v004 Monster Animation Sets")]
        public static void RefreshV004MonsterAnimations()
        {
            CreateOrUpdate();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [InitializeOnLoadMethod]
        private static void ScheduleV004MonsterAnimationAutoApply()
        {
            if (EditorPrefs.GetBool(V004AutoApplyEditorPrefKey, false))
            {
                return;
            }

            EditorApplication.update -= ApplyV004MonsterAnimationsWhenReady;
            EditorApplication.update += ApplyV004MonsterAnimationsWhenReady;
        }

        private static void ApplyV004MonsterAnimationsWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            EditorApplication.update -= ApplyV004MonsterAnimationsWhenReady;
            try
            {
                CreateOrUpdate();
                EditorPrefs.SetBool(V004AutoApplyEditorPrefKey, true);
                Debug.Log("Applied v004 monster animation sets to existing M1/M2 prefabs and scene instances.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static M1DirectionalAnimationAssets CreateOrUpdate()
        {
            EnsureFolder(DataRoot);
            var sets = new Dictionary<string, DirectionalAnimationSet>(StringComparer.Ordinal);
            for (var index = 0; index < Atlases.Length; index++)
            {
                var spec = Atlases[index];
                ConfigureAtlasImporter(spec);
                sets.Add(spec.Role, CreateAnimationSet(spec));
            }

            for (var index = 0; index < V004Roles.Length; index++)
            {
                var spec = V004Roles[index];
                sets.Add(spec.Role, CreateV004AnimationSet(spec));
            }

            AssetDatabase.SaveAssets();
            for (var index = 0; index < Atlases.Length; index++)
            {
                var spec = Atlases[index];
                var assetPath = AnimationSetPath(spec.Role);
                var reloaded = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(assetPath);
                ValidateExactSet(reloaded, spec);
                sets[spec.Role] = reloaded;
            }

            for (var index = 0; index < V004Roles.Length; index++)
            {
                var spec = V004Roles[index];
                var assetPath = AnimationSetPath(spec.Role);
                var reloaded = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(assetPath);
                ValidateV004Set(reloaded, spec);
                sets[spec.Role] = reloaded;
            }

            return new M1DirectionalAnimationAssets(
                sets["player"],
                sets["dasher"],
                sets["archer"],
                sets["minion"]);
        }

        private static void ConfigureAtlasImporter(AtlasSpec spec)
        {
            AssetDatabase.ImportAsset(spec.AtlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(spec.AtlasPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Animation atlas '{spec.AtlasPath}' is missing or has no TextureImporter.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
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
            importer.maxTextureSize = 8192;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, 0f);
            importer.SetTextureSettings(settings);

#pragma warning disable CS0618
            importer.spritesheet = BuildMetadata(spec);
#pragma warning restore CS0618
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.AtlasPath);
            var expectedWidth = CellSize * spec.MaxFrames * Directions.Length;
            var expectedHeight = CellSize * spec.States.Length;
            if (texture == null || texture.width != expectedWidth || texture.height != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Animation atlas '{spec.AtlasPath}' must be {expectedWidth}x{expectedHeight}.");
            }
        }

        private static SpriteMetaData[] BuildMetadata(AtlasSpec spec)
        {
            var metadata = new List<SpriteMetaData>();
            var atlasHeight = CellSize * spec.States.Length;
            for (var stateIndex = 0; stateIndex < spec.States.Length; stateIndex++)
            {
                var state = spec.States[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                    {
                        var x = (directionIndex * spec.MaxFrames + frameIndex) * CellSize;
                        var y = atlasHeight - (stateIndex + 1) * CellSize;
                        metadata.Add(new SpriteMetaData
                        {
                            name = FrameName(spec.Role, state.Name, direction.Name, frameIndex, spec.Version),
                            rect = new Rect(x, y, CellSize, CellSize),
                            alignment = (int)SpriteAlignment.Custom,
                            pivot = new Vector2(0.5f, 0f),
                            border = Vector4.zero
                        });
                    }
                }
            }

            return metadata.ToArray();
        }

        private static DirectionalAnimationSet CreateAnimationSet(AtlasSpec spec)
        {
            var sprites = LoadSpritesByName(spec.AtlasPath);
            var assetPath = AnimationSetPath(spec.Role);
            var set = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(assetPath);
            if (set == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    throw new InvalidOperationException($"'{assetPath}' exists but is not a DirectionalAnimationSet.");
                }

                set = ScriptableObject.CreateInstance<DirectionalAnimationSet>();
                AssetDatabase.CreateAsset(set, assetPath);
            }

            var serialized = new SerializedObject(set);
            serialized.FindProperty("role").stringValue = spec.Role;
            var clips = serialized.FindProperty("clips");
            clips.arraySize = spec.States.Length * Directions.Length;
            var clipIndex = 0;
            for (var stateIndex = 0; stateIndex < spec.States.Length; stateIndex++)
            {
                var state = spec.States[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var clip = clips.GetArrayElementAtIndex(clipIndex++);
                    clip.FindPropertyRelative("state").intValue = (int)state.State;
                    clip.FindPropertyRelative("direction").intValue = (int)direction.Direction;
                    clip.FindPropertyRelative("framesPerSecond").floatValue = state.FramesPerSecond;
                    clip.FindPropertyRelative("loop").boolValue = state.Loop;
                    var frames = clip.FindPropertyRelative("frames");
                    frames.arraySize = state.FrameCount;
                    for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                    {
                        var name = FrameName(spec.Role, state.Name, direction.Name, frameIndex, spec.Version);
                        if (!sprites.TryGetValue(name, out var sprite))
                        {
                            throw new InvalidOperationException($"Animation atlas '{spec.AtlasPath}' is missing sprite '{name}'.");
                        }

                        frames.GetArrayElementAtIndex(frameIndex).objectReferenceValue = sprite;
                    }
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            ValidateExactSet(set, spec);
            return set;
        }

        private static DirectionalAnimationSet CreateV004AnimationSet(V004RoleSpec spec)
        {
            var assetPath = AnimationSetPath(spec.Role);
            var set = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(assetPath);
            if (set == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    throw new InvalidOperationException($"'{assetPath}' exists but is not a DirectionalAnimationSet.");
                }

                set = ScriptableObject.CreateInstance<DirectionalAnimationSet>();
                AssetDatabase.CreateAsset(set, assetPath);
            }

            var serialized = new SerializedObject(set);
            serialized.FindProperty("role").stringValue = spec.Role;
            var clips = serialized.FindProperty("clips");
            clips.arraySize = V004States.Length * Directions.Length;
            var clipIndex = 0;
            for (var stateIndex = 0; stateIndex < V004States.Length; stateIndex++)
            {
                var state = V004States[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var clip = clips.GetArrayElementAtIndex(clipIndex++);
                    clip.FindPropertyRelative("state").intValue = (int)state.State;
                    clip.FindPropertyRelative("direction").intValue = (int)direction.Direction;
                    clip.FindPropertyRelative("framesPerSecond").floatValue = state.FramesPerSecond;
                    clip.FindPropertyRelative("loop").boolValue = state.Loop;
                    var frames = clip.FindPropertyRelative("frames");
                    frames.arraySize = state.FrameCount;
                    for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                    {
                        frames.GetArrayElementAtIndex(frameIndex).objectReferenceValue =
                            LoadV004Frame(spec, state, direction, frameIndex);
                    }
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            ValidateV004Set(set, spec);
            return set;
        }

        private static Sprite LoadV004Frame(
            V004RoleSpec role,
            V004StateSpec state,
            DirectionSpec direction,
            int frameIndex)
        {
            var path = V004FramePath(role, state, direction, frameIndex);
            if (!File.Exists(Path.GetFullPath(path)))
            {
                throw new FileNotFoundException("Required v004 animation frame is missing.", path);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"v004 animation frame '{path}' has no TextureImporter.");
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            var needsImport = importer.textureType != TextureImporterType.Sprite ||
                              importer.spriteImportMode != SpriteImportMode.Single ||
                              !Mathf.Approximately(importer.spritePixelsPerUnit, role.PixelsPerUnit) ||
                              !importer.alphaIsTransparency ||
                              importer.filterMode != FilterMode.Point ||
                              importer.mipmapEnabled ||
                              importer.streamingMipmaps ||
                              importer.textureCompression != TextureImporterCompression.Uncompressed ||
                              importer.crunchedCompression ||
                              importer.wrapMode != TextureWrapMode.Clamp ||
                              importer.npotScale != TextureImporterNPOTScale.None ||
                              settings.spriteMeshType != SpriteMeshType.FullRect ||
                              settings.spriteExtrude != 0 ||
                              settings.spriteAlignment != (int)SpriteAlignment.Custom ||
                              settings.spritePivot != new Vector2(0.5f, 0f);
            if (needsImport)
            {
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteExtrude = 0;
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(0.5f, 0f);
                importer.SetTextureSettings(settings);

                // SetTextureSettings can restore its old default texture mode. Keep
                // all importer-level fields after it so Unity persists this file as
                // a Sprite instead of silently returning it to Texture2D/Default.
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.spritePixelsPerUnit = role.PixelsPerUnit;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.streamingMipmaps = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                var importedAssets = AssetDatabase.LoadAllAssetsAtPath(path);
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
                throw new InvalidOperationException($"v004 animation frame '{path}' did not import as a Sprite.");
            }

            return sprite;
        }

        private static void ValidateV004Set(DirectionalAnimationSet set, V004RoleSpec spec)
        {
            if (set == null)
            {
                throw new InvalidOperationException(
                    $"Directional animation set '{AnimationSetPath(spec.Role)}' failed to reload.");
            }

            set.Validate();
            var expectedClipCount = V004States.Length * Directions.Length;
            if (!string.Equals(set.Role, spec.Role, StringComparison.Ordinal) ||
                set.ClipCount != expectedClipCount)
            {
                throw new InvalidOperationException(
                    $"Animation set '{spec.Role}' must contain exactly {expectedClipCount} v004 clips.");
            }

            for (var stateIndex = 0; stateIndex < V004States.Length; stateIndex++)
            {
                var state = V004States[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var clip = set.GetClip(state.State, direction.Direction);
                    if (clip.FrameCount != state.FrameCount ||
                        !Mathf.Approximately(clip.FramesPerSecond, state.FramesPerSecond) ||
                        clip.Loop != state.Loop)
                    {
                        throw new InvalidOperationException(
                            $"Animation set '{spec.Role}' has an invalid v004 {state.State}/{direction.Direction} contract.");
                    }

                    for (var frameIndex = 0; frameIndex < clip.FrameCount; frameIndex++)
                    {
                        var expectedPath = V004FramePath(spec, state, direction, frameIndex);
                        var sprite = clip.GetFrame(frameIndex);
                        if (sprite == null ||
                            !string.Equals(AssetDatabase.GetAssetPath(sprite), expectedPath, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Animation set '{spec.Role}' contains a stale v004 frame for {state.State}/{direction.Direction}.");
                        }
                    }
                }
            }
        }

        private static string V004FramePath(
            V004RoleSpec role,
            V004StateSpec state,
            DirectionSpec direction,
            int frameIndex)
        {
            return $"{MotionsV004Root}/{UppercaseFirst(role.Role)}/{V004SourceDirection(direction)}/{state.SourceFolder}/frame-{frameIndex}.png";
        }

        private static string V004SourceDirection(DirectionSpec direction)
        {
            switch (direction.Direction)
            {
                case CharacterDirection.SouthEast:
                case CharacterDirection.SouthWest:
                    return "South";
                case CharacterDirection.NorthEast:
                case CharacterDirection.NorthWest:
                    return "North";
                default:
                    return UppercaseFirst(direction.Name);
            }
        }

        private static void ValidateExactSet(DirectionalAnimationSet set, AtlasSpec spec)
        {
            if (set == null)
            {
                throw new InvalidOperationException(
                    $"Directional animation set '{AnimationSetPath(spec.Role)}' failed to reload.");
            }

            set.Validate();
            var expectedClipCount = spec.States.Length * Directions.Length;
            if (!string.Equals(set.Role, spec.Role, StringComparison.Ordinal) ||
                set.ClipCount != expectedClipCount)
            {
                throw new InvalidOperationException(
                    $"Animation set '{spec.Role}' must contain exactly {expectedClipCount} clips.");
            }

            for (var stateIndex = 0; stateIndex < spec.States.Length; stateIndex++)
            {
                var state = spec.States[stateIndex];
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var clip = set.GetClip(state.State, direction.Direction);
                    if (clip.FrameCount != state.FrameCount ||
                        !Mathf.Approximately(clip.FramesPerSecond, state.FramesPerSecond) ||
                        clip.Loop != state.Loop)
                    {
                        throw new InvalidOperationException(
                            $"Animation set '{spec.Role}' has an invalid {state.State}/{direction.Direction} contract.");
                    }

                    for (var frameIndex = 0; frameIndex < clip.FrameCount; frameIndex++)
                    {
                        var sprite = clip.GetFrame(frameIndex);
                        var expectedName = FrameName(spec.Role, state.Name, direction.Name, frameIndex, spec.Version);
                        if (sprite == null || !string.Equals(sprite.name, expectedName, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Animation set '{spec.Role}' contains a stale frame for {state.State}/{direction.Direction}.");
                        }
                    }
                }
            }
        }

        private static string AnimationSetPath(string role)
        {
            return $"{DataRoot}/{UppercaseFirst(role)}DirectionalAnimationSet.asset";
        }

        private static Dictionary<string, Sprite> LoadSpritesByName(string atlasPath)
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var assets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
            for (var index = 0; index < assets.Length; index++)
            {
                if (!(assets[index] is Sprite sprite))
                {
                    continue;
                }

                if (!result.TryAdd(sprite.name, sprite))
                {
                    throw new InvalidOperationException($"Animation atlas '{atlasPath}' contains duplicate sprite '{sprite.name}'.");
                }
            }

            return result;
        }

        private static string FrameName(
            string role,
            string state,
            string direction,
            int frameIndex,
            string version)
        {
            return $"chr_{role}_{state}_{direction}_{FrameLetters[frameIndex]}_{version}";
        }

        private static string UppercaseFirst(string value)
        {
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var separator = assetPath.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new InvalidOperationException($"Cannot create asset folder '{assetPath}'.");
            }

            var parent = assetPath.Substring(0, separator);
            var name = assetPath.Substring(separator + 1);
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
            {
                throw new InvalidOperationException($"Unity failed to create asset folder '{assetPath}'.");
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

        private readonly struct StateSpec
        {
            public StateSpec(CharacterAnimationState state, string name, int frameCount, float framesPerSecond, bool loop)
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

        private readonly struct V004RoleSpec
        {
            public V004RoleSpec(string role, float pixelsPerUnit)
            {
                Role = role;
                PixelsPerUnit = pixelsPerUnit;
            }

            public string Role { get; }
            public float PixelsPerUnit { get; }
        }

        private readonly struct V004StateSpec
        {
            public V004StateSpec(
                CharacterAnimationState state,
                string sourceFolder,
                int frameCount,
                float framesPerSecond,
                bool loop)
            {
                State = state;
                SourceFolder = sourceFolder;
                FrameCount = frameCount;
                FramesPerSecond = framesPerSecond;
                Loop = loop;
            }

            public CharacterAnimationState State { get; }
            public string SourceFolder { get; }
            public int FrameCount { get; }
            public float FramesPerSecond { get; }
            public bool Loop { get; }
        }

        private readonly struct AtlasSpec
        {
            public AtlasSpec(string role, string version, int maxFrames, StateSpec[] states)
            {
                Role = role;
                Version = version;
                MaxFrames = maxFrames;
                States = states;
            }

            public string Role { get; }
            public string Version { get; }
            public int MaxFrames { get; }
            public StateSpec[] States { get; }
            public string AtlasPath => $"{AtlasRoot}/chr_{Role}_animation_atlas_{Version}.png";
        }
    }
}
