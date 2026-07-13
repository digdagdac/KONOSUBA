using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Overbless.Editor.Evidence
{
    public sealed class ScopeAuditAllowance
    {
        public ScopeAuditAllowance(string path, string token, string approvalReference, string approvalSha256)
            : this(path, token, 0, 0, string.Empty, approvalReference, approvalSha256)
        {
        }

        public ScopeAuditAllowance(
            string path,
            string token,
            int line,
            int column,
            string sourceSha256,
            string approvalReference,
            string approvalSha256)
        {
            Path = path;
            Token = token;
            Line = line;
            Column = column;
            SourceSha256 = sourceSha256;
            ApprovalReference = approvalReference;
            ApprovalSha256 = approvalSha256;
        }

        public string Path { get; }
        public string Token { get; }
        public string ApprovalReference { get; }
        public int Line { get; }
        public int Column { get; }
        public string SourceSha256 { get; }
        public string ApprovalSha256 { get; }
    }

    public sealed class ScopeAuditMatch
    {
        internal ScopeAuditMatch(string path, string token, string sourceSha256, int line, int column, bool allowlisted, string approvalReference)
        {
            Path = path;
            Token = token;
            SourceSha256 = sourceSha256;
            Line = line;
            Column = column;
            Allowlisted = allowlisted;
            ApprovalReference = approvalReference;
        }

        public string Path { get; }
        public string Token { get; }
        public string SourceSha256 { get; }
        public int Line { get; }
        public int Column { get; }
        public bool Allowlisted { get; }
        public string ApprovalReference { get; }
    }

    public sealed class ScopeAuditReport
    {
        internal ScopeAuditReport(CanonicalJsonValue value, IReadOnlyList<ScopeAuditMatch> matches)
        {
            Value = value;
            Matches = matches;
        }

        public CanonicalJsonValue Value { get; }
        public IReadOnlyList<ScopeAuditMatch> Matches { get; }
        public bool IsClean
        {
            get
            {
                foreach (var match in Matches)
                {
                    if (!match.Allowlisted) return false;
                }
                return true;
            }
        }

        public byte[] Utf8Bytes => CanonicalJson.SerializeUtf8(Value);
    }

    /// <summary>Scans source roots for explicitly forbidden M2 identifiers and records only explicitly governed exceptions.</summary>
    public static class ScopeAudit
    {
        public const string Schema = "overbless.source-scope-audit/v1";
        public const string DefaultOutputPath = "Evidence/scope-audit.json";

        private static readonly string[] DefaultForbiddenTokens =
        {
            "Breakable",
            "Cliff",
            "Echo",
            "FinalEncounter",
            "Golem",
            "Residue",
            "Room_02",
            "Room_03",
            "Room_Final",
            "Trap"
        };
        private static readonly string[] DefaultScannedRoots =
        {
            "Assets/_Project/Data",
            "Assets/_Project/Editor",
            "Assets/_Project/Prefabs",
            "Assets/_Project/Runtime",
            "Assets/_Project/Scenes"
        };
        private static readonly string[] DefaultExcludedSourcePaths =
        {
            "Assets/_Project/Editor/Evidence/ScopeAudit.cs"
        };

        public static IReadOnlyList<string> ForbiddenGameplayTokens => Array.AsReadOnly(DefaultForbiddenTokens);

        public static ScopeAuditReport Audit(
            IEnumerable<string> scannedRoots,
            IEnumerable<string> forbiddenTokens,
            IEnumerable<ScopeAuditAllowance> allowlist)
        {
            return Audit(scannedRoots, forbiddenTokens, allowlist, new string[0]);
        }

        private static ScopeAuditReport Audit(
            IEnumerable<string> scannedRoots,
            IEnumerable<string> forbiddenTokens,
            IEnumerable<ScopeAuditAllowance> allowlist,
            IEnumerable<string> excludedSourcePaths)
        {
            var roots = NormalizeRoots(scannedRoots);
            var tokens = NormalizeTokens(forbiddenTokens);
            var sourcePaths = ExcludeSourcePaths(EnumerateSourcePaths(roots), excludedSourcePaths);
            var allowances = NormalizeAllowances(allowlist, tokens, sourcePaths);
            var matches = FindMatches(sourcePaths, tokens, allowances);
            ValidateAllowancesMatch(allowances, matches);

            var rootValues = new List<CanonicalJsonValue>();
            foreach (var root in roots) rootValues.Add(CanonicalJsonValue.String(root));
            var tokenValues = new List<CanonicalJsonValue>();
            foreach (var token in tokens) tokenValues.Add(CanonicalJsonValue.String(token));
            var allowanceValues = new List<CanonicalJsonValue>();
            foreach (var allowance in allowances) allowanceValues.Add(ToCanonicalValue(allowance));
            var matchValues = new List<CanonicalJsonValue>();
            foreach (var match in matches) matchValues.Add(ToCanonicalValue(match));

            var value = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("allowlist", CanonicalJsonValue.Array(allowanceValues)),
                new CanonicalJsonProperty("auditStatus", CanonicalJsonValue.String(ContainsUnallowlistedMatch(matches) ? "FAIL" : "PASS")),
                new CanonicalJsonProperty("forbiddenTokens", CanonicalJsonValue.Array(tokenValues)),
                new CanonicalJsonProperty("matches", CanonicalJsonValue.Array(matchValues)),
                new CanonicalJsonProperty("scannedRoots", CanonicalJsonValue.Array(rootValues)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(Schema)));
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(value, Schema, new[]
            {
                "schema", "scannedRoots", "forbiddenTokens", "allowlist", "matches", "auditStatus"
            });
            if (!shape.IsValid) throw new InvalidOperationException("Scope audit shape is invalid: " + shape.Code + ".");
            return new ScopeAuditReport(value, matches.AsReadOnly());
        }

        public static ScopeAuditReport AuditDefault(IEnumerable<ScopeAuditAllowance> allowlist)
        {
            return Audit(DefaultScannedRoots, DefaultForbiddenTokens, allowlist, DefaultExcludedSourcePaths);
        }

        public static string Export(
            string outputPath,
            IEnumerable<string> scannedRoots,
            IEnumerable<string> forbiddenTokens,
            IEnumerable<ScopeAuditAllowance> allowlist)
        {
            var report = Audit(scannedRoots, forbiddenTokens, allowlist);
            var bytes = report.Utf8Bytes;
            EvidenceArtifactIO.WriteNew(outputPath, bytes);
            return CanonicalJson.Sha256Hex(bytes);
        }

        /// <summary>Batch-mode entry point. It records an empty governance allowlist; forbidden matches remain FAIL evidence.</summary>
        public static void Execute()
        {
            string outputPath;
            if (!TryGetCommandLineArgument("-scopeAuditOutput", out outputPath)) outputPath = DefaultOutputPath;
            var report = AuditDefault(new ScopeAuditAllowance[0]);
            var bytes = report.Utf8Bytes;
            EvidenceArtifactIO.WriteNew(outputPath, bytes);
            var sha256 = CanonicalJson.Sha256Hex(bytes);
            Debug.Log("Wrote scope audit " + outputPath + " (" + sha256 + ").");
        }

        private static List<string> NormalizeRoots(IEnumerable<string> roots)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                if (!CanonicalJson.IsNormalizedRelativePath(root)) throw new ArgumentException("Audit roots must be normalized root-relative paths.", nameof(roots));
                if (!seen.Add(root)) throw new ArgumentException("Audit roots must not contain duplicates.", nameof(roots));
                var fullPath = EvidenceArtifactIO.GetFullPath(root);
                if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Audit root is missing: " + root + ".");
                normalized.Add(root);
            }
            if (normalized.Count == 0) throw new ArgumentException("At least one audit root is required.", nameof(roots));
            normalized.Sort(CanonicalJson.CompareUtf8Ordinal);
            return normalized;
        }

        private static List<string> NormalizeTokens(IEnumerable<string> tokens)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token) || token != token.Trim()) throw new ArgumentException("Forbidden tokens must be nonempty exact identifiers.", nameof(tokens));
                if (!seen.Add(token)) throw new ArgumentException("Forbidden tokens must not contain duplicates.", nameof(tokens));
                normalized.Add(token);
            }
            if (normalized.Count == 0) throw new ArgumentException("At least one forbidden token is required.", nameof(tokens));
            normalized.Sort(CanonicalJson.CompareUtf8Ordinal);
            return normalized;
        }

        private static List<string> EnumerateSourcePaths(IEnumerable<string> roots)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                var fullRoot = EvidenceArtifactIO.GetFullPath(root);
                foreach (var fullPath in EnumerateGovernedFiles(fullRoot))
                {
                    var relativePath = GetRelativeProjectPath(fullPath);
                    if (!seen.Add(relativePath)) throw new InvalidOperationException("Audit source paths overlap: " + relativePath + ".");
                    paths.Add(relativePath);
                }
            }
            if (paths.Count == 0) throw new InvalidOperationException("Audit roots contain no governed C# or serialized Unity files.");
            paths.Sort(CanonicalJson.CompareUtf8Ordinal);
            return paths;
        }
        private static List<string> ExcludeSourcePaths(
            IReadOnlyList<string> sourcePaths,
            IEnumerable<string> excludedSourcePaths)
        {
            if (excludedSourcePaths == null) throw new ArgumentNullException(nameof(excludedSourcePaths));
            var sourceSet = new HashSet<string>(sourcePaths, StringComparer.Ordinal);
            var excludedSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var excludedSourcePath in excludedSourcePaths)
            {
                if (!CanonicalJson.IsNormalizedRelativePath(excludedSourcePath) || !sourceSet.Contains(excludedSourcePath))
                {
                    throw new ArgumentException("Audit exclusions must reference governed source paths.", nameof(excludedSourcePaths));
                }
                if (!excludedSet.Add(excludedSourcePath))
                {
                    throw new ArgumentException("Audit exclusions must not contain duplicates.", nameof(excludedSourcePaths));
                }
            }

            var paths = new List<string>();
            foreach (var sourcePath in sourcePaths)
            {
                if (!excludedSet.Contains(sourcePath)) paths.Add(sourcePath);
            }
            if (paths.Count == 0) throw new InvalidOperationException("Audit roots contain no source paths after governed exclusions.");
            return paths;
        }


        private static List<string> EnumerateGovernedFiles(string fullRoot)
        {
            var paths = new List<string>();
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(fullRoot);
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
                foreach (var file in files)
                {
                    EnsureNotReparsePoint(file);
                    if (IsGovernedFile(file)) paths.Add(file);
                }
            }
            return paths;
        }

        private static bool IsGovernedFile(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureNotReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Scope audit refuses reparse-point paths: " + path + ".");
            }
        }

        private static List<ScopeAuditAllowance> NormalizeAllowances(
            IEnumerable<ScopeAuditAllowance> allowlist,
            IReadOnlyList<string> tokens,
            IReadOnlyList<string> sourcePaths)
        {
            if (allowlist == null) throw new ArgumentNullException(nameof(allowlist));
            var normalized = new List<ScopeAuditAllowance>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var sourceSet = new HashSet<string>(sourcePaths, StringComparer.Ordinal);
            var tokenSet = new HashSet<string>(tokens, StringComparer.Ordinal);
            foreach (var allowance in allowlist)
            {
                if (allowance == null) throw new ArgumentException("Allowlist entries cannot contain null.", nameof(allowlist));
                if (!CanonicalJson.IsNormalizedRelativePath(allowance.Path) || !sourceSet.Contains(allowance.Path))
                {
                    throw new ArgumentException("Allowlist entry references an unknown source path.", nameof(allowlist));
                }
                if (string.IsNullOrEmpty(allowance.Token) || !tokenSet.Contains(allowance.Token))
                {
                    throw new ArgumentException("Allowlist entry references an unknown forbidden token.", nameof(allowlist));
                }
                if (!CanonicalJson.IsNormalizedRelativePath(allowance.ApprovalReference))
                {
                    throw new ArgumentException("Allowlist approval reference must be a normalized root-relative path.", nameof(allowlist));
                }
                if (!CanonicalJson.IsLowerSha256(allowance.ApprovalSha256))
                {
                    throw new ArgumentException("Allowlist approval SHA-256 must be lowercase hexadecimal.", nameof(allowlist));
                }
                if (allowance.Line <= 0 || allowance.Column <= 0)
                {
                    throw new ArgumentException("Allowlist entries must identify a positive line and column.", nameof(allowlist));
                }
                if (!CanonicalJson.IsLowerSha256(allowance.SourceSha256))
                {
                    throw new ArgumentException("Allowlist source SHA-256 must be lowercase hexadecimal.", nameof(allowlist));
                }
                var approvalBytes = EvidenceArtifactIO.ReadAllBytes(allowance.ApprovalReference);
                if (!string.Equals(CanonicalJson.Sha256Hex(approvalBytes), allowance.ApprovalSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Allowlist approval SHA-256 does not match actual bytes: " + allowance.ApprovalReference + ".");
                }

                var identity = GetAllowanceIdentity(
                    allowance.Path,
                    allowance.Token,
                    allowance.SourceSha256,
                    allowance.Line,
                    allowance.Column);
                if (!identities.Add(identity)) throw new ArgumentException("Allowlist contains a duplicate token occurrence.", nameof(allowlist));
                normalized.Add(allowance);
            }
            normalized.Sort(CompareAllowances);
            return normalized;
        }

        private static List<ScopeAuditMatch> FindMatches(
            IReadOnlyList<string> sourcePaths,
            IReadOnlyList<string> tokens,
            IReadOnlyList<ScopeAuditAllowance> allowances)
        {
            var allowanceMap = new Dictionary<string, ScopeAuditAllowance>(StringComparer.Ordinal);
            foreach (var allowance in allowances)
            {
                allowanceMap.Add(GetAllowanceIdentity(
                    allowance.Path,
                    allowance.Token,
                    allowance.SourceSha256,
                    allowance.Line,
                    allowance.Column), allowance);
            }

            var matches = new List<ScopeAuditMatch>();
            foreach (var sourcePath in sourcePaths)
            {
                string sourceSha256;
                var text = ReadUtf8Source(sourcePath, out sourceSha256);
                foreach (var token in tokens)
                {
                    var expression = new Regex(Regex.Escape(token), RegexOptions.CultureInvariant);
                    foreach (Match match in expression.Matches(text))
                    {
                        var lineAndColumn = GetLineAndColumn(text, match.Index);
                        ScopeAuditAllowance allowance;
                        allowanceMap.TryGetValue(GetAllowanceIdentity(
                            sourcePath,
                            token,
                            sourceSha256,
                            lineAndColumn.Line,
                            lineAndColumn.Column), out allowance);
                        matches.Add(new ScopeAuditMatch(
                            sourcePath,
                            token,
                            sourceSha256,
                            lineAndColumn.Line,
                            lineAndColumn.Column,
                            allowance != null,
                            allowance == null ? string.Empty : allowance.ApprovalReference));
                    }
                }
            }
            matches.Sort(CompareMatches);
            return matches;
        }

        private static void ValidateAllowancesMatch(IEnumerable<ScopeAuditAllowance> allowances, IEnumerable<ScopeAuditMatch> matches)
        {
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matches)
            {
                if (match.Allowlisted)
                {
                    matched.Add(GetAllowanceIdentity(
                        match.Path,
                        match.Token,
                        match.SourceSha256,
                        match.Line,
                        match.Column));
                }
            }
            foreach (var allowance in allowances)
            {
                if (!matched.Contains(GetAllowanceIdentity(
                    allowance.Path,
                    allowance.Token,
                    allowance.SourceSha256,
                    allowance.Line,
                    allowance.Column)))
                {
                    throw new InvalidOperationException("Allowlist entry does not match an exact forbidden token occurrence: " + allowance.Path + ".");
                }
            }
        }

        private static CanonicalJsonValue ToCanonicalValue(ScopeAuditAllowance allowance)
        {
            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("approvalReference", CanonicalJsonValue.String(allowance.ApprovalReference)),
                new CanonicalJsonProperty("approvalSha256", CanonicalJsonValue.String(allowance.ApprovalSha256)),
                new CanonicalJsonProperty("column", CanonicalJsonValue.Number(allowance.Column)),
                new CanonicalJsonProperty("line", CanonicalJsonValue.Number(allowance.Line)),
                new CanonicalJsonProperty("path", CanonicalJsonValue.String(allowance.Path)),
                new CanonicalJsonProperty("sourceSha256", CanonicalJsonValue.String(allowance.SourceSha256)),
                new CanonicalJsonProperty("token", CanonicalJsonValue.String(allowance.Token)));
        }

        private static CanonicalJsonValue ToCanonicalValue(ScopeAuditMatch match)
        {
            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("allowlisted", CanonicalJsonValue.Boolean(match.Allowlisted)),
                new CanonicalJsonProperty("approvalReference", string.IsNullOrEmpty(match.ApprovalReference) ? CanonicalJsonValue.Null() : CanonicalJsonValue.String(match.ApprovalReference)),
                new CanonicalJsonProperty("column", CanonicalJsonValue.Number(match.Column)),
                new CanonicalJsonProperty("line", CanonicalJsonValue.Number(match.Line)),
                new CanonicalJsonProperty("path", CanonicalJsonValue.String(match.Path)),
                new CanonicalJsonProperty("sourceSha256", CanonicalJsonValue.String(match.SourceSha256)),
                new CanonicalJsonProperty("token", CanonicalJsonValue.String(match.Token)));
        }

        private static bool ContainsUnallowlistedMatch(IEnumerable<ScopeAuditMatch> matches)
        {
            foreach (var match in matches)
            {
                if (!match.Allowlisted) return true;
            }
            return false;
        }

        private static string ReadUtf8Source(string path, out string sourceSha256)
        {
            var bytes = EvidenceArtifactIO.ReadAllBytes(path);
            sourceSha256 = CanonicalJson.Sha256Hex(bytes);
            try
            {
                return new System.Text.UTF8Encoding(false, true).GetString(bytes);
            }
            catch (System.Text.DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Audit source is not valid UTF-8: " + path + ".", exception);
            }
        }

        private static string GetAllowanceIdentity(string path, string token, string sourceSha256, int line, int column)
        {
            return path + "\n" + token + "\n" + sourceSha256 + "\n" + line + "\n" + column;
        }

        private static LineAndColumn GetLineAndColumn(string text, int index)
        {
            var line = 1;
            var column = 1;
            for (var characterIndex = 0; characterIndex < index; characterIndex++)
            {
                if (text[characterIndex] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }
            return new LineAndColumn(line, column);
        }

        private static string GetRelativeProjectPath(string fullPath)
        {
            var root = Path.GetFullPath(Directory.GetCurrentDirectory());
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            var absolutePath = Path.GetFullPath(fullPath);
            if (!absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Audit source escapes the project root.", nameof(fullPath));
            }
            var relative = absolutePath.Substring(rootWithSeparator.Length).Replace(Path.DirectorySeparatorChar, '/');
            return CanonicalJson.NormalizeRelativePath(relative);
        }

        private static int CompareAllowances(ScopeAuditAllowance left, ScopeAuditAllowance right)
        {
            var path = CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path);
            if (path != 0) return path;
            var token = CanonicalJson.CompareUtf8Ordinal(left.Token, right.Token);
            if (token != 0) return token;
            var sourceSha256 = CanonicalJson.CompareUtf8Ordinal(left.SourceSha256, right.SourceSha256);
            if (sourceSha256 != 0) return sourceSha256;
            var line = left.Line.CompareTo(right.Line);
            if (line != 0) return line;
            return left.Column.CompareTo(right.Column);
        }

        private static int CompareMatches(ScopeAuditMatch left, ScopeAuditMatch right)
        {
            var path = CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path);
            if (path != 0) return path;
            var line = left.Line.CompareTo(right.Line);
            if (line != 0) return line;
            var column = left.Column.CompareTo(right.Column);
            if (column != 0) return column;
            return CanonicalJson.CompareUtf8Ordinal(left.Token, right.Token);
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

        private struct LineAndColumn
        {
            public LineAndColumn(int line, int column)
            {
                Line = line;
                Column = column;
            }

            public int Line { get; }
            public int Column { get; }
        }
    }
}
