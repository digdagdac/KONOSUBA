using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Overbless.Editor.Build;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Evidence
{
    /// <summary>
    /// Hosts the only interactive path that can persist a final human gate decision.
    /// The persistence method and its human-confirmation capability are private so general Editor automation
    /// cannot supply an identity label or attestation to create a PASS decision.
    /// </summary>
    internal static class GateDecisionWriter
    {
        private const string GateDecisionFileName = "gate-decision.json";
        private const string EvidenceManifestFileName = "evidence-manifest.json";
        private const string ValidatorReportFileName = "validator-report.json";
        private const string UtcTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

        /// <summary>
        /// Opens the local interactive approval surface. This does not write a decision; a user must select an
        /// explicit decision, provide an attestation, and confirm the write-once action in the window.
        /// </summary>
        [MenuItem("Overbless/M2 Entry Gate/Record Human Gate Decision")]
        public static void OpenHumanGateDecisionWindow()
        {
            if (Application.isBatchMode)
            {
                throw new InvalidOperationException("Human gate decisions cannot be recorded in batch mode.");
            }

            HumanGateDecisionWindow.Open();
        }

        private static bool RecordDecisionFromConfirmedWindow(
            string candidateId,
            string decision,
            string decidedUtc,
            string userAttestation,
            string trustAnchor,
            string signatureBase64)
        {
            if (Application.isBatchMode)
            {
                throw new InvalidOperationException("Human gate decisions cannot be recorded in batch mode.");
            }

            var snapshot = GateDecisionSnapshot.Capture(
                candidateId,
                decision,
                decidedUtc,
                userAttestation,
                trustAnchor,
                signatureBase64);
            ValidateSnapshot(snapshot);

            if (!EditorUtility.DisplayDialog(
                    "Confirm Human Gate Decision",
                    "Record " + snapshot.Decision + " for candidate '" + snapshot.CandidateId + "'? This write-once decision cannot be changed or auto-created.",
                    "Record " + snapshot.Decision,
                    "Cancel"))
            {
                return false;
            }

            CommitDecision(snapshot, new HumanDecisionCapability());
            return true;
        }

        private static string CommitDecision(GateDecisionSnapshot snapshot, HumanDecisionCapability capability)
        {
            if (capability == null) throw new ArgumentNullException(nameof(capability));

            var terminal = CandidateCoordinator.GetValidatedTerminalMachineEvent(snapshot.CandidateId);
            if (terminal.EventName == "MACHINE_REWORK" && snapshot.Decision != "REWORK")
            {
                throw new InvalidOperationException("A MACHINE_REWORK terminal event permits only an explicit REWORK decision.");
            }
            if (terminal.EventName != "MACHINE_READY" && terminal.EventName != "MACHINE_REWORK")
            {
                throw new InvalidOperationException("Candidate terminal machine event is invalid.");
            }

            var candidateDirectory = CandidateCoordinator.GetCandidateDirectory(snapshot.CandidateId);
            RequireCurrentArtifactHash(candidateDirectory, EvidenceManifestFileName, terminal.EvidenceManifestSha256);
            RequireCurrentArtifactHash(candidateDirectory, ValidatorReportFileName, terminal.ValidatorReportSha256);
            RequireValidDetachedSignature(snapshot, terminal);

            var decisionPath = Path.Combine(candidateDirectory, GateDecisionFileName);
            if (File.Exists(decisionPath) || Directory.Exists(decisionPath))
            {
                throw new InvalidOperationException("Write-once gate decision already exists: " + decisionPath);
            }

            var decision = CanonicalJsonValue.Object(
                new CanonicalJsonProperty("schema", CanonicalJsonValue.String(EvidenceContracts.GateDecision)),
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(terminal.CandidateId)),
                new CanonicalJsonProperty("evidenceManifestSha256", CanonicalJsonValue.String(terminal.EvidenceManifestSha256)),
                new CanonicalJsonProperty("validatorReportSha256", CanonicalJsonValue.String(terminal.ValidatorReportSha256)),
                new CanonicalJsonProperty("decision", CanonicalJsonValue.String(snapshot.Decision)),
                new CanonicalJsonProperty("decidedBy", CanonicalJsonValue.String(snapshot.DecidedBy)),
                new CanonicalJsonProperty("decidedUtc", CanonicalJsonValue.String(snapshot.DecidedUtc)),
                new CanonicalJsonProperty("signatureAlgorithm", CanonicalJsonValue.String(snapshot.SignatureAlgorithm)),
                new CanonicalJsonProperty("signatureBase64", CanonicalJsonValue.String(snapshot.SignatureBase64)),
                new CanonicalJsonProperty("trustAnchor", CanonicalJsonValue.String(snapshot.TrustAnchor)),
                new CanonicalJsonProperty("userAttestation", CanonicalJsonValue.String(snapshot.UserAttestation)));
            var bytes = CanonicalJson.SerializeUtf8(decision);
            WriteDecisionAtomically(candidateDirectory, decisionPath, bytes);

            return decisionPath;
        }

        private static void ValidateSnapshot(GateDecisionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(snapshot.CandidateId))
            {
                throw new ArgumentException("Candidate ID is required.", nameof(snapshot));
            }
            if (snapshot.Decision != "PASS" && snapshot.Decision != "REWORK")
            {
                throw new ArgumentException("Decision must be explicitly PASS or REWORK.", nameof(snapshot));
            }
            if (string.IsNullOrWhiteSpace(snapshot.UserAttestation))
            {
                throw new ArgumentException("A non-empty user attestation is required.", nameof(snapshot));
            }
            if (string.IsNullOrWhiteSpace(snapshot.TrustAnchor))
            {
                throw new ArgumentException("A configured trust-anchor identifier is required.", nameof(snapshot));
            }
            if (!string.Equals(snapshot.SignatureAlgorithm, "RSA-SHA256", StringComparison.Ordinal))
            {
                throw new ArgumentException("Only RSA-SHA256 detached signatures are accepted.", nameof(snapshot));
            }

            try
            {
                var signature = Convert.FromBase64String(snapshot.SignatureBase64);
                if (signature.Length == 0 ||
                    !string.Equals(Convert.ToBase64String(signature), snapshot.SignatureBase64, StringComparison.Ordinal))
                {
                    throw new ArgumentException("The detached signature must be canonical non-empty Base64.", nameof(snapshot));
                }
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("The detached signature must be canonical non-empty Base64.", nameof(snapshot), exception);
            }


            DateTime parsed;
            if (!DateTime.TryParseExact(snapshot.DecidedUtc, UtcTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed) ||
                !string.Equals(snapshot.DecidedUtc, parsed.ToUniversalTime().ToString(UtcTimestampFormat, CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw new ArgumentException("decidedUtc must be an explicit UTC timestamp in yyyy-MM-ddTHH:mm:ss.fffZ form.", nameof(snapshot));
            }
        }

        private static void WriteDecisionAtomically(string candidateDirectory, string decisionPath, byte[] bytes)
        {
            var temporaryPath = Path.Combine(
                candidateDirectory,
                GateDecisionFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                // Same-directory File.Move is a no-overwrite promotion: an existing final path fails rather than being replaced.
                File.Move(temporaryPath, decisionPath);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }

        private static void RequireCurrentArtifactHash(string candidateDirectory, string relativePath, string expectedSha256)
        {
            var path = Path.Combine(candidateDirectory, relativePath);
            if (!File.Exists(path)) throw new InvalidOperationException("Required terminal artifact is missing: " + relativePath);
            var bytes = File.ReadAllBytes(path);
            CanonicalJsonValue ignored;
            string error;
            if (!CanonicalJson.TryParseCanonicalUtf8(bytes, out ignored, out error))
            {
                throw new InvalidOperationException("Required terminal artifact is not canonical JSON: " + relativePath + " (" + error + ").");
            }
            var actualSha256 = CanonicalJson.Sha256Hex(bytes);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terminal artifact hash does not match its machine event: " + relativePath);
            }
        }
        private static string PrepareSigningPayload(string candidateId, string decision, string decidedUtc)
        {
            if (decision != "PASS" && decision != "REWORK")
            {
                throw new ArgumentException("Decision must be PASS or REWORK.", nameof(decision));
            }

            DateTime parsed;
            if (!DateTime.TryParseExact(
                    decidedUtc,
                    UtcTimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                throw new ArgumentException("Prepare an explicit UTC timestamp before signing.", nameof(decidedUtc));
            }

            var terminal = CandidateCoordinator.GetValidatedTerminalMachineEvent(candidateId);
            if (terminal.EventName == "MACHINE_REWORK" && decision != "REWORK")
            {
                throw new InvalidOperationException("A MACHINE_REWORK terminal event permits only REWORK.");
            }
            if (terminal.EventName != "MACHINE_READY" && terminal.EventName != "MACHINE_REWORK")
            {
                throw new InvalidOperationException("Candidate terminal machine event is invalid.");
            }

            return Encoding.UTF8.GetString(CreateSigningPayload(
                terminal.CandidateId,
                terminal.EvidenceManifestSha256,
                terminal.ValidatorReportSha256,
                decision,
                decidedUtc));
        }

        private static byte[] CreateSigningPayload(
            string candidateId,
            string evidenceManifestSha256,
            string validatorReportSha256,
            string decision,
            string decidedUtc)
        {
            return CanonicalJson.SerializeUtf8(CanonicalJsonValue.Object(
                new CanonicalJsonProperty("candidateId", CanonicalJsonValue.String(candidateId)),
                new CanonicalJsonProperty("decidedUtc", CanonicalJsonValue.String(decidedUtc)),
                new CanonicalJsonProperty("decision", CanonicalJsonValue.String(decision)),
                new CanonicalJsonProperty("evidenceManifestSha256", CanonicalJsonValue.String(evidenceManifestSha256)),
                new CanonicalJsonProperty("validatorReportSha256", CanonicalJsonValue.String(validatorReportSha256))));
        }

        private static void RequireValidDetachedSignature(
            GateDecisionSnapshot snapshot,
            CandidateCoordinator.TerminalMachineEvent terminal)
        {
            const string anchorVariable = "OVERBLESS_M2_GATE_TRUST_ANCHOR";
            const string keyVariable = "OVERBLESS_M2_GATE_TRUSTED_PUBLIC_KEY_SPKI_BASE64";
            var configuredAnchor = Environment.GetEnvironmentVariable(anchorVariable);
            var configuredKey = Environment.GetEnvironmentVariable(keyVariable);
            if (string.IsNullOrWhiteSpace(configuredAnchor) ||
                string.IsNullOrWhiteSpace(configuredKey) ||
                !string.Equals(snapshot.TrustAnchor, configuredAnchor, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Configure the external trust anchor and RSA public key before recording a gate decision.");
            }

            try
            {
                var publicKey = Convert.FromBase64String(configuredKey);
                var signature = Convert.FromBase64String(snapshot.SignatureBase64);
                var payload = CreateSigningPayload(
                    terminal.CandidateId,
                    terminal.EvidenceManifestSha256,
                    terminal.ValidatorReportSha256,
                    snapshot.Decision,
                    snapshot.DecidedUtc);
                using (var rsa = RSA.Create())
                {
                    int bytesRead;
                    rsa.ImportSubjectPublicKeyInfo(publicKey, out bytesRead);
                    if (bytesRead != publicKey.Length ||
                        !rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        throw new InvalidOperationException("Detached gate-decision signature is invalid.");
                    }
                }
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("Trusted public key or detached signature is not Base64.", exception);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException("Trusted public key or detached signature is invalid.", exception);
            }
        }

        private sealed class GateDecisionSnapshot
        {
            private GateDecisionSnapshot(
                string candidateId,
                string decision,
                string decidedUtc,
                string userAttestation,
                string trustAnchor,
                string signatureBase64)
            {
                CandidateId = candidateId;
                Decision = decision;
                DecidedBy = "user";
                DecidedUtc = decidedUtc;
                UserAttestation = userAttestation;
                TrustAnchor = trustAnchor;
                SignatureAlgorithm = "RSA-SHA256";
                SignatureBase64 = signatureBase64;
            }

            public string CandidateId { get; }
            public string Decision { get; }
            public string DecidedBy { get; }
            public string DecidedUtc { get; }
            public string UserAttestation { get; }
            public string TrustAnchor { get; }
            public string SignatureAlgorithm { get; }
            public string SignatureBase64 { get; }

            public static GateDecisionSnapshot Capture(
                string candidateId,
                string decision,
                string decidedUtc,
                string userAttestation,
                string trustAnchor,
                string signatureBase64)
            {
                return new GateDecisionSnapshot(
                    candidateId,
                    decision,
                    decidedUtc,
                    userAttestation,
                    trustAnchor,
                    signatureBase64);
            }
        }

        /// <summary>
        /// Private construction makes confirmation from the interactive window an explicit capability boundary.
        /// It is intentionally not an authentication mechanism for code with the user's Editor process privileges.
        /// </summary>
        private sealed class HumanDecisionCapability
        {
            public HumanDecisionCapability()
            {
            }
        }

        private sealed class HumanGateDecisionWindow : EditorWindow
        {
            private string candidateId = string.Empty;
            private string userAttestation = string.Empty;
            private string decidedUtc = string.Empty;
            private string trustAnchor = string.Empty;
            private string signatureBase64 = string.Empty;
            private string preparedDecision = string.Empty;
            private string signingPayload = string.Empty;

            public static void Open()
            {
                var window = GetWindow<HumanGateDecisionWindow>(true, "Record Human Gate Decision");
                window.minSize = new Vector2(680f, 620f);
                window.Show();
            }

            private void OnGUI()
            {
                EditorGUILayout.HelpBox(
                    "Prepare the canonical payload, sign its exact UTF-8 bytes outside Unity with the trusted RSA private key, then paste the detached signature. Unity stores only the authenticated write-once decision; PASS is never generated automatically.",
                    MessageType.Info);
                candidateId = EditorGUILayout.TextField("Candidate ID", candidateId);
                decidedUtc = EditorGUILayout.TextField("Decided UTC", decidedUtc);

                using (new EditorGUI.DisabledScope(Application.isBatchMode))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Prepare PASS Signing Payload"))
                    {
                        Prepare("PASS");
                    }
                    if (GUILayout.Button("Prepare REWORK Signing Payload"))
                    {
                        Prepare("REWORK");
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.LabelField("Canonical UTF-8 Signing Payload (" + preparedDecision + ")");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextArea(signingPayload, GUILayout.MinHeight(90f));
                }

                trustAnchor = EditorGUILayout.TextField("Trust Anchor", trustAnchor);
                EditorGUILayout.LabelField("Detached RSA-SHA256 Signature (Base64)");
                signatureBase64 = EditorGUILayout.TextArea(signatureBase64, GUILayout.MinHeight(80f));
                EditorGUILayout.LabelField("User Attestation");
                userAttestation = EditorGUILayout.TextArea(userAttestation, GUILayout.MinHeight(80f));

                EditorGUILayout.Space();
                using (new EditorGUI.DisabledScope(Application.isBatchMode))
                {
                    if (GUILayout.Button("Record PASS"))
                    {
                        Record("PASS");
                    }

                    if (GUILayout.Button("Record REWORK"))
                    {
                        Record("REWORK");
                    }
                }
            }

            private void Prepare(string decision)
            {
                try
                {
                    decidedUtc = DateTime.UtcNow.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);
                    signingPayload = PrepareSigningPayload(candidateId, decision, decidedUtc);
                    preparedDecision = decision;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog("Signing Payload Was Not Prepared", exception.Message, "Close");
                }
            }

            private void Record(string decision)
            {
                try
                {
                    var currentPayload = PrepareSigningPayload(candidateId, decision, decidedUtc);
                    if (!string.Equals(preparedDecision, decision, StringComparison.Ordinal) ||
                        !string.Equals(signingPayload, currentPayload, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Prepare and externally sign the current candidate, decision, and UTC payload before recording.");
                    }

                    if (RecordDecisionFromConfirmedWindow(
                            candidateId,
                            decision,
                            decidedUtc,
                            userAttestation,
                            trustAnchor,
                            signatureBase64))
                    {
                        Close();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorUtility.DisplayDialog("Gate Decision Was Not Recorded", exception.Message, "Close");
                }
            }
        }
    }
}
