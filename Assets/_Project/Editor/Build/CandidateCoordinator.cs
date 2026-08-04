using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Overbless.Editor.Evidence;
using UnityEngine;

namespace Overbless.Editor.Build
{
    /// <summary>
    /// Creates and advances a local candidate only through the approved, write-once evidence chain.
    /// </summary>
    public static class CandidateCoordinator
    {
        public const string CandidateRootRelativePath = "Evidence/M2EntryGate";

        private const string RequiredUnityVersion = "6000.0.72f1";
        private const string ApprovalRelativePath = "Docs/Decisions/M0_SOURCE_APPROVAL.json";
        private const string PackageLockRelativePath = "Packages/packages-lock.json";
        private const string RequiredScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";
        private const string TransitionSchema = "overbless.transition-entry/v1";
        private const string CandidateFileName = "candidate.json";
        private const string SourceManifestFileName = "source-manifest.json";
        private const string TransitionLogFileName = "transition-log.jsonl";
        private const string BuildManifestFileName = "build/build-manifest.json";
        private const string EvidenceManifestFileName = "evidence-manifest.json";
        private const string ValidatorReportFileName = "validator-report.json";

        private static readonly string[] TestReferenceNames =
        {
            "editModeResultSha256",
            "playModeResultSha256",
            "projectConfigResultSha256",
            "scopeAuditResultSha256"
        };

        private static readonly string[] TestResultPaths =
        {
            "automated/editmode-results.result.json",
            "automated/playmode-results.result.json",
            "automated/project-config.result.json",
            "automated/scope-audit.result.json"
        };
        private static readonly IReadOnlyList<string> RequiredScopeRoots = ScopeAudit.ScannedRoots;
        private static readonly IReadOnlyList<string> RequiredScopeExclusions = ScopeAudit.ExcludedSourcePaths;
        private static readonly TestResultExpectation[] TestResultExpectations =
        {
            new TestResultExpectation(
                "automated/editmode-results.result.json",
                "automated/editmode-results.xml",
                "UnityTestRunner",
                "NUnitSuite",
                "overbless.source-nunit/v1",
                new[] { "BLS-EFFECT-001", "BLS-SEAL-002", "CMB-ATTACK-001", "EXT-M2-001", "FND-DISPLAY-002", "FND-RULES-003", "FND-UNITY-001" },
                "Overbless.Tests.EditMode",
                new[]
                {
                    "Overbless.Tests.EditMode.CoreContractTests.AttackStateMachine_LockCancelAndResetDisposeEachContextOnce",
                    "Overbless.Tests.EditMode.CoreContractTests.AttackStateMachine_ReentrantObserversCannotPublishStaleLockOrCorruptRecovery",
                    "Overbless.Tests.EditMode.CoreContractTests.Blessings_RejectDuplicatesOrderEffectsDeterministicallyAndUseExactMultipliers",
                    "Overbless.Tests.EditMode.CoreContractTests.DamageLedger_KeysAcceptedDamageByAttackAndTargetAndRejectsSelfDamage",
                    "Overbless.Tests.EditMode.CoreContractTests.Health_PreservesRatioAndEmitsOneDeathUntilReset",
                    "Overbless.Tests.EditMode.CoreContractTests.Health_ReentrantResetAndRekillNeverPublishesTheOlderDeath",
                    "Overbless.Tests.EditMode.CoreContractTests.Health_ReentrantResetFromDeathStopsLaterOldLifeObservers",
                    "Overbless.Tests.EditMode.CoreContractTests.HudController_PublishesOnlyChangedValidStates",
                    "Overbless.Tests.EditMode.CoreContractTests.PlayerInputRouter_RequiresEveryOwnerToReleaseItsOwnBlock",
                    "Overbless.Tests.EditMode.CoreContractTests.WorldHealthBar_TracksHealthRatioFromLeftToRight",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.CanonicalJson_SortsKeysRejectsNonCanonicalBytesAndNormalizesPaths",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceContracts_ExposeApprovedSchemasCriteriaAndCheckOrder",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceContracts_SelectDetailUsesDeclaredFailurePrecedence",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_RejectsPublicPerformancePayloadMutations",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_RejectsPublicSchemaCriteriaAndReportCheckMutations",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_RequiresThreeUniqueAudioEventsAndBlindTesterOrders",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.EvidenceSchemaValidator_UsesSixtyHalfOpenPerformanceBuckets",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.M2EntryGateValidator_DetachedSignatureBindsUserAttestation",
                    "Overbless.Tests.EditMode.EvidenceSchemaTests.M2Validator_ArtifactSnapshotRetainsOneBoundedPrivateCopy"
                }),
            new TestResultExpectation(
                "automated/playmode-results.result.json",
                "automated/playmode-results.xml",
                "UnityTestRunner",
                "NUnitSuite",
                "overbless.source-nunit/v1",
                new[] { "FUN-GUIDED-001", "PLY-LIFE-001", "ROOM-SOUL-001", "WEB-START-003" },
                "Overbless.Tests.PlayMode",
                new[]
                {
                    "Overbless.Tests.PlayMode.M1IntegrationTests.GuidedScene_BlessingsSoulsAudioPauseAndRestartCommitObservableState",
                    "Overbless.Tests.PlayMode.M1IntegrationTests.GuidedScene_RequiresTrustedGestureAndRearmsAfterFocusLoss",
                    "Overbless.Tests.PlayMode.M1IntegrationTests.M1RoomLifecycle_CollectingRequiredSoulsOpensExitAndResetClearsTransientState",
                    "Overbless.Tests.PlayMode.M1IntegrationTests.PlayerLifecycle_DeathAndResetRestoreConfiguredSpawnState"
                }),
            new TestResultExpectation(
                "automated/project-config.result.json",
                "automated/project-config.raw.json",
                "ProjectConfigExporter",
                "ProjectConfigSnapshot",
                "overbless.source-project-config/v1",
                new[] { "FND-DISPLAY-002", "FND-RULES-003", "FND-UNITY-001" }),
            new TestResultExpectation(
                "automated/scope-audit.result.json",
                "automated/scope-audit.raw.json",
                "ScopeAudit",
                "ScopeAudit",
                "overbless.source-scope-audit/v1",
                new[] { "EXT-M2-001" })
        };

        public sealed class TerminalMachineEvent
        {
            internal TerminalMachineEvent(string candidateId, string eventName, string entrySha256, string evidenceManifestSha256, string validatorReportSha256)
            {
                CandidateId = candidateId;
                EventName = eventName;
                EntrySha256 = entrySha256;
                EvidenceManifestSha256 = evidenceManifestSha256;
                ValidatorReportSha256 = validatorReportSha256;
            }

            public string CandidateId { get; }
            public string EventName { get; }
            public string EntrySha256 { get; }
            public string EvidenceManifestSha256 { get; }
            public string ValidatorReportSha256 { get; }
        }

        private sealed class SourceFile
        {
            public SourceFile(string path, string sha256)
                : this(path, null, -1, sha256)
            {
            }

            public SourceFile(string path, string mode, long size, string sha256)
            {
                Path = path;
                Mode = mode;
                Size = size;
                Sha256 = sha256;
            }

            public string Path { get; }
            public string Mode { get; }
            public long Size { get; }
            public string Sha256 { get; }
        }
        private sealed class TestResultExpectation
        {
            public TestResultExpectation(string resultPath, string rawPath, string producer, string payloadType, string payloadSchema, string[] criterionIds)
                : this(resultPath, rawPath, producer, payloadType, payloadSchema, criterionIds, null, null)
            {
            }

            public TestResultExpectation(string resultPath, string rawPath, string producer, string payloadType, string payloadSchema, string[] criterionIds, string nunitSuite, string[] nunitTestFullNames)
            {
                ResultPath = resultPath;
                RawPath = rawPath;
                Producer = producer;
                PayloadType = payloadType;
                PayloadSchema = payloadSchema;
                CriterionIds = criterionIds;
                NUnitSuite = nunitSuite;
                NUnitTestFullNames = nunitTestFullNames ?? new string[0];
            }

            public string ResultPath { get; }
            public string RawPath { get; }
            public string Producer { get; }
            public string PayloadType { get; }
            public string PayloadSchema { get; }
            public string[] CriterionIds { get; }
            public string NUnitSuite { get; }
            public IReadOnlyList<string> NUnitTestFullNames { get; }
        }


        private sealed class CandidateDocument
        {
            public string CandidateId;
            public string CandidateSha256;
            public string CreatedUtc;
            public string Scene;
            public string SourceCommit;
            public string UnityVersion;
        }

        private sealed class SourceManifestDocument
        {
            public string CandidateId;
            public string CandidateSha256;
            public string PackageLockSha256;
            public string SourceCommit;
            public List<SourceFile> Files;
            public string SourceManifestSha256;
            public string SourceTreeSha256;
        }

        private sealed class TransitionDocument
        {
            public string CandidateId;
            public string EntrySha256;
            public string EventName;
            public string OccurredUtc;
            public string PreviousEntrySha256;
            public Dictionary<string, string> References;
            public int Sequence;
        }

        private sealed class ValidatedChain
        {
            public CandidateDocument Candidate;
            public SourceManifestDocument SourceManifest;
            public List<TransitionDocument> Entries;
            public long TransitionLogLength;
            public string TransitionLogSha256;
        }

        /// <summary>Creates candidate.json, source-manifest.json, and the initial SOURCE_SEALED transition.</summary>
        public static string CreateCandidate(string candidateId)
        {
            candidateId = NormalizeCandidateId(candidateId);
            var projectRoot = GetProjectRoot();
            RequireApprovedUnityVersion();
            EnsureCleanGitRepository(projectRoot);

            var sourceCommit = ReadGitCommit(projectRoot);
            var sourceFiles = ReadTrackedSourceFiles(projectRoot, sourceCommit);
            var approvedSources = ReadApprovedSources(projectRoot, sourceCommit);
            VerifyApprovedSources(sourceFiles, approvedSources);
            ValidateMaterializedUnityInputs(projectRoot, sourceFiles);

            var packageLockSha256 = FindSourceFile(sourceFiles, PackageLockRelativePath).Sha256;
            var sourceTreeSha256 = ComputeSourceTreeSha256(sourceFiles);
            var createdUtc = FormatUtc(DateTime.UtcNow);
            var candidateWithoutSelfHash = CreateCandidateValue(candidateId, sourceCommit, Application.unityVersion, RequiredScenePath, createdUtc, null);
            var candidateSha256 = CanonicalJson.Sha256Hex(candidateWithoutSelfHash);
            var candidate = CreateCandidateValue(candidateId, sourceCommit, Application.unityVersion, RequiredScenePath, createdUtc, candidateSha256);

            var sourceManifestWithoutSelfHash = CreateSourceManifestValue(
                candidateId,
                candidateSha256,
                sourceCommit,
                sourceFiles,
                packageLockSha256,
                sourceTreeSha256,
                null);
            var sourceManifestSha256 = CanonicalJson.Sha256Hex(sourceManifestWithoutSelfHash);
            var sourceManifest = CreateSourceManifestValue(
                candidateId,
                candidateSha256,
                sourceCommit,
                sourceFiles,
                packageLockSha256,
                sourceTreeSha256,
                sourceManifestSha256);

            var sourceReferences = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["candidateSha256"] = candidateSha256,
                ["sourceManifestSha256"] = sourceManifestSha256
            };
            var sourceTransitionWithoutSelfHash = CreateTransitionValue(
                candidateId,
                1,
                "SOURCE_SEALED",
                createdUtc,
                sourceReferences,
                null,
                null);
            var sourceTransitionSha256 = CanonicalJson.Sha256Hex(sourceTransitionWithoutSelfHash);
            var sourceTransition = CreateTransitionValue(
                candidateId,
                1,
                "SOURCE_SEALED",
                createdUtc,
                sourceReferences,
                null,
                sourceTransitionSha256);

            var candidateDirectory = GetCandidateDirectory(candidateId);
            if (Directory.Exists(candidateDirectory)) throw new InvalidOperationException("Write-once candidate directory already exists: " + candidateDirectory);
            var candidatePath = Path.Combine(candidateDirectory, CandidateFileName);
            var sourceManifestPath = Path.Combine(candidateDirectory, SourceManifestFileName);
            var transitionLogPath = Path.Combine(candidateDirectory, TransitionLogFileName);
            EnsurePathsDoNotExist(candidatePath, sourceManifestPath, transitionLogPath);
            EnsureGitSnapshotStillClean(projectRoot, sourceCommit);

            WriteCanonicalJsonNew(candidatePath, candidate);
            WriteCanonicalJsonNew(sourceManifestPath, sourceManifest);
            WriteTransitionLogNew(transitionLogPath, sourceTransition);
            return candidateDirectory;
        }
        /// <summary>Batch-mode entry point. Requires -candidateId and writes only the initial SOURCE_SEALED candidate state.</summary>
        public static void CreateCandidateForBatchMode()
        {
            string candidateId = null;
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], "-candidateId", StringComparison.Ordinal))
                {
                    continue;
                }

                candidateId = arguments[index + 1];
                break;
            }

            if (string.IsNullOrEmpty(candidateId))
            {
                throw new InvalidOperationException("-candidateId is required.");
            }

            var candidateDirectory = CreateCandidate(candidateId);
            UnityEngine.Debug.Log("Created source-sealed M2 entry candidate at '" + candidateDirectory + "'.");
        }
        /// <summary>
        /// Acquires the candidate-bound source capability immediately before BuildPlayer. It includes every
        /// materialized Unity input and rejects ignored, missing, extra, or reparse-point inputs.
        /// </summary>
        internal static BuildManifestWriter.CandidateSourceCapability AcquireCandidateSourceCapability(string candidateId)
        {
            candidateId = NormalizeCandidateId(candidateId);
            var chain = GetValidatedChain(candidateId, false);
            if (chain.Entries.Count == 0 ||
                !string.Equals(chain.Entries[chain.Entries.Count - 1].EventName, "TESTS_PASSED", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A candidate source capability can be acquired only after TESTS_PASSED.");
            }

            var projectRoot = GetProjectRoot();
            var inputs = ValidateMaterializedUnityInputs(projectRoot, chain.SourceManifest.Files);
            var digest = ComputeCandidateSourceCapabilitySha256(
                candidateId,
                chain.Candidate.SourceCommit,
                chain.SourceManifest.SourceManifestSha256,
                inputs);
            return new BuildManifestWriter.CandidateSourceCapability(
                candidateId,
                chain.Candidate.SourceCommit,
                chain.SourceManifest.SourceManifestSha256,
                digest);
        }


        /// <summary>
        /// Appends exactly one legal transition. External references are read from their standard candidate-local paths,
        /// never accepted as caller supplied hashes.
        internal static void SealSuccessfulBuild(
            string candidateId,
            string servedDirectory,
            string servedManifestPath,
            BuildManifestWriter.BuildProvenance provenance)
        {
            candidateId = NormalizeCandidateId(candidateId);
            var chain = GetValidatedChain(candidateId, false);
            if (chain.Entries.Count == 0 ||
                !string.Equals(chain.Entries[chain.Entries.Count - 1].EventName, "TESTS_PASSED", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A successful build may be sealed only after TESTS_PASSED.");
            }

            RequireCandidateSourceCapability(candidateId, chain, provenance);
            if (!string.Equals(provenance.Scene, chain.Candidate.Scene, StringComparison.Ordinal) ||
                !string.Equals(provenance.Scene, RequiredScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate build provenance scene does not bind the approved candidate scene.");
            }
            var servedRootManifestSha256 = BuildManifestWriter.ConsumeCandidateBridge(
                provenance,
                servedDirectory,
                servedManifestPath);
            var servedManifest = ReadCanonicalDocument(servedManifestPath);
            var servedFiles = RequireArray(servedManifest, "files");
            var servedFileSetSha256 = RequireLowerSha256(servedManifest, "fileSetSha256");
            if (!string.Equals(servedFileSetSha256, CanonicalJson.Sha256Hex(servedFiles), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Served-root file-set hash is invalid.");
            }

            var candidateDirectory = GetCandidateDirectory(candidateId);
            var buildDirectory = GetPathInsideDirectory(candidateDirectory, "build");
            EnsurePathsDoNotExist(buildDirectory);
            var stagingDirectory = GetPathInsideDirectory(
                candidateDirectory,
                ".build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                foreach (var file in servedFiles.Items)
                {
                    RequireExactKeys(file, "path", "sha256", "size");
                    var relativePath = RequireString(file, "path");
                    var expectedSha256 = RequireLowerSha256(file, "sha256");
                    var expectedSize = RequireNonNegativeLong(file, "size");
                    var sourcePath = GetPathInsideDirectory(servedDirectory, relativePath);
                    EnsurePathHasNoReparsePoints(servedDirectory, relativePath);
                    var destinationPath = GetPathInsideDirectory(stagingDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Copy(sourcePath, destinationPath, false);
                    var info = new FileInfo(destinationPath);
                    if (info.Length != expectedSize ||
                        !string.Equals(Sha256File(destinationPath), expectedSha256, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Copied candidate build file does not match served bytes.");
                    }
                }

                var settings = CanonicalJsonValue.Object(
                    new CanonicalJsonProperty("autoconnectProfiler", CanonicalJsonValue.Boolean(false)),
                    Property("compressionFormat", "Disabled"),
                    new CanonicalJsonProperty("decompressionFallback", CanonicalJsonValue.Boolean(false)),
                    new CanonicalJsonProperty("deepProfiling", CanonicalJsonValue.Boolean(false)),
                    new CanonicalJsonProperty("development", CanonicalJsonValue.Boolean(true)),
                    Property("exceptionSupport", "ExplicitlyThrownExceptionsOnly"),
                    new CanonicalJsonProperty("memorySizeMb", CanonicalJsonValue.Number(provenance.Settings.MemorySizeMb)),
                    Property("scene", provenance.Scene),
                    Property("target", "WebGL"),
                    Property("unityVersion", RequiredUnityVersion));
                var unsigned = CanonicalJsonValue.Object(
                    Property("schema", EvidenceContracts.BuildManifest),
                    Property("candidateId", candidateId),
                    Property("sourceManifestSha256", chain.SourceManifest.SourceManifestSha256),
                    Property("sourceCapabilitySha256", provenance.SourceCapability.Digest),
                    Property("servedRootManifestSha256", servedRootManifestSha256),
                    new CanonicalJsonProperty("settings", settings),
                    new CanonicalJsonProperty("files", servedFiles),
                    Property("fileSetSha256", servedFileSetSha256));
                var manifest = CanonicalJsonValue.Object(
                    Property("schema", EvidenceContracts.BuildManifest),
                    Property("candidateId", candidateId),
                    Property("sourceManifestSha256", chain.SourceManifest.SourceManifestSha256),
                    Property("sourceCapabilitySha256", provenance.SourceCapability.Digest),
                    Property("servedRootManifestSha256", servedRootManifestSha256),
                    new CanonicalJsonProperty("settings", settings),
                    new CanonicalJsonProperty("files", servedFiles),
                    Property("fileSetSha256", servedFileSetSha256),
                    Property("buildManifestSha256", CanonicalJson.Sha256Hex(unsigned)));
                WriteCanonicalJsonNew(
                    Path.Combine(stagingDirectory, Path.GetFileName(BuildManifestFileName)),
                    manifest);
                Directory.Move(stagingDirectory, buildDirectory);
            }
            catch
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, true);
                }

                throw;
            }
        }

        /// </summary>
        public static string AppendMachineEvent(string candidateId, string eventName)
        {
            candidateId = NormalizeCandidateId(candidateId);
            if (string.IsNullOrEmpty(eventName)) throw new ArgumentException("Transition event is required.", nameof(eventName));

            var chain = GetValidatedChain(candidateId, false);
            var previous = chain.Entries[chain.Entries.Count - 1];
            var candidateDirectory = GetCandidateDirectory(candidateId);
            var normalizedEventName = eventName.Trim();
            var derivedEventName = DeriveNextEvent(candidateDirectory, chain);
            if (!string.Equals(normalizedEventName, derivedEventName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Transition event must be derived as '{derivedEventName}', not asserted as '{normalizedEventName}'.");
            }

            if (!IsLegalNextEvent(previous.EventName, derivedEventName, chain.Entries))
            {
                throw new InvalidOperationException($"Transition '{derivedEventName}' cannot follow '{previous.EventName}'.");
            }

            var references = ReadReferencesForEvent(candidateDirectory, derivedEventName, chain.Candidate, chain.SourceManifest);
            EnsureGitSnapshotStillClean(GetProjectRoot(), chain.Candidate.SourceCommit);
            var sequence = previous.Sequence + 1;
            var occurredUtc = FormatUtc(DateTime.UtcNow);
            var transitionWithoutSelfHash = CreateTransitionValue(
                candidateId,
                sequence,
                derivedEventName,
                occurredUtc,
                references,
                previous.EntrySha256,
                null);
            var entrySha256 = CanonicalJson.Sha256Hex(transitionWithoutSelfHash);
            var transition = CreateTransitionValue(
                candidateId,
                sequence,
                derivedEventName,
                occurredUtc,
                references,
                previous.EntrySha256,
                entrySha256);
            var transitionLogPath = Path.Combine(candidateDirectory, TransitionLogFileName);
            AppendTransitionLog(transitionLogPath, transition, chain.TransitionLogLength, chain.TransitionLogSha256);
            return transitionLogPath;
        }

        /// <summary>Returns the terminal event only when the complete candidate chain is valid.</summary>
        public static TerminalMachineEvent GetValidatedTerminalMachineEvent(string candidateId)
        {
            candidateId = NormalizeCandidateId(candidateId);
            var chain = GetValidatedChain(candidateId, true);
            var terminal = chain.Entries[chain.Entries.Count - 1];
            return new TerminalMachineEvent(
                candidateId,
                terminal.EventName,
                terminal.EntrySha256,
                terminal.References["evidenceManifestSha256"],
                terminal.References["validatorReportSha256"]);
        }

        public static string GetCandidateDirectory(string candidateId)
        {
            return Path.Combine(GetProjectRoot(), CandidateRootRelativePath.Replace('/', Path.DirectorySeparatorChar), NormalizeCandidateId(candidateId));
        }

        private static ValidatedChain GetValidatedChain(string candidateId, bool requireTerminal)
        {
            var candidateDirectory = GetCandidateDirectory(candidateId);
            if (!Directory.Exists(candidateDirectory) || IsReparsePoint(candidateDirectory))
            {
                throw new InvalidOperationException("Candidate directory is missing or unsafe.");
            }

            EnsurePathHasNoReparsePoints(GetProjectRoot(), CandidateRootRelativePath + "/" + candidateId);
            var candidate = ReadCandidate(Path.Combine(candidateDirectory, CandidateFileName), candidateId);
            var projectRoot = GetProjectRoot();
            EnsureGitSnapshotStillClean(projectRoot, candidate.SourceCommit);

            var sourceManifest = ReadSourceManifest(Path.Combine(candidateDirectory, SourceManifestFileName), candidate);
            var sourceFiles = ReadTrackedSourceFiles(projectRoot, candidate.SourceCommit);
            var approvedSources = ReadApprovedSources(projectRoot, candidate.SourceCommit);
            VerifyApprovedSources(sourceFiles, approvedSources);
            ValidateSourceManifestSnapshot(sourceManifest, sourceFiles);
            ValidateMaterializedUnityInputs(projectRoot, sourceManifest.Files);

            long transitionLogLength;
            string transitionLogSha256;
            var entries = ReadTransitionLog(Path.Combine(candidateDirectory, TransitionLogFileName), candidate, sourceManifest, out transitionLogLength, out transitionLogSha256);

            var lastEvent = entries[entries.Count - 1].EventName;
            if (requireTerminal && !IsTerminalEvent(lastEvent))
            {
                throw new InvalidOperationException("Candidate does not have a terminal machine event.");
            }

            return new ValidatedChain
            {
                Candidate = candidate,
                SourceManifest = sourceManifest,
                Entries = entries,
                TransitionLogLength = transitionLogLength,
                TransitionLogSha256 = transitionLogSha256
            };
        }

        private static List<TransitionDocument> ReadTransitionLog(string path, CandidateDocument candidate, SourceManifestDocument sourceManifest, out long length, out string sha256)
        {
            if (!File.Exists(path) || IsReparsePoint(path)) throw new InvalidOperationException("Candidate transition log is missing or unsafe.");
            var bytes = File.ReadAllBytes(path);
            length = bytes.LongLength;
            sha256 = CanonicalJson.Sha256Hex(bytes);
            if (bytes.Length == 0 || bytes[bytes.Length - 1] != (byte)'\n') throw new InvalidOperationException("Transition log must end with a newline.");
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Transition log is not valid UTF-8.", exception);
            }

            var lines = text.Split('\n');
            var entries = new List<TransitionDocument>();
            string previousEntrySha256 = null;
            for (var index = 0; index < lines.Length - 1; index++)
            {
                var line = lines[index];
                if (line.Length == 0 || line.IndexOf('\r') >= 0) throw new InvalidOperationException("Transition log contains an invalid line.");
                CanonicalJsonValue value;
                string error;
                if (!CanonicalJson.TryParseCanonicalUtf8(new UTF8Encoding(false, true).GetBytes(line), out value, out error) || value.Kind != CanonicalJsonKind.Object)
                {
                    throw new InvalidOperationException("Transition log entry is not canonical JSON: " + error);
                }

                var entry = ReadTransition(value, candidate, sourceManifest, index + 1, previousEntrySha256);
                if (index == 0)
                {
                    if (!string.Equals(entry.EventName, "SOURCE_SEALED", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("The first transition must be SOURCE_SEALED.");
                    }
                }
                else if (!IsLegalNextEvent(entries[index - 1].EventName, entry.EventName, entries))
                {
                    throw new InvalidOperationException($"Transition '{entry.EventName}' is not legal after '{entries[index - 1].EventName}'.");
                }

                ValidateTransitionReferences(Path.GetDirectoryName(path), candidate, sourceManifest, entry);
                entries.Add(entry);
                previousEntrySha256 = entry.EntrySha256;
            }

            if (entries.Count == 0) throw new InvalidOperationException("Candidate transition log has no entries.");
            return entries;
        }
        private static CandidateDocument ReadCandidate(string path, string expectedCandidateId)
        {
            var value = ReadCanonicalDocument(path);
            RequireExactKeys(value, "candidateId", "candidateSha256", "createdUtc", "scene", "schema", "sourceCommit", "unityVersion");
            var candidate = new CandidateDocument
            {
                CandidateId = RequireString(value, "candidateId"),
                CandidateSha256 = RequireLowerSha256(value, "candidateSha256"),
                CreatedUtc = RequireString(value, "createdUtc"),
                Scene = RequireString(value, "scene"),
                SourceCommit = RequireLowerHex(value, "sourceCommit", 40),
                UnityVersion = RequireString(value, "unityVersion")
            };
            if (!string.Equals(RequireString(value, "schema"), EvidenceContracts.Candidate, StringComparison.Ordinal)) throw new InvalidOperationException("Candidate schema is invalid.");
            if (!string.Equals(candidate.CandidateId, expectedCandidateId, StringComparison.Ordinal) || NormalizeCandidateId(candidate.CandidateId) != candidate.CandidateId)
            {
                throw new InvalidOperationException("Candidate identifier is invalid.");
            }

            if (!CanonicalJson.IsNormalizedRelativePath(candidate.Scene) || !string.Equals(candidate.Scene, RequiredScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate scene is invalid.");
            }

            RequireUtc(candidate.CreatedUtc, "candidate createdUtc");
            if (!string.Equals(candidate.UnityVersion, RequiredUnityVersion, StringComparison.Ordinal)) throw new InvalidOperationException("Candidate unityVersion is not the approved Unity version.");
            if (!string.Equals(candidate.CandidateSha256, CanonicalJson.Sha256Hex(value.WithoutTopLevelProperty("candidateSha256")), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate self hash is invalid.");
            }

            return candidate;
        }

        private static SourceManifestDocument ReadSourceManifest(string path, CandidateDocument candidate)
        {
            var value = ReadCanonicalDocument(path);
            RequireExactKeys(value, "candidateId", "candidateSha256", "files", "packageLockSha256", "schema", "sourceCommit", "sourceManifestSha256", "sourceTreeSha256");
            var sourceManifest = new SourceManifestDocument
            {
                CandidateId = RequireString(value, "candidateId"),
                CandidateSha256 = RequireLowerSha256(value, "candidateSha256"),
                Files = ReadSourceFiles(RequireArray(value, "files")),
                PackageLockSha256 = RequireLowerSha256(value, "packageLockSha256"),
                SourceCommit = RequireLowerHex(value, "sourceCommit", 40),
                SourceManifestSha256 = RequireLowerSha256(value, "sourceManifestSha256"),
                SourceTreeSha256 = RequireLowerSha256(value, "sourceTreeSha256")
            };
            if (!string.Equals(RequireString(value, "schema"), EvidenceContracts.SourceManifest, StringComparison.Ordinal)) throw new InvalidOperationException("Source manifest schema is invalid.");
            if (!string.Equals(sourceManifest.CandidateId, candidate.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(sourceManifest.CandidateSha256, candidate.CandidateSha256, StringComparison.Ordinal) ||
                !string.Equals(sourceManifest.SourceCommit, candidate.SourceCommit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source manifest does not bind to its candidate.");
            }

            if (!string.Equals(sourceManifest.SourceTreeSha256, ComputeSourceTreeSha256(sourceManifest.Files), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source tree hash is invalid.");
            }

            if (!string.Equals(sourceManifest.PackageLockSha256, FindSourceFile(sourceManifest.Files, PackageLockRelativePath).Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source manifest package lock hash is invalid.");
            }

            if (!string.Equals(sourceManifest.SourceManifestSha256, CanonicalJson.Sha256Hex(value.WithoutTopLevelProperty("sourceManifestSha256")), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source manifest self hash is invalid.");
            }

            return sourceManifest;
        }

        private static TransitionDocument ReadTransition(CanonicalJsonValue value, CandidateDocument candidate, SourceManifestDocument sourceManifest, int expectedSequence, string expectedPreviousEntrySha256)
        {
            RequireExactKeys(value, "candidateId", "entrySha256", "event", "occurredUtc", "previousEntrySha256", "refs", "schema", "seq");
            if (!string.Equals(RequireString(value, "schema"), TransitionSchema, StringComparison.Ordinal)) throw new InvalidOperationException("Transition schema is invalid.");

            var eventName = RequireString(value, "event");
            var references = ReadReferences(RequireObject(value, "refs"), eventName);
            var document = new TransitionDocument
            {
                CandidateId = RequireString(value, "candidateId"),
                EntrySha256 = RequireLowerSha256(value, "entrySha256"),
                EventName = eventName,
                OccurredUtc = RequireString(value, "occurredUtc"),
                PreviousEntrySha256 = ReadNullableLowerSha256(value, "previousEntrySha256"),
                References = references,
                Sequence = RequirePositiveInteger(value, "seq")
            };
            if (!string.Equals(document.CandidateId, candidate.CandidateId, StringComparison.Ordinal)) throw new InvalidOperationException("Transition candidate identifier is invalid.");
            if (document.Sequence != expectedSequence) throw new InvalidOperationException("Transition sequence is invalid.");
            RequireUtc(document.OccurredUtc, "transition occurredUtc");

            if (expectedPreviousEntrySha256 == null)
            {
                if (document.PreviousEntrySha256 != null) throw new InvalidOperationException("Initial transition cannot have a previous entry hash.");
            }
            else if (!string.Equals(document.PreviousEntrySha256, expectedPreviousEntrySha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Transition previous entry hash is invalid.");
            }

            if (!string.Equals(document.EntrySha256, CanonicalJson.Sha256Hex(value.WithoutTopLevelProperty("entrySha256")), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Transition self hash is invalid.");
            }

            return document;
        }

        private static void ValidateTransitionReferences(string candidateDirectory, CandidateDocument candidate, SourceManifestDocument sourceManifest, TransitionDocument entry)
        {
            var expectedReferences = ReadReferencesForEvent(candidateDirectory, entry.EventName, candidate, sourceManifest);
            foreach (var reference in expectedReferences)
            {
                RequireReference(entry, reference.Key, reference.Value);
            }

            if (entry.EventName == "TESTS_PASSED" || entry.EventName == "TESTS_FAILED")
            {
                var derived = ValidateTestResults(candidateDirectory, candidate, sourceManifest) ? "TESTS_PASSED" : "TESTS_FAILED";
                if (!string.Equals(entry.EventName, derived, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Test transition does not match the validated test outcome.");
                }
            }
            else if (entry.EventName == "MACHINE_READY" || entry.EventName == "MACHINE_REWORK")
            {
                var evidenceManifestSha256 = ReadValidatedEvidenceManifest(candidateDirectory, candidate, sourceManifest);
                var derived = ReadValidatedValidatorReport(candidateDirectory, candidate, evidenceManifestSha256);
                if (!string.Equals(entry.EventName, derived, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Terminal transition does not match the validated validator outcome.");
                }
            }
        }

        private static string DeriveNextEvent(string candidateDirectory, ValidatedChain chain)
        {
            var previousEventName = chain.Entries[chain.Entries.Count - 1].EventName;
            switch (previousEventName)
            {
                case "SOURCE_SEALED":
                    return ValidateTestResults(candidateDirectory, chain.Candidate, chain.SourceManifest) ? "TESTS_PASSED" : "TESTS_FAILED";
                case "TESTS_PASSED":
                case "TESTS_FAILED":
                    ReadValidatedBuildManifest(candidateDirectory, chain.Candidate, chain.SourceManifest, out _, out _);
                    return "BUILD_SEALED";
                case "BUILD_SEALED":
                    ReadValidatedEvidenceManifest(candidateDirectory, chain.Candidate, chain.SourceManifest);
                    return "EVIDENCE_SEALED";
                case "EVIDENCE_SEALED":
                    var evidenceManifestSha256 = ReadValidatedEvidenceManifest(candidateDirectory, chain.Candidate, chain.SourceManifest);
                    var terminalEvent = ReadValidatedValidatorReport(candidateDirectory, chain.Candidate, evidenceManifestSha256);
                    if (terminalEvent == "MACHINE_READY" && chain.Entries.Count >= 2 && chain.Entries[1].EventName != "TESTS_PASSED")
                    {
                        throw new InvalidOperationException("A candidate with failed tests cannot derive MACHINE_READY.");
                    }

                    return terminalEvent;
                default:
                    throw new InvalidOperationException("The candidate already has a terminal machine event.");
            }
        }

        private static Dictionary<string, string> ReadReferencesForEvent(string candidateDirectory, string eventName, CandidateDocument candidate, SourceManifestDocument sourceManifest)
        {
            var references = new Dictionary<string, string>(StringComparer.Ordinal);
            switch (eventName)
            {
                case "SOURCE_SEALED":
                    references.Add("candidateSha256", candidate.CandidateSha256);
                    references.Add("sourceManifestSha256", sourceManifest.SourceManifestSha256);
                    return references;
                case "TESTS_PASSED":
                case "TESTS_FAILED":
                    ValidateTestResults(candidateDirectory, candidate, sourceManifest);
                    for (var index = 0; index < TestReferenceNames.Length; index++)
                    {
                        references.Add(TestReferenceNames[index], GetCanonicalFileSha256(candidateDirectory, TestResultPaths[index]));
                    }

                    return references;
                case "BUILD_SEALED":
                    string fileSetSha256;
                    ReadValidatedBuildManifest(candidateDirectory, candidate, sourceManifest, out _, out fileSetSha256);
                    references.Add("buildManifestSha256", GetCanonicalFileSha256(candidateDirectory, BuildManifestFileName));
                    references.Add("fileSetSha256", fileSetSha256);
                    return references;
                case "EVIDENCE_SEALED":
                    ReadValidatedEvidenceManifest(candidateDirectory, candidate, sourceManifest);
                    references.Add("evidenceManifestSha256", GetCanonicalFileSha256(candidateDirectory, EvidenceManifestFileName));
                    return references;
                case "MACHINE_READY":
                case "MACHINE_REWORK":
                    var evidenceManifestHash = ReadValidatedEvidenceManifest(candidateDirectory, candidate, sourceManifest);
                    ReadValidatedValidatorReport(candidateDirectory, candidate, evidenceManifestHash);
                    references.Add("evidenceManifestSha256", GetCanonicalFileSha256(candidateDirectory, EvidenceManifestFileName));
                    references.Add("validatorReportSha256", GetCanonicalFileSha256(candidateDirectory, ValidatorReportFileName));
                    return references;
                default:
                    throw new InvalidOperationException("Transition event is invalid.");
            }
        }

        private static bool ValidateTestResults(string candidateDirectory, CandidateDocument candidate, SourceManifestDocument sourceManifest)
        {
            var allPassed = true;
            foreach (var expectation in TestResultExpectations)
            {
                if (!ReadValidatedTestResult(candidateDirectory, candidate, sourceManifest, expectation)) allPassed = false;
            }

            return allPassed;
        }

        private static bool ReadValidatedTestResult(string candidateDirectory, CandidateDocument candidate, SourceManifestDocument sourceManifest, TestResultExpectation expectation)
        {
            var result = ReadCanonicalDocument(GetPathInsideDirectory(candidateDirectory, expectation.ResultPath));
            RequireExactKeys(result, "candidateId", "criterionIds", "payload", "payloadType", "producedUtc", "producer", "rawArtifact", "schema", "sourceManifestSha256", "status");
            if (!string.Equals(RequireString(result, "schema"), "overbless.source-result/v1", StringComparison.Ordinal) ||
                !string.Equals(RequireString(result, "candidateId"), candidate.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(RequireLowerSha256(result, "sourceManifestSha256"), sourceManifest.SourceManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(RequireString(result, "producer"), expectation.Producer, StringComparison.Ordinal) ||
                !string.Equals(RequireString(result, "payloadType"), expectation.PayloadType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Test result identity or producer contract is invalid: " + expectation.ResultPath);
            }

            RequireUtc(RequireString(result, "producedUtc"), "test result producedUtc");
            ValidateExactCriteria(RequireArray(result, "criterionIds"), expectation.CriterionIds, expectation.ResultPath);
            ValidateRawArtifactReference(candidateDirectory, RequireObject(result, "rawArtifact"), expectation.RawPath);

            var status = RequireResultStatus(result);
            var payloadPassed = ValidateTestPayload(candidateDirectory, sourceManifest, RequireObject(result, "payload"), expectation, status);
            return payloadPassed;
        }

        private static bool ValidateTestPayload(string candidateDirectory, SourceManifestDocument sourceManifest, CanonicalJsonValue payload, TestResultExpectation expectation, string resultStatus)
        {
            switch (expectation.PayloadType)
            {
                case "NUnitSuite":
                    RequireValidationSuccess(EvidenceSchemaValidator.ValidateSchemaObject(
                        payload,
                        expectation.PayloadSchema,
                        new[] { "schema", "suite", "total", "passed", "failed", "skipped", "exitCode", "failureSummary" }),
                        "NUnit payload");
                    if (string.IsNullOrEmpty(expectation.NUnitSuite) ||
                        expectation.NUnitTestFullNames.Count == 0 ||
                        !string.Equals(RequireString(payload, "suite"), expectation.NUnitSuite, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("NUnit payload does not bind the required suite.");
                    }
                    var total = RequireNonNegativeInteger(payload, "total");
                    var passed = RequireNonNegativeInteger(payload, "passed");
                    var failed = RequireNonNegativeInteger(payload, "failed");
                    var skipped = RequireNonNegativeInteger(payload, "skipped");
                    var exitCode = RequireNonNegativeInteger(payload, "exitCode");
                    var failureSummary = RequireStringValue(payload, "failureSummary");
                    if (checked(passed + failed + skipped) != total) throw new InvalidOperationException("NUnit payload totals are inconsistent.");

                    long rawTotal;
                    long rawPassed;
                    long rawFailed;
                    long rawSkipped;
                    ReadNUnitCounts(candidateDirectory, expectation.RawPath, expectation, out rawTotal, out rawPassed, out rawFailed, out rawSkipped);
                    if (total != rawTotal || passed != rawPassed || failed != rawFailed || skipped != rawSkipped)
                    {
                        throw new InvalidOperationException("NUnit payload does not match the raw test result.");
                    }

                    var nunitPassed = total > 0 && failed == 0 && exitCode == 0 && string.IsNullOrEmpty(failureSummary);
                    RequireResultStatusMatchesPayload(resultStatus, nunitPassed);
                    return nunitPassed;

                case "ProjectConfigSnapshot":
                    RequireValidationSuccess(EvidenceSchemaValidator.ValidateSchemaObject(
                        payload,
                        expectation.PayloadSchema,
                        new[] { "schema", "unityVersion", "directPackages", "packageLockSha256", "renderer", "input", "addressablesPresent", "scene", "buildSettings", "displayPolicy", "snapshotStatus", "failureCodes" }),
                        "Project configuration payload");
                    RequireCanonicalRawPayloadMatches(candidateDirectory, expectation.RawPath, payload);
                    var projectConfigPassed = IsApprovedProjectConfig(payload, sourceManifest);
                    RequireResultStatusMatchesPayload(RequireResultStatus(payload, "snapshotStatus"), projectConfigPassed);
                    RequireResultStatusMatchesPayload(resultStatus, projectConfigPassed);
                    return projectConfigPassed;

                case "ScopeAudit":
                    RequireValidationSuccess(EvidenceSchemaValidator.ValidateSchemaObject(
                        payload,
                        expectation.PayloadSchema,
                        new[] { "schema", "scannedRoots", "forbiddenTokens", "allowlist", "matches", "auditStatus" }),
                        "Scope-audit payload");
                    RequireCanonicalRawPayloadMatches(candidateDirectory, expectation.RawPath, payload);
                    var scopeAuditPassed = IsApprovedScopeAudit(payload, sourceManifest);
                    RequireResultStatusMatchesPayload(RequireResultStatus(payload, "auditStatus"), scopeAuditPassed);
                    RequireResultStatusMatchesPayload(resultStatus, scopeAuditPassed);
                    return scopeAuditPassed;

                default:
                    throw new InvalidOperationException("Test payload type is invalid.");
            }
        }

        private static bool IsApprovedProjectConfig(CanonicalJsonValue payload, SourceManifestDocument sourceManifest)
        {
            var failureCodes = RequireArray(payload, "failureCodes");
            var directPackages = RequireArray(payload, "directPackages");
            var packagesApproved = true;
            foreach (var package in directPackages.Items)
            {
                var packageName = RequireString(package, "name");
                if (string.Equals(packageName, "com.unity.addressables", StringComparison.Ordinal)) packagesApproved = false;
            }

            return string.Equals(RequireString(payload, "unityVersion"), RequiredUnityVersion, StringComparison.Ordinal) &&
                string.Equals(RequireString(payload, "packageLockSha256"), sourceManifest.PackageLockSha256, StringComparison.Ordinal) &&
                string.Equals(RequireString(payload, "renderer"), "URP2D", StringComparison.Ordinal) &&
                string.Equals(RequireString(payload, "input"), "InputSystem", StringComparison.Ordinal) &&
                !RequireBoolean(payload, "addressablesPresent") &&
                string.Equals(RequireString(payload, "scene"), RequiredScenePath, StringComparison.Ordinal) &&
                failureCodes.Items.Count == 0 &&
                packagesApproved &&
                IsApprovedProjectBuildSettings(RequireObject(payload, "buildSettings")) &&
                IsApprovedDisplayPolicy(RequireObject(payload, "displayPolicy"));
        }

        private static bool IsApprovedProjectBuildSettings(CanonicalJsonValue settings)
        {
            var scenes = RequireArray(settings, "scenes");
            return RequireBoolean(settings, "development") &&
                !RequireBoolean(settings, "autoconnectProfiler") &&
                !RequireBoolean(settings, "deepProfiling") &&
                !RequireBoolean(settings, "decompressionFallback") &&
                string.Equals(RequireString(settings, "compressionFormat"), "Disabled", StringComparison.Ordinal) &&
                string.Equals(RequireString(settings, "exceptionSupport"), "ExplicitlyThrownExceptionsOnly", StringComparison.Ordinal) &&
                RequirePositiveInteger(settings, "memorySizeMb") > 0 &&
                string.Equals(RequireString(settings, "target"), "WebGL", StringComparison.Ordinal) &&
                scenes.Items.Count == 1 &&
                scenes.Items[0].Kind == CanonicalJsonKind.String &&
                string.Equals(scenes.Items[0].StringValue, RequiredScenePath, StringComparison.Ordinal);
        }

        private static bool IsApprovedDisplayPolicy(CanonicalJsonValue displayPolicy)
        {
            return RequireBoolean(displayPolicy, "letterboxNon16x9") &&
                RequireBoolean(displayPolicy, "sameWorldBounds") &&
                string.Equals(RequireString(displayPolicy, "canvasScaleMode"), "ScaleWithScreenSize", StringComparison.Ordinal) &&
                RequirePositiveInteger(displayPolicy, "aspectNumerator") == 16 &&
                RequirePositiveInteger(displayPolicy, "aspectDenominator") == 9 &&
                RequirePositiveInteger(displayPolicy, "designWidth") == 1920 &&
                RequirePositiveInteger(displayPolicy, "designHeight") == 1080 &&
                RequirePositiveInteger(displayPolicy, "minimumWidth") == 1280 &&
                RequirePositiveInteger(displayPolicy, "minimumHeight") == 720;
        }

        private static bool IsApprovedScopeAudit(CanonicalJsonValue payload, SourceManifestDocument sourceManifest)
        {
            ValidateExactCriteria(RequireArray(payload, "scannedRoots"), RequiredScopeRoots, "scope-audit scanned roots");
            ValidateExactCriteria(RequireArray(payload, "forbiddenTokens"), ScopeAudit.ForbiddenGameplayTokens, "scope-audit forbidden tokens");

            if (RequireArray(payload, "allowlist").Items.Count != 0)
            {
                throw new InvalidOperationException("Candidate scope audit allowances require a separately sealed external approval contract.");
            }

            var derivedMatches = DeriveScopeAuditMatches(sourceManifest);
            var reportedMatches = RequireArray(payload, "matches");
            if (reportedMatches.Items.Count != derivedMatches.Count)
            {
                throw new InvalidOperationException("Scope audit does not report the complete sealed-source match set.");
            }

            for (var index = 0; index < reportedMatches.Items.Count; index++)
            {
                var reported = reportedMatches.Items[index];
                var derived = derivedMatches[index];
                RequireExactKeys(reported, "allowlisted", "approvalReference", "column", "line", "path", "sourceSha256", "token");
                if (RequireBoolean(reported, "allowlisted") ||
                    RequireNullableString(reported, "approvalReference") != null ||
                    !string.Equals(RequireString(reported, "path"), derived.Path, StringComparison.Ordinal) ||
                    !string.Equals(RequireString(reported, "token"), derived.Token, StringComparison.Ordinal) ||
                    !string.Equals(RequireLowerSha256(reported, "sourceSha256"), derived.SourceSha256, StringComparison.Ordinal) ||
                    RequirePositiveInteger(reported, "line") != derived.Line ||
                    RequirePositiveInteger(reported, "column") != derived.Column)
                {
                    throw new InvalidOperationException("Scope audit match does not rederive from sealed source bytes.");
                }
            }

            return derivedMatches.Count == 0;
        }

        private static List<SealedScopeAuditMatch> DeriveScopeAuditMatches(SourceManifestDocument sourceManifest)
        {
            var projectRoot = GetProjectRoot();
            var matches = new List<SealedScopeAuditMatch>();
            foreach (var source in sourceManifest.Files)
            {
                if (!IsGovernedScopePath(source.Path)) continue;

                var text = ReadSealedScopeAuditText(projectRoot, source);
                foreach (var token in ScopeAudit.ForbiddenGameplayTokens)
                {
                    foreach (Match match in ScopeAudit.FindForbiddenTokenMatches(text, token))
                    {
                        int line;
                        int column;
                        GetScopeLineAndColumn(text, match.Index, out line, out column);
                        matches.Add(new SealedScopeAuditMatch(source.Path, token, source.Sha256, line, column));
                    }
                }
            }

            matches.Sort(CompareScopeAuditMatches);
            return matches;
        }

        private static bool IsGovernedScopePath(string path)
        {
            var inRoot = false;
            foreach (var root in RequiredScopeRoots)
            {
                if (path.StartsWith(root + "/", StringComparison.Ordinal))
                {
                    inRoot = true;
                    break;
                }
            }

            if (!inRoot) return false;

            foreach (var excludedPath in RequiredScopeExclusions)
            {
                if (string.Equals(path, excludedPath, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadSealedScopeAuditText(string projectRoot, SourceFile source)
        {
            var path = GetPathInsideDirectory(projectRoot, source.Path);
            EnsurePathHasNoReparsePoints(projectRoot, source.Path);
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != source.Size || !string.Equals(CanonicalJson.Sha256Hex(bytes), source.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Scope audit source bytes do not match the sealed source manifest: " + source.Path + ".");
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Scope audit sealed source is not valid UTF-8: " + source.Path + ".", exception);
            }
        }

        private static void GetScopeLineAndColumn(string text, int index, out int line, out int column)
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

        private static int CompareScopeAuditMatches(SealedScopeAuditMatch left, SealedScopeAuditMatch right)
        {
            var path = CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path);
            if (path != 0) return path;
            var line = left.Line.CompareTo(right.Line);
            if (line != 0) return line;
            var column = left.Column.CompareTo(right.Column);
            if (column != 0) return column;
            return CanonicalJson.CompareUtf8Ordinal(left.Token, right.Token);
        }

        private sealed class SealedScopeAuditMatch
        {
            public SealedScopeAuditMatch(string path, string token, string sourceSha256, int line, int column)
            {
                Path = path;
                Token = token;
                SourceSha256 = sourceSha256;
                Line = line;
                Column = column;
            }

            public string Path { get; }
            public string Token { get; }
            public string SourceSha256 { get; }
            public int Line { get; }
            public int Column { get; }
        }

        private static void RequireCanonicalRawPayloadMatches(string candidateDirectory, string rawPath, CanonicalJsonValue payload)
        {
            var rawBytes = File.ReadAllBytes(GetPathInsideDirectory(candidateDirectory, rawPath));
            CanonicalJsonValue rawPayload;
            string error;
            if (!CanonicalJson.TryParseCanonicalUtf8(rawBytes, out rawPayload, out error) || rawPayload.Kind != CanonicalJsonKind.Object ||
                !CanonicalJson.ByteArraysEqual(rawBytes, CanonicalJson.SerializeUtf8(payload)))
            {
                throw new InvalidOperationException("Structured payload does not match its canonical raw artifact: " + rawPath + ".");
            }
        }

        private static void ReadNUnitCounts(string candidateDirectory, string rawPath, TestResultExpectation expectation, out long total, out long passed, out long failed, out long skipped)
        {
            total = 0;
            passed = 0;
            failed = 0;
            skipped = 0;
            var path = GetPathInsideDirectory(candidateDirectory, rawPath);
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    var document = XDocument.Load(reader, LoadOptions.None);
                    var testRun = document.Root;
                    if (testRun == null ||
                        !string.Equals(testRun.Name.LocalName, "test-run", StringComparison.Ordinal) ||
                        !string.Equals((string)testRun.Attribute("result"), "Passed", StringComparison.Ordinal) ||
                        !TryReadXmlCount(testRun, "total", out total) ||
                        !TryReadXmlCount(testRun, "passed", out passed) ||
                        !TryReadXmlCount(testRun, "failed", out failed) ||
                        !TryReadXmlCount(testRun, "skipped", out skipped) ||
                        total != passed + failed + skipped ||
                        !ValidateNoFailedNUnitNodes(testRun) ||
                        !ValidateNUnitSuite(testRun, expectation))
                    {
                        throw new InvalidOperationException("NUnit raw artifact has invalid aggregate counts.");
                    }
                }
            }
            catch (XmlException exception)
            {
                throw new InvalidOperationException("NUnit raw artifact is invalid XML.", exception);
            }
        }

        private static bool ValidateNUnitSuite(XElement testRun, TestResultExpectation expectation)
        {
            XElement suite = null;
            foreach (var candidate in testRun.Descendants())
            {
                var fullName = candidate.Attribute("fullname");
                if (string.Equals(candidate.Name.LocalName, "test-suite", StringComparison.Ordinal) &&
                    fullName != null &&
                    string.Equals(fullName.Value, expectation.NUnitSuite, StringComparison.Ordinal))
                {
                    if (suite != null) throw new InvalidOperationException("NUnit raw artifact contains the required suite more than once.");
                    suite = candidate;
                }
            }

            if (suite == null ||
                !string.Equals((string)suite.Attribute("type"), "TestSuite", StringComparison.Ordinal) ||
                !string.Equals((string)suite.Attribute("result"), "Passed", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("NUnit raw artifact does not contain the required passing suite.");
            }

            var expected = new HashSet<string>(expectation.NUnitTestFullNames, StringComparer.Ordinal);
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var testCase in suite.Descendants())
            {
                if (!string.Equals(testCase.Name.LocalName, "test-case", StringComparison.Ordinal)) continue;

                var fullName = testCase.Attribute("fullname");
                if (fullName == null || !expected.Contains(fullName.Value)) continue;
                if (!string.Equals((string)testCase.Attribute("result"), "Passed", StringComparison.Ordinal) ||
                    !found.Add(fullName.Value))
                {
                    throw new InvalidOperationException("NUnit required test case did not pass exactly once.");
                }
            }

            if (!found.SetEquals(expected)) throw new InvalidOperationException("NUnit raw artifact is missing a required test case.");
            return true;
        }

        private static bool ValidateNoFailedNUnitNodes(XElement testRun)
        {
            foreach (var element in testRun.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "test-suite", StringComparison.Ordinal) &&
                    !string.Equals(element.Name.LocalName, "test-case", StringComparison.Ordinal))
                {
                    continue;
                }

                var result = (string)element.Attribute("result");
                if (string.Equals(result, "Failed", StringComparison.Ordinal) ||
                    string.Equals(result, "Error", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("NUnit raw artifact contains a failed suite or test case.");
                }
            }

            return true;
        }

        private static bool TryReadXmlCount(XElement element, string name, out long value)
        {
            value = 0;
            var attribute = element.Attribute(name);
            return attribute != null &&
                long.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                value >= 0;
        }

        private static void ValidateRawArtifactReference(string candidateDirectory, CanonicalJsonValue reference, string expectedPath)
        {
            RequireExactKeys(reference, "path", "sha256", "size");
            if (!string.Equals(RequireString(reference, "path"), expectedPath, StringComparison.Ordinal)) throw new InvalidOperationException("Test raw artifact path is invalid.");
            var expectedSize = RequireNonNegativeInteger(reference, "size");
            var expectedSha256 = RequireLowerSha256(reference, "sha256");
            var path = GetPathInsideDirectory(candidateDirectory, expectedPath);
            EnsurePathHasNoReparsePoints(candidateDirectory, expectedPath);
            var info = new FileInfo(path);
            if (!info.Exists || IsReparsePoint(path) || info.Length != expectedSize || !string.Equals(Sha256File(path), expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Test raw artifact bytes do not match their reference: " + expectedPath);
            }
        }

        private static void ReadValidatedBuildManifest(string candidateDirectory, CandidateDocument candidate, SourceManifestDocument sourceManifest, out string buildManifestSha256, out string fileSetSha256)
        {
            var manifest = ReadCanonicalDocument(GetPathInsideDirectory(candidateDirectory, BuildManifestFileName));
            RequireExactKeys(manifest, "buildManifestSha256", "candidateId", "fileSetSha256", "files", "schema", "servedRootManifestSha256", "settings", "sourceCapabilitySha256", "sourceManifestSha256");
            var sourceCapabilitySha256 = RequireLowerSha256(manifest, "sourceCapabilitySha256");
            var expectedSourceCapabilitySha256 = ComputeCandidateSourceCapabilitySha256(
                candidate.CandidateId,
                candidate.SourceCommit,
                sourceManifest.SourceManifestSha256,
                ValidateMaterializedUnityInputs(GetProjectRoot(), sourceManifest.Files));
            if (!string.Equals(RequireString(manifest, "schema"), EvidenceContracts.BuildManifest, StringComparison.Ordinal) ||
                !string.Equals(RequireString(manifest, "candidateId"), candidate.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(RequireLowerSha256(manifest, "sourceManifestSha256"), sourceManifest.SourceManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(sourceCapabilitySha256, expectedSourceCapabilitySha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Build manifest identity or source capability is invalid.");
            }

            buildManifestSha256 = RequireLowerSha256(manifest, "buildManifestSha256");
            fileSetSha256 = RequireLowerSha256(manifest, "fileSetSha256");
            RequireLowerSha256(manifest, "servedRootManifestSha256");
            if (!string.Equals(buildManifestSha256, CanonicalJson.Sha256Hex(manifest.WithoutTopLevelProperty("buildManifestSha256")), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Build manifest self hash is invalid.");
            }

            ValidateBuildSettings(RequireObject(manifest, "settings"), candidate);
            ValidateBuildFiles(candidateDirectory, RequireArray(manifest, "files"), fileSetSha256);
        }

        private static void ValidateBuildSettings(CanonicalJsonValue settings, CandidateDocument candidate)
        {
            RequireExactKeys(settings, "autoconnectProfiler", "compressionFormat", "decompressionFallback", "deepProfiling", "development", "exceptionSupport", "memorySizeMb", "scene", "target", "unityVersion");
            if (!RequireBoolean(settings, "development") ||
                RequireBoolean(settings, "autoconnectProfiler") ||
                RequireBoolean(settings, "deepProfiling") ||
                RequireBoolean(settings, "decompressionFallback") ||
                !string.Equals(RequireString(settings, "compressionFormat"), "Disabled", StringComparison.Ordinal) ||
                !string.Equals(RequireString(settings, "exceptionSupport"), "ExplicitlyThrownExceptionsOnly", StringComparison.Ordinal) ||
                RequirePositiveInteger(settings, "memorySizeMb") <= 0 ||
                !string.Equals(RequireString(settings, "target"), "WebGL", StringComparison.Ordinal) ||
                !string.Equals(RequireString(settings, "scene"), RequiredScenePath, StringComparison.Ordinal) ||
                !string.Equals(RequireString(settings, "unityVersion"), candidate.UnityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Build settings do not satisfy the approved WebGL contract.");
            }
        }

        private static void ValidateBuildFiles(string candidateDirectory, CanonicalJsonValue files, string expectedFileSetSha256)
        {
            var buildRoot = GetPathInsideDirectory(candidateDirectory, "build");
            if (!Directory.Exists(buildRoot) || IsReparsePoint(buildRoot)) throw new InvalidOperationException("Materialized build root is missing or unsafe.");
            if (files.Items.Count == 0) throw new InvalidOperationException("Build manifest files are required.");

            string previousPath = null;
            var declaredPaths = new HashSet<string>(StringComparer.Ordinal);
            var hasIndex = false;
            var hasWasm = false;
            var hasData = false;
            foreach (var file in files.Items)
            {
                RequireExactKeys(file, "path", "sha256", "size");
                var path = RequireString(file, "path");
                var sha256 = RequireLowerSha256(file, "sha256");
                var size = RequireNonNegativeLong(file, "size");
                if (!CanonicalJson.IsNormalizedRelativePath(path) ||
                    (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, path) >= 0) ||
                    !declaredPaths.Add(path))
                {
                    throw new InvalidOperationException("Build manifest file inventory is invalid.");
                }

                ValidateMaterializedFile(buildRoot, path, size, sha256);
                hasIndex |= string.Equals(path, "index.html", StringComparison.Ordinal);
                hasWasm |= path.EndsWith(".wasm", StringComparison.Ordinal);
                hasData |= path.EndsWith(".data", StringComparison.Ordinal);
                previousPath = path;
            }

            if (!hasIndex || !hasWasm || !hasData) throw new InvalidOperationException("Build manifest is missing a required materialized WebGL deliverable.");
            ValidateCompleteBuildInventory(buildRoot, declaredPaths);
            if (!string.Equals(CanonicalJson.Sha256Hex(files), expectedFileSetSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Build manifest file-set hash is invalid.");
            }
        }

        private static void ValidateMaterializedFile(string root, string relativePath, long expectedSize, string expectedSha256)
        {
            var path = GetPathInsideDirectory(root, relativePath);
            EnsurePathHasNoReparsePoints(root, relativePath);
            if (!File.Exists(path) || IsReparsePoint(path)) throw new InvalidOperationException("Materialized build file is missing or unsafe: " + relativePath);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long actualSize;
                var actualSha256 = CanonicalJson.Sha256Hex(stream, out actualSize);
                if (stream.Length != actualSize || actualSize != expectedSize || !string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Materialized build file does not match its manifest entry: " + relativePath);
                }
            }
        }

        private static void ValidateCompleteBuildInventory(string buildRoot, ISet<string> declaredPaths)
        {
            var directories = new Stack<string>();
            directories.Push(buildRoot);
            while (directories.Count > 0)
            {
                var directory = directories.Pop();
                if (IsReparsePoint(directory)) throw new InvalidOperationException("Materialized build contains a reparse-point directory.");

                foreach (var childDirectory in Directory.GetDirectories(directory))
                {
                    if (IsReparsePoint(childDirectory)) throw new InvalidOperationException("Materialized build contains a reparse-point directory.");
                    directories.Push(childDirectory);
                }

                foreach (var file in Directory.GetFiles(directory))
                {
                    if (IsReparsePoint(file)) throw new InvalidOperationException("Materialized build contains a reparse-point file.");
                    var relativePath = ToRelativePath(buildRoot, file);
                    if (string.Equals(relativePath, BuildManifestFileName.Substring("build/".Length), StringComparison.Ordinal)) continue;
                    if (!declaredPaths.Contains(relativePath)) throw new InvalidOperationException("Materialized build contains an undeclared file: " + relativePath);
                }
            }
        }

        private static string ToRelativePath(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root);
            var normalizedPath = Path.GetFullPath(path);
            var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!normalizedPath.StartsWith(rootWithSeparator, comparison)) throw new InvalidOperationException("Materialized build path escapes its root.");
            var relativePath = normalizedPath.Substring(rootWithSeparator.Length).Replace('\\', '/');
            if (!CanonicalJson.IsNormalizedRelativePath(relativePath)) throw new InvalidOperationException("Materialized build path is not normalized.");
            return relativePath;
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        private static void EnsurePathHasNoReparsePoints(string root, string relativePath)
        {
            var current = Path.GetFullPath(root);
            if (IsReparsePoint(current)) throw new InvalidOperationException("Artifact root is a reparse point.");

            foreach (var segment in NormalizeRelativePath(relativePath).Split('/'))
            {
                current = Path.Combine(current, segment);
                if (IsReparsePoint(current)) throw new InvalidOperationException("Artifact path contains a reparse point: " + relativePath);
            }
        }

        private static string ReadValidatedEvidenceManifest(string candidateDirectory, CandidateDocument candidate, SourceManifestDocument sourceManifest)
        {
            string buildManifestSha256;
            string ignoredFileSetSha256;
            ReadValidatedBuildManifest(candidateDirectory, candidate, sourceManifest, out buildManifestSha256, out ignoredFileSetSha256);

            var manifest = ReadCanonicalDocument(GetPathInsideDirectory(candidateDirectory, EvidenceManifestFileName));
            RequireExactKeys(manifest, "artifacts", "buildManifestSha256", "candidateId", "evidenceManifestSha256", "generatedUtc", "requiredCriterionIds", "schema", "sourceManifestSha256");
            if (!string.Equals(RequireString(manifest, "schema"), EvidenceContracts.EvidenceManifest, StringComparison.Ordinal) ||
                !string.Equals(RequireString(manifest, "candidateId"), candidate.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(RequireLowerSha256(manifest, "sourceManifestSha256"), sourceManifest.SourceManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(RequireLowerSha256(manifest, "buildManifestSha256"), buildManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Evidence manifest identity is invalid.");
            }

            RequireUtc(RequireString(manifest, "generatedUtc"), "evidence manifest generatedUtc");
            var evidenceManifestSha256 = RequireLowerSha256(manifest, "evidenceManifestSha256");
            if (!string.Equals(evidenceManifestSha256, CanonicalJson.Sha256Hex(manifest.WithoutTopLevelProperty("evidenceManifestSha256")), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Evidence manifest self hash is invalid.");
            }

            RequireValidationSuccess(EvidenceSchemaValidator.ValidateCriteria(RequireArray(manifest, "requiredCriterionIds").Items, true), "Evidence manifest criteria");
            ValidateEvidenceArtifactInventory(candidateDirectory, RequireArray(manifest, "artifacts"));
            return evidenceManifestSha256;
        }

        private static void ValidateEvidenceArtifactInventory(string candidateDirectory, CanonicalJsonValue artifacts)
        {
            if (artifacts.Items.Count != 66) throw new InvalidOperationException("Evidence manifest must contain exactly 66 artifacts.");
            string previousPath = null;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var artifact in artifacts.Items)
            {
                RequireExactKeys(artifact, "criterionIds", "path", "role", "sha256", "size");
                var path = RequireString(artifact, "path");
                var role = RequireString(artifact, "role");
                var size = RequireNonNegativeInteger(artifact, "size");
                var sha256 = RequireLowerSha256(artifact, "sha256");
                if (!CanonicalJson.IsNormalizedRelativePath(path) ||
                    (role != "RAW" && role != "SOURCE_RESULT" && role != "BUILD_RESULT" && role != "CAPTURE_MANIFEST") ||
                    (previousPath != null && CanonicalJson.CompareUtf8Ordinal(previousPath, path) >= 0) ||
                    !paths.Add(path))
                {
                    throw new InvalidOperationException("Evidence artifact inventory is invalid.");
                }

                RequireValidationSuccess(EvidenceSchemaValidator.ValidateCriteria(RequireArray(artifact, "criterionIds").Items, false), "Evidence artifact criteria");
                var artifactPath = GetPathInsideDirectory(candidateDirectory, path);
                EnsurePathHasNoReparsePoints(candidateDirectory, path);
                var info = new FileInfo(artifactPath);
                if (!info.Exists || IsReparsePoint(artifactPath) || info.Length != size || !string.Equals(Sha256File(artifactPath), sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Evidence artifact bytes do not match their inventory entry: " + path);
                }

                previousPath = path;
            }
        }

        private static string ReadValidatedValidatorReport(string candidateDirectory, CandidateDocument candidate, string evidenceManifestSha256)
        {
            var report = ReadCanonicalDocument(GetPathInsideDirectory(candidateDirectory, ValidatorReportFileName));
            RequireExactKeys(report, "candidateId", "checkedCriterionIds", "checks", "evidenceManifestSha256", "generatedUtc", "schema", "status", "validatorReportSha256");
            if (!string.Equals(RequireString(report, "schema"), EvidenceContracts.ValidatorReport, StringComparison.Ordinal) ||
                !string.Equals(RequireString(report, "candidateId"), candidate.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(RequireLowerSha256(report, "evidenceManifestSha256"), evidenceManifestSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Validator report identity is invalid.");
            }

            RequireUtc(RequireString(report, "generatedUtc"), "validator report generatedUtc");
            var reportSha256 = RequireLowerSha256(report, "validatorReportSha256");
            if (!string.Equals(reportSha256, CanonicalJson.Sha256Hex(report.WithoutTopLevelProperty("validatorReportSha256")), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Validator report self hash is invalid.");
            }

            RequireValidationSuccess(EvidenceSchemaValidator.ValidateCriteria(RequireArray(report, "checkedCriterionIds").Items, true), "Validator report criteria");
            var checks = RequireArray(report, "checks");
            RequireValidationSuccess(EvidenceSchemaValidator.ValidateReportChecks(checks), "Validator report checks");
            var allChecksPassed = true;
            foreach (var check in checks.Items)
            {
                if (!string.Equals(RequireString(check, "status"), "PASS", StringComparison.Ordinal)) allChecksPassed = false;
            }

            var status = RequireMachineStatus(report, "status");
            if ((status == "MACHINE_READY") != allChecksPassed)
            {
                throw new InvalidOperationException("Validator report status does not match its check outcomes.");
            }

            var independentlyReady = DeriveMachineReadinessFromFixedArtifacts(candidateDirectory, candidate, evidenceManifestSha256);
            if (!independentlyReady || status != "MACHINE_READY")
            {
                throw new InvalidOperationException(
                    "Terminal reports are accepted only when the complete fixed-artifact derivation is MACHINE_READY with the exact all-PASS check map.");
            }

            return status;
        }
        private static bool DeriveMachineReadinessFromFixedArtifacts(string candidateDirectory, CandidateDocument candidate, string evidenceManifestSha256)
        {
            EnsureGitSnapshotStillClean(GetProjectRoot(), candidate.SourceCommit);
            var derivationRoot = Path.Combine(Path.GetDirectoryName(candidateDirectory), "." + candidate.CandidateId + ".derive-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectoryForSemanticDerivation(candidateDirectory, derivationRoot);
                var report = CreateSyntheticPassingValidatorReport(candidate.CandidateId, evidenceManifestSha256);
                var reportPath = Path.Combine(derivationRoot, ValidatorReportFileName);
                File.WriteAllBytes(reportPath, CanonicalJson.SerializeUtf8(report));

                var reportSha256 = RequireLowerSha256(report, "validatorReportSha256");
                WriteSyntheticTerminalTransition(
                    Path.Combine(candidateDirectory, TransitionLogFileName),
                    Path.Combine(derivationRoot, TransitionLogFileName),
                    candidate.CandidateId,
                    evidenceManifestSha256,
                    reportSha256);

                return M2EntryGateValidator.ValidateCandidateRoot(derivationRoot, candidate.CandidateId, false).IsMachineReady;
            }
            finally
            {
                if (Directory.Exists(derivationRoot)) Directory.Delete(derivationRoot, true);
            }
        }

        private static void CopyDirectoryForSemanticDerivation(string sourceDirectory, string destinationDirectory)
        {
            if (IsReparsePoint(sourceDirectory)) throw new InvalidOperationException("Candidate directory cannot be a reparse point.");
            Directory.CreateDirectory(destinationDirectory);

            foreach (var sourceFile in Directory.GetFiles(sourceDirectory))
            {
                var name = Path.GetFileName(sourceFile);
                if (string.Equals(name, TransitionLogFileName, StringComparison.Ordinal) ||
                    string.Equals(name, ValidatorReportFileName, StringComparison.Ordinal) ||
                    string.Equals(name, "gate-decision.json", StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsReparsePoint(sourceFile)) throw new InvalidOperationException("Candidate semantic derivation refuses reparse-point files.");
                File.Copy(sourceFile, Path.Combine(destinationDirectory, name), false);
            }

            foreach (var sourceChildDirectory in Directory.GetDirectories(sourceDirectory))
            {
                if (IsReparsePoint(sourceChildDirectory)) throw new InvalidOperationException("Candidate semantic derivation refuses reparse-point directories.");
                CopyDirectoryForSemanticDerivation(sourceChildDirectory, Path.Combine(destinationDirectory, Path.GetFileName(sourceChildDirectory)));
            }
        }

        private static CanonicalJsonValue CreateSyntheticPassingValidatorReport(string candidateId, string evidenceManifestSha256)
        {
            var criteria = new List<CanonicalJsonValue>();
            foreach (var criterion in EvidenceContracts.CriterionIds) criteria.Add(CanonicalJsonValue.String(criterion));

            var checks = new List<CanonicalJsonValue>();
            foreach (var checkId in EvidenceContracts.Checks)
            {
                checks.Add(CanonicalJsonValue.Object(
                    Property("checkId", checkId),
                    Property("detailCode", "OK"),
                    Property("status", "PASS")));
            }

            var generatedUtc = FormatUtc(DateTime.UtcNow);
            var unsigned = CanonicalJsonValue.Object(
                Property("schema", EvidenceContracts.ValidatorReport),
                Property("candidateId", candidateId),
                new CanonicalJsonProperty("checkedCriterionIds", CanonicalJsonValue.Array(criteria)),
                new CanonicalJsonProperty("checks", CanonicalJsonValue.Array(checks)),
                Property("evidenceManifestSha256", evidenceManifestSha256),
                Property("generatedUtc", generatedUtc),
                Property("status", "MACHINE_READY"));
            var reportSha256 = CanonicalJson.Sha256Hex(unsigned);
            return CanonicalJsonValue.Object(
                Property("schema", EvidenceContracts.ValidatorReport),
                Property("candidateId", candidateId),
                new CanonicalJsonProperty("checkedCriterionIds", CanonicalJsonValue.Array(criteria)),
                new CanonicalJsonProperty("checks", CanonicalJsonValue.Array(checks)),
                Property("evidenceManifestSha256", evidenceManifestSha256),
                Property("generatedUtc", generatedUtc),
                Property("status", "MACHINE_READY"),
                Property("validatorReportSha256", reportSha256));
        }

        private static void WriteSyntheticTerminalTransition(string sourcePath, string destinationPath, string candidateId, string evidenceManifestSha256, string validatorReportSha256)
        {
            var sourceText = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(sourcePath));
            if (sourceText.Length == 0 || sourceText[sourceText.Length - 1] != '\n') throw new InvalidOperationException("Transition log must end with a newline.");

            var prefix = sourceText;
            var finalLineStart = sourceText.LastIndexOf('\n', sourceText.Length - 2) + 1;
            var finalLine = sourceText.Substring(finalLineStart, sourceText.Length - finalLineStart - 1);
            CanonicalJsonValue finalEntry;
            string error;
            if (!CanonicalJson.TryParseCanonicalUtf8(new UTF8Encoding(false, true).GetBytes(finalLine), out finalEntry, out error) || finalEntry.Kind != CanonicalJsonKind.Object)
            {
                throw new InvalidOperationException("Transition log final entry is invalid: " + error);
            }

            if (IsTerminalEvent(RequireString(finalEntry, "event")))
            {
                prefix = sourceText.Substring(0, finalLineStart);
                if (prefix.Length == 0) throw new InvalidOperationException("Transition log has no nonterminal predecessor.");
                var predecessorStart = prefix.LastIndexOf('\n', prefix.Length - 2) + 1;
                finalLine = prefix.Substring(predecessorStart, prefix.Length - predecessorStart - 1);
                if (!CanonicalJson.TryParseCanonicalUtf8(new UTF8Encoding(false, true).GetBytes(finalLine), out finalEntry, out error) || finalEntry.Kind != CanonicalJsonKind.Object)
                {
                    throw new InvalidOperationException("Transition log predecessor is invalid: " + error);
                }
            }

            if (!string.Equals(RequireString(finalEntry, "event"), "EVIDENCE_SEALED", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Machine derivation requires an EVIDENCE_SEALED predecessor.");
            }

            var sequence = RequirePositiveInteger(finalEntry, "seq") + 1;
            var predecessorSha256 = RequireLowerSha256(finalEntry, "entrySha256");
            var references = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["evidenceManifestSha256"] = evidenceManifestSha256,
                ["validatorReportSha256"] = validatorReportSha256
            };
            var occurredUtc = FormatUtc(DateTime.UtcNow);
            var unsigned = CreateTransitionValue(candidateId, sequence, "MACHINE_READY", occurredUtc, references, predecessorSha256, null);
            var entrySha256 = CanonicalJson.Sha256Hex(unsigned);
            var transition = CreateTransitionValue(candidateId, sequence, "MACHINE_READY", occurredUtc, references, predecessorSha256, entrySha256);
            File.WriteAllText(destinationPath, prefix + CanonicalJson.Serialize(transition) + "\n", new UTF8Encoding(false));
        }

        private static void ValidateExactCriteria(CanonicalJsonValue criteria, IReadOnlyList<string> expected, string context)
        {
            if (criteria.Items.Count != expected.Count) throw new InvalidOperationException("Test criterion count is invalid: " + context);
            for (var index = 0; index < expected.Count; index++)
            {
                if (criteria.Items[index].Kind != CanonicalJsonKind.String || !string.Equals(criteria.Items[index].StringValue, expected[index], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Test criteria are invalid: " + context);
                }
            }
        }


        private static void RequireResultStatusMatchesPayload(string status, bool payloadPassed)
        {
            if ((status == "PASS") != payloadPassed) throw new InvalidOperationException("Test result status does not match its payload outcome.");
        }

        private static string RequireResultStatus(CanonicalJsonValue value, string propertyName = "status")
        {
            var status = RequireString(value, propertyName);
            if (status != "PASS" && status != "FAIL") throw new InvalidOperationException("Result status is invalid.");
            return status;
        }

        private static string RequireMachineStatus(CanonicalJsonValue value, string propertyName)
        {
            var status = RequireString(value, propertyName);
            if (status != "MACHINE_READY" && status != "MACHINE_REWORK") throw new InvalidOperationException("Machine status is invalid.");
            return status;
        }

        private static void RequireValidationSuccess(EvidenceValidationResult result, string context)
        {
            if (!result.IsValid) throw new InvalidOperationException(context + " is invalid: " + result.Code + ".");
        }
        private static bool IsLegalNextEvent(string previousEventName, string eventName, IList<TransitionDocument> entries)
        {
            switch (previousEventName)
            {
                case "SOURCE_SEALED":
                    return eventName == "TESTS_PASSED" || eventName == "TESTS_FAILED";
                case "TESTS_PASSED":
                case "TESTS_FAILED":
                    return eventName == "BUILD_SEALED";
                case "BUILD_SEALED":
                    return eventName == "EVIDENCE_SEALED";
                case "EVIDENCE_SEALED":
                    if (eventName == "MACHINE_REWORK") return true;
                    return eventName == "MACHINE_READY" && entries.Count >= 2 && entries[1].EventName == "TESTS_PASSED";
                default:
                    return false;
            }
        }

        private static bool IsTerminalEvent(string eventName)
        {
            return eventName == "MACHINE_READY" || eventName == "MACHINE_REWORK";
        }

        private static CanonicalJsonValue CreateCandidateValue(string candidateId, string sourceCommit, string unityVersion, string scene, string createdUtc, string candidateSha256)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                Property("schema", EvidenceContracts.Candidate),
                Property("candidateId", candidateId),
                Property("sourceCommit", sourceCommit),
                Property("unityVersion", unityVersion),
                Property("scene", scene),
                Property("createdUtc", createdUtc)
            };
            if (candidateSha256 != null) properties.Add(Property("candidateSha256", candidateSha256));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue CreateSourceManifestValue(string candidateId, string candidateSha256, string sourceCommit, IList<SourceFile> files, string packageLockSha256, string sourceTreeSha256, string sourceManifestSha256)
        {
            var fileValues = new List<CanonicalJsonValue>();
            foreach (var file in files)
            {
                fileValues.Add(CanonicalJsonValue.Object(
                    Property("mode", file.Mode),
                    Property("path", file.Path),
                    Property("sha256", file.Sha256),
                    new CanonicalJsonProperty("size", CanonicalJsonValue.Number(file.Size))));
            }

            var properties = new List<CanonicalJsonProperty>
            {
                Property("schema", EvidenceContracts.SourceManifest),
                Property("candidateId", candidateId),
                Property("candidateSha256", candidateSha256),
                Property("sourceCommit", sourceCommit),
                new CanonicalJsonProperty("files", CanonicalJsonValue.Array(fileValues)),
                Property("packageLockSha256", packageLockSha256),
                Property("sourceTreeSha256", sourceTreeSha256)
            };
            if (sourceManifestSha256 != null) properties.Add(Property("sourceManifestSha256", sourceManifestSha256));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue CreateTransitionValue(string candidateId, int sequence, string eventName, string occurredUtc, IDictionary<string, string> references, string previousEntrySha256, string entrySha256)
        {
            var referenceProperties = new List<CanonicalJsonProperty>();
            foreach (var pair in references) referenceProperties.Add(Property(pair.Key, pair.Value));
            var properties = new List<CanonicalJsonProperty>
            {
                Property("schema", TransitionSchema),
                Property("candidateId", candidateId),
                new CanonicalJsonProperty("seq", CanonicalJsonValue.Number(sequence)),
                Property("event", eventName),
                Property("occurredUtc", occurredUtc),
                new CanonicalJsonProperty("refs", CanonicalJsonValue.Object(referenceProperties)),
                new CanonicalJsonProperty("previousEntrySha256", previousEntrySha256 == null ? CanonicalJsonValue.Null() : CanonicalJsonValue.String(previousEntrySha256))
            };
            if (entrySha256 != null) properties.Add(Property("entrySha256", entrySha256));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonProperty Property(string name, string value)
        {
            return new CanonicalJsonProperty(name, CanonicalJsonValue.String(value));
        }

        private static List<SourceFile> ReadTrackedSourceFiles(string projectRoot, string sourceCommit)
        {
            var result = RunGitBytes(projectRoot, "ls-tree -r -z --full-tree " + sourceCommit);
            if (result.ExitCode != 0) throw new InvalidOperationException("Unable to enumerate tracked source files: " + result.StandardError);
            if (result.StandardOutput.Length == 0) throw new InvalidOperationException("Git repository has no tracked source files.");

            var files = new List<SourceFile>();
            var start = 0;
            while (start < result.StandardOutput.Length)
            {
                var end = Array.IndexOf(result.StandardOutput, (byte)0, start);
                if (end < 0) throw new InvalidOperationException("Git tree output is malformed.");
                if (end == start) throw new InvalidOperationException("Git tree output contains an empty entry.");

                var firstSpace = Array.IndexOf(result.StandardOutput, (byte)' ', start, end - start);
                var secondSpace = firstSpace < 0 ? -1 : Array.IndexOf(result.StandardOutput, (byte)' ', firstSpace + 1, end - firstSpace - 1);
                var tab = secondSpace < 0 ? -1 : Array.IndexOf(result.StandardOutput, (byte)'\t', secondSpace + 1, end - secondSpace - 1);
                if (firstSpace <= start || secondSpace <= firstSpace || tab <= secondSpace) throw new InvalidOperationException("Git tree entry is malformed.");

                var mode = DecodeGitText(result.StandardOutput, start, firstSpace - start);
                var type = DecodeGitText(result.StandardOutput, firstSpace + 1, secondSpace - firstSpace - 1);
                var objectId = DecodeGitText(result.StandardOutput, secondSpace + 1, tab - secondSpace - 1);
                var path = NormalizeRelativePath(DecodeGitText(result.StandardOutput, tab + 1, end - tab - 1));
                if ((mode != "100644" && mode != "100755" && mode != "120000") || type != "blob" || !IsLowerHex(objectId, 40))
                {
                    throw new InvalidOperationException("Git tree contains an unsupported entry: " + path);
                }

                if (mode == "120000") throw new InvalidOperationException("Git tree contains an unverifiable symbolic link: " + path);
                long size;
                var sha256 = Sha256GitBlob(projectRoot, objectId, out size);
                files.Add(new SourceFile(path, mode, size, sha256));
                start = end + 1;
            }

            files.Sort((left, right) => CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path));
            for (var index = 1; index < files.Count; index++)
            {
                if (string.Equals(files[index - 1].Path, files[index].Path, StringComparison.Ordinal)) throw new InvalidOperationException("Tracked source path is duplicated.");
            }

            return files;
        }

        private static List<SourceFile> ReadApprovedSources(string projectRoot, string sourceCommit)
        {
            var value = ReadJsonDocument(ReadGitBlob(projectRoot, sourceCommit + ":" + ApprovalRelativePath), false, ApprovalRelativePath);
            RequireExactKeys(value, "decidedAtUtc", "decidedBy", "decision", "schemaVersion", "sourceSealRequirement", "sources", "status");
            if (RequirePositiveInteger(value, "schemaVersion") != 1 ||
                !string.Equals(RequireString(value, "decision"), "approved", StringComparison.Ordinal) ||
                !string.Equals(RequireString(value, "status"), "confirmed", StringComparison.Ordinal) ||
                !string.Equals(RequireString(value, "decidedBy"), "user", StringComparison.Ordinal) ||
                string.IsNullOrEmpty(RequireString(value, "sourceSealRequirement")))
            {
                throw new InvalidOperationException("Source approval record is not an approved user decision.");
            }

            RequireUtc(RequireString(value, "decidedAtUtc"), "source approval decidedAtUtc");
            var records = new List<SourceFile>();
            foreach (var source in RequireArray(value, "sources").Items)
            {
                RequireExactKeys(source, "path", "sha256");
                var path = RequireString(source, "path");
                if (!CanonicalJson.IsNormalizedRelativePath(path)) throw new InvalidOperationException("Approved source path is not normalized.");
                records.Add(new SourceFile(path, RequireLowerSha256(source, "sha256")));
            }

            if (records.Count == 0) throw new InvalidOperationException("Source approval record has no sources.");
            records.Sort((left, right) => CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path));
            for (var index = 1; index < records.Count; index++)
            {
                if (string.Equals(records[index - 1].Path, records[index].Path, StringComparison.Ordinal)) throw new InvalidOperationException("Approved source path is duplicated.");
            }

            return records;
        }

        private static void VerifyApprovedSources(IList<SourceFile> sourceFiles, IList<SourceFile> approvedSources)
        {
            foreach (var approvedSource in approvedSources)
            {
                var trackedSource = FindSourceFile(sourceFiles, approvedSource.Path);
                if (!string.Equals(trackedSource.Sha256, approvedSource.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Approved source hash does not match actual file bytes: " + approvedSource.Path);
                }
            }
        }
        private static void ValidateSourceManifestSnapshot(SourceManifestDocument sourceManifest, IList<SourceFile> sourceFiles)
        {
            if (sourceManifest.Files.Count != sourceFiles.Count) throw new InvalidOperationException("Source manifest does not contain the exact sealed Git tree.");

            for (var index = 0; index < sourceFiles.Count; index++)
            {
                var expected = sourceFiles[index];
                var actual = sourceManifest.Files[index];
                if (!string.Equals(actual.Path, expected.Path, StringComparison.Ordinal) ||
                    !string.Equals(actual.Mode, expected.Mode, StringComparison.Ordinal) ||
                    actual.Size != expected.Size ||
                    !string.Equals(actual.Sha256, expected.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Source manifest file does not match the sealed Git tree: " + expected.Path);
                }
            }

            if (!string.Equals(sourceManifest.SourceTreeSha256, ComputeSourceTreeSha256(sourceFiles), StringComparison.Ordinal) ||
                !string.Equals(sourceManifest.PackageLockSha256, FindSourceFile(sourceFiles, PackageLockRelativePath).Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source manifest hash bindings do not match the sealed Git tree.");
            }
        }


        private static SourceFile FindSourceFile(IList<SourceFile> files, string path)
        {
            var normalizedPath = NormalizeRelativePath(path);
            foreach (var file in files)
            {
                if (string.Equals(file.Path, normalizedPath, StringComparison.Ordinal)) return file;
            }

            throw new InvalidOperationException("Required source file is not tracked: " + normalizedPath);
        }

        private static List<SourceFile> ReadSourceFiles(CanonicalJsonValue array)
        {
            var files = new List<SourceFile>();
            foreach (var value in array.Items)
            {
                RequireExactKeys(value, "mode", "path", "sha256", "size");
                var path = RequireString(value, "path");
                var mode = RequireString(value, "mode");
                var size = RequireNonNegativeLong(value, "size");
                if (!CanonicalJson.IsNormalizedRelativePath(path)) throw new InvalidOperationException("Source manifest path is not normalized.");
                if (mode != "100644" && mode != "100755" && mode != "120000") throw new InvalidOperationException("Source manifest file mode is invalid.");
                if (mode == "120000") throw new InvalidOperationException("Source manifest contains an unverifiable symbolic link: " + path);
                files.Add(new SourceFile(path, mode, size, RequireLowerSha256(value, "sha256")));
            }
            if (files.Count == 0) throw new InvalidOperationException("Source manifest has no files.");
            for (var index = 1; index < files.Count; index++)
            {
                if (CanonicalJson.CompareUtf8Ordinal(files[index - 1].Path, files[index].Path) >= 0)
                {
                    throw new InvalidOperationException("Source manifest files are not uniquely sorted.");
                }
            }

            return files;
        }

        private static string ComputeSourceTreeSha256(IList<SourceFile> files)
        {
            var values = new List<CanonicalJsonValue>();
            foreach (var file in files)
            {
                values.Add(CanonicalJsonValue.Object(
                    Property("mode", file.Mode),
                    Property("path", file.Path),
                    Property("sha256", file.Sha256),
                    new CanonicalJsonProperty("size", CanonicalJsonValue.Number(file.Size))));
            }

            return CanonicalJson.Sha256Hex(CanonicalJsonValue.Array(values));
        }
        private static void RequireCandidateSourceCapability(string candidateId, ValidatedChain chain, BuildManifestWriter.BuildProvenance provenance)
        {
            if (provenance == null || provenance.SourceCapability == null)
            {
                throw new InvalidOperationException("Candidate builds require a pre-BuildPlayer source capability.");
            }

            var inputs = ValidateMaterializedUnityInputs(GetProjectRoot(), chain.SourceManifest.Files);
            var expectedDigest = ComputeCandidateSourceCapabilitySha256(
                candidateId,
                chain.Candidate.SourceCommit,
                chain.SourceManifest.SourceManifestSha256,
                inputs);
            var capability = provenance.SourceCapability;
            if (!string.Equals(capability.CandidateId, candidateId, StringComparison.Ordinal) ||
                !string.Equals(capability.SourceCommit, chain.Candidate.SourceCommit, StringComparison.Ordinal) ||
                !string.Equals(capability.SourceManifestSha256, chain.SourceManifest.SourceManifestSha256, StringComparison.Ordinal) ||
                !string.Equals(capability.Digest, expectedDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate source capability does not bind the current sealed source snapshot.");
            }
        }

        private static List<SourceFile> ValidateMaterializedUnityInputs(string projectRoot, IList<SourceFile> sourceFiles)
        {
            if (string.IsNullOrEmpty(projectRoot) || sourceFiles == null) throw new InvalidOperationException("Materialized Unity input inventory is unavailable.");
            if (IsReparsePoint(projectRoot)) throw new InvalidOperationException("Project root cannot be a reparse point.");

            var declared = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
            var inputs = new List<SourceFile>();
            foreach (var sourceFile in sourceFiles)
            {
                if (declared.ContainsKey(sourceFile.Path))
                {
                    throw new InvalidOperationException("Sealed source inventory contains a duplicate path: " + sourceFile.Path);
                }

                declared.Add(sourceFile.Path, sourceFile);
                if (IsMaterializedUnityInput(sourceFile.Path)) inputs.Add(sourceFile);
            }

            if (inputs.Count == 0) throw new InvalidOperationException("Sealed source inventory has no Unity inputs.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rootName in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                var inputRoot = GetPathInsideDirectory(projectRoot, rootName);
                if (!Directory.Exists(inputRoot) || IsReparsePoint(inputRoot))
                {
                    throw new InvalidOperationException("Required materialized Unity input root is missing or unsafe: " + rootName + ".");
                }

                var directories = new Stack<string>();
                directories.Push(inputRoot);
                while (directories.Count > 0)
                {
                    var directory = directories.Pop();
                    if (IsReparsePoint(directory))
                    {
                        throw new InvalidOperationException("Materialized Unity input contains a reparse-point directory.");
                    }

                    foreach (var childDirectory in Directory.GetDirectories(directory))
                    {
                        if (IsReparsePoint(childDirectory))
                        {
                            throw new InvalidOperationException("Materialized Unity input contains a reparse-point directory.");
                        }

                        directories.Push(childDirectory);
                    }

                    foreach (var filePath in Directory.GetFiles(directory))
                    {
                        if (IsReparsePoint(filePath))
                        {
                            throw new InvalidOperationException("Materialized Unity input contains a reparse-point file.");
                        }

                        var relativePath = ToRelativePath(projectRoot, filePath);
                        SourceFile sourceFile;
                        if (!declared.TryGetValue(relativePath, out sourceFile) || !IsMaterializedUnityInput(relativePath))
                        {
                            throw new InvalidOperationException("Materialized Unity input is absent from the sealed source tree: " + relativePath);
                        }

                        var info = new FileInfo(filePath);
                        if (info.Length != sourceFile.Size || !string.Equals(Sha256File(filePath), sourceFile.Sha256, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Materialized Unity input bytes do not match the sealed source tree: " + relativePath);
                        }

                        if (!seen.Add(relativePath))
                        {
                            throw new InvalidOperationException("Materialized Unity input path is duplicated: " + relativePath);
                        }
                    }
                }
            }

            foreach (var input in inputs)
            {
                if (!seen.Contains(input.Path))
                {
                    throw new InvalidOperationException("Sealed Unity input is missing from the materialized snapshot: " + input.Path);
                }
            }

            inputs.Sort((left, right) => CanonicalJson.CompareUtf8Ordinal(left.Path, right.Path));
            return inputs;
        }

        private static bool IsMaterializedUnityInput(string path)
        {
            return path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.StartsWith("Packages/", StringComparison.Ordinal) ||
                path.StartsWith("ProjectSettings/", StringComparison.Ordinal);
        }

        private static string ComputeCandidateSourceCapabilitySha256(
            string candidateId,
            string sourceCommit,
            string sourceManifestSha256,
            IList<SourceFile> inputs)
        {
            var values = new List<CanonicalJsonValue>();
            foreach (var input in inputs)
            {
                values.Add(CanonicalJsonValue.Object(
                    Property("mode", input.Mode),
                    Property("path", input.Path),
                    Property("sha256", input.Sha256),
                    new CanonicalJsonProperty("size", CanonicalJsonValue.Number(input.Size))));
            }

            return CanonicalJson.Sha256Hex(CanonicalJsonValue.Object(
                Property("candidateId", candidateId),
                new CanonicalJsonProperty("materializedUnityInputs", CanonicalJsonValue.Array(values)),
                Property("sourceCommit", sourceCommit),
                Property("sourceManifestSha256", sourceManifestSha256)));
        }

        private static Dictionary<string, string> ReadReferences(CanonicalJsonValue refs, string eventName)
        {
            var expectedKeys = GetExpectedReferenceNames(eventName);
            RequireExactKeys(refs, expectedKeys);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in expectedKeys) result.Add(key, RequireLowerSha256(refs, key));
            return result;
        }

        private static string[] GetExpectedReferenceNames(string eventName)
        {
            switch (eventName)
            {
                case "SOURCE_SEALED": return new[] { "candidateSha256", "sourceManifestSha256" };
                case "TESTS_PASSED":
                case "TESTS_FAILED": return TestReferenceNames;
                case "BUILD_SEALED": return new[] { "buildManifestSha256", "fileSetSha256" };
                case "EVIDENCE_SEALED": return new[] { "evidenceManifestSha256" };
                case "MACHINE_READY":
                case "MACHINE_REWORK": return new[] { "evidenceManifestSha256", "validatorReportSha256" };
                default: throw new InvalidOperationException("Transition event is invalid.");
            }
        }

        private static void RequireReference(TransitionDocument entry, string name, string expectedValue)
        {
            string actualValue;
            if (!entry.References.TryGetValue(name, out actualValue) || !string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Transition reference is invalid: " + name);
            }
        }


        private static string GetCanonicalFileSha256(string candidateDirectory, string relativePath)
        {
            var path = GetPathInsideDirectory(candidateDirectory, relativePath);
            var bytes = File.ReadAllBytes(path);
            CanonicalJsonValue ignored;
            string error;
            if (!CanonicalJson.TryParseCanonicalUtf8(bytes, out ignored, out error))
            {
                throw new InvalidOperationException("Artifact is not canonical JSON: " + relativePath + " (" + error + ").");
            }

            return CanonicalJson.Sha256Hex(bytes);
        }

        private static CanonicalJsonValue ReadCanonicalDocument(string path)
        {
            return ReadJsonDocument(path, true);
        }

        private static CanonicalJsonValue ReadJsonDocument(string path, bool requireCanonical)
        {
            if (!File.Exists(path) || IsReparsePoint(path)) throw new InvalidOperationException("Required artifact is missing or unsafe: " + path);
            return ReadJsonDocument(File.ReadAllBytes(path), requireCanonical, path);
        }

        private static CanonicalJsonValue ReadJsonDocument(byte[] bytes, bool requireCanonical, string name)
        {
            if (bytes == null) throw new InvalidOperationException("Required artifact is missing: " + name);
            CanonicalJsonValue value;
            string error;
            var success = requireCanonical
                ? CanonicalJson.TryParseCanonicalUtf8(bytes, out value, out error)
                : CanonicalJson.TryParseUtf8(bytes, out value, out error);
            if (!success) throw new InvalidOperationException("Invalid JSON artifact '" + name + "': " + error);
            if (value.Kind != CanonicalJsonKind.Object) throw new InvalidOperationException("JSON artifact root must be an object: " + name);
            return value;
        }

        private static void WriteCanonicalJsonNew(string path, CanonicalJsonValue value)
        {
            var bytes = CanonicalJson.SerializeUtf8(value);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void EnsurePathsDoNotExist(params string[] paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path) || Directory.Exists(path)) throw new InvalidOperationException("Write-once artifact already exists: " + path);
            }
        }

        private static void WriteTransitionLogNew(string path, CanonicalJsonValue entry)
        {
            var entryBytes = CanonicalJson.SerializeUtf8(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(entryBytes, 0, entryBytes.Length);
                stream.WriteByte((byte)'\n');
                stream.Flush(true);
            }
        }

        private static void AppendTransitionLog(string path, CanonicalJsonValue entry, long validatedLength, string validatedSha256)
        {
            var entryBytes = CanonicalJson.SerializeUtf8(entry);
            if (IsReparsePoint(path)) throw new InvalidOperationException("Transition log cannot be a reparse point.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (stream.Length == 0) throw new InvalidOperationException("Transition log is empty.");
                stream.Position = stream.Length - 1;
                if (stream.ReadByte() != '\n') throw new InvalidOperationException("Transition log must end with a newline.");

                if (stream.Length > int.MaxValue) throw new InvalidOperationException("Transition log is too large.");
                var currentBytes = new byte[(int)stream.Length];
                stream.Position = 0;
                var offset = 0;
                while (offset < currentBytes.Length)
                {
                    var count = stream.Read(currentBytes, offset, currentBytes.Length - offset);
                    if (count == 0) throw new InvalidOperationException("Transition log changed while appending.");
                    offset += count;
                }
                if (currentBytes.LongLength != validatedLength ||
                    !string.Equals(CanonicalJson.Sha256Hex(currentBytes), validatedSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Transition log changed after full-chain validation and before append.");
                }

                ValidateAppendPredecessor(currentBytes, entry);
                stream.Position = stream.Length;
                stream.Write(entryBytes, 0, entryBytes.Length);
                stream.WriteByte((byte)'\n');
                stream.Flush(true);
            }
        }

        private static void ValidateAppendPredecessor(byte[] currentBytes, CanonicalJsonValue entry)
        {
            string currentText;
            try
            {
                currentText = new UTF8Encoding(false, true).GetString(currentBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Transition log is not valid UTF-8.", exception);
            }

            var lastNewline = currentText.Length - 1;
            var priorNewline = currentText.LastIndexOf('\n', lastNewline - 1);
            var lastLine = currentText.Substring(priorNewline + 1, lastNewline - priorNewline - 1);
            CanonicalJsonValue previousEntry;
            var error = "empty line";
            if (lastLine.Length == 0 || !CanonicalJson.TryParseCanonicalUtf8(new UTF8Encoding(false, true).GetBytes(lastLine), out previousEntry, out error) || previousEntry.Kind != CanonicalJsonKind.Object)
            {
                throw new InvalidOperationException("Transition log has no canonical final entry: " + error);
            }

            var previousSequence = RequirePositiveInteger(previousEntry, "seq");
            var previousEntrySha256 = RequireLowerSha256(previousEntry, "entrySha256");
            var newSequence = RequirePositiveInteger(entry, "seq");
            var newPreviousEntrySha256 = ReadNullableLowerSha256(entry, "previousEntrySha256");
            if (newSequence != previousSequence + 1 || !string.Equals(newPreviousEntrySha256, previousEntrySha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Transition log changed before the append could be committed.");
            }
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }


        private static string GetPathInsideDirectory(string directory, string relativePath)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            var root = Path.GetFullPath(directory);
            var fullPath = Path.GetFullPath(Path.Combine(root, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(rootWithSeparator, comparison)) throw new InvalidOperationException("Artifact path escapes its root.");
            return fullPath;
        }

        private static string NormalizeRelativePath(string path)
        {
            var normalizedPath = CanonicalJson.NormalizeRelativePath(path);
            if (!string.Equals(path, normalizedPath, StringComparison.Ordinal)) throw new InvalidOperationException("Path is not normalized: " + path);
            return normalizedPath;
        }

        public static bool IsValidCandidateId(string candidateId)
        {
            try
            {
                NormalizeCandidateId(candidateId);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        private static string NormalizeCandidateId(string candidateId)
        {
            if (string.IsNullOrEmpty(candidateId) || candidateId.Length > 64) throw new ArgumentException("Candidate identifier must contain 1 through 64 lowercase characters.", nameof(candidateId));
            for (var index = 0; index < candidateId.Length; index++)
            {
                var character = candidateId[index];
                if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '-'))
                {
                    throw new ArgumentException("Candidate identifier must use lowercase letters, digits, and hyphens only.", nameof(candidateId));
                }
            }
            if (candidateId[0] == '-' || candidateId[candidateId.Length - 1] == '-' || candidateId.IndexOf("--", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("Candidate identifier has an invalid hyphen placement.", nameof(candidateId));
            }
            return candidateId;
        }

        private static void EnsureCleanGitRepository(string projectRoot)
        {
            var rootResult = RunGit(projectRoot, "rev-parse --show-toplevel");
            if (rootResult.ExitCode != 0) throw new InvalidOperationException("Candidate sealing requires a Git repository: " + rootResult.StandardError);
            var gitRoot = Path.GetFullPath(rootResult.StandardOutput.TrimEnd('\r', '\n'));
            if (!PathsEqual(projectRoot, gitRoot)) throw new InvalidOperationException("Candidate sealing must run at the Git repository root.");

            var statusResult = RunGit(projectRoot, "status --porcelain=v1 --untracked-files=all");
            if (statusResult.ExitCode != 0) throw new InvalidOperationException("Unable to check the Git working tree: " + statusResult.StandardError);
            if (statusResult.StandardOutput.Length != 0) throw new InvalidOperationException("Candidate sealing requires a clean Git working tree.");
        }
        private static void EnsureGitSnapshotStillClean(string projectRoot, string sourceCommit)
        {
            EnsureCleanGitRepository(projectRoot);
            if (!string.Equals(ReadGitCommit(projectRoot), sourceCommit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Git HEAD changed while the source snapshot was being sealed.");
            }
        }


        private static string ReadGitCommit(string projectRoot)
        {
            var result = RunGit(projectRoot, "rev-parse HEAD");
            if (result.ExitCode != 0) throw new InvalidOperationException("Unable to read the Git source commit: " + result.StandardError);
            var commit = result.StandardOutput.TrimEnd('\r', '\n');
            if (!IsLowerHex(commit, 40)) throw new InvalidOperationException("Git source commit is not a lowercase SHA-1 identifier.");
            return commit;
        }

        private static byte[] ReadGitBlob(string projectRoot, string objectSpecifier)
        {
            var result = RunGitBytes(projectRoot, "cat-file blob " + objectSpecifier);
            if (result.ExitCode != 0) throw new InvalidOperationException("Unable to read immutable Git blob: " + result.StandardError);
            return result.StandardOutput;
        }
        private static string Sha256GitBlob(string projectRoot, string objectId, out long length)
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
                    var sha256 = CanonicalJson.Sha256Hex(process.StandardOutput.BaseStream, out length);
                    var standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new InvalidOperationException("Unable to hash immutable Git blob: " + standardError);
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

        private static GitResult RunGit(string projectRoot, string arguments)
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
                    var standardOutput = process.StandardOutput.ReadToEnd();
                    var standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new GitResult(process.ExitCode, standardOutput, standardError);
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unable to execute Git.", exception);
            }
        }

        private sealed class GitResult
        {
            public GitResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
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
        private static string Sha256File(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long ignoredLength;
                return CanonicalJson.Sha256Hex(stream, out ignoredLength);
            }
        }

        private static void RequireExactKeys(CanonicalJsonValue value, params string[] expectedKeys)
        {
            if (value == null || value.Kind != CanonicalJsonKind.Object) throw new InvalidOperationException("JSON value must be an object.");
            if (value.Properties.Count != expectedKeys.Length) throw new InvalidOperationException("JSON object has an unexpected member count.");
            var expected = new HashSet<string>(expectedKeys, StringComparer.Ordinal);
            foreach (var property in value.Properties)
            {
                if (!expected.Remove(property.Name)) throw new InvalidOperationException("JSON object contains an unexpected or duplicate member: " + property.Name);
            }
            if (expected.Count != 0) throw new InvalidOperationException("JSON object is missing a required member.");
        }

        private static CanonicalJsonValue RequireObject(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.Object) throw new InvalidOperationException("JSON object member is required: " + propertyName);
            return property;
        }

        private static CanonicalJsonValue RequireArray(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.Array) throw new InvalidOperationException("JSON array member is required: " + propertyName);
            return property;
        }

        private static string RequireString(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.String || string.IsNullOrEmpty(property.StringValue))
            {
                throw new InvalidOperationException("Non-empty JSON string member is required: " + propertyName);
            }
            return property.StringValue;
        }
        private static string RequireStringValue(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.String)
            {
                throw new InvalidOperationException("JSON string member is required: " + propertyName);
            }

            return property.StringValue;
        }

        private static string RequireNullableString(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property)) throw new InvalidOperationException("JSON member is required: " + propertyName);
            if (property.Kind == CanonicalJsonKind.Null) return null;
            if (property.Kind != CanonicalJsonKind.String || string.IsNullOrEmpty(property.StringValue)) throw new InvalidOperationException("Nullable JSON string member is invalid: " + propertyName);
            return property.StringValue;
        }

        private static bool RequireBoolean(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.Boolean)
            {
                throw new InvalidOperationException("JSON Boolean member is required: " + propertyName);
            }

            return property.BooleanValue;
        }



        private static string RequireLowerSha256(CanonicalJsonValue value, string propertyName)
        {
            var hash = RequireString(value, propertyName);
            if (!CanonicalJson.IsLowerSha256(hash)) throw new InvalidOperationException("JSON SHA-256 member is invalid: " + propertyName);
            return hash;
        }

        private static string ReadNullableLowerSha256(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property)) throw new InvalidOperationException("JSON member is required: " + propertyName);
            if (property.Kind == CanonicalJsonKind.Null) return null;
            if (property.Kind != CanonicalJsonKind.String || !CanonicalJson.IsLowerSha256(property.StringValue)) throw new InvalidOperationException("Nullable SHA-256 member is invalid: " + propertyName);
            return property.StringValue;
        }

        private static string RequireLowerHex(CanonicalJsonValue value, string propertyName, int length)
        {
            var hash = RequireString(value, propertyName);
            if (!IsLowerHex(hash, length)) throw new InvalidOperationException("JSON hexadecimal member is invalid: " + propertyName);
            return hash;
        }

        private static int RequirePositiveInteger(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.Number ||
                property.NumberValue <= 0d || property.NumberValue > int.MaxValue || Math.Floor(property.NumberValue) != property.NumberValue)
            {
                throw new InvalidOperationException("Positive integer JSON member is required: " + propertyName);
            }
            return (int)property.NumberValue;
        }

        private static int RequireNonNegativeInteger(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.Number ||
                property.NumberValue < 0d || property.NumberValue > int.MaxValue || Math.Floor(property.NumberValue) != property.NumberValue)
            {
                throw new InvalidOperationException("Non-negative integer JSON member is required: " + propertyName);
            }

            return (int)property.NumberValue;
        }

        private static long RequireNonNegativeLong(CanonicalJsonValue value, string propertyName)
        {
            CanonicalJsonValue property;
            if (!value.TryGetSingleProperty(propertyName, out property) || property.Kind != CanonicalJsonKind.Number ||
                property.NumberValue < 0d || property.NumberValue > 9007199254740991d || Math.Floor(property.NumberValue) != property.NumberValue)
            {
                throw new InvalidOperationException("Non-negative integer JSON member is required: " + propertyName);
            }

            return (long)property.NumberValue;
        }
        private static void RequireUtc(string value, string name)
        {
            DateTime parsed;
            if (!DateTime.TryParseExact(value, new[] { "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                throw new InvalidOperationException("UTC timestamp is invalid: " + name);
            }
        }

        private static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }
        private static void RequireApprovedUnityVersion()
        {
            if (!string.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate sealing requires Unity " + RequiredUnityVersion + ".");
            }
        }

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            }
            return true;
        }

        private static bool PathsEqual(string left, string right)
        {
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
    }
}
