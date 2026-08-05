using System;
using System.Collections.Generic;
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
        private const string DataRoot = "Assets/_Project/Data/Animations";

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
            new StateSpec(CharacterAnimationState.Walk, "walk", 4, 6f, true),
            new StateSpec(CharacterAnimationState.Run, "run", 4, 9f, true),
            new StateSpec(CharacterAnimationState.AttackCharge, "attack_charge", 6, 8f, false),
            new StateSpec(CharacterAnimationState.AttackExecute, "attack_execute", 6, 14f, false),
            new StateSpec(CharacterAnimationState.Recover, "recover", 4, 7f, false),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly StateSpec[] MinionStates =
        {
            new StateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new StateSpec(CharacterAnimationState.Walk, "walk", 4, 6f, true),
            new StateSpec(CharacterAnimationState.Run, "run", 4, 9f, true),
            new StateSpec(CharacterAnimationState.AttackCharge, "attack_charge", 6, 8f, false),
            new StateSpec(CharacterAnimationState.AttackExecute, "attack_execute", 6, 24f, false),
            new StateSpec(CharacterAnimationState.Recover, "recover", 4, 7f, false),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly AtlasSpec[] Atlases =
        {
            new AtlasSpec("player", "v001", 6, PlayerStates),
            new AtlasSpec("dasher", "v002", 8, MajorEnemyStates),
            new AtlasSpec("archer", "v003", 8, MajorEnemyStates, true, "MotionsV003"),
            new AtlasSpec("minion", "v002", 8, MinionStates)
        };

        public static M1DirectionalAnimationAssets CreateOrUpdate()
        {
            EnsureFolder(DataRoot);
            var sets = new Dictionary<string, DirectionalAnimationSet>(StringComparer.Ordinal);
            for (var index = 0; index < Atlases.Length; index++)
            {
                var spec = Atlases[index];
                ConfigureSpriteImporters(spec);
                sets.Add(spec.Role, CreateAnimationSet(spec));
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

            return new M1DirectionalAnimationAssets(
                sets["player"],
                sets["dasher"],
                sets["archer"],
                sets["minion"]);
        }

        private static void ConfigureSpriteImporters(AtlasSpec spec)
        {
            if (!spec.UsesMotionSheets)
            {
                ConfigureAtlasImporter(spec);
                return;
            }

            for (var stateIndex = 0; stateIndex < spec.States.Length; stateIndex++)
            {
                ConfigureMotionSheetImporter(spec, spec.States[stateIndex]);
            }
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

        private static void ConfigureMotionSheetImporter(AtlasSpec spec, StateSpec state)
        {
            var motionPath = spec.MotionPath(state);
            AssetDatabase.ImportAsset(motionPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(motionPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Animation motion sheet '{motionPath}' is missing or has no TextureImporter.");
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
            importer.spritesheet = BuildMotionMetadata(spec, state);
#pragma warning restore CS0618
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(motionPath);
            var expectedWidth = CellSize * state.FrameCount * Directions.Length;
            if (texture == null || texture.width != expectedWidth || texture.height != CellSize)
            {
                throw new InvalidOperationException(
                    $"Animation motion sheet '{motionPath}' must be {expectedWidth}x{CellSize}.");
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

        private static SpriteMetaData[] BuildMotionMetadata(AtlasSpec spec, StateSpec state)
        {
            var metadata = new List<SpriteMetaData>();
            for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
            {
                var direction = Directions[directionIndex];
                for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                {
                    metadata.Add(new SpriteMetaData
                    {
                        name = FrameName(spec.Role, state.Name, direction.Name, frameIndex, spec.Version),
                        rect = new Rect((directionIndex * state.FrameCount + frameIndex) * CellSize, 0, CellSize, CellSize),
                        alignment = (int)SpriteAlignment.Custom,
                        pivot = new Vector2(0.5f, 0f),
                        border = Vector4.zero
                    });
                }
            }

            return metadata.ToArray();
        }

        private static DirectionalAnimationSet CreateAnimationSet(AtlasSpec spec)
        {
            var sprites = LoadSpritesByName(spec);
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

        private static Dictionary<string, Sprite> LoadSpritesByName(AtlasSpec spec)
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var spritePaths = spec.UsesMotionSheets ? spec.MotionPaths() : new[] { spec.AtlasPath };
            for (var pathIndex = 0; pathIndex < spritePaths.Length; pathIndex++)
            {
                var spritePath = spritePaths[pathIndex];
                var assets = AssetDatabase.LoadAllAssetsAtPath(spritePath);
                for (var index = 0; index < assets.Length; index++)
                {
                    if (!(assets[index] is Sprite sprite))
                    {
                        continue;
                    }

                    if (!result.TryAdd(sprite.name, sprite))
                    {
                        throw new InvalidOperationException($"Animation source '{spritePath}' contains duplicate sprite '{sprite.name}'.");
                    }
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

        private readonly struct AtlasSpec
        {
            public AtlasSpec(
                string role,
                string version,
                int maxFrames,
                StateSpec[] states,
                bool usesMotionSheets = false,
                string motionFolder = "Motions")
            {
                Role = role;
                Version = version;
                MaxFrames = maxFrames;
                States = states;
                UsesMotionSheets = usesMotionSheets;
                MotionFolder = motionFolder;
            }

            public string Role { get; }
            public string Version { get; }
            public int MaxFrames { get; }
            public StateSpec[] States { get; }
            public bool UsesMotionSheets { get; }
            public string MotionFolder { get; }
            public string AtlasPath => $"{AtlasRoot}/chr_{Role}_animation_atlas_{Version}.png";
            public string MotionPath(StateSpec state) => $"{AtlasRoot}/{MotionFolder}/chr_{Role}_{state.Name}_motion_{Version}.png";
            public string[] MotionPaths()
            {
                var paths = new string[States.Length];
                for (var stateIndex = 0; stateIndex < States.Length; stateIndex++)
                {
                    paths[stateIndex] = MotionPath(States[stateIndex]);
                }

                return paths;
            }
        }
    }
}
