using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Overbless.Editor.Evidence
{
    public static class EvidenceContracts
    {
        public const string Candidate = "overbless.candidate/v1";
        public const string SourceManifest = "overbless.source-manifest/v1";
        public const string BuildManifest = "overbless.build/v1";
        public const string EvidenceManifest = "overbless.evidence/v1";
        public const string ValidatorReport = "overbless.validator-report/v1";
        public const string GateDecision = "overbless.gate-decision/v1";
        public const string AudioRandomization = "overbless.audio-randomization/v1";

        private static readonly string[] criterionIds =
        {
            "AUD-BLIND-001","AUD-ONCE-002","BLS-EFFECT-001","BLS-SEAL-002","CMB-ATTACK-001","EXT-M2-001",
            "FND-DISPLAY-002","FND-RULES-003","FND-UNITY-001","FUN-GUIDED-001","FUN-UNDERSTAND-002","PLY-LIFE-001",
            "ROOM-SOUL-001","VIS-HIT-002","VIS-IDENTIFY-001","WEB-INPUT-001","WEB-PERF-002","WEB-START-003"
        };
        private static readonly string[] checks = { "SCHEMA","IDENTITY","TRANSITION","INVENTORY","HASHES","COVERAGE","SOURCE_GATE","BUILD_GATE","TESTERS","BROWSER_MATRIX","PERFORMANCE","AUDIO" };
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> detailPrecedence = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["SCHEMA"] = Array.AsReadOnly(new[]{"UNKNOWN_SCHEMA","MISSING_KEY","ADDITIONAL_KEY","TYPE","ENUM","UNSORTED","DUPLICATE"}),
                ["IDENTITY"] = Array.AsReadOnly(new[]{"CANDIDATE_HASH","SOURCE_HASH","BUILD_HASH","ID_MISMATCH"}),
                ["TRANSITION"] = Array.AsReadOnly(new[]{"PARTIAL","CHAIN","SEQUENCE","ILLEGAL_EVENT","REF_MISMATCH"}),
                ["INVENTORY"] = Array.AsReadOnly(new[]{"MISSING_PATH","UNKNOWN_PATH","COUNT","ROLE","RAW_LINK","CRITERION_MISMATCH"}),
                ["HASHES"] = Array.AsReadOnly(new[]{"FILE_HASH","SELF_HASH","TREE_HASH","FILESET_HASH"}),
                ["COVERAGE"] = Array.AsReadOnly(new[]{"UNKNOWN_CRITERION","UNCOVERED","DUPLICATE_PAIR","ENVELOPE_MISMATCH"}),
                ["SOURCE_GATE"] = Array.AsReadOnly(new[]{"SOURCE_RESULT_COUNT","SOURCE_RESULT_FAIL","PAYLOAD_MISMATCH"}),
                ["BUILD_GATE"] = Array.AsReadOnly(new[]{"BUILD_RESULT_COUNT","BUILD_RESULT_FAIL","PAYLOAD_MISMATCH"}),
                ["TESTERS"] = Array.AsReadOnly(new[]{"COUNT","DUPLICATE_ID","PRIOR_EXPOSURE","COACHING"}),
                ["BROWSER_MATRIX"] = Array.AsReadOnly(new[]{"MISSING_CELL","DUPLICATE_CELL","FOCUS","START","INPUT"}),
                ["PERFORMANCE"] = Array.AsReadOnly(new[]{"MISSING_CELL","BUCKET_COUNT","FRAME_COUNT","FOREGROUND","FPS"}),
                ["AUDIO"] = Array.AsReadOnly(new[]{"EVENT_MISSING","EVENT_DUPLICATE","BLIND_FAIL"})
            });

        // These return snapshots so no caller can mutate the process-wide contract.
        // The array type is retained for existing Unity editor callers that use Array.IndexOf.
        public static string[] CriterionIds => (string[])criterionIds.Clone();
        public static string[] Checks => (string[])checks.Clone();
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> DetailPrecedence => detailPrecedence;

        public static string SelectDetail(string checkId, ISet<string> failures)
        {
            if (failures == null) throw new ArgumentNullException(nameof(failures));
            IReadOnlyList<string> order;
            if (!detailPrecedence.TryGetValue(checkId, out order)) throw new ArgumentOutOfRangeException(nameof(checkId));
            foreach (var code in order) if (failures.Contains(code)) return code;
            return "OK";
        }
    }
}
