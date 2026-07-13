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

        private static readonly (string Name, float Frequency, int Seed)[] Cues =
        {
            ("DasherReady", 180f, 104729),
            ("ArcherReady", 520f, 104731),
            ("AttackLocked", 760f, 104743),
            ("PlayerHit", 110f, 104759),
            ("SoulCollected", 940f, 104761),
            ("ExitOpened", 660f, 104773)
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

            var generatorSourceSha = Sha(File.ReadAllBytes(generatorSourcePath));
            var existingRecords = ReadManifestRecords(manifestPath);
            var ownedIds = new HashSet<string>(StringComparer.Ordinal);
            var staged = new List<StagedOutput>();
            var generatedRows = new List<string>();
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
                EnsureReviewedRecordIsStable(recordPath, cue.Name, cue.Seed, parameters, path, sha, generatorSourceSha, toolVersion);
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

                StageIfChanged(staged, path, bytes);
                StageIfChanged(staged, recordPath, CanonicalJson.SerializeUtf8(record));
                var notes = cue.Name + ";record=" + recordPath + ";generatorSha=" + generatorSourceSha;
                generatedRows.Add(string.Join(",", new[]
                {
                    Csv(assetId),
                    "audio",
                    Csv(path),
                    "generated",
                    Csv("Unity Editor " + toolVersion),
                    "none",
                    Csv(generatedAtUtc),
                    "none",
                    Csv(generatorSourcePath),
                    Csv(path),
                    "none",
                    "repository-native procedural",
                    Csv(reviewer),
                    Csv(notes),
                    sha
                }));
            }

            var finalManifestLines = new List<string> { ManifestHeader };
            var seenExistingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in existingRecords)
            {
                if (string.Equals(record.Raw, ManifestHeader, StringComparison.Ordinal))
                {
                    continue;
                }

                if (record.FieldCount < 2 || string.IsNullOrEmpty(record.AssetId))
                {
                    throw new InvalidOperationException("Asset manifest contains a malformed row.");
                }

                if (!seenExistingIds.Add(record.AssetId))
                {
                    throw new InvalidOperationException("Asset manifest contains duplicate asset ID: " + record.AssetId);
                }

                if (!ownedIds.Contains(record.AssetId))
                {
                    finalManifestLines.Add(record.Raw);
                }
            }

            finalManifestLines.AddRange(generatedRows);
            var manifestBytes = new UTF8Encoding(false).GetBytes(string.Join("\n", finalManifestLines) + "\n");
            StageIfChanged(staged, manifestPath, manifestBytes);

            PublishAll(staged);
            }
            catch (Exception generationException)
            {
                var failures = new List<Exception> { generationException };
                CleanupStaged(staged, failures);
                throw new AggregateException("Procedural audio generation failed.", failures);
            }

            AssetDatabase.Refresh();
        }

        private static List<ManifestRecord> ReadManifestRecords(string path)
        {
            if (!File.Exists(path))
            {
                return new List<ManifestRecord>();
            }

            return ParseCsvRecords(File.ReadAllText(path, Encoding.UTF8));
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
                    fields[0],
                    fields.Count));
            }
        }

        private static FileStream AcquireGenerationLock()
        {
            var projectIdentity = Sha(Encoding.UTF8.GetBytes(Path.GetFullPath("."))).Substring(0, 16);
            var lockPath = Path.Combine(Path.GetTempPath(), "overbless-procedural-audio-" + projectIdentity + ".lock");
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private static void EnsureReviewedRecordIsStable(
            string recordPath,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion)
        {
            if (!File.Exists(recordPath) ||
                TryReadMatchingRecord(recordPath, eventName, seed, parameters, path, outputSha, generatorSha, toolVersion, out _))
            {
                return;
            }

            var existing = File.ReadAllText(recordPath, Encoding.UTF8);
            var reviewer = ExtractJsonString(existing, "reviewer");
            if (!IsValidReviewer(reviewer))
            {
                throw new InvalidOperationException("Existing procedural audio reviewer is malformed.");
            }

            if (!string.Equals(reviewer, PendingReviewer, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Reviewed procedural audio provenance is immutable; create a versioned asset ID and record.");
            }
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

            var reviewer = ExtractJsonString(existing, "reviewer");
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
            out string existing)
        {
            existing = null;
            if (!File.Exists(recordPath))
            {
                return false;
            }

            existing = File.ReadAllText(recordPath, Encoding.UTF8);
            return RecordSemanticsMatch(
                existing,
                eventName,
                seed,
                parameters,
                path,
                outputSha,
                generatorSha,
                toolVersion);
        }

        private static bool RecordSemanticsMatch(
            string json,
            string eventName,
            int seed,
            string parameters,
            string path,
            string outputSha,
            string generatorSha,
            string toolVersion)
        {
            if (!CanonicalJson.TryParse(json, out var root, out var error) ||
                root.Kind != CanonicalJsonKind.Object)
            {
                throw new InvalidOperationException("Procedural audio record is malformed JSON: " + error);
            }

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

            return string.Equals(GetRequiredRecordString(root, "event"), eventName, StringComparison.Ordinal) &&
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

        private static string GetRequiredRecordString(CanonicalJsonValue root, string propertyName)
        {
            CanonicalJsonValue value;
            if (!root.TryGetSingleProperty(propertyName, out value) || value.Kind != CanonicalJsonKind.String)
            {
                throw new InvalidOperationException("Procedural audio record requires one string property '" + propertyName + "'.");
            }

            return value.StringValue;
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            if (!CanonicalJson.TryParse(json, out var root, out var error) ||
                root.Kind != CanonicalJsonKind.Object)
            {
                throw new InvalidOperationException("Procedural audio record is malformed JSON: " + error);
            }

            if (!root.TryGetSingleProperty(propertyName, out var value) ||
                value.Kind != CanonicalJsonKind.String)
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
                var generationUtc = ExtractJsonString(existing, "generationUtc");
                if (string.IsNullOrEmpty(generationUtc))
                {
                    throw new InvalidOperationException("Existing procedural audio generation time is missing.");
                }

                return generationUtc;
            }

            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private static void StageIfChanged(List<StagedOutput> outputs, string destination, byte[] bytes)
        {
            if (File.Exists(destination))
            {
                var existing = File.ReadAllBytes(destination);
                if (existing.Length == bytes.Length)
                {
                    var equal = true;
                    for (var index = 0; index < bytes.Length; index++)
                    {
                        if (existing[index] != bytes[index])
                        {
                            equal = false;
                            break;
                        }
                    }

                    if (equal)
                    {
                        return;
                    }
                }
            }

            outputs.Add(Stage(destination, bytes));
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
        private static StagedOutput Stage(string destination, byte[] bytes)
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

                return new StagedOutput(destination, temporary);
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

        private static void PublishAll(List<StagedOutput> outputs)
        {
            var published = new List<PublishedOutput>();
            var failures = new List<Exception>();
            var preserveBackups = false;
            try
            {
                foreach (var output in outputs)
                {
                    if (File.Exists(output.Destination))
                    {
                        var backup = output.Destination + "." + Guid.NewGuid().ToString("N") + ".bak";
                        File.Replace(output.Temporary, output.Destination, backup);
                        published.Add(new PublishedOutput(output.Destination, backup, true));
                    }
                    else
                    {
                        File.Move(output.Temporary, output.Destination);
                        published.Add(new PublishedOutput(output.Destination, null, false));
                    }
                }
            }
            catch (Exception publicationException)
            {
                failures.Add(publicationException);
                for (var index = published.Count - 1; index >= 0; index--)
                {
                    var output = published[index];
                    try
                    {
                        if (output.HadDestination)
                        {
                            if (File.Exists(output.Destination))
                            {
                                File.Replace(output.Backup, output.Destination, null);
                            }
                            else
                            {
                                File.Move(output.Backup, output.Destination);
                            }
                        }
                        else if (File.Exists(output.Destination))
                        {
                            File.Delete(output.Destination);
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        preserveBackups = true;
                        failures.Add(rollbackException);
                    }
                }
            }

            CleanupStaged(outputs, failures);
            if (!preserveBackups)
            {
                foreach (var output in published)
                {
                    try
                    {
                        if (output.Backup != null && File.Exists(output.Backup))
                        {
                            File.Delete(output.Backup);
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        failures.Add(cleanupException);
                    }
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException("Procedural audio publication failed or required cleanup.", failures);
            }
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
            public ManifestRecord(string raw, string assetId, int fieldCount)
            {
                Raw = raw;
                AssetId = assetId;
                FieldCount = fieldCount;
            }

            public string Raw { get; }
            public string AssetId { get; }
            public int FieldCount { get; }
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
            public StagedOutput(string destination, string temporary)
            {
                Destination = destination;
                Temporary = temporary;
            }

            public string Destination { get; }
            public string Temporary { get; }
        }
    }
}
