using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WebStartGate : MonoBehaviour
    {
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private FunctionalAudioEmitter audioEmitter;

        private bool requiresGesture;
        private bool releaseObserved;
        private bool ownsGateClaim;
        private bool audioPausedBeforeGate;

        public event Action Started;
        public event Action FocusRecovered;

        public bool IsStarted { get; private set; }
        public bool IsAwaitingGesture => requiresGesture;
        public float StartedAtUnscaledTime { get; private set; }

        private void Awake()
        {
            ValidateConfiguration();
            EnterGestureGate();
        }

        private void Update()
        {
            if (!requiresGesture)
            {
                return;
            }
            EnforceGate();

            if (!releaseObserved)
            {
                releaseObserved = !AnyRelevantControlPressed();
                return;
            }

            if (!HasTrustedGesture())
            {
                return;
            }

            AcceptTrustedGesture();
        }

        private void AcceptTrustedGesture()
        {
            if (!requiresGesture)
            {
                return;
            }

            if (!IsStarted)
            {
                audioEmitter.SetWebStarted();
            }

            ReleaseGestureGate();

            if (!IsStarted)
            {
                IsStarted = true;
                StartedAtUnscaledTime = Time.unscaledTime;
                Started?.Invoke();
            }
            else
            {
                FocusRecovered?.Invoke();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                EnterGestureGate();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                EnterGestureGate();
            }
        }

        private void OnDisable()
        {
            ReleaseGestureGate();
        }

        private void EnterGestureGate()
        {
            ValidateConfiguration();
            requiresGesture = true;
            releaseObserved = !AnyRelevantControlPressed();

            if (!ownsGateClaim)
            {
                inputRouter.AcquireInputBlock(PlayerInputBlocker.FocusGate);
                audioPausedBeforeGate = AudioListener.pause;
                GameplayTimeScaleCoordinator.Acquire(GameplayTimeScaleClaim.FocusGate);
                ownsGateClaim = true;
            }

            EnforceGate();
        }

        private void EnforceGate()
        {
            inputRouter.ResetInputState();
            inputRouter.AcquireInputBlock(PlayerInputBlocker.FocusGate);
            AudioListener.pause = true;
        }

        private void ReleaseGestureGate()
        {
            if (!ownsGateClaim)
            {
                return;
            }

            inputRouter.ResetInputState();
            inputRouter.ReleaseInputBlock(PlayerInputBlocker.FocusGate);
            AudioListener.pause = audioPausedBeforeGate;
            GameplayTimeScaleCoordinator.Release(GameplayTimeScaleClaim.FocusGate);
            ownsGateClaim = false;
            requiresGesture = false;
        }

        private void ValidateConfiguration()
        {
            if (inputRouter == null)
            {
                throw new InvalidOperationException("WebStartGate requires a PlayerInputRouter.");
            }

            if (audioEmitter == null)
            {
                throw new InvalidOperationException("WebStartGate requires a FunctionalAudioEmitter.");
            }
        }

        private static bool HasTrustedGesture()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.isPressed) ||
                   (Mouse.current != null &&
                    (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed));
        }

        private static bool AnyRelevantControlPressed()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.isPressed) ||
                   (Mouse.current != null &&
                    (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed));
        }
    }
}
