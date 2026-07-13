using System;
using System.Collections.Generic;
using Overbless.Runtime;

namespace Overbless.Editor.Evidence
{
    public sealed class AudioEventEvidence
    {
        public AudioEventEvidence(FunctionalAudioEvent eventType, long token, int frame)
        {
            EventType = eventType;
            Token = token;
            Frame = frame;
        }

        public FunctionalAudioEvent EventType { get; }
        public long Token { get; }
        public int Frame { get; }
    }

    /// <summary>Serializes functional-audio emission records in deterministic token order.</summary>
    public static class AudioEventExporter
    {
        public const string Schema = "overbless.audio-events/v1";
        public const string DefaultOutputPath = "Evidence/audio-events.json";
        private static readonly FunctionalAudioEvent[] RequiredEvents =
        {
            FunctionalAudioEvent.DasherReady,
            FunctionalAudioEvent.ArcherReady,
            FunctionalAudioEvent.ExitOpened
        };

        public static CanonicalJsonValue CreatePayload(IEnumerable<FunctionalAudioRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var events = new List<AudioEventEvidence>();
            foreach (var record in records)
            {
                events.Add(new AudioEventEvidence(record.EventType, record.Token, record.Frame));
            }
            return CreatePayload(events);
        }

        public static CanonicalJsonValue CreatePayload(IEnumerable<AudioEventEvidence> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var events = new List<AudioEventEvidence>();
            foreach (var record in records)
            {
                if (record == null) throw new ArgumentException("Audio event records cannot contain null.", nameof(records));
                ValidateRecord(record);
                events.Add(record);
            }

            events.Sort(CompareEvents);
            ValidateRequiredEventsAndUniqueTokens(events);
            var values = new List<CanonicalJsonValue>();
            foreach (var record in events) values.Add(ToCanonicalValue(record));

            var value = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("events", CanonicalJsonValue.Array(values)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(Schema)));
            var shape = EvidenceSchemaValidator.ValidateSchemaObject(value, Schema, new[] { "schema", "events" });
            if (!shape.IsValid) throw new InvalidOperationException("Audio event payload shape is invalid: " + shape.Code + ".");
            return value;
        }

        public static string Export(string outputPath, IEnumerable<FunctionalAudioRecord> records)
        {
            var payload = CreatePayload(records);
            var bytes = CanonicalJson.SerializeUtf8(payload);
            EvidenceArtifactIO.WriteNew(outputPath, bytes);
            return CanonicalJson.Sha256Hex(bytes);
        }

        public static string Export(string outputPath, IEnumerable<AudioEventEvidence> records)
        {
            var payload = CreatePayload(records);
            var bytes = CanonicalJson.SerializeUtf8(payload);
            EvidenceArtifactIO.WriteNew(outputPath, bytes);
            return CanonicalJson.Sha256Hex(bytes);
        }

        private static void ValidateRecord(AudioEventEvidence record)
        {
            if (!Enum.IsDefined(typeof(FunctionalAudioEvent), record.EventType))
            {
                throw new ArgumentOutOfRangeException(nameof(record), "Audio event type is unknown.");
            }
            if (record.Token <= 0) throw new ArgumentOutOfRangeException(nameof(record), "Audio event token must be positive.");
            if (record.Frame < 0) throw new ArgumentOutOfRangeException(nameof(record), "Audio event frame must be nonnegative.");
        }

        private static void ValidateRequiredEventsAndUniqueTokens(IReadOnlyList<AudioEventEvidence> events)
        {
            if (events.Count != RequiredEvents.Length)
            {
                throw new InvalidOperationException("Audio event evidence must contain exactly the required event set.");
            }

            var eventTypes = new HashSet<FunctionalAudioEvent>();
            var tokens = new HashSet<long>();
            foreach (var record in events)
            {
                if (!eventTypes.Add(record.EventType))
                {
                    throw new InvalidOperationException("Audio event evidence must contain each required event exactly once.");
                }
                if (!tokens.Add(record.Token))
                {
                    throw new InvalidOperationException("Audio event tokens must be globally unique.");
                }
            }
            if (!eventTypes.SetEquals(RequiredEvents))
            {
                throw new InvalidOperationException("Audio event evidence must contain exactly DasherReady, ArcherReady, and ExitOpened.");
            }
        }

        private static CanonicalJsonValue ToCanonicalValue(AudioEventEvidence record)
        {
            var eventName = Enum.GetName(typeof(FunctionalAudioEvent), record.EventType);
            if (eventName == null) throw new InvalidOperationException("Audio event type has no stable name.");
            return CanonicalJsonValue.Object(
                new CanonicalJsonProperty("event", CanonicalJsonValue.String(eventName)),
                new CanonicalJsonProperty("frame", CanonicalJsonValue.Number(record.Frame)),
                new CanonicalJsonProperty("token", CanonicalJsonValue.Number(record.Token)));
        }

        private static int CompareEvents(AudioEventEvidence left, AudioEventEvidence right)
        {
            var token = left.Token.CompareTo(right.Token);
            if (token != 0) return token;
            var eventName = CanonicalJson.CompareUtf8Ordinal(
                Enum.GetName(typeof(FunctionalAudioEvent), left.EventType),
                Enum.GetName(typeof(FunctionalAudioEvent), right.EventType));
            if (eventName != 0) return eventName;
            return left.Frame.CompareTo(right.Frame);
        }
    }
}
