using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Overbless.Editor.Evidence
{
    public sealed class FrameCompletionRecord
    {
        internal FrameCompletionRecord(long completedAtMicroseconds, long durationMicroseconds, bool foreground, bool unpaused)
        {
            CompletedAtMicroseconds = completedAtMicroseconds;
            DurationMicroseconds = durationMicroseconds;
            Foreground = foreground;
            Unpaused = unpaused;
        }

        public long CompletedAtMicroseconds { get; }
        public long DurationMicroseconds { get; }
        public bool Foreground { get; }
        public bool Unpaused { get; }
    }

    public sealed class FrameTimeReport
    {
        internal FrameTimeReport(
            CanonicalJsonValue value,
            IReadOnlyList<PerformanceBucket> buckets,
            IReadOnlyList<FrameCompletionRecord> sampleRecords,
            bool meetsFpsFloor,
            bool allForeground,
            bool noPause)
        {
            Value = value;
            Buckets = buckets;
            SampleRecords = sampleRecords;
            MeetsFpsFloor = meetsFpsFloor;
            AllForeground = allForeground;
            NoPause = noPause;
        }

        public CanonicalJsonValue Value { get; }
        public IReadOnlyList<PerformanceBucket> Buckets { get; }
        public IReadOnlyList<FrameCompletionRecord> SampleRecords { get; }
        public bool MeetsFpsFloor { get; }
        public bool AllForeground { get; }
        public bool NoPause { get; }
        public bool Passes => MeetsFpsFloor && AllForeground && NoPause;
        public byte[] Utf8Bytes => CanonicalJson.SerializeUtf8(Value);
    }

    /// <summary>Records completed frames with monotonic microsecond timestamps and produces a fixed sixty-second performance sample.</summary>
    public sealed class FrameTimeCollector
    {
        public const string Schema = "overbless.performance/v1";
        public const int WarmupSeconds = 10;
        public const int SampleSeconds = 60;
        public const int BucketCount = 60;
        public const long MicrosecondsPerSecond = 1000000L;
        public const long MinimumFramesPerSecond = 45L;

        private readonly List<FrameCompletionRecord> records = new List<FrameCompletionRecord>();
        private readonly List<ApplicationStateRecord> applicationStates = new List<ApplicationStateRecord>();
        private readonly long bucketOriginMicroseconds;
        private readonly long stopwatchStartTicks;
        private long lastRecordedMicroseconds;
        private bool hasRecordedFrame;
        private long lastApplicationStateMicroseconds;
        private bool hasApplicationState;

        public FrameTimeCollector()
            : this(WarmupSeconds * MicrosecondsPerSecond)
        {
        }

        public FrameTimeCollector(long bucketOriginMicroseconds)
        {
            var requiredOriginMicroseconds = WarmupSeconds * MicrosecondsPerSecond;
            if (bucketOriginMicroseconds != requiredOriginMicroseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(bucketOriginMicroseconds), "Performance sampling must begin exactly ten seconds after realtime collection starts.");
            }
            this.bucketOriginMicroseconds = bucketOriginMicroseconds;
            stopwatchStartTicks = Stopwatch.GetTimestamp();
        }

        public long BucketOriginMicroseconds => bucketOriginMicroseconds;
        public IReadOnlyList<FrameCompletionRecord> Records => records.AsReadOnly();

        /// <summary>Records a completed frame using a monotonic Stopwatch timestamp and frame-local focus/pause observations.</summary>
        public void RecordCompletedFrame()
        {
            var timestamp = GetElapsedMicroseconds();
            var duration = hasRecordedFrame ? timestamp - lastRecordedMicroseconds : 0L;
            RecordCompletedFrame(timestamp, duration, Application.isFocused, Time.timeScale > 0f);
        }

        /// <summary>Records one completed frame. Call this after the frame is complete, not when it begins.</summary>
        public void RecordCompletedFrame(long completedAtMicroseconds, long durationMicroseconds, bool foreground, bool unpaused)
        {
            if (completedAtMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(completedAtMicroseconds));
            if (durationMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(durationMicroseconds));
            if (!hasRecordedFrame)
            {
                if (durationMicroseconds != 0)
                {
                    throw new InvalidOperationException("The first completed frame must use a zero duration because no prior completion timestamp exists.");
                }
            }
            else
            {
                if (completedAtMicroseconds <= lastRecordedMicroseconds)
                {
                    throw new InvalidOperationException("Completed-frame timestamps must be strictly increasing.");
                }
                var expectedDuration = completedAtMicroseconds - lastRecordedMicroseconds;
                if (durationMicroseconds != expectedDuration)
                {
                    throw new InvalidOperationException("Completed-frame duration must equal the elapsed time since the prior completion.");
                }
            }

            records.Add(new FrameCompletionRecord(completedAtMicroseconds, durationMicroseconds, foreground, unpaused));
            lastRecordedMicroseconds = completedAtMicroseconds;
            hasRecordedFrame = true;
        }

        /// <summary>Records every focus or pause transition plus explicit observations at both performance-sample boundaries.</summary>
        public void RecordApplicationState(long observedAtMicroseconds, bool foreground, bool unpaused)
        {
            if (observedAtMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(observedAtMicroseconds));
            if (hasApplicationState && observedAtMicroseconds < lastApplicationStateMicroseconds)
            {
                throw new InvalidOperationException("Application-state timestamps must be monotonic.");
            }

            applicationStates.Add(new ApplicationStateRecord(observedAtMicroseconds, foreground, unpaused));
            lastApplicationStateMicroseconds = observedAtMicroseconds;
            hasApplicationState = true;
        }

        public FrameTimeReport CreateReport(string browser, string resolution, string scenario)
        {
            ValidateCell(browser, resolution, scenario);
            var sampleEnd = checked(bucketOriginMicroseconds + SampleSeconds * MicrosecondsPerSecond);
            var sampleRecords = GetSampleRecords(sampleEnd);
            if (sampleRecords.Count == 0) throw new InvalidOperationException("Performance sample contains no completed frames.");
            if (!HasCompletionAtOrAfter(sampleEnd))
            {
                throw new InvalidOperationException("Performance sample is incomplete; record a completed frame at or after the sixty-second boundary.");
            }
            ValidateSampleTiming(sampleRecords);
            var applicationState = GetSampleApplicationState(sampleEnd);

            var buckets = CreateBuckets(sampleRecords, sampleEnd);
            var allForeground = applicationState.AllForeground;
            var noPause = applicationState.NoPause;

            var meetsFpsFloor = true;
            foreach (var bucket in buckets)
            {
                if (bucket.CompletedFrames < MinimumFramesPerSecond || bucket.MinFpsEquivalent < MinimumFramesPerSecond)
                {
                    meetsFpsFloor = false;
                    break;
                }
            }

            var durations = new List<long>();
            foreach (var record in sampleRecords) durations.Add(record.DurationMicroseconds);
            durations.Sort();
            var longestFrame = durations[durations.Count - 1];
            var p95Frame = durations[(int)(((95L * durations.Count + 99L) / 100L) - 1L)];
            var reportValue = ToCanonicalValue(
                browser,
                resolution,
                scenario,
                buckets,
                allForeground,
                noPause,
                meetsFpsFloor && allForeground && noPause,
                longestFrame,
                p95Frame);

            var shape = EvidenceSchemaValidator.ValidateSchemaObject(reportValue, Schema, new[]
            {
                "schema", "browser", "resolution", "scenario", "warmupSeconds", "sampleSeconds", "bucketOriginMicroseconds", "buckets", "allForeground", "noPause", "status", "longestFrameUs", "p95FrameUs"
            });
            if (!shape.IsValid) throw new InvalidOperationException("Performance report shape is invalid: " + shape.Code + ".");

            var completionTimes = new List<long>();
            foreach (var record in sampleRecords) completionTimes.Add(record.CompletedAtMicroseconds);
            var bucketValidation = EvidenceSchemaValidator.ValidatePerformanceBuckets(bucketOriginMicroseconds, buckets, completionTimes);
            if (!bucketValidation.IsValid) throw new InvalidOperationException("Performance bucket calculation is invalid: " + bucketValidation.Code + ".");
            return new FrameTimeReport(reportValue, buckets.AsReadOnly(), sampleRecords.AsReadOnly(), meetsFpsFloor, allForeground, noPause);
        }

        public string Export(string outputPath, string browser, string resolution, string scenario)
        {
            var report = CreateReport(browser, resolution, scenario);
            var bytes = report.Utf8Bytes;
            EvidenceArtifactIO.WriteNew(outputPath, bytes);
            return CanonicalJson.Sha256Hex(bytes);
        }

        private List<FrameCompletionRecord> GetSampleRecords(long sampleEnd)
        {
            var result = new List<FrameCompletionRecord>();
            foreach (var record in records)
            {
                if (record.CompletedAtMicroseconds >= bucketOriginMicroseconds && record.CompletedAtMicroseconds < sampleEnd)
                {
                    result.Add(record);
                }
            }
            return result;
        }

        private void ValidateSampleTiming(IReadOnlyList<FrameCompletionRecord> sampleRecords)
        {
            var hasCompletionBeforeSample = false;
            foreach (var record in records)
            {
                if (record.CompletedAtMicroseconds < bucketOriginMicroseconds)
                {
                    hasCompletionBeforeSample = true;
                    break;
                }
            }
            if (!hasCompletionBeforeSample)
            {
                throw new InvalidOperationException("Performance sample requires a completed frame before the sample origin to establish frame durations.");
            }

            foreach (var record in sampleRecords)
            {
                if (record.DurationMicroseconds <= 0)
                {
                    throw new InvalidOperationException("Performance sample contains a frame without a timestamp-derived duration.");
                }
            }
        }

        private SampleApplicationState GetSampleApplicationState(long sampleEnd)
        {
            var hasInitialState = false;
            var initialForeground = true;
            var initialUnpaused = true;
            foreach (var state in applicationStates)
            {
                if (state.ObservedAtMicroseconds > bucketOriginMicroseconds) break;
                hasInitialState = true;
                initialForeground = state.Foreground;
                initialUnpaused = state.Unpaused;
            }
            if (!hasInitialState)
            {
                throw new InvalidOperationException("Performance sample requires an application-state observation at or before the sample origin.");
            }

            var allForeground = initialForeground;
            var noPause = initialUnpaused;
            var hasBoundaryState = false;
            foreach (var state in applicationStates)
            {
                if (state.ObservedAtMicroseconds < bucketOriginMicroseconds) continue;
                if (state.ObservedAtMicroseconds > sampleEnd) break;
                allForeground &= state.Foreground;
                noPause &= state.Unpaused;
                if (state.ObservedAtMicroseconds == sampleEnd) hasBoundaryState = true;
            }
            foreach (var record in records)
            {
                if (record.CompletedAtMicroseconds < bucketOriginMicroseconds) continue;
                if (record.CompletedAtMicroseconds > sampleEnd) break;
                allForeground &= record.Foreground;
                noPause &= record.Unpaused;
            }
            if (!hasBoundaryState)
            {
                throw new InvalidOperationException("Performance sample requires an application-state observation at the sixty-second boundary.");
            }
            return new SampleApplicationState(allForeground, noPause);
        }

        private bool HasCompletionAtOrAfter(long timestamp)
        {
            foreach (var record in records)
            {
                if (record.CompletedAtMicroseconds >= timestamp) return true;
            }
            return false;
        }

        private List<PerformanceBucket> CreateBuckets(IReadOnlyList<FrameCompletionRecord> sampleRecords, long sampleEnd)
        {
            var counts = new long[BucketCount];
            foreach (var record in sampleRecords)
            {
                var bucketIndex = (int)((record.CompletedAtMicroseconds - bucketOriginMicroseconds) / MicrosecondsPerSecond);
                if (bucketIndex < 0 || bucketIndex >= BucketCount)
                {
                    throw new InvalidOperationException("Completed frame is outside the fixed half-open sample interval.");
                }
                counts[bucketIndex]++;
            }

            var buckets = new List<PerformanceBucket>();
            for (var index = 0; index < BucketCount; index++)
            {
                var start = checked(bucketOriginMicroseconds + index * MicrosecondsPerSecond);
                var end = checked(start + MicrosecondsPerSecond);
                if (end > sampleEnd) throw new InvalidOperationException("Performance bucket exceeds the fixed sample interval.");
                buckets.Add(new PerformanceBucket(index, start, end, counts[index], counts[index]));
            }
            return buckets;
        }

        private CanonicalJsonValue ToCanonicalValue(
            string browser,
            string resolution,
            string scenario,
            IReadOnlyList<PerformanceBucket> buckets,
            bool allForeground,
            bool noPause,
            bool passes,
            long longestFrameUs,
            long p95FrameUs)
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
                new CanonicalJsonProperty("bucketOriginMicroseconds", CanonicalJsonValue.Number(bucketOriginMicroseconds)),
                new CanonicalJsonProperty("buckets", CanonicalJsonValue.Array(bucketValues)),
                new CanonicalJsonProperty("longestFrameUs", CanonicalJsonValue.Number(longestFrameUs)),
                new CanonicalJsonProperty("noPause", CanonicalJsonValue.Boolean(noPause)),
                new CanonicalJsonProperty("p95FrameUs", CanonicalJsonValue.Number(p95FrameUs)),
                new CanonicalJsonProperty("resolution", CanonicalJsonValue.String(resolution)),
                new CanonicalJsonProperty("sampleSeconds", CanonicalJsonValue.Number(SampleSeconds)),
                new CanonicalJsonProperty("scenario", CanonicalJsonValue.String(scenario)),
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(Schema)),
                new CanonicalJsonProperty("status", CanonicalJsonValue.String(passes ? "PASS" : "FAIL")),
                new CanonicalJsonProperty("warmupSeconds", CanonicalJsonValue.Number(WarmupSeconds)));
        }

        private static void ValidateCell(string browser, string resolution, string scenario)
        {
            if (browser != "Chrome" && browser != "Edge") throw new ArgumentOutOfRangeException(nameof(browser));
            if (resolution != "1280x720" && resolution != "1920x1080") throw new ArgumentOutOfRangeException(nameof(resolution));
            if (scenario != "baseline" && scenario != "stress") throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        private long GetElapsedMicroseconds()
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - stopwatchStartTicks;
            if (elapsedTicks < 0) throw new InvalidOperationException("Monotonic stopwatch moved backwards.");
            var wholeSeconds = elapsedTicks / Stopwatch.Frequency;
            var remainderTicks = elapsedTicks % Stopwatch.Frequency;
            return checked(wholeSeconds * MicrosecondsPerSecond + remainderTicks * MicrosecondsPerSecond / Stopwatch.Frequency);
        }
        private sealed class ApplicationStateRecord
        {
            public ApplicationStateRecord(long observedAtMicroseconds, bool foreground, bool unpaused)
            {
                ObservedAtMicroseconds = observedAtMicroseconds;
                Foreground = foreground;
                Unpaused = unpaused;
            }

            public long ObservedAtMicroseconds { get; }
            public bool Foreground { get; }
            public bool Unpaused { get; }
        }

        private struct SampleApplicationState
        {
            public SampleApplicationState(bool allForeground, bool noPause)
            {
                AllForeground = allForeground;
                NoPause = noPause;
            }

            public bool AllForeground { get; }
            public bool NoPause { get; }
        }
    }
}
