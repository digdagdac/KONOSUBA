using System;
using UnityEngine;

namespace Overbless.Editor.Evidence
{
    /// <summary>
    /// Final M2 binding check. This type deliberately performs no writes: it only validates sealed bytes and a user-authored decision.
    /// </summary>
    public static class GateBindingChecker
    {
        public const bool IsReadOnly = true;

        /// <summary>Batch-mode entry point. Requires -candidateId and fails the process unless the existing user decision is PASS.</summary>
        public static void Check()
        {
            string candidateId;
            if (!TryGetCommandLineArgument("-candidateId", out candidateId)) throw new InvalidOperationException("-candidateId is required.");
            var result = CheckCandidate(candidateId);
            if (!result.IsM2Approved) throw new InvalidOperationException("M2 entry remains REWORK: " + string.Join(" | ", result.Errors));
            Debug.Log("Read-only M2 gate binding check PASS for " + candidateId + ".");
        }

        public static M2GateValidationResult CheckCandidate(string candidateId)
        {
            return M2EntryGateValidator.ValidateCandidate(candidateId, true);
        }

        public static M2GateValidationResult CheckCandidateRoot(string candidateRoot, string candidateId)
        {
            return M2EntryGateValidator.ValidateCandidateRoot(candidateRoot, candidateId, true);
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
    }
}
