using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Overbless.Runtime;

namespace Overbless.Editor.Evidence
{
    /// <summary>Captures the small, required project-configuration surface without mutating project settings.</summary>
    public static class ProjectConfigExporter
    {
        public const string Schema = "overbless.source-project-config/v1";
        public const string DefaultOutputPath = "Evidence/project-config.json";
        private const int InputSystemPackageOnly = 1;
        private const string RequiredUnityVersion = "6000.0.72f1";

        public static CanonicalJsonValue CreateSnapshot()
        {
            var failures = new List<string>();
            var unityVersion = Application.unityVersion;
            if (!string.Equals(unityVersion, RequiredUnityVersion, StringComparison.Ordinal)) failures.Add("UNITY_VERSION_MISMATCH");
            var packages = ReadDirectPackages(failures);
            var packageLockSha256 = ReadPackageLockHash(failures);
            var input = ReadInputConfiguration(failures);
            var renderer = ReadRendererConfiguration(failures);
            var scenes = ReadBuildScenes(failures);
            var buildSettings = ReadBuildSettings(scenes, failures);
            var displayPolicy = ReadDisplayPolicy(scenes, failures);
            var addressablesPresent = ContainsAddressables(packages) || ContainsAddressablesInPackageLock(failures);
            if (addressablesPresent) failures.Add("ADDRESSABLES_PRESENT");

            SortAndRejectDuplicateFailures(failures);
            var failureValues = new List<CanonicalJsonValue>();
            foreach (var failure in failures) failureValues.Add(CanonicalJsonValue.String(failure));

            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("addressablesPresent", CanonicalJsonValue.Boolean(addressablesPresent)),
                new CanonicalJsonProperty("buildSettings", buildSettings),
                new CanonicalJsonProperty("directPackages", ToPackageArray(packages)),
                new CanonicalJsonProperty("displayPolicy", displayPolicy),
                new CanonicalJsonProperty("failureCodes", CanonicalJsonValue.Array(failureValues)),
                new CanonicalJsonProperty("input", input),
                new CanonicalJsonProperty("packageLockSha256", packageLockSha256 == null ? CanonicalJsonValue.Null() : CanonicalJsonValue.String(packageLockSha256)),
                new CanonicalJsonProperty("renderer", renderer),
                new CanonicalJsonProperty("scene", CanonicalJsonValue.String(scenes.Count == 1 ? scenes[0] : string.Empty)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(Schema)),
                new CanonicalJsonProperty("snapshotStatus", CanonicalJsonValue.String(failures.Count == 0 ? "PASS" : "FAIL")),
                new CanonicalJsonProperty("unityVersion", CanonicalJsonValue.String(unityVersion)));
        }

        public static string Export(string outputPath)
        {
            var snapshot = CreateSnapshot();
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(snapshot, Schema, new[]
            {
                "schema", "unityVersion", "directPackages", "packageLockSha256", "renderer", "input", "addressablesPresent", "scene", "buildSettings", "displayPolicy", "snapshotStatus", "failureCodes"
            });
            if (!shape.IsValid) throw new InvalidOperationException("Project configuration snapshot shape is invalid: " + shape.Code + ".");

            var bytes = CanonicalJson.SerializeUtf8(snapshot);
            EvidenceArtifactIO.WriteNew(outputPath, bytes);
            return CanonicalJson.Sha256Hex(bytes);
        }

        /// <summary>Batch-mode entry point. Pass -projectConfigOutput &lt;normalized Evidence path&gt; to override the output.</summary>
        public static void Execute()
        {
            string outputPath;
            if (!TryGetCommandLineArgument("-projectConfigOutput", out outputPath)) outputPath = DefaultOutputPath;
            var sha256 = Export(outputPath);
            Debug.Log("Wrote project configuration snapshot " + outputPath + " (" + sha256 + ").");
        }

        private static List<PackageReference> ReadDirectPackages(ICollection<string> failures)
        {
            var result = new List<PackageReference>();
            var manifestPath = "Packages/manifest.json";
            byte[] bytes;
            try
            {
                bytes = EvidenceArtifactIO.ReadAllBytes(manifestPath);
            }
            catch (FileNotFoundException)
            {
                failures.Add("PACKAGE_MANIFEST_MISSING");
                return result;
            }

            CanonicalJsonValue manifest;
            string error;
            if (!CanonicalJson.TryParseUtf8(bytes, out manifest, out error) || manifest == null || manifest.Kind != CanonicalJsonKind.Object)
            {
                failures.Add("PACKAGE_MANIFEST_INVALID");
                return result;
            }

            CanonicalJsonValue dependencies;
            if (!manifest.TryGetSingleProperty("dependencies", out dependencies) || dependencies.Kind != CanonicalJsonKind.Object)
            {
                failures.Add("PACKAGE_DEPENDENCIES_MISSING");
                return result;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in dependencies.Properties)
            {
                if (string.IsNullOrEmpty(property.Name) || property.Value.Kind != CanonicalJsonKind.String || string.IsNullOrEmpty(property.Value.StringValue))
                {
                    failures.Add("PACKAGE_DEPENDENCIES_INVALID");
                    return new List<PackageReference>();
                }
                if (!ids.Add(property.Name))
                {
                    failures.Add("PACKAGE_DEPENDENCIES_DUPLICATE");
                    return new List<PackageReference>();
                }
                result.Add(new PackageReference(property.Name, property.Value.StringValue));
            }

            result.Sort(ComparePackageReferences);
            return result;
        }

        private static string ReadPackageLockHash(ICollection<string> failures)
        {
            try
            {
                return CanonicalJson.Sha256Hex(EvidenceArtifactIO.ReadAllBytes("Packages/packages-lock.json"));
            }
            catch (FileNotFoundException)
            {
                failures.Add("PACKAGE_LOCK_MISSING");
                return null;
            }
        }

        private static CanonicalJsonValue ReadInputConfiguration(ICollection<string> failures)
        {
            var mode = -1;
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (assets.Length != 1 || assets[0] == null) throw new InvalidOperationException();
                var serialized = new SerializedObject(assets[0]);
                var property = serialized.FindProperty("activeInputHandler");
                if (property == null) throw new InvalidOperationException();
                mode = property.intValue;
            }
            catch (ArgumentException)
            {
                failures.Add("INPUT_CONFIGURATION_UNAVAILABLE");
            }
            catch (InvalidOperationException)
            {
                failures.Add("INPUT_CONFIGURATION_UNAVAILABLE");
            }
            catch (UnityException)
            {
                failures.Add("INPUT_CONFIGURATION_UNAVAILABLE");
            }

            if (mode != InputSystemPackageOnly) failures.Add("INPUT_NOT_PACKAGE_ONLY");
            return CanonicalJsonValue.String(mode == InputSystemPackageOnly ? "InputSystem" : "Unexpected");
        }

        private static CanonicalJsonValue ReadRendererConfiguration(ICollection<string> failures)
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
            {
                failures.Add("RENDERER_MISSING");
                return CanonicalJsonValue.String("Missing");
            }

            var pipelineType = pipeline.GetType().FullName ?? pipeline.GetType().Name;
            if (!string.Equals(pipelineType, "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset", StringComparison.Ordinal))
            {
                failures.Add("RENDERER_NOT_URP");
                return CanonicalJsonValue.String("Unexpected");
            }

            var pipelinePath = AssetDatabase.GetAssetPath(pipeline);
            if (!CanonicalJson.IsNormalizedRelativePath(pipelinePath))
            {
                failures.Add("RENDERER_PATH_INVALID");
                return CanonicalJsonValue.String("Unexpected");
            }

            try
            {
                var serialized = new SerializedObject(pipeline);
                var rendererList = serialized.FindProperty("m_RendererDataList");
                var defaultRendererIndex = serialized.FindProperty("m_DefaultRendererIndex");
                if (rendererList == null || !rendererList.isArray || defaultRendererIndex == null || defaultRendererIndex.intValue < 0 || defaultRendererIndex.intValue >= rendererList.arraySize)
                {
                    failures.Add("RENDERER_DATA_UNAVAILABLE");
                    return CanonicalJsonValue.String("Unexpected");
                }

                var rendererData = rendererList.GetArrayElementAtIndex(defaultRendererIndex.intValue).objectReferenceValue;
                if (rendererData == null)
                {
                    failures.Add("RENDERER_DATA_UNAVAILABLE");
                    return CanonicalJsonValue.String("Unexpected");
                }

                var rendererDataPath = AssetDatabase.GetAssetPath(rendererData);
                var rendererDataType = rendererData.GetType().FullName ?? rendererData.GetType().Name;
                if (!CanonicalJson.IsNormalizedRelativePath(rendererDataPath))
                {
                    failures.Add("RENDERER_DATA_PATH_INVALID");
                    return CanonicalJsonValue.String("Unexpected");
                }
                if (!string.Equals(rendererDataType, "UnityEngine.Rendering.Universal.Renderer2DData", StringComparison.Ordinal))
                {
                    failures.Add("RENDERER_NOT_2D");
                    return CanonicalJsonValue.String("Unexpected");
                }
            }
            catch (ArgumentException)
            {
                failures.Add("RENDERER_CONFIGURATION_UNAVAILABLE");
                return CanonicalJsonValue.String("Unexpected");
            }
            catch (InvalidOperationException)
            {
                failures.Add("RENDERER_CONFIGURATION_UNAVAILABLE");
                return CanonicalJsonValue.String("Unexpected");
            }
            catch (UnityException)
            {
                failures.Add("RENDERER_CONFIGURATION_UNAVAILABLE");
                return CanonicalJsonValue.String("Unexpected");
            }

            return CanonicalJsonValue.String("URP2D");
        }

        private static List<string> ReadBuildScenes(ICollection<string> failures)
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene == null || !scene.enabled) continue;
                var path = scene.path == null ? string.Empty : scene.path.Replace('\\', '/');
                if (!CanonicalJson.IsNormalizedRelativePath(path))
                {
                    failures.Add("BUILD_SCENE_PATH_INVALID");
                    continue;
                }
                scenes.Add(path);
            }

            if (scenes.Count != 1) failures.Add("BUILD_SCENE_COUNT");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scene in scenes)
            {
                if (!seen.Add(scene))
                {
                    failures.Add("BUILD_SCENE_DUPLICATE");
                    break;
                }
            }
            return scenes;
        }

        private static CanonicalJsonValue ReadBuildSettings(IReadOnlyList<string> scenes, ICollection<string> failures)
        {
            var activeTarget = EditorUserBuildSettings.activeBuildTarget;
            if (activeTarget != BuildTarget.WebGL) failures.Add("BUILD_TARGET_NOT_WEBGL");
            if (!EditorUserBuildSettings.development) failures.Add("DEVELOPMENT_BUILD_DISABLED");
            if (EditorUserBuildSettings.connectProfiler) failures.Add("AUTOCONNECT_PROFILER_ENABLED");
            if (EditorUserBuildSettings.buildWithDeepProfilingSupport) failures.Add("DEEP_PROFILING_ENABLED");
            if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Disabled) failures.Add("WEBGL_COMPRESSION_NOT_DISABLED");
            if (PlayerSettings.WebGL.decompressionFallback) failures.Add("WEBGL_DECOMPRESSION_FALLBACK_ENABLED");
            if (PlayerSettings.WebGL.exceptionSupport != WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly) failures.Add("WEBGL_EXCEPTIONS_INVALID");

            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("autoconnectProfiler", CanonicalJsonValue.Boolean(EditorUserBuildSettings.connectProfiler)),
                new CanonicalJsonProperty("compressionFormat", CanonicalJsonValue.String(PlayerSettings.WebGL.compressionFormat.ToString())),
                new CanonicalJsonProperty("decompressionFallback", CanonicalJsonValue.Boolean(PlayerSettings.WebGL.decompressionFallback)),
                new CanonicalJsonProperty("deepProfiling", CanonicalJsonValue.Boolean(EditorUserBuildSettings.buildWithDeepProfilingSupport)),
                new CanonicalJsonProperty("development", CanonicalJsonValue.Boolean(EditorUserBuildSettings.development)),
                new CanonicalJsonProperty("exceptionSupport", CanonicalJsonValue.String(PlayerSettings.WebGL.exceptionSupport.ToString())),
                new CanonicalJsonProperty("memorySizeMb", CanonicalJsonValue.Number(PlayerSettings.WebGL.memorySize)),
                new CanonicalJsonProperty("scenes", CanonicalJsonValue.Array(ToStringValues(scenes))),
                new CanonicalJsonProperty("target", CanonicalJsonValue.String(activeTarget == BuildTarget.WebGL ? "WebGL" : "Unexpected")));
        }

        private static CanonicalJsonValue ReadDisplayPolicy(IReadOnlyList<string> scenes, ICollection<string> failures)
        {
            var canvasScaleMode = "Unavailable";
            var designWidth = 0;
            var designHeight = 0;
            var hasFixedAspectViewport = false;
            var sceneWasOpened = false;
            Scene scene = default(Scene);

            if (scenes == null || scenes.Count != 1)
            {
                failures.Add("DISPLAY_SCENE_COUNT");
            }
            else
            {
                try
                {
                    scene = SceneManager.GetSceneByPath(scenes[0]);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        scene = EditorSceneManager.OpenScene(scenes[0], OpenSceneMode.Additive);
                        sceneWasOpened = true;
                    }

                    var scalers = new List<CanvasScaler>();
                    var viewports = new List<FixedAspectViewport>();
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        scalers.AddRange(root.GetComponentsInChildren<CanvasScaler>(true));
                        viewports.AddRange(root.GetComponentsInChildren<FixedAspectViewport>(true));
                    }

                    if (scalers.Count != 1)
                    {
                        failures.Add("DISPLAY_CANVAS_SCALER_COUNT");
                    }
                    else
                    {
                        var scaler = scalers[0];
                        canvasScaleMode = scaler.uiScaleMode.ToString();
                        var canvas = scaler.GetComponent<Canvas>();
                        if (!scaler.isActiveAndEnabled)
                        {
                            failures.Add("DISPLAY_CANVAS_SCALER_INACTIVE");
                        }
                        if (canvas == null || !canvas.isActiveAndEnabled)
                        {
                            failures.Add("DISPLAY_GAMEPLAY_CANVAS_INACTIVE");
                        }

                        var referenceResolution = scaler.referenceResolution;
                        if (referenceResolution.x <= 0f || referenceResolution.y <= 0f ||
                            referenceResolution.x != Mathf.Floor(referenceResolution.x) ||
                            referenceResolution.y != Mathf.Floor(referenceResolution.y))
                        {
                            failures.Add("DISPLAY_CANVAS_REFERENCE_INVALID");
                        }
                        else
                        {
                            designWidth = (int)referenceResolution.x;
                            designHeight = (int)referenceResolution.y;
                        }
                        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                        {
                            failures.Add("DISPLAY_CANVAS_MODE_INVALID");
                        }
                    }

                    if (viewports.Count != 1)
                    {
                        failures.Add("DISPLAY_VIEWPORT_COUNT");
                    }
                    else
                    {
                        var viewport = viewports[0];
                        var camera = viewport.GetComponent<Camera>();
                        if (!viewport.isActiveAndEnabled)
                        {
                            failures.Add("DISPLAY_VIEWPORT_INACTIVE");
                        }
                        if (camera == null || !camera.isActiveAndEnabled || !camera.CompareTag("MainCamera"))
                        {
                            failures.Add("DISPLAY_GAMEPLAY_CAMERA_INACTIVE");
                        }
                        else if (viewport.isActiveAndEnabled)
                        {
                            hasFixedAspectViewport = true;
                        }
                    }
                }
                catch (ArgumentException)
                {
                    failures.Add("DISPLAY_CONFIGURATION_UNAVAILABLE");
                }
                catch (InvalidOperationException)
                {
                    failures.Add("DISPLAY_CONFIGURATION_UNAVAILABLE");
                }
                catch (UnityException)
                {
                    failures.Add("DISPLAY_CONFIGURATION_UNAVAILABLE");
                }
                finally
                {
                    if (sceneWasOpened && scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }

            var minimumWidth = ReadWebScreenDimension("defaultScreenWidthWeb", failures);
            var minimumHeight = ReadWebScreenDimension("defaultScreenHeightWeb", failures);
            if (!hasFixedAspectViewport) failures.Add("DISPLAY_LETTERBOX_MISSING");
            if (designWidth != 1920 || designHeight != 1080) failures.Add("DISPLAY_DESIGN_RESOLUTION_INVALID");
            if (minimumWidth != 1280 || minimumHeight != 720) failures.Add("DISPLAY_MINIMUM_RESOLUTION_INVALID");

            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("aspectDenominator", CanonicalJsonValue.Number(hasFixedAspectViewport ? 9 : 0)),
                new CanonicalJsonProperty("aspectNumerator", CanonicalJsonValue.Number(hasFixedAspectViewport ? 16 : 0)),
                new CanonicalJsonProperty("canvasScaleMode", CanonicalJsonValue.String(canvasScaleMode)),
                new CanonicalJsonProperty("designHeight", CanonicalJsonValue.Number(designHeight)),
                new CanonicalJsonProperty("designWidth", CanonicalJsonValue.Number(designWidth)),
                new CanonicalJsonProperty("letterboxNon16x9", CanonicalJsonValue.Boolean(hasFixedAspectViewport)),
                new CanonicalJsonProperty("minimumHeight", CanonicalJsonValue.Number(minimumHeight)),
                new CanonicalJsonProperty("minimumWidth", CanonicalJsonValue.Number(minimumWidth)),
                new CanonicalJsonProperty("sameWorldBounds", CanonicalJsonValue.Boolean(hasFixedAspectViewport)));
        }

        private static int ReadWebScreenDimension(string propertyName, ICollection<string> failures)
        {
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (assets.Length != 1 || assets[0] == null) throw new InvalidOperationException();
                var property = new SerializedObject(assets[0]).FindProperty(propertyName);
                if (property == null || property.intValue <= 0) throw new InvalidOperationException();
                return property.intValue;
            }
            catch (ArgumentException)
            {
                failures.Add("DISPLAY_MINIMUM_CONFIGURATION_UNAVAILABLE");
            }
            catch (InvalidOperationException)
            {
                failures.Add("DISPLAY_MINIMUM_CONFIGURATION_UNAVAILABLE");
            }
            catch (UnityException)
            {
                failures.Add("DISPLAY_MINIMUM_CONFIGURATION_UNAVAILABLE");
            }
            return 0;
        }

        private static bool ContainsAddressables(IEnumerable<PackageReference> packages)
        {
            foreach (var package in packages)
            {
                if (string.Equals(package.Id, "com.unity.addressables", StringComparison.Ordinal)) return true;
            }
            return false;
        }
        private static bool ContainsAddressablesInPackageLock(ICollection<string> failures)
        {
            byte[] bytes;
            try
            {
                bytes = EvidenceArtifactIO.ReadAllBytes("Packages/packages-lock.json");
            }
            catch (FileNotFoundException)
            {
                return false;
            }

            CanonicalJsonValue packageLock;
            string error;
            if (!CanonicalJson.TryParseUtf8(bytes, out packageLock, out error) || packageLock == null || packageLock.Kind != CanonicalJsonKind.Object)
            {
                failures.Add("PACKAGE_LOCK_INVALID");
                return false;
            }
            return ContainsAddressablesDependency(packageLock);
        }

        private static bool ContainsAddressablesDependency(CanonicalJsonValue value)
        {
            if (value == null) return false;
            if (value.Kind == CanonicalJsonKind.Array)
            {
                foreach (var item in value.Items)
                {
                    if (ContainsAddressablesDependency(item)) return true;
                }
                return false;
            }
            if (value.Kind != CanonicalJsonKind.Object) return false;

            foreach (var property in value.Properties)
            {
                if (string.Equals(property.Name, "dependencies", StringComparison.Ordinal) && property.Value.Kind == CanonicalJsonKind.Object)
                {
                    foreach (var dependency in property.Value.Properties)
                    {
                        if (string.Equals(dependency.Name, "com.unity.addressables", StringComparison.Ordinal)) return true;
                    }
                }
                if (ContainsAddressablesDependency(property.Value)) return true;
            }
            return false;
        }

        private static CanonicalJsonValue ToPackageArray(IEnumerable<PackageReference> packages)
        {
            var values = new List<CanonicalJsonValue>();
            foreach (var package in packages)
            {
                values.Add(CanonicalJsonValue.Object(
                    new CanonicalJsonProperty("name", CanonicalJsonValue.String(package.Id)),
                    new CanonicalJsonProperty("version", CanonicalJsonValue.String(package.Version))));
            }
            return CanonicalJsonValue.Array(values);
        }

        private static IEnumerable<CanonicalJsonValue> ToStringValues(IEnumerable<string> values)
        {
            foreach (var value in values) yield return CanonicalJsonValue.String(value);
        }

        private static int ComparePackageReferences(PackageReference left, PackageReference right)
        {
            return CanonicalJson.CompareUtf8Ordinal(left.Id, right.Id);
        }

        private static void SortAndRejectDuplicateFailures(List<string> failures)
        {
            failures.Sort(CanonicalJson.CompareUtf8Ordinal);
            for (var index = failures.Count - 1; index > 0; index--)
            {
                if (string.Equals(failures[index], failures[index - 1], StringComparison.Ordinal)) failures.RemoveAt(index);
            }
        }

        private static bool TryGetCommandLineArgument(string name, out string value)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], name, StringComparison.Ordinal)) continue;
                value = arguments[index + 1];
                return !string.IsNullOrEmpty(value);
            }

            value = null;
            return false;
        }

        private sealed class PackageReference
        {
            public PackageReference(string id, string version)
            {
                Id = id;
                Version = version;
            }

            public string Id { get; }
            public string Version { get; }
        }
    }
}
