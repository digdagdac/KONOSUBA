using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overbless.Runtime
{
    public enum PlayerInputBlocker
    {
        LifeCycle,
        FocusGate,
        Pause,
        RoomRestart
    }
    [DisallowMultipleComponent]
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        private InputActionMap inputActions;
        private InputAction moveAction;
        private InputAction mousePositionAction;
        private InputAction dashAction;
        private InputAction firstBlessingAction;
        private InputAction secondBlessingAction;
        private InputAction thirdBlessingAction;
        private InputAction applyAction;
        private InputAction cancelAction;
        private InputAction pauseAction;
        private InputAction restartAction;
        private Vector2 movement;
        private Vector2 mousePosition;
        private readonly HashSet<PlayerInputBlocker> inputBlockers = new HashSet<PlayerInputBlocker>();
        private bool hasApplicationFocus = true;
        private bool restartInputEnabled;

        public event Action<Vector2> MovementChanged;
        public event Action<Vector2> MousePositionChanged;
        public event Action DashRequested;
        public event Action<int> BlessingSelectionRequested;
        public event Action ApplyRequested;
        public event Action CancelRequested;
        public event Action PauseRequested;
        public event Action RestartRequested;

        public Vector2 Movement => movement;
        public Vector2 MousePosition => mousePosition;
        public bool IsInputEnabled => inputBlockers.Count == 0;

        private void Awake()
        {
            CreateInputActions();
        }

        private void OnEnable()
        {
            inputActions.Enable();
        }

        private void OnDisable()
        {
            inputActions.Disable();
            ClearHeldMovement();
        }

        private void OnDestroy()
        {
            inputActions.Dispose();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            hasApplicationFocus = hasFocus;

            if (!hasFocus)
            {
                ClearHeldMovement();
            }
        }

        public void AcquireInputBlock(PlayerInputBlocker blocker)
        {
            ValidateBlocker(blocker);
            if (inputBlockers.Add(blocker))
            {
                ClearHeldMovement();
            }
        }

        public void ReleaseInputBlock(PlayerInputBlocker blocker)
        {
            ValidateBlocker(blocker);
            inputBlockers.Remove(blocker);
        }
        public void SetRestartInputEnabled(bool value)
        {
            restartInputEnabled = value;
        }


        public void ResetInputState()
        {
            ClearHeldMovement();

            if (mousePositionAction != null && mousePositionAction.enabled)
            {
                SetMousePosition(mousePositionAction.ReadValue<Vector2>());
            }
        }

        public void ClearHeldMovement()
        {
            SetMovement(Vector2.zero);
        }

        private void CreateInputActions()
        {
            inputActions = new InputActionMap("Player");

            moveAction = inputActions.AddAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            moveAction.performed += HandleMovement;
            moveAction.canceled += HandleMovement;

            mousePositionAction = inputActions.AddAction("MousePosition", InputActionType.Value, "<Mouse>/position");
            mousePositionAction.performed += HandleMousePosition;
            mousePositionAction.canceled += HandleMousePosition;

            dashAction = inputActions.AddAction("Dash", InputActionType.Button, "<Keyboard>/space");
            dashAction.performed += HandleDash;

            firstBlessingAction = inputActions.AddAction("FirstBlessing", InputActionType.Button, "<Keyboard>/1");
            firstBlessingAction.AddBinding("<Keyboard>/numpad1");
            firstBlessingAction.performed += HandleFirstBlessing;

            secondBlessingAction = inputActions.AddAction("SecondBlessing", InputActionType.Button, "<Keyboard>/2");
            secondBlessingAction.AddBinding("<Keyboard>/numpad2");
            secondBlessingAction.performed += HandleSecondBlessing;

            // Echo is part of the approved M2 scope and was previously reachable
            // only through direct keyboard polling inside BlessingTargeting.
            thirdBlessingAction = inputActions.AddAction("ThirdBlessing", InputActionType.Button, "<Keyboard>/3");
            thirdBlessingAction.AddBinding("<Keyboard>/numpad3");
            thirdBlessingAction.performed += HandleThirdBlessing;

            applyAction = inputActions.AddAction("Apply", InputActionType.Button, "<Mouse>/leftButton");
            applyAction.performed += HandleApply;

            cancelAction = inputActions.AddAction("Cancel", InputActionType.Button, "<Mouse>/rightButton");
            cancelAction.performed += HandleCancel;

            pauseAction = inputActions.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            pauseAction.performed += HandlePause;

            restartAction = inputActions.AddAction("Restart", InputActionType.Button, "<Keyboard>/r");
            restartAction.performed += HandleRestart;
        }

        private void HandleMovement(InputAction.CallbackContext context)
        {
            if (!CanRouteInput())
            {
                return;
            }

            SetMovement(context.ReadValue<Vector2>());
        }

        private void HandleMousePosition(InputAction.CallbackContext context)
        {
            if (!CanRouteInput())
            {
                return;
            }

            SetMousePosition(context.ReadValue<Vector2>());
        }

        private void HandleDash(InputAction.CallbackContext context)
        {
            if (CanRouteInput())
            {
                InvokeObservers(DashRequested, "Dash request");
            }
        }

        private void HandleFirstBlessing(InputAction.CallbackContext context)
        {
            if (CanRouteInput())
            {
                InvokeObservers(BlessingSelectionRequested, 1, "Blessing selection request");
            }
        }

        private void HandleSecondBlessing(InputAction.CallbackContext context)
        {
            if (CanRouteInput())
            {
                InvokeObservers(BlessingSelectionRequested, 2, "Blessing selection request");
            }
        }

        private void HandleThirdBlessing(InputAction.CallbackContext context)
        {
            if (CanRouteInput())
            {
                InvokeObservers(BlessingSelectionRequested, 3, "Blessing selection request");
            }
        }

        private void HandleApply(InputAction.CallbackContext context)
        {
            if (CanRouteInput())
            {
                InvokeObservers(ApplyRequested, "Blessing apply request");
            }
        }

        private void HandleCancel(InputAction.CallbackContext context)
        {
            if (CanRouteCancel())
            {
                InvokeObservers(CancelRequested, "Cancel request");
            }
        }
        private void HandlePause(InputAction.CallbackContext context)
        {
            if (CanRouteCancel())
            {
                InvokeObservers(PauseRequested, "Pause request");
            }
        }

        private void HandleRestart(InputAction.CallbackContext context)
        {
            if (hasApplicationFocus &&
                restartInputEnabled &&
                !inputBlockers.Contains(PlayerInputBlocker.FocusGate))
            {
                InvokeObservers(RestartRequested, "Restart request");
            }
        }

        private bool CanRouteCancel()
        {
            return hasApplicationFocus &&
                   !inputBlockers.Contains(PlayerInputBlocker.FocusGate) &&
                   !inputBlockers.Contains(PlayerInputBlocker.LifeCycle) &&
                   !inputBlockers.Contains(PlayerInputBlocker.RoomRestart);
        }

        private bool CanRouteInput()
        {
            return IsInputEnabled && hasApplicationFocus;
        }

        private static void InvokeObservers(Action observers, string operation)
        {
            if (observers == null)
            {
                return;
            }

            var failures = new List<Exception>();
            foreach (Action observer in observers.GetInvocationList())
            {
                try
                {
                    observer();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            ThrowObserverFailures(operation, failures);
        }

        private static void InvokeObservers<T>(Action<T> observers, T value, string operation)
        {
            if (observers == null)
            {
                return;
            }

            var failures = new List<Exception>();
            foreach (Action<T> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(value);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            ThrowObserverFailures(operation, failures);
        }

        private static void ThrowObserverFailures(string operation, List<Exception> failures)
        {
            if (failures.Count != 0)
            {
                throw new AggregateException(operation + " observers failed.", failures);
            }
        }

        private static void ValidateBlocker(PlayerInputBlocker blocker)
        {
            if (blocker != PlayerInputBlocker.LifeCycle &&
                blocker != PlayerInputBlocker.RoomRestart &&
                blocker != PlayerInputBlocker.FocusGate &&
                blocker != PlayerInputBlocker.Pause)
            {
                throw new ArgumentOutOfRangeException(nameof(blocker), blocker, "Unsupported player-input blocker.");
            }
        }
        private void SetMovement(Vector2 value)
        {
            if (movement == value)
            {
                return;
            }

            movement = value;
            InvokeObservers(MovementChanged, movement, "Movement change");
        }
        private void SetMousePosition(Vector2 value)
        {
            if (mousePosition == value)
            {
                return;
            }

            mousePosition = value;
            InvokeObservers(MousePositionChanged, mousePosition, "Mouse-position change");
        }
    }
}
