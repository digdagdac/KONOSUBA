using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace Overbless.Editor.Evidence
{
    public sealed class EvidenceArtifactReference
    {
        public EvidenceArtifactReference(string path, string role, string sha256, IEnumerable<string> criterionIds)
            : this(path, role, -1L, sha256, criterionIds)
        {
        }

        public EvidenceArtifactReference(string path, string role, long size, string sha256, IEnumerable<string> criterionIds)
        {
            Path = path;
            Role = role;
            Size = size;
            Sha256 = sha256;
            if (criterionIds == null) throw new ArgumentNullException(nameof(criterionIds));

            var copy = new List<string>();
            foreach (var criterionId in criterionIds) copy.Add(criterionId);
            copy.Sort(CanonicalJson.CompareUtf8Ordinal);
            CriterionIds = copy.AsReadOnly();
        }

        public string Path { get; }
        public string Role { get; }
        public long Size { get; }
        public string Sha256 { get; }
        public IReadOnlyList<string> CriterionIds { get; }
    }

    public sealed class EvidenceManifestRequest
    {
        public EvidenceManifestRequest(
            string candidateId,
            string candidateSha256,
            string sourceManifestSha256,
            string buildManifestSha256,
            DateTimeOffset generatedUtc,
            IEnumerable<EvidenceArtifactReference> artifacts)
            : this(candidateId, candidateSha256, sourceManifestSha256, buildManifestSha256, generatedUtc, "Evidence", artifacts)
        {
        }

        public EvidenceManifestRequest(
            string candidateId,
            string candidateSha256,
            string sourceManifestSha256,
            string buildManifestSha256,
            DateTimeOffset generatedUtc,
            string artifactRoot,
            IEnumerable<EvidenceArtifactReference> artifacts)
        {
            CandidateId = candidateId;
            CandidateSha256 = candidateSha256;
            SourceManifestSha256 = sourceManifestSha256;
            BuildManifestSha256 = buildManifestSha256;
            GeneratedUtc = generatedUtc;
            ArtifactRoot = artifactRoot;
            if (artifacts == null) throw new ArgumentNullException(nameof(artifacts));

            var copy = new List<EvidenceArtifactReference>();
            foreach (var artifact in artifacts)
            {
                if (artifact == null) throw new ArgumentException("Artifact references cannot contain null.", nameof(artifacts));
                copy.Add(artifact);
            }

            Artifacts = copy.AsReadOnly();
        }

        public string CandidateId { get; }
        public string CandidateSha256 { get; }
        public string SourceManifestSha256 { get; }
        public string BuildManifestSha256 { get; }
        public DateTimeOffset GeneratedUtc { get; }
        public string ArtifactRoot { get; }
        public IReadOnlyList<EvidenceArtifactReference> Artifacts { get; }
    }

    public sealed class EvidenceManifestDocument
    {
        internal EvidenceManifestDocument(CanonicalJsonValue value, string sha256)
        {
            Value = value;
            Sha256 = sha256;
        }

        public CanonicalJsonValue Value { get; }
        public string Sha256 { get; }
        public byte[] Utf8Bytes => CanonicalJson.SerializeUtf8(Value);
    }

    /// <summary>Builds a sealed, coverage-complete evidence inventory from bytes already written under Evidence/.</summary>
    public static class EvidenceManifestBuilder
    {
        public const string DefaultOutputPath = "Evidence/evidence-manifest.json";

        public static EvidenceManifestDocument Build(EvidenceManifestRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateRequestIdentity(request);

            var artifacts = ValidateAndSortArtifacts(request);
            ValidateCoverage(artifacts);

            var artifactValues = new List<CanonicalJsonValue>();
            foreach (var artifact in artifacts) artifactValues.Add(ToCanonicalValue(artifact));

            var requiredCriteria = new List<CanonicalJsonValue>();
            foreach (var criterionId in EvidenceContracts.CriterionIds) requiredCriteria.Add(CanonicalJsonValue.String(criterionId));
            requiredCriteria.Sort(CompareCanonicalStrings);

            var unsignedValue = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("artifacts", CanonicalJsonValue.Array(artifactValues)),
                new CanonicalJsonProperty("buildManifestSha256", CanonicalJsonValue.String(request.BuildManifestSha256)),
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(request.CandidateId)),
                new CanonicalJsonProperty("candidateSha256", CanonicalJsonValue.String(request.CandidateSha256)),
                new CanonicalJsonProperty("generatedUtc", CanonicalJsonValue.String(FormatUtc(request.GeneratedUtc))),
                new CanonicalJsonProperty("requiredCriterionIds", CanonicalJsonValue.Array(requiredCriteria)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(EvidenceContracts.EvidenceManifest)),
                new CanonicalJsonProperty("sourceManifestSha256", CanonicalJsonValue.String(request.SourceManifestSha256)));
            var manifestSha256 = CanonicalJson.Sha256Hex(unsignedValue);
            var value = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("artifacts", CanonicalJsonValue.Array(artifactValues)),
                new CanonicalJsonProperty("buildManifestSha256", CanonicalJsonValue.String(request.BuildManifestSha256)),
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(request.CandidateId)),
                new CanonicalJsonProperty("candidateSha256", CanonicalJsonValue.String(request.CandidateSha256)),
                new CanonicalJsonProperty("evidenceManifestSha256", CanonicalJsonValue.String(manifestSha256)),
                new CanonicalJsonProperty("generatedUtc", CanonicalJsonValue.String(FormatUtc(request.GeneratedUtc))),
                new CanonicalJsonProperty("requiredCriterionIds", CanonicalJsonValue.Array(requiredCriteria)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(EvidenceContracts.EvidenceManifest)),
                new CanonicalJsonProperty("sourceManifestSha256", CanonicalJsonValue.String(request.SourceManifestSha256)));

            var shape = EvidenceSchemaValidator.ValidateSchemaObject(value, EvidenceContracts.EvidenceManifest, new[]
            {
                "schema", "candidateId", "candidateSha256", "sourceManifestSha256", "buildManifestSha256", "requiredCriterionIds", "artifacts", "generatedUtc", "evidenceManifestSha256"
            });
            if (!shape.IsValid) throw new InvalidOperationException("Evidence manifest shape is invalid: " + shape.Code + ".");
            return new EvidenceManifestDocument(value, manifestSha256);
        }

        public static EvidenceManifestDocument Write(string outputPath, EvidenceManifestRequest request)
        {
            var document = Build(request);
            EvidenceArtifactIO.WriteNew(outputPath, document.Utf8Bytes);
            return document;
        }

        public static EvidenceValidationResult Validate(EvidenceManifestRequest request)
        {
            try
            {
                Build(request);
                return EvidenceValidationResult.Pass();
            }
            catch (ArgumentException exception)
            {
                return EvidenceValidationResult.Fail("TYPE", exception.Message);
            }
            catch (FileNotFoundException exception)
            {
                return EvidenceValidationResult.Fail("MISSING_PATH", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return EvidenceValidationResult.Fail("INVENTORY", exception.Message);
            }
        }

        private static void ValidateRequestIdentity(EvidenceManifestRequest request)
        {
            if (string.IsNullOrEmpty(request.CandidateId)) throw new ArgumentException("Candidate ID is required.", nameof(request));
            if (!CanonicalJson.IsLowerSha256(request.CandidateSha256)) throw new ArgumentException("Candidate SHA-256 must be lowercase hexadecimal.", nameof(request));
            if (!CanonicalJson.IsLowerSha256(request.SourceManifestSha256)) throw new ArgumentException("Source manifest SHA-256 must be lowercase hexadecimal.", nameof(request));
            if (!CanonicalJson.IsLowerSha256(request.BuildManifestSha256)) throw new ArgumentException("Build manifest SHA-256 must be lowercase hexadecimal.", nameof(request));
            if (!CanonicalJson.IsNormalizedRelativePath(request.ArtifactRoot)) throw new ArgumentException("Artifact root must be normalized and root-relative.", nameof(request));
            if (request.GeneratedUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Generated time must be UTC.", nameof(request));
        }

        private static List<EvidenceArtifactReference> ValidateAndSortArtifacts(EvidenceManifestRequest request)
        {
            if (request.Artifacts.Count == 0) throw new InvalidOperationException("Evidence inventory cannot be empty.");

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var coveragePairs = new HashSet<string>(StringComparer.Ordinal);
            var validated = new List<EvidenceArtifactReference>();
            foreach (var artifact in request.Artifacts)
            {
                if (!CanonicalJson.IsNormalizedRelativePath(artifact.Path)) throw new ArgumentException("Artifact path must be normalized and root-relative.", nameof(request));
                if (string.IsNullOrEmpty(artifact.Role)) throw new ArgumentException("Artifact role is required.", nameof(request));
                if (artifact.Size < -1L) throw new ArgumentException("Artifact size must be nonnegative when declared.", nameof(request));
                if (!CanonicalJson.IsLowerSha256(artifact.Sha256)) throw new ArgumentException("Artifact SHA-256 must be lowercase hexadecimal.", nameof(request));
                if (!paths.Add(artifact.Path)) throw new InvalidOperationException("Evidence inventory contains a duplicate path: " + artifact.Path + ".");
                if (artifact.CriterionIds == null || artifact.CriterionIds.Count == 0) throw new InvalidOperationException("Each artifact must cover at least one criterion.");

                var criteria = new HashSet<string>(StringComparer.Ordinal);
                string previousCriterion = null;
                foreach (var criterionId in artifact.CriterionIds)
                {
                    if (string.IsNullOrEmpty(criterionId) || Array.IndexOf(EvidenceContracts.CriterionIds, criterionId) < 0)
                    {
                        throw new InvalidOperationException("Artifact contains an unknown criterion ID.");
                    }
                    if (!criteria.Add(criterionId)) throw new InvalidOperationException("Artifact contains a duplicate criterion ID.");
                    if (previousCriterion != null && CanonicalJson.CompareUtf8Ordinal(previousCriterion, criterionId) >= 0)
                    {
                        throw new InvalidOperationException("Artifact criterion IDs must be UTF-8 ordinal sorted.");
                    }
                    previousCriterion = criterionId;

                    var coverageKey = artifact.Path + "\n" + criterionId;
                    if (!coveragePairs.Add(coverageKey)) throw new InvalidOperationException("Evidence inventory contains a duplicate coverage pair.");
                }

                var actualMetadata = EvidenceArtifactIO.GetFileMetadata(request.ArtifactRoot, artifact.Path);
                if (artifact.Size >= 0 && artifact.Size != actualMetadata.Size)
                {
                    throw new InvalidOperationException("Artifact size does not match actual bytes: " + artifact.Path + ".");
                }
                if (!string.Equals(actualMetadata.Sha256, artifact.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Artifact SHA-256 does not match actual bytes: " + artifact.Path + ".");
                }
                validated.Add(new EvidenceArtifactReference(artifact.Path, artifact.Role, actualMetadata.Size, artifact.Sha256, artifact.CriterionIds));
            }

            validated.Sort(CompareArtifacts);
            return validated;
        }

        private static void ValidateCoverage(IReadOnlyList<EvidenceArtifactReference> artifacts)
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var artifact in artifacts)
            {
                foreach (var criterionId in artifact.CriterionIds) covered.Add(criterionId);
            }

            foreach (var criterionId in EvidenceContracts.CriterionIds)
            {
                if (!covered.Contains(criterionId)) throw new InvalidOperationException("Evidence inventory leaves a required criterion uncovered: " + criterionId + ".");
            }
        }


        private static CanonicalJsonValue ToCanonicalValue(EvidenceArtifactReference artifact)
        {
            var criteria = new List<CanonicalJsonValue>();
            foreach (var criterionId in artifact.CriterionIds) criteria.Add(CanonicalJsonValue.String(criterionId));
            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("criterionIds", CanonicalJsonValue.Array(criteria)),
                new CanonicalJsonProperty("path", CanonicalJsonValue.String(artifact.Path)),
                new CanonicalJsonProperty("role", CanonicalJsonValue.String(artifact.Role)),
                new CanonicalJsonProperty("sha256", CanonicalJsonValue.String(artifact.Sha256)),
                new CanonicalJsonProperty("size", CanonicalJsonValue.Number(artifact.Size)));
        }

        private static int CompareArtifacts(EvidenceArtifactReference left, EvidenceArtifactReference right)
        {
            return CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path);
        }

        private static int CompareCanonicalStrings(CanonicalJsonValue left, CanonicalJsonValue right)
        {
            return CanonicalJson.CompareUtf8Ordinal(left.StringValue, right.StringValue);
        }

        private static string FormatUtc(DateTimeOffset value)
        {
            return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class EvidenceArtifactMetadata
    {
        public EvidenceArtifactMetadata(long size, string sha256)
        {
            Size = size;
            Sha256 = sha256;
        }

        public long Size { get; }
        public string Sha256 { get; }
    }

    internal static class EvidenceArtifactIO
    {
        private const string EvidenceDirectory = "Evidence/";

        public static byte[] ReadAllBytes(string relativePath)
        {
            var fullPath = GetFullPath(relativePath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Referenced evidence artifact is missing.", relativePath);
            return File.ReadAllBytes(fullPath);
        }

        public static byte[] ReadAllBytes(string rootRelativePath, string relativePath)
        {
            var fullPath = GetFullPath(rootRelativePath, relativePath);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Referenced evidence artifact is missing.", relativePath);
            return File.ReadAllBytes(fullPath);
        }

        internal static EvidenceArtifactMetadata GetFileMetadata(string rootRelativePath, string relativePath)
        {
            var fullPath = GetFullPath(rootRelativePath, relativePath);
            return GetFileMetadataAtPath(fullPath, relativePath);
        }

        internal static EvidenceArtifactMetadata GetFileMetadataAtPath(string fullPath)
        {
            return GetFileMetadataAtPath(fullPath, fullPath);
        }

        private static EvidenceArtifactMetadata GetFileMetadataAtPath(string fullPath, string relativePath)
        {
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Referenced evidence artifact is missing.", relativePath);
            EnsureNotReparsePoint(fullPath, relativePath);
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha256 = SHA256.Create())
            {
                EnsureNotReparsePoint(fullPath, relativePath);
                var size = stream.Length;
                var hash = sha256.ComputeHash(stream);
                return new EvidenceArtifactMetadata(size, ToLowerHex(hash));
            }
        }

        public static void WriteNew(string relativePath, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            var normalized = CanonicalJson.NormalizeRelativePath(relativePath);
            if (!normalized.StartsWith(EvidenceDirectory, StringComparison.Ordinal))
            {
                throw new ArgumentException("Persistent evidence must be written under Evidence/.", nameof(relativePath));
            }

            var fullPath = GetFullPath(normalized);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Evidence output has no parent directory.");
            Directory.CreateDirectory(directory);
            fullPath = GetFullPath(normalized);

            try
            {
                using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
            }
            catch (IOException exception) when (File.Exists(fullPath))
            {
                throw new InvalidOperationException("Evidence artifacts are write-once: " + normalized + ".", exception);
            }

            var persisted = ReadAllBytes(normalized);
            if (!CanonicalJson.ByteArraysEqual(bytes, persisted))
            {
                throw new IOException("Evidence output bytes did not persist exactly: " + normalized + ".");
            }
        }

        public static string GetFullPath(string relativePath)
        {
            var normalized = CanonicalJson.NormalizeRelativePath(relativePath);
            var root = Path.GetFullPath(Directory.GetCurrentDirectory());
            return GetFullPathUnderRoot(root, normalized, relativePath);
        }

        public static string GetFullPath(string rootRelativePath, string relativePath)
        {
            var root = GetFullPath(rootRelativePath);
            var normalized = CanonicalJson.NormalizeRelativePath(relativePath);
            return GetFullPathUnderRoot(root, normalized, relativePath);
        }

        private static string GetFullPathUnderRoot(string root, string normalizedPath, string originalPath)
        {
            var absoluteRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(absoluteRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = absoluteRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? absoluteRoot
                : absoluteRoot + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(rootWithSeparator, comparison))
            {
                throw new ArgumentException("Path escapes the project root.", nameof(originalPath));
            }
            RejectReparsePointComponents(absoluteRoot, normalizedPath, originalPath);
            return fullPath;
        }

        private static void RejectReparsePointComponents(string root, string normalizedPath, string originalPath)
        {
            if (!EnsureNotReparsePointIfExists(root, originalPath)) return;
            var current = root;
            foreach (var component in normalizedPath.Split('/'))
            {
                current = Path.Combine(current, component);
                if (!EnsureNotReparsePointIfExists(current, originalPath)) return;
            }
        }

        private static bool EnsureNotReparsePointIfExists(string path, string originalPath)
        {
            try
            {
                EnsureNotReparsePoint(path, originalPath);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        private static void EnsureNotReparsePoint(string path, string originalPath)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Evidence artifact path traverses a reparse point: " + originalPath + ".");
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            const string Hex = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = Hex[bytes[index] >> 4];
                characters[index * 2 + 1] = Hex[bytes[index] & 15];
            }
            return new string(characters);
        }
    }
}
