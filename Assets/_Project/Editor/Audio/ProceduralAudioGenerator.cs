using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Overbless.Editor.Evidence;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Audio
{
    public static class ProceduralAudioGenerator
    {
        private const int SampleRate = 22050;
        private const int Samples = SampleRate / 4;
        private const string ManifestHeader = "asset_id,category,filename,status,tool,model,created_at,prompt_file,source_file,final_file,manual_edits,license,approved_by,notes,sha256";
        private const string PendingReviewer = "pending-user-gate";
        private const string CleanupJournalDirectory = "Library/Overbless/ProceduralAudioCleanup";

        private static readonly (string Name, float Frequency, int Seed)[] Cues =
        {
            ("DasherReady", 180f, 104729),
            ("ArcherReady", 520f, 104731),
            ("AttackLocked", 760f, 104743),
            ("PlayerHit", 110f, 104759),
            ("SoulCollected", 940f, 104761),
            ("ExitOpened", 660f, 104773),
            // Core-loop cues. Frequencies stay clear of the existing set so each
            // cue remains identifiable without relying on volume or timing.
            ("BlessingApplied", 1180f, 104779),
            ("BlessingRejected", 240f, 104789),
            ("EnemyDefeated", 330f, 104801),
            ("FriendlyFireKill", 1480f, 104803)
        };

        public static void GenerateAll()
        {
            const string assetDirectory = "Assets/_Project/Audio/M1Functional";
            const string generationDirectory = "Docs/AI_Usage/generations";
            const string manifestPath = "Docs/AI_Usage/asset_manifest.csv";
            const string generatorSourcePath = "Assets/_Project/Editor/Audio/ProceduralAudioGenerator.cs";

            Directory.CreateDirectory(assetDirectory);
            Directory.CreateDirectory(generationDirectory);
            using var generationLock = AcquireGenerationLock();

            var toolVersion = Application.unityVersion;
            if (!string.Equals(toolVersion, "6000.0.72f1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Procedural audio must be generated with Unity 6000.0.72f1, not " + toolVersion + ".");
            }

            var generatorSourcePreimage =
                RequireCurrentGeneratorSource(generatorSourcePath);
            var managedOutputPaths =
                GetManagedOutputPaths(assetDirectory, generationDirectory, manifestPath);
            RecoverPublicationJournalTemporaries();
            var recovery = RecoverPublicationJournals();
            if (recovery.AssetFilesChanged)
            {
                AssetDatabase.Refresh();
            }

            ReportCleanupDiagnostics(recovery.CleanupDiagnostics);
            RequireNoPendingPublicationJournals(recovery.CleanupDiagnostics);
            RecoverOrphanedOutputTemporaries(managedOutputPaths);
            var outputPreimages = CaptureOutputPreimages(managedOutputPaths);
            var generatorSourceSha = generatorSourcePreimage.Sha256;
            var existingRecords = ReadManifestRecords(manifestPath);
            ValidateManagedManifestOwnership(
                existingRecords,
                assetDirectory,
                generationDirectory);
            var ownedIds = new HashSet<string>(StringComparer.Ordinal);
            var staged = new List<StagedOutput>();
            var generatedRows = new List<string>();
            PublicationResult publication;
            var publicationStarted = false;
            try
            {
                foreach (var cue in Cues)
                {
                    var assetId = "audio_" + cue.Name.ToLowerInvariant();
                    if (!ownedIds.Add(assetId))
                    {
                        throw new InvalidOperationException("Duplicate procedural audio asset ID: " + assetId);
                    }

                    var path = assetDirectory + "/" + cue.Name + ".wav";
                    var recordPath = generationDirectory + "/" + cue.Name + ".json";
                    var bytes = BuildWave(cue.Frequency, cue.Seed);
                    var sha = Sha(bytes);
                    var parameters = "sine+seeded-noise;frequencyHz=" + cue.Frequency.ToString(CultureInfo.InvariantCulture) + ";sampleRate=" + SampleRate + ";durationSeconds=0.25";
                    if (TryGetManifestRecord(existingRecords, assetId, out var existingManifestRecord))
                    {
                        ValidateOwnedManifestRecord(
                            existingManifestRecord,
                            assetId,
                            cue.Name,
                            cue.Seed,
                            parameters,
                            path,
                            recordPath,
                            generatorSourcePath);
                    }

                    var preserveExistingRecord = EnsureReviewedRecordIsStable(
                        recordPath,
                        cue.Name,
                        cue.Seed,
                        parameters,
                        path,
                        sha,
                        generatorSourceSha,
                        toolVersion);
                    var generatedAtUtc = ResolveStableGenerationTime(recordPath, cue.Name, cue.Seed, parameters, path, sha, generatorSourceSha, toolVersion);
                    var reviewer = ResolveStableReviewer(recordPath, cue.Name, cue.Seed, parameters, path, sha, generatorSourceSha, toolVersion);
                    var record = CanonicalJsonValue.Object(
                        new CanonicalJsonProperty("event", CanonicalJsonValue.String(cue.Name)),
                        new CanonicalJsonProperty("generatorName", CanonicalJsonValue.String("Overbless ProceduralAudioGenerator")),
                        new CanonicalJsonProperty("toolName", CanonicalJsonValue.String("Unity Editor")),
                        new CanonicalJsonProperty("toolVersion", CanonicalJsonValue.String(toolVersion)),
                        new CanonicalJsonProperty("generatorSourceSha256", CanonicalJsonValue.String(generatorSourceSha)),
                        new CanonicalJsonProperty("generationUtc", CanonicalJsonValue.String(generatedAtUtc)),
                        new CanonicalJsonProperty("seed", CanonicalJsonValue.Number(cue.Seed)),
                        new CanonicalJsonProperty("parametersOrInstruction", CanonicalJsonValue.String(parameters)),
                        new CanonicalJsonProperty("originalWavPath", CanonicalJsonValue.String(path)),
                        new CanonicalJsonProperty("originalWavSha256", CanonicalJsonValue.String(sha)),
                        new CanonicalJsonProperty("modifications", CanonicalJsonValue.Array()),
                        new CanonicalJsonProperty("finalWavPath", CanonicalJsonValue.String(path)),
                        new CanonicalJsonProperty("finalWavSha256", CanonicalJsonValue.String(sha)),
                        new CanonicalJsonProperty("reviewer", CanonicalJsonValue.String(reviewer)));

                    StageIfChanged(staged, path, bytes, outputPreimages);
                    if (!preserveExistingRecord)
                    {
                        StageIfChanged(
                            staged,
                            recordPath,
                            CanonicalJson.SerializeUtf8(record),
                            outputPreimages);
                    }

                    generatedRows.Add(BuildManifestRow(
                        assetId,
                        cue.Name,
                        path,
                        recordPath,
                        generatorSourcePath,
                        toolVersion,
                        generatedAtUtc,
                        reviewer,
                        generatorSourceSha,
                        sha));
                }

                var finalManifestLines = new List<string> { ManifestHeader };
                foreach (var record in existingRecords)
                {
                    if (!ownedIds.Contains(record.AssetId))
                    {
                        finalManifestLines.Add(record.Raw);
                    }
                }

                finalManifestLines.AddRange(generatedRows);
                var manifestBytes = new UTF8Encoding(false).GetBytes(string.Join("\n", finalManifestLines) + "\n");
                StageIfChanged(staged, manifestPath, manifestBytes, outputPreimages);
                RequireOutputPreimagesUnchanged(outputPreimages);
                RequireOutputPreimageUnchanged(generatorSourcePreimage);
                publicationStarted = true;
                publication = PublishAll(
                    staged,
                    outputPreimages,
                    generatorSourcePreimage);
            }
            catch (Exception generationException)
            {
                var failures = new List<Exception> { generationException };
                if (!publicationStarted)
                {
                    CleanupStaged(staged, failures);
                }
                throw new AggregateException(
                    publicationStarted
                        ? "Procedural audio publication failed; journal-owned recovery artifacts were retained."
                        : "Procedural audio generation failed before publication started.",
                    failures);
            }

            if (!publication.Committed)
            {
                throw new InvalidOperationException("Procedural audio publication did not reach a committed state.");
            }

            try
            {
                AssetDatabase.Refresh();
            }
            finally
            {
                ReportCleanupDiagnostics(publication.CleanupDiagnostics);
            }
        }

        private static List<ManifestRecord> ReadManifestRecords(string path)
        {
            if (!File.Exists(path))
            {
                return new List<ManifestRecord>();
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Asset manifest is not valid UTF-8.", exception);
            }

            var parsedRecords = ParseCsvRecords(text);
            if (parsedRecords.Count == 0 ||
                !string.Equals(parsedRecords[0].Raw, ManifestHeader, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Asset manifest requires the exact canonical 15-column header.");
            }

            var records = new List<ManifestRecord>();
            var seenAssetIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 1; index < parsedRecords.Count; index++)
            {
                var record = parsedRecords[index];
                if (string.Equals(record.Raw, ManifestHeader, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Asset manifest contains more than one header.");
                }

                if (record.FieldCount != 15 || string.IsNullOrEmpty(record.AssetId))
                {
                    throw new InvalidOperationException("Asset manifest contains a malformed row.");
                }

                if (!seenAssetIds.Add(record.AssetId))
                {
                    throw new InvalidOperationException("Asset manifest contains duplicate asset ID: " + record.AssetId);
                }

                records.Add(record);
            }

            return records;
        }

        private static void ValidateManagedManifestOwnership(
            IReadOnlyList<ManifestRecord> records,
            string assetDirectory,
            string generationDirectory)
        {
            var managedOwners =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (var cue in Cues)
            {
                var assetId = "audio_" + cue.Name.ToLowerInvariant();
                managedOwners.Add(
                    Path.GetFullPath(
                        assetDirectory + "/" + cue.Name + ".wav"),
                    assetId);
                managedOwners.Add(
                    Path.GetFullPath(
                        generationDirectory + "/" + cue.Name + ".json"),
                    assetId);
            }

            foreach (var record in records)
            {
                RequireManagedPathOwner(
                    record,
                    record.Fields[2],
                    managedOwners);
                RequireManagedPathOwner(
                    record,
                    record.Fields[7],
                    managedOwners);
                RequireManagedPathOwner(
                    record,
                    record.Fields[8],
                    managedOwners);
                RequireManagedPathOwner(
                    record,
                    record.Fields[9],
                    managedOwners);

                var noteSegments = record.Fields[13].Split(';');
                foreach (var segment in noteSegments)
                {
                    if (segment.StartsWith("record=", StringComparison.Ordinal))
                    {
                        RequireManagedPathOwner(
                            record,
                            segment.Substring("record=".Length),
                            managedOwners);
                    }
                }
            }
        }

        private static void RequireManagedPathOwner(
            ManifestRecord record,
            string claimedPath,
            IReadOnlyDictionary<string, string> managedOwners)
        {
            if (string.IsNullOrEmpty(claimedPath))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(claimedPath);
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (NotSupportedException)
            {
                return;
            }
            catch (PathTooLongException)
            {
                return;
            }

            string canonicalOwner;
            if (managedOwners.TryGetValue(fullPath, out canonicalOwner) &&
                !string.Equals(
                    record.AssetId,
                    canonicalOwner,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Asset manifest row '" + record.AssetId +
                    "' conflicts with managed artifact ownership for " +
                    claimedPath + "; expected asset ID '" +
                    canonicalOwner + "'.");
            }
        }
        private static List<ManifestRecord> ParseCsvRecords(string text)
        {
            var records = new List<ManifestRecord>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var recordStart = 0;
            var inQuotes = false;
            var afterClosingQuote = false;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                            afterClosingQuote = true;
                        }
                    }
                    else
                    {
                        field.Append(character);
                    }

                    continue;
                }

                if (character == '\r' || character == '\n')
                {
                    AddCsvRecord(records, text, recordStart, index, fields, field);
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    recordStart = index + 1;
                    fields.Clear();
                    field.Length = 0;
                    afterClosingQuote = false;
                    continue;
                }

                if (afterClosingQuote)
                {
                    if (character != ',')
                    {
                        throw new InvalidOperationException("Asset manifest contains a malformed quoted field.");
                    }

                    fields.Add(field.ToString());
                    field.Length = 0;
                    afterClosingQuote = false;
                    continue;
                }

                if (character == '"')
                {
                    if (field.Length != 0)
                    {
                        throw new InvalidOperationException("Asset manifest contains a malformed quoted field.");
                    }

                    inQuotes = true;
                    continue;
                }

                if (character == ',')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    continue;
                }

                field.Append(character);
            }

            if (inQuotes)
            {
                throw new InvalidOperationException("Asset manifest contains an unterminated quoted field.");
            }

            if (recordStart < text.Length)
            {
                AddCsvRecord(records, text, recordStart, text.Length, fields, field);
            }

            return records;
        }

        private static void AddCsvRecord(
            List<ManifestRecord> records,
            string text,
            int recordStart,
            int recordEnd,
            List<string> fields,
            StringBuilder field)
        {
            fields.Add(field.ToString());
            if (recordEnd > recordStart)
            {
                records.Add(new ManifestRecord(
                    text.Substring(recordStart, recordEnd - recordStart),
                    fields));
            }
        }

        private static bool TryGetManifestRecord(List<ManifestRecord> records, string assetId, out ManifestRecord result)
        {
            foreach (var record in records)
            {
                if (string.Equals(record.AssetId, assetId, StringComparison.Ordinal))
                {
                    result = record;
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static string BuildManifestRow(
            string assetId,
            string eventName,
            string path,
            string recordPath,
            string generatorSourcePath,
            string toolVersion,
            string generatedAtUtc,
            string reviewer,
            string generatorSourceSha,
            string outputSha)
        {
            var fields = BuildManifestFields(
                assetId,
                eventName,
                path,
                recordPath,
                generatorSourcePath,
                toolVersion,
                generatedAtUtc,
                reviewer,
                generatorSourceSha,
                outputSha);
            for (var index = 0; index < fields.Length; index++)
            {
                fields[index] = Csv(fields[index]);
            }

            return string.Join(",", fields);
        }

        private static string[] BuildManifestFields(
            string assetId,
            string eventName,
            string path,
            string recordPath,
            string generatorSourcePath,
            string toolVersion,
            string generatedAtUtc,
            string reviewer,
            string generatorSourceSha,
            string outputSha)
        {
            var notes = eventName + ";record=" + recordPath + ";generatorSha=" + generatorSourceSha;
            return new[]
            {
                assetId,
                "audio",
                path,
                "generated",
                "Unity Editor " + toolVersion,
                "none",
                generatedAtUtc,
                "none",
                generatorSourcePath,
                path,
                "none",
                "repository-native procedural",
                reviewer,
                notes,
                outputSha
            };
        }

        private static FileStream AcquireGenerationLock()
        {
            var projectIdentity = Sha(Encoding.UTF8.GetBytes(Path.GetFullPath("."))).Substring(0, 16);
            var lockPath = Path.Combine(Path.GetTempPath(), "overbless-procedural-audio-" + projectIdentity + ".lock");
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private static bool EnsureReviewedRecordIsStable(
            string recordPath,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion)
        {
            if (!TryReadMatchingRecord(
                    recordPath,
                    eventName,
                    seed,
                    parameters,
                    path,
                    outputSha,
                    generatorSha,
                    toolVersion,
                    out var existing))
            {
                if (!existing.Exists)
                {
                    return false;
                }

                var reviewer = GetRequiredRecordString(existing.Root, "reviewer");
                if (!IsValidReviewer(reviewer))
                {
                    throw new InvalidOperationException("Existing procedural audio reviewer is malformed.");
                }

                if (!string.Equals(reviewer, PendingReviewer, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Reviewed procedural audio provenance is immutable; create a versioned asset ID and record.");
                }

                return false;
            }

            EnsureApprovedRecordUsesCanonicalBytes(recordPath, existing);
            return true;
        }

        private static string ResolveStableReviewer(
            string recordPath,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion)
        {
            if (!TryReadMatchingRecord(recordPath, eventName, seed, parameters, path, outputSha, generatorSha, toolVersion, out var existing))
            {
                return PendingReviewer;
            }

            var reviewer = GetRequiredRecordString(existing.Root, "reviewer");
            if (!IsValidReviewer(reviewer))
            {
                throw new InvalidOperationException("Existing procedural audio reviewer is malformed.");
            }

            return reviewer;
        }

        private static bool TryReadMatchingRecord(
            string recordPath,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion,
            out ExistingRecord existing)
        {
            existing = default;
            if (!File.Exists(recordPath))
            {
                return false;
            }

            existing = ReadExistingGenerationRecord(recordPath);
            return RecordSemanticsMatch(
                existing.Root,
                eventName,
                seed,
                parameters,
                path,
                outputSha,
                generatorSha,
                toolVersion);
        }

        private static ExistingRecord ReadExistingGenerationRecord(string recordPath)
        {
            var bytes = File.ReadAllBytes(recordPath);
            if (!CanonicalJson.TryParseUtf8(bytes, out var root, out var error) ||
                root.Kind != CanonicalJsonKind.Object)
            {
                throw new InvalidOperationException("Procedural audio record is malformed JSON: " + error);
            }

            return new ExistingRecord(bytes, root);
        }

        private static bool RecordSemanticsMatch(
            CanonicalJsonValue root,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion)
        {
            var requiredNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "event", "generatorName", "toolName", "toolVersion", "generatorSourceSha256",
                "generationUtc", "seed", "parametersOrInstruction", "originalWavPath",
                "originalWavSha256", "modifications", "finalWavPath", "finalWavSha256", "reviewer"
            };
            var observedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.Properties)
            {
                if (!requiredNames.Contains(property.Name) || !observedNames.Add(property.Name))
                {
                    return false;
                }
            }

            if (observedNames.Count != requiredNames.Count)
            {
                return false;
            }

            CanonicalJsonValue seedValue;
            CanonicalJsonValue modifications;
            if (!root.TryGetSingleProperty("seed", out seedValue) ||
                seedValue.Kind != CanonicalJsonKind.Number ||
                seedValue.NumberValue != seed ||
                !root.TryGetSingleProperty("modifications", out modifications) ||
                modifications.Kind != CanonicalJsonKind.Array ||
                modifications.Items.Count != 0)
            {
                return false;
            }

            var generationUtc = GetRequiredRecordString(root, "generationUtc");
            DateTime parsedGeneration;
            if (generationUtc.Length != 24 ||
                !DateTime.TryParseExact(
                    generationUtc,
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsedGeneration))
            {
                return false;
            }

            var reviewer = GetRequiredRecordString(root, "reviewer");
            return !string.IsNullOrEmpty(toolVersion) &&
                   IsSha256(outputSha) &&
                   IsSha256(generatorSha) &&
                   string.Equals(GetRequiredRecordString(root, "event"), eventName, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "generatorName"), "Overbless ProceduralAudioGenerator", StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "toolName"), "Unity Editor", StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "toolVersion"), toolVersion, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "generatorSourceSha256"), generatorSha, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "parametersOrInstruction"), parameters, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "originalWavPath"), path, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "originalWavSha256"), outputSha, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "finalWavPath"), path, StringComparison.Ordinal) &&
                   string.Equals(GetRequiredRecordString(root, "finalWavSha256"), outputSha, StringComparison.Ordinal) &&
                   IsValidReviewer(reviewer);
        }

        private static void ValidateOwnedManifestRecord(
            ManifestRecord manifestRecord,
            string assetId,
            string eventName,
            int seed,
            string parameters,
            string path,
            string recordPath,
            string generatorSourcePath)
        {
            var existing = ReadExistingGenerationRecord(recordPath);
            EnsureApprovedRecordUsesCanonicalBytes(recordPath, existing);

            var toolVersion = GetRequiredRecordString(existing.Root, "toolVersion");
            var generatorSourceSha = GetRequiredRecordString(existing.Root, "generatorSourceSha256");
            var outputSha = GetRequiredRecordString(existing.Root, "finalWavSha256");
            if (!RecordSemanticsMatch(
                    existing.Root,
                    eventName,
                    seed,
                    parameters,
                    path,
                    outputSha,
                    generatorSourceSha,
                    toolVersion))
            {
                throw new InvalidOperationException(
                    "Owned asset manifest provenance conflicts with generation record: " + assetId + ".");
            }

            var expectedFields = BuildManifestFields(
                assetId,
                eventName,
                path,
                recordPath,
                generatorSourcePath,
                toolVersion,
                GetRequiredRecordString(existing.Root, "generationUtc"),
                GetRequiredRecordString(existing.Root, "reviewer"),
                generatorSourceSha,
                outputSha);
            for (var index = 0; index < expectedFields.Length; index++)
            {
                if (!string.Equals(manifestRecord.Fields[index], expectedFields[index], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Owned asset manifest provenance conflicts with generation record: " + assetId + ".");
                }
            }
        }

        private static void EnsureApprovedRecordUsesCanonicalBytes(string recordPath, ExistingRecord existing)
        {
            var reviewer = GetRequiredRecordString(existing.Root, "reviewer");
            if (string.Equals(reviewer, PendingReviewer, StringComparison.Ordinal))
            {
                return;
            }

            var canonicalBytes = CanonicalJson.SerializeUtf8(existing.Root);
            if (!ByteArraysEqual(existing.Bytes, canonicalBytes))
            {
                throw new InvalidOperationException(
                    "Reviewed procedural audio provenance must remain canonical and byte-immutable: " + recordPath + ".");
            }
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetRequiredRecordString(CanonicalJsonValue root, string propertyName)
        {
            CanonicalJsonValue value;
            if (!root.TryGetSingleProperty(propertyName, out value) || value.Kind != CanonicalJsonKind.String)
            {
                throw new InvalidOperationException("Procedural audio record requires one string property '" + propertyName + "'.");
            }

            return value.StringValue;
        }
        private static bool IsValidReviewer(string reviewer)
        {
            if (string.Equals(reviewer, PendingReviewer, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(reviewer))
            {
                return false;
            }

            foreach (var character in reviewer)
            {
                if (char.IsControl(character))
                {
                    return false;
                }
            }

            return true;
        }


        private static string Csv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string ResolveStableGenerationTime(
            string recordPath,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion)
        {
            if (TryReadMatchingRecord(recordPath, eventName, seed, parameters, path, outputSha, generatorSha, toolVersion, out var existing))
            {
                var generationUtc = GetRequiredRecordString(existing.Root, "generationUtc");
                if (string.IsNullOrEmpty(generationUtc))
                {
                    throw new InvalidOperationException("Existing procedural audio generation time is missing.");
                }

                return generationUtc;
            }

            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static OutputPreimage RequireCurrentGeneratorSource(
            string generatorSourcePath)
        {
            var sourcePath = Path.GetFullPath(generatorSourcePath);
            var preimage = CaptureOutputPreimage(sourcePath);
            if (!preimage.Exists)
            {
                throw new InvalidOperationException(
                    "Procedural audio generator source is missing: " +
                    generatorSourcePath + ".");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorUtility.scriptCompilationFailed)
            {
                throw new InvalidOperationException(
                    "Procedural audio generator source changed, failed compilation, or is still importing; rerun generation after a successful Unity compile.");
            }

            RequireOutputPreimageUnchanged(preimage);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                generatorSourcePath);
            if (script == null ||
                script.GetClass() != typeof(ProceduralAudioGenerator))
            {
                throw new InvalidOperationException(
                    "The imported procedural audio generator source does not bind the executing generator type.");
            }

            var assemblyPath =
                typeof(ProceduralAudioGenerator).Assembly.Location;
            if (string.IsNullOrEmpty(assemblyPath) ||
                !File.Exists(assemblyPath) ||
                File.GetLastWriteTimeUtc(assemblyPath) <
                File.GetLastWriteTimeUtc(sourcePath))
            {
                throw new InvalidOperationException(
                    "The loaded procedural audio generator assembly predates the captured source revision.");
            }

            return preimage;
        }
        private static List<string> GetManagedOutputPaths(
            string assetDirectory,
            string generationDirectory,
            string manifestPath)
        {
            var paths = new List<string>();
            foreach (var cue in Cues)
            {
                paths.Add(Path.GetFullPath(
                    assetDirectory + "/" + cue.Name + ".wav"));
                paths.Add(Path.GetFullPath(
                    generationDirectory + "/" + cue.Name + ".json"));
            }

            paths.Add(Path.GetFullPath(manifestPath));
            return paths;
        }

        private static void RecoverOrphanedOutputTemporaries(
            IReadOnlyList<string> managedOutputPaths)
        {
            foreach (var destination in managedOutputPaths)
            {
                var directory = Path.GetDirectoryName(destination);
                var fileName = Path.GetFileName(destination);
                var prefix = "." + fileName + ".";
                foreach (var candidate in Directory.GetFiles(directory))
                {
                    var candidateName = Path.GetFileName(candidate);
                    if (!candidateName.StartsWith(prefix, StringComparison.Ordinal) ||
                        !candidateName.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var identifierLength =
                        candidateName.Length - prefix.Length - ".tmp".Length;
                    if (identifierLength != 32)
                    {
                        continue;
                    }

                    var isGeneratorTemporary = true;
                    for (var index = prefix.Length;
                         index < prefix.Length + identifierLength;
                         index++)
                    {
                        var character = candidateName[index];
                        if ((character < '0' || character > '9') &&
                            (character < 'a' || character > 'f'))
                        {
                            isGeneratorTemporary = false;
                            break;
                        }
                    }

                    if (isGeneratorTemporary)
                    {
                        File.Delete(candidate);
                        if (File.Exists(candidate))
                        {
                            throw new IOException(
                                "Procedural audio orphan staging temporary still exists after cleanup: " +
                                candidate + ".");
                        }
                    }
                }
            }
        }

        private static OutputPreimage CaptureOutputPreimage(
            string destination)
        {
            var fullPath = Path.GetFullPath(destination);
            var exists = File.Exists(fullPath);
            return new OutputPreimage(
                fullPath,
                exists,
                exists ? Sha(File.ReadAllBytes(fullPath)) : null);
        }

        private static Dictionary<string, OutputPreimage> CaptureOutputPreimages(
            IReadOnlyList<string> managedOutputPaths)
        {
            var preimages =
                new Dictionary<string, OutputPreimage>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (var destination in managedOutputPaths)
            {
                var preimage = CaptureOutputPreimage(destination);
                preimages.Add(preimage.Destination, preimage);
            }

            return preimages;
        }

        private static void RequireOutputPreimagesUnchanged(
            IReadOnlyDictionary<string, OutputPreimage> preimages)
        {
            foreach (var preimage in preimages.Values)
            {
                RequireOutputPreimageUnchanged(preimage);
            }
        }

        private static void RequireOutputPreimageUnchanged(
            OutputPreimage preimage)
        {
            var exists = File.Exists(preimage.Destination);
            if (exists != preimage.Exists ||
                (exists &&
                 !string.Equals(
                     Sha(File.ReadAllBytes(preimage.Destination)),
                     preimage.Sha256,
                     StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Procedural audio destination changed after validation: " +
                    preimage.Destination + ".");
            }
        }

        private static void RequireStagedPayloadUnchanged(
            StagedOutput output)
        {
            if (!File.Exists(output.Temporary) ||
                !string.Equals(
                    Sha(File.ReadAllBytes(output.Temporary)),
                    output.PayloadSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Procedural audio staged file changed after durable staging: " +
                    output.Temporary + ".");
            }
        }

        private static void StageIfChanged(
            List<StagedOutput> outputs,
            string destination,
            byte[] bytes,
            IReadOnlyDictionary<string, OutputPreimage> preimages)
        {
            OutputPreimage preimage;
            if (!preimages.TryGetValue(
                    Path.GetFullPath(destination),
                    out preimage))
            {
                throw new InvalidOperationException(
                    "Generated output has no captured destination preimage: " +
                    destination + ".");
            }

            RequireOutputPreimageUnchanged(preimage);
            if (preimage.Exists &&
                ByteArraysEqual(File.ReadAllBytes(destination), bytes))
            {
                return;
            }

            outputs.Add(Stage(destination, bytes, preimage));
        }
        private static void CleanupStaged(List<StagedOutput> outputs, List<Exception> errors)
        {
            foreach (var output in outputs)
            {
                try
                {
                    if (File.Exists(output.Temporary))
                    {
                        File.Delete(output.Temporary);
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }
        private static StagedOutput Stage(
            string destination,
            byte[] bytes,
            OutputPreimage preimage)
        {
            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Generated output requires a parent directory: " + destination);
            }

            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                var payloadSha256 = Sha(bytes);
                if (!string.Equals(
                        Sha(File.ReadAllBytes(temporary)),
                        payloadSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Procedural audio staging bytes do not match the intended generated payload.");
                }

                return new StagedOutput(
                    destination,
                    temporary,
                    preimage,
                    payloadSha256);
            }
            catch (Exception stagingException)
            {
                var failures = new List<Exception> { stagingException };
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception cleanupException)
                {
                    failures.Add(cleanupException);
                }

                throw new AggregateException("Procedural audio staging failed.", failures);
            }
        }

        private static void RequirePublicationReadSetStable(
            IReadOnlyList<StagedOutput> outputs,
            IReadOnlyDictionary<string, OutputPreimage> outputPreimages,
            OutputPreimage generatorSourcePreimage)
        {
            var stagedByDestination =
                new Dictionary<string, StagedOutput>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (var output in outputs)
            {
                stagedByDestination.Add(
                    Path.GetFullPath(output.Destination),
                    output);
            }

            foreach (var preimage in outputPreimages.Values)
            {
                StagedOutput output;
                if (!stagedByDestination.TryGetValue(
                        preimage.Destination,
                        out output))
                {
                    RequireOutputPreimageUnchanged(preimage);
                    continue;
                }

                if (!File.Exists(preimage.Destination) ||
                    !string.Equals(
                        Sha(File.ReadAllBytes(preimage.Destination)),
                        output.PayloadSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Published procedural audio output does not match its intended payload: " +
                        preimage.Destination + ".");
                }
            }

            RequireOutputPreimageUnchanged(generatorSourcePreimage);
        }

        private static PublicationResult PublishAll(
            List<StagedOutput> outputs,
            IReadOnlyDictionary<string, OutputPreimage> outputPreimages,
            OutputPreimage generatorSourcePreimage)
        {
            var plans = new List<PublishedOutput>();
            foreach (var output in outputs)
            {
                RequireOutputPreimageUnchanged(output.Preimage);
                RequireStagedPayloadUnchanged(output);
                var hadDestination = output.Preimage.Exists;
                var backup = hadDestination
                    ? output.Destination + "." + Guid.NewGuid().ToString("N") + ".bak"
                    : null;
                plans.Add(new PublishedOutput(output.Destination, backup, hadDestination));
            }

            PublicationJournal journal = null;
            try
            {
                journal = CreatePublicationJournal(plans, outputs);
                for (var index = 0; index < outputs.Count; index++)
                {
                    var output = outputs[index];
                    var plan = plans[index];
                    var entry = journal.Entries[index];
                    RequireOutputPreimageUnchanged(output.Preimage);
                    RequireStagedPayloadUnchanged(output);
                    if (GetPreparedPublicationFileState(entry) != PreparedPublicationFileState.PrePublication)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication journal filesystem state changed before publication: " +
                            entry.Destination + ".");
                    }

                    if (plan.HadDestination)
                    {
                        File.Replace(output.Temporary, plan.Destination, plan.Backup);
                    }
                    else
                    {
                        File.Move(output.Temporary, plan.Destination);
                    }

                    if (GetPreparedPublicationFileState(entry) != PreparedPublicationFileState.Published)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication journal filesystem state changed during publication: " +
                            entry.Destination + ".");
                    }

                    entry.Published = true;
                    PersistPublicationJournal(journal);
                }

                RequirePublicationReadSetStable(
                    outputs,
                    outputPreimages,
                    generatorSourcePreimage);
                journal.State = PublicationJournalState.Committed;
                try
                {
                    PersistPublicationJournal(journal);
                }
                catch (Exception commitException)
                {
                    if (commitException is CommitStateUncertainException)
                    {
                        throw;
                    }
                    if (!TryReadPublicationJournal(journal.Path, out var recoveredJournal))
                    {
                        throw new CommitStateUncertainException(
                            "Procedural audio publication commit state is uncertain; the durable journal was retained: " +
                            journal.Path + ".",
                            commitException);
                    }

                    journal = recoveredJournal;
                    if (!journal.Committed)
                    {
                        throw;
                    }
                }
            }
            catch (Exception publicationException)
            {
                var failures = new List<Exception> { publicationException };
                if (publicationException is CommitStateUncertainException)
                {
                    throw new AggregateException(
                        "Procedural audio publication outcome is uncertain; recovery journal was retained.",
                        failures);
                }

                if (journal != null)
                {
                    try
                    {
                        RollbackPreparedPublicationJournal(journal, null);
                    }
                    catch (Exception rollbackException)
                    {
                        failures.Add(rollbackException);
                    }
                }
                else
                {
                    CleanupStaged(outputs, failures);
                }

                throw new AggregateException("Procedural audio publication failed before commit.", failures);
            }

            var cleanupDiagnostics = new List<Exception>();
            CleanupStaged(outputs, cleanupDiagnostics);
            CleanupCommittedPublicationJournal(journal, cleanupDiagnostics);
            return new PublicationResult(true, cleanupDiagnostics);
        }

        private static void RecoverPublicationJournalTemporaries()
        {
            if (!Directory.Exists(CleanupJournalDirectory))
            {
                return;
            }

            var candidatesByJournal =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.GetFiles(CleanupJournalDirectory))
            {
                string journalPath;
                if (!TryGetAuthoritativeJournalPath(path, out journalPath))
                {
                    continue;
                }

                List<string> candidates;
                if (!candidatesByJournal.TryGetValue(journalPath, out candidates))
                {
                    candidates = new List<string>();
                    candidatesByJournal.Add(journalPath, candidates);
                }

                candidates.Add(path);
            }

            var journalPaths = new List<string>(candidatesByJournal.Keys);
            journalPaths.Sort(StringComparer.Ordinal);
            foreach (var journalPath in journalPaths)
            {
                var candidates = candidatesByJournal[journalPath];
                candidates.Sort(StringComparer.Ordinal);
                if (File.Exists(journalPath))
                {
                    var authoritative = ReadPublicationJournal(journalPath);
                    foreach (var candidatePath in candidates)
                    {
                        PublicationJournal candidate;
                        try
                        {
                            candidate = ReadPublicationJournal(candidatePath);
                        }
                        catch (Exception readException)
                        {
                            try
                            {
                                File.Delete(candidatePath);
                                if (File.Exists(candidatePath))
                                {
                                    throw new IOException(
                                        "Unreadable journal shadow still exists after cleanup.");
                                }
                            }
                            catch (Exception cleanupException)
                            {
                                throw new AggregateException(
                                    "Procedural audio could not remove an unreadable non-authoritative journal shadow: " +
                                    candidatePath + ".",
                                    readException,
                                    cleanupException);
                            }

                            continue;
                        }

                        RequireSamePublicationIdentity(authoritative, candidate);
                        File.Delete(candidatePath);
                        if (File.Exists(candidatePath))
                        {
                            throw new IOException(
                                "Procedural audio journal shadow still exists after cleanup: " +
                                candidatePath + ".");
                        }
                    }

                    continue;
                }

                if (candidates.Count != 1)
                {
                    throw new InvalidOperationException(
                        "Procedural audio recovery found multiple initial journal candidates without an authoritative journal: " +
                        journalPath + ".");
                }

                PublicationJournal initialCandidate;
                try
                {
                    initialCandidate =
                        ReadPublicationJournal(candidates[0]);
                }
                catch (Exception readException)
                {
                    try
                    {
                        File.Delete(candidates[0]);
                        if (File.Exists(candidates[0]))
                        {
                            throw new IOException(
                                "Unreadable initial journal candidate still exists after cleanup.");
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException(
                            "Procedural audio could not remove an unreadable initial journal candidate: " +
                            candidates[0] + ".",
                            readException,
                            cleanupException);
                    }

                    continue;
                }

                RequireInitialPublicationJournalCandidate(initialCandidate);
                File.Move(candidates[0], journalPath);
            }
        }

        private static bool TryGetAuthoritativeJournalPath(
            string temporaryPath,
            out string journalPath)
        {
            journalPath = null;
            var fileName = Path.GetFileName(temporaryPath);
            const string marker = ".journal.";
            const string suffix = ".tmp";
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var markerIndex = fileName.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex <= 0)
            {
                return false;
            }

            var identifierStart = markerIndex + marker.Length;
            var identifierLength =
                fileName.Length - identifierStart - suffix.Length;
            if (identifierLength != 32)
            {
                return false;
            }

            for (var index = identifierStart;
                 index < identifierStart + identifierLength;
                 index++)
            {
                var character = fileName[index];
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f'))
                {
                    return false;
                }
            }

            var journalName =
                fileName.Substring(0, markerIndex + ".journal".Length);
            journalPath = Path.Combine(
                Path.GetDirectoryName(temporaryPath),
                journalName);
            return true;
        }

        private static void RequireSamePublicationIdentity(
            PublicationJournal authoritative,
            PublicationJournal candidate)
        {
            if (authoritative.Legacy != candidate.Legacy ||
                authoritative.Entries.Count != candidate.Entries.Count)
            {
                throw new InvalidOperationException(
                    "Procedural audio journal temporary does not match its authoritative transaction: " +
                    candidate.Path + ".");
            }

            for (var index = 0; index < authoritative.Entries.Count; index++)
            {
                var expected = authoritative.Entries[index];
                var actual = candidate.Entries[index];
                if (!string.Equals(expected.Destination, actual.Destination, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expected.Backup, actual.Backup, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expected.NewFile, actual.NewFile, StringComparison.OrdinalIgnoreCase) ||
                    expected.HadDestination != actual.HadDestination ||
                    !string.Equals(expected.OriginalSha256, actual.OriginalSha256, StringComparison.Ordinal) ||
                    !string.Equals(expected.StagedSha256, actual.StagedSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Procedural audio journal temporary does not match its authoritative transaction: " +
                        candidate.Path + ".");
                }
            }
        }

        private static void RequireInitialPublicationJournalCandidate(
            PublicationJournal candidate)
        {
            if (candidate.Legacy ||
                candidate.State != PublicationJournalState.Prepared)
            {
                throw new InvalidOperationException(
                    "Procedural audio initial journal candidate has an invalid state: " +
                    candidate.Path + ".");
            }

            foreach (var entry in candidate.Entries)
            {
                if (entry.Published ||
                    entry.RollbackStarted ||
                    entry.RollbackCompleted ||
                    entry.CleanupStarted ||
                    entry.CleanupCompleted ||
                    GetPreparedPublicationFileState(entry) !=
                    PreparedPublicationFileState.PrePublication)
                {
                    throw new InvalidOperationException(
                        "Procedural audio initial journal candidate is ambiguous or tampered: " +
                        candidate.Path + ".");
                }
            }
        }
        private static RecoveryResult RecoverPublicationJournals()
        {
            var cleanupDiagnostics = new List<Exception>();
            var assetFilesChanged = false;
            if (!Directory.Exists(CleanupJournalDirectory))
            {
                return new RecoveryResult(assetFilesChanged, cleanupDiagnostics);
            }

            foreach (var journalPath in Directory.GetFiles(CleanupJournalDirectory))
            {
                if (!journalPath.EndsWith(".journal", StringComparison.Ordinal))
                {
                    continue;
                }

                var journal = ReadPublicationJournal(journalPath);
                if (journal.Committed)
                {
                    CleanupCommittedPublicationJournal(journal, cleanupDiagnostics);
                }
                else
                {
                    assetFilesChanged |= RollbackPreparedPublicationJournal(journal, cleanupDiagnostics);
                }
            }

            return new RecoveryResult(assetFilesChanged, cleanupDiagnostics);
        }

        private static void RequireNoPendingPublicationJournals(
            IReadOnlyList<Exception> cleanupDiagnostics)
        {
            if (!Directory.Exists(CleanupJournalDirectory))
            {
                return;
            }

            string pendingJournal = null;
            foreach (var path in Directory.GetFiles(CleanupJournalDirectory))
            {
                if (path.EndsWith(".journal", StringComparison.Ordinal))
                {
                    pendingJournal = path;
                    break;
                }
            }

            if (pendingJournal == null)
            {
                return;
            }

            var failures = new List<Exception>(cleanupDiagnostics)
            {
                new InvalidOperationException(
                    "Procedural audio recovery remains pending; no new publication is allowed while this journal exists: " +
                    pendingJournal + ".")
            };
            throw new AggregateException(
                "Procedural audio recovery must complete before generation can continue.",
                failures);
        }

        private static PublicationJournal CreatePublicationJournal(
            List<PublishedOutput> plans,
            List<StagedOutput> outputs)
        {
            if (plans.Count != outputs.Count)
            {
                throw new InvalidOperationException("Procedural audio publication journal output count is inconsistent.");
            }

            Directory.CreateDirectory(CleanupJournalDirectory);
            var journalPath = Path.Combine(
                CleanupJournalDirectory,
                "procedural-audio-" + Guid.NewGuid().ToString("N") + ".journal");
            var entries = new List<PublicationJournalEntry>();
            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                var output = outputs[index];
                var destination = Path.GetFullPath(plan.Destination);
                var newFile = Path.GetFullPath(output.Temporary);
                RequireOutputPreimageUnchanged(output.Preimage);
                RequireStagedPayloadUnchanged(output);
                if (!string.Equals(destination, Path.GetFullPath(output.Destination), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(destination, output.Preimage.Destination, StringComparison.OrdinalIgnoreCase) ||
                    plan.HadDestination != output.Preimage.Exists ||
                    !File.Exists(newFile))
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication journal source state is inconsistent: " + destination + ".");
                }

                entries.Add(new PublicationJournalEntry(
                    destination,
                    plan.Backup == null ? null : Path.GetFullPath(plan.Backup),
                    newFile,
                    plan.HadDestination,
                    false,
                    false,
                    false,
                    false,
                    false,
                    output.Preimage.Sha256,
                    output.PayloadSha256));
            }

            var journal = new PublicationJournal(
                journalPath,
                entries,
                PublicationJournalState.Prepared,
                false);
            var journalBytes = SerializePublicationJournal(journal);
            var temporaryJournalPath =
                journal.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteNewFileDurably(temporaryJournalPath, journalBytes);
                File.Move(temporaryJournalPath, journal.Path);
                return journal;
            }
            catch (Exception creationException)
            {
                if (File.Exists(journal.Path))
                {
                    try
                    {
                        if (ByteArraysEqual(File.ReadAllBytes(journal.Path), journalBytes))
                        {
                            return journal;
                        }
                    }
                    catch (Exception verificationException)
                    {
                        throw new CommitStateUncertainException(
                            "Procedural audio initial journal publication is uncertain; staged recovery artifacts were retained: " +
                            journal.Path + ".",
                            new AggregateException(creationException, verificationException));
                    }

                    throw new CommitStateUncertainException(
                        "Procedural audio initial journal publication is uncertain; staged recovery artifacts were retained: " +
                        journal.Path + ".",
                        creationException);
                }

                try
                {
                    if (File.Exists(temporaryJournalPath))
                    {
                        File.Delete(temporaryJournalPath);
                    }

                    if (File.Exists(temporaryJournalPath))
                    {
                        throw new IOException(
                            "The initial journal temporary still exists after cleanup.");
                    }
                }
                catch (Exception cleanupException)
                {
                    throw new CommitStateUncertainException(
                        "Procedural audio initial journal cleanup is uncertain; staged recovery artifacts were retained: " +
                        temporaryJournalPath + ".",
                        new AggregateException(creationException, cleanupException));
                }

                throw;
            }
        }

        private static void PersistPublicationJournal(PublicationJournal journal)
        {
            var temporary = journal.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteNewFileDurably(temporary, SerializePublicationJournal(journal));
                File.Replace(temporary, journal.Path, null);
            }
            catch (Exception persistenceException)
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }

                    if (File.Exists(temporary))
                    {
                        throw new IOException(
                            "The journal update temporary still exists after cleanup.");
                    }
                }
                catch (Exception cleanupException)
                {
                    throw new CommitStateUncertainException(
                        "Procedural audio journal update cleanup is uncertain; the authoritative journal and staged recovery artifacts were retained: " +
                        journal.Path + ".",
                        new AggregateException(persistenceException, cleanupException));
                }


                throw;
            }
        }

        private static byte[] SerializePublicationJournal(PublicationJournal journal)
        {
            if (journal.Legacy)
            {
                throw new InvalidOperationException(
                    "Legacy procedural audio publication journals cannot be rewritten: " + journal.Path + ".");
            }

            var content = new StringBuilder("overbless-procedural-audio-publication-v3\n");
            content.Append(SerializeJournalState(journal.State));
            content.Append('\n');
            foreach (var entry in journal.Entries)
            {
                content.Append(entry.HadDestination ? '1' : '0');
                content.Append('\t');
                content.Append(EncodeJournalPath(entry.Destination));
                content.Append('\t');
                content.Append(entry.Backup == null ? string.Empty : EncodeJournalPath(entry.Backup));
                content.Append('\t');
                content.Append(EncodeJournalPath(entry.NewFile));
                content.Append('\t');
                content.Append(entry.Published ? '1' : '0');
                content.Append('\t');
                content.Append(entry.RollbackStarted ? '1' : '0');
                content.Append('\t');
                content.Append(entry.RollbackCompleted ? '1' : '0');
                content.Append('\t');
                content.Append(entry.CleanupStarted ? '1' : '0');
                content.Append('\t');
                content.Append(entry.CleanupCompleted ? '1' : '0');
                content.Append('\t');
                content.Append(entry.OriginalSha256 ?? string.Empty);
                content.Append('\t');
                content.Append(entry.StagedSha256);
                content.Append('\n');
            }

            return new UTF8Encoding(false).GetBytes(content.ToString());
        }

        private static PublicationJournal ReadPublicationJournal(string journalPath)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(journalPath));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException("Procedural audio publication journal is not valid UTF-8: " + journalPath + ".", exception);
            }

            var lines = text.Split('\n');
            var legacy = lines.Length >= 1 &&
                         string.Equals(lines[0], "overbless-procedural-audio-publication-v2", StringComparison.Ordinal);
            if (lines.Length < 3 ||
                (!legacy &&
                 !string.Equals(lines[0], "overbless-procedural-audio-publication-v3", StringComparison.Ordinal)) ||
                !string.Equals(lines[lines.Length - 1], string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Procedural audio publication journal format is invalid: " + journalPath + ".");
            }

            var state = ParseJournalState(lines[1], journalPath);
            if (legacy && state == PublicationJournalState.RollingBack)
            {
                throw new InvalidOperationException(
                    "Legacy procedural audio publication journal has an invalid rollback state: " + journalPath + ".");
            }

            var entries = new List<PublicationJournalEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 2; index < lines.Length - 1; index++)
            {
                var fields = lines[index].Split(new[] { '\t' });
                if (fields.Length != (legacy ? 5 : 11))
                {
                    throw new InvalidOperationException("Procedural audio publication journal entry is malformed: " + journalPath + ".");
                }

                var hadDestination = ParseJournalFlag(fields[0], "destination state", journalPath);
                var destination = DecodeJournalPath(fields[1], "destination", journalPath);
                var backup = string.IsNullOrEmpty(fields[2])
                    ? null
                    : DecodeJournalPath(fields[2], "backup", journalPath);
                var newFile = DecodeJournalPath(fields[3], "new file", journalPath);
                var published = ParseJournalFlag(fields[4], "publication progress state", journalPath);
                var rollbackStarted = !legacy && ParseJournalFlag(fields[5], "rollback write-ahead state", journalPath);
                var rollbackCompleted = !legacy && ParseJournalFlag(fields[6], "rollback progress state", journalPath);
                var cleanupStarted = !legacy && ParseJournalFlag(fields[7], "rollback cleanup write-ahead state", journalPath);
                var cleanupCompleted = !legacy && ParseJournalFlag(fields[8], "rollback cleanup progress state", journalPath);
                var originalSha256 = legacy ? null : fields[9];
                var stagedSha256 = legacy ? null : fields[10];
                ValidatePublicationJournalEntry(
                    destination,
                    backup,
                    newFile,
                    hadDestination,
                    published,
                    rollbackStarted,
                    rollbackCompleted,
                    cleanupStarted,
                    cleanupCompleted,
                    originalSha256,
                    stagedSha256,
                    legacy,
                    journalPath);
                if (!seenPaths.Add(destination) ||
                    !seenPaths.Add(newFile) ||
                    (backup != null && !seenPaths.Add(backup)))
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication journal contains duplicate paths: " + journalPath + ".");
                }

                entries.Add(new PublicationJournalEntry(
                    destination,
                    backup,
                    newFile,
                    hadDestination,
                    published,
                    rollbackStarted,
                    rollbackCompleted,
                    cleanupStarted,
                    cleanupCompleted,
                    originalSha256,
                    stagedSha256));
            }

            var journal = new PublicationJournal(journalPath, entries, state, legacy);
            ValidatePublicationJournalProgress(journal);
            return journal;
        }

        private static bool TryReadPublicationJournal(string journalPath, out PublicationJournal journal)
        {
            try
            {
                journal = ReadPublicationJournal(journalPath);
                return true;
            }
            catch
            {
                journal = null;
                return false;
            }
        }

        private static PublicationJournalState ParseJournalState(string value, string journalPath)
        {
            if (string.Equals(value, "prepared", StringComparison.Ordinal))
            {
                return PublicationJournalState.Prepared;
            }

            if (string.Equals(value, "rolling-back", StringComparison.Ordinal))
            {
                return PublicationJournalState.RollingBack;
            }

            if (string.Equals(value, "committed", StringComparison.Ordinal))
            {
                return PublicationJournalState.Committed;
            }

            throw new InvalidOperationException("Procedural audio publication journal state is invalid: " + journalPath + ".");
        }

        private static string SerializeJournalState(PublicationJournalState state)
        {
            switch (state)
            {
                case PublicationJournalState.Prepared:
                    return "prepared";
                case PublicationJournalState.RollingBack:
                    return "rolling-back";
                case PublicationJournalState.Committed:
                    return "committed";
                default:
                    throw new InvalidOperationException("Procedural audio publication journal state cannot be serialized.");
            }
        }

        private static bool ParseJournalFlag(string value, string name, string journalPath)
        {
            if (string.Equals(value, "0", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(value, "1", StringComparison.Ordinal))
            {
                return true;
            }

            throw new InvalidOperationException(
                "Procedural audio publication journal " + name + " is invalid: " + journalPath + ".");
        }

        private static string EncodeJournalPath(string path)
        {
            return Convert.ToBase64String(new UTF8Encoding(false).GetBytes(path));
        }

        private static string DecodeJournalPath(string value, string name, string journalPath)
        {
            try
            {
                var path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value));
                if (!Path.IsPathRooted(path))
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication journal " + name + " is not absolute: " + journalPath + ".");
                }

                return Path.GetFullPath(path);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "Procedural audio publication journal " + name + " is not base64: " + journalPath + ".",
                    exception);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    "Procedural audio publication journal " + name + " is not UTF-8: " + journalPath + ".",
                    exception);
            }
        }

        private static void ValidatePublicationJournalEntry(
            string destination,
            string backup,
            string newFile,
            bool hadDestination,
            bool published,
            bool rollbackStarted,
            bool rollbackCompleted,
            bool cleanupStarted,
            bool cleanupCompleted,
            string originalSha256,
            string stagedSha256,
            bool legacy,
            string journalPath)
        {
            if (!IsProjectPath(destination) ||
                destination.EndsWith(".tmp", StringComparison.Ordinal) ||
                destination.EndsWith(".bak", StringComparison.Ordinal) ||
                !IsProjectPath(newFile) ||
                !newFile.EndsWith(".tmp", StringComparison.Ordinal) ||
                string.Equals(destination, newFile, StringComparison.OrdinalIgnoreCase) ||
                (hadDestination && (backup == null || !IsProjectPath(backup) ||
                                    !backup.EndsWith(".bak", StringComparison.Ordinal) ||
                                    string.Equals(destination, backup, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(newFile, backup, StringComparison.OrdinalIgnoreCase))) ||
                (!hadDestination && backup != null) ||
                (!legacy && (!IsSha256(stagedSha256) ||
                             (hadDestination
                                 ? !IsSha256(originalSha256)
                                 : !string.IsNullOrEmpty(originalSha256)) ||
                             (rollbackStarted && !published) ||
                             (cleanupStarted && !rollbackCompleted) ||
                             (cleanupCompleted && !cleanupStarted))))
            {
                throw new InvalidOperationException(
                    "Procedural audio publication journal contains an invalid entry: " + journalPath + ".");
            }
        }

        private static void ValidatePublicationJournalProgress(PublicationJournal journal)
        {
            if (journal.Legacy)
            {
                if (journal.State != PublicationJournalState.Prepared &&
                    journal.State != PublicationJournalState.Committed)
                {
                    throw new InvalidOperationException(
                        "Legacy procedural audio publication journal state is invalid: " + journal.Path + ".");
                }

                return;
            }

            var anyRollbackCleanupStarted = false;
            foreach (var entry in journal.Entries)
            {
                if (journal.State == PublicationJournalState.Prepared &&
                    (entry.RollbackStarted || entry.RollbackCompleted ||
                     entry.CleanupStarted || entry.CleanupCompleted))
                {
                    throw new InvalidOperationException(
                        "Prepared procedural audio publication journal contains rollback progress: " + journal.Path + ".");
                }

                if (journal.State == PublicationJournalState.Committed &&
                    (!entry.Published || entry.RollbackStarted || entry.RollbackCompleted ||
                     entry.CleanupStarted || entry.CleanupCompleted))
                {
                    throw new InvalidOperationException(
                        "Committed procedural audio publication journal contains rollback progress: " + journal.Path + ".");
                }

                anyRollbackCleanupStarted |= entry.CleanupStarted;
            }

            if (anyRollbackCleanupStarted)
            {
                foreach (var entry in journal.Entries)
                {
                    if (!entry.RollbackCompleted)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication journal began cleanup before rollback completed: " +
                            journal.Path + ".");
                    }
                }
            }
        }


        private static PreparedPublicationFileState GetPreparedPublicationFileState(PublicationJournalEntry entry)
        {
            var destinationExists = File.Exists(entry.Destination);
            var newFileExists = File.Exists(entry.NewFile);
            if (entry.HadDestination)
            {
                var backupExists = File.Exists(entry.Backup);
                if (destinationExists && newFileExists && !backupExists)
                {
                    VerifyJournalFileHash(entry.Destination, entry.OriginalSha256);
                    VerifyJournalFileHash(entry.NewFile, entry.StagedSha256);
                    return PreparedPublicationFileState.PrePublication;
                }

                if (destinationExists && !newFileExists && backupExists)
                {
                    VerifyJournalFileHash(entry.Destination, entry.StagedSha256);
                    VerifyJournalFileHash(entry.Backup, entry.OriginalSha256);
                    return PreparedPublicationFileState.Published;
                }

                if (destinationExists && !newFileExists && !backupExists)
                {
                    VerifyJournalFileHash(entry.Destination, entry.OriginalSha256);
                    return PreparedPublicationFileState.Restored;
                }
            }
            else
            {
                if (!destinationExists && newFileExists)
                {
                    VerifyJournalFileHash(entry.NewFile, entry.StagedSha256);
                    return PreparedPublicationFileState.PrePublication;
                }

                if (destinationExists && !newFileExists)
                {
                    VerifyJournalFileHash(entry.Destination, entry.StagedSha256);
                    return PreparedPublicationFileState.Published;
                }

                if (!destinationExists && !newFileExists)
                {
                    return PreparedPublicationFileState.Restored;
                }
            }

            throw new InvalidOperationException(
                "Procedural audio publication journal filesystem state is ambiguous or tampered: " +
                entry.Destination + ".");
        }

        private static void VerifyJournalFileHash(string path, string expectedSha256)
        {
            if (!IsSha256(expectedSha256) ||
                !File.Exists(path) ||
                !string.Equals(Sha(File.ReadAllBytes(path)), expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Procedural audio publication journal filesystem content is ambiguous or tampered: " + path + ".");
            }
        }

        private static bool RollbackPreparedPublicationJournal(
            PublicationJournal journal,
            List<Exception> cleanupDiagnostics)
        {
            if (journal.Committed)
            {
                throw new InvalidOperationException(
                    "Committed procedural audio publication cannot be rolled back: " + journal.Path + ".");
            }

            if (journal.Legacy)
            {
                throw new InvalidOperationException(
                    "Legacy prepared procedural audio publication journal cannot be resumed safely: " +
                    journal.Path + ".");
            }

            if (journal.State == PublicationJournalState.Prepared)
            {
                journal.State = PublicationJournalState.RollingBack;
                PersistPublicationJournal(journal);
            }
            else if (journal.State != PublicationJournalState.RollingBack)
            {
                throw new InvalidOperationException(
                    "Procedural audio publication rollback state is invalid: " + journal.Path + ".");
            }

            ValidatePublicationJournalProgress(journal);

            var assetFilesChanged = false;
            for (var index = journal.Entries.Count - 1; index >= 0; index--)
            {
                var entry = journal.Entries[index];
                var fileState = GetPreparedPublicationFileState(entry);
                if (entry.RollbackCompleted)
                {
                    if (entry.RollbackStarted || entry.CleanupCompleted)
                    {
                        if (fileState != PreparedPublicationFileState.Restored)
                        {
                            throw new InvalidOperationException(
                                "Procedural audio publication rollback progress is inconsistent: " +
                                entry.Destination + ".");
                        }
                    }
                    else if (!entry.CleanupStarted &&
                             fileState != PreparedPublicationFileState.PrePublication)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication rollback progress is inconsistent: " +
                            entry.Destination + ".");
                    }
                    else if (entry.CleanupStarted &&
                             fileState != PreparedPublicationFileState.PrePublication &&
                             fileState != PreparedPublicationFileState.Restored)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication rollback cleanup progress is inconsistent: " +
                            entry.Destination + ".");
                    }

                    assetFilesChanged |= entry.RollbackStarted;
                    continue;
                }

                if (entry.RollbackStarted)
                {
                    if (fileState == PreparedPublicationFileState.Restored)
                    {
                        entry.RollbackCompleted = true;
                        PersistPublicationJournal(journal);
                        assetFilesChanged = true;
                        continue;
                    }

                    if (fileState != PreparedPublicationFileState.Published)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication rollback write-ahead state is inconsistent: " +
                            entry.Destination + ".");
                    }
                }
                else
                {
                    if (fileState == PreparedPublicationFileState.PrePublication)
                    {
                        if (entry.Published)
                        {
                            throw new InvalidOperationException(
                                "Procedural audio publication progress is inconsistent before rollback: " +
                                entry.Destination + ".");
                        }

                        entry.RollbackCompleted = true;
                        PersistPublicationJournal(journal);
                        continue;
                    }

                    if (fileState != PreparedPublicationFileState.Published)
                    {
                        throw new InvalidOperationException(
                            "Procedural audio publication rollback state is ambiguous or tampered: " +
                            entry.Destination + ".");
                    }

                    if (!entry.Published)
                    {
                        entry.Published = true;
                        PersistPublicationJournal(journal);
                    }

                    entry.RollbackStarted = true;
                    PersistPublicationJournal(journal);
                }

                if (entry.HadDestination)
                {
                    File.Replace(entry.Backup, entry.Destination, null);
                }
                else
                {
                    File.Delete(entry.Destination);
                }

                if (GetPreparedPublicationFileState(entry) != PreparedPublicationFileState.Restored)
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication rollback inverse did not restore the expected state: " +
                        entry.Destination + ".");
                }

                entry.RollbackCompleted = true;
                PersistPublicationJournal(journal);
                assetFilesChanged = true;
            }

            var cleanupSucceeded = true;
            foreach (var entry in journal.Entries)
            {
                try
                {
                    CleanupRolledBackPublicationEntry(journal, entry);
                }
                catch (Exception cleanupException)
                {
                    cleanupSucceeded = false;
                    if (cleanupDiagnostics == null)
                    {
                        throw;
                    }

                    cleanupDiagnostics.Add(new InvalidOperationException(
                        "Procedural audio publication rollback cleanup is pending: " + entry.NewFile + ".",
                        cleanupException));
                }
            }

            if (!cleanupSucceeded)
            {
                return assetFilesChanged;
            }

            try
            {
                File.Delete(journal.Path);
            }
            catch (Exception cleanupException)
            {
                if (cleanupDiagnostics == null)
                {
                    throw;
                }

                cleanupDiagnostics.Add(new InvalidOperationException(
                    "Procedural audio publication rollback completed, but journal cleanup is pending: " +
                    journal.Path + ".",
                    cleanupException));
            }

            return assetFilesChanged;
        }

        private static void CleanupRolledBackPublicationEntry(
            PublicationJournal journal,
            PublicationJournalEntry entry)
        {
            var fileState = GetPreparedPublicationFileState(entry);
            if (entry.CleanupCompleted)
            {
                if (fileState != PreparedPublicationFileState.Restored)
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication rollback cleanup progress is inconsistent: " +
                        entry.Destination + ".");
                }

                return;
            }

            if (!entry.CleanupStarted)
            {
                var expectedState = entry.RollbackStarted
                    ? PreparedPublicationFileState.Restored
                    : PreparedPublicationFileState.PrePublication;
                if (fileState != expectedState)
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication rollback cleanup state is ambiguous or tampered: " +
                        entry.Destination + ".");
                }

                entry.CleanupStarted = true;
                PersistPublicationJournal(journal);
                fileState = GetPreparedPublicationFileState(entry);
            }

            if (fileState == PreparedPublicationFileState.PrePublication)
            {
                if (entry.RollbackStarted)
                {
                    throw new InvalidOperationException(
                        "Procedural audio publication rollback cleanup retained an unexpected staged file: " +
                        entry.NewFile + ".");
                }

                File.Delete(entry.NewFile);
                fileState = GetPreparedPublicationFileState(entry);
            }

            if (fileState != PreparedPublicationFileState.Restored)
            {
                throw new InvalidOperationException(
                    "Procedural audio publication rollback cleanup state is ambiguous or tampered: " +
                    entry.Destination + ".");
            }

            entry.CleanupCompleted = true;
            PersistPublicationJournal(journal);
        }

        private static void CleanupCommittedPublicationJournal(
            PublicationJournal journal,
            List<Exception> cleanupDiagnostics)
        {
            if (!journal.Committed)
            {
                throw new InvalidOperationException(
                    "Prepared procedural audio publication journal cannot be cleaned as committed: " + journal.Path + ".");
            }

            foreach (var entry in journal.Entries)
            {
                if (!entry.Published ||
                    File.Exists(entry.NewFile) ||
                    !File.Exists(entry.Destination))
                {
                    throw new InvalidOperationException(
                        "Committed procedural audio publication journal is inconsistent: " + journal.Path + ".");
                }
                if (!journal.Legacy)
                {
                    VerifyJournalFileHash(entry.Destination, entry.StagedSha256);
                    if (entry.HadDestination && File.Exists(entry.Backup))
                    {
                        VerifyJournalFileHash(entry.Backup, entry.OriginalSha256);
                    }
                }
            }

            var cleanupSucceeded = true;
            foreach (var entry in journal.Entries)
            {
                if (!entry.HadDestination || !File.Exists(entry.Backup))
                {
                    continue;
                }

                try
                {
                    File.Delete(entry.Backup);
                }
                catch (Exception cleanupException)
                {
                    cleanupSucceeded = false;
                    cleanupDiagnostics.Add(new InvalidOperationException(
                        "Procedural audio publication committed, but backup cleanup is pending: " + entry.Backup + ".",
                        cleanupException));
                }
            }

            if (!cleanupSucceeded)
            {
                return;
            }

            try
            {
                File.Delete(journal.Path);
            }
            catch (Exception cleanupException)
            {
                cleanupDiagnostics.Add(new InvalidOperationException(
                    "Procedural audio publication committed, but journal cleanup is pending: " + journal.Path + ".",
                    cleanupException));
            }
        }

        private static bool IsProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            {
                return false;
            }

            var projectRoot = Path.GetFullPath(".");
            if (!projectRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                projectRoot += Path.DirectorySeparatorChar;
            }

            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteNewFileDurably(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void ReportCleanupDiagnostics(IReadOnlyList<Exception> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                Debug.LogWarning(diagnostic);
            }
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] BuildWave(float frequency, int seed)
        {
            var pcm = new short[Samples];
            var random = new System.Random(seed);
            for (var i = 0; i < pcm.Length; i++)
            {
                var t = (double)i / SampleRate;
                var envelope = Math.Min(1d, i / 400d) * Math.Max(0d, 1d - t / 0.25d);
                var tone = Math.Sin(2d * Math.PI * frequency * t) * 0.7d;
                var texture = (random.NextDouble() * 2d - 1d) * 0.04d;
                pcm[i] = (short)Math.Round(Math.Clamp((tone + texture) * envelope, -1d, 1d) * short.MaxValue);
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length * 2);
            foreach (var sample in pcm)
            {
                writer.Write(sample);
            }

            return stream.ToArray();
        }

        private static string Sha(byte[] data)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }
        private readonly struct ManifestRecord
        {
            public ManifestRecord(string raw, List<string> fields)
            {
                Raw = raw;
                Fields = new List<string>(fields).AsReadOnly();
                AssetId = Fields[0];
                FieldCount = Fields.Count;
            }

            public string Raw { get; }
            public IReadOnlyList<string> Fields { get; }
            public string AssetId { get; }
            public int FieldCount { get; }
        }

        private readonly struct ExistingRecord
        {
            public ExistingRecord(byte[] bytes, CanonicalJsonValue root)
            {
                Bytes = bytes;
                Root = root;
            }

            public byte[] Bytes { get; }
            public CanonicalJsonValue Root { get; }
            public bool Exists => Root != null;
        }

        private sealed class PublicationResult
        {
            public PublicationResult(bool committed, List<Exception> cleanupDiagnostics)
            {
                Committed = committed;
                CleanupDiagnostics = cleanupDiagnostics.AsReadOnly();
            }

            public bool Committed { get; }
            public IReadOnlyList<Exception> CleanupDiagnostics { get; }
        }
        private sealed class CommitStateUncertainException : Exception
        {
            public CommitStateUncertainException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }
        private sealed class RecoveryResult
        {
            public RecoveryResult(bool assetFilesChanged, List<Exception> cleanupDiagnostics)
            {
                AssetFilesChanged = assetFilesChanged;
                CleanupDiagnostics = cleanupDiagnostics.AsReadOnly();
            }

            public bool AssetFilesChanged { get; }
            public IReadOnlyList<Exception> CleanupDiagnostics { get; }
        }

        private enum PublicationJournalState
        {
            Prepared,
            RollingBack,
            Committed
        }

        private enum PreparedPublicationFileState
        {
            PrePublication,
            Published,
            Restored
        }

        private sealed class PublicationJournal
        {
            public PublicationJournal(
                string path,
                List<PublicationJournalEntry> entries,
                PublicationJournalState state,
                bool legacy)
            {
                Path = path;
                Entries = entries;
                State = state;
                Legacy = legacy;
            }

            public string Path { get; }
            public List<PublicationJournalEntry> Entries { get; }
            public PublicationJournalState State { get; set; }
            public bool Legacy { get; }
            public bool Committed => State == PublicationJournalState.Committed;
        }

        private sealed class PublicationJournalEntry
        {
            public PublicationJournalEntry(
                string destination,
                string backup,
                string newFile,
                bool hadDestination,
                bool published,
                bool rollbackStarted,
                bool rollbackCompleted,
                bool cleanupStarted,
                bool cleanupCompleted,
                string originalSha256,
                string stagedSha256)
            {
                Destination = destination;
                Backup = backup;
                NewFile = newFile;
                HadDestination = hadDestination;
                Published = published;
                RollbackStarted = rollbackStarted;
                RollbackCompleted = rollbackCompleted;
                CleanupStarted = cleanupStarted;
                CleanupCompleted = cleanupCompleted;
                OriginalSha256 = originalSha256;
                StagedSha256 = stagedSha256;
            }

            public string Destination { get; }
            public string Backup { get; }
            public string NewFile { get; }
            public bool HadDestination { get; }
            public bool Published { get; set; }
            public bool RollbackStarted { get; set; }
            public bool RollbackCompleted { get; set; }
            public bool CleanupStarted { get; set; }
            public bool CleanupCompleted { get; set; }
            public string OriginalSha256 { get; }
            public string StagedSha256 { get; }
        }

        private readonly struct PublishedOutput
        {
            public PublishedOutput(string destination, string backup, bool hadDestination)
            {
                Destination = destination;
                Backup = backup;
                HadDestination = hadDestination;
            }

            public string Destination { get; }
            public string Backup { get; }
            public bool HadDestination { get; }
        }
        private readonly struct StagedOutput
        {
            public StagedOutput(
                string destination,
                string temporary,
                OutputPreimage preimage,
                string payloadSha256)
            {
                Destination = destination;
                Temporary = temporary;
                Preimage = preimage;
                PayloadSha256 = payloadSha256;
            }

            public string Destination { get; }
            public string Temporary { get; }
            public OutputPreimage Preimage { get; }
            public string PayloadSha256 { get; }
        }

        private readonly struct OutputPreimage
        {
            public OutputPreimage(
                string destination,
                bool exists,
                string sha256)
            {
                Destination = destination;
                Exists = exists;
                Sha256 = sha256;
            }

            public string Destination { get; }
            public bool Exists { get; }
            public string Sha256 { get; }
        }
    }
}
