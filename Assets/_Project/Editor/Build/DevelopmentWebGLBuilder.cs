using System;
using System.IO;
using Overbless.Editor.Bootstrap;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Overbless.Editor.Build
{
    /// <summary>
    /// Builds the single approved M1 scene as an uncompressed local Development WebGL player.
    /// The output root is ignored by source control and is recreated for every build.
    /// </summary>
    public static class DevelopmentWebGLBuilder
    {
        public const string OutputDirectory = "Builds/M1_GuidedValidation_WebGL";
        private const string ScenePath = M1ContentBootstrap.ScenePath;

        [MenuItem("Overbless/M1/Build Development WebGL")]
        public static void Build()
        {
            string candidateId;
            var isCandidateBuild = TryGetCommandLineArgument("-candidateId", out candidateId);
            BuildManifestWriter.CandidateSourceCapability sourceCapability = null;
            if (isCandidateBuild)
            {
                // Candidate inputs are sealed before any bootstrap or BuildPlayer action can observe them.
                sourceCapability = CandidateCoordinator.AcquireCandidateSourceCapability(candidateId);
            }

            if (!isCandidateBuild)
            {
                M1ContentBootstrap.CreateOrUpdate();
            }
            EnsureWebGlTarget();
            ConfigureDevelopmentSettings();
            DeletePublishedOutput();

            var stagingDirectory = CreatePrivateStagingDirectory();
            var stagingRoot = Directory.GetParent(stagingDirectory).FullName;
            var stagingManifestPath = default(string);
            var published = false;
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = stagingDirectory,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.Development
                };
                var settingsSnapshot = BuildManifestWriter.CaptureRequiredWebGlDevelopmentSettings(ScenePath);

                var report = BuildPipeline.BuildPlayer(options);
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    var result = report == null ? "no build report" : report.summary.result.ToString();
                    throw new InvalidOperationException($"M1 Development WebGL build failed: {result}.");
                }

                var provenance = BuildManifestWriter.CaptureSuccessfulWebGlDevelopmentBuild(report, options, settingsSnapshot, sourceCapability);
                BuildManifestWriter.RecordDeterministicPostprocessing(provenance, "webgl-template-v1", () => PostprocessWebTemplate(stagingDirectory));
                stagingManifestPath = BuildManifestWriter.WriteForDirectory(stagingDirectory, provenance);
                if (isCandidateBuild)
                {
                    CandidateCoordinator.SealSuccessfulBuild(candidateId, stagingDirectory, stagingManifestPath, provenance);
                }

                PublishStagingDirectory(stagingDirectory);
                published = true;
                Debug.Log($"M1 Development WebGL build completed at '{OutputDirectory}'. Served-file manifest: '{Path.Combine(OutputDirectory + ".sealed", BuildManifestWriter.ManifestFileName)}'.");
            }
            finally
            {
                if (!published)
                {
                    DeleteDirectoryIfPresent(stagingDirectory);
                    DeleteDirectoryIfPresent(stagingDirectory + ".sealed");
                }
                DeleteDirectoryIfPresent(stagingRoot);
            }
        }

        // Intended for -executeMethod in Unity batch mode.
        public static void BuildForBatchMode()
        {
            Build();
        }
        // Existing output cannot establish the BuildReport provenance required for a sealed manifest.
        public static void PostprocessExistingForBatchMode()
        {
            throw new InvalidOperationException("Existing WebGL output has no verifiable build provenance. Run BuildForBatchMode instead.");
        }

        private static void EnsureWebGlTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                return;
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                throw new InvalidOperationException("Unity failed to switch the active build target to WebGL.");
            }
        }

        private static void ConfigureDevelopmentSettings()
        {
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        }

        private static void PostprocessWebTemplate(string outputDirectory)
        {
            var indexPath = Path.Combine(outputDirectory, "index.html");
            var stylePath = Path.Combine(outputDirectory, "TemplateData", "style.css");
            if (!File.Exists(indexPath) || !File.Exists(stylePath))
            {
                throw new InvalidOperationException("A completed Development WebGL template is required for postprocessing.");
            }

            var html = File.ReadAllText(indexPath).Replace("\r\n", "\n");
            html = ReplaceExactlyOnce(html, "<canvas id=\"unity-canvas\" width=960 height=600 tabindex=\"-1\"></canvas>",
                "<canvas id=\"unity-canvas\" width=1280 height=720 tabindex=\"-1\"></canvas>");
            html = RemoveExactlyOnce(html, "        <div id=\"unity-fullscreen-button\"></div>\n");
            html = ReplaceExactlyOnce(html,
                "        canvas.style.width = \"1280px\";\n        canvas.style.height = \"720px\";",
                "        canvas.style.width = \"100vw\";\n        canvas.style.height = \"100vh\";");
            html = RemoveExactlyOnce(html,
                "                document.querySelector(\"#unity-fullscreen-button\").onclick = () => {\n                  unityInstance.SetFullscreen(1);\n                };\n");
            EnsureNoFullscreenContract(html, indexPath);
            File.WriteAllText(indexPath, html, new System.Text.UTF8Encoding(false));

            var css = File.ReadAllText(stylePath);
            css = ReplaceExactlyOnce(css, "body { padding: 0; margin: 0 }",
                "body { padding: 0; margin: 0; overflow: hidden; background: #0e121c }");
            css = ReplaceExactlyOnce(css,
                "#unity-container.unity-desktop { left: 50%; top: 50%; transform: translate(-50%, -50%) }",
                "#unity-container.unity-desktop { left: 0; top: 0; width: 100%; height: 100% }");
            css = ReplaceExactlyOnce(css, "#unity-canvas { background: #1F1F20 }",
                "#unity-canvas { display: block; width: 100%; height: 100%; background: #0e121c }");
            css = ReplaceExactlyOnce(css, "#unity-footer { position: relative }", "#unity-footer { position: relative; display: none }");
            css = RemoveExactlyOnce(
                css,
                "#unity-fullscreen-button { cursor:pointer; float: right; width: 38px; height: 38px; background: url('fullscreen-button.png') no-repeat center }\n");
            EnsureNoFullscreenContract(css, stylePath);
            File.WriteAllText(stylePath, css, new System.Text.UTF8Encoding(false));

            var fullscreenIconPath = Path.Combine(outputDirectory, "TemplateData", "fullscreen-button.png");
            if (File.Exists(fullscreenIconPath))
            {
                File.Delete(fullscreenIconPath);
            }

            if (File.Exists(fullscreenIconPath)) throw new InvalidOperationException("Fullscreen icon removal postcondition failed.");
        }

        private static string ReplaceExactlyOnce(string source, string oldValue, string newValue)
        {
            var oldCount = CountOccurrences(source, oldValue);
            var newCount = CountOccurrences(source, newValue);
            if (oldCount == 1) return source.Replace(oldValue, newValue);
            if (oldCount == 0 && newCount >= 1) return source;
            throw new InvalidOperationException($"Development WebGL template replacement contract changed; expected one old fragment or at least one new fragment: {oldValue}");
        }

        private static string RemoveExactlyOnce(string source, string oldValue)
        {
            var oldCount = CountOccurrences(source, oldValue);
            if (oldCount == 1) return source.Replace(oldValue, string.Empty);
            if (oldCount == 0) return source;
            throw new InvalidOperationException($"Development WebGL template removal contract changed; expected at most one fragment: {oldValue}");
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void EnsureNoFullscreenContract(string source, string path)
        {
            if (source.IndexOf("unity-fullscreen-button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("SetFullscreen(", StringComparison.Ordinal) >= 0 ||
                source.IndexOf("fullscreen-button.png", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException($"Development WebGL fullscreen-removal postcondition failed for '{path}'.");
            }
        }
        private static bool TryGetCommandLineArgument(string name, out string value)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], name, StringComparison.Ordinal))
                {
                    value = args[index + 1];
                    return !string.IsNullOrWhiteSpace(value);
                }
            }

            value = null;
            return false;
        }
        private static string CreatePrivateStagingDirectory()
        {
            var buildsDirectory = Path.GetFullPath("Builds");
            Directory.CreateDirectory(buildsDirectory);
            var stagingRoot = Path.Combine(
                buildsDirectory,
                ".M1_GuidedValidation_WebGL." + Guid.NewGuid().ToString("N") + ".staging");
            var stagingDirectory = Path.Combine(stagingRoot, Path.GetFileName(OutputDirectory));
            Directory.CreateDirectory(stagingDirectory);
            return stagingDirectory;
        }

        private static void DeletePublishedOutput()
        {
            var fullOutputPath = Path.GetFullPath(OutputDirectory);
            var fullBuildsPath = Path.GetFullPath("Builds");
            var buildsPrefix = fullBuildsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullOutputPath.StartsWith(buildsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to clear build output outside the ignored Builds directory: '{fullOutputPath}'.");
            }

            DeleteDirectoryIfPresent(fullOutputPath);
            DeleteDirectoryIfPresent(fullOutputPath + ".sealed");
        }

        private static void PublishStagingDirectory(string stagingDirectory)
        {
            var finalOutputPath = Path.GetFullPath(OutputDirectory);
            var finalMetadataPath = finalOutputPath + ".sealed";
            var stagingMetadataPath = stagingDirectory + ".sealed";
            if (Directory.Exists(finalOutputPath) || Directory.Exists(finalMetadataPath))
            {
                throw new InvalidOperationException("A published build output appeared while the private staging build was being sealed.");
            }

            Directory.Move(stagingMetadataPath, finalMetadataPath);
            Directory.Move(stagingDirectory, finalOutputPath);
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            if (!Directory.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Refusing to delete a reparse-point build directory: " + path);
            }

            Directory.Delete(path, true);
        }
    }
}
