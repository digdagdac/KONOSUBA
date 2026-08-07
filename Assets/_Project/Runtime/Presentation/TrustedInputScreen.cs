using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Overbless.Runtime
{
    /// <summary>
    /// A full-screen flow screen that advances to one named scene on a trusted input.
    /// The title screen and the run result screen are both this component with different
    /// authored content.
    /// </summary>
    /// <remarks>
    /// The press must start on this screen. A button that was already held when the screen
    /// appeared is ignored until it is released, so a click that ended the previous scene
    /// cannot skip through this one. Timing uses unscaled time because a flow screen runs
    /// with gameplay stopped.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrustedInputScreen : MonoBehaviour
    {
        private const float PromptBlinkPeriodSeconds = 1.1f;

        [SerializeField] private string nextScene;
        [SerializeField] private Text promptText;
        [SerializeField] private float minimumVisibleSeconds = 0.35f;

        private float readyAtUnscaledTime;
        private bool releaseObserved;
        private bool advanced;

        /// <summary>The scene this screen loads.</summary>
        public string NextScene => nextScene;

        /// <summary>True once this screen has requested its scene load.</summary>
        public bool HasAdvanced => advanced;

        /// <summary>True while the screen still waits for a press that started here.</summary>
        public bool IsWaitingForRelease => !releaseObserved;

        private void OnEnable()
        {
            ValidateConfiguration();
            readyAtUnscaledTime = Time.unscaledTime + minimumVisibleSeconds;
            releaseObserved = !AnyTrustedControlPressed();
            advanced = false;
        }

        private void Update()
        {
            if (promptText != null)
            {
                var phase = Mathf.Repeat(Time.unscaledTime, PromptBlinkPeriodSeconds) / PromptBlinkPeriodSeconds;
                var color = promptText.color;
                color.a = Mathf.Lerp(0.35f, 1f, Mathf.Abs(Mathf.Sin(phase * Mathf.PI)));
                promptText.color = color;
            }

            if (advanced)
            {
                return;
            }

            if (!releaseObserved)
            {
                releaseObserved = !AnyTrustedControlPressed();
                return;
            }

            if (Time.unscaledTime < readyAtUnscaledTime || !AnyTrustedControlPressed())
            {
                return;
            }

            Advance();
        }

        /// <summary>
        /// Loads the next scene once. Exposed so a test can drive the transition without
        /// synthesising device input.
        /// </summary>
        public void Advance()
        {
            if (advanced)
            {
                return;
            }

            advanced = true;
            SceneManager.LoadScene(nextScene, LoadSceneMode.Single);
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(nextScene))
            {
                throw new InvalidOperationException("A flow screen requires the name of the scene it advances to.");
            }

            if (minimumVisibleSeconds < 0f)
            {
                throw new InvalidOperationException("A flow screen requires a non-negative minimum visible duration.");
            }
        }

        private static bool AnyTrustedControlPressed()
        {
            // A quick click can begin and end inside one frame, so a held-state poll alone
            // would drop it. The frame-edge checks catch that case.
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.anyKey.isPressed || keyboard.anyKey.wasPressedThisFrame))
            {
                return true;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }

            return mouse.leftButton.isPressed ||
                   mouse.leftButton.wasPressedThisFrame ||
                   mouse.leftButton.wasReleasedThisFrame ||
                   mouse.rightButton.isPressed ||
                   mouse.rightButton.wasPressedThisFrame;
        }
    }
}
