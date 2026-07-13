using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Overbless.Editor.Build;

namespace Overbless.Editor.Evidence
{
    /// <summary>
    /// Produces a local, derived review snapshot. It is deliberately not a canonical evidence artifact.
    /// </summary>
    public static class ReviewIndexGenerator
    {
        private const string ReviewIndexFileName = "review-index.json";
        private const string ReviewIndexSchema = "overbless.review-index/v1";

        /// <summary>
        /// Writes one review index after the candidate has a complete, validated terminal machine event.
        /// The index has no authority in the gate and never overwrites a prior snapshot.
        /// </summary>
        public static string WriteReviewIndex(string candidateId)
        {
            if (string.IsNullOrEmpty(candidateId)) throw new ArgumentException("Candidate identifier is required.", nameof(candidateId));
            var terminal = CandidateCoordinator.GetValidatedTerminalMachineEvent(candidateId);
            var candidateDirectory = CandidateCoordinator.GetCandidateDirectory(candidateId);
            var indexPath = Path.Combine(candidateDirectory, ReviewIndexFileName);
            if (File.Exists(indexPath) || Directory.Exists(indexPath)) throw new InvalidOperationException("Write-once review index already exists: " + indexPath);

            var files = CollectFiles(candidateDirectory, indexPath);
            var index = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(ReviewIndexSchema)),
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(terminal.CandidateId)),
                new CanonicalJsonProperty("canonical", CanonicalJsonValue.Boolean(false)),
                new CanonicalJsonProperty("derivedFromEntrySha256", CanonicalJsonValue.String(terminal.EntrySha256)),
                new CanonicalJsonProperty("files", CanonicalJsonValue.Array(files)),
                new CanonicalJsonProperty("generatedUtc", CanonicalJsonValue.String(FormatUtc(DateTime.UtcNow))));

            WriteNew(indexPath, CanonicalJson.SerializeUtf8(index));
            return indexPath;
        }

        private static List<CanonicalJsonValue> CollectFiles(string candidateDirectory, string indexPath)
        {
            var root = Path.GetFullPath(candidateDirectory);
            EnsureNotReparsePoint(root);
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(root);
            var records = new List<FileRecord>();

            while (pendingDirectories.Count > 0)
            {
                var directory = pendingDirectories.Pop();
                EnsureNotReparsePoint(directory);

                var directories = Directory.GetDirectories(directory);
                Array.Sort(directories, StringComparer.Ordinal);
                foreach (var childDirectory in directories)
                {
                    EnsureNotReparsePoint(childDirectory);
                    pendingDirectories.Push(childDirectory);
                }

                var files = Directory.GetFiles(directory);
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var path in files)
                {
                    EnsureNotReparsePoint(path);
                    if (PathsEqual(path, indexPath)) continue;

                    var absolutePath = Path.GetFullPath(path);
                    if (!absolutePath.StartsWith(rootWithSeparator, comparison))
                    {
                        throw new InvalidOperationException("Review index encountered a path outside the candidate root: " + path);
                    }

                    var relativePath = Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
                    if (!CanonicalJson.IsNormalizedRelativePath(relativePath))
                    {
                        throw new InvalidOperationException("Review index encountered a non-normalized artifact path: " + relativePath);
                    }
                    records.Add(new FileRecord(relativePath, EvidenceArtifactIO.GetFileMetadataAtPath(absolutePath).Sha256));
                }
            }

            records.Sort((left, right) => CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path));
            var values = new List<CanonicalJsonValue>();
            foreach (var record in records)
            {
                values.Add(CanonicalJsonValue.Object(
                    new CanonicalJsonProperty("path", CanonicalJsonValue.String(record.Path)),
                    new CanonicalJsonProperty("sha256", CanonicalJsonValue.String(record.Sha256))));
            }
            return values;
        }

        private static void EnsureNotReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Review index refuses reparse-point paths: " + path);
            }
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }

        private static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private sealed class FileRecord
        {
            public FileRecord(string path, string sha256)
            {
                Path = path;
                Sha256 = sha256;
            }

            public string Path { get; }
            public string Sha256 { get; }
        }
    }
}
