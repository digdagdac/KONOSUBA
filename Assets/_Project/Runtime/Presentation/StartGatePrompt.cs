using System;
using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// Shows what the room is waiting for. A browser build starts with gameplay, timers and
    /// audio stopped until a trusted input arrives, and without this panel that state looks
    /// like a frozen game.
    /// </summary>
    /// <remarks>
    /// Presentation only. It reads the gate and never releases it, so the trusted-gesture
    /// contract stays owned by <see cref="WebStartGate"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StartGatePrompt : MonoBehaviour
    {
        [SerializeField] private WebStartGate startGate;
        [SerializeField] private GameObject promptRoot;

        private bool lastAwaiting;

        /// <summary>True while the prompt is on screen.</summary>
        public bool IsPromptVisible => promptRoot != null && promptRoot.activeSelf;

        private void Awake()
        {
            ValidateConfiguration();
        }

        private void OnEnable()
        {
            ValidateConfiguration();
            Refresh(true);
        }

        private void Update()
        {
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            var awaiting = startGate.IsAwaitingGesture;
            if (!force && awaiting == lastAwaiting)
            {
                return;
            }

            lastAwaiting = awaiting;
            promptRoot.SetActive(awaiting);
        }

        private void ValidateConfiguration()
        {
            if (startGate == null || promptRoot == null)
            {
                throw new InvalidOperationException("Start gate prompt requires the web start gate and its panel.");
            }
        }
    }
}
