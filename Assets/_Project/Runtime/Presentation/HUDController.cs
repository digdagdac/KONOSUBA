using System;
using UnityEngine;
using UnityEngine.UI;

namespace Overbless.Runtime
{
    public readonly struct HudState
    {
        public HudState(
            int health,
            int maximumHealth,
            float dash01,
            bool dashAvailable,
            float dashCooldownRemaining,
            bool hasteAvailable,
            bool giantAvailable,
            int souls,
            bool exitOpen)
            : this(
                health,
                maximumHealth,
                dash01,
                dashAvailable,
                dashCooldownRemaining,
                hasteAvailable,
                giantAvailable,
                false,
                souls,
                exitOpen)
        {
        }

        public HudState(
            int health,
            int maximumHealth,
            float dash01,
            bool dashAvailable,
            float dashCooldownRemaining,
            bool hasteAvailable,
            bool giantAvailable,
            bool echoAvailable,
            int souls,
            bool exitOpen)
        {
            Health = health;
            MaximumHealth = maximumHealth;
            Dash01 = dash01;
            DashAvailable = dashAvailable;
            DashCooldownRemaining = dashCooldownRemaining;
            HasteAvailable = hasteAvailable;
            GiantAvailable = giantAvailable;
            EchoAvailable = echoAvailable;
            Souls = souls;
            ExitOpen = exitOpen;
        }

        public int Health { get; }
        public int MaximumHealth { get; }
        public float Dash01 { get; }
        public bool DashAvailable { get; }
        public float DashCooldownRemaining { get; }
        public bool HasteAvailable { get; }
        public bool GiantAvailable { get; }
        public bool EchoAvailable { get; }
        public int Souls { get; }
        public bool ExitOpen { get; }
    }

    [DisallowMultipleComponent]
    public sealed class HUDController : MonoBehaviour
    {
        private const int RequiredSouls = M1RoomDefinition.RequiredSoulCount;

        private static readonly Color AvailableHasteColor = new Color32(55, 211, 242, 255);
        private static readonly Color AvailableGiantColor = new Color32(255, 137, 72, 255);
        private static readonly Color AvailableEchoColor = new Color32(179, 121, 255, 255);
        private static readonly Color SelectedColor = new Color32(255, 238, 143, 255);
        private static readonly Color UnavailableColor = new Color32(66, 76, 92, 255);
        private static readonly Color HealthyColor = new Color32(70, 224, 205, 255);
        private static readonly Color DangerColor = new Color32(255, 92, 102, 255);

        // A refused blessing input used to produce no output at all. A short pulse
        // on the affected card distinguishes "input ignored" from "input refused".
        private const float FeedbackPulseSeconds = 0.28f;
        private static readonly Color AppliedPulseColor = new Color32(255, 255, 255, 255);
        private static readonly Color RejectedPulseColor = new Color32(255, 92, 102, 255);

        [Header("Runtime state")]
        [SerializeField] private Health playerHealth;
        [SerializeField] private DashAbility dashAbility;
        [SerializeField] private BlessingTargeting blessingTargeting;
        [SerializeField] private M1RoomLifecycle roomLifecycle;

        [Header("Bars")]
        [SerializeField] private Image healthFill;
        [SerializeField] private Image dashFill;

        [Header("Readouts")]
        [SerializeField] private Text healthText;
        [SerializeField] private Text dashText;
        [SerializeField] private Text soulText;
        [SerializeField] private Text exitText;
        [SerializeField] private Text selectionText;
        [SerializeField] private Text hasteStatusText;
        [SerializeField] private Text giantStatusText;
        [SerializeField] private Text echoStatusText;

        [Header("Blessing cards")]
        [SerializeField] private Image hasteFrame;
        [SerializeField] private Image giantFrame;
        [SerializeField] private Image echoFrame;

        private HudState state;
        private bool hasState;
        private bool renderedIsSelecting;
        private BlessingType renderedSelectedType;
        private bool subscribedToFeedback;
        private bool pulseActive;
        private bool pulseIsRejection;
        private BlessingType pulseType;
        private float pulseEndsAtUnscaled;
        private bool renderedPulseActive;
        private bool renderedPulseIsRejection;
        private BlessingType renderedPulseType;

        public event Action<HudState> StateChanged;

        public bool HasState => hasState;
        public bool IsBound =>
            playerHealth != null && dashAbility != null && blessingTargeting != null && roomLifecycle != null;
        public bool IsViewConfigured =>
            healthFill != null && healthFill.sprite != null && healthFill.type == Image.Type.Filled &&
            dashFill != null && dashFill.sprite != null && dashFill.type == Image.Type.Filled &&
            healthText != null && dashText != null && soulText != null && exitText != null &&
            selectionText != null && hasteStatusText != null && giantStatusText != null && echoStatusText != null &&
            hasteFrame != null && giantFrame != null && echoFrame != null;

        public HudState State
        {
            get
            {
                if (!hasState)
                {
                    throw new InvalidOperationException("HUD state has not been initialized.");
                }

                return state;
            }
        }

        private void OnEnable()
        {
            SubscribeToFeedback();
            RefreshFromSources();
        }

        private void OnDisable()
        {
            UnsubscribeFromFeedback();
        }

        private void Update()
        {
            RefreshFromSources();
        }

        private void SubscribeToFeedback()
        {
            if (subscribedToFeedback || blessingTargeting == null)
            {
                return;
            }

            blessingTargeting.BlessingApplied += HandleBlessingApplied;
            blessingTargeting.BlessingRejected += HandleBlessingRejected;
            subscribedToFeedback = true;
        }

        private void UnsubscribeFromFeedback()
        {
            if (!subscribedToFeedback || blessingTargeting == null)
            {
                return;
            }

            blessingTargeting.BlessingApplied -= HandleBlessingApplied;
            blessingTargeting.BlessingRejected -= HandleBlessingRejected;
            subscribedToFeedback = false;
        }

        private void HandleBlessingApplied(BlessingApplicationSignal signal)
        {
            BeginFeedbackPulse(signal.Type, false);
        }

        private void HandleBlessingRejected(BlessingRejectionSignal signal)
        {
            BeginFeedbackPulse(signal.Type, true);
        }

        private void BeginFeedbackPulse(BlessingType type, bool isRejection)
        {
            pulseActive = true;
            pulseIsRejection = isRejection;
            pulseType = type;
            // Selection runs at a reduced time scale, so the pulse must not be
            // stretched by it.
            pulseEndsAtUnscaled = Time.unscaledTime + FeedbackPulseSeconds;
        }

        public bool TryGetState(out HudState currentState)
        {
            currentState = state;
            return hasState;
        }

        public void SetState(HudState newState)
        {
            ValidateState(newState);
            var changed = !hasState || !StatesEqual(state, newState);
            var selectionChanged = RefreshDynamicViewInputs();
            state = newState;
            hasState = true;

            // Rendering builds interpolated strings, so it must only run when the
            // rendered result can actually differ. Update() calls SetState every
            // frame; rendering unconditionally produced per-frame string garbage.
            if (changed || selectionChanged)
            {
                RenderView(newState);
            }

            if (changed)
            {
                StateChanged?.Invoke(newState);
            }
        }

        /// <summary>
        /// Tracks the view inputs RenderView reads outside HudState: the blessing
        /// selection and the feedback pulse. Without this the render guard would
        /// miss transitions that never touch HudState.
        /// </summary>
        private bool RefreshDynamicViewInputs()
        {
            if (pulseActive && Time.unscaledTime >= pulseEndsAtUnscaled)
            {
                pulseActive = false;
            }

            var selecting = blessingTargeting != null && blessingTargeting.IsSelecting;
            var selected = selecting ? blessingTargeting.SelectedType : default;
            if (hasState &&
                selecting == renderedIsSelecting &&
                selected == renderedSelectedType &&
                pulseActive == renderedPulseActive &&
                pulseIsRejection == renderedPulseIsRejection &&
                pulseType == renderedPulseType)
            {
                return false;
            }

            renderedIsSelecting = selecting;
            renderedSelectedType = selected;
            renderedPulseActive = pulseActive;
            renderedPulseIsRejection = pulseIsRejection;
            renderedPulseType = pulseType;
            return true;
        }

        private void RefreshFromSources()
        {
            if (!IsBound)
            {
                return;
            }

            var cooldownDuration = dashAbility.CooldownDuration;
            var dashAvailable = dashAbility.CanDash;
            var dash01 = dashAvailable
                ? 1f
                : cooldownDuration <= 0f
                    ? 0f
                    : 1f - Mathf.Clamp01(dashAbility.CooldownRemaining / cooldownDuration);

            SetState(new HudState(
                playerHealth.CurrentHealth,
                playerHealth.MaximumHealth,
                dash01,
                dashAvailable,
                dashAbility.CooldownRemaining,
                blessingTargeting.IsAvailable(BlessingType.Haste),
                blessingTargeting.IsAvailable(BlessingType.Giant),
                blessingTargeting.IsAvailable(BlessingType.Echo),
                roomLifecycle.SoulCount,
                roomLifecycle.IsExitOpen));
        }

        private void RenderView(HudState value)
        {
            if (!IsViewConfigured)
            {
                return;
            }

            var health01 = (float)value.Health / value.MaximumHealth;
            healthFill.fillAmount = health01;
            healthFill.color = health01 <= 0.34f ? DangerColor : HealthyColor;
            healthText.text = $"LIFE  {value.Health} / {value.MaximumHealth}";

            dashFill.fillAmount = value.Dash01;
            dashText.text = value.DashAvailable
                ? "DASH  READY"
                : value.DashCooldownRemaining > 0f
                    ? $"DASH  {Mathf.CeilToInt(value.DashCooldownRemaining)}s"
                    : "DASH  UNAVAILABLE";

            soulText.text = $"SOULS  {Mathf.Min(value.Souls, RequiredSouls)} / {RequiredSouls}";
            exitText.text = value.ExitOpen ? "EXIT  OPEN" : $"EXIT  LOCKED  {Mathf.Min(value.Souls, RequiredSouls)}/{RequiredSouls}";

            var selectingHaste = blessingTargeting != null &&
                                 blessingTargeting.IsSelecting &&
                                 blessingTargeting.SelectedType == BlessingType.Haste;
            var selectingGiant = blessingTargeting != null &&
                                 blessingTargeting.IsSelecting &&
                                 blessingTargeting.SelectedType == BlessingType.Giant;
            var selectingEcho = blessingTargeting != null &&
                                blessingTargeting.IsSelecting &&
                                blessingTargeting.SelectedType == BlessingType.Echo;

            hasteFrame.color = !value.HasteAvailable
                ? UnavailableColor
                : selectingHaste ? SelectedColor : AvailableHasteColor;
            giantFrame.color = !value.GiantAvailable
                ? UnavailableColor
                : selectingGiant ? SelectedColor : AvailableGiantColor;
            echoFrame.color = !value.EchoAvailable
                ? UnavailableColor
                : selectingEcho ? SelectedColor : AvailableEchoColor;
            if (pulseActive)
            {
                var pulseColor = pulseIsRejection ? RejectedPulseColor : AppliedPulseColor;
                switch (pulseType)
                {
                    case BlessingType.Haste:
                        hasteFrame.color = pulseColor;
                        break;
                    case BlessingType.Giant:
                        giantFrame.color = pulseColor;
                        break;
                    case BlessingType.Echo:
                        echoFrame.color = pulseColor;
                        break;
                }
            }

            hasteStatusText.text = value.HasteAvailable ? selectingHaste ? "SELECTED" : "READY" : "BOUND";
            giantStatusText.text = value.GiantAvailable ? selectingGiant ? "SELECTED" : "READY" : "BOUND";
            echoStatusText.text = value.EchoAvailable ? selectingEcho ? "SELECTED" : "READY" : "BOUND";

            if (selectingHaste)
            {
                selectionText.text = "HASTE SELECTED  |  POINT AT AN ENEMY + CLICK  |  RMB CANCEL";
            }
            else if (selectingGiant)
            {
                selectionText.text = "GIANT SELECTED  |  POINT AT AN ENEMY + CLICK  |  RMB CANCEL";
            }
            else if (selectingEcho)
            {
                selectionText.text = "ECHO SELECTED  |  REPEAT LOCKED ATTACK  |  POINT AT AN ENEMY + CLICK  |  RMB CANCEL";
            }
            else
            {
                selectionText.text = "1 / 2 / 3 SELECT BLESSING  |  SPACE DASH  |  R RESTART";
            }
        }

        private static void ValidateState(HudState value)
        {
            if (value.MaximumHealth <= 0 ||
                value.Health < 0 ||
                value.Health > value.MaximumHealth ||
                value.Souls < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (float.IsNaN(value.Dash01) ||
                float.IsInfinity(value.Dash01) ||
                value.Dash01 < 0f ||
                value.Dash01 > 1f ||
                float.IsNaN(value.DashCooldownRemaining) ||
                float.IsInfinity(value.DashCooldownRemaining) ||
                value.DashCooldownRemaining < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static bool StatesEqual(HudState left, HudState right)
        {
            return left.Health == right.Health &&
                   left.MaximumHealth == right.MaximumHealth &&
                   Mathf.Approximately(left.Dash01, right.Dash01) &&
                   left.DashAvailable == right.DashAvailable &&
                   Mathf.Approximately(left.DashCooldownRemaining, right.DashCooldownRemaining) &&
                   left.HasteAvailable == right.HasteAvailable &&
                   left.GiantAvailable == right.GiantAvailable &&
                   left.EchoAvailable == right.EchoAvailable &&
                   left.Souls == right.Souls &&
                   left.ExitOpen == right.ExitOpen;
        }
    }
}
