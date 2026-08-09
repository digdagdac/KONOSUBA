using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overbless.Runtime
{
    public readonly struct BlessingSelectionState
    {
        public BlessingSelectionState(bool isSelecting, BlessingType selectedType, float unscaledTime)
        {
            IsSelecting = isSelecting;
            SelectedType = selectedType;
            UnscaledTime = unscaledTime;
        }

        public bool IsSelecting { get; }
        public BlessingType SelectedType { get; }
        public float UnscaledTime { get; }
    }

    public readonly struct BlessingPreviewData
    {
        public BlessingPreviewData(int targetEntityId, BlessingDefinition definition)
        {
            TargetEntityId = targetEntityId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public int TargetEntityId { get; }
        public BlessingDefinition Definition { get; }
    }

    public readonly struct BlessingTargetState
    {
        public BlessingTargetState(
            int targetEntityId,
            Vector3 worldPosition,
            bool isEligible,
            bool isOutlined,
            bool hasPreview,
            BlessingPreviewData preview)
        {
            TargetEntityId = targetEntityId;
            WorldPosition = worldPosition;
            IsEligible = isEligible;
            IsOutlined = isOutlined;
            HasPreview = hasPreview;
            Preview = preview;
        }

        public int TargetEntityId { get; }
        public Vector3 WorldPosition { get; }
        public bool IsEligible { get; }
        public bool IsOutlined { get; }
        public bool HasPreview { get; }
        public BlessingPreviewData Preview { get; }
    }

    public enum BlessingRejectionReason
    {
        /// <summary>The chosen slot is still bound to an earlier target.</summary>
        SlotUnavailable,

        /// <summary>The hovered target stopped being a legal recipient.</summary>
        TargetUnavailable,

        /// <summary>The blessing system refused the application.</summary>
        ApplicationFailed
    }

    /// <summary>
    /// Reports a completed blessing application so feedback layers can react.
    /// Applying a blessing is the player's only offensive action, so it needs an
    /// explicit signal rather than a log line.
    /// </summary>
    public readonly struct BlessingApplicationSignal
    {
        public BlessingApplicationSignal(BlessingType type, int targetEntityId, long occurrence)
        {
            if (targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetEntityId), targetEntityId, "Applied blessings require a non-zero target entity ID.");
            }

            if (occurrence <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(occurrence), occurrence, "Feedback occurrences must be positive.");
            }

            Type = type;
            TargetEntityId = targetEntityId;
            Occurrence = occurrence;
        }

        public BlessingType Type { get; }
        public int TargetEntityId { get; }
        public long Occurrence { get; }
    }

    /// <summary>
    /// Reports a refused blessing input. Rejections were previously silent, which
    /// left the player unable to tell a missed input from an unavailable slot.
    /// </summary>
    public readonly struct BlessingRejectionSignal
    {
        public BlessingRejectionSignal(
            BlessingType type,
            int targetEntityId,
            BlessingRejectionReason reason,
            long occurrence)
        {
            if (occurrence <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(occurrence), occurrence, "Feedback occurrences must be positive.");
            }

            Type = type;
            TargetEntityId = targetEntityId;
            Reason = reason;
            Occurrence = occurrence;
        }

        public BlessingType Type { get; }

        /// <summary>Zero when no specific target was involved.</summary>
        public int TargetEntityId { get; }

        public BlessingRejectionReason Reason { get; }
        public long Occurrence { get; }
    }

    [DisallowMultipleComponent]
    public sealed class BlessingTargeting : MonoBehaviour
    {
        public const float SelectionTimeScale = 0.2f;

        private const int NoTarget = 0;
        private const int OverlapBufferCapacity = 16;
        private const string EnemyBodyLayerName = "EnemyBody";

        [SerializeField] private Camera targetingCamera;
        [SerializeField] private string enemyBodyLayerName = EnemyBodyLayerName;
        [SerializeField] private bool echoEnabled;

        /// <summary>
        /// Optional. When assigned, selection, apply, and cancel arrive through the
        /// router, which is the only component allowed to read input devices
        /// (PROJECT_RULES section 3). Left optional so a scene generated before
        /// this field existed keeps working through the legacy polling path.
        /// </summary>
        [SerializeField] private PlayerInputRouter inputRouter;

        private readonly SortedDictionary<int, TargetBinding> targetsByEntityId =
            new SortedDictionary<int, TargetBinding>();
        private readonly Dictionary<Collider2D, int> targetEntityIdsByCollider =
            new Dictionary<Collider2D, int>();
        private readonly List<Collider2D> overlapResults = new List<Collider2D>(OverlapBufferCapacity);

        private BlessingSystem blessingSystem;
        private BlessingSlot hasteSlot;
        private BlessingSlot giantSlot;
        private BlessingSlot echoSlot;
        private Health ownerHealth;
        private int enemyBodyLayer;
        private int enemyBodyLayerMask;
        private bool isInitialized;
        private bool isSelecting;
        private bool isTargetingConfigurationValidated;
        private bool isEnemyBodyLayerValidated;
        private bool suppressTargetStatePublication;
        private bool suppressSelectionStatePublication;
        private bool isResettingTargets;
        private bool isCleaningUp;
        private bool isCancellingSelection;
        private bool isRouterSubscribed;
        private long selectionPublicationGeneration;
        private long targetPublicationGeneration;
        private BlessingType selectedType;
        private int hoveredTargetEntityId;
        private long feedbackOccurrence;

        public event Action<BlessingSelectionState> SelectionUiChanged;
        public event Action<IReadOnlyList<BlessingTargetState>> TargetStatesChanged;
        public event Action<BlessingApplicationSignal> BlessingApplied;
        public event Action<BlessingRejectionSignal> BlessingRejected;

        public bool IsSelecting => isSelecting;
        public BlessingType SelectedType => selectedType;
        public bool EchoEnabled => echoEnabled;
        public bool IsAvailable(BlessingType type)
        {
            EnsureInitialized();
            if (type == BlessingType.Echo && !echoEnabled)
            {
                return false;
            }

            return GetSlot(type).IsAvailable;
        }
        public BlessingSystem System
        {
            get
            {
                EnsureInitialized();
                return blessingSystem;
            }
        }
        public IReadOnlyList<BlessingTargetState> GetTargetStates()
        {
            EnsureInitialized();
            return BuildTargetStates();
        }


        private void Awake()
        {
            // Existing scene/player prefabs may predate the feedback presenter.
            // Auto-attach so TargetStatesChanged always has a subscriber in play.
            if (GetComponent<BlessingTargetFeedbackPresenter>() == null)
            {
                gameObject.AddComponent<BlessingTargetFeedbackPresenter>();
            }

            EnsureInitialized();
        }
        private void OnEnable()
        {
            SubscribeToRouter();
            if (isInitialized)
            {
                PublishSelectionState();
                PublishTargetStates();
            }
        }

        private void SubscribeToRouter()
        {
            if (isRouterSubscribed || inputRouter == null)
            {
                return;
            }

            inputRouter.BlessingSelectionRequested += HandleRoutedSelection;
            inputRouter.ApplyRequested += HandleRoutedApply;
            inputRouter.CancelRequested += HandleRoutedCancel;
            isRouterSubscribed = true;
        }

        private void UnsubscribeFromRouter()
        {
            if (!isRouterSubscribed || inputRouter == null)
            {
                return;
            }

            inputRouter.BlessingSelectionRequested -= HandleRoutedSelection;
            inputRouter.ApplyRequested -= HandleRoutedApply;
            inputRouter.CancelRequested -= HandleRoutedCancel;
            isRouterSubscribed = false;
        }

        private void HandleRoutedSelection(int blessingIndex)
        {
            switch (blessingIndex)
            {
                case 1:
                    Select(BlessingType.Haste);
                    break;
                case 2:
                    Select(BlessingType.Giant);
                    break;
                case 3:
                    Select(BlessingType.Echo);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(blessingIndex), blessingIndex, "Only blessing slots one through three exist.");
            }
        }

        private void HandleRoutedApply()
        {
            if (isSelecting)
            {
                ApplyHoveredTarget();
            }
        }

        private void HandleRoutedCancel()
        {
            CancelSelection();
        }


        private void Update()
        {
            EnsureInitialized();
            hasteSlot.Advance(Time.time);
            giantSlot.Advance(Time.time);
            if (echoEnabled)
            {
                echoSlot.Advance(Time.time);
            }

            HandleInput();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                HandlePause();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                HandleFocusLost();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromRouter();
            if (!isInitialized)
            {
                return;
            }

            var wasSelecting = isSelecting;
            CancelSelection();
            if (!wasSelecting)
            {
                PublishSelectionState();
                PublishTargetStates();
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        public void SetOwnerHealth(Health health)
        {
            EnsureInitialized();

            if (ReferenceEquals(ownerHealth, health))
            {
                return;
            }

            if (!ReferenceEquals(ownerHealth, null))
            {
                ownerHealth.Died -= HandleOwnerDied;
            }

            ownerHealth = health == null ? null : health;
            if (!ReferenceEquals(ownerHealth, null))
            {
                ownerHealth.Died += HandleOwnerDied;
            }
        }

        public void RegisterTarget(IEnemyBlessingRuntime target, Health health, Transform targetTransform)
        {
            EnsureInitialized();
            ThrowIfTargetMutationBlocked();

            if (IsDestroyedRuntime(target))
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.Definition == null)
            {
                throw new ArgumentException("Blessing targets require an EnemyDefinition.", nameof(target));
            }

            if (health == null)
            {
                throw new ArgumentNullException(nameof(health));
            }

            if (targetTransform == null)
            {
                throw new ArgumentNullException(nameof(targetTransform));
            }

            if (target.EntityId == 0 || target.EntityId != health.EntityId)
            {
                throw new ArgumentException("Blessing targets must expose the same non-zero entity ID as their Health component.", nameof(target));
            }

            if (targetsByEntityId.ContainsKey(target.EntityId))
            {
                throw new InvalidOperationException($"Target entity {target.EntityId} is already registered.");
            }

            EnsureEnemyBodyLayerConfiguration();
            var targetColliders = CacheTargetColliders(target.EntityId, targetTransform);
            targetsByEntityId.Add(target.EntityId, new TargetBinding(target, health, targetTransform, targetColliders));
            health.Died += HandleRegisteredTargetDied;
            PublishTargetStates();
        }

        public bool DeregisterTarget(int targetEntityId)
        {
            EnsureInitialized();
            ThrowIfTargetMutationBlocked();

            if (!targetsByEntityId.TryGetValue(targetEntityId, out var binding))
            {
                return false;
            }

            Exception failure = null;
            var removed = false;
            suppressTargetStatePublication = true;
            try
            {
                var forceForgetLockedTarget = RequiresForcedForget(targetEntityId, binding);
                if (forceForgetLockedTarget)
                {
                    blessingSystem.ForgetTarget(targetEntityId);
                }
                else
                {
                    try
                    {
                        blessingSystem.RemoveTarget(binding.Runtime);
                    }
                    catch (Exception restorationException)
                    {
                        var restorationFailures = new List<Exception> { restorationException };
                        try
                        {
                            PinSlotsForRestorationRetry(targetEntityId);
                        }
                        catch (Exception pinException)
                        {
                            restorationFailures.Add(pinException);
                        }

                        if (restorationFailures.Count == 1)
                        {
                            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo
                                .Capture(restorationException)
                                .Throw();
                        }

                        throw new AggregateException(
                            "Blessing target restoration failed and slot retry ownership could not be fully retained.",
                            restorationFailures);
                    }
                }

                RemoveBindingState(targetEntityId, binding, forceForgetLockedTarget);
                removed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                suppressTargetStatePublication = false;
            }

            try
            {
                PublishTargetStates();
            }
            catch (Exception publicationException)
            {
                failure = failure == null
                    ? publicationException
                    : new AggregateException(failure, publicationException);
            }

            if (failure != null)
            {
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return removed;
        }

        public bool Select(BlessingType type)
        {
            EnsureInitialized();
            if (type == BlessingType.Echo && !echoEnabled)
            {
                return false;
            }

            ThrowIfTargetMutationBlocked();
            if (!isActiveAndEnabled || isCancellingSelection)
            {
                return false;
            }

            // A dead owner must not be able to open a new selection. Pause and the
            // web focus gate are covered by the zero time-scale check below, but
            // the life-cycle block is not expressed through time scale.
            if (!ReferenceEquals(ownerHealth, null) && ownerHealth.IsDead)
            {
                return false;
            }

            var slot = GetSlot(type);
            var isPlayable = Time.timeScale > 0f;
            if (!slot.IsAvailable || !isPlayable)
            {
                if (isPlayable)
                {
                    // The slot is still bound to a previous target. Surfacing this
                    // is the only signal the player gets that the input was read.
                    RaiseBlessingRejected(type, NoTarget, BlessingRejectionReason.SlotUnavailable);
                }

                return false;
            }

            EnsureTargetingConfiguration();

            if (!isSelecting)
            {
                GameplayTimeScaleCoordinator.Acquire(GameplayTimeScaleClaim.BlessingSelection);
                isSelecting = true;
            }

            selectedType = type;
            hoveredTargetEntityId = NoTarget;
            PublishSelectionTransition();
            return isSelecting && selectedType == type;
        }

        public bool SetHoveredTarget(int targetEntityId)
        {
            EnsureInitialized();
            ThrowIfTargetMutationBlocked();

            if (!isSelecting)
            {
                return false;
            }

            if (targetEntityId != NoTarget &&
                (!targetsByEntityId.TryGetValue(targetEntityId, out var binding) ||
                 !IsEligible(targetEntityId, binding)))
            {
                if (hoveredTargetEntityId != NoTarget)
                {
                    hoveredTargetEntityId = NoTarget;
                    PublishTargetStates();
                }

                return false;
            }

            if (hoveredTargetEntityId == targetEntityId)
            {
                return true;
            }

            hoveredTargetEntityId = targetEntityId;
            PublishTargetStates();
            return isSelecting && hoveredTargetEntityId == targetEntityId;
        }

        public bool ApplyHoveredTarget()
        {
            EnsureInitialized();
            ThrowIfTargetMutationBlocked();

            if (!isSelecting || hoveredTargetEntityId == NoTarget ||
                !targetsByEntityId.TryGetValue(hoveredTargetEntityId, out var binding) ||
                !IsEligible(hoveredTargetEntityId, binding))
            {
                return false;
            }

            var slot = GetSlot(selectedType);
            var attemptedType = selectedType;
            var attemptedTargetEntityId = hoveredTargetEntityId;
            if (RequiresForcedForget(hoveredTargetEntityId, binding))
            {
                PublishTargetStates();
                RaiseBlessingRejected(attemptedType, attemptedTargetEntityId, BlessingRejectionReason.TargetUnavailable);
                return false;
            }
            if (!blessingSystem.TryApply(slot, binding.Runtime, binding.Health, out var application))
            {
                PublishTargetStates();
                RaiseBlessingRejected(attemptedType, attemptedTargetEntityId, BlessingRejectionReason.ApplicationFailed);
                return false;
            }

            Debug.Log(FormatApplyLog(application));
            CancelSelection();
            // Raised after cancellation so observers see settled selection state.
            RaiseBlessingApplied(attemptedType, attemptedTargetEntityId);
            return true;
        }

        private void RaiseBlessingApplied(BlessingType type, int targetEntityId)
        {
            BlessingApplied?.Invoke(
                new BlessingApplicationSignal(type, targetEntityId, NextFeedbackOccurrence()));
        }

        private void RaiseBlessingRejected(BlessingType type, int targetEntityId, BlessingRejectionReason reason)
        {
            BlessingRejected?.Invoke(
                new BlessingRejectionSignal(type, targetEntityId, reason, NextFeedbackOccurrence()));
        }

        /// <summary>
        /// Monotonic occurrence used by feedback consumers to build de-duplication
        /// tokens. Repeated identical rejections must remain distinguishable.
        /// </summary>
        private long NextFeedbackOccurrence()
        {
            if (feedbackOccurrence == long.MaxValue)
            {
                throw new InvalidOperationException("Blessing feedback occurrence overflowed.");
            }

            return ++feedbackOccurrence;
        }

        public void CancelSelection()
        {
            EnsureInitialized();

            if (!isSelecting || isCancellingSelection)
            {
                return;
            }

            isCancellingSelection = true;
            try
            {
                isSelecting = false;
                hoveredTargetEntityId = NoTarget;
                RestoreTimeScale();
                PublishSelectionTransition();
            }
            finally
            {
                isCancellingSelection = false;
            }
        }

        public void HandlePause()
        {
            CancelSelection();
        }

        public void HandleFocusLost()
        {
            CancelSelection();
        }

        public void HandleOwnerDeath()
        {
            CancelSelection();
        }

        public void HandleRoomRestart()
        {
            EnsureInitialized();
            if (isResettingTargets)
            {
                throw new InvalidOperationException("Blessing-target reset cannot be re-entered.");
            }

            isResettingTargets = true;
            var failures = new List<Exception>();
            var restorationFailed = false;
            try
            {
                try
                {
                    CancelSelection();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                var targetSnapshot = new List<KeyValuePair<int, TargetBinding>>(targetsByEntityId);
                suppressTargetStatePublication = true;
                try
                {
                    for (var index = 0; index < targetSnapshot.Count; index++)
                    {
                        var pair = targetSnapshot[index];
                        if (!targetsByEntityId.TryGetValue(pair.Key, out var currentBinding) ||
                            !IsSameBinding(currentBinding, pair.Value))
                        {
                            continue;
                        }

                        var binding = pair.Value;
                        if (RequiresForcedForget(pair.Key, binding))
                        {
                            try
                            {
                                RemoveBindingState(pair.Key, binding, true);
                            }
                            catch (Exception exception)
                            {
                                restorationFailed = true;
                                failures.Add(exception);
                            }

                            continue;
                        }

                        try
                        {
                            var hadActiveBlessings =
                                blessingSystem.GetActiveBlessings(pair.Key).Count != 0;
                            var removed = blessingSystem.RemoveTarget(binding.Runtime);
                            if ((hadActiveBlessings && !removed) ||
                                blessingSystem.GetActiveBlessings(pair.Key).Count != 0)
                            {
                                throw new InvalidOperationException(
                                    "Blessing target restoration did not discharge the registered ownership.");
                            }
                        }
                        catch (Exception exception)
                        {
                            restorationFailed = true;
                            failures.Add(exception);
                            try
                            {
                                PinSlotsForRestorationRetry(pair.Key);
                            }
                            catch (Exception pinException)
                            {
                                failures.Add(pinException);
                            }

                            continue;
                        }

                        try
                        {
                            ReleaseSlotsAfterRestoration(pair.Key);
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    }

                    if (!restorationFailed)
                    {
                        var systemResetSucceeded = false;
                        try
                        {
                            blessingSystem.Reset();
                            systemResetSucceeded = true;
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }

                        if (systemResetSucceeded)
                        {
                            try
                            {
                                CancelAllSlots();
                            }
                            catch (Exception exception)
                            {
                                failures.Add(exception);
                            }
                        }
                    }
                }
                finally
                {
                    suppressTargetStatePublication = false;
                }

                try
                {
                    PublishTargetStates();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            finally
            {
                isResettingTargets = false;
            }

            ThrowFailures(failures);
        }

        private void HandleInput()
        {
            if (inputRouter != null)
            {
                // Selection, apply, and cancel arrive as router events, so nothing
                // here reads a device. Hover is still re-evaluated every frame
                // because targets keep moving while the pointer stays still.
                if (isSelecting)
                {
                    UpdateHoveredTargetFromMouse(inputRouter.MousePosition);
                }

                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                {
                    Select(BlessingType.Haste);
                }
                else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                {
                    Select(BlessingType.Giant);
                }
                else if (echoEnabled &&
                         (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame))
                {
                    Select(BlessingType.Echo);
                }
                else if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    CancelSelection();
                }
            }

            if (!isSelecting)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            UpdateHoveredTargetFromMouse(mouse.position.ReadValue());
            if (mouse.rightButton.wasPressedThisFrame)
            {
                CancelSelection();
            }
            else if (mouse.leftButton.wasPressedThisFrame)
            {
                ApplyHoveredTarget();
            }
        }

        private void UpdateHoveredTargetFromMouse(Vector2 screenPosition)
        {
            EnsureTargetingConfiguration();
            var worldPosition = targetingCamera.ScreenToWorldPoint(screenPosition);
            var filter = new ContactFilter2D();
            filter.SetLayerMask(enemyBodyLayerMask);
            filter.useTriggers = true;
            overlapResults.Clear();
            var colliderCount = Physics2D.OverlapPoint(worldPosition, filter, overlapResults);
            try
            {
                if (colliderCount > OverlapBufferCapacity)
                {
                    throw new InvalidOperationException(
                        $"Blessing targeting overlap capacity ({OverlapBufferCapacity}) was exhausted.");
                }

                var selectedEntityId = NoTarget;
                for (var index = 0; index < colliderCount; index++)
                {
                    var collider = overlapResults[index];
                    if (collider == null ||
                        !targetEntityIdsByCollider.TryGetValue(collider, out var targetEntityId) ||
                        !targetsByEntityId.TryGetValue(targetEntityId, out var binding) ||
                        !IsEligible(targetEntityId, binding) ||
                        (selectedEntityId != NoTarget && targetEntityId >= selectedEntityId))
                    {
                        continue;
                    }

                    selectedEntityId = targetEntityId;
                }

                SetHoveredTarget(selectedEntityId);
            }
            finally
            {
                overlapResults.Clear();
            }
        }

        private bool IsEligible(int registeredEntityId, TargetBinding binding)
        {
            if (!isSelecting || RequiresForcedForget(registeredEntityId, binding))
            {
                return false;
            }

            return !binding.Health.IsDead &&
                   GetSlot(selectedType).IsAvailable &&
                   blessingSystem.CanApply(selectedType, binding.Runtime);
        }

        private BlessingSlot GetSlot(BlessingType type)
        {
            switch (type)
            {
                case BlessingType.Haste:
                    return hasteSlot;
                case BlessingType.Giant:
                    return giantSlot;
                case BlessingType.Echo:
                    return echoSlot;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Only implemented blessing types have slots.");
            }
        }

        private void HandleOwnerDied(DeathEvent deathEvent)
        {
            HandleOwnerDeath();
        }

        private void HandleRegisteredTargetDied(DeathEvent deathEvent)
        {
            if (hoveredTargetEntityId == deathEvent.EntityId)
            {
                CancelSelection();
            }
            else
            {
                PublishTargetStates();
            }
        }

        private void HandleSlotAvailabilityChanged(BlessingSlot slot)
        {
            if (isInitialized && !suppressTargetStatePublication)
            {
                PublishTargetStates();
            }
        }

        private void PublishSelectionTransition()
        {
            selectionPublicationGeneration++;
            targetPublicationGeneration++;

            var failures = new List<Exception>();
            try
            {
                PublishSelectionState();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                PublishTargetStates();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures);
        }
        private void PublishSelectionState()
        {
            var publicationGeneration = ++selectionPublicationGeneration;
            if (suppressSelectionStatePublication || !isInitialized || SelectionUiChanged == null)
            {
                return;
            }

            var state = new BlessingSelectionState(isSelecting, selectedType, Time.unscaledTime);
            var failures = new List<Exception>();
            foreach (Action<BlessingSelectionState> observer in SelectionUiChanged.GetInvocationList())
            {
                if (selectionPublicationGeneration != publicationGeneration)
                {
                    break;
                }

                try
                {
                    observer(state);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            ThrowFailures(failures);
        }

        private void PublishTargetStates()
        {
            var publicationGeneration = ++targetPublicationGeneration;
            if (suppressTargetStatePublication || !isInitialized || TargetStatesChanged == null)
            {
                return;
            }

            var states = BuildTargetStates();
            var failures = new List<Exception>();
            foreach (Action<IReadOnlyList<BlessingTargetState>> observer in TargetStatesChanged.GetInvocationList())
            {
                if (targetPublicationGeneration != publicationGeneration)
                {
                    break;
                }

                try
                {
                    observer(states);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            ThrowFailures(failures);
        }

        private IReadOnlyList<BlessingTargetState> BuildTargetStates()
        {
            if (targetsByEntityId.Count == 0)
            {
                return Array.Empty<BlessingTargetState>();
            }

            var states = new List<BlessingTargetState>(targetsByEntityId.Count);
            foreach (var pair in targetsByEntityId)
            {
                var binding = pair.Value;
                if (binding.Transform == null)
                {
                    continue;
                }

                var isEligible = IsEligible(pair.Key, binding);
                var hasPreview = isEligible && pair.Key == hoveredTargetEntityId;
                var preview = hasPreview
                    ? new BlessingPreviewData(pair.Key, BlessingDefinition.Get(selectedType))
                    : default;
                states.Add(new BlessingTargetState(
                    pair.Key,
                    binding.Transform.position,
                    isEligible,
                    isEligible,
                    hasPreview,
                    preview));
            }

            return states.AsReadOnly();
        }

        private void RestoreTimeScale()
        {
            GameplayTimeScaleCoordinator.Release(GameplayTimeScaleClaim.BlessingSelection);
        }

        private void EnsureInitialized()
        {
            if (isCleaningUp)
            {
                throw new ObjectDisposedException(nameof(BlessingTargeting));
            }

            if (isInitialized)
            {
                return;
            }

            blessingSystem = new BlessingSystem();
            hasteSlot = new BlessingSlot(BlessingDefinition.Haste);
            giantSlot = new BlessingSlot(BlessingDefinition.Giant);
            echoSlot = new BlessingSlot(BlessingDefinition.Echo);
            hasteSlot.AvailabilityChanged += HandleSlotAvailabilityChanged;
            giantSlot.AvailabilityChanged += HandleSlotAvailabilityChanged;
            echoSlot.AvailabilityChanged += HandleSlotAvailabilityChanged;
            selectedType = BlessingType.Haste;
            hoveredTargetEntityId = NoTarget;
            isInitialized = true;
        }
        private void EnsureTargetingConfiguration()
        {
            EnsureEnemyBodyLayerConfiguration();

            if (isTargetingConfigurationValidated)
            {
                if (targetingCamera == null)
                {
                    throw new InvalidOperationException("Blessing targeting's configured camera was destroyed.");
                }

                return;
            }

            if (targetingCamera == null)
            {
                targetingCamera = Camera.main;
            }

            if (targetingCamera == null)
            {
                throw new InvalidOperationException(
                    "Blessing targeting requires an assigned camera or a camera tagged MainCamera.");
            }

            isTargetingConfigurationValidated = true;
        }

        private void EnsureEnemyBodyLayerConfiguration()
        {
            if (isEnemyBodyLayerValidated)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(enemyBodyLayerName))
            {
                throw new InvalidOperationException("Blessing targeting requires an EnemyBody layer name.");
            }

            enemyBodyLayer = LayerMask.NameToLayer(enemyBodyLayerName);
            if (enemyBodyLayer < 0)
            {
                throw new InvalidOperationException(
                    $"Blessing targeting layer '{enemyBodyLayerName}' does not exist.");
            }

            enemyBodyLayerMask = 1 << enemyBodyLayer;
            isEnemyBodyLayerValidated = true;
        }

        private Collider2D[] CacheTargetColliders(int targetEntityId, Transform targetTransform)
        {
            var colliders = targetTransform.GetComponentsInChildren<Collider2D>(true);
            var targetColliders = new List<Collider2D>(colliders.Length);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null || collider.gameObject.layer != enemyBodyLayer)
                {
                    continue;
                }

                if (targetEntityIdsByCollider.ContainsKey(collider))
                {
                    throw new InvalidOperationException(
                        $"Blessing target collider for entity {targetEntityId} is already registered.");
                }

                targetColliders.Add(collider);
            }

            if (targetColliders.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Blessing target entity {targetEntityId} requires a Collider2D on layer '{enemyBodyLayerName}'.");
            }

            for (var index = 0; index < targetColliders.Count; index++)
            {
                targetEntityIdsByCollider.Add(targetColliders[index], targetEntityId);
            }

            return targetColliders.ToArray();
        }

        private void RemoveTargetColliders(TargetBinding binding)
        {
            for (var index = 0; index < binding.Colliders.Length; index++)
            {
                targetEntityIdsByCollider.Remove(binding.Colliders[index]);
            }
        }


        private static bool RequiresForcedForget(
            int registeredEntityId,
            TargetBinding binding)
        {
            return IsDestroyedRuntime(binding.Runtime) ||
                   binding.Health == null ||
                   binding.Runtime.EntityId != registeredEntityId ||
                   binding.Health.EntityId != registeredEntityId;
        }

        private void RemoveBindingState(int targetEntityId, TargetBinding binding, bool forceForgetLockedTarget)
        {
            var failures = new List<Exception>();
            try
            {
                if (!ReferenceEquals(binding.Health, null))
                {
                    binding.Health.Died -= HandleRegisteredTargetDied;
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            RemoveTargetColliders(binding);
            targetsByEntityId.Remove(targetEntityId);
            if (hoveredTargetEntityId == targetEntityId)
            {
                hoveredTargetEntityId = NoTarget;
            }

            try
            {
                blessingSystem.ForgetTarget(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                if (forceForgetLockedTarget)
                {
                    ForceForgetSlotsForTarget(targetEntityId);
                }
                else
                {
                    ReleaseSlotsAfterRestoration(targetEntityId);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures);
        }
        private void ThrowIfTargetMutationBlocked()
        {
            if (isResettingTargets)
            {
                throw new InvalidOperationException("Blessing targets cannot be mutated during room reset.");
            }
        }

        private static bool IsSameBinding(TargetBinding left, TargetBinding right)
        {
            return ReferenceEquals(left.Runtime, right.Runtime) &&
                   ReferenceEquals(left.Health, right.Health) &&
                   ReferenceEquals(left.Transform, right.Transform);
        }

        private void PinSlotsForRestorationRetry(int targetEntityId)
        {
            var failures = new List<Exception>();
            try
            {
                if (hasteSlot.LockedTargetEntityId == targetEntityId)
                {
                    hasteSlot.PinForRestorationRetry();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                if (giantSlot.LockedTargetEntityId == targetEntityId)
                {
                    giantSlot.PinForRestorationRetry();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                if (echoSlot.LockedTargetEntityId == targetEntityId)
                {
                    echoSlot.PinForRestorationRetry();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures);
        }

        private void ReleaseSlotsAfterRestoration(int targetEntityId)
        {
            var failures = new List<Exception>();
            try
            {
                hasteSlot.ReleaseAfterRestoration(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                giantSlot.ReleaseAfterRestoration(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                echoSlot.ReleaseAfterRestoration(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures);
        }

        private void ForceForgetSlotsForTarget(int targetEntityId)
        {
            var failures = new List<Exception>();
            try
            {
                hasteSlot.ForceForgetTarget(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                giantSlot.ForceForgetTarget(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                echoSlot.ForceForgetTarget(targetEntityId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures);
        }
        private void CancelAllSlots()
        {
            var failures = new List<Exception>();
            try
            {
                hasteSlot.CancelLock();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                giantSlot.CancelLock();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                echoSlot.CancelLock();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures);
        }


        private static bool IsDestroyedRuntime(IEnemyBlessingRuntime runtime)
        {
            return ReferenceEquals(runtime, null) ||
                   (runtime is UnityEngine.Object runtimeObject && runtimeObject == null);
        }

        private static void ThrowFailures(List<Exception> failures)
        {
            if (failures.Count == 1)
            {
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
        private void Cleanup()
        {
            if (!isInitialized || isCleaningUp)
            {
                return;
            }

            isCleaningUp = true;
            suppressSelectionStatePublication = true;
            suppressTargetStatePublication = true;
            var failures = new List<Exception>();
            var transferredRestoration = false;
            try
            {
                if (isSelecting)
                {
                    isSelecting = false;
                    hoveredTargetEntityId = NoTarget;
                    try
                    {
                        RestoreTimeScale();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                try
                {
                    if (!ReferenceEquals(ownerHealth, null))
                    {
                        ownerHealth.Died -= HandleOwnerDied;
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                finally
                {
                    ownerHealth = null;
                }

                hasteSlot.AvailabilityChanged -= HandleSlotAvailabilityChanged;
                giantSlot.AvailabilityChanged -= HandleSlotAvailabilityChanged;
                echoSlot.AvailabilityChanged -= HandleSlotAvailabilityChanged;

                var targetSnapshot = new List<KeyValuePair<int, TargetBinding>>(targetsByEntityId);
                for (var index = 0; index < targetSnapshot.Count; index++)
                {
                    var pair = targetSnapshot[index];
                    var binding = pair.Value;
                    try
                    {
                        if (!ReferenceEquals(binding.Health, null))
                        {
                            binding.Health.Died -= HandleRegisteredTargetDied;
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }

                    try
                    {
                        if (RequiresForcedForget(pair.Key, binding))
                        {
                            blessingSystem.ForgetTarget(pair.Key);
                        }
                        else
                        {
                            var hadActiveBlessings =
                                blessingSystem.GetActiveBlessings(pair.Key).Count != 0;
                            var removed = blessingSystem.RemoveTarget(binding.Runtime);
                            if ((hadActiveBlessings && !removed) ||
                                blessingSystem.GetActiveBlessings(pair.Key).Count != 0)
                            {
                                throw new InvalidOperationException(
                                    "Blessing cleanup did not discharge the registered ownership.");
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                        transferredRestoration = true;
                        BlessingRestorationRecovery.Enqueue(
                            blessingSystem,
                            binding.Runtime,
                            pair.Key);
                    }
                }
                for (var index = 0; index < targetSnapshot.Count; index++)
                {
                    var pair = targetSnapshot[index];
                    if (blessingSystem.GetActiveBlessings(pair.Key).Count == 0)
                    {
                        continue;
                    }

                    transferredRestoration = true;
                    BlessingRestorationRecovery.Enqueue(
                        blessingSystem,
                        pair.Value.Runtime,
                        pair.Key);
                }

                targetsByEntityId.Clear();
                targetEntityIdsByCollider.Clear();

                if (!transferredRestoration)
                {
                    var systemResetSucceeded = false;
                    try
                    {
                        blessingSystem.Reset();
                        systemResetSucceeded = true;
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }

                    if (systemResetSucceeded)
                    {
                        try
                        {
                            hasteSlot.Dispose();
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }

                        try
                        {
                            giantSlot.Dispose();
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }

                        try
                        {
                            echoSlot.Dispose();
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    }
                }
            }
            finally
            {
                isInitialized = false;
                SelectionUiChanged = null;
                TargetStatesChanged = null;
                suppressSelectionStatePublication = false;
                suppressTargetStatePublication = false;
            }

            ThrowFailures(failures);
        }

        private static class BlessingRestorationRecovery
        {
            private static readonly List<RecoveryRecord> Pending = new List<RecoveryRecord>();
            private static RecoveryRunner runner;

            public static void Enqueue(
                BlessingSystem system,
                IEnemyBlessingRuntime target,
                int targetEntityId)
            {
                for (var index = 0; index < Pending.Count; index++)
                {
                    if (ReferenceEquals(Pending[index].System, system) &&
                        Pending[index].TargetEntityId == targetEntityId)
                    {
                        return;
                    }
                }

                Pending.Add(new RecoveryRecord(system, target, targetEntityId));
                EnsureRunner();
            }

            private static void EnsureRunner()
            {
                if (runner != null)
                {
                    return;
                }

                var runnerObject = new GameObject("Blessing Restoration Recovery");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(runnerObject);
                runner = runnerObject.AddComponent<RecoveryRunner>();
            }

            private static void Process()
            {
                for (var index = Pending.Count - 1; index >= 0; index--)
                {
                    var record = Pending[index];
                    if (IsDestroyedRuntime(record.Target))
                    {
                        record.ObserveTargetDestroyed();
                        if (Time.unscaledTime < record.NextAttemptAt)
                        {
                            continue;
                        }

                        try
                        {
                            record.System.ForgetTarget(record.TargetEntityId);
                            Pending.RemoveAt(index);
                        }
                        catch (Exception exception)
                        {
                            record.RecordFailure(exception);
                        }

                        continue;
                    }

                    if (Time.unscaledTime < record.NextAttemptAt)
                    {
                        continue;
                    }

                    try
                    {
                        if (record.Target.EntityId != record.TargetEntityId)
                        {
                            record.System.ForgetTarget(record.TargetEntityId);
                        }
                        else if (record.System.GetActiveBlessings(record.TargetEntityId).Count == 0)
                        {
                            record.System.ForgetTarget(record.TargetEntityId);
                        }
                        else
                        {
                            var removed = record.System.RemoveTarget(record.Target);
                            if (!removed &&
                                record.System.GetActiveBlessings(record.TargetEntityId).Count != 0)
                            {
                                throw new InvalidOperationException(
                                    "Blessing restoration retry did not discharge the retained target ownership.");
                            }
                        }

                        if (record.System.GetActiveBlessings(record.TargetEntityId).Count != 0)
                        {
                            throw new InvalidOperationException(
                                "Blessing restoration retry left retained target ownership active.");
                        }

                        Pending.RemoveAt(index);
                    }
                    catch (Exception exception)
                    {
                        record.RecordFailure(exception);
                    }
                }

                if (Pending.Count == 0 && runner != null)
                {
                    var completedRunner = runner;
                    runner = null;
                    UnityEngine.Object.Destroy(completedRunner.gameObject);
                }
            }

            private sealed class RecoveryRecord
            {
                private const float InitialRetryDelay = 0.25f;
                private const float MaximumRetryDelay = 30f;

                private bool persistentFailureReported;

                public RecoveryRecord(
                    BlessingSystem system,
                    IEnemyBlessingRuntime target,
                    int targetEntityId)
                {
                    System = system;
                    Target = target;
                    TargetEntityId = targetEntityId;
                }

                public BlessingSystem System { get; }
                public IEnemyBlessingRuntime Target { get; }
                public int TargetEntityId { get; }
                public int FailureCount { get; private set; }
                public float NextAttemptAt { get; private set; }
                public Exception LastFailure { get; private set; }
                public bool TargetDestructionObserved { get; private set; }

                public void ObserveTargetDestroyed()
                {
                    if (TargetDestructionObserved)
                    {
                        return;
                    }

                    TargetDestructionObserved = true;
                    NextAttemptAt = 0f;
                }

                public void RecordFailure(Exception exception)
                {
                    LastFailure = exception;
                    FailureCount++;

                    var exponent = Math.Min(FailureCount - 1, 16);
                    var delay = Math.Min(
                        MaximumRetryDelay,
                        InitialRetryDelay * (float)Math.Pow(2d, exponent));
                    NextAttemptAt = Time.unscaledTime + delay;

                    if (FailureCount == 1)
                    {
                        Debug.LogException(exception);
                    }
                    else if (!persistentFailureReported && delay >= MaximumRetryDelay)
                    {
                        persistentFailureReported = true;
                        Debug.LogError(
                            $"Blessing baseline restoration for entity {TargetEntityId} remains retained after " +
                            $"{FailureCount} failures and will retry every {MaximumRetryDelay:0} seconds. " +
                            $"Last error: {exception.Message}");
                    }
                }
            }

            private sealed class RecoveryRunner : MonoBehaviour
            {
                private void Update()
                {
                    Process();
                }
            }
        }

        private static string FormatApplyLog(BlessingApplication application)
        {
            var log = new StringBuilder();
            log.Append("BlessingApplied {\"type\":\"");
            log.Append(application.Type);
            log.Append("\",\"targetEntityId\":");
            log.Append(application.TargetEntityId);
            log.Append(",\"activeBlessingIds\":[");

            for (var index = 0; index < application.ActiveBlessings.Count; index++)
            {
                if (index > 0)
                {
                    log.Append(',');
                }

                log.Append('"');
                log.Append(BlessingDefinition.Get(application.ActiveBlessings[index]).Id);
                log.Append('"');
            }

            log.Append("]}");
            return log.ToString();
        }

        private readonly struct TargetBinding
        {
            public TargetBinding(
                IEnemyBlessingRuntime runtime,
                Health health,
                Transform transform,
                Collider2D[] colliders)
            {
                Runtime = runtime;
                Health = health;
                Transform = transform;
                Colliders = colliders;
            }

            public IEnemyBlessingRuntime Runtime { get; }
            public Health Health { get; }
            public Transform Transform { get; }
            public Collider2D[] Colliders { get; }
        }
    }
}
