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

    [DisallowMultipleComponent]
    public sealed class BlessingTargeting : MonoBehaviour
    {
        public const float SelectionTimeScale = 0.2f;

        private const int NoTarget = 0;
        private const int OverlapBufferCapacity = 16;
        private const string EnemyBodyLayerName = "EnemyBody";

        [SerializeField] private Camera targetingCamera;
        [SerializeField] private string enemyBodyLayerName = EnemyBodyLayerName;

        private readonly SortedDictionary<int, TargetBinding> targetsByEntityId =
            new SortedDictionary<int, TargetBinding>();
        private readonly Dictionary<Collider2D, int> targetEntityIdsByCollider =
            new Dictionary<Collider2D, int>();
        private readonly List<Collider2D> overlapResults = new List<Collider2D>(OverlapBufferCapacity);

        private BlessingSystem blessingSystem;
        private BlessingSlot hasteSlot;
        private BlessingSlot giantSlot;
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
        private long selectionPublicationGeneration;
        private long targetPublicationGeneration;
        private BlessingType selectedType;
        private int hoveredTargetEntityId;

        public event Action<BlessingSelectionState> SelectionUiChanged;
        public event Action<IReadOnlyList<BlessingTargetState>> TargetStatesChanged;

        public bool IsSelecting => isSelecting;
        public BlessingType SelectedType => selectedType;
        public bool IsAvailable(BlessingType type)
        {
            EnsureInitialized();
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
            EnsureInitialized();
        }
        private void OnEnable()
        {
            if (isInitialized)
            {
                PublishSelectionState();
                PublishTargetStates();
            }
        }


        private void Update()
        {
            EnsureInitialized();
            hasteSlot.Advance(Time.time);
            giantSlot.Advance(Time.time);
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
                var forceForgetLockedTarget = IsDestroyedRuntime(binding.Runtime) || binding.Health == null;
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
            ThrowIfTargetMutationBlocked();

            var slot = GetSlot(type);
            if (!slot.IsAvailable || Time.timeScale <= 0f)
            {
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
            return true;
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
                (!targetsByEntityId.TryGetValue(targetEntityId, out var binding) || !IsEligible(binding)))
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
            return true;
        }

        public bool ApplyHoveredTarget()
        {
            EnsureInitialized();
            ThrowIfTargetMutationBlocked();

            if (!isSelecting || hoveredTargetEntityId == NoTarget ||
                !targetsByEntityId.TryGetValue(hoveredTargetEntityId, out var binding) || !IsEligible(binding))
            {
                return false;
            }

            var slot = GetSlot(selectedType);
            if (!blessingSystem.TryApply(slot, binding.Runtime, binding.Health, out var application))
            {
                PublishTargetStates();
                return false;
            }

            Debug.Log(FormatApplyLog(application));
            CancelSelection();
            return true;
        }

        public void CancelSelection()
        {
            EnsureInitialized();

            if (!isSelecting)
            {
                return;
            }

            isSelecting = false;
            hoveredTargetEntityId = NoTarget;
            RestoreTimeScale();
            PublishSelectionTransition();
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
                        if (IsDestroyedRuntime(binding.Runtime) || binding.Health == null)
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
                            blessingSystem.RemoveTarget(binding.Runtime);
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
                        try
                        {
                            blessingSystem.Reset();
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }

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
                        !IsEligible(binding) ||
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

        private bool IsEligible(TargetBinding binding)
        {
            if (!isSelecting || binding.Health == null ||
                (binding.Runtime is UnityEngine.Object runtimeObject && runtimeObject == null))
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

                var isEligible = IsEligible(binding);
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
            hasteSlot.AvailabilityChanged += HandleSlotAvailabilityChanged;
            giantSlot.AvailabilityChanged += HandleSlotAvailabilityChanged;
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

                foreach (var pair in targetsByEntityId)
                {
                    try
                    {
                        if (!ReferenceEquals(pair.Value.Health, null))
                        {
                            pair.Value.Health.Died -= HandleRegisteredTargetDied;
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                targetsByEntityId.Clear();
                targetEntityIdsByCollider.Clear();

                try
                {
                    blessingSystem.Reset();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                try
                {
                    hasteSlot.AvailabilityChanged -= HandleSlotAvailabilityChanged;
                    giantSlot.AvailabilityChanged -= HandleSlotAvailabilityChanged;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

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
