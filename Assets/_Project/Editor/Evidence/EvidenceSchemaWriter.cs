using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Overbless.Editor.Evidence
{
    public sealed class EvidenceSchemaDefinition
    {
        internal EvidenceSchemaDefinition(string fileName, string schemaLiteral, string[] requiredKeys, CanonicalJsonValue document)
        {
            FileName = fileName;
            SchemaLiteral = schemaLiteral;
            RequiredKeys = Array.AsReadOnly((string[])requiredKeys.Clone());
            Document = document;
        }

        public string FileName { get; }
        public string SchemaLiteral { get; }
        public IReadOnlyList<string> RequiredKeys { get; }
        public CanonicalJsonValue Document { get; }
    }

    public sealed class EvidencePayloadDefinition
    {
        internal EvidencePayloadDefinition(string payloadType, string schemaLiteral, string[] requiredKeys)
        {
            PayloadType = payloadType;
            SchemaLiteral = schemaLiteral;
            RequiredKeys = Array.AsReadOnly((string[])requiredKeys.Clone());
        }

        public string PayloadType { get; }
        public string SchemaLiteral { get; }
        public IReadOnlyList<string> RequiredKeys { get; }
    }

    /// <summary>Generates the fixed Draft 2020-12 evidence schema inventory without producing evidence decisions.</summary>
    public static class EvidenceSchemaWriter
    {
        public const string DefaultSchemaDirectory = "Assets/_Project/Editor/Evidence/Schemas";
        public const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

        private static readonly EvidenceSchemaDefinition[] Definitions = CreateDefinitions();
        private static readonly EvidencePayloadDefinition[] PayloadDefinitions = CreatePayloadDefinitions();

        public static IReadOnlyList<EvidenceSchemaDefinition> AllSchemas => Definitions;

        public static bool TryGetSchemaLiteral(string fileName, out string schemaLiteral)
        {
            foreach (var definition in Definitions)
            {
                if (string.Equals(definition.FileName, fileName, StringComparison.Ordinal))
                {
                    schemaLiteral = definition.SchemaLiteral;
                    return true;
                }
            }

            schemaLiteral = null;
            return false;
        }

        public static bool TryGetSchemaDefinition(string schemaLiteral, out EvidenceSchemaDefinition schemaDefinition)
        {
            foreach (var definition in Definitions)
            {
                if (string.Equals(definition.SchemaLiteral, schemaLiteral, StringComparison.Ordinal))
                {
                    schemaDefinition = definition;
                    return true;
                }
            }

            schemaDefinition = null;
            return false;
        }

        public static bool TryGetPayloadDefinition(string payloadType, out EvidencePayloadDefinition payloadDefinition)
        {
            foreach (var definition in PayloadDefinitions)
            {
                if (string.Equals(definition.PayloadType, payloadType, StringComparison.Ordinal))
                {
                    payloadDefinition = definition;
                    return true;
                }
            }

            payloadDefinition = null;
            return false;
        }

        public static bool IsKnownSchemaLiteral(string schemaLiteral)
        {
            EvidenceSchemaDefinition ignored;
            return !string.IsNullOrEmpty(schemaLiteral) && TryGetSchemaDefinition(schemaLiteral, out ignored);
        }

        public static CanonicalJsonValue GetSchemaDocument(string schemaLiteral)
        {
            EvidenceSchemaDefinition definition;
            if (TryGetSchemaDefinition(schemaLiteral, out definition)) return definition.Document;
            throw new ArgumentOutOfRangeException(nameof(schemaLiteral), "Unknown evidence schema literal.");
        }

        public static void WriteAll(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Schema directory is required.", nameof(directory));
            Directory.CreateDirectory(directory);
            foreach (var definition in Definitions)
            {
                File.WriteAllBytes(Path.Combine(directory, definition.FileName), CanonicalJson.SerializeUtf8(definition.Document));
            }
        }

        /// <summary>Batch-mode entry point. Pass -schemaDirectory &lt;path&gt; to override the project schema directory.</summary>
        public static void Execute()
        {
            string directory;
            if (!TryGetCommandLineArgument("-schemaDirectory", out directory)) directory = DefaultSchemaDirectory;
            WriteAll(directory);
            Debug.Log("Wrote " + Definitions.Length + " deterministic evidence schemas to " + directory + ".");
        }

        private static EvidenceSchemaDefinition[] CreateDefinitions()
        {
            var sha256 = HashSchema();
            var path = PathSchema();
            var nonempty = TextSchema(1);
            var timestamp = ExactStringSchema("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", 24);
            var criteria = ArraySchema(CriterionSchema(), 1, null, true);
            var rawReference = ObjectSchema(
                Field("path", path),
                Field("size", IntegerSchema(0, null)),
                Field("sha256", sha256));
            var sourceFile = ObjectSchema(
                Field("mode", EnumSchema("100644", "100755", "120000")),
                Field("path", path),
                Field("size", IntegerSchema(0, null)),
                Field("sha256", sha256));
            var buildFile = ObjectSchema(
                Field("path", path),
                Field("size", IntegerSchema(0, null)),
                Field("sha256", sha256));
            var artifact = ObjectSchema(
                Field("criterionIds", criteria),
                Field("path", path),
                Field("role", EnumSchema("RAW", "SOURCE_RESULT", "BUILD_RESULT", "CAPTURE_MANIFEST")),
                Field("sha256", sha256),
                Field("size", IntegerSchema(0, null)));
            var captureFile = buildFile;
            var package = ObjectSchema(Field("name", nonempty), Field("version", nonempty));
            var renderer = EnumSchema("URP2D", "Missing", "Unexpected");
            var input = EnumSchema("InputSystem", "Unexpected");
            var projectBuildSettings = ObjectSchema(
                Field("autoconnectProfiler", BooleanSchema()),
                Field("compressionFormat", nonempty),
                Field("decompressionFallback", BooleanSchema()),
                Field("deepProfiling", BooleanSchema()),
                Field("development", BooleanSchema()),
                Field("exceptionSupport", nonempty),
                Field("memorySizeMb", IntegerSchema(0, null)),
                Field("scenes", ArraySchema(path, 0, null, true)),
                Field("target", EnumSchema("WebGL", "Unexpected")));
            var displayPolicy = ObjectSchema(
                Field("aspectDenominator", IntegerSchema(0, null)),
                Field("aspectNumerator", IntegerSchema(0, null)),
                Field("canvasScaleMode", nonempty),
                Field("designHeight", IntegerSchema(0, null)),
                Field("designWidth", IntegerSchema(0, null)),
                Field("letterboxNon16x9", BooleanSchema()),
                Field("minimumHeight", IntegerSchema(0, null)),
                Field("minimumWidth", IntegerSchema(0, null)),
                Field("sameWorldBounds", BooleanSchema()));
            var scopeAllowance = ObjectSchema(
                Field("approvalReference", path),
                Field("approvalSha256", sha256),
                Field("column", IntegerSchema(0, null)),
                Field("line", IntegerSchema(0, null)),
                Field("path", path),
                Field("sourceSha256", sha256),
                Field("token", nonempty));
            var scopeMatch = ObjectSchema(
                Field("allowlisted", BooleanSchema()),
                Field("approvalReference", NullableSchema(path)),
                Field("column", IntegerSchema(1, null)),
                Field("line", IntegerSchema(1, null)),
                Field("path", path),
                Field("sourceSha256", sha256),
                Field("token", nonempty));
            var audioEvent = ObjectSchema(
                Field("event", EnumSchema("DasherReady", "ArcherReady", "ExitOpened")),
                Field("frame", IntegerSchema(0, null)),
                Field("token", IntegerSchema(1, null)));
            var observation = ObjectSchema(
                Field("identified", BooleanSchema()),
                Field("resolution", EnumSchema("1280x720", "1920x1080")),
                Field("testerId", TesterIdSchema()));
            var browserInputs = ArraySchema(TextSchema(1), 1, null, true);
            var performanceBucket = ObjectSchema(
                Field("completedFrames", IntegerSchema(0, null)),
                Field("endUs", IntegerSchema(1, null)),
                Field("index", IntegerSchema(0, 59)),
                Field("minFpsEquivalent", NumberSchema(0d, null)),
                Field("startUs", IntegerSchema(0, null)));
            var reportCheck = ObjectSchema(
                Field("checkId", EnumSchema(EvidenceContracts.Checks)),
                Field("detailCode", nonempty),
                Field("status", EnumSchema("PASS", "FAIL")));
            var nunitPayload = PayloadObject("overbless.source-nunit/v1",
                Field("exitCode", IntegerSchema(0, null)),
                Field("failed", IntegerSchema(0, null)),
                Field("failureSummary", StringSchema(null, 0)),
                Field("passed", IntegerSchema(0, null)),
                Field("skipped", IntegerSchema(0, null)),
                Field("suite", nonempty),
                Field("total", IntegerSchema(0, null)));
            var projectConfigPayload = PayloadObject("overbless.source-project-config/v1",
                Field("addressablesPresent", BooleanSchema()),
                Field("buildSettings", projectBuildSettings),
                Field("directPackages", ArraySchema(package, 0, null, true)),
                Field("displayPolicy", displayPolicy),
                Field("failureCodes", ArraySchema(nonempty, 0, null, true)),
                Field("input", input),
                Field("packageLockSha256", NullableSchema(sha256)),
                Field("renderer", renderer),
                Field("scene", StringSchema(null, 0)),
                Field("snapshotStatus", EnumSchema("PASS", "FAIL")),
                Field("unityVersion", nonempty));
            var scopeAuditPayload = PayloadObject("overbless.source-scope-audit/v1",
                Field("allowlist", ArraySchema(scopeAllowance, 0, null, true)),
                Field("auditStatus", EnumSchema("PASS", "FAIL")),
                Field("forbiddenTokens", ArraySchema(nonempty, 1, null, true)),
                Field("matches", ArraySchema(scopeMatch, 0, null, false)),
                Field("scannedRoots", ArraySchema(path, 1, null, true)));
            var audioEventsPayload = PayloadObject("overbless.audio-events/v1",
                Field("events", ArraySchema(audioEvent, 3, 3, false)));
            var visualPayload = VisualPayloadSchema(observation);
            var usabilityPayload = PayloadObject("overbless.usability/v1",
                Field("attempts", IntegerSchema(1, null)),
                Field("completed", BooleanSchema()),
                Field("consentRef", nonempty),
                Field("hitExplanation", nonempty),
                Field("nextAction", nonempty),
                Field("noCoaching", BooleanSchema()),
                Field("priorExposure", BooleanSchema()),
                Field("startGestureUtc", timestamp),
                Field("testerId", TesterIdSchema()),
                Field("understoodAtMilliseconds", IntegerSchema(0, null)));
            var audioBlindPayload = PayloadObject("overbless.audio-blind/v1",
                Field("answers", ArraySchema(AudioEventNameSchema(), 3, 3, true)),
                Field("consentRef", nonempty),
                Field("priorExposure", BooleanSchema()),
                Field("testerId", TesterIdSchema()));
            var browserPayload = PayloadObject("overbless.browser/v1",
                Field("audioUnlocked", BooleanSchema()),
                Field("browser", EnumSchema("Chrome", "Edge")),
                Field("buildManifestVerifiedAfter", BooleanSchema()),
                Field("buildManifestVerifiedBefore", BooleanSchema()),
                Field("coldLoad", BooleanSchema()),
                Field("dpr", NumberSchema(0d, null)),
                Field("extensionsEnabled", BooleanSchema()),
                Field("focusLossZeroed", BooleanSchema()),
                Field("hardwareAcceleration", BooleanSchema()),
                Field("inputs", browserInputs),
                Field("profileFresh", BooleanSchema()),
                Field("regainGesture", BooleanSchema()),
                Field("stuckKeys", BooleanSchema()),
                Field("timerStartedAfterGesture", BooleanSchema()),
                Field("trustedStart", BooleanSchema()),
                Field("version", nonempty),
                Field("viewportCssHeight", IntegerSchema(1, null)),
                Field("viewportCssWidth", IntegerSchema(1, null)),
                Field("zoomPercent", IntegerSchema(1, null)));
            var performancePayload = PerformancePayloadSchema(performanceBucket);
            var sourcePayloads = OneOfSchema(nunitPayload, projectConfigPayload, scopeAuditPayload);
            var buildPayloads = OneOfSchema(audioEventsPayload, visualPayload, usabilityPayload, audioBlindPayload, browserPayload, performancePayload);

            return new[]
            {
                Define("candidate-v1.schema.json", EvidenceContracts.Candidate,
                    Field("candidateId", nonempty), Field("candidateSha256", sha256), Field("createdUtc", timestamp), Field("scene", path), Field("sourceCommit", ExactStringSchema("^[0-9a-f]{40}$", 40)), Field("unityVersion", ConstSchema("6000.0.72f1"))),
                Define("source-manifest-v1.schema.json", EvidenceContracts.SourceManifest,
                    Field("candidateId", nonempty), Field("candidateSha256", sha256), Field("files", ArraySchema(sourceFile, 1, null, true)), Field("packageLockSha256", sha256), Field("sourceCommit", ExactStringSchema("^[0-9a-f]{40}$", 40)), Field("sourceManifestSha256", sha256), Field("sourceTreeSha256", sha256)),
                Define("transition-entry-v1.schema.json", "overbless.transition-entry/v1",
                    Field("candidateId", nonempty), Field("entrySha256", sha256), Field("event", EnumSchema("SOURCE_SEALED", "TESTS_PASSED", "TESTS_FAILED", "BUILD_SEALED", "EVIDENCE_SEALED", "MACHINE_READY", "MACHINE_REWORK")), Field("occurredUtc", timestamp), Field("previousEntrySha256", NullableSchema(sha256)), Field("refs", OpenHashObjectSchema()), Field("seq", IntegerSchema(1, null))),
                Define("source-result-v1.schema.json", "overbless.source-result/v1",
                    Field("candidateId", nonempty), Field("criterionIds", criteria), Field("payload", sourcePayloads), Field("payloadType", EnumSchema("NUnitSuite", "ProjectConfigSnapshot", "ScopeAudit")), Field("producer", nonempty), Field("producedUtc", timestamp), Field("rawArtifact", rawReference), Field("sourceManifestSha256", sha256), Field("status", EnumSchema("PASS", "FAIL"))),
                Define("source-nunit-payload-v1.schema.json", "overbless.source-nunit/v1",
                    Field("exitCode", IntegerSchema(0, null)), Field("failed", IntegerSchema(0, null)), Field("failureSummary", StringSchema(null, 0)), Field("passed", IntegerSchema(0, null)), Field("skipped", IntegerSchema(0, null)), Field("suite", nonempty), Field("total", IntegerSchema(0, null))),
                Define("source-project-config-payload-v1.schema.json", "overbless.source-project-config/v1",
                    Field("addressablesPresent", BooleanSchema()), Field("buildSettings", projectBuildSettings), Field("directPackages", ArraySchema(package, 0, null, true)), Field("displayPolicy", displayPolicy), Field("failureCodes", ArraySchema(nonempty, 0, null, true)), Field("input", input), Field("packageLockSha256", NullableSchema(sha256)), Field("renderer", renderer), Field("scene", StringSchema(null, 0)), Field("snapshotStatus", EnumSchema("PASS", "FAIL")), Field("unityVersion", nonempty)),
                Define("source-scope-audit-payload-v1.schema.json", "overbless.source-scope-audit/v1",
                    Field("allowlist", ArraySchema(scopeAllowance, 0, null, true)), Field("auditStatus", EnumSchema("PASS", "FAIL")), Field("forbiddenTokens", ArraySchema(nonempty, 1, null, true)), Field("matches", ArraySchema(scopeMatch, 0, null, false)), Field("scannedRoots", ArraySchema(path, 1, null, true))),
                Define("build-manifest-v1.schema.json", EvidenceContracts.BuildManifest,
                    Field("buildManifestSha256", sha256), Field("candidateId", nonempty), Field("fileSetSha256", sha256), Field("files", ArraySchema(buildFile, 1, null, true)), Field("servedRootManifestSha256", sha256), Field("settings", BuildSettingsSchema()), Field("sourceCapabilitySha256", sha256), Field("sourceManifestSha256", sha256)),
                Define("build-result-v1.schema.json", "overbless.build-result/v1",
                    Field("buildManifestSha256", sha256), Field("candidateId", nonempty), Field("criterionIds", criteria), Field("payload", buildPayloads), Field("payloadType", EnumSchema("AudioEvents", "VisualIdentify", "VisualHitDisplay", "Usability", "AudioBlind", "Browser", "Performance")), Field("producer", nonempty), Field("producedUtc", timestamp), Field("rawArtifacts", ArraySchema(rawReference, 1, null, true)), Field("sourceManifestSha256", sha256), Field("status", EnumSchema("PASS", "FAIL"))),
                Define("audio-events-payload-v1.schema.json", "overbless.audio-events/v1", Field("events", ArraySchema(audioEvent, 3, 3, false))),
                DefineAudioRandomization(),
                DefineVisual(observation),
                Define("usability-payload-v1.schema.json", "overbless.usability/v1",
                    Field("attempts", IntegerSchema(1, null)), Field("completed", BooleanSchema()), Field("consentRef", nonempty), Field("hitExplanation", nonempty), Field("nextAction", nonempty), Field("noCoaching", BooleanSchema()), Field("priorExposure", BooleanSchema()), Field("startGestureUtc", timestamp), Field("testerId", TesterIdSchema()), Field("understoodAtMilliseconds", IntegerSchema(0, null))),
                Define("audio-blind-payload-v1.schema.json", "overbless.audio-blind/v1",
                    Field("answers", ArraySchema(AudioEventNameSchema(), 3, 3, true)), Field("consentRef", nonempty), Field("priorExposure", BooleanSchema()), Field("testerId", TesterIdSchema())),
                Define("browser-payload-v1.schema.json", "overbless.browser/v1",
                    Field("audioUnlocked", BooleanSchema()), Field("browser", EnumSchema("Chrome", "Edge")), Field("buildManifestVerifiedAfter", BooleanSchema()), Field("buildManifestVerifiedBefore", BooleanSchema()), Field("coldLoad", BooleanSchema()), Field("dpr", NumberSchema(0d, null)), Field("extensionsEnabled", BooleanSchema()), Field("focusLossZeroed", BooleanSchema()), Field("hardwareAcceleration", BooleanSchema()), Field("inputs", browserInputs), Field("profileFresh", BooleanSchema()), Field("regainGesture", BooleanSchema()), Field("stuckKeys", BooleanSchema()), Field("timerStartedAfterGesture", BooleanSchema()), Field("trustedStart", BooleanSchema()), Field("version", nonempty), Field("viewportCssHeight", IntegerSchema(1, null)), Field("viewportCssWidth", IntegerSchema(1, null)), Field("zoomPercent", IntegerSchema(1, null))),
                DefinePerformance(performanceBucket),
                Define("capture-manifest-v1.schema.json", "overbless.capture-manifest/v1",
                    Field("buildManifestSha256", sha256), Field("candidateId", nonempty), Field("captureSet", EnumSchema("Visual1920x1080", "Visual1280x720", "Telegraph", "Grayscale", "Letterbox")), Field("files", ArraySchema(captureFile, 1, null, true))),
                Define("evidence-manifest-v1.schema.json", EvidenceContracts.EvidenceManifest,
                    Field("artifacts", ArraySchema(artifact, 1, null, true)), Field("buildManifestSha256", sha256), Field("candidateId", nonempty), Field("candidateSha256", sha256), Field("evidenceManifestSha256", sha256), Field("generatedUtc", timestamp), Field("requiredCriterionIds", criteria), Field("sourceManifestSha256", sha256)),
                Define("validator-report-v1.schema.json", EvidenceContracts.ValidatorReport,
                    Field("candidateId", nonempty), Field("checkedCriterionIds", criteria), Field("checks", ArraySchema(reportCheck, EvidenceContracts.Checks.Length, EvidenceContracts.Checks.Length, false)), Field("evidenceManifestSha256", sha256), Field("generatedUtc", timestamp), Field("status", EnumSchema("MACHINE_READY", "MACHINE_REWORK")), Field("validatorReportSha256", sha256)),
                DefineGateDecision()
            };
        }

        private static EvidencePayloadDefinition[] CreatePayloadDefinitions()
        {
            return new[]
            {
                Payload("NUnitSuite", "overbless.source-nunit/v1", "schema", "suite", "total", "passed", "failed", "skipped", "exitCode", "failureSummary"),
                Payload("ProjectConfigSnapshot", "overbless.source-project-config/v1", "schema", "unityVersion", "directPackages", "packageLockSha256", "renderer", "input", "addressablesPresent", "scene", "buildSettings", "displayPolicy", "snapshotStatus", "failureCodes"),
                Payload("ScopeAudit", "overbless.source-scope-audit/v1", "schema", "scannedRoots", "forbiddenTokens", "allowlist", "matches", "auditStatus"),
                Payload("AudioEvents", "overbless.audio-events/v1", "schema", "events"),
                Payload("VisualIdentify", "overbless.visual/v1", "schema", "testerIds", "resolutions", "observations"),
                Payload("VisualHitDisplay", "overbless.visual/v1", "schema", "sharedGeometry", "grayscaleDistinct", "sameWorldBounds", "letterboxPass"),
                Payload("Usability", "overbless.usability/v1", "schema", "testerId", "priorExposure", "startGestureUtc", "understoodAtMilliseconds", "attempts", "completed", "hitExplanation", "nextAction", "consentRef", "noCoaching"),
                Payload("AudioBlind", "overbless.audio-blind/v1", "schema", "testerId", "priorExposure", "answers", "consentRef"),
                Payload("Browser", "overbless.browser/v1", "schema", "browser", "version", "profileFresh", "extensionsEnabled", "hardwareAcceleration", "viewportCssWidth", "viewportCssHeight", "zoomPercent", "dpr", "coldLoad", "trustedStart", "audioUnlocked", "timerStartedAfterGesture", "inputs", "focusLossZeroed", "regainGesture", "buildManifestVerifiedBefore", "buildManifestVerifiedAfter", "stuckKeys"),
                Payload("Performance", "overbless.performance/v1", "schema", "browser", "resolution", "scenario", "warmupSeconds", "sampleSeconds", "bucketOriginMicroseconds", "buckets", "allForeground", "noPause", "status", "longestFrameUs", "p95FrameUs")
            };
        }

        private static EvidenceSchemaDefinition DefineAudioRandomization()
        {
            const string literal = EvidenceContracts.AudioRandomization;
            var order = ObjectSchema(Field("eventOrder", ArraySchema(AudioEventNameSchema(), 3, 3, false)), Field("testerId", TesterIdSchema()));
            return Define("audio-randomization-v1.schema.json", literal,
                Field("buildManifestSha256", HashSchema()), Field("candidateId", TextSchema(1)), Field("orders", ArraySchema(order, 3, 3, true)), Field("seed", IntegerSchema(0, int.MaxValue)));
        }

        private static EvidenceSchemaDefinition DefineVisual(CanonicalJsonValue observation)
        {
            const string literal = "overbless.visual/v1";
            var identify = ObjectSchema(
                Field("observations", ArraySchema(observation, 6, 6, false)),
                Field("resolutions", ArraySchema(EnumSchema("1280x720", "1920x1080"), 2, 2, true)),
                Field("schema", ConstSchema(literal)),
                Field("testerIds", ArraySchema(TesterIdSchema(), 3, 3, true)));
            var hitDisplay = ObjectSchema(
                Field("grayscaleDistinct", BooleanSchema()),
                Field("letterboxPass", BooleanSchema()),
                Field("sameWorldBounds", BooleanSchema()),
                Field("schema", ConstSchema(literal)),
                Field("sharedGeometry", BooleanSchema()));
            return new EvidenceSchemaDefinition("visual-payload-v1.schema.json", literal, new[] { "schema" }, RootSchema(literal,
                Property("oneOf", CanonicalJsonValue.Array(new[] { identify, hitDisplay }))));
        }

        private static EvidenceSchemaDefinition DefinePerformance(CanonicalJsonValue bucket)
        {
            return Define("performance-payload-v1.schema.json", "overbless.performance/v1",
                Field("allForeground", BooleanSchema()), Field("browser", EnumSchema("Chrome", "Edge")), Field("bucketOriginMicroseconds", IntegerSchema(0, null)), Field("buckets", ArraySchema(bucket, 60, 60, false)), Field("longestFrameUs", IntegerSchema(0, null)), Field("noPause", BooleanSchema()), Field("p95FrameUs", IntegerSchema(0, null)), Field("resolution", EnumSchema("1280x720", "1920x1080")), Field("sampleSeconds", ConstSchema(60)), Field("scenario", EnumSchema("baseline", "stress")), Field("status", EnumSchema("PASS", "FAIL")), Field("warmupSeconds", ConstSchema(10)));
        }

        private static CanonicalJsonValue VisualPayloadSchema(CanonicalJsonValue observation)
        {
            const string literal = "overbless.visual/v1";
            var identify = ObjectSchema(
                Field("observations", ArraySchema(observation, 6, 6, false)),
                Field("resolutions", ArraySchema(EnumSchema("1280x720", "1920x1080"), 2, 2, true)),
                Field("schema", ConstSchema(literal)),
                Field("testerIds", ArraySchema(TesterIdSchema(), 3, 3, true)));
            var hitDisplay = ObjectSchema(
                Field("grayscaleDistinct", BooleanSchema()),
                Field("letterboxPass", BooleanSchema()),
                Field("sameWorldBounds", BooleanSchema()),
                Field("schema", ConstSchema(literal)),
                Field("sharedGeometry", BooleanSchema()));
            return OneOfSchema(identify, hitDisplay);
        }

        private static CanonicalJsonValue PerformancePayloadSchema(CanonicalJsonValue bucket)
        {
            return PayloadObject("overbless.performance/v1",
                Field("allForeground", BooleanSchema()),
                Field("browser", EnumSchema("Chrome", "Edge")),
                Field("bucketOriginMicroseconds", IntegerSchema(0, null)),
                Field("buckets", ArraySchema(bucket, 60, 60, false)),
                Field("longestFrameUs", IntegerSchema(0, null)),
                Field("noPause", BooleanSchema()),
                Field("p95FrameUs", IntegerSchema(0, null)),
                Field("resolution", EnumSchema("1280x720", "1920x1080")),
                Field("sampleSeconds", ConstSchema(60)),
                Field("scenario", EnumSchema("baseline", "stress")),
                Field("status", EnumSchema("PASS", "FAIL")),
                Field("warmupSeconds", ConstSchema(10)));
        }

        private static EvidenceSchemaDefinition DefineGateDecision()
        {
            return Define("gate-decision-v1.schema.json", EvidenceContracts.GateDecision,
                Field("candidateId", TextSchema(1)), Field("decidedBy", ConstSchema("user")), Field("decidedUtc", ExactStringSchema("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", 24)), Field("decision", EnumSchema("PASS", "REWORK")), Field("evidenceManifestSha256", HashSchema()), Field("signatureAlgorithm", ConstSchema("RSA-SHA256")), Field("signatureBase64", TextSchema(1)), Field("trustAnchor", TextSchema(1)), Field("userAttestation", TextSchema(1)), Field("validatorReportSha256", HashSchema()));
        }

        private static EvidenceSchemaDefinition Define(string fileName, string literal, params SchemaField[] fields)
        {
            var required = new List<string> { "schema" };
            var properties = new List<CanonicalJsonProperty> { Property("schema", ConstSchema(literal)) };
            foreach (var field in fields)
            {
                required.Add(field.Name);
                properties.Add(Property(field.Name, field.Schema));
            }
            required.Sort(CanonicalJson.CompareUtf8Ordinal);
            return new EvidenceSchemaDefinition(fileName, literal, required.ToArray(), RootSchema(literal,
                Property("properties", CanonicalJsonValue.Object(properties)),
                Property("required", StringArray(required)),
                Property("type", CanonicalJsonValue.String("object")),
                Property("additionalProperties", CanonicalJsonValue.Boolean(false))));
        }

        private static EvidencePayloadDefinition Payload(string payloadType, string schemaLiteral, params string[] requiredKeys) => new EvidencePayloadDefinition(payloadType, schemaLiteral, requiredKeys);
        private static SchemaField Field(string name, CanonicalJsonValue schema) => new SchemaField(name, schema);
        private static CanonicalJsonValue RootSchema(string literal, params CanonicalJsonProperty[] fields)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                Property("$id", CanonicalJsonValue.String(literal)),
                Property("$schema", CanonicalJsonValue.String(Draft202012))
            };
            properties.AddRange(fields);
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue ObjectSchema(params SchemaField[] fields)
        {
            var required = new List<string>();
            var properties = new List<CanonicalJsonProperty>();
            foreach (var field in fields)
            {
                required.Add(field.Name);
                properties.Add(Property(field.Name, field.Schema));
            }
            required.Sort(CanonicalJson.CompareUtf8Ordinal);
            return Object(
                Property("additionalProperties", CanonicalJsonValue.Boolean(false)),
                Property("properties", CanonicalJsonValue.Object(properties)),
                Property("required", StringArray(required)),
                Property("type", CanonicalJsonValue.String("object")));
        }
        private static CanonicalJsonValue PayloadObject(string literal, params SchemaField[] fields)
        {
            var schemaFields = new List<SchemaField> { Field("schema", ConstSchema(literal)) };
            schemaFields.AddRange(fields);
            return ObjectSchema(schemaFields.ToArray());
        }

        private static CanonicalJsonValue OneOfSchema(params CanonicalJsonValue[] alternatives)
        {
            return Object(Property("oneOf", CanonicalJsonValue.Array(alternatives)));
        }


        private static CanonicalJsonValue OpenHashObjectSchema()
        {
            return Object(
                Property("additionalProperties", HashSchema()),
                Property("type", CanonicalJsonValue.String("object")));
        }

        private static CanonicalJsonValue ArraySchema(CanonicalJsonValue item, int minimum, int? maximum, bool unique)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                Property("items", item),
                Property("minItems", CanonicalJsonValue.Number(minimum)),
                Property("type", CanonicalJsonValue.String("array"))
            };
            if (maximum.HasValue) properties.Add(Property("maxItems", CanonicalJsonValue.Number(maximum.Value)));
            if (unique) properties.Add(Property("uniqueItems", CanonicalJsonValue.Boolean(true)));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue NullableSchema(CanonicalJsonValue schema) => Object(Property("anyOf", CanonicalJsonValue.Array(new[] { schema, Object(Property("type", CanonicalJsonValue.String("null"))) })));
        private static CanonicalJsonValue BooleanSchema() => Object(Property("type", CanonicalJsonValue.String("boolean")));
        private static CanonicalJsonValue ConstSchema(string value) => Object(Property("const", CanonicalJsonValue.String(value)));
        private static CanonicalJsonValue ConstSchema(long value) => Object(Property("const", CanonicalJsonValue.Number(value)));
        private static CanonicalJsonValue ConstSchema(bool value) => Object(Property("const", CanonicalJsonValue.Boolean(value)));
        private static CanonicalJsonValue HashSchema() => ExactStringSchema("^[0-9a-f]{64}$", 64);
        private static CanonicalJsonValue PathSchema() => StringSchema("^(?!.*(?:^|/)(?:\\.|\\.\\.)(?:/|$))(?!.*//)[^/\\\\:]+(?:/[^/\\\\:]+)*$", 1);
        private static CanonicalJsonValue TesterIdSchema() => TextSchema(1);
        private static CanonicalJsonValue AudioEventNameSchema() => EnumSchema("DasherReady", "ArcherReady", "ExitOpened");
        private static CanonicalJsonValue CriterionSchema() => EnumSchema(EvidenceContracts.CriterionIds);
        private static CanonicalJsonValue TextSchema(int minimum) => StringSchema(null, minimum);
        private static CanonicalJsonValue ExactStringSchema(string pattern, int length) => StringSchema(pattern, length, length);
        private static CanonicalJsonValue StringSchema(string pattern, int minimum) => StringSchema(pattern, minimum, null);
        private static CanonicalJsonValue StringSchema(string pattern, int minimum, int? maximum)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                Property("minLength", CanonicalJsonValue.Number(minimum)),
                Property("type", CanonicalJsonValue.String("string"))
            };
            if (maximum.HasValue) properties.Add(Property("maxLength", CanonicalJsonValue.Number(maximum.Value)));
            if (!string.IsNullOrEmpty(pattern)) properties.Add(Property("pattern", CanonicalJsonValue.String(pattern)));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue IntegerSchema(long minimum, long? maximum)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                Property("minimum", CanonicalJsonValue.Number(minimum)),
                Property("type", CanonicalJsonValue.String("integer"))
            };
            if (maximum.HasValue) properties.Add(Property("maximum", CanonicalJsonValue.Number(maximum.Value)));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue NumberSchema(double minimum, double? maximum)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                Property("minimum", CanonicalJsonValue.Number(minimum)),
                Property("type", CanonicalJsonValue.String("number"))
            };
            if (maximum.HasValue) properties.Add(Property("maximum", CanonicalJsonValue.Number(maximum.Value)));
            return CanonicalJsonValue.Object(properties);
        }

        private static CanonicalJsonValue EnumSchema(params string[] values) => Object(Property("enum", StringArray(values)));
        private static CanonicalJsonValue Object(params CanonicalJsonProperty[] values) => CanonicalJsonValue.Object(values);
        private static CanonicalJsonProperty Property(string name, CanonicalJsonValue value) => new CanonicalJsonProperty(name, value);
        private static CanonicalJsonValue StringArray(IEnumerable<string> values)
        {
            var result = new List<CanonicalJsonValue>();
            foreach (var value in values) result.Add(CanonicalJsonValue.String(value));
            return CanonicalJsonValue.Array(result);
        }

        private static CanonicalJsonValue BuildSettingsSchema()
        {
            return ObjectSchema(
                Field("autoconnectProfiler", BooleanSchema()), Field("compressionFormat", TextSchema(1)), Field("decompressionFallback", BooleanSchema()), Field("deepProfiling", BooleanSchema()), Field("development", BooleanSchema()), Field("exceptionSupport", TextSchema(1)), Field("memorySizeMb", IntegerSchema(1, null)), Field("scene", PathSchema()), Field("target", ConstSchema("WebGL")), Field("unityVersion", ConstSchema("6000.0.72f1")));
        }

        private static bool TryGetCommandLineArgument(string name, out string value)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    value = arguments[index + 1];
                    return !string.IsNullOrEmpty(value);
                }
            }

            value = null;
            return false;
        }

        private sealed class SchemaField
        {
            public SchemaField(string name, CanonicalJsonValue schema)
            {
                Name = name;
                Schema = schema;
            }

            public string Name { get; }
            public CanonicalJsonValue Schema { get; }
        }
    }
}
