using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Overbless.Editor.Bootstrap;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Overbless.Editor.Build
{
    /// <summary>
    /// Builds the public submission player: the whole run in one Release WebGL build that a
    /// reviewer can open from a single link.
    /// </summary>
    /// <remarks>
    /// This is a separate path from <see cref="DevelopmentWebGLBuilder"/> on purpose. The
    /// development contract is bound to provenance tests that require an uncompressed
    /// Development player, and a public build needs the opposite: Release code, compression,
    /// and a decompression fallback so a static host without custom headers still runs it.
    /// Authorized by <c>Docs/Decisions/CONTEST_SUBMISSION_APPROVAL.json</c>.
    /// </remarks>
    public static class ContestWebGLBuilder
    {
        public const string OutputDirectory = "Builds/Overbless_Web";

        /// <summary>
        /// GitHub Pages serves this branch. A directory named <c>docs</c> is not usable here:
        /// the repository already tracks <c>Docs</c>, and a case-insensitive filesystem would
        /// merge the two into one directory.
        /// </summary>
        public const string PublishBranch = "gh-pages";

        public const string PublishScript = "Tools/publish_gh_pages.py";
        public const string ManifestDirectory = "Builds/Overbless_Web.provenance";
        public const string ManifestFileName = "submission-build-manifest.json";
        public const string PageTitle = "이 멋진 적에게 축복을 | Overbless";

        private static readonly string[] VersionedPayloadFiles =
        {
            "Overbless_Web.loader.js",
            "Overbless_Web.data.unityweb",
            "Overbless_Web.framework.js.unityweb",
            "Overbless_Web.wasm.unityweb"
        };

        /// <summary>Every scene of the submitted run, in play order.</summary>
        public static readonly string[] Scenes =
        {
            M1ContentBootstrap.TitleScenePath,
            M1ContentBootstrap.ScenePath,
            "Assets/_Project/Scenes/Room_02.unity",
            "Assets/_Project/Scenes/Room_03.unity",
            M1ContentBootstrap.ResultScenePath
        };

        [MenuItem("Overbless/Contest/Build Submission WebGL")]
        public static void Build()
        {
            RequireScenes();
            EnsureWebGlTarget();
            ConfigureSubmissionSettings();

            var output = Path.GetFullPath(OutputDirectory);
            DeleteDirectoryIfPresent(output);
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                var result = report == null ? "no build report" : report.summary.result.ToString();
                throw new InvalidOperationException($"Submission WebGL build failed: {result}.");
            }

            PostprocessTemplate(output);
            var manifestPath = WriteManifest(output);
            Debug.Log(
                $"Submission WebGL build completed at '{OutputDirectory}'. File manifest: '{manifestPath}'. " +
                $"Publish it with: {PublishInstructions}");
        }

        // Intended for -executeMethod in Unity batch mode.
        public static void BuildForBatchMode()
        {
            Build();
        }

        /// <summary>
        /// Publishing is a git operation, not an asset operation, so it lives in
        /// <c>Tools/publish_gh_pages.py</c>. Keeping it out of the editor also keeps a build
        /// from ever overwriting tracked source directories.
        /// </summary>
        public static string PublishInstructions =>
            $"python {PublishScript} --build {OutputDirectory}   then   git push origin {PublishBranch}";

        private static void RequireScenes()
        {
            for (var index = 0; index < Scenes.Length; index++)
            {
                if (!File.Exists(Path.GetFullPath(Scenes[index])))
                {
                    throw new InvalidOperationException(
                        $"Submission build requires scene '{Scenes[index]}'. Regenerate content first.");
                }
            }
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

        /// <summary>
        /// Release code, Brotli compression, and a decompression fallback. GitHub Pages serves
        /// compressed files without a matching Content-Encoding header, so without the fallback
        /// the player would fail to start there.
        /// </summary>
        private static void ConfigureSubmissionSettings()
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
        }

        /// <summary>
        /// Makes the page fill the browser, names it in Korean for the submission, and keeps the
        /// fullscreen control small and out of the play area. Safe to run again on an already
        /// processed page.
        /// </summary>
        private static void PostprocessTemplate(string outputDirectory)
        {
            var indexPath = Path.Combine(outputDirectory, "index.html");
            if (!File.Exists(indexPath))
            {
                throw new InvalidOperationException("The submission build produced no index.html.");
            }

            var html = File.ReadAllText(indexPath).Replace("\r\n", "\n");
            html = ReplaceTitle(html);
            html = ReplaceBuildTitle(html);
            html = VersionWebGlPayloadUrls(html, outputDirectory);

            // Unity keeps the canvas drawing buffer in sync with the canvas element itself, and
            // the runtime letterboxes to 16:9 from Screen. Overriding the element size here made
            // the two disagree and clipped the view, so the element keeps the template's own
            // size and only the page around it is styled. Reviewers who want a bigger view use
            // the fullscreen control, which stays enabled.
            if (!html.Contains(StyleMarker, StringComparison.Ordinal))
            {
                var style =
                    "  <style>\n" +
                    "    " + StyleMarker + "\n" +
                    "    html, body { margin: 0; padding: 0; width: 100%; height: 100%; background: #05080f; overflow: hidden; }\n" +
                    "    #unity-container.unity-desktop { position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); }\n" +
                    "    #unity-canvas { display: block; background: #05080f; }\n" +
                    "    #unity-footer { display: flex; align-items: center; justify-content: flex-end; gap: 8px;\n" +
                    "                    width: 100%; height: 26px; border: 0 !important; background: transparent !important; opacity: 0.6; }\n" +
                    "    #unity-build-title { color: #9fb4c4; font: 12px/1.2 system-ui, sans-serif; padding: 0 !important; }\n" +
                    "    #unity-warning { position: fixed !important; left: 50%; top: 12px; transform: translateX(-50%); z-index: 5; }\n" +
                    "  </style>\n";
                html = html.Replace("</head>", style + "</head>");
            }

            File.WriteAllText(indexPath, html, new UTF8Encoding(false));
        }

        private const string StyleMarker = "/* overbless-submission-layout */";

        /// <summary>Replaces the internal project name shown over the page with the game's title.</summary>
        private static string ReplaceBuildTitle(string html)
        {
            const string opening = "<div id=\"unity-build-title\">";
            var start = html.IndexOf(opening, StringComparison.Ordinal);
            if (start < 0)
            {
                return html;
            }

            var contentStart = start + opening.Length;
            var end = html.IndexOf("</div>", contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                return html;
            }

            return html.Substring(0, contentStart) + "이 멋진 적에게 축복을" + html.Substring(end);
        }

        private static string VersionWebGlPayloadUrls(string html, string outputDirectory)
        {
            var version = ComputeWebGlPayloadVersion(outputDirectory);
            for (var index = 0; index < VersionedPayloadFiles.Length; index++)
            {
                html = ReplacePayloadUrlVersion(html, VersionedPayloadFiles[index], version);
            }

            return html;
        }

        private static string ReplacePayloadUrlVersion(string html, string fileName, string version)
        {
            var result = new StringBuilder(html.Length + version.Length * 2);
            var sourceOffset = 0;
            var replaced = false;
            while (true)
            {
                var fileNameIndex = html.IndexOf(fileName, sourceOffset, StringComparison.Ordinal);
                if (fileNameIndex < 0)
                {
                    break;
                }

                var versionStart = fileNameIndex + fileName.Length;
                var versionEnd = versionStart;
                if (versionStart < html.Length && html[versionStart] == '?')
                {
                    versionEnd = html.IndexOf('"', versionStart);
                    if (versionEnd < 0)
                    {
                        throw new InvalidOperationException(
                            $"Submission template has an unterminated versioned URL for '{fileName}'.");
                    }
                }

                result.Append(html, sourceOffset, versionStart - sourceOffset);
                result.Append("?v=").Append(version);
                sourceOffset = versionEnd;
                replaced = true;
            }

            if (!replaced)
            {
                throw new InvalidOperationException(
                    $"Submission template has no URL for required WebGL payload '{fileName}'.");
            }

            result.Append(html, sourceOffset, html.Length - sourceOffset);
            return result.ToString();
        }

        private static string ComputeWebGlPayloadVersion(string outputDirectory)
        {
            var fingerprint = new StringBuilder();
            var buildDirectory = Path.Combine(outputDirectory, "Build");
            for (var index = 0; index < VersionedPayloadFiles.Length; index++)
            {
                var payloadPath = Path.Combine(buildDirectory, VersionedPayloadFiles[index]);
                if (!File.Exists(payloadPath))
                {
                    throw new InvalidOperationException(
                        $"Submission build has no required WebGL payload '{VersionedPayloadFiles[index]}'.");
                }

                fingerprint.Append(ComputeSha256(payloadPath)).Append('\n');
            }

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint.ToString()));
            var version = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                version.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return version.ToString();
        }

        /// <summary>
        /// Re-applies the page layout to an already built player. Useful when only the page
        /// presentation changes, so a full rebuild is not needed.
        /// </summary>
        [MenuItem("Overbless/Contest/Reapply Submission Page Layout")]
        public static void PostprocessExistingBuild()
        {
            var output = Path.GetFullPath(OutputDirectory);
            PostprocessTemplate(output);
            var manifestPath = WriteManifest(output);
            Debug.Log($"Re-applied the submission page layout. File manifest: '{manifestPath}'.");
        }

        // Intended for -executeMethod in Unity batch mode.
        public static void PostprocessExistingBuildForBatchMode()
        {
            PostprocessExistingBuild();
        }

        private static string ReplaceTitle(string html)
        {
            var start = html.IndexOf("<title>", StringComparison.Ordinal);
            var end = html.IndexOf("</title>", StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("The submission template has no title element to name.");
            }

            start += "<title>".Length;
            return html.Substring(0, start) + PageTitle + html.Substring(end);
        }

        /// <summary>
        /// Records what is actually served: every file with its size and SHA-256 over real
        /// bytes, plus the settings the player was built with.
        /// </summary>
        private static string WriteManifest(string outputDirectory)
        {
            var manifestDirectory = Path.GetFullPath(ManifestDirectory);
            Directory.CreateDirectory(manifestDirectory);
            var manifestPath = Path.Combine(manifestDirectory, ManifestFileName);

            var files = new List<string>(Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            builder.Append("{\n  \"schema\": \"overbless.submission-build/v1\",\n");
            builder.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
            builder.Append("  \"buildUtc\": \"")
                .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                .Append("\",\n");
            builder.Append("  \"development\": false,\n");
            builder.Append("  \"compressionFormat\": \"Brotli\",\n");
            builder.Append("  \"decompressionFallback\": true,\n");
            builder.Append("  \"scenes\": [\n");
            for (var index = 0; index < Scenes.Length; index++)
            {
                builder.Append("    \"").Append(Scenes[index]).Append('"');
                builder.Append(index == Scenes.Length - 1 ? "\n" : ",\n");
            }

            builder.Append("  ],\n  \"files\": [\n");
            long totalBytes = 0;
            for (var index = 0; index < files.Count; index++)
            {
                var info = new FileInfo(files[index]);
                totalBytes += info.Length;
                var relative = files[index]
                    .Substring(outputDirectory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                builder.Append("    { \"path\": \"").Append(relative).Append("\", \"bytes\": ")
                    .Append(info.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"sha256\": \"").Append(ComputeSha256(files[index])).Append("\" }");
                builder.Append(index == files.Count - 1 ? "\n" : ",\n");
            }

            builder.Append("  ],\n  \"totalBytes\": ")
                .Append(totalBytes.ToString(CultureInfo.InvariantCulture))
                .Append("\n}\n");

            File.WriteAllText(manifestPath, builder.ToString(), new UTF8Encoding(false));
            return manifestPath;
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
