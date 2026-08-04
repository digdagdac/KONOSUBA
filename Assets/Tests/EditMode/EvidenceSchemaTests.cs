using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using NUnit.Framework;
using Overbless.Editor.Evidence;

namespace Overbless.Tests.EditMode
{
    public sealed class EvidenceSchemaTests
    {
        [Test]
        public void EvidenceContracts_ExposeApprovedSchemasCriteriaAndCheckOrder()
        {
            Assert.That(EvidenceContracts.Candidate, Is.EqualTo("overbless.candidate/v1"));
            Assert.That(EvidenceContracts.SourceManifest, Is.EqualTo("overbless.source-manifest/v1"));
            Assert.That(EvidenceContracts.BuildManifest, Is.EqualTo("overbless.build/v1"));
            Assert.That(EvidenceContracts.EvidenceManifest, Is.EqualTo("overbless.evidence/v1"));
            Assert.That(EvidenceContracts.ValidatorReport, Is.EqualTo("overbless.validator-report/v1"));
            Assert.That(EvidenceContracts.GateDecision, Is.EqualTo("overbless.gate-decision/v1"));
            Assert.That(EvidenceContracts.AudioRandomization, Is.EqualTo("overbless.audio-randomization/v1"));

            CollectionAssert.AreEqual(
                new[]
                {
                    "AUD-BLIND-001", "AUD-ONCE-002", "BLS-EFFECT-001", "BLS-SEAL-002", "CMB-ATTACK-001", "EXT-M2-001",
                    "FND-DISPLAY-002", "FND-RULES-003", "FND-UNITY-001", "FUN-GUIDED-001", "FUN-UNDERSTAND-002", "PLY-LIFE-001",
                    "ROOM-SOUL-001", "VIS-HIT-002", "VIS-IDENTIFY-001", "WEB-INPUT-001", "WEB-PERF-002", "WEB-START-003"
                },
                EvidenceContracts.CriterionIds);
            CollectionAssert.AreEqual(
                new[]
                {
                    "SCHEMA", "IDENTITY", "TRANSITION", "INVENTORY", "HASHES", "COVERAGE", "SOURCE_GATE", "BUILD_GATE",
                    "TESTERS", "BROWSER_MATRIX", "PERFORMANCE", "AUDIO"
                },
                EvidenceContracts.Checks);
        }

        [Test]
        public void EvidenceContracts_SelectDetailUsesDeclaredFailurePrecedence()
        {
            var schemaFailures = new HashSet<string> { "TYPE", "DUPLICATE", "MISSING_KEY" };
            var performanceFailures = new HashSet<string> { "FPS", "FRAME_COUNT", "MISSING_CELL" };

            Assert.That(EvidenceContracts.SelectDetail("SCHEMA", schemaFailures), Is.EqualTo("MISSING_KEY"));
            Assert.That(EvidenceSchemaValidator.SelectDetail("SCHEMA", schemaFailures), Is.EqualTo("MISSING_KEY"));
            Assert.That(EvidenceContracts.SelectDetail("PERFORMANCE", performanceFailures), Is.EqualTo("MISSING_CELL"));
            Assert.That(EvidenceContracts.SelectDetail("AUDIO", new HashSet<string> { "BLIND_FAIL" }), Is.EqualTo("BLIND_FAIL"));
            Assert.That(EvidenceContracts.SelectDetail("AUDIO", new HashSet<string>()), Is.EqualTo("OK"));
            Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceContracts.SelectDetail("UNKNOWN", new HashSet<string>()));
        }

        [Test]
        public void CanonicalJson_SortsKeysRejectsNonCanonicalBytesAndNormalizesPaths()
        {
            var value = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("z", CanonicalJsonValue.Number(2)),
                new CanonicalJsonProperty("a", CanonicalJsonValue.String("x")));
            var canonicalText = "{\"a\":\"x\",\"z\":2}";
            var canonicalBytes = Encoding.UTF8.GetBytes(canonicalText);

            Assert.That(CanonicalJson.Serialize(value), Is.EqualTo(canonicalText));
            Assert.That(CanonicalJson.Sha256Hex(canonicalBytes), Is.EqualTo(
                "9a663862a36f231820d8cd1275334dd6371c0ffd5f4e02fe51ddcc20c63465a2"));
            Assert.That(CanonicalJson.TryParseCanonicalUtf8(canonicalBytes, out var parsed, out var parseError), Is.True, parseError);
            Assert.That(parsed.Kind, Is.EqualTo(CanonicalJsonKind.Object));
            Assert.That(CanonicalJson.TryParseCanonicalUtf8(Encoding.UTF8.GetBytes("{\"z\":2,\"a\":\"x\"}"), out _, out _), Is.False);
            Assert.That(CanonicalJson.NormalizeRelativePath("evidence/candidate.json"), Is.EqualTo("evidence/candidate.json"));
            Assert.That(CanonicalJson.IsNormalizedRelativePath("evidence/candidate.json"), Is.True);
            Assert.That(CanonicalJson.IsNormalizedRelativePath("evidence\\candidate.json"), Is.False);
            Assert.Throws<ArgumentException>(() => CanonicalJson.NormalizeRelativePath("evidence/../candidate.json"));
        }
        [Test]
        public void CanonicalJson_StreamHashingHandlesEmptyAndChunkLimitedStreams()
        {
            using (var emptyStream = new MemoryStream())
            {
                var emptyHash = CanonicalJson.Sha256Hex(emptyStream, out var emptyLength);
                Assert.That(emptyHash, Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
                Assert.That(emptyLength, Is.Zero);
            }

            using (var chunkLimitedStream = new ChunkLimitedMemoryStream(Encoding.UTF8.GetBytes("abc"), 1))
            {
                var chunkLimitedHash = CanonicalJson.Sha256Hex(chunkLimitedStream, out var chunkLimitedLength);
                Assert.That(chunkLimitedHash, Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
                Assert.That(chunkLimitedLength, Is.EqualTo(3L));
            }
        }

        [Test]
        public void M2EntryGateValidator_DetachedSignatureBindsUserAttestation()
        {
            const string anchorVariable = "OVERBLESS_M2_GATE_TRUST_ANCHOR";
            const string keyVariable = "OVERBLESS_M2_GATE_TRUSTED_PUBLIC_KEY_SPKI_BASE64";
            const string candidateId = "candidate-1";
            const string outcome = "PASS";
            const string decidedUtc = "2026-07-13T12:00:00.000Z";
            const string attestation = "I reviewed the exact sealed candidate.";
            const string trustAnchor = "test-anchor";
            var evidenceHash = new string('a', 64);
            var reportHash = new string('b', 64);
            var previousAnchor = Environment.GetEnvironmentVariable(anchorVariable);
            var previousKey = Environment.GetEnvironmentVariable(keyVariable);
            try
            {
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    Environment.SetEnvironmentVariable(anchorVariable, trustAnchor);
                    Environment.SetEnvironmentVariable(
                        keyVariable,
                        Convert.ToBase64String(EncodeSubjectPublicKeyInfo(rsa.ExportParameters(false))));
                    var payload = CanonicalJson.SerializeUtf8(CanonicalJsonValue.Object(
                        new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(candidateId)),
                        new CanonicalJsonProperty("decidedUtc", CanonicalJsonValue.String(decidedUtc)),
                        new CanonicalJsonProperty("decision", CanonicalJsonValue.String(outcome)),
                        new CanonicalJsonProperty("evidenceManifestSha256", CanonicalJsonValue.String(evidenceHash)),
                        new CanonicalJsonProperty("userAttestation", CanonicalJsonValue.String(attestation)),
                        new CanonicalJsonProperty("validatorReportSha256", CanonicalJsonValue.String(reportHash))));
                    var writerType = typeof(M2EntryGateValidator).Assembly.GetType(
                        "Overbless.Editor.Evidence.GateDecisionWriter");
                    Assert.That(writerType, Is.Not.Null);
                    var createWriterPayload = writerType.GetMethod(
                        "CreateSigningPayload",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    Assert.That(createWriterPayload, Is.Not.Null);
                    CollectionAssert.AreEqual(
                        payload,
                        (byte[])createWriterPayload.Invoke(
                            null,
                            new object[]
                            {
                                candidateId,
                                evidenceHash,
                                reportHash,
                                outcome,
                                decidedUtc,
                                attestation
                            }));
                    var signature = Convert.ToBase64String(
                        rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                    var verify = typeof(M2EntryGateValidator).GetMethod(
                        "VerifyDetachedUserDecisionSignature",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    Assert.That(verify, Is.Not.Null);

                    var validErrors = new List<string>();
                    var validResult = (bool)verify.Invoke(
                        null,
                        new object[]
                        {
                            candidateId,
                            evidenceHash,
                            reportHash,
                            outcome,
                            decidedUtc,
                            attestation,
                            trustAnchor,
                            "RSA-SHA256",
                            signature,
                            validErrors
                        });
                    Assert.That(validResult, Is.True, string.Join(" | ", validErrors));
                    Assert.That(validErrors, Is.Empty);

                    var tamperedErrors = new List<string>();
                    Assert.That(
                        verify.Invoke(
                            null,
                            new object[]
                            {
                                candidateId,
                                evidenceHash,
                                reportHash,
                                outcome,
                                decidedUtc,
                                attestation + " Altered.",
                                trustAnchor,
                                "RSA-SHA256",
                                signature,
                                tamperedErrors
                            }),
                        Is.EqualTo(false));
                    Assert.That(tamperedErrors, Is.Not.Empty);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(anchorVariable, previousAnchor);
                Environment.SetEnvironmentVariable(keyVariable, previousKey);
            }
        }

        [Test]
        public void EvidenceSchemaValidator_RejectsPublicSchemaCriteriaAndReportCheckMutations()
        {
            var schemaObject = CreateCandidate(EvidenceContracts.Candidate);
            var requiredKeys = new[]
            {
                "schema",
                "candidateId",
                "candidateSha256",
                "createdUtc",
                "scene",
                "sourceCommit",
                "unityVersion"
            };

            Assert.That(
                EvidenceSchemaValidator.ValidateSchemaObject(schemaObject, EvidenceContracts.Candidate, requiredKeys).IsValid,
                Is.True);
            Assert.That(
                EvidenceSchemaValidator.ValidateSchemaObject(
                    schemaObject.WithoutTopLevelProperty("schema"),
                    EvidenceContracts.Candidate,
                    requiredKeys).Code,
                Is.EqualTo("MISSING_KEY"));
            Assert.That(
                EvidenceSchemaValidator.ValidateSchemaObject(
                    CreateCandidate(EvidenceContracts.Candidate, includeUnexpected: true),
                    EvidenceContracts.Candidate,
                    requiredKeys).Code,
                Is.EqualTo("ADDITIONAL_KEY"));
            Assert.That(
                EvidenceSchemaValidator.ValidateSchemaObject(
                    CreateCandidate(EvidenceContracts.Candidate, duplicateSchema: true),
                    EvidenceContracts.Candidate,
                    requiredKeys).Code,
                Is.EqualTo("DUPLICATE"));
            Assert.That(
                EvidenceSchemaValidator.ValidateSchemaObject(
                    CreateCandidate("overbless.unknown/v1"),
                    EvidenceContracts.Candidate,
                    requiredKeys).Code,
                Is.EqualTo("UNKNOWN_SCHEMA"));

            foreach (var schemaLiteral in new[]
                     {
                         EvidenceContracts.Candidate,
                         EvidenceContracts.SourceManifest,
                         EvidenceContracts.BuildManifest,
                         EvidenceContracts.EvidenceManifest,
                         EvidenceContracts.ValidatorReport,
                         EvidenceContracts.GateDecision,
                         EvidenceContracts.AudioRandomization
                     })
            {
                Assert.That(
                    EvidenceSchemaWriter.TryGetSchemaDefinition(schemaLiteral, out var definition),
                    Is.True);
                Assert.That(definition.SchemaLiteral, Is.EqualTo(schemaLiteral));
            }

            var criteria = CreateAllCriteria();
            Assert.That(EvidenceSchemaValidator.ValidateCriteria(criteria, true).IsValid, Is.True);
            var missingCriterion = new List<CanonicalJsonValue>(criteria);
            missingCriterion.RemoveAt(missingCriterion.Count - 1);
            Assert.That(EvidenceSchemaValidator.ValidateCriteria(missingCriterion, true).Code, Is.EqualTo("UNCOVERED"));
            var duplicateCriterion = new List<CanonicalJsonValue>(criteria);
            duplicateCriterion[1] = duplicateCriterion[0];
            Assert.That(EvidenceSchemaValidator.ValidateCriteria(duplicateCriterion, true).Code, Is.EqualTo("DUPLICATE"));
            var unknownCriterion = new List<CanonicalJsonValue>(criteria);
            unknownCriterion[unknownCriterion.Count - 1] = CanonicalJsonValue.String("ZZZ-UNKNOWN-001");
            Assert.That(EvidenceSchemaValidator.ValidateCriteria(unknownCriterion, true).Code, Is.EqualTo("UNKNOWN_CRITERION"));

            var reportChecks = CreatePassingReportChecks();
            Assert.That(EvidenceSchemaValidator.ValidateReportChecks(reportChecks).IsValid, Is.True);
            for (var index = 0; index < EvidenceContracts.Checks.Length; index++)
            {
                var mutatedChecks = new List<CanonicalJsonValue>(reportChecks.Items);
                mutatedChecks[index] = CreateReportCheck(EvidenceContracts.Checks[index], "FAIL", "NOT_DECLARED");
                var result = EvidenceSchemaValidator.ValidateReportChecks(CanonicalJsonValue.Array(mutatedChecks));
                Assert.That(result.Code, Is.EqualTo("ENUM"), $"Expected {EvidenceContracts.Checks[index]} mutation to reject.");
            }
        }

        [Test]
        public void EvidenceSchemaValidator_RequiresThreeUniqueAudioEventsAndBlindTesterOrders()
        {
            var validOrder = new[] { "ArcherReady", "DasherReady", "ExitOpened" };
            var buildManifestSha256 = new string('a', 64);
            var randomization = CreateAudioRandomization(
                buildManifestSha256,
                CreateAudioOrder("tester-a", validOrder),
                CreateAudioOrder("tester-b", new[] { "DasherReady", "ExitOpened", "ArcherReady" }),
                CreateAudioOrder("tester-c", new[] { "ExitOpened", "ArcherReady", "DasherReady" }));

            var validPermutation = EvidenceSchemaValidator.ValidateAudioPermutation(validOrder);
            var duplicatePermutation = EvidenceSchemaValidator.ValidateAudioPermutation(
                new[] { "ArcherReady", "ArcherReady", "ExitOpened" });
            var missingPermutation = EvidenceSchemaValidator.ValidateAudioPermutation(
                new[] { "ArcherReady", "DasherReady" });
            var randomizationResult = EvidenceSchemaValidator.ValidateAudioRandomization(
                randomization,
                "candidate-1",
                buildManifestSha256,
                new[] { "tester-a", "tester-b", "tester-c" });
            var duplicateOrderResult = EvidenceSchemaValidator.ValidateAudioRandomization(
                CreateAudioRandomization(
                    buildManifestSha256,
                    CreateAudioOrder("tester-a", new[] { "ArcherReady", "ArcherReady", "ExitOpened" }),
                    CreateAudioOrder("tester-b", new[] { "DasherReady", "ExitOpened", "ArcherReady" }),
                    CreateAudioOrder("tester-c", new[] { "ExitOpened", "ArcherReady", "DasherReady" })),
                "candidate-1",
                buildManifestSha256,
                new[] { "tester-a", "tester-b", "tester-c" });
            var buildBindingResult = EvidenceSchemaValidator.ValidateAudioRandomization(
                randomization,
                "candidate-1",
                new string('b', 64),
                new[] { "tester-a", "tester-b", "tester-c" });
            var candidateBindingResult = EvidenceSchemaValidator.ValidateAudioRandomization(
                CreateAudioRandomization(
                    "candidate-2",
                    buildManifestSha256,
                    CreateAudioOrder("tester-a", validOrder),
                    CreateAudioOrder("tester-b", new[] { "DasherReady", "ExitOpened", "ArcherReady" }),
                    CreateAudioOrder("tester-c", new[] { "ExitOpened", "ArcherReady", "DasherReady" })),
                "candidate-1",
                buildManifestSha256,
                new[] { "tester-a", "tester-b", "tester-c" });

            Assert.That(validPermutation.IsValid, Is.True, validPermutation.Message);
            Assert.That(duplicatePermutation.Code, Is.EqualTo("EVENT_DUPLICATE"));
            Assert.That(missingPermutation.Code, Is.EqualTo("EVENT_MISSING"));
            Assert.That(randomizationResult.IsValid, Is.True, randomizationResult.Message);
            Assert.That(duplicateOrderResult.Code, Is.EqualTo("EVENT_DUPLICATE"));
            Assert.That(buildBindingResult.Code, Is.EqualTo("BUILD_HASH"));
            Assert.That(candidateBindingResult.Code, Is.EqualTo("ID_MISMATCH"));
        }

        [Test]
        public void EvidenceSchemaValidator_UsesSixtyHalfOpenPerformanceBuckets()
        {
            const long origin = 100L;
            var buckets = CreatePerformanceBuckets(origin, 1L);
            var completionTimes = new List<long>();
            for (var index = 0; index < buckets.Count; index++)
            {
                completionTimes.Add(origin + index * 1000000L);
            }

            var validResult = EvidenceSchemaValidator.ValidatePerformanceBuckets(origin, buckets, completionTimes);
            var outsideSample = new List<long>(completionTimes)
            {
                origin + 60L * 1000000L
            };
            var outsideResult = EvidenceSchemaValidator.ValidatePerformanceBuckets(origin, buckets, outsideSample);
            var invalidBoundaryBuckets = new List<PerformanceBucket>(buckets);
            var finalBucket = invalidBoundaryBuckets[invalidBoundaryBuckets.Count - 1];
            invalidBoundaryBuckets[invalidBoundaryBuckets.Count - 1] = new PerformanceBucket(
                finalBucket.Index,
                finalBucket.StartUs,
                finalBucket.EndUs + 1L,
                finalBucket.CompletedFrames,
                finalBucket.MinFpsEquivalent);
            var invalidBoundaryResult = EvidenceSchemaValidator.ValidatePerformanceBuckets(
                origin,
                invalidBoundaryBuckets,
                completionTimes);

            Assert.That(validResult.IsValid, Is.True, validResult.Message);
            Assert.That(outsideResult.Code, Is.EqualTo("FRAME_COUNT"));
            Assert.That(invalidBoundaryResult.Code, Is.EqualTo("FRAME_COUNT"));
        }
        [Test]
        public void EvidenceSchemaValidator_RejectsPublicPerformancePayloadMutations()
        {
            const long origin = 100L;
            var buckets = CreatePerformanceBuckets(origin, 45L);
            var completionTimes = CreateCompletionTimes(origin, 45);
            var frameDurations = new List<long>();
            for (var index = 0; index < completionTimes.Count; index++)
            {
                frameDurations.Add(10000L);
            }

            var validResult = EvidenceSchemaValidator.ValidatePerformance(
                CreatePerformancePayload("Chrome", true, true, "PASS", buckets),
                completionTimes,
                frameDurations,
                "Chrome",
                "1280x720",
                "baseline");
            var missingCellResult = EvidenceSchemaValidator.ValidatePerformance(
                CreatePerformancePayload("Edge", true, true, "PASS", buckets),
                completionTimes,
                frameDurations,
                "Chrome",
                "1280x720",
                "baseline");
            var focusResult = EvidenceSchemaValidator.ValidatePerformance(
                CreatePerformancePayload("Chrome", false, true, "PASS", buckets),
                completionTimes,
                frameDurations,
                "Chrome",
                "1280x720",
                "baseline");
            var statusResult = EvidenceSchemaValidator.ValidatePerformance(
                CreatePerformancePayload("Chrome", true, true, "FAIL", buckets),
                completionTimes,
                frameDurations,
                "Chrome",
                "1280x720",
                "baseline");

            Assert.That(validResult.IsValid, Is.True, validResult.Message);
            Assert.That(missingCellResult.Code, Is.EqualTo("MISSING_CELL"));
            Assert.That(focusResult.Code, Is.EqualTo("FOREGROUND"));
            Assert.That(statusResult.Code, Is.EqualTo("FPS"));
        }


        private static CanonicalJsonValue CreateAudioRandomization(
            string buildManifestSha256,
            params CanonicalJsonValue[] orders)
        {
            return CreateAudioRandomization("candidate-1", buildManifestSha256, orders);
        }

        private static CanonicalJsonValue CreateAudioRandomization(
            string candidateId,
            string buildManifestSha256,
            params CanonicalJsonValue[] orders)
        {
            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("buildManifestSha256", CanonicalJsonValue.String(buildManifestSha256)),
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(candidateId)),
                new CanonicalJsonProperty("orders", CanonicalJsonValue.Array(orders)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(EvidenceContracts.AudioRandomization)),
                new CanonicalJsonProperty("seed", CanonicalJsonValue.Number(7)));
        }

        private static CanonicalJsonValue CreateAudioOrder(string testerId, IEnumerable<string> eventOrder)
        {
            var events = new List<CanonicalJsonValue>();
            foreach (var eventName in eventOrder)
            {
                events.Add(CanonicalJsonValue.String(eventName));
            }

            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("eventOrder", CanonicalJsonValue.Array(events)),
                new CanonicalJsonProperty("testerId", CanonicalJsonValue.String(testerId)));
        }

        private static List<CanonicalJsonValue> CreateAllCriteria()
        {
            var criteria = new List<CanonicalJsonValue>();
            foreach (var criterionId in EvidenceContracts.CriterionIds)
            {
                criteria.Add(CanonicalJsonValue.String(criterionId));
            }

            return criteria;
        }

        private static CanonicalJsonValue CreatePassingReportChecks()
        {
            var checks = new List<CanonicalJsonValue>();
            foreach (var checkId in EvidenceContracts.Checks)
            {
                checks.Add(CreateReportCheck(checkId, "PASS", "OK"));
            }

            return CanonicalJsonValue.Array(checks);
        }

        private static CanonicalJsonValue CreateCandidate(
            string schema,
            bool includeUnexpected = false,
            bool duplicateSchema = false)
        {
            var properties = new List<CanonicalJsonProperty>
            {
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String("candidate-1")),
                new CanonicalJsonProperty("candidateSha256", CanonicalJsonValue.String(new string('a', 64))),
                new CanonicalJsonProperty("createdUtc", CanonicalJsonValue.String("2026-07-13T00:00:00.000Z")),
                new CanonicalJsonProperty("scene", CanonicalJsonValue.String("Assets/_Project/Scenes/M1_GuidedValidation.unity")),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(schema))
            };
            if (duplicateSchema)
            {
                properties.Add(new CanonicalJsonProperty("schema", CanonicalJsonValue.String(schema)));
            }

            properties.Add(new CanonicalJsonProperty("sourceCommit", CanonicalJsonValue.String(new string('b', 40))));
            if (includeUnexpected)
            {
                properties.Add(new CanonicalJsonProperty("unexpected", CanonicalJsonValue.Boolean(true)));
            }

            properties.Add(new CanonicalJsonProperty("unityVersion", CanonicalJsonValue.String("6000.0.72f1")));
            return CanonicalJsonValue.Object(properties);
        }
        private static CanonicalJsonValue CreateReportCheck(string checkId, string status, string detailCode)
        {
            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("checkId", CanonicalJsonValue.String(checkId)),
                new CanonicalJsonProperty("detailCode", CanonicalJsonValue.String(detailCode)),
                new CanonicalJsonProperty("status", CanonicalJsonValue.String(status)));
        }

        private static CanonicalJsonValue CreatePerformancePayload(
            string browser,
            bool allForeground,
            bool noPause,
            string status,
            IReadOnlyList<PerformanceBucket> buckets)
        {
            var bucketValues = new List<CanonicalJsonValue>();
            foreach (var bucket in buckets)
            {
                bucketValues.Add(CanonicalJsonValue.Object(
                    new CanonicalJsonProperty("completedFrames", CanonicalJsonValue.Number(bucket.CompletedFrames)),
                    new CanonicalJsonProperty("endUs", CanonicalJsonValue.Number(bucket.EndUs)),
                    new CanonicalJsonProperty("index", CanonicalJsonValue.Number(bucket.Index)),
                    new CanonicalJsonProperty("minFpsEquivalent", CanonicalJsonValue.Number(bucket.MinFpsEquivalent)),
                    new CanonicalJsonProperty("startUs", CanonicalJsonValue.Number(bucket.StartUs))));
            }

            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("allForeground", CanonicalJsonValue.Boolean(allForeground)),
                new CanonicalJsonProperty("browser", CanonicalJsonValue.String(browser)),
                new CanonicalJsonProperty("bucketOriginMicroseconds", CanonicalJsonValue.Number(100L)),
                new CanonicalJsonProperty("buckets", CanonicalJsonValue.Array(bucketValues)),
                new CanonicalJsonProperty("longestFrameUs", CanonicalJsonValue.Number(10000L)),
                new CanonicalJsonProperty("noPause", CanonicalJsonValue.Boolean(noPause)),
                new CanonicalJsonProperty("p95FrameUs", CanonicalJsonValue.Number(10000L)),
                new CanonicalJsonProperty("resolution", CanonicalJsonValue.String("1280x720")),
                new CanonicalJsonProperty("sampleSeconds", CanonicalJsonValue.Number(60L)),
                new CanonicalJsonProperty("scenario", CanonicalJsonValue.String("baseline")),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String("overbless.performance/v1")),
                new CanonicalJsonProperty("status", CanonicalJsonValue.String(status)),
                new CanonicalJsonProperty("warmupSeconds", CanonicalJsonValue.Number(10L)));
        }

        [Test]
        public void M2Validator_ArtifactSnapshotRetainsOneBoundedPrivateCopy()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Ignore("The M2 no-follow artifact snapshot contract is Windows-specific.");
            }

            var sourcePath = Path.Combine(
                Path.GetTempPath(),
                "overbless-snapshot-test-" + Guid.NewGuid().ToString("N") + ".bin");
            var originalBytes = Encoding.UTF8.GetBytes("sealed artifact");
            File.WriteAllBytes(sourcePath, originalBytes);

            object cache = null;
            try
            {
                var cacheType = typeof(M2EntryGateValidator).GetNestedType(
                    "ArtifactSnapshotCache",
                    BindingFlags.NonPublic);
                Assert.That(cacheType, Is.Not.Null);
                cache = Activator.CreateInstance(cacheType, true);
                var get = cacheType.GetMethod(
                    "Get",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(get, Is.Not.Null);

                var snapshot = get.Invoke(cache, new object[] { sourcePath });
                Assert.That(snapshot, Is.Not.Null);
                var snapshotType = snapshot.GetType();
                Assert.That(
                    snapshotType.GetProperty("Size").GetValue(snapshot),
                    Is.EqualTo((long)originalBytes.Length));
                Assert.That(
                    snapshotType.GetProperty("Sha256").GetValue(snapshot),
                    Is.EqualTo(CanonicalJson.Sha256Hex(originalBytes)));

                File.WriteAllBytes(sourcePath, Encoding.UTF8.GetBytes("changed after snapshot"));
                var retainedBytes = (byte[])snapshotType.GetProperty("Bytes").GetValue(snapshot);
                CollectionAssert.AreEqual(originalBytes, retainedBytes);
            }
            finally
            {
                if (cache is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }
        private static byte[] EncodeSubjectPublicKeyInfo(RSAParameters parameters)
        {
            var rsaPublicKey = EncodeDer(
                0x30,
                Combine(
                    EncodeDerInteger(parameters.Modulus),
                    EncodeDerInteger(parameters.Exponent)));
            var algorithmIdentifier = EncodeDer(
                0x30,
                new byte[]
                {
                    0x06, 0x09, 0x2a, 0x86, 0x48, 0x86, 0xf7,
                    0x0d, 0x01, 0x01, 0x01, 0x05, 0x00
                });
            return EncodeDer(
                0x30,
                Combine(
                    algorithmIdentifier,
                    EncodeDer(0x03, Combine(new byte[] { 0x00 }, rsaPublicKey))));
        }

        private static byte[] EncodeDerInteger(byte[] unsignedValue)
        {
            if (unsignedValue == null || unsignedValue.Length == 0)
            {
                throw new ArgumentException("RSA integer is empty.", nameof(unsignedValue));
            }

            var firstNonZero = 0;
            while (firstNonZero < unsignedValue.Length - 1 &&
                   unsignedValue[firstNonZero] == 0)
            {
                firstNonZero++;
            }

            var length = unsignedValue.Length - firstNonZero;
            var needsPositivePrefix = (unsignedValue[firstNonZero] & 0x80) != 0;
            var content = new byte[length + (needsPositivePrefix ? 1 : 0)];
            Buffer.BlockCopy(
                unsignedValue,
                firstNonZero,
                content,
                needsPositivePrefix ? 1 : 0,
                length);
            return EncodeDer(0x02, content);
        }

        private static byte[] EncodeDer(byte tag, byte[] content)
        {
            var length = EncodeDerLength(content.Length);
            var encoded = new byte[1 + length.Length + content.Length];
            encoded[0] = tag;
            Buffer.BlockCopy(length, 0, encoded, 1, length.Length);
            Buffer.BlockCopy(content, 0, encoded, 1 + length.Length, content.Length);
            return encoded;
        }

        private static byte[] EncodeDerLength(int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (length < 0x80)
            {
                return new[] { (byte)length };
            }

            var bytes = new List<byte>();
            var remaining = length;
            while (remaining > 0)
            {
                bytes.Insert(0, (byte)(remaining & 0xff));
                remaining >>= 8;
            }

            bytes.Insert(0, (byte)(0x80 | bytes.Count));
            return bytes.ToArray();
        }

        private static byte[] Combine(params byte[][] parts)
        {
            var length = 0;
            foreach (var part in parts)
            {
                length = checked(length + part.Length);
            }

            var combined = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, combined, offset, part.Length);
                offset += part.Length;
            }

            return combined;
        }
        private static List<long> CreateCompletionTimes(long origin, int completedFramesPerBucket)
        {
            var completionTimes = new List<long>();
            for (var bucket = 0; bucket < 60; bucket++)
            {
                for (var frame = 0; frame < completedFramesPerBucket; frame++)
                {
                    completionTimes.Add(origin + bucket * 1000000L + frame);
                }
            }

            return completionTimes;
        }

        private static List<PerformanceBucket> CreatePerformanceBuckets(long origin, long completedFrames)
        {
            var buckets = new List<PerformanceBucket>();
            for (var index = 0; index < 60; index++)
            {
                var start = origin + index * 1000000L;
                buckets.Add(new PerformanceBucket(index, start, start + 1000000L, completedFrames, completedFrames));
            }

            return buckets;
        }
        private sealed class ChunkLimitedMemoryStream : MemoryStream
        {
            private readonly int maximumReadSize;

            public ChunkLimitedMemoryStream(byte[] buffer, int maximumReadSize)
                : base(buffer, false)
            {
                if (maximumReadSize <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maximumReadSize));
                }

                this.maximumReadSize = maximumReadSize;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(count, maximumReadSize));
            }
        }
    }
}
