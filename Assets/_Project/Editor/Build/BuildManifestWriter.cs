using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Overbless.Editor.Evidence;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Overbless.Editor.Build
{
    /// <summary>
    /// Seals the bytes a local WebGL server may expose. The manifest is published outside the served root,
    /// so every addressable byte is included in its inventory.
    /// </summary>
    public static class BuildManifestWriter
    {
        public const string ManifestFileName = "build-manifest.json";
        public const string ServedRootManifestSchema = "overbless.served-root/v1";
        private const string RequiredUnityVersion = "6000.0.72f1";
        private const BuildOptions ApprovedBuildOptions = BuildOptions.Development;

        /// <summary>Immutable pre-build settings and BuildReport facts bound to one served WebGL directory.</summary>
        public sealed class BuildProvenance
        {
            internal BuildProvenance(
                string outputDirectory,
                string scene,
                BuildOptions options,
                BuildSettingsSnapshot settings,
                string emittedSnapshotSha256,
                CandidateSourceCapability sourceCapability)
            {
                OutputDirectory = outputDirectory;
                Scene = scene;
                Options = options;
                Settings = settings;
                EmittedSnapshotSha256 = emittedSnapshotSha256;
                SourceCapability = sourceCapability;
            }

            internal string OutputDirectory { get; }
            internal string Scene { get; }
            internal BuildOptions Options { get; }
            internal BuildSettingsSnapshot Settings { get; }
            internal string EmittedSnapshotSha256 { get; }
            internal CandidateSourceCapability SourceCapability { get; }
            internal string PostprocessName { get; set; }
            internal string PostprocessedSnapshotSha256 { get; set; }
            internal string SealedManifestPath { get; set; }
            internal string SealedManifestSha256 { get; set; }
            internal bool SealConsumed { get; set; }
            internal bool BridgeConsumed { get; set; }
        }

        /// <summary>Candidate-bound source facts acquired before BuildPlayer and consumed only by that candidate.</summary>
        public sealed class CandidateSourceCapability
        {
            internal CandidateSourceCapability(string candidateId, string sourceCommit, string sourceManifestSha256, string digest)
            {
                CandidateId = candidateId;
                SourceCommit = sourceCommit;
                SourceManifestSha256 = sourceManifestSha256;
                Digest = digest;
            }

            internal string CandidateId { get; }
            internal string SourceCommit { get; }
            internal string SourceManifestSha256 { get; }
            internal string Digest { get; }
        }

        /// <summary>Captures required settings immediately before BuildPipeline.BuildPlayer is invoked.</summary>
        public sealed class BuildSettingsSnapshot
        {
            internal BuildSettingsSnapshot(string scene, int memorySizeMb)
            {
                Scene = scene;
                MemorySizeMb = memorySizeMb;
            }

            internal string Scene { get; }
            internal int MemorySizeMb { get; }
        }

        /// <summary>Captures the approved WebGL settings before BuildPipeline.BuildPlayer is invoked.</summary>
        public static BuildSettingsSnapshot CaptureRequiredWebGlDevelopmentSettings(string scene)
        {
            if (!CanonicalJson.IsNormalizedRelativePath(scene)) throw new ArgumentException("A normalized build scene is required.", nameof(scene));
            if (!string.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Development WebGL builds require Unity " + RequiredUnityVersion + ".");
            }

            ValidateCurrentDevelopmentSettings();
            return new BuildSettingsSnapshot(scene, PlayerSettings.WebGL.memorySize);
        }

        /// <summary>Captures the only provenance accepted by this writer: one successful, settings-bound WebGL Development BuildReport.</summary>
        public static BuildProvenance CaptureSuccessfulWebGlDevelopmentBuild(
            BuildReport report,
            BuildPlayerOptions options,
            BuildSettingsSnapshot settings,
            CandidateSourceCapability sourceCapability)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("A successful BuildReport is required to seal a WebGL build.");
            }

            if (options.target != BuildTarget.WebGL ||
                options.targetGroup != BuildTargetGroup.WebGL ||
                report.summary.platform != BuildTarget.WebGL ||
                report.summary.platformGroup != BuildTargetGroup.WebGL)
            {
                throw new InvalidOperationException("Build provenance must bind WebGL target facts.");
            }

            ValidateApprovedBuildOptions(options.options, "requested build");
            ValidateApprovedBuildOptions(report.summary.options, "BuildReport summary");
            if (report.summary.options != options.options)
            {
                throw new InvalidOperationException("Build provenance must bind matching requested and reported BuildOptions.");
            }

            if (options.scenes == null || options.scenes.Length != 1 || !string.Equals(options.scenes[0], settings.Scene, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Build provenance must bind exactly the approved scene.");
            }

            if (string.IsNullOrEmpty(options.locationPathName) || string.IsNullOrEmpty(report.summary.outputPath) ||
                !PathsEqual(NormalizeFullPath(options.locationPathName), NormalizeFullPath(report.summary.outputPath)))
            {
                throw new InvalidOperationException("BuildReport outputPath does not bind to the requested build output.");
            }

            if (sourceCapability != null &&
                (string.IsNullOrEmpty(sourceCapability.CandidateId) ||
                 string.IsNullOrEmpty(sourceCapability.SourceCommit) ||
                 !CanonicalJson.IsLowerSha256(sourceCapability.SourceManifestSha256) ||
                 !CanonicalJson.IsLowerSha256(sourceCapability.Digest)))
            {
                throw new InvalidOperationException("Candidate build source capability is invalid.");
            }

            ValidateCurrentDevelopmentSettings();
            if (PlayerSettings.WebGL.memorySize != settings.MemorySizeMb)
            {
                throw new InvalidOperationException("WebGL memory settings changed after the pre-build settings snapshot.");
            }

            var outputDirectory = NormalizeFullPath(report.summary.outputPath);
            // This snapshot is taken immediately after the BuildReport, before any deterministic postprocessing.
            var emittedSnapshotSha256 = ComputeSnapshotSha256(CollectStableServedFiles(outputDirectory));
            return new BuildProvenance(outputDirectory, settings.Scene, options.options, settings, emittedSnapshotSha256, sourceCapability);
        }

        /// <summary>
        /// Records one named deterministic transformation between the BuildReport-emitted snapshot and the final served snapshot.
        /// The callback must fail rather than partially accepting an unexpected WebGL template.
        /// </summary>
        public static void RecordDeterministicPostprocessing(BuildProvenance provenance, string transformationName, Action transformation)
        {
            if (provenance == null) throw new ArgumentNullException(nameof(provenance));
            if (string.IsNullOrEmpty(transformationName)) throw new ArgumentException("A deterministic postprocessing name is required.", nameof(transformationName));
            if (transformation == null) throw new ArgumentNullException(nameof(transformation));
            if (provenance.SealConsumed || provenance.PostprocessedSnapshotSha256 != null)
            {
                throw new InvalidOperationException("Build provenance postprocessing can be recorded exactly once before sealing.");
            }

            var before = ComputeSnapshotSha256(CollectStableServedFiles(provenance.OutputDirectory));
            if (!string.Equals(before, provenance.EmittedSnapshotSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Served bytes changed after the successful BuildReport and before postprocessing.");
            }

            transformation();

            provenance.PostprocessName = transformationName;
            provenance.PostprocessedSnapshotSha256 = ComputeSnapshotSha256(CollectStableServedFiles(provenance.OutputDirectory));
        }


        private static void ValidateCurrentDevelopmentSettings()
        {
            if (!string.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal) ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL ||
                !EditorUserBuildSettings.development ||
                PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Disabled ||
                PlayerSettings.WebGL.decompressionFallback ||
                PlayerSettings.WebGL.exceptionSupport != WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly ||
                EditorUserBuildSettings.connectProfiler ||
                EditorUserBuildSettings.buildWithDeepProfilingSupport ||
                PlayerSettings.WebGL.memorySize <= 0)
            {
                throw new InvalidOperationException("Build settings do not satisfy the approved WebGL Development contract.");
            }
        }

        public static string WriteForDirectory(string servedDirectory, BuildProvenance provenance)
        {
            var root = RequireServedDirectory(servedDirectory);
            if (provenance == null) throw new ArgumentNullException(nameof(provenance));
            if (!PathsEqual(root, provenance.OutputDirectory))
            {
                throw new InvalidOperationException("Build provenance does not bind to the served directory.");
            }

            var parent = Directory.GetParent(root);
            if (parent == null) throw new InvalidOperationException("The served directory has no parent for sealed metadata.");
            var manifestDirectory = Path.Combine(parent.FullName, Path.GetFileName(root) + ".sealed");
            return Write(root, Path.Combine(manifestDirectory, ManifestFileName), provenance);
        }

        /// <summary>Atomically creates a write-once manifest outside <paramref name="servedDirectory"/>.</summary>
        public static string Write(string servedDirectory, string manifestPath, BuildProvenance provenance)
        {
            var root = RequireServedDirectory(servedDirectory);
            if (string.IsNullOrWhiteSpace(manifestPath)) throw new ArgumentException("A manifest path is required.", nameof(manifestPath));
            if (provenance == null) throw new ArgumentNullException(nameof(provenance));
            if (!PathsEqual(root, provenance.OutputDirectory)) throw new InvalidOperationException("Build provenance does not bind to the served directory.");
            if (provenance.SealConsumed)
            {
                throw new InvalidOperationException("Build provenance can seal only one served-root manifest.");
            }

            provenance.SealConsumed = true;
            ValidateCurrentDevelopmentSettings();
            ValidateApprovedBuildOptions(provenance.Options, "stored build provenance");
            if (string.IsNullOrEmpty(provenance.PostprocessName) ||
                !CanonicalJson.IsLowerSha256(provenance.PostprocessedSnapshotSha256) ||
                !string.Equals(provenance.Settings.Scene, provenance.Scene, StringComparison.Ordinal) ||
                PlayerSettings.WebGL.memorySize != provenance.Settings.MemorySizeMb)
            {
                throw new InvalidOperationException("Build provenance settings or deterministic postprocessing record are not stable.");
            }

            var destination = NormalizeFullPath(manifestPath);
            if (IsWithin(root, destination))
            {
                throw new InvalidOperationException("A sealed build manifest must be outside the served directory.");
            }

            var manifestDirectory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(manifestDirectory)) throw new InvalidOperationException("Cannot determine the manifest directory.");
            Directory.CreateDirectory(manifestDirectory);

            // Use this single final inventory both to compare provenance and to emit manifest bytes.
            var files = CollectStableServedFiles(root);
            var finalSnapshotSha256 = ComputeSnapshotSha256(files);
            if (!string.Equals(finalSnapshotSha256, provenance.PostprocessedSnapshotSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Served bytes changed after recorded deterministic postprocessing.");
            }

            var fileValues = new List<CanonicalJsonValue>();
            foreach (var file in files)
            {
                fileValues.Add(CanonicalJsonValue.Object(
                    Property("path", file.Path),
                    Property("sha256", file.Sha256),
                    new CanonicalJsonProperty("size", CanonicalJsonValue.Number(file.Size))));
            }

            var filesValue = CanonicalJsonValue.Array(fileValues);
            var unsignedManifest = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("development", CanonicalJsonValue.Boolean(true)),
                Property("emittedFileSetSha256", provenance.EmittedSnapshotSha256),
                Property("fileSetSha256", finalSnapshotSha256),
                new CanonicalJsonProperty("files", filesValue),
                Property("postprocess", provenance.PostprocessName),
                Property("scene", provenance.Scene),
                Property("schema", ServedRootManifestSchema),
                Property("target", "WebGL"),
                Property("unityVersion", RequiredUnityVersion));
            var manifest = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("development", CanonicalJsonValue.Boolean(true)),
                Property("emittedFileSetSha256", provenance.EmittedSnapshotSha256),
                Property("fileSetSha256", finalSnapshotSha256),
                new CanonicalJsonProperty("files", filesValue),
                Property("postprocess", provenance.PostprocessName),
                Property("scene", provenance.Scene),
                Property("schema", ServedRootManifestSchema),
                Property("servedRootManifestSha256", CanonicalJson.Sha256Hex(unsignedManifest)),
                Property("target", "WebGL"),
                Property("unityVersion", RequiredUnityVersion));
            var manifestBytes = CanonicalJson.SerializeUtf8(manifest);
            WriteCanonicalJsonAtomicallyNew(destination, manifest);
            provenance.SealedManifestPath = destination;
            provenance.SealedManifestSha256 = CanonicalJson.Sha256Hex(manifestBytes);
            return destination;
        }

        internal static string ConsumeCandidateBridge(
            BuildProvenance provenance,
            string servedDirectory,
            string servedManifestPath)
        {
            if (provenance == null || !provenance.SealConsumed || provenance.BridgeConsumed)
            {
                throw new InvalidOperationException("Candidate build bridge requires unused, successfully sealed build provenance.");
            }

            var root = RequireServedDirectory(servedDirectory);
            ValidateApprovedBuildOptions(provenance.Options, "stored build provenance");
            if (!PathsEqual(root, provenance.OutputDirectory) ||
                !string.Equals(
                    ComputeSnapshotSha256(CollectStableServedFiles(root)),
                    provenance.PostprocessedSnapshotSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate build bridge bytes do not match the recorded postprocessing snapshot.");
            }

            var manifestPath = NormalizeFullPath(servedManifestPath);
            if (!PathsEqual(manifestPath, provenance.SealedManifestPath) ||
                !CanonicalJson.IsLowerSha256(provenance.SealedManifestSha256) ||
                (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Candidate build bridge manifest path is not the sealed manifest.");
            }

            var manifestBytes = File.ReadAllBytes(manifestPath);
            if (!string.Equals(CanonicalJson.Sha256Hex(manifestBytes), provenance.SealedManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate build bridge manifest bytes changed after publication.");
            }
            CanonicalJsonValue manifest;
            string error;
            if (!CanonicalJson.TryParseCanonicalUtf8(manifestBytes, out manifest, out error) ||
                manifest.Kind != CanonicalJsonKind.Object)
            {
                throw new InvalidOperationException("Served-root manifest is not canonical: " + error);
            }

            CanonicalJsonValue selfHash;
            if (!manifest.TryGetSingleProperty("servedRootManifestSha256", out selfHash) ||
                selfHash.Kind != CanonicalJsonKind.String ||
                !CanonicalJson.IsLowerSha256(selfHash.StringValue) ||
                !string.Equals(
                    selfHash.StringValue,
                    CanonicalJson.Sha256Hex(manifest.WithoutTopLevelProperty("servedRootManifestSha256")),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Served-root manifest self hash is invalid.");
            }

            CanonicalJsonValue fileSetHash;
            CanonicalJsonValue emittedFileSetHash;
            CanonicalJsonValue postprocess;
            CanonicalJsonValue scene;
            CanonicalJsonValue schema;
            if (!manifest.TryGetSingleProperty("fileSetSha256", out fileSetHash) ||
                fileSetHash.Kind != CanonicalJsonKind.String ||
                !string.Equals(fileSetHash.StringValue, provenance.PostprocessedSnapshotSha256, StringComparison.Ordinal) ||
                !manifest.TryGetSingleProperty("emittedFileSetSha256", out emittedFileSetHash) ||
                emittedFileSetHash.Kind != CanonicalJsonKind.String ||
                !string.Equals(emittedFileSetHash.StringValue, provenance.EmittedSnapshotSha256, StringComparison.Ordinal) ||
                !manifest.TryGetSingleProperty("postprocess", out postprocess) ||
                postprocess.Kind != CanonicalJsonKind.String ||
                !string.Equals(postprocess.StringValue, provenance.PostprocessName, StringComparison.Ordinal) ||
                !manifest.TryGetSingleProperty("scene", out scene) ||
                scene.Kind != CanonicalJsonKind.String ||
                !string.Equals(scene.StringValue, provenance.Scene, StringComparison.Ordinal) ||
                !manifest.TryGetSingleProperty("schema", out schema) ||
                schema.Kind != CanonicalJsonKind.String ||
                !string.Equals(schema.StringValue, ServedRootManifestSchema, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Served-root manifest does not bind the final recorded build snapshot.");
            }

            provenance.BridgeConsumed = true;
            return selfHash.StringValue;
        }

        private static string ComputeSnapshotSha256(IReadOnlyList<ManifestFile> files)
        {
            var values = new List<CanonicalJsonValue>();
            foreach (var file in files)
            {
                values.Add(CanonicalJsonValue.Object(
                    Property("path", file.Path),
                    Property("sha256", file.Sha256),
                    new CanonicalJsonProperty("size", CanonicalJsonValue.Number(file.Size))));
            }

            return CanonicalJson.Sha256Hex(CanonicalJsonValue.Array(values));
        }
        private static void ValidateApprovedBuildOptions(BuildOptions options, string context)
        {
            if ((options & BuildOptions.ConnectWithProfiler) != 0 ||
                (options & BuildOptions.EnableDeepProfilingSupport) != 0 ||
                (options & ~ApprovedBuildOptions) != 0 ||
                (options & ApprovedBuildOptions) != ApprovedBuildOptions)
            {
                throw new InvalidOperationException(context + " BuildOptions do not satisfy the approved WebGL Development contract.");
            }
        }
        private static string RequireServedDirectory(string servedDirectory)
        {
            if (string.IsNullOrWhiteSpace(servedDirectory)) throw new ArgumentException("A served directory is required.", nameof(servedDirectory));
            var root = NormalizeFullPath(servedDirectory);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The served directory '" + root + "' does not exist.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The served directory cannot be a reparse point.");
            return root;
        }

        private static List<ManifestFile> CollectStableServedFiles(string root)
        {
            var first = CollectServedFiles(root);
            var second = CollectServedFiles(root);
            if (!SameFileSet(first, second))
            {
                throw new InvalidOperationException("The served file set changed while it was being sealed.");
            }

            return second;
        }

        private static List<ManifestFile> CollectServedFiles(string root)
        {
            var files = new List<ManifestFile>();
            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                var directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("The served file set contains a reparse-point directory: " + directory);
                }

                foreach (var childDirectory in Directory.GetDirectories(directory))
                {
                    directories.Push(NormalizeFullPath(childDirectory));
                }

                foreach (var filePath in Directory.GetFiles(directory))
                {
                    var normalizedFilePath = NormalizeFullPath(filePath);
                    if ((File.GetAttributes(normalizedFilePath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException("The served file set contains a reparse-point file: " + normalizedFilePath);
                    }

                    string sha256;
                    long size;
                    using (var stream = new FileStream(normalizedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        sha256 = CanonicalJson.Sha256Hex(stream, out size);
                        if (stream.Length != size) throw new InvalidOperationException("A served file changed while it was being hashed: " + normalizedFilePath);
                    }

                    files.Add(new ManifestFile(ToRelativePath(root, normalizedFilePath), size, sha256));
                }
            }

            files.Sort((left, right) => CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path));
            for (var index = 1; index < files.Count; index++)
            {
                if (string.Equals(files[index - 1].Path, files[index].Path, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The served file set contains duplicate normalized path '" + files[index].Path + "'.");
                }
            }

            if (files.Count == 0) throw new InvalidOperationException("A build manifest requires at least one served file.");
            return files;
        }

        private static bool SameFileSet(IReadOnlyList<ManifestFile> left, IReadOnlyList<ManifestFile> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index].Path, right[index].Path, StringComparison.Ordinal) ||
                    left[index].Size != right[index].Size ||
                    !string.Equals(left[index].Sha256, right[index].Sha256, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void WriteCanonicalJsonAtomicallyNew(string destination, CanonicalJsonValue value)
        {
            var bytes = CanonicalJson.SerializeUtf8(value);
            var directory = Path.GetDirectoryName(destination);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            var published = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                File.Move(temporary, destination);
                published = true;
            }
            finally
            {
                if (!published && File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static CanonicalJsonProperty Property(string name, string value)
        {
            return new CanonicalJsonProperty(name, CanonicalJsonValue.String(value));
        }

        private static string NormalizeFullPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, PathComparison) ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ToRelativePath(string root, string filePath)
        {
            var rootWithSeparator = EnsureTrailingSeparator(root);
            if (!filePath.StartsWith(rootWithSeparator, PathComparison))
            {
                throw new InvalidOperationException("'" + filePath + "' is not contained by served root '" + root + "'.");
            }

            var relativePath = filePath.Substring(rootWithSeparator.Length).Replace('\\', '/');
            if (!string.Equals(relativePath, relativePath.Normalize(NormalizationForm.FormC), StringComparison.Ordinal) ||
                !CanonicalJson.IsNormalizedRelativePath(relativePath))
            {
                throw new InvalidOperationException("The served path is not already NFC-normalized: '" + relativePath + "'.");
            }

            return relativePath;
        }

        private static bool IsWithin(string root, string path)
        {
            return string.Equals(root, path, PathComparison) || path.StartsWith(EnsureTrailingSeparator(root), PathComparison);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(NormalizeFullPath(left), NormalizeFullPath(right), PathComparison);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly struct ManifestFile
        {
            public ManifestFile(string path, long size, string sha256)
            {
                Path = path;
                Size = size;
                Sha256 = sha256;
            }

            public string Path { get; }
            public long Size { get; }
            public string Sha256 { get; }
        }
    }
}
