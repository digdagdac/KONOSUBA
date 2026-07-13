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
        private const int MaxFrames = 6;
        private const string AtlasRoot = "Assets/_Project/Art/M1Production/Characters/Animation";
        private const string DataRoot = "Assets/_Project/Data/Animations";
        private const string FrameLetters = "abcdef";

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
            new StateSpec(CharacterAnimationState.Move, "move", 6, 10f, true),
            new StateSpec(CharacterAnimationState.Dash, "dash", 4, 14f, false),
            new StateSpec(CharacterAnimationState.BlessCast, "bless_cast", 6, 8f, true),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly StateSpec[] MajorEnemyStates =
        {
            new StateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new StateSpec(CharacterAnimationState.Move, "move", 6, 9f, true),
            new StateSpec(CharacterAnimationState.AttackCharge, "attack_charge", 6, 8f, true),
            new StateSpec(CharacterAnimationState.AttackExecute, "attack_execute", 4, 14f, false),
            new StateSpec(CharacterAnimationState.Recover, "recover", 4, 7f, false),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly StateSpec[] MinionStates =
        {
            new StateSpec(CharacterAnimationState.Idle, "idle", 4, 4f, true),
            new StateSpec(CharacterAnimationState.Move, "move", 6, 10f, true),
            new StateSpec(CharacterAnimationState.BasicAttack, "basic_attack", 4, 12f, false),
            new StateSpec(CharacterAnimationState.Hit, "hit", 3, 12f, false),
            new StateSpec(CharacterAnimationState.Death, "death", 6, 8f, false)
        };

        private static readonly AtlasSpec[] Atlases =
        {
            new AtlasSpec("player", PlayerStates),
            new AtlasSpec("dasher", MajorEnemyStates),
            new AtlasSpec("archer", MajorEnemyStates),
            new AtlasSpec("minion", MinionStates)
        };

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
            var expectedWidth = CellSize * MaxFrames * Directions.Length;
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
                        var x = (directionIndex * MaxFrames + frameIndex) * CellSize;
                        var y = atlasHeight - (stateIndex + 1) * CellSize;
                        metadata.Add(new SpriteMetaData
                        {
                            name = FrameName(spec.Role, state.Name, direction.Name, frameIndex),
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
            var assetPath = $"{DataRoot}/{UppercaseFirst(spec.Role)}DirectionalAnimationSet.asset";
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
                    clip.FindPropertyRelative("state").enumValueIndex = (int)state.State;
                    clip.FindPropertyRelative("direction").enumValueIndex = (int)direction.Direction;
                    clip.FindPropertyRelative("framesPerSecond").floatValue = state.FramesPerSecond;
                    clip.FindPropertyRelative("loop").boolValue = state.Loop;
                    var frames = clip.FindPropertyRelative("frames");
                    frames.arraySize = state.FrameCount;
                    for (var frameIndex = 0; frameIndex < state.FrameCount; frameIndex++)
                    {
                        var name = FrameName(spec.Role, state.Name, direction.Name, frameIndex);
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
            set.Validate();
            return set;
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

        private static string FrameName(string role, string state, string direction, int frameIndex)
        {
            return $"chr_{role}_{state}_{direction}_{FrameLetters[frameIndex]}_v001";
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
            public AtlasSpec(string role, StateSpec[] states)
            {
                Role = role;
                States = states;
            }

            public string Role { get; }
            public StateSpec[] States { get; }
            public string AtlasPath => $"{AtlasRoot}/chr_{Role}_animation_atlas_v001.png";
        }
    }
}
