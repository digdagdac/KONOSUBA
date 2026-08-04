using System;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum GameplayTimeScaleClaim
    {
        FocusGate,
        Pause,
        BlessingSelection
    }

    public static class GameplayTimeScaleCoordinator
    {
        private static bool focusGateClaimed;
        private static bool pauseClaimed;
        private static bool blessingSelectionClaimed;
        private static bool hasBaseline;
        private static float baselineTimeScale = 1f;

        public static void Acquire(GameplayTimeScaleClaim claim)
        {
            if (IsClaimed(claim))
            {
                return;
            }

            if (!hasBaseline)
            {
                baselineTimeScale = Time.timeScale;
                hasBaseline = true;
            }

            SetClaimed(claim, true);
            ApplyEffectiveTimeScale();
        }

        public static void Release(GameplayTimeScaleClaim claim)
        {
            if (!IsClaimed(claim))
            {
                return;
            }

            SetClaimed(claim, false);
            if (focusGateClaimed || pauseClaimed || blessingSelectionClaimed)
            {
                ApplyEffectiveTimeScale();
                return;
            }

            Time.timeScale = baselineTimeScale;
            baselineTimeScale = 1f;
            hasBaseline = false;
        }

        private static bool IsClaimed(GameplayTimeScaleClaim claim)
        {
            switch (claim)
            {
                case GameplayTimeScaleClaim.FocusGate:
                    return focusGateClaimed;
                case GameplayTimeScaleClaim.Pause:
                    return pauseClaimed;
                case GameplayTimeScaleClaim.BlessingSelection:
                    return blessingSelectionClaimed;
                default:
                    throw new ArgumentOutOfRangeException(nameof(claim), claim, null);
            }
        }

        private static void SetClaimed(GameplayTimeScaleClaim claim, bool value)
        {
            switch (claim)
            {
                case GameplayTimeScaleClaim.FocusGate:
                    focusGateClaimed = value;
                    break;
                case GameplayTimeScaleClaim.Pause:
                    pauseClaimed = value;
                    break;
                case GameplayTimeScaleClaim.BlessingSelection:
                    blessingSelectionClaimed = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(claim), claim, null);
            }
        }

        private static void ApplyEffectiveTimeScale()
        {
            if (focusGateClaimed || pauseClaimed)
            {
                Time.timeScale = 0f;
                return;
            }

            Time.timeScale = blessingSelectionClaimed
                ? BlessingTargeting.SelectionTimeScale
                : baselineTimeScale;
        }
    }

    [DisallowMultipleComponent]
    public sealed class PauseController : MonoBehaviour
    {
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private BlessingTargeting blessingTargeting;
        [SerializeField] private RoomRestartController restartController;

        public event Action<bool> PauseChanged;

        public bool IsPaused { get; private set; }

        private void Awake()
        {
            ValidateConfiguration();
        }

        private void OnEnable()
        {
            inputRouter.PauseRequested += HandlePauseRequested;
            restartController.Restarted += HandleRoomRestarted;
        }

        private void OnDisable()
        {
            inputRouter.PauseRequested -= HandlePauseRequested;
            restartController.Restarted -= HandleRoomRestarted;
            if (IsPaused)
            {
                SetPaused(false);
            }
        }

        public void SetPaused(bool value)
        {
            if (IsPaused == value)
            {
                return;
            }

            if (value)
            {
                inputRouter.AcquireInputBlock(PlayerInputBlocker.Pause);
                GameplayTimeScaleCoordinator.Acquire(GameplayTimeScaleClaim.Pause);
                try
                {
                    blessingTargeting.CancelSelection();
                    IsPaused = true;
                }
                catch
                {
                    GameplayTimeScaleCoordinator.Release(GameplayTimeScaleClaim.Pause);
                    inputRouter.ReleaseInputBlock(PlayerInputBlocker.Pause);
                    throw;
                }
            }
            else
            {
                GameplayTimeScaleCoordinator.Release(GameplayTimeScaleClaim.Pause);
                inputRouter.ReleaseInputBlock(PlayerInputBlocker.Pause);
                IsPaused = false;
            }

            PauseChanged?.Invoke(IsPaused);
        }

        private void HandlePauseRequested()
        {
            if (blessingTargeting.IsSelecting)
            {
                blessingTargeting.CancelSelection();
                return;
            }

            SetPaused(!IsPaused);
        }

        private void HandleRoomRestarted()
        {
            if (IsPaused)
            {
                SetPaused(false);
            }
        }

        private void ValidateConfiguration()
        {
            if (inputRouter == null || blessingTargeting == null || restartController == null)
            {
                throw new InvalidOperationException("PauseController requires input, blessing targeting, and room restart references.");
            }
        }
    }
}
