using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Overbless.Editor.Audio;
namespace Overbless.Editor.Bootstrap
{
    public static class ProjectBootstrap
    {
        private const string ProjectRoot = "Assets/_Project";
        private const int FirstProjectLayer = 8;
        private const int InputSystemPackageOnly = 1;

        private static readonly string[] RequiredDirectories =
        {
            ProjectRoot,
            ProjectRoot + "/Art",
            ProjectRoot + "/Audio",
            ProjectRoot + "/Data",
            ProjectRoot + "/Editor",
            ProjectRoot + "/Editor/Bootstrap",
            ProjectRoot + "/Prefabs",
            ProjectRoot + "/Runtime",
            ProjectRoot + "/Scenes"
        };

        private static readonly string[] RequiredSortingLayers =
        {
            "Background",
            "World",
            "Actors",
            "VFX",
            "Telegraph",
            "UI"
        };

        private static readonly string[] RequiredPhysicsLayers =
        {
            "Player",
            "EnemyBody",
            "EnemyAttack",
            "Projectile",
            "World",
            "Pickup",
            "Exit"
        };

        public static void Configure()
        {
            CreateRequiredDirectories();
            ConfigureInputHandling();
            ConfigureTagsAndLayers();
            ConfigureWebGLBuild();
            ProceduralAudioGenerator.GenerateAll();
            M1ContentBootstrap.CreateOrUpdate();
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateRequiredDirectories()
        {
            foreach (var directory in RequiredDirectories)
            {
                EnsureAssetDirectory(directory);
            }
        }

        private static void EnsureAssetDirectory(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parentPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parentPath) || !AssetDatabase.IsValidFolder(parentPath))
            {
                throw new InvalidOperationException($"Cannot create '{assetPath}' because its parent folder is unavailable.");
            }

            var folderName = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(AssetDatabase.CreateFolder(parentPath, folderName)))
            {
                throw new InvalidOperationException($"Unity failed to create required folder '{assetPath}'.");
            }
        }

        private static void ConfigureInputHandling()
        {
            var projectSettings = new SerializedObject(LoadSettingsAsset("ProjectSettings/ProjectSettings.asset"));
            var activeInputHandler = RequireProperty(projectSettings, "activeInputHandler");
            activeInputHandler.intValue = InputSystemPackageOnly;
            projectSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureTagsAndLayers()
        {
            var tagManager = new SerializedObject(LoadSettingsAsset("ProjectSettings/TagManager.asset"));
            ConfigureSortingLayers(tagManager);
            ConfigurePhysicsLayers(tagManager);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            ConfigurePhysics2DCollisionMatrix();
        }

        private static void ConfigureSortingLayers(SerializedObject tagManager)
        {
            var sortingLayers = RequireProperty(tagManager, "m_SortingLayers");

            foreach (var sortingLayerName in RequiredSortingLayers)
            {
                var matchingLayerIndex = FindSortingLayerIndex(sortingLayers, sortingLayerName);
                if (matchingLayerIndex >= 0)
                {
                    continue;
                }

                var uniqueId = GetUniqueSortingLayerId(sortingLayers, sortingLayerName);
                sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
                var sortingLayer = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
                RequireProperty(sortingLayer, "name").stringValue = sortingLayerName;
                RequireProperty(sortingLayer, "uniqueID").intValue = uniqueId;
                RequireProperty(sortingLayer, "locked").boolValue = false;
            }
        }

        private static int FindSortingLayerIndex(SerializedProperty sortingLayers, string sortingLayerName)
        {
            var matchingLayerIndex = -1;
            for (var index = 0; index < sortingLayers.arraySize; index++)
            {
                var layer = sortingLayers.GetArrayElementAtIndex(index);
                if (RequireProperty(layer, "name").stringValue != sortingLayerName)
                {
                    continue;
                }

                if (matchingLayerIndex >= 0)
                {
                    throw new InvalidOperationException($"Sorting layer '{sortingLayerName}' is configured more than once.");
                }

                matchingLayerIndex = index;
            }

            return matchingLayerIndex;
        }

        private static int GetUniqueSortingLayerId(SerializedProperty sortingLayers, string sortingLayerName)
        {
            var uniqueId = GetStableSortingLayerId(sortingLayerName);
            if (SortingLayerIdExists(sortingLayers, uniqueId))
            {
                throw new InvalidOperationException(
                    $"Sorting layer '{sortingLayerName}' cannot use its deterministic identifier because it is already assigned.");
            }

            return uniqueId;
        }

        private static int GetStableSortingLayerId(string sortingLayerName)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;

            foreach (var character in sortingLayerName)
            {
                hash ^= character;
                hash *= prime;
            }

            var uniqueId = (int)(hash & 0x7FFFFFFFu);
            if (uniqueId == 0)
            {
                throw new InvalidOperationException(
                    $"Sorting layer '{sortingLayerName}' cannot use a zero identifier.");
            }

            return uniqueId;
        }

        private static bool SortingLayerIdExists(SerializedProperty sortingLayers, int candidateId)
        {
            for (var index = 0; index < sortingLayers.arraySize; index++)
            {
                var layer = sortingLayers.GetArrayElementAtIndex(index);
                if (RequireProperty(layer, "uniqueID").intValue == candidateId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ConfigurePhysicsLayers(SerializedObject tagManager)
        {
            var layers = RequireProperty(tagManager, "layers");
            if (layers.arraySize != 32)
            {
                throw new InvalidOperationException("Unity's layer configuration must contain exactly 32 layer slots.");
            }

            for (var offset = 0; offset < RequiredPhysicsLayers.Length; offset++)
            {
                var layer = layers.GetArrayElementAtIndex(FirstProjectLayer + offset);
                var configuredName = layer.stringValue;
                var requiredName = RequiredPhysicsLayers[offset];

                if (!string.IsNullOrEmpty(configuredName) && configuredName != requiredName)
                {
                    throw new InvalidOperationException(
                        $"Layer {FirstProjectLayer + offset} is already assigned to '{configuredName}', not '{requiredName}'.");
                }

                layer.stringValue = requiredName;
            }
        }

        private static void ConfigurePhysics2DCollisionMatrix()
        {
            for (var firstOffset = 0; firstOffset < RequiredPhysicsLayers.Length; firstOffset++)
            {
                for (var secondOffset = firstOffset; secondOffset < RequiredPhysicsLayers.Length; secondOffset++)
                {
                    Physics2D.IgnoreLayerCollision(
                        FirstProjectLayer + firstOffset,
                        FirstProjectLayer + secondOffset,
                        !ShouldPhysicsLayersCollide(RequiredPhysicsLayers[firstOffset], RequiredPhysicsLayers[secondOffset]));
                }
            }
        }

        private static bool ShouldPhysicsLayersCollide(string firstLayer, string secondLayer)
        {
            if (firstLayer == "Player")
            {
                return secondLayer == "World" ||
                       secondLayer == "EnemyBody" ||
                       secondLayer == "EnemyAttack" ||
                       secondLayer == "Projectile" ||
                       secondLayer == "Pickup" ||
                       secondLayer == "Exit";
            }

            if (secondLayer == "Player")
            {
                return ShouldPhysicsLayersCollide(secondLayer, firstLayer);
            }

            if (firstLayer == "EnemyBody")
            {
                return secondLayer == "World" ||
                       secondLayer == "EnemyBody" ||
                       secondLayer == "EnemyAttack" ||
                       secondLayer == "Projectile";
            }

            if (secondLayer == "EnemyBody")
            {
                return ShouldPhysicsLayersCollide(secondLayer, firstLayer);
            }

            if (firstLayer == "EnemyAttack")
            {
                return secondLayer == "Player" || secondLayer == "EnemyBody";
            }

            if (secondLayer == "EnemyAttack")
            {
                return ShouldPhysicsLayersCollide(secondLayer, firstLayer);
            }

            if (firstLayer == "Projectile")
            {
                return secondLayer == "Player" ||
                       secondLayer == "EnemyBody" ||
                       secondLayer == "World";
            }

            if (secondLayer == "Projectile")
            {
                return ShouldPhysicsLayersCollide(secondLayer, firstLayer);
            }

            if (firstLayer == "Pickup" || firstLayer == "Exit")
            {
                return secondLayer == "Player";
            }

            if (secondLayer == "Pickup" || secondLayer == "Exit")
            {
                return firstLayer == "Player";
            }

            return false;
        }

        private static void ConfigureWebGLBuild()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                throw new InvalidOperationException("Unity failed to switch the active build target to WebGL.");
            }

            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
        }


        private static void ConfigureBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(M1ContentBootstrap.ScenePath, true)
            };
        }

        private static UnityEngine.Object LoadSettingsAsset(string path)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets.Length != 1 || assets[0] == null)
            {
                throw new InvalidOperationException($"Expected exactly one settings asset at '{path}'.");
            }

            return assets[0];
        }

        private static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyPath)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                throw new InvalidOperationException($"Unity setting '{propertyPath}' is unavailable.");
            }

            return property;
        }

        private static SerializedProperty RequireProperty(SerializedProperty parent, string propertyPath)
        {
            var property = parent.FindPropertyRelative(propertyPath);
            if (property == null)
            {
                throw new InvalidOperationException($"Unity setting '{propertyPath}' is unavailable.");
            }

            return property;
        }
    }
}
