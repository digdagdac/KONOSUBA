using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Security.Cryptography;
using UnityEngine;
using Overbless.Editor.Build;

namespace Overbless.Editor.Evidence
{
    public sealed class M2GateValidationResult
    {
        internal M2GateValidationResult(bool isMachineReady, bool isM2Approved, IReadOnlyList<string> errors)
        {
            IsMachineReady = isMachineReady;
            IsM2Approved = isM2Approved;
            Errors = errors;
        }

        public bool IsMachineReady { get; }
        public bool IsM2Approved { get; }
        public string DerivedState => IsM2Approved ? "PASS" : "REWORK";
        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// Fail-closed evidence validator. It only reads a sealed candidate and never creates a user decision.
    /// </summary>
    public static class M2EntryGateValidator
    {
        public const string DefaultEvidenceRoot = "Evidence/M2EntryGate";
        private const string CandidateFile = "candidate.json";
        private const string SourceManifestFile = "source-manifest.json";
        private const string BuildManifestFile = "build/build-manifest.json";
        private const string EvidenceManifestFile = "evidence-manifest.json";
        private const string ValidatorReportFile = "validator-report.json";
        private const string TransitionLogFile = "transition-log.jsonl";
        private const string GateDecisionFile = "gate-decision.json";
        private const int ExpectedArtifactCount = 66;
        private static readonly IReadOnlyDictionary<string, ResultExpectation> ExpectedResults = CreateExpectedResults();
        private static readonly string[] ExpectedScopeRoots =
        {
            "Assets/_Project/Data",
            "Assets/_Project/Prefabs",
            "Assets/_Project/Runtime",
            "Assets/_Project/Scenes"
        };
        [ThreadStatic] private static ArtifactSnapshotCache activeSnapshots;

        /// <summary>Batch-mode entry point. Requires -candidateId and exits by throwing when M2 remains blocked.</summary>
        public static void Validate()
        {
            string candidateId;
            if (!TryGetCommandLineArgument("-candidateId", out candidateId)) throw new InvalidOperationException("-candidateId is required.");
            var result = ValidateCandidate(candidateId, true);
            if (!result.IsM2Approved) throw new InvalidOperationException("M2 entry is REWORK: " + string.Join(" | ", result.Errors));
            UnityEngine.Debug.Log("M2 entry gate PASS for " + candidateId + ".");
        }

        public static M2GateValidationResult ValidateCandidate(string candidateId)
        {
            return ValidateCandidate(candidateId, false);
        }

        public static M2GateValidationResult ValidateCandidate(string candidateId, bool requireUserPass)
        {
            if (!CandidateCoordinator.IsValidCandidateId(candidateId)) return Failure("Candidate ID is invalid.");
            var root = Path.Combine(DefaultEvidenceRoot, candidateId);
            return ValidateCandidateRoot(root, candidateId, requireUserPass);
        }

        public static M2GateValidationResult ValidateCandidateRoot(string candidateRoot, string candidateId, bool requireUserPass)
        {
            var errors = new List<string>();
            if (!CandidateCoordinator.IsValidCandidateId(candidateId)) return Failure("Candidate ID is invalid.");
            if (string.IsNullOrEmpty(candidateRoot) || !TryValidateDirectoryPath(candidateRoot, "Candidate root", errors)) return Failure(errors);
            activeSnapshots = new ArtifactSnapshotCache();

            Document candidate;
            Document source;
            Document build;
            Document evidence;
            Document report;
            if (!TryReadDocument(candidateRoot, CandidateFile, errors, out candidate) || !TryReadDocument(candidateRoot, SourceManifestFile, errors, out source) || !TryReadDocument(candidateRoot, BuildManifestFile, errors, out build) || !TryReadDocument(candidateRoot, EvidenceManifestFile, errors, out evidence) || !TryReadDocument(candidateRoot, ValidatorReportFile, errors, out report))
            {
                return Failure(errors);
            }

            string candidateSelfHash;
            string candidateSourceCommit;
            string candidateScene;
            string sourceSelfHash;
            string sourcePackageLockHash;
            string buildSelfHash;
            string buildFileSetHash;
            string evidenceSelfHash;
            string reportSelfHash;
            Dictionary<string, string> resultDocumentHashes;
            CanonicalJsonValue sourceFiles;
            if (!ValidateCandidate(candidate, candidateId, errors, out candidateSelfHash, out candidateSourceCommit, out candidateScene) ||
                !ValidateSourceManifest(source, candidateRoot, candidateId, candidateSelfHash, candidateSourceCommit, errors, out sourceSelfHash, out sourcePackageLockHash, out sourceFiles) ||
                !ValidateBuildManifest(build, candidateRoot, candidateId, candidateSourceCommit, sourceSelfHash, candidateScene, sourceFiles, errors, out buildSelfHash, out buildFileSetHash) ||
                !ValidateEvidenceManifest(evidence, candidateRoot, candidateId, candidateSelfHash, sourceSelfHash, buildSelfHash, sourcePackageLockHash, candidateScene, sourceFiles, errors, out evidenceSelfHash, out resultDocumentHashes) ||
                !ValidateValidatorReport(report, candidateId, evidenceSelfHash, errors, out reportSelfHash))
            {
                return Failure(errors);
            }

            if (!ValidateTransitionLog(candidateRoot, candidateId, candidateSelfHash, sourceSelfHash, build.RawSha256, buildFileSetHash, evidence.RawSha256, report.RawSha256, resultDocumentHashes, errors)) return Failure(errors);

            var decisionValid = TryValidateGateDecision(candidateRoot, candidateId, evidence.RawSha256, report.RawSha256, errors, out var decisionIsPass);
            if (!decisionValid && CandidateArtifactExists(candidateRoot, GateDecisionFile, errors)) return Failure(errors);
            if (requireUserPass && !HasConfiguredTrustedDecisionKey(errors)) return Failure(errors);
            var machineReady = errors.Count == 0;
            var approved = machineReady && decisionValid && decisionIsPass;
            if (requireUserPass && !approved)
            {
                if (!decisionValid) errors.Add("A valid user gate decision is required for M2 entry.");
                else if (!decisionIsPass) errors.Add("The user gate decision is REWORK.");
            }

            return new M2GateValidationResult(machineReady, approved, errors.AsReadOnly());
        }

        private static bool ValidateCandidate(Document document, string candidateId, List<string> errors, out string selfHash, out string sourceCommit, out string scene)
        {
            selfHash = null;
            sourceCommit = null;
            scene = null;
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(document.Value, EvidenceContracts.Candidate, new[] { "schema", "candidateId", "sourceCommit", "unityVersion", "scene", "createdUtc", "candidateSha256" });
            if (!AddResult(shape, "candidate", errors)) return false;
            string actualCandidateId;
            string unityVersion;
            string createdUtc;
            if (!TryGetString(document.Value, "candidateId", out actualCandidateId) || !TryGetString(document.Value, "sourceCommit", out sourceCommit) || !TryGetString(document.Value, "unityVersion", out unityVersion) || !TryGetString(document.Value, "scene", out scene) || !TryGetString(document.Value, "createdUtc", out createdUtc))
            {
                errors.Add("Candidate field type is invalid.");
                return false;
            }
            if (!string.Equals(actualCandidateId, candidateId, StringComparison.Ordinal) || !IsLowerHex(sourceCommit, 40) || !string.Equals(unityVersion, "6000.0.72f1", StringComparison.Ordinal) || !CanonicalJson.IsNormalizedRelativePath(scene) || !IsUtcMilliseconds(createdUtc))
            {
                errors.Add("Candidate identity is invalid.");
                return false;
            }
            return ValidateSelfHash(document.Value, "candidateSha256", errors, out selfHash);
        }

        private static bool ValidateSourceManifest(Document document, string candidateRoot, string candidateId, string candidateSelfHash, string candidateSourceCommit, List<string> errors, out string selfHash, out string packageLockHash, out CanonicalJsonValue sourceFiles)
        {
            selfHash = null;
            packageLockHash = null;
            sourceFiles = null;
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(document.Value, EvidenceContracts.SourceManifest, new[] { "schema", "candidateId", "candidateSha256", "sourceCommit", "files", "packageLockSha256", "sourceTreeSha256", "sourceManifestSha256" });
            if (!AddResult(shape, "source manifest", errors)) return false;
            string actualCandidateId;
            string candidateHash;
            string sourceCommit;
            string treeHash;
            CanonicalJsonValue files;
            if (!TryGetString(document.Value, "candidateId", out actualCandidateId) || !TryGetString(document.Value, "candidateSha256", out candidateHash) || !TryGetString(document.Value, "sourceCommit", out sourceCommit) || !TryGetString(document.Value, "packageLockSha256", out packageLockHash) || !TryGetString(document.Value, "sourceTreeSha256", out treeHash) || !document.Value.TryGetSingleProperty("files", out files) || files.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Source manifest field type is invalid.");
                return false;
            }
            if (!string.Equals(actualCandidateId, candidateId, StringComparison.Ordinal) || !string.Equals(candidateHash, candidateSelfHash, StringComparison.Ordinal) || !string.Equals(sourceCommit, candidateSourceCommit, StringComparison.Ordinal) || !CanonicalJson.IsLowerSha256(packageLockHash) || !CanonicalJson.IsLowerSha256(treeHash))
            {
                errors.Add("Source manifest identity binding is invalid.");
                return false;
            }
            if (!ValidateSourceFiles(files, treeHash, FindProjectRoot(candidateRoot), sourceCommit, errors) || !ManifestContainsFileHash(files, "Packages/packages-lock.json", packageLockHash))
            {
                errors.Add("Source manifest package-lock binding is invalid.");
                return false;
            }
            sourceFiles = files;
            return ValidateSelfHash(document.Value, "sourceManifestSha256", errors, out selfHash);
        }

        private static bool ValidateBuildManifest(
            Document document,
            string candidateRoot,
            string candidateId,
            string sourceCommit,
            string sourceSelfHash,
            string candidateScene,
            CanonicalJsonValue sourceFiles,
            List<string> errors,
            out string selfHash,
            out string fileSetHash)
        {
            selfHash = null;
            fileSetHash = null;
            var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(document.Value, new[] { "schema", "candidateId", "sourceManifestSha256", "sourceCapabilitySha256", "servedRootManifestSha256", "settings", "files", "fileSetSha256", "buildManifestSha256" });
            if (!AddResult(shape, "build manifest", errors)) return false;
            string actualSchema;
            string actualCandidateId;
            string sourceHash;
            string sourceCapabilityHash;
            string servedRootHash;
            CanonicalJsonValue settings;
            CanonicalJsonValue files;
            if (!TryGetString(document.Value, "schema", out actualSchema) || !TryGetString(document.Value, "candidateId", out actualCandidateId) || !TryGetString(document.Value, "sourceManifestSha256", out sourceHash) || !TryGetString(document.Value, "sourceCapabilitySha256", out sourceCapabilityHash) || !TryGetString(document.Value, "servedRootManifestSha256", out servedRootHash) || !TryGetString(document.Value, "fileSetSha256", out fileSetHash) || !document.Value.TryGetSingleProperty("settings", out settings) || !document.Value.TryGetSingleProperty("files", out files) || settings.Kind != CanonicalJsonKind.Object || files.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Build manifest field type is invalid.");
                return false;
            }

            var expectedSourceCapabilityHash = ComputeCandidateSourceCapabilitySha256(candidateId, sourceCommit, sourceSelfHash, sourceFiles);
            if (actualSchema != EvidenceContracts.BuildManifest ||
                !string.Equals(actualCandidateId, candidateId, StringComparison.Ordinal) ||
                !string.Equals(sourceHash, sourceSelfHash, StringComparison.Ordinal) ||
                !string.Equals(sourceCapabilityHash, expectedSourceCapabilityHash, StringComparison.Ordinal) ||
                !CanonicalJson.IsLowerSha256(servedRootHash) ||
                !CanonicalJson.IsLowerSha256(sourceCapabilityHash) ||
                !CanonicalJson.IsLowerSha256(fileSetHash))
            {
                errors.Add("Build manifest identity or source capability binding is invalid.");
                return false;
            }

            if (!ValidateBuildSettings(settings, candidateScene, errors) || !ValidateBuildFiles(candidateRoot, files, fileSetHash, errors)) return false;
            return ValidateSelfHash(document.Value, "buildManifestSha256", errors, out selfHash);
        }

        private static bool ValidateEvidenceManifest(Document document, string root, string candidateId, string candidateSelfHash, string sourceSelfHash, string buildSelfHash, string sourcePackageLockHash, string candidateScene, CanonicalJsonValue sourceFiles, List<string> errors, out string selfHash, out Dictionary<string, string> resultDocumentHashes)
        {
            selfHash = null;
            resultDocumentHashes = null;
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(document.Value, EvidenceContracts.EvidenceManifest, new[] { "schema", "candidateId", "candidateSha256", "sourceManifestSha256", "buildManifestSha256", "requiredCriterionIds", "artifacts", "generatedUtc", "evidenceManifestSha256" });
            if (!AddResult(shape, "evidence manifest", errors)) return false;
            string actualCandidateId;
            string sourceHash;
            string buildHash;
            string candidateHash;
            string generatedUtc;
            CanonicalJsonValue criteria;
            CanonicalJsonValue artifacts;
            if (!TryGetString(document.Value, "candidateId", out actualCandidateId) || !TryGetString(document.Value, "candidateSha256", out candidateHash) || !TryGetString(document.Value, "sourceManifestSha256", out sourceHash) || !TryGetString(document.Value, "buildManifestSha256", out buildHash) || !TryGetString(document.Value, "generatedUtc", out generatedUtc) || !document.Value.TryGetSingleProperty("requiredCriterionIds", out criteria) || !document.Value.TryGetSingleProperty("artifacts", out artifacts) || criteria.Kind != CanonicalJsonKind.Array || artifacts.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Evidence manifest field type is invalid.");
                return false;
            }
            if (!string.Equals(actualCandidateId, candidateId, StringComparison.Ordinal) || !string.Equals(candidateHash, candidateSelfHash, StringComparison.Ordinal) || !string.Equals(sourceHash, sourceSelfHash, StringComparison.Ordinal) || !string.Equals(buildHash, buildSelfHash, StringComparison.Ordinal) || !IsUtcMilliseconds(generatedUtc))
            {
                errors.Add("Evidence manifest identity binding is invalid.");
                return false;
            }
            var criteriaResult = EvidenceSchemaValidator.ValidateCriteria(criteria.Items, true);
            if (!AddResult(criteriaResult, "evidence criteria", errors)) return false;
            if (!ValidateArtifacts(root, artifacts, candidateId, sourceSelfHash, buildSelfHash, sourcePackageLockHash, candidateScene, sourceFiles, errors, out resultDocumentHashes)) return false;
            return ValidateSelfHash(document.Value, "evidenceManifestSha256", errors, out selfHash);
        }

        private static bool ValidateValidatorReport(Document document, string candidateId, string evidenceSelfHash, List<string> errors, out string selfHash)
        {
            selfHash = null;
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(document.Value, EvidenceContracts.ValidatorReport, new[] { "schema", "candidateId", "evidenceManifestSha256", "checkedCriterionIds", "checks", "status", "generatedUtc", "validatorReportSha256" });
            if (!AddResult(shape, "validator report", errors)) return false;
            string actualCandidateId;
            string evidenceHash;
            string status;
            string generatedUtc;
            CanonicalJsonValue criteria;
            CanonicalJsonValue checks;
            if (!TryGetString(document.Value, "candidateId", out actualCandidateId) || !TryGetString(document.Value, "evidenceManifestSha256", out evidenceHash) || !TryGetString(document.Value, "status", out status) || !TryGetString(document.Value, "generatedUtc", out generatedUtc) || !document.Value.TryGetSingleProperty("checkedCriterionIds", out criteria) || !document.Value.TryGetSingleProperty("checks", out checks) || criteria.Kind != CanonicalJsonKind.Array || checks.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Validator report field type is invalid.");
                return false;
            }
            if (!string.Equals(actualCandidateId, candidateId, StringComparison.Ordinal) || !string.Equals(evidenceHash, evidenceSelfHash, StringComparison.Ordinal) || !IsUtcMilliseconds(generatedUtc))
            {
                errors.Add("Validator report identity or status is invalid.");
                return false;
            }
            if (!AddResult(EvidenceSchemaValidator.ValidateCriteria(criteria.Items, true), "report criteria", errors) || !AddResult(EvidenceSchemaValidator.ValidateReportChecks(checks), "report checks", errors)) return false;
            foreach (var check in checks.Items)
            {
                CanonicalJsonValue checkStatus;
                check.TryGetSingleProperty("status", out checkStatus);
                if (!string.Equals(checkStatus.StringValue, "PASS", StringComparison.Ordinal))
                {
                    errors.Add("Validator report contains a failed check.");
                    return false;
                }
            }
            if (status != "MACHINE_READY")
            {
                errors.Add("Validator report status is not the derived MACHINE_READY result.");
                return false;
            }
            return ValidateSelfHash(document.Value, "validatorReportSha256", errors, out selfHash);
        }

        private static bool ValidateSourceFiles(CanonicalJsonValue files, string expectedTreeHash, string projectRoot, string sourceCommit, List<string> errors)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                errors.Add("Source inventory project root is unavailable.");
                return false;
            }

            var previousPath = null as string;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files.Items)
            {
                var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(file, new[] { "mode", "path", "size", "sha256" });
                if (!AddResult(shape, "source file", errors)) return false;
                string mode;
                string path;
                string hash;
                long size;
                if (!TryGetString(file, "mode", out mode) || !TryGetString(file, "path", out path) || !TryGetString(file, "sha256", out hash) || !TryGetIntegerProperty(file, "size", out size) || (mode != "100644" && mode != "100755" && mode != "120000") || size < 0 || !CanonicalJson.IsLowerSha256(hash) || !CanonicalJson.IsNormalizedRelativePath(path))
                {
                    errors.Add("Source file record is invalid.");
                    return false;
                }
                if (mode == "120000")
                {
                    errors.Add("Source inventory contains an unverifiable symbolic link.");
                    return false;
                }
                if (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, path) > 0)
                {
                    errors.Add("Source file records are not sorted.");
                    return false;
                }
                if (!paths.Add(path))
                {
                    errors.Add("Source file path is duplicated.");
                    return false;
                }
                if (!TryValidateReferencedFile(projectRoot, path, size, hash, errors)) return false;
                previousPath = path;
            }
            if (files.Items.Count == 0 || !string.Equals(CanonicalJson.Sha256Hex(files), expectedTreeHash, StringComparison.Ordinal))
            {
                errors.Add("Source tree hash or materialized file set does not match.");
                return false;
            }
            return ValidateSourceCommitTree(files, sourceCommit, projectRoot, errors);
        }
        private static bool ValidateSourceCommitTree(CanonicalJsonValue files, string sourceCommit, string projectRoot, List<string> errors)
        {
            var declared = new Dictionary<string, SourceTreeEntry>(StringComparer.Ordinal);
            foreach (var file in files.Items)
            {
                string mode;
                string path;
                string sha256;
                if (!TryGetString(file, "mode", out mode) || !TryGetString(file, "path", out path) || !TryGetString(file, "sha256", out sha256))
                {
                    errors.Add("Source manifest cannot be bound to the declared commit tree.");
                    return false;
                }
                declared.Add(path, new SourceTreeEntry(mode, path, sha256));
            }

            try
            {
                var workingTree = RunGitBytes(projectRoot, "status --porcelain=v1 --untracked-files=all");
                if (workingTree.ExitCode != 0 || workingTree.StandardOutput.Length != 0)
                {
                    errors.Add("Source working tree is not the exact sealed snapshot.");
                    return false;
                }

                var result = RunGitBytes(projectRoot, "ls-tree -r -z --full-tree " + sourceCommit);
                if (result.ExitCode != 0 || result.StandardOutput.Length == 0)
                {
                    errors.Add("Source commit tree cannot be enumerated.");
                    return false;
                }

                var expected = new Dictionary<string, SourceTreeEntry>(StringComparer.Ordinal);
                var start = 0;
                while (start < result.StandardOutput.Length)
                {
                    var end = Array.IndexOf(result.StandardOutput, (byte)0, start);
                    if (end <= start)
                    {
                        errors.Add("Source commit tree is malformed.");
                        return false;
                    }

                    var firstSpace = Array.IndexOf(result.StandardOutput, (byte)' ', start, end - start);
                    var secondSpace = firstSpace < 0 ? -1 : Array.IndexOf(result.StandardOutput, (byte)' ', firstSpace + 1, end - firstSpace - 1);
                    var tab = secondSpace < 0 ? -1 : Array.IndexOf(result.StandardOutput, (byte)'\t', secondSpace + 1, end - secondSpace - 1);
                    if (firstSpace <= start || secondSpace <= firstSpace || tab <= secondSpace)
                    {
                        errors.Add("Source commit tree entry is malformed.");
                        return false;
                    }

                    var mode = DecodeGitText(result.StandardOutput, start, firstSpace - start);
                    var type = DecodeGitText(result.StandardOutput, firstSpace + 1, secondSpace - firstSpace - 1);
                    var objectId = DecodeGitText(result.StandardOutput, secondSpace + 1, tab - secondSpace - 1);
                    var path = DecodeGitText(result.StandardOutput, tab + 1, end - tab - 1);
                    if ((mode != "100644" && mode != "100755") || type != "blob" || !IsLowerHex(objectId, 40) || !CanonicalJson.IsNormalizedRelativePath(path))
                    {
                        errors.Add("Source commit tree contains an unsupported entry.");
                        return false;
                    }
                    if (expected.ContainsKey(path))
                    {
                        errors.Add("Source commit tree contains a duplicate path.");
                        return false;
                    }
                    expected.Add(path, new SourceTreeEntry(mode, path, Sha256GitBlob(projectRoot, objectId)));
                    start = end + 1;
                }

                if (declared.Count != expected.Count)
                {
                    errors.Add("Source manifest does not contain the exact sealed commit tree.");
                    return false;
                }
                foreach (var pair in declared)
                {
                    SourceTreeEntry committed;
                    if (!expected.TryGetValue(pair.Key, out committed) ||
                        !string.Equals(pair.Value.Mode, committed.Mode, StringComparison.Ordinal) ||
                        !string.Equals(pair.Value.Sha256, committed.Sha256, StringComparison.Ordinal))
                    {
                        errors.Add("Source manifest entry does not bind the sealed commit tree: " + pair.Key + ".");
                        return false;
                    }
                }
                if (!ValidateMaterializedUnityInputs(projectRoot, declared, errors))
                {
                    return false;
                }
                return true;
            }
            catch (InvalidOperationException exception)
            {
                errors.Add("Source commit tree cannot be verified: " + exception.Message);
                return false;
            }
        }
        private static bool ValidateMaterializedUnityInputs(
            string projectRoot,
            IReadOnlyDictionary<string, SourceTreeEntry> declared,
            List<string> errors)
        {
            try
            {
                var normalizedRoot = Path.GetFullPath(projectRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (HasReparsePointInPath(projectRoot))
                {
                    errors.Add("Materialized Unity input project root contains a reparse point.");
                    return false;
                }
                var roots = new[] { "Assets", "Packages", "ProjectSettings" };
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var inputRoot = Path.Combine(projectRoot, roots[rootIndex]);
                    if (!Directory.Exists(inputRoot))
                    {
                        errors.Add("Required materialized Unity input root is missing: " + roots[rootIndex] + ".");
                        return false;
                    }

                    if ((File.GetAttributes(inputRoot) & FileAttributes.ReparsePoint) != 0)
                    {
                        errors.Add("Required materialized Unity input root is a reparse point: " + roots[rootIndex] + ".");
                        return false;
                    }

                    var pendingDirectories = new Stack<string>();
                    pendingDirectories.Push(inputRoot);
                    while (pendingDirectories.Count > 0)
                    {
                        var directory = pendingDirectories.Pop();
                        var directories = Directory.GetDirectories(directory);
                        for (var index = 0; index < directories.Length; index++)
                        {
                            if ((File.GetAttributes(directories[index]) & FileAttributes.ReparsePoint) != 0)
                            {
                                errors.Add("Materialized Unity input contains a reparse directory.");
                                return false;
                            }

                            pendingDirectories.Push(directories[index]);
                        }

                        var files = Directory.GetFiles(directory);
                        for (var index = 0; index < files.Length; index++)
                        {
                            var fullPath = Path.GetFullPath(files[index]);
                            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                            {
                                errors.Add("Materialized Unity input escapes the project or uses a reparse file.");
                                return false;
                            }

                            var relativePath = fullPath.Substring(normalizedRoot.Length).Replace('\\', '/');
                            if (!declared.ContainsKey(relativePath))
                            {
                                errors.Add("Materialized Unity input is absent from the sealed source tree: " + relativePath + ".");
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                errors.Add("Materialized Unity inputs cannot be enumerated: " + exception.Message);
                return false;
            }
        }


        private static bool ValidateBuildSettings(CanonicalJsonValue settings, string expectedScene, List<string> errors)
        {
            var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(settings, new[] { "development", "autoconnectProfiler", "deepProfiling", "compressionFormat", "decompressionFallback", "exceptionSupport", "memorySizeMb", "target", "scene", "unityVersion" });
            if (!AddResult(shape, "build settings", errors)) return false;
            CanonicalJsonValue development;
            CanonicalJsonValue autoconnect;
            CanonicalJsonValue deepProfiling;
            CanonicalJsonValue fallback;
            string compression;
            string exceptions;
            string target;
            string scene;
            string unityVersion;
            long memory;
            if (!settings.TryGetSingleProperty("development", out development) || !settings.TryGetSingleProperty("autoconnectProfiler", out autoconnect) || !settings.TryGetSingleProperty("deepProfiling", out deepProfiling) || !settings.TryGetSingleProperty("decompressionFallback", out fallback) || !TryGetString(settings, "compressionFormat", out compression) || !TryGetString(settings, "exceptionSupport", out exceptions) || !TryGetString(settings, "target", out target) || !TryGetString(settings, "scene", out scene) || !TryGetString(settings, "unityVersion", out unityVersion) || !TryGetIntegerProperty(settings, "memorySizeMb", out memory) || development.Kind != CanonicalJsonKind.Boolean || autoconnect.Kind != CanonicalJsonKind.Boolean || deepProfiling.Kind != CanonicalJsonKind.Boolean || fallback.Kind != CanonicalJsonKind.Boolean)
            {
                errors.Add("Build settings types are invalid.");
                return false;
            }
            if (!development.BooleanValue || autoconnect.BooleanValue || deepProfiling.BooleanValue || fallback.BooleanValue || compression != "Disabled" || exceptions != "ExplicitlyThrownExceptionsOnly" || memory <= 0 || target != "WebGL" || !string.Equals(scene, expectedScene, StringComparison.Ordinal) || unityVersion != "6000.0.72f1")
            {
                errors.Add("Build settings values or candidate scene binding are invalid.");
                return false;
            }
            return true;
        }

        private static bool ValidateBuildFiles(string candidateRoot, CanonicalJsonValue files, string expectedFileSetHash, List<string> errors)
        {
            var buildRoot = SafePath(candidateRoot, "build");
            if (buildRoot == null || !Directory.Exists(buildRoot))
            {
                errors.Add("Materialized build root is missing.");
                return false;
            }

            var previousPath = null as string;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var hasIndex = false;
            var hasWasm = false;
            var hasData = false;
            foreach (var file in files.Items)
            {
                var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(file, new[] { "path", "size", "sha256" });
                if (!AddResult(shape, "build file", errors)) return false;
                string path;
                string hash;
                long size;
                if (!TryGetString(file, "path", out path) || !TryGetString(file, "sha256", out hash) || !TryGetIntegerProperty(file, "size", out size) || size < 0 || !CanonicalJson.IsLowerSha256(hash) || !CanonicalJson.IsNormalizedRelativePath(path))
                {
                    errors.Add("Build file record is invalid.");
                    return false;
                }
                if (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, path) > 0)
                {
                    errors.Add("Build file records are not sorted.");
                    return false;
                }
                if (!paths.Add(path))
                {
                    errors.Add("Build file path is duplicated.");
                    return false;
                }
                if (!TryValidateReferencedFile(buildRoot, path, size, hash, errors)) return false;
                hasIndex |= string.Equals(path, "index.html", StringComparison.Ordinal);
                hasWasm |= path.EndsWith(".wasm", StringComparison.Ordinal);
                hasData |= path.EndsWith(".data", StringComparison.Ordinal);
                previousPath = path;
            }
            if (!ValidateCompleteBuildInventory(buildRoot, paths, errors)) return false;
            if (files.Items.Count == 0 || !hasIndex || !hasWasm || !hasData || !string.Equals(CanonicalJson.Sha256Hex(files), expectedFileSetHash, StringComparison.Ordinal))
            {
                errors.Add("Build file set hash or required materialized WebGL deliverables are invalid.");
                return false;
            }
            return true;
        }

        private static bool ValidateCompleteBuildInventory(string buildRoot, HashSet<string> declaredPaths, List<string> errors)
        {
            try
            {
                var rootWithSeparator = buildRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? buildRoot
                    : buildRoot + Path.DirectorySeparatorChar;
                var directories = new Stack<string>();
                directories.Push(buildRoot);
                while (directories.Count > 0)
                {
                    var directory = directories.Pop();
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        errors.Add("Materialized build contains a reparse-point directory.");
                        return false;
                    }

                    foreach (var childDirectory in Directory.GetDirectories(directory))
                    {
                        if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                        {
                            errors.Add("Materialized build contains a reparse-point directory.");
                            return false;
                        }

                        directories.Push(childDirectory);
                    }

                    foreach (var fullPath in Directory.GetFiles(directory))
                    {
                        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                        {
                            errors.Add("Materialized build contains a reparse-point file.");
                            return false;
                        }

                        var normalizedPath = Path.GetFullPath(fullPath);
                        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        if (!normalizedPath.StartsWith(rootWithSeparator, comparison))
                        {
                            errors.Add("Build inventory file escapes the confined build root.");
                            return false;
                        }

                        var relativePath = normalizedPath.Substring(rootWithSeparator.Length).Replace('\\', '/');
                        if (relativePath == "build-manifest.json") continue;
                        if (!declaredPaths.Contains(relativePath))
                        {
                            errors.Add("Materialized build contains an unsealed file: " + relativePath + ".");
                            return false;
                        }
                    }
                }
            }
            catch (IOException)
            {
                errors.Add("Materialized build inventory cannot be enumerated.");
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add("Materialized build inventory cannot be enumerated.");
                return false;
            }

            return true;
        }
        private static bool ValidateArtifacts(string root, CanonicalJsonValue artifacts, string candidateId, string sourceSelfHash, string buildSelfHash, string sourcePackageLockHash, string candidateScene, CanonicalJsonValue sourceFiles, List<string> errors, out Dictionary<string, string> resultDocumentHashes)
        {
            resultDocumentHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (artifacts.Items.Count != ExpectedArtifactCount)
            {
                errors.Add("Evidence artifact count is not exactly 66.");
                return false;
            }
            var expected = CreateExpectedArtifactRoles();
            var actual = new Dictionary<string, string>(StringComparer.Ordinal);
            string previousPath = null;
            foreach (var artifact in artifacts.Items)
            {
                var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(artifact, new[] { "path", "role", "size", "sha256", "criterionIds" });
                if (!AddResult(shape, "artifact", errors)) return false;
                string path;
                string role;
                string sha256;
                long size;
                CanonicalJsonValue criteria;
                if (!TryGetString(artifact, "path", out path) || !TryGetString(artifact, "role", out role) || !TryGetString(artifact, "sha256", out sha256) || !TryGetIntegerProperty(artifact, "size", out size) || !artifact.TryGetSingleProperty("criterionIds", out criteria) || criteria.Kind != CanonicalJsonKind.Array || size < 0 || !CanonicalJson.IsLowerSha256(sha256) || !CanonicalJson.IsNormalizedRelativePath(path))
                {
                    errors.Add("Artifact record is invalid.");
                    return false;
                }
                if (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, path) > 0)
                {
                    errors.Add("Artifact paths are not sorted.");
                    return false;
                }
                previousPath = path;
                string expectedRole;
                if (!expected.TryGetValue(path, out expectedRole) || !string.Equals(expectedRole, role, StringComparison.Ordinal) || actual.ContainsKey(path))
                {
                    errors.Add("Artifact path or role is unknown or duplicated.");
                    return false;
                }
                var criteriaResult = EvidenceSchemaValidator.ValidateCriteria(criteria.Items, false);
                if (!AddResult(criteriaResult, "artifact criteria", errors)) return false;
                if (!TryValidateReferencedFile(root, path, size, sha256, errors)) return false;
                actual.Add(path, role);
                if (role == "SOURCE_RESULT" || role == "BUILD_RESULT")
                {
                    string resultDocumentHash;
                    if (!ValidateResultEnvelope(root, path, role, candidateId, sourceSelfHash, buildSelfHash, sourcePackageLockHash, candidateScene, sourceFiles, criteria.Items, errors, out resultDocumentHash)) return false;
                    resultDocumentHashes.Add(path, resultDocumentHash);
                }
                else if (role == "CAPTURE_MANIFEST")
                {
                    if (!ValidateCaptureManifest(root, path, candidateId, buildSelfHash, errors)) return false;
                }
            }
            if (actual.Count != expected.Count || resultDocumentHashes.Count != ExpectedResults.Count)
            {
                errors.Add("Evidence artifact set or fixed result set has missing paths.");
                return false;
            }
            return true;
        }

        private static bool ValidateResultEnvelope(string root, string path, string role, string candidateId, string sourceSelfHash, string buildSelfHash, string sourcePackageLockHash, string candidateScene, CanonicalJsonValue sourceFiles, IReadOnlyList<CanonicalJsonValue> artifactCriteria, List<string> errors, out string documentHash)
        {
            documentHash = null;
            ResultExpectation expectation;
            if (!ExpectedResults.TryGetValue(path, out expectation) || expectation.Role != role)
            {
                errors.Add("Result path is not in the fixed result map.");
                return false;
            }

            Document result;
            if (!TryReadDocument(root, path, errors, out result)) return false;
            documentHash = result.RawSha256;
            var literal = role == "SOURCE_RESULT" ? "overbless.source-result/v1" : "overbless.build-result/v1";
            var required = role == "SOURCE_RESULT"
                ? new[] { "schema", "candidateId", "sourceManifestSha256", "criterionIds", "producer", "producedUtc", "status", "rawArtifact", "payloadType", "payload" }
                : new[] { "schema", "candidateId", "sourceManifestSha256", "buildManifestSha256", "criterionIds", "producer", "producedUtc", "status", "rawArtifacts", "payloadType", "payload" };
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(result.Value, literal, required);
            if (!AddResult(shape, "result envelope", errors)) return false;

            string actualCandidate;
            string sourceHash;
            string producer;
            string producedUtc;
            string status;
            string payloadType;
            CanonicalJsonValue criteria;
            CanonicalJsonValue payload;
            if (!TryGetString(result.Value, "candidateId", out actualCandidate) || !TryGetString(result.Value, "sourceManifestSha256", out sourceHash) || !TryGetString(result.Value, "producer", out producer) || !TryGetString(result.Value, "producedUtc", out producedUtc) || !TryGetString(result.Value, "status", out status) || !TryGetString(result.Value, "payloadType", out payloadType) || !result.Value.TryGetSingleProperty("criterionIds", out criteria) || !result.Value.TryGetSingleProperty("payload", out payload) || criteria.Kind != CanonicalJsonKind.Array || payload.Kind != CanonicalJsonKind.Object || actualCandidate != candidateId || sourceHash != sourceSelfHash || producer != expectation.Producer || payloadType != expectation.PayloadType || !IsUtcMilliseconds(producedUtc))
            {
                errors.Add("Result envelope identity, map, or status is invalid.");
                return false;
            }
            if (!ListsEqual(criteria.Items, artifactCriteria) || !ListsEqual(criteria.Items, expectation.CriterionIds))
            {
                errors.Add("Result envelope criterion IDs do not match the fixed result map.");
                return false;
            }
            if (role == "BUILD_RESULT")
            {
                string buildHash;
                if (!TryGetString(result.Value, "buildManifestSha256", out buildHash) || buildHash != buildSelfHash)
                {
                    errors.Add("Build result manifest binding is invalid.");
                    return false;
                }
            }

            if (!ValidatePayloadShape(payload, expectation, errors)) return false;
            if (role == "SOURCE_RESULT")
            {
                CanonicalJsonValue rawArtifact;
                if (!result.Value.TryGetSingleProperty("rawArtifact", out rawArtifact) || !ValidateRawReference(root, rawArtifact, expectation.RawPaths[0], errors)) return false;
            }
            else
            {
                CanonicalJsonValue rawArtifacts;
                if (!result.Value.TryGetSingleProperty("rawArtifacts", out rawArtifacts) || rawArtifacts.Kind != CanonicalJsonKind.Array || rawArtifacts.Items.Count != expectation.RawPaths.Length)
                {
                    errors.Add("Build result raw artifacts are invalid.");
                    return false;
                }
                string previousPath = null;
                for (var index = 0; index < rawArtifacts.Items.Count; index++)
                {
                    string rawPath;
                    if (!TryGetRawPath(rawArtifacts.Items[index], out rawPath) || (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, rawPath) > 0) || rawPath != expectation.RawPaths[index] || !ValidateRawReference(root, rawArtifacts.Items[index], expectation.RawPaths[index], errors)) return false;
                    previousPath = rawPath;
                }
            }
            if (!ValidatePayloadSemantics(root, path, payload, expectation, candidateId, buildSelfHash, sourcePackageLockHash, candidateScene, sourceFiles, errors)) return false;
            if (status != "PASS")
            {
                errors.Add("Result envelope status is not the semantic PASS result.");
                return false;
            }
            return true;
        }

        private static bool ValidateRawReference(string root, CanonicalJsonValue reference, string expectedPath, List<string> errors)
        {
            var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(reference, new[] { "path", "size", "sha256" });
            if (!AddResult(shape, "raw artifact reference", errors)) return false;
            string path;
            string hash;
            long size;
            if (!TryGetString(reference, "path", out path) || !TryGetString(reference, "sha256", out hash) || !TryGetIntegerProperty(reference, "size", out size) || path != expectedPath || size < 0 || !CanonicalJson.IsLowerSha256(hash))
            {
                errors.Add("Raw artifact reference is invalid.");
                return false;
            }
            return TryValidateReferencedFile(root, path, size, hash, errors);
        }

        private static bool TryGetRawPath(CanonicalJsonValue reference, out string path)
        {
            path = null;
            return TryGetString(reference, "path", out path);
        }
        private static bool ValidatePayloadShape(CanonicalJsonValue payload, ResultExpectation expectation, List<string> errors)
        {
            string[] required;
            switch (expectation.PayloadType)
            {
                case "NUnitSuite":
                    required = new[] { "schema", "suite", "total", "passed", "failed", "skipped", "exitCode", "failureSummary" };
                    break;
                case "ProjectConfigSnapshot":
                    required = new[] { "schema", "unityVersion", "directPackages", "packageLockSha256", "renderer", "input", "addressablesPresent", "scene", "buildSettings", "displayPolicy", "snapshotStatus", "failureCodes" };
                    break;
                case "ScopeAudit":
                    required = new[] { "schema", "scannedRoots", "forbiddenTokens", "allowlist", "matches", "auditStatus" };
                    break;
                case "AudioEvents":
                    required = new[] { "schema", "events" };
                    break;
                case "VisualIdentify":
                    required = new[] { "schema", "testerIds", "resolutions", "observations" };
                    break;
                case "VisualHitDisplay":
                    required = new[] { "schema", "sharedGeometry", "grayscaleDistinct", "sameWorldBounds", "letterboxPass" };
                    break;
                case "Usability":
                    required = new[] { "schema", "testerId", "priorExposure", "startGestureUtc", "understoodAtMilliseconds", "attempts", "completed", "hitExplanation", "nextAction", "consentRef", "noCoaching" };
                    break;
                case "AudioBlind":
                    required = new[] { "schema", "testerId", "priorExposure", "answers", "consentRef" };
                    break;
                case "Browser":
                    required = new[] { "schema", "browser", "version", "profileFresh", "extensionsEnabled", "hardwareAcceleration", "viewportCssWidth", "viewportCssHeight", "zoomPercent", "dpr", "coldLoad", "trustedStart", "audioUnlocked", "timerStartedAfterGesture", "inputs", "focusLossZeroed", "regainGesture", "buildManifestVerifiedBefore", "buildManifestVerifiedAfter", "stuckKeys" };
                    break;
                case "Performance":
                    required = new[] { "schema", "browser", "resolution", "scenario", "warmupSeconds", "sampleSeconds", "bucketOriginMicroseconds", "buckets", "allForeground", "noPause", "status", "longestFrameUs", "p95FrameUs" };
                    break;
                default:
                    errors.Add("Result payload type is unknown.");
                    return false;
            }

            return AddResult(EvidenceSchemaValidator.ValidateSchemaObject(payload, expectation.PayloadSchema, required), "result payload", errors);
        }
        private static bool ValidatePayloadSemantics(string root, string resultPath, CanonicalJsonValue payload, ResultExpectation expectation, string candidateId, string buildSelfHash, string sourcePackageLockHash, string candidateScene, CanonicalJsonValue sourceFiles, List<string> errors)
        {
            switch (expectation.PayloadType)
            {
                case "NUnitSuite":
                    return ValidateNUnitPayload(root, payload, expectation, errors);
                case "ProjectConfigSnapshot":
                    return ValidateProjectConfigPayload(root, payload, expectation.RawPaths[0], sourcePackageLockHash, candidateScene, errors);
                case "ScopeAudit":
                    return ValidateScopeAuditPayload(root, payload, expectation.RawPaths[0], sourceFiles, errors);
                case "AudioEvents":
                    return ValidateAudioEventsPayload(root, payload, expectation.RawPaths[0], errors);
                case "VisualIdentify":
                    return ValidateVisualIdentifyPayload(payload, errors);
                case "VisualHitDisplay":
                    return ValidateVisualHitDisplayPayload(payload, errors);
                case "Usability":
                    return ValidateUsabilityPayload(payload, resultPath, errors);
                case "AudioBlind":
                    return ValidateAudioBlindPayload(root, payload, resultPath, candidateId, buildSelfHash, errors);
                case "Browser":
                    return ValidateBrowserPayload(payload, resultPath, errors);
                case "Performance":
                    return ValidatePerformancePayload(root, payload, resultPath, expectation.RawPaths[0], errors);
                default:
                    errors.Add("Result payload semantic type is unknown.");
                    return false;
            }
        }

        private static bool ValidateNUnitPayload(string root, CanonicalJsonValue payload, ResultExpectation expectation, List<string> errors)
        {
            string suite;
            string failureSummary;
            long total;
            long passed;
            long failed;
            long skipped;
            long exitCode;
            if (!TryGetString(payload, "suite", out suite) || !TryGetString(payload, "failureSummary", out failureSummary) ||
                !TryGetIntegerProperty(payload, "total", out total) || !TryGetIntegerProperty(payload, "passed", out passed) ||
                !TryGetIntegerProperty(payload, "failed", out failed) || !TryGetIntegerProperty(payload, "skipped", out skipped) ||
                !TryGetIntegerProperty(payload, "exitCode", out exitCode) || !string.Equals(suite, expectation.NUnitSuite, StringComparison.Ordinal) ||
                total <= 0 || passed < 0 || failed < 0 || skipped < 0 || passed > total || failed > total - passed || skipped != total - passed - failed ||
                exitCode != 0 || failed != 0 || !string.IsNullOrEmpty(failureSummary))
            {
                errors.Add("NUnit payload does not derive a passing test result.");
                return false;
            }

            long rawTotal;
            long rawPassed;
            long rawFailed;
            long rawSkipped;
            if (!TryReadNUnitCounts(root, expectation.RawPaths[0], expectation, errors, out rawTotal, out rawPassed, out rawFailed, out rawSkipped))
            {
                return false;
            }
            if (total != rawTotal || passed != rawPassed || failed != rawFailed || skipped != rawSkipped)
            {
                errors.Add("NUnit payload does not match the raw test result.");
                return false;
            }
            return true;
        }

        private static bool ValidateProjectConfigPayload(string root, CanonicalJsonValue payload, string rawPath, string sourcePackageLockHash, string candidateScene, List<string> errors)
        {
            if (!ValidateCanonicalRawPayload(root, rawPath, payload, errors)) return false;

            string snapshotStatus;
            string packageLockHash;
            string renderer;
            string input;
            string scene;
            string unityVersion;
            CanonicalJsonValue addressables;
            CanonicalJsonValue failureCodes;
            CanonicalJsonValue buildSettings;
            CanonicalJsonValue displayPolicy;
            if (!TryGetString(payload, "snapshotStatus", out snapshotStatus) || !TryGetString(payload, "packageLockSha256", out packageLockHash) ||
                !TryGetString(payload, "renderer", out renderer) || !TryGetString(payload, "input", out input) ||
                !TryGetString(payload, "scene", out scene) || !TryGetString(payload, "unityVersion", out unityVersion) || !payload.TryGetSingleProperty("addressablesPresent", out addressables) ||
                !payload.TryGetSingleProperty("failureCodes", out failureCodes) || !payload.TryGetSingleProperty("buildSettings", out buildSettings) ||
                !payload.TryGetSingleProperty("displayPolicy", out displayPolicy) || addressables.Kind != CanonicalJsonKind.Boolean ||
                failureCodes.Kind != CanonicalJsonKind.Array || buildSettings.Kind != CanonicalJsonKind.Object || displayPolicy.Kind != CanonicalJsonKind.Object ||
                snapshotStatus != "PASS" || packageLockHash != sourcePackageLockHash || renderer != "URP2D" || input != "InputSystem" ||
                unityVersion != "6000.0.72f1" || addressables.BooleanValue || failureCodes.Items.Count != 0 || scene != candidateScene)
            {
                errors.Add("Project configuration payload does not derive a passing source configuration.");
                return false;
            }

            if (!ValidateProjectBuildSettings(buildSettings, candidateScene, errors) || !ValidateDisplayPolicy(displayPolicy, errors))
            {
                return false;
            }

            CanonicalJsonValue packages;
            if (!payload.TryGetSingleProperty("directPackages", out packages) || packages.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Project configuration package inventory is invalid.");
                return false;
            }
            foreach (var package in packages.Items)
            {
                string name;
                if (!TryGetString(package, "name", out name) || string.Equals(name, "com.unity.addressables", StringComparison.Ordinal))
                {
                    errors.Add("Project configuration reports Addressables in its package inventory.");
                    return false;
                }
            }
            return true;
        }

        private static bool ValidateProjectBuildSettings(CanonicalJsonValue settings, string candidateScene, List<string> errors)
        {
            CanonicalJsonValue development;
            CanonicalJsonValue autoconnect;
            CanonicalJsonValue deepProfiling;
            CanonicalJsonValue fallback;
            CanonicalJsonValue scenes;
            string compression;
            string exceptions;
            string target;
            long memory;
            if (!settings.TryGetSingleProperty("development", out development) || !settings.TryGetSingleProperty("autoconnectProfiler", out autoconnect) ||
                !settings.TryGetSingleProperty("deepProfiling", out deepProfiling) || !settings.TryGetSingleProperty("decompressionFallback", out fallback) ||
                !settings.TryGetSingleProperty("scenes", out scenes) || !TryGetString(settings, "compressionFormat", out compression) ||
                !TryGetString(settings, "exceptionSupport", out exceptions) || !TryGetString(settings, "target", out target) ||
                !TryGetIntegerProperty(settings, "memorySizeMb", out memory) || development.Kind != CanonicalJsonKind.Boolean ||
                autoconnect.Kind != CanonicalJsonKind.Boolean || deepProfiling.Kind != CanonicalJsonKind.Boolean ||
                fallback.Kind != CanonicalJsonKind.Boolean || scenes.Kind != CanonicalJsonKind.Array || scenes.Items.Count != 1 ||
                scenes.Items[0].Kind != CanonicalJsonKind.String || scenes.Items[0].StringValue != candidateScene ||
                !development.BooleanValue || autoconnect.BooleanValue || deepProfiling.BooleanValue || fallback.BooleanValue ||
                compression != "Disabled" || exceptions != "ExplicitlyThrownExceptionsOnly" || target != "WebGL" || memory <= 0)
            {
                errors.Add("Project configuration build settings are not the required observed WebGL settings.");
                return false;
            }
            return true;
        }

        private static bool ValidateDisplayPolicy(CanonicalJsonValue displayPolicy, List<string> errors)
        {
            CanonicalJsonValue letterbox;
            CanonicalJsonValue sameWorldBounds;
            string canvasScaleMode;
            long numerator;
            long denominator;
            long designWidth;
            long designHeight;
            long minimumWidth;
            long minimumHeight;
            if (!displayPolicy.TryGetSingleProperty("letterboxNon16x9", out letterbox) ||
                !displayPolicy.TryGetSingleProperty("sameWorldBounds", out sameWorldBounds) ||
                !TryGetString(displayPolicy, "canvasScaleMode", out canvasScaleMode) ||
                !TryGetIntegerProperty(displayPolicy, "aspectNumerator", out numerator) ||
                !TryGetIntegerProperty(displayPolicy, "aspectDenominator", out denominator) ||
                !TryGetIntegerProperty(displayPolicy, "designWidth", out designWidth) ||
                !TryGetIntegerProperty(displayPolicy, "designHeight", out designHeight) ||
                !TryGetIntegerProperty(displayPolicy, "minimumWidth", out minimumWidth) ||
                !TryGetIntegerProperty(displayPolicy, "minimumHeight", out minimumHeight) ||
                letterbox.Kind != CanonicalJsonKind.Boolean || sameWorldBounds.Kind != CanonicalJsonKind.Boolean ||
                !letterbox.BooleanValue || !sameWorldBounds.BooleanValue || canvasScaleMode != "ScaleWithScreenSize" ||
                numerator != 16 || denominator != 9 || designWidth != 1920 || designHeight != 1080 ||
                minimumWidth != 1280 || minimumHeight != 720)
            {
                errors.Add("Project configuration display policy is not observed as the required 16:9 policy.");
                return false;
            }
            return true;
        }

        private static bool ValidateScopeAuditPayload(string root, CanonicalJsonValue payload, string rawPath, CanonicalJsonValue sourceFiles, List<string> errors)
        {
            if (!ValidateCanonicalRawPayload(root, rawPath, payload, errors)) return false;

            string status;
            CanonicalJsonValue scannedRoots;
            CanonicalJsonValue forbiddenTokens;
            CanonicalJsonValue matches;
            CanonicalJsonValue allowlist;
            if (!TryGetString(payload, "auditStatus", out status) || !payload.TryGetSingleProperty("scannedRoots", out scannedRoots) ||
                !payload.TryGetSingleProperty("forbiddenTokens", out forbiddenTokens) || !payload.TryGetSingleProperty("matches", out matches) ||
                !payload.TryGetSingleProperty("allowlist", out allowlist) || status != "PASS" || scannedRoots.Kind != CanonicalJsonKind.Array ||
                forbiddenTokens.Kind != CanonicalJsonKind.Array || matches.Kind != CanonicalJsonKind.Array || allowlist.Kind != CanonicalJsonKind.Array ||
                !StringArrayEquals(scannedRoots, ExpectedScopeRoots) || !StringArrayEquals(forbiddenTokens, ScopeAudit.ForbiddenGameplayTokens))
            {
                errors.Add("Scope audit payload does not bind the required M0/M1 audit contract.");
                return false;
            }

            var projectRoot = FindProjectRoot(root);
            if (string.IsNullOrEmpty(projectRoot))
            {
                errors.Add("Scope audit project root is unavailable.");
                return false;
            }

            var sources = new Dictionary<string, SourceTreeEntry>(StringComparer.Ordinal);
            foreach (var file in sourceFiles.Items)
            {
                string path;
                string sha256;
                if (!TryGetString(file, "path", out path) || !TryGetString(file, "sha256", out sha256) || sources.ContainsKey(path))
                {
                    errors.Add("Scope audit cannot bind a sealed source path.");
                    return false;
                }
                sources.Add(path, new SourceTreeEntry(null, path, sha256));
            }

            var allowances = new Dictionary<string, ScopeAllowance>(StringComparer.Ordinal);
            ScopeAllowance previousAllowance = null;
            if (allowlist.Items.Count != 0)
            {
                errors.Add("Scope audit allowances require a separately anchored user-owned approval ledger; this candidate must use an empty allowlist.");
                return false;
            }

            foreach (var allowance in allowlist.Items)
            {
                var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(allowance, new[] { "approvalReference", "approvalSha256", "column", "line", "path", "sourceSha256", "token" });
                if (!AddResult(shape, "scope audit allowance", errors)) return false;

                string path;
                string token;
                string sourceSha256;
                string approvalReference;
                string approvalSha256;
                long line;
                long column;
                SourceTreeEntry source;
                if (!TryGetString(allowance, "path", out path) || !TryGetString(allowance, "token", out token) ||
                    !TryGetString(allowance, "sourceSha256", out sourceSha256) || !TryGetString(allowance, "approvalReference", out approvalReference) ||
                    !TryGetString(allowance, "approvalSha256", out approvalSha256) || !TryGetIntegerProperty(allowance, "line", out line) ||
                    !TryGetIntegerProperty(allowance, "column", out column) || line <= 0 || column <= 0 ||
                    !CanonicalJson.IsNormalizedRelativePath(path) || !CanonicalJson.IsNormalizedRelativePath(approvalReference) ||
                    !CanonicalJson.IsLowerSha256(sourceSha256) || !CanonicalJson.IsLowerSha256(approvalSha256) ||
                    !ContainsScopeToken(token) || !IsGovernedScopePath(path) || !sources.TryGetValue(path, out source) ||
                    !string.Equals(source.Sha256, sourceSha256, StringComparison.Ordinal) ||
                    !TryValidateScopeApproval(projectRoot, sources, approvalReference, approvalSha256, errors))
                {
                    errors.Add("Scope audit allowance does not bind a sealed source exception.");
                    return false;
                }

                var identity = ScopeIdentity(path, token, sourceSha256, line, column);
                if (allowances.ContainsKey(identity))
                {
                    errors.Add("Scope audit allowance duplicates a forbidden token occurrence.");
                    return false;
                }
                var record = new ScopeAllowance(path, token, sourceSha256, line, column, approvalReference);
                if (previousAllowance != null && CompareScopeAllowances(previousAllowance, record) >= 0)
                {
                    errors.Add("Scope audit allowances are not canonically sorted.");
                    return false;
                }
                allowances.Add(identity, record);
                previousAllowance = record;
            }

            var derivedMatches = new List<ScopeMatch>();
            foreach (var source in sources.Values)
            {
                if (!IsGovernedScopePath(source.Path)) continue;

                string text;
                if (!TryReadSealedSourceText(projectRoot, source, errors, out text)) return false;
                foreach (var token in ScopeAudit.ForbiddenGameplayTokens)
                {
                    var expression = new Regex("\\b" + Regex.Escape(token) + "\\b", RegexOptions.CultureInvariant);
                    foreach (Match match in expression.Matches(text))
                    {
                        long line;
                        long column;
                        GetScopeLineAndColumn(text, match.Index, out line, out column);
                        derivedMatches.Add(new ScopeMatch(source.Path, token, source.Sha256, line, column));
                    }
                }
            }
            derivedMatches.Sort(CompareScopeMatches);

            foreach (var allowance in allowances)
            {
                if (!ContainsScopeMatch(derivedMatches, allowance.Key))
                {
                    errors.Add("Scope audit allowance has no exact forbidden-token occurrence.");
                    return false;
                }
            }
            foreach (var derived in derivedMatches)
            {
                if (!allowances.ContainsKey(derived.Identity))
                {
                    errors.Add("Scope audit has an ungoverned forbidden-token match.");
                    return false;
                }
            }
            if (matches.Items.Count != derivedMatches.Count)
            {
                errors.Add("Scope audit does not report the complete sealed-source match set.");
                return false;
            }

            for (var index = 0; index < matches.Items.Count; index++)
            {
                var match = matches.Items[index];
                var derived = derivedMatches[index];
                string path;
                string token;
                string sourceSha256;
                string approvalReference;
                long line;
                long column;
                CanonicalJsonValue allowlisted;
                ScopeAllowance allowance;
                if (!TryGetString(match, "path", out path) || !TryGetString(match, "token", out token) ||
                    !TryGetString(match, "sourceSha256", out sourceSha256) || !TryGetString(match, "approvalReference", out approvalReference) ||
                    !TryGetIntegerProperty(match, "line", out line) || !TryGetIntegerProperty(match, "column", out column) ||
                    !match.TryGetSingleProperty("allowlisted", out allowlisted) || allowlisted.Kind != CanonicalJsonKind.Boolean ||
                    !allowlisted.BooleanValue || !allowances.TryGetValue(derived.Identity, out allowance) ||
                    path != derived.Path || token != derived.Token || sourceSha256 != derived.SourceSha256 ||
                    line != derived.Line || column != derived.Column || approvalReference != allowance.ApprovalReference)
                {
                    errors.Add("Scope audit match does not rederive from sealed source bytes.");
                    return false;
                }
            }
            return true;
        }
        private static bool ContainsScopeToken(string token)
        {
            foreach (var expected in ScopeAudit.ForbiddenGameplayTokens)
            {
                if (string.Equals(token, expected, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsGovernedScopePath(string path)
        {
            var inRoot = false;
            foreach (var root in ExpectedScopeRoots)
            {
                if (path.StartsWith(root + "/", StringComparison.Ordinal))
                {
                    inRoot = true;
                    break;
                }
            }
            if (!inRoot) return false;

            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryValidateScopeApproval(string projectRoot, IReadOnlyDictionary<string, SourceTreeEntry> sources, string approvalReference, string approvalSha256, List<string> errors)
        {
            SourceTreeEntry approval;
            if (!sources.TryGetValue(approvalReference, out approval) ||
                !string.Equals(approval.Sha256, approvalSha256, StringComparison.Ordinal))
            {
                return false;
            }

            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(projectRoot, approvalReference, "Scope approval", errors, out snapshot)) return false;
            return string.Equals(snapshot.Sha256, approvalSha256, StringComparison.Ordinal);
        }

        private static bool TryReadSealedSourceText(string projectRoot, SourceTreeEntry source, List<string> errors, out string text)
        {
            text = null;
            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(projectRoot, source.Path, "Scope audit sealed source", errors, out snapshot)) return false;
            if (!string.Equals(snapshot.Sha256, source.Sha256, StringComparison.Ordinal))
            {
                errors.Add("Scope audit source bytes do not match the sealed source manifest: " + source.Path + ".");
                return false;
            }

            try
            {
                text = new UTF8Encoding(false, true).GetString(snapshot.Bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                errors.Add("Scope audit sealed source is not valid UTF-8: " + source.Path + ".");
                return false;
            }
        }

        private static string ScopeIdentity(string path, string token, string sourceSha256, long line, long column)
        {
            return path + "\n" + token + "\n" + sourceSha256 + "\n" + line.ToString(CultureInfo.InvariantCulture) + "\n" + column.ToString(CultureInfo.InvariantCulture);
        }

        private static void GetScopeLineAndColumn(string text, int index, out long line, out long column)
        {
            line = 1;
            column = 1;
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
        }

        private static int CompareScopeMatches(ScopeMatch left, ScopeMatch right)
        {
            var path = CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path);
            if (path != 0) return path;
            var line = left.Line.CompareTo(right.Line);
            if (line != 0) return line;
            var column = left.Column.CompareTo(right.Column);
            if (column != 0) return column;
            return CanonicalJson.CompareUtf8Ordinal(left.Token, right.Token);
        }
        private static int CompareScopeAllowances(ScopeAllowance left, ScopeAllowance right)
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

        private static bool ContainsScopeMatch(IEnumerable<ScopeMatch> matches, string identity)
        {
            foreach (var match in matches)
            {
                if (string.Equals(match.Identity, identity, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool ValidateAudioEventsPayload(string root, CanonicalJsonValue payload, string rawPath, List<string> errors)
        {
            if (!ValidateCanonicalRawPayload(root, rawPath, payload, errors)) return false;

            CanonicalJsonValue events;
            if (!payload.TryGetSingleProperty("events", out events) || events.Kind != CanonicalJsonKind.Array || events.Items.Count != 3)
            {
                errors.Add("Audio event payload is invalid.");
                return false;
            }

            var eventNames = new HashSet<string>(StringComparer.Ordinal);
            var tokens = new HashSet<long>();
            long previousToken = 0;
            foreach (var item in events.Items)
            {
                string eventName;
                long token;
                if (!TryGetString(item, "event", out eventName) || !TryGetIntegerProperty(item, "token", out token) ||
                    token <= previousToken || !eventNames.Add(eventName) || !tokens.Add(token))
                {
                    errors.Add("Audio event payload does not contain one unique record for each event.");
                    return false;
                }
                previousToken = token;
            }
            if (!eventNames.SetEquals(new[] { "ArcherReady", "DasherReady", "ExitOpened" }))
            {
                errors.Add("Audio event payload is missing a required event.");
                return false;
            }
            return true;
        }
        private static bool ValidateCanonicalRawPayload(string root, string rawPath, CanonicalJsonValue payload, List<string> errors)
        {
            Document raw;
            if (!TryReadDocument(root, rawPath, errors, out raw)) return false;
            if (!string.Equals(CanonicalJson.Serialize(raw.Value), CanonicalJson.Serialize(payload), StringComparison.Ordinal))
            {
                errors.Add("Structured payload does not match its canonical raw artifact.");
                return false;
            }
            return true;
        }

        private static bool TryReadNUnitCounts(string root, string rawPath, ResultExpectation expectation, List<string> errors, out long total, out long passed, out long failed, out long skipped)
        {
            total = 0;
            passed = 0;
            failed = 0;
            skipped = 0;
            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(root, rawPath, "NUnit raw artifact", errors, out snapshot)) return false;

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var stream = new MemoryStream(snapshot.Bytes, false))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    var document = XDocument.Load(reader, LoadOptions.None);
                    var testRun = document.Root;
                    if (testRun == null ||
                        !string.Equals(testRun.Name.LocalName, "test-run", StringComparison.Ordinal) ||
                        !XmlAttributeEquals(testRun, "result", "Passed") ||
                        !TryGetXmlCount(testRun, "total", out total) ||
                        !TryGetXmlCount(testRun, "passed", out passed) ||
                        !TryGetXmlCount(testRun, "failed", out failed) ||
                        !TryGetXmlCount(testRun, "skipped", out skipped) ||
                        total != passed + failed + skipped ||
                        !ValidateNoFailedNUnitNodes(testRun, errors) ||
                        !ValidateNUnitSuite(testRun, expectation, errors))
                    {
                        errors.Add("NUnit raw artifact has invalid aggregate counts.");
                        return false;
                    }
                }
            }
            catch (XmlException)
            {
                errors.Add("NUnit raw artifact is invalid XML.");
                return false;
            }

            return true;
        }
        private static bool ValidateNUnitSuite(XElement testRun, ResultExpectation expectation, List<string> errors)
        {
            XElement suite = null;
            foreach (var candidate in testRun.Descendants())
            {
                var fullName = candidate.Attribute("fullname");
                if (string.Equals(candidate.Name.LocalName, "test-suite", StringComparison.Ordinal) && fullName != null &&
                    string.Equals(fullName.Value, expectation.NUnitSuite, StringComparison.Ordinal))
                {
                    if (suite != null)
                    {
                        errors.Add("NUnit raw artifact contains the required suite more than once.");
                        return false;
                    }
                    suite = candidate;
                }
            }
            if (suite == null || !XmlAttributeEquals(suite, "type", "TestSuite") || !XmlAttributeEquals(suite, "result", "Passed"))
            {
                errors.Add("NUnit raw artifact does not contain the required passing suite.");
                return false;
            }

            var expected = new HashSet<string>(expectation.NUnitTestFullNames, StringComparer.Ordinal);
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var testCase in suite.Descendants())
            {
                if (!string.Equals(testCase.Name.LocalName, "test-case", StringComparison.Ordinal)) continue;

                var fullName = testCase.Attribute("fullname");
                if (fullName == null || !expected.Contains(fullName.Value)) continue;
                if (!XmlAttributeEquals(testCase, "result", "Passed") || !found.Add(fullName.Value))
                {
                    errors.Add("NUnit required test case did not pass exactly once.");
                    return false;
                }
            }
            if (!found.SetEquals(expected))
            {
                errors.Add("NUnit raw artifact is missing a required test case.");
                return false;
            }
            return true;
        }
        private static bool ValidateNoFailedNUnitNodes(XElement testRun, List<string> errors)
        {
            foreach (var element in testRun.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "test-suite", StringComparison.Ordinal) &&
                    !string.Equals(element.Name.LocalName, "test-case", StringComparison.Ordinal))
                {
                    continue;
                }

                var result = element.Attribute("result");
                if (result != null &&
                    (string.Equals(result.Value, "Failed", StringComparison.Ordinal) ||
                     string.Equals(result.Value, "Error", StringComparison.Ordinal)))
                {
                    errors.Add("NUnit raw artifact contains a failed suite or test case.");
                    return false;
                }
            }

            return true;
        }

        private static bool XmlAttributeEquals(XElement element, string name, string expected)
        {
            var attribute = element.Attribute(name);
            return attribute != null && string.Equals(attribute.Value, expected, StringComparison.Ordinal);
        }

        private static bool TryGetXmlCount(XElement element, string name, out long value)
        {
            value = 0;
            var attribute = element.Attribute(name);
            return attribute != null && long.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static bool ValidateVisualIdentifyPayload(CanonicalJsonValue payload, List<string> errors)
        {
            CanonicalJsonValue testerIds;
            CanonicalJsonValue resolutions;
            CanonicalJsonValue observations;
            if (!payload.TryGetSingleProperty("testerIds", out testerIds) || !payload.TryGetSingleProperty("resolutions", out resolutions) ||
                !payload.TryGetSingleProperty("observations", out observations) || testerIds.Kind != CanonicalJsonKind.Array ||
                resolutions.Kind != CanonicalJsonKind.Array || observations.Kind != CanonicalJsonKind.Array ||
                !StringArrayEquals(testerIds, new[] { "tester-01", "tester-02", "tester-03" }) ||
                !StringArrayEquals(resolutions, new[] { "1280x720", "1920x1080" }) || observations.Items.Count != 6)
            {
                errors.Add("Visual identification payload does not cover the fixed tester-resolution matrix.");
                return false;
            }

            var pairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var observation in observations.Items)
            {
                string testerId;
                string resolution;
                CanonicalJsonValue identified;
                if (!TryGetString(observation, "testerId", out testerId) || !TryGetString(observation, "resolution", out resolution) ||
                    !observation.TryGetSingleProperty("identified", out identified) || identified.Kind != CanonicalJsonKind.Boolean ||
                    !identified.BooleanValue || !pairs.Add(testerId + "\n" + resolution))
                {
                    errors.Add("Visual identification observations are invalid.");
                    return false;
                }
            }
            foreach (var testerId in new[] { "tester-01", "tester-02", "tester-03" })
            {
                foreach (var resolution in new[] { "1280x720", "1920x1080" })
                {
                    if (!pairs.Contains(testerId + "\n" + resolution))
                    {
                        errors.Add("Visual identification matrix is incomplete.");
                        return false;
                    }
                }
            }
            return true;
        }
        private static bool ValidateVisualHitDisplayPayload(CanonicalJsonValue payload, List<string> errors)
        {
            if (!BooleanPropertyEquals(payload, "grayscaleDistinct", true) ||
                !BooleanPropertyEquals(payload, "letterboxPass", true) ||
                !BooleanPropertyEquals(payload, "sameWorldBounds", true) ||
                !BooleanPropertyEquals(payload, "sharedGeometry", true))
            {
                errors.Add("Visual hit-display payload does not derive all required display checks.");
                return false;
            }
            return true;
        }

        private static bool ValidateUsabilityPayload(CanonicalJsonValue payload, string resultPath, List<string> errors)
        {
            if (!ValidateTesterPayload(payload, resultPath, "usability", errors) ||
                !BooleanPropertyEquals(payload, "completed", true) ||
                !BooleanPropertyEquals(payload, "noCoaching", true) ||
                !BooleanPropertyEquals(payload, "priorExposure", false))
            {
                errors.Add("Usability payload does not derive the required independent completion.");
                return false;
            }
            return true;
        }

        private static bool ValidateTesterPayload(CanonicalJsonValue payload, string resultPath, string directory, List<string> errors)
        {
            string expectedTester;
            string actualTester;
            if (!TryGetExpectedTesterId(resultPath, directory, out expectedTester) || !TryGetString(payload, "testerId", out actualTester) ||
                !string.Equals(actualTester, expectedTester, StringComparison.Ordinal))
            {
                errors.Add("Tester payload identity does not bind to its fixed evidence path.");
                return false;
            }
            return true;
        }

        private static bool ValidateAudioBlindPayload(string root, CanonicalJsonValue payload, string resultPath, string candidateId, string buildSelfHash, List<string> errors)
        {
            if (!ValidateTesterPayload(payload, resultPath, "audio-blind", errors)) return false;
            if (!BooleanPropertyEquals(payload, "priorExposure", false))
            {
                errors.Add("Audio-blind payload does not preserve the required prior-exposure control.");
                return false;
            }

            string testerId;
            CanonicalJsonValue answers;
            if (!TryGetString(payload, "testerId", out testerId) || !payload.TryGetSingleProperty("answers", out answers) || answers.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Audio-blind payload is invalid.");
                return false;
            }

            Document randomization;
            if (!TryReadDocument(root, "audio-blind/randomization.raw.json", errors, out randomization)) return false;
            var randomizationResult = EvidenceSchemaValidator.ValidateAudioRandomization(
                randomization.Value,
                candidateId,
                buildSelfHash,
                new[] { "tester-01", "tester-02", "tester-03" });
            if (!AddResult(randomizationResult, "audio randomization", errors)) return false;

            CanonicalJsonValue orders;
            if (!randomization.Value.TryGetSingleProperty("orders", out orders) || orders.Kind != CanonicalJsonKind.Array)
            {
                errors.Add("Audio randomization orders are unavailable.");
                return false;
            }
            foreach (var order in orders.Items)
            {
                string orderTester;
                CanonicalJsonValue eventOrder;
                if (!TryGetString(order, "testerId", out orderTester) || !order.TryGetSingleProperty("eventOrder", out eventOrder)) continue;
                if (orderTester == testerId)
                {
                    if (StringArraysEqual(answers, eventOrder)) return true;
                    errors.Add("Audio-blind answers do not match the tester's randomized event order.");
                    return false;
                }
            }

            errors.Add("Audio-blind tester is absent from the randomization.");
            return false;
        }

        private static bool ValidateBrowserPayload(CanonicalJsonValue payload, string resultPath, List<string> errors)
        {
            const string prefix = "browser/";
            const string suffix = ".result.json";
            if (!resultPath.StartsWith(prefix, StringComparison.Ordinal) || !resultPath.EndsWith(suffix, StringComparison.Ordinal))
            {
                errors.Add("Browser payload path is invalid.");
                return false;
            }
            var cell = resultPath.Substring(prefix.Length, resultPath.Length - prefix.Length - suffix.Length).Split('/');
            if (cell.Length != 2 || (cell[0] != "chrome" && cell[0] != "edge") || (cell[1] != "1280x720" && cell[1] != "1920x1080"))
            {
                errors.Add("Browser payload matrix cell is invalid.");
                return false;
            }

            string browser;
            CanonicalJsonValue dpr;
            long zoomPercent;
            var expectedBrowser = cell[0] == "chrome" ? "Chrome" : "Edge";
            var expectedWidth = cell[1] == "1280x720" ? 1280L : 1920L;
            var expectedHeight = cell[1] == "1280x720" ? 720L : 1080L;
            long viewportWidth;
            long viewportHeight;
            if (!TryGetString(payload, "browser", out browser) || browser != expectedBrowser ||
                !payload.TryGetSingleProperty("dpr", out dpr) || dpr.Kind != CanonicalJsonKind.Number || dpr.NumberValue <= 0d ||
                !TryGetIntegerProperty(payload, "zoomPercent", out zoomPercent) || zoomPercent != 100 ||
                !TryGetIntegerProperty(payload, "viewportCssWidth", out viewportWidth) || viewportWidth != expectedWidth ||
                !TryGetIntegerProperty(payload, "viewportCssHeight", out viewportHeight) || viewportHeight != expectedHeight ||
                !BooleanPropertyEquals(payload, "audioUnlocked", true) ||
                !BooleanPropertyEquals(payload, "buildManifestVerifiedAfter", true) ||
                !BooleanPropertyEquals(payload, "buildManifestVerifiedBefore", true) ||
                !BooleanPropertyEquals(payload, "coldLoad", true) ||
                !BooleanPropertyEquals(payload, "extensionsEnabled", false) ||
                !BooleanPropertyEquals(payload, "focusLossZeroed", true) ||
                !BooleanPropertyEquals(payload, "hardwareAcceleration", true) ||
                !BooleanPropertyEquals(payload, "profileFresh", true) ||
                !BooleanPropertyEquals(payload, "regainGesture", true) ||
                !BooleanPropertyEquals(payload, "stuckKeys", false) ||
                !BooleanPropertyEquals(payload, "timerStartedAfterGesture", true) ||
                !BooleanPropertyEquals(payload, "trustedStart", true))
            {
                errors.Add("Browser payload does not derive a passing browser matrix cell.");
                return false;
            }
            return true;
        }

        private static bool ValidatePerformancePayload(string root, CanonicalJsonValue payload, string resultPath, string rawPath, List<string> errors)
        {
            string browser;
            string resolution;
            string scenario;
            if (!TryParsePerformanceCell(resultPath, out browser, out resolution, out scenario))
            {
                errors.Add("Performance result path is invalid.");
                return false;
            }

            List<FrameCompletionRecord> records;
            if (!TryReadPerformanceRecords(root, rawPath, errors, out records)) return false;
            var result = EvidenceSchemaValidator.ValidatePerformance(payload, records, browser, resolution, scenario);
            return AddResult(result, "performance payload", errors);
        }

        private static bool TryParsePerformanceCell(string resultPath, out string browser, out string resolution, out string scenario)
        {
            browser = null;
            resolution = null;
            scenario = null;
            const string prefix = "performance/";
            const string suffix = ".result.json";
            if (!resultPath.StartsWith(prefix, StringComparison.Ordinal) || !resultPath.EndsWith(suffix, StringComparison.Ordinal)) return false;
            var values = resultPath.Substring(prefix.Length, resultPath.Length - prefix.Length - suffix.Length).Split('-');
            if (values.Length != 3 || (values[0] != "chrome" && values[0] != "edge") ||
                (values[1] != "1280x720" && values[1] != "1920x1080") || (values[2] != "baseline" && values[2] != "stress"))
            {
                return false;
            }
            browser = values[0] == "chrome" ? "Chrome" : "Edge";
            resolution = values[1];
            scenario = values[2];
            return true;
        }

        private static bool TryReadPerformanceRecords(string root, string rawPath, List<string> errors, out List<FrameCompletionRecord> records)
        {
            records = null;
            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(root, rawPath, "Performance raw CSV", errors, out snapshot)) return false;

            string text;
            try
            {
                text = new System.Text.UTF8Encoding(false, true).GetString(snapshot.Bytes);
            }
            catch (System.Text.DecoderFallbackException)
            {
                errors.Add("Performance raw CSV is not UTF-8.");
                return false;
            }

            const string header = "completedAtMicroseconds,durationMicroseconds,foreground,unpaused";
            if (text.IndexOf('\r') >= 0 || !text.EndsWith("\n", StringComparison.Ordinal))
            {
                errors.Add("Performance raw CSV must use LF and end with one LF.");
                return false;
            }
            var lines = text.Split('\n');
            if (lines.Length < 3 || lines[0] != header || lines[lines.Length - 1].Length != 0)
            {
                errors.Add("Performance raw CSV header or rows are invalid.");
                return false;
            }

            records = new List<FrameCompletionRecord>();
            for (var index = 1; index < lines.Length - 1; index++)
            {
                var cells = lines[index].Split(',');
                long completedAt;
                long duration;
                bool foreground;
                bool unpaused;
                if (cells.Length != 4 || !long.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out completedAt) ||
                    !long.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out duration) || completedAt < 0 || duration < 0 ||
                    !TryParseCsvBoolean(cells[2], out foreground) || !TryParseCsvBoolean(cells[3], out unpaused))
                {
                    errors.Add("Performance raw CSV contains an invalid record.");
                    return false;
                }
                records.Add(new FrameCompletionRecord(completedAt, duration, foreground, unpaused));
            }
            return records.Count > 0;
        }

        private static bool TryParseCsvBoolean(string value, out bool result)
        {
            if (value == "true")
            {
                result = true;
                return true;
            }
            if (value == "false")
            {
                result = false;
                return true;
            }
            result = false;
            return false;
        }

        private static bool TryGetExpectedTesterId(string resultPath, string directory, out string testerId)
        {
            testerId = null;
            var prefix = directory + "/";
            const string suffix = ".result.json";
            if (!resultPath.StartsWith(prefix, StringComparison.Ordinal) || !resultPath.EndsWith(suffix, StringComparison.Ordinal)) return false;
            testerId = resultPath.Substring(prefix.Length, resultPath.Length - prefix.Length - suffix.Length);
            return testerId == "tester-01" || testerId == "tester-02" || testerId == "tester-03";
        }

        private static bool StringArrayEquals(CanonicalJsonValue values, IReadOnlyList<string> expected)
        {
            if (values == null || expected == null || values.Kind != CanonicalJsonKind.Array || values.Items.Count != expected.Count) return false;
            for (var index = 0; index < expected.Count; index++)
            {
                if (values.Items[index].Kind != CanonicalJsonKind.String || values.Items[index].StringValue != expected[index]) return false;
            }
            return true;
        }

        private static bool StringArraysEqual(CanonicalJsonValue left, CanonicalJsonValue right)
        {
            if (left == null || right == null || left.Kind != CanonicalJsonKind.Array || right.Kind != CanonicalJsonKind.Array || left.Items.Count != right.Items.Count) return false;
            for (var index = 0; index < left.Items.Count; index++)
            {
                if (left.Items[index].Kind != CanonicalJsonKind.String || right.Items[index].Kind != CanonicalJsonKind.String ||
                    left.Items[index].StringValue != right.Items[index].StringValue)
                {
                    return false;
                }
            }
            return true;
        }
        private static bool ValidateCaptureManifest(string root, string manifestPath, string candidateId, string buildSelfHash, List<string> errors)
        {
            string captureSet;
            string[] expectedFiles;
            switch (manifestPath)
            {
                case "visual/1920x1080/capture-manifest.json":
                    captureSet = "Visual1920x1080";
                    expectedFiles = new[] { "visual/1920x1080/tester-01.png", "visual/1920x1080/tester-02.png", "visual/1920x1080/tester-03.png" };
                    break;
                case "visual/1280x720/capture-manifest.json":
                    captureSet = "Visual1280x720";
                    expectedFiles = new[] { "visual/1280x720/tester-01.png", "visual/1280x720/tester-02.png", "visual/1280x720/tester-03.png" };
                    break;
                case "visual/telegraph/capture-manifest.json":
                    captureSet = "Telegraph";
                    expectedFiles = new[] { "visual/telegraph/1280x720.png", "visual/telegraph/1920x1080.png" };
                    break;
                case "visual/grayscale/capture-manifest.json":
                    captureSet = "Grayscale";
                    expectedFiles = new[] { "visual/grayscale/1280x720.png", "visual/grayscale/1920x1080.png" };
                    break;
                case "visual/letterbox/capture-manifest.json":
                    captureSet = "Letterbox";
                    expectedFiles = new[] { "visual/letterbox/1440x1080.png", "visual/letterbox/2560x1080.png" };
                    break;
                default:
                    errors.Add("Capture manifest path is unknown.");
                    return false;
            }

            Document manifest;
            if (!TryReadDocument(root, manifestPath, errors, out manifest)) return false;
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(manifest.Value, "overbless.capture-manifest/v1", new[] { "schema", "candidateId", "buildManifestSha256", "captureSet", "files" });
            if (!AddResult(shape, "capture manifest", errors)) return false;
            string actualCandidate;
            string actualBuild;
            string actualSet;
            CanonicalJsonValue files;
            if (!TryGetString(manifest.Value, "candidateId", out actualCandidate) || !TryGetString(manifest.Value, "buildManifestSha256", out actualBuild) || !TryGetString(manifest.Value, "captureSet", out actualSet) || !manifest.Value.TryGetSingleProperty("files", out files) || files.Kind != CanonicalJsonKind.Array || actualCandidate != candidateId || actualBuild != buildSelfHash || actualSet != captureSet || files.Items.Count != expectedFiles.Length)
            {
                errors.Add("Capture manifest binding or count is invalid.");
                return false;
            }

            string previousPath = null;
            for (var index = 0; index < files.Items.Count; index++)
            {
                var file = files.Items[index];
                var fileShape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(file, new[] { "path", "size", "sha256" });
                if (!AddResult(fileShape, "capture file", errors)) return false;
                string filePath;
                string hash;
                long size;
                if (!TryGetString(file, "path", out filePath) || !TryGetString(file, "sha256", out hash) || !TryGetIntegerProperty(file, "size", out size) || filePath != expectedFiles[index] || size < 0 || !CanonicalJson.IsLowerSha256(hash) || (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, filePath) > 0) || !TryValidateReferencedFile(root, filePath, size, hash, errors))
                {
                    errors.Add("Capture file record is invalid.");
                    return false;
                }
                previousPath = filePath;
            }
            return true;
        }

        private static bool ValidateTransitionLog(string root, string candidateId, string candidateHash, string sourceHash, string buildManifestHash, string fileSetHash, string evidenceManifestHash, string validatorReportHash, IReadOnlyDictionary<string, string> resultDocumentHashes, List<string> errors)
        {
            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(root, TransitionLogFile, "Transition log", errors, out snapshot)) return false;
            var bytes = snapshot.Bytes;
            if (bytes.Length == 0 || bytes[bytes.Length - 1] != (byte)'\n')
            {
                errors.Add("Transition log must end with exactly one LF.");
                return false;
            }
            string text;
            try { text = new System.Text.UTF8Encoding(false, true).GetString(bytes); }
            catch (System.Text.DecoderFallbackException) { errors.Add("Transition log is not UTF-8."); return false; }
            if (text.IndexOf('\r') >= 0)
            {
                errors.Add("Transition log must use LF only.");
                return false;
            }
            var lines = text.Split('\n');
            if (lines.Length < 2 || lines[lines.Length - 1].Length != 0)
            {
                errors.Add("Transition log has an invalid suffix.");
                return false;
            }
            string previousHash = null;
            string previousEvent = null;
            for (var index = 0; index < lines.Length - 1; index++)
            {
                CanonicalJsonValue entry;
                var parseError = "empty line";
                if (lines[index].Length == 0 ||
                    !CanonicalJson.TryParse(lines[index], out entry, out parseError) ||
                    !string.Equals(CanonicalJson.Serialize(entry), lines[index], StringComparison.Ordinal))
                {
                    errors.Add("Transition entry is not canonical: " + parseError + ".");
                    return false;
                }
                var shape = EvidenceSchemaValidator.ValidateSchemaObject(entry, "overbless.transition-entry/v1", new[] { "schema", "candidateId", "seq", "event", "occurredUtc", "refs", "previousEntrySha256", "entrySha256" });
                if (!AddResult(shape, "transition entry", errors)) return false;
                string actualCandidate;
                string eventName;
                string occurredUtc;
                string entryHash;
                CanonicalJsonValue previous;
                CanonicalJsonValue refs;
                long sequence;
                if (!TryGetString(entry, "candidateId", out actualCandidate) || !TryGetIntegerProperty(entry, "seq", out sequence) || !TryGetString(entry, "event", out eventName) || !TryGetString(entry, "occurredUtc", out occurredUtc) || !TryGetString(entry, "entrySha256", out entryHash) || !entry.TryGetSingleProperty("previousEntrySha256", out previous) || !entry.TryGetSingleProperty("refs", out refs) || refs.Kind != CanonicalJsonKind.Object || actualCandidate != candidateId || sequence != index + 1 || !IsUtcMilliseconds(occurredUtc) || !CanonicalJson.IsLowerSha256(entryHash))
                {
                    errors.Add("Transition entry fields are invalid.");
                    return false;
                }
                if ((index == 0 && previous.Kind != CanonicalJsonKind.Null) || (index > 0 && (previous.Kind != CanonicalJsonKind.String || previous.StringValue != previousHash)))
                {
                    errors.Add("Transition previous-entry hash is invalid.");
                    return false;
                }
                if (CanonicalJson.Sha256Hex(entry.WithoutTopLevelProperty("entrySha256")) != entryHash)
                {
                    errors.Add("Transition self hash is invalid.");
                    return false;
                }
                if (!ValidateTransitionEvent(eventName, previousEvent, refs, candidateHash, sourceHash, buildManifestHash, fileSetHash, evidenceManifestHash, validatorReportHash, resultDocumentHashes, errors)) return false;
                previousHash = entryHash;
                previousEvent = eventName;
            }
            if (previousEvent != "MACHINE_READY")
            {
                errors.Add("Transition chain does not end in MACHINE_READY.");
                return false;
            }
            return true;
        }

        private static bool ValidateTransitionEvent(string eventName, string previousEvent, CanonicalJsonValue refs, string candidateHash, string sourceHash, string buildManifestHash, string fileSetHash, string evidenceManifestHash, string validatorReportHash, IReadOnlyDictionary<string, string> resultDocumentHashes, List<string> errors)
        {
            string[] required;
            if (eventName == "SOURCE_SEALED" && previousEvent == null) required = new[] { "candidateSha256", "sourceManifestSha256" };
            else if (eventName == "TESTS_PASSED" && previousEvent == "SOURCE_SEALED") required = new[] { "editModeResultSha256", "playModeResultSha256", "projectConfigResultSha256", "scopeAuditResultSha256" };
            else if (eventName == "BUILD_SEALED" && previousEvent == "TESTS_PASSED") required = new[] { "buildManifestSha256", "fileSetSha256" };
            else if (eventName == "EVIDENCE_SEALED" && previousEvent == "BUILD_SEALED") required = new[] { "evidenceManifestSha256" };
            else if (eventName == "MACHINE_READY" && previousEvent == "EVIDENCE_SEALED") required = new[] { "evidenceManifestSha256", "validatorReportSha256" };
            else
            {
                errors.Add("Transition event sequence is illegal.");
                return false;
            }

            var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(refs, required);
            if (!AddResult(shape, "transition refs", errors)) return false;
            foreach (var key in required)
            {
                string value;
                if (!TryGetString(refs, key, out value) || !CanonicalJson.IsLowerSha256(value))
                {
                    errors.Add("Transition reference is invalid.");
                    return false;
                }
            }

            if (eventName == "SOURCE_SEALED" &&
                (!StringPropertyEquals(refs, "candidateSha256", candidateHash) ||
                 !StringPropertyEquals(refs, "sourceManifestSha256", sourceHash))) return ReferenceFailure(errors);

            if (eventName == "TESTS_PASSED" &&
                (!StringPropertyEquals(refs, "editModeResultSha256", GetResultDocumentHash(resultDocumentHashes, "automated/editmode-results.result.json")) ||
                 !StringPropertyEquals(refs, "playModeResultSha256", GetResultDocumentHash(resultDocumentHashes, "automated/playmode-results.result.json")) ||
                 !StringPropertyEquals(refs, "projectConfigResultSha256", GetResultDocumentHash(resultDocumentHashes, "automated/project-config.result.json")) ||
                 !StringPropertyEquals(refs, "scopeAuditResultSha256", GetResultDocumentHash(resultDocumentHashes, "automated/scope-audit.result.json")))) return ReferenceFailure(errors);

            if (eventName == "BUILD_SEALED" &&
                (!StringPropertyEquals(refs, "buildManifestSha256", buildManifestHash) ||
                 !StringPropertyEquals(refs, "fileSetSha256", fileSetHash))) return ReferenceFailure(errors);

            if (eventName == "EVIDENCE_SEALED" &&
                !StringPropertyEquals(refs, "evidenceManifestSha256", evidenceManifestHash)) return ReferenceFailure(errors);

            if (eventName == "MACHINE_READY" &&
                (!StringPropertyEquals(refs, "evidenceManifestSha256", evidenceManifestHash) ||
                 !StringPropertyEquals(refs, "validatorReportSha256", validatorReportHash))) return ReferenceFailure(errors);

            return true;
        }

        private static string GetResultDocumentHash(IReadOnlyDictionary<string, string> resultDocumentHashes, string path)
        {
            string hash;
            return resultDocumentHashes != null && resultDocumentHashes.TryGetValue(path, out hash) ? hash : null;
        }

        private static bool TryValidateGateDecision(string root, string candidateId, string evidenceHash, string reportHash, List<string> errors, out bool isPass)
        {
            isPass = false;
            var path = SafePath(root, GateDecisionFile);
            if (path == null || !File.Exists(path)) return false;

            Document decision;
            if (!TryReadDocument(root, GateDecisionFile, errors, out decision)) return false;
            var shape = EvidenceSchemaValidator.ValidateRequiredOnlyObject(decision.Value, new[]
            {
                "schema",
                "candidateId",
                "evidenceManifestSha256",
                "validatorReportSha256",
                "decision",
                "decidedBy",
                "decidedUtc",
                "userAttestation",
                "trustAnchor",
                "signatureAlgorithm",
                "signatureBase64"
            });
            if (!AddResult(shape, "gate decision", errors)) return false;

            string schema;
            string actualCandidate;
            string actualEvidence;
            string actualReport;
            string outcome;
            string decidedBy;
            string decidedUtc;
            string attestation;
            string trustAnchor;
            string signatureAlgorithm;
            string signatureBase64;
            if (!TryGetString(decision.Value, "schema", out schema) ||
                !TryGetString(decision.Value, "candidateId", out actualCandidate) ||
                !TryGetString(decision.Value, "evidenceManifestSha256", out actualEvidence) ||
                !TryGetString(decision.Value, "validatorReportSha256", out actualReport) ||
                !TryGetString(decision.Value, "decision", out outcome) ||
                !TryGetString(decision.Value, "decidedBy", out decidedBy) ||
                !TryGetString(decision.Value, "decidedUtc", out decidedUtc) ||
                !TryGetString(decision.Value, "userAttestation", out attestation) ||
                !TryGetString(decision.Value, "trustAnchor", out trustAnchor) ||
                !TryGetString(decision.Value, "signatureAlgorithm", out signatureAlgorithm) ||
                !TryGetString(decision.Value, "signatureBase64", out signatureBase64))
            {
                errors.Add("Gate decision field type is invalid.");
                return false;
            }

            if (schema != EvidenceContracts.GateDecision ||
                actualCandidate != candidateId ||
                actualEvidence != evidenceHash ||
                actualReport != reportHash ||
                (outcome != "PASS" && outcome != "REWORK") ||
                decidedBy != "user" ||
                !IsUtcMilliseconds(decidedUtc) ||
                string.IsNullOrEmpty(attestation) ||
                !VerifyDetachedUserDecisionSignature(
                    actualCandidate,
                    actualEvidence,
                    actualReport,
                    outcome,
                    decidedUtc,
                    trustAnchor,
                    signatureAlgorithm,
                    signatureBase64,
                    errors))
            {
                errors.Add("Gate decision binding or authenticated ownership is invalid.");
                return false;
            }

            isPass = outcome == "PASS";
            return true;
        }

        private static bool HasConfiguredTrustedDecisionKey(List<string> errors)
        {
            const string anchorVariable = "OVERBLESS_M2_GATE_TRUST_ANCHOR";
            const string keyVariable = "OVERBLESS_M2_GATE_TRUSTED_PUBLIC_KEY_SPKI_BASE64";
            var configuredAnchor = Environment.GetEnvironmentVariable(anchorVariable);
            var configuredKey = Environment.GetEnvironmentVariable(keyVariable);
            if (string.IsNullOrWhiteSpace(configuredAnchor) || string.IsNullOrWhiteSpace(configuredKey))
            {
                errors.Add("M2 approval requires a configured detached trusted public key. Set " + anchorVariable + " and " + keyVariable + " outside the candidate directory.");
                return false;
            }

            try
            {
                var publicKey = Convert.FromBase64String(configuredKey);
                using (var rsa = RSA.Create())
                {
                    int bytesRead;
                    rsa.ImportSubjectPublicKeyInfo(publicKey, out bytesRead);
                    if (bytesRead != publicKey.Length)
                    {
                        errors.Add("Configured trusted public key has trailing bytes.");
                        return false;
                    }
                }

                return true;
            }
            catch (FormatException)
            {
                errors.Add("Configured trusted public key is not Base64.");
                return false;
            }
            catch (CryptographicException)
            {
                errors.Add("Configured trusted public key is not a valid RSA SubjectPublicKeyInfo key.");
                return false;
            }
        }
        private static bool VerifyDetachedUserDecisionSignature(
            string candidateId,
            string evidenceHash,
            string reportHash,
            string outcome,
            string decidedUtc,
            string trustAnchor,
            string signatureAlgorithm,
            string signatureBase64,
            List<string> errors)
        {
            const string anchorVariable = "OVERBLESS_M2_GATE_TRUST_ANCHOR";
            const string keyVariable = "OVERBLESS_M2_GATE_TRUSTED_PUBLIC_KEY_SPKI_BASE64";
            var configuredAnchor = Environment.GetEnvironmentVariable(anchorVariable);
            var configuredKey = Environment.GetEnvironmentVariable(keyVariable);
            if (string.IsNullOrWhiteSpace(configuredAnchor) || string.IsNullOrWhiteSpace(configuredKey))
            {
                errors.Add("M2 approval requires a configured detached trusted public key. Set " + anchorVariable + " and " + keyVariable + " outside the candidate directory.");
                return false;
            }

            if (!string.Equals(trustAnchor, configuredAnchor, StringComparison.Ordinal) ||
                !string.Equals(signatureAlgorithm, "RSA-SHA256", StringComparison.Ordinal))
            {
                errors.Add("Gate decision trust anchor or signature algorithm is not configured.");
                return false;
            }

            try
            {
                var publicKey = Convert.FromBase64String(configuredKey);
                var signature = Convert.FromBase64String(signatureBase64);
                if (!string.Equals(Convert.ToBase64String(signature), signatureBase64, StringComparison.Ordinal))
                {
                    errors.Add("Gate decision signature encoding is not canonical Base64.");
                    return false;
                }

                var signedPayload = CanonicalJson.SerializeUtf8(CanonicalJsonValue.Object(
                    new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(candidateId)),
                    new CanonicalJsonProperty("decidedUtc", CanonicalJsonValue.String(decidedUtc)),
                    new CanonicalJsonProperty("decision", CanonicalJsonValue.String(outcome)),
                    new CanonicalJsonProperty("evidenceManifestSha256", CanonicalJsonValue.String(evidenceHash)),
                    new CanonicalJsonProperty("validatorReportSha256", CanonicalJsonValue.String(reportHash))));
                using (var rsa = RSA.Create())
                {
                    int bytesRead;
                    rsa.ImportSubjectPublicKeyInfo(publicKey, out bytesRead);
                    if (bytesRead != publicKey.Length ||
                        !rsa.VerifyData(signedPayload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        errors.Add("Gate decision detached user signature is invalid.");
                        return false;
                    }
                }

                return true;
            }
            catch (FormatException)
            {
                errors.Add("Configured trusted public key or gate signature is not Base64.");
                return false;
            }
            catch (CryptographicException)
            {
                errors.Add("Configured trusted public key is not a valid RSA SubjectPublicKeyInfo key.");
                return false;
            }
        }

        private static bool TryReadDocument(string root, string relativePath, List<string> errors, out Document document)
        {
            document = null;
            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(root, relativePath, "Required evidence file", errors, out snapshot)) return false;

            if (snapshot.Document == null)
            {
                CanonicalJsonValue value;
                string error;
                if (!CanonicalJson.TryParseCanonicalUtf8(snapshot.Bytes, out value, out error))
                {
                    errors.Add("Evidence JSON is noncanonical at " + relativePath + ": " + error + ".");
                    return false;
                }

                snapshot.Document = new Document(value, snapshot.Sha256, snapshot.Bytes);
            }

            document = snapshot.Document;
            return true;
        }

        private static bool TryValidateReferencedFile(string root, string relativePath, long expectedSize, string expectedHash, List<string> errors)
        {
            ArtifactSnapshot snapshot;
            if (!TryGetArtifactSnapshot(root, relativePath, "Referenced artifact", errors, out snapshot)) return false;
            if (snapshot.Bytes.LongLength != expectedSize || snapshot.Sha256 != expectedHash)
            {
                errors.Add("Referenced artifact size or hash is invalid: " + relativePath + ".");
                return false;
            }

            return true;
        }

        private static bool TryGetArtifactSnapshot(string root, string relativePath, string description, List<string> errors, out ArtifactSnapshot snapshot)
        {
            snapshot = null;
            var path = SafePath(root, relativePath);
            if (path == null || !File.Exists(path))
            {
                errors.Add(description + " is missing or unsafe: " + relativePath + ".");
                return false;
            }

            try
            {
                var cache = activeSnapshots ?? (activeSnapshots = new ArtifactSnapshotCache());
                snapshot = cache.Get(path);
                return true;
            }
            catch (UnauthorizedAccessException exception)
            {
                errors.Add(description + " cannot be read: " + relativePath + ": " + exception.Message);
                return false;
            }
            catch (IOException exception)
            {
                errors.Add(description + " cannot be read: " + relativePath + ": " + exception.Message);
                return false;
            }
        }

        private static bool ValidateSelfHash(CanonicalJsonValue value, string field, List<string> errors, out string selfHash)
        {
            selfHash = null;
            if (!TryGetString(value, field, out selfHash) || !CanonicalJson.IsLowerSha256(selfHash) || CanonicalJson.Sha256Hex(value.WithoutTopLevelProperty(field)) != selfHash)
            {
                errors.Add("Self hash is invalid: " + field + ".");
                selfHash = null;
                return false;
            }
            return true;
        }

        private static Dictionary<string, string> CreateExpectedArtifactRoles()
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            Add(expected, "RAW", "automated/editmode-results.xml", "automated/playmode-results.xml", "automated/project-config.raw.json", "automated/scope-audit.raw.json", "automated/audio-events.raw.json");
            Add(expected, "SOURCE_RESULT", "automated/editmode-results.result.json", "automated/playmode-results.result.json", "automated/project-config.result.json", "automated/scope-audit.result.json");
            Add(expected, "BUILD_RESULT", "automated/audio-events.result.json", "visual/identify.result.json", "visual/hit-display.result.json");
            Add(expected, "RAW", "visual/1920x1080/tester-01.png", "visual/1920x1080/tester-02.png", "visual/1920x1080/tester-03.png", "visual/1280x720/tester-01.png", "visual/1280x720/tester-02.png", "visual/1280x720/tester-03.png", "visual/telegraph/1920x1080.png", "visual/telegraph/1280x720.png", "visual/grayscale/1920x1080.png", "visual/grayscale/1280x720.png", "visual/letterbox/1440x1080.png", "visual/letterbox/2560x1080.png");
            Add(expected, "CAPTURE_MANIFEST", "visual/1920x1080/capture-manifest.json", "visual/1280x720/capture-manifest.json", "visual/telegraph/capture-manifest.json", "visual/grayscale/capture-manifest.json", "visual/letterbox/capture-manifest.json");
            AddTesterArtifacts(expected, "usability");
            AddTesterArtifacts(expected, "audio-blind");
            Add(expected, "RAW", "audio-blind/randomization.raw.json");
            AddBrowserArtifacts(expected);
            AddPerformanceArtifacts(expected);
            return expected;
        }

        private static void AddTesterArtifacts(Dictionary<string, string> expected, string directory)
        {
            for (var index = 1; index <= 3; index++)
            {
                var name = "tester-" + index.ToString("00", CultureInfo.InvariantCulture);
                Add(expected, "RAW", directory + "/" + name + ".webm");
                Add(expected, "BUILD_RESULT", directory + "/" + name + ".result.json");
            }
        }

        private static void AddBrowserArtifacts(Dictionary<string, string> expected)
        {
            foreach (var browser in new[] { "chrome", "edge" })
            {
                foreach (var resolution in new[] { "1280x720", "1920x1080" })
                {
                    Add(expected, "RAW", "browser/" + browser + "/" + resolution + ".webm");
                    Add(expected, "BUILD_RESULT", "browser/" + browser + "/" + resolution + ".result.json");
                }
            }
        }

        private static void AddPerformanceArtifacts(Dictionary<string, string> expected)
        {
            foreach (var browser in new[] { "chrome", "edge" })
            {
                foreach (var resolution in new[] { "1280x720", "1920x1080" })
                {
                    foreach (var scenario in new[] { "baseline", "stress" })
                    {
                        var stem = browser + "-" + resolution + "-" + scenario;
                        Add(expected, "RAW", "performance/" + stem + ".csv");
                        Add(expected, "BUILD_RESULT", "performance/" + stem + ".result.json");
                    }
                }
            }
        }

        private static IReadOnlyDictionary<string, ResultExpectation> CreateExpectedResults()
        {
            var results = new Dictionary<string, ResultExpectation>(StringComparer.Ordinal);
            AddExpectation(results, "automated/editmode-results.result.json", "SOURCE_RESULT", "UnityTestRunner", "NUnitSuite", "overbless.source-nunit/v1", new[] { "BLS-EFFECT-001", "BLS-SEAL-002", "CMB-ATTACK-001", "EXT-M2-001", "FND-DISPLAY-002", "FND-RULES-003", "FND-UNITY-001" }, new[] { "automated/editmode-results.xml" }, "Overbless.Tests.EditMode", new[]
            {
                "Overbless.Tests.EditMode.CoreContractTests.AttackStateMachine_LockCancelAndResetDisposeEachContextOnce",
                "Overbless.Tests.EditMode.CoreContractTests.AttackStateMachine_ReentrantObserversCannotPublishStaleLockOrCorruptRecovery",
                "Overbless.Tests.EditMode.CoreContractTests.Blessings_RejectDuplicatesOrderEffectsDeterministicallyAndUseExactMultipliers",
                "Overbless.Tests.EditMode.CoreContractTests.DamageLedger_RejectsDuplicateAndSelfDamageWithoutApplyingTwice",
                "Overbless.Tests.EditMode.CoreContractTests.Health_PreservesRatioAndEmitsOneDeathUntilReset",
                "Overbless.Tests.EditMode.CoreContractTests.HudController_PublishesOnlyChangedValidStates",
                "Overbless.Tests.EditMode.CoreContractTests.PlayerInputRouter_RequiresEveryOwnerToReleaseItsOwnBlock",
                "Overbless.Tests.EditMode.CoreContractTests.WorldHealthBar_TracksHealthRatioFromLeftToRight",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.CanonicalJson_SortsKeysRejectsNonCanonicalBytesAndNormalizesPaths",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceContracts_ExposeApprovedSchemasCriteriaAndCheckOrder",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceContracts_SelectDetailUsesDeclaredFailurePrecedence",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_RejectsPublicPerformancePayloadMutations",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_RejectsPublicSchemaCriteriaAndReportCheckMutations",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_RequiresThreeUniqueAudioEventsAndBlindTesterOrders",
                "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_UsesSixtyHalfOpenPerformanceBuckets"
            });
            AddExpectation(results, "automated/playmode-results.result.json", "SOURCE_RESULT", "UnityTestRunner", "NUnitSuite", "overbless.source-nunit/v1", new[] { "FUN-GUIDED-001", "PLY-LIFE-001", "ROOM-SOUL-001", "WEB-START-003" }, new[] { "automated/playmode-results.xml" }, "Overbless.Tests.PlayMode", new[]
            {
                "Overbless.Tests.PlayMode.M1IntegrationTests.GuidedScene_BlessingsSoulsAudioPauseAndRestartCommitObservableState",
                "Overbless.Tests.PlayMode.M1IntegrationTests.GuidedScene_RequiresTrustedGestureAndRearmsAfterFocusLoss",
                "Overbless.Tests.PlayMode.M1IntegrationTests.M1RoomLifecycle_CollectingRequiredSoulsOpensExitAndResetClearsTransientState",
                "Overbless.Tests.PlayMode.M1IntegrationTests.PlayerLifecycle_DeathAndResetRestoreConfiguredSpawnState"
            });
            AddExpectation(results, "automated/project-config.result.json", "SOURCE_RESULT", "ProjectConfigExporter", "ProjectConfigSnapshot", "overbless.source-project-config/v1", new[] { "FND-DISPLAY-002", "FND-RULES-003", "FND-UNITY-001" }, new[] { "automated/project-config.raw.json" });
            AddExpectation(results, "automated/scope-audit.result.json", "SOURCE_RESULT", "ScopeAudit", "ScopeAudit", "overbless.source-scope-audit/v1", new[] { "EXT-M2-001" }, new[] { "automated/scope-audit.raw.json" });
            AddExpectation(results, "automated/audio-events.result.json", "BUILD_RESULT", "Exporter", "AudioEvents", "overbless.audio-events/v1", new[] { "AUD-ONCE-002" }, new[] { "automated/audio-events.raw.json" });
            AddExpectation(results, "visual/identify.result.json", "BUILD_RESULT", "QACustodian", "VisualIdentify", "overbless.visual/v1", new[] { "VIS-IDENTIFY-001" }, new[] { "visual/1280x720/capture-manifest.json", "visual/1920x1080/capture-manifest.json" });
            AddExpectation(results, "visual/hit-display.result.json", "BUILD_RESULT", "QACustodian", "VisualHitDisplay", "overbless.visual/v1", new[] { "FND-DISPLAY-002", "VIS-HIT-002" }, new[] { "visual/grayscale/capture-manifest.json", "visual/letterbox/capture-manifest.json", "visual/telegraph/capture-manifest.json" });

            for (var index = 1; index <= 3; index++)
            {
                var tester = "tester-" + index.ToString("00", CultureInfo.InvariantCulture);
                AddExpectation(results, "usability/" + tester + ".result.json", "BUILD_RESULT", "QACustodian", "Usability", "overbless.usability/v1", new[] { "FUN-GUIDED-001", "FUN-UNDERSTAND-002" }, new[] { "usability/" + tester + ".webm" });
                AddExpectation(results, "audio-blind/" + tester + ".result.json", "BUILD_RESULT", "QACustodian", "AudioBlind", "overbless.audio-blind/v1", new[] { "AUD-BLIND-001" }, new[] { "audio-blind/randomization.raw.json", "audio-blind/" + tester + ".webm" });
            }

            foreach (var browser in new[] { "chrome", "edge" })
            {
                foreach (var resolution in new[] { "1280x720", "1920x1080" })
                {
                    AddExpectation(results, "browser/" + browser + "/" + resolution + ".result.json", "BUILD_RESULT", "QACustodian", "Browser", "overbless.browser/v1", new[] { "WEB-INPUT-001", "WEB-START-003" }, new[] { "browser/" + browser + "/" + resolution + ".webm" });
                    foreach (var scenario in new[] { "baseline", "stress" })
                    {
                        var stem = browser + "-" + resolution + "-" + scenario;
                        AddExpectation(results, "performance/" + stem + ".result.json", "BUILD_RESULT", "FrameTimeCollector", "Performance", "overbless.performance/v1", new[] { "WEB-PERF-002" }, new[] { "performance/" + stem + ".csv" });
                    }
                }
            }
            return results;
        }

        private static void AddExpectation(Dictionary<string, ResultExpectation> results, string path, string role, string producer, string payloadType, string payloadSchema, string[] criterionIds, string[] rawPaths, string nunitSuite = null, string[] nunitTestFullNames = null)
        {
            results.Add(path, new ResultExpectation(role, producer, payloadType, payloadSchema, ToJsonStrings(criterionIds), rawPaths, nunitSuite, nunitTestFullNames));
        }

        private static IReadOnlyList<CanonicalJsonValue> ToJsonStrings(IEnumerable<string> values)
        {
            var result = new List<CanonicalJsonValue>();
            foreach (var value in values) result.Add(CanonicalJsonValue.String(value));
            return result.AsReadOnly();
        }
        private static void Add(Dictionary<string, string> values, string role, params string[] paths)
        {
            foreach (var path in paths) values.Add(path, role);
        }

        private static bool AddResult(EvidenceValidationResult result, string context, List<string> errors)
        {
            if (result.IsValid) return true;
            errors.Add(context + " failed " + result.Code + ": " + result.Message);
            return false;
        }

        private static bool TryGetString(CanonicalJsonValue value, string property, out string result)
        {
            result = null;
            CanonicalJsonValue child;
            if (!value.TryGetSingleProperty(property, out child) || child.Kind != CanonicalJsonKind.String) return false;
            result = child.StringValue;
            return true;
        }

        private static bool TryGetIntegerProperty(CanonicalJsonValue value, string property, out long result)
        {
            result = 0;
            CanonicalJsonValue child;
            if (!value.TryGetSingleProperty(property, out child) || child.Kind != CanonicalJsonKind.Number || double.IsNaN(child.NumberValue) || double.IsInfinity(child.NumberValue) || child.NumberValue < long.MinValue || child.NumberValue >= 9223372036854775808d || Math.Floor(child.NumberValue) != child.NumberValue)
            {
                return false;
            }

            result = (long)child.NumberValue;
            return true;
        }

        private static bool StringPropertyEquals(CanonicalJsonValue value, string property, string expected)
        {
            string actual;
            return TryGetString(value, property, out actual) && actual == expected;
        }
        private static bool BooleanPropertyEquals(CanonicalJsonValue value, string property, bool expected)
        {
            CanonicalJsonValue actual;
            return value.TryGetSingleProperty(property, out actual) && actual.Kind == CanonicalJsonKind.Boolean && actual.BooleanValue == expected;
        }

        private static bool ReferenceFailure(List<string> errors)
        {
            errors.Add("Transition reference does not bind the sealed artifact.");
            return false;
        }

        private static bool ListsEqual(IReadOnlyList<CanonicalJsonValue> left, IReadOnlyList<CanonicalJsonValue> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (left[index].Kind != CanonicalJsonKind.String || right[index].Kind != CanonicalJsonKind.String || left[index].StringValue != right[index].StringValue) return false;
            }
            return true;
        }
        private static bool ManifestContainsFileHash(CanonicalJsonValue files, string expectedPath, string expectedHash)
        {
            foreach (var file in files.Items)
            {
                string path;
                string hash;
                if (TryGetString(file, "path", out path) && TryGetString(file, "sha256", out hash) &&
                    string.Equals(path, expectedPath, StringComparison.Ordinal) && string.Equals(hash, expectedHash, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ComputeCandidateSourceCapabilitySha256(
            string candidateId,
            string sourceCommit,
            string sourceManifestSha256,
            CanonicalJsonValue sourceFiles)
        {
            var inputs = new List<CanonicalJsonValue>();
            foreach (var file in sourceFiles.Items)
            {
                string path;
                if (TryGetString(file, "path", out path) &&
                    (path.StartsWith("Assets/", StringComparison.Ordinal) ||
                     path.StartsWith("Packages/", StringComparison.Ordinal) ||
                     path.StartsWith("ProjectSettings/", StringComparison.Ordinal)))
                {
                    inputs.Add(file);
                }
            }

            return CanonicalJson.Sha256Hex(CanonicalJsonValue.Object(
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(candidateId)),
                new CanonicalJsonProperty("materializedUnityInputs", CanonicalJsonValue.Array(inputs)),
                new CanonicalJsonProperty("sourceCommit", CanonicalJsonValue.String(sourceCommit)),
                new CanonicalJsonProperty("sourceManifestSha256", CanonicalJsonValue.String(sourceManifestSha256))));
        }
        private static string FindProjectRoot(string candidateRoot)
        {
            if (string.IsNullOrEmpty(candidateRoot)) return null;
            try
            {
                for (var directory = new DirectoryInfo(Path.GetFullPath(candidateRoot)); directory != null; directory = directory.Parent)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                        Directory.Exists(Path.Combine(directory.FullName, "Packages")) &&
                        Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
                    {
                        return directory.FullName;
                    }
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
            return null;
        }

        private static bool TryValidateDirectoryPath(string path, string context, List<string> errors)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!Directory.Exists(fullPath))
                {
                    errors.Add(context + " is missing.");
                    return false;
                }

                if (HasReparsePointInPath(fullPath))
                {
                    errors.Add(context + " contains a reparse point.");
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException || exception is UnauthorizedAccessException)
            {
                errors.Add(context + " cannot be resolved safely.");
                return false;
            }
        }

        private static bool CandidateArtifactExists(string root, string relativePath, List<string> errors)
        {
            var path = SafePath(root, relativePath);
            if (path == null)
            {
                errors.Add("Candidate artifact path is unsafe: " + relativePath + ".");
                return false;
            }

            return File.Exists(path);
        }

        private static string SafePath(string root, string relativePath)
        {
            if (!CanonicalJson.IsNormalizedRelativePath(relativePath)) return null;
            try
            {
                var fullRoot = Path.GetFullPath(root);
                if (!Directory.Exists(fullRoot) || HasReparsePointInPath(fullRoot)) return null;

                var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!fullPath.StartsWith(prefix, comparison)) return null;

                var current = fullRoot;
                foreach (var segment in relativePath.Split('/'))
                {
                    current = Path.Combine(current, segment);
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return null;
                    }
                }

                return fullPath;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException || exception is UnauthorizedAccessException || exception is IOException)
            {
                return null;
            }
        }

        private static bool HasReparsePointInPath(string fullPath)
        {
            var normalized = Path.GetFullPath(fullPath);
            var root = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(root)) return true;

            var current = root;
            var remainder = normalized.Substring(root.Length);
            foreach (var segment in remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }

            return false;
        }

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var character in value)
            {
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f')) return false;
            }
            return true;
        }
        private static string Sha256GitBlob(string projectRoot, string objectId)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "cat-file blob " + objectId,
                        WorkingDirectory = projectRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    process.Start();
                    long ignoredLength;
                    var sha256 = CanonicalJson.Sha256Hex(process.StandardOutput.BaseStream, out ignoredLength);
                    var standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new InvalidOperationException("Unable to read immutable Git blob: " + standardError);
                    return sha256;
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to execute Git.", exception);
            }
        }

        private static string DecodeGitText(byte[] bytes, int offset, int count)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes, offset, count);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Git tree contains invalid UTF-8 text.", exception);
            }
        }

        private static GitBytesResult RunGitBytes(string projectRoot, string arguments)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = arguments,
                        WorkingDirectory = projectRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    process.Start();
                    using (var output = new MemoryStream())
                    {
                        process.StandardOutput.BaseStream.CopyTo(output);
                        var standardError = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        return new GitBytesResult(process.ExitCode, output.ToArray(), standardError);
                    }
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to execute Git.", exception);
            }
        }

        private static bool IsUtcMilliseconds(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 24 || value[10] != 'T' || value[19] != '.' || value[23] != 'Z') return false;
            DateTime parsed;
            return DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);
        }

        private static bool TryGetCommandLineArgument(string name, out string value)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] == name)
                {
                    value = arguments[index + 1];
                    return !string.IsNullOrEmpty(value);
                }
            }
            value = null;
            return false;
        }

        private static M2GateValidationResult Failure(string error)
        {
            return Failure(new List<string> { error });
        }

        private static M2GateValidationResult Failure(List<string> errors)
        {
            return new M2GateValidationResult(false, false, errors.AsReadOnly());
        }
        private sealed class SourceTreeEntry
        {
            public SourceTreeEntry(string mode, string path, string sha256)
            {
                Mode = mode;
                Path = path;
                Sha256 = sha256;
            }

            public string Mode { get; }
            public string Path { get; }
            public string Sha256 { get; }
        }

        private sealed class ScopeAllowance
        {
            public ScopeAllowance(string path, string token, string sourceSha256, long line, long column, string approvalReference)
            {
                Path = path;
                Token = token;
                SourceSha256 = sourceSha256;
                Line = line;
                Column = column;
                ApprovalReference = approvalReference;
            }

            public string Path { get; }
            public string Token { get; }
            public string SourceSha256 { get; }
            public long Line { get; }
            public long Column { get; }
            public string ApprovalReference { get; }
        }

        private sealed class ScopeMatch
        {
            public ScopeMatch(string path, string token, string sourceSha256, long line, long column)
            {
                Path = path;
                Token = token;
                SourceSha256 = sourceSha256;
                Line = line;
                Column = column;
                Identity = ScopeIdentity(path, token, sourceSha256, line, column);
            }

            public string Path { get; }
            public string Token { get; }
            public string SourceSha256 { get; }
            public long Line { get; }
            public long Column { get; }
            public string Identity { get; }
        }

        private sealed class GitBytesResult
        {
            public GitBytesResult(int exitCode, byte[] standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }
            public byte[] StandardOutput { get; }
            public string StandardError { get; }
        }

        private sealed class ResultExpectation
        {
            public ResultExpectation(string role, string producer, string payloadType, string payloadSchema, IReadOnlyList<CanonicalJsonValue> criterionIds, string[] rawPaths, string nunitSuite, string[] nunitTestFullNames)
            {
                Role = role;
                Producer = producer;
                PayloadType = payloadType;
                PayloadSchema = payloadSchema;
                CriterionIds = criterionIds;
                RawPaths = rawPaths;
                NUnitSuite = nunitSuite;
                NUnitTestFullNames = Array.AsReadOnly(nunitTestFullNames ?? new string[0]);
            }

            public string Role { get; }
            public string Producer { get; }
            public string PayloadType { get; }
            public string PayloadSchema { get; }
            public IReadOnlyList<CanonicalJsonValue> CriterionIds { get; }
            public string[] RawPaths { get; }
            public string NUnitSuite { get; }
            public IReadOnlyList<string> NUnitTestFullNames { get; }
        }
        private sealed class ArtifactSnapshotCache
        {
            private readonly Dictionary<string, ArtifactSnapshot> snapshots =
                new Dictionary<string, ArtifactSnapshot>(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            public ArtifactSnapshot Get(string path)
            {
                ArtifactSnapshot snapshot;
                if (snapshots.TryGetValue(path, out snapshot)) return snapshot;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Artifact is a reparse point.");
                }

                var bytes = File.ReadAllBytes(path);
                snapshot = new ArtifactSnapshot(bytes, CanonicalJson.Sha256Hex(bytes));
                snapshots.Add(path, snapshot);
                return snapshot;
            }
        }

        private sealed class ArtifactSnapshot
        {
            public ArtifactSnapshot(byte[] bytes, string sha256)
            {
                Bytes = bytes;
                Sha256 = sha256;
            }

            public byte[] Bytes { get; }
            public string Sha256 { get; }
            public Document Document { get; set; }
        }

        private sealed class Document
        {
            public Document(CanonicalJsonValue value, string rawSha256, byte[] rawBytes)
            {
                Value = value;
                RawSha256 = rawSha256;
                RawBytes = rawBytes;
            }

            public CanonicalJsonValue Value { get; }
            public string RawSha256 { get; }
            public byte[] RawBytes { get; }
        }
    }
}
