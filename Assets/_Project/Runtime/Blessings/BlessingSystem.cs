using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Overbless.Runtime
{
    public interface IEnemyBlessingRuntime
    {
        int EntityId { get; }
        EnemyDefinition Definition { get; }
        float HealthRatio { get; }
        void ApplyBlessingRuntimeStats(EnemyRuntimeStats stats, float healthRatio);
    }

    public readonly struct BlessingApplication
    {
        public BlessingApplication(BlessingType type, int targetEntityId, IReadOnlyList<BlessingType> activeBlessings)
        {
            Type = type;
            TargetEntityId = targetEntityId;
            ActiveBlessings = activeBlessings;
        }

        public BlessingType Type { get; }
        public int TargetEntityId { get; }
        public IReadOnlyList<BlessingType> ActiveBlessings { get; }
    }

    public sealed class BlessingSystem
    {
        private readonly Dictionary<int, HashSet<BlessingType>> activeBlessingsByEntityId =
            new Dictionary<int, HashSet<BlessingType>>();
        private readonly Dictionary<int, Dictionary<BlessingType, BlessingSlot>> lockedSlotsByEntityId =
            new Dictionary<int, Dictionary<BlessingType, BlessingSlot>>();
        private static readonly BlessingType[] NoBlessings = Array.Empty<BlessingType>();
        private static readonly BlessingType[] ImplementedBlessingTypes =
        {
            BlessingType.Haste,
            BlessingType.Giant
        };
        private bool isMutating;

        public bool CanApply(BlessingType type, IEnemyBlessingRuntime target)
        {
            if (target == null)
            {
                return false;
            }

            RequireDefinition(target);

            if ((type != BlessingType.Haste && type != BlessingType.Giant) || target.EntityId == 0)
            {
                return false;
            }

            return !activeBlessingsByEntityId.TryGetValue(target.EntityId, out var activeBlessings) ||
                   !activeBlessings.Contains(type);
        }

        public bool TryApply(
            BlessingSlot slot,
            IEnemyBlessingRuntime target,
            Health health,
            out BlessingApplication application)
        {
            using var mutation = EnterMutation();
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (health == null)
            {
                throw new ArgumentNullException(nameof(health));
            }

            RequireDefinition(target);

            var targetEntityId = target.EntityId;
            if (targetEntityId == 0 || health.EntityId != targetEntityId || health.IsDead)
            {
                application = default;
                return false;
            }

            var type = slot.Definition.Type;
            if (!slot.IsAvailable || !CanApply(type, target))
            {
                application = default;
                return false;
            }

            if (!slot.TryLock(health))
            {
                application = default;
                return false;
            }

            if (!IsLiveTarget(target, health, targetEntityId))
            {
                slot.CancelLock();
                application = default;
                return false;
            }

            var preservedHealthRatio = target.HealthRatio;
            if (!IsValidHealthRatio(preservedHealthRatio))
            {
                var validationException = new InvalidOperationException(
                    "Blessing targets must expose a finite health ratio within [0,1].");
                try
                {
                    slot.CancelLock();
                }
                catch (Exception cancellationException)
                {
                    throw new AggregateException(
                        "Blessing application rejected an invalid post-lock health ratio and lock cancellation failed.",
                        validationException,
                        cancellationException);
                }

                throw validationException;
            }

            var priorActiveBlessings = GetActiveBlessings(targetEntityId);


            if (!activeBlessingsByEntityId.TryGetValue(targetEntityId, out var activeBlessings))
            {
                activeBlessings = new HashSet<BlessingType>();
                activeBlessingsByEntityId.Add(targetEntityId, activeBlessings);
            }

            activeBlessings.Add(type);
            RecordLockedSlot(targetEntityId, type, slot);

            try
            {
                var orderedActiveBlessings = GetOrderedActiveBlessings(activeBlessings);
                var stats = EnemyRuntimeStats.Recompute(target.Definition, orderedActiveBlessings);
                var currentHealthRatio = preservedHealthRatio;
                target.ApplyBlessingRuntimeStats(stats, currentHealthRatio);
                application = new BlessingApplication(type, targetEntityId, orderedActiveBlessings);
                return true;
            }
            catch (Exception primaryException)
            {
                var failures = new List<Exception> { primaryException };
                var rollbackSucceeded = false;
                try
                {
                    var rollbackStats = EnemyRuntimeStats.Recompute(target.Definition, priorActiveBlessings);
                    target.ApplyBlessingRuntimeStats(rollbackStats, preservedHealthRatio);
                    rollbackSucceeded = true;
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }

                if (rollbackSucceeded)
                {
                    activeBlessings.Remove(type);
                    if (activeBlessings.Count == 0)
                    {
                        activeBlessingsByEntityId.Remove(targetEntityId);
                    }

                    RemoveLockedSlot(targetEntityId, type, slot);

                    try
                    {
                        slot.CancelLock();
                    }
                    catch (Exception slotException)
                    {
                        failures.Add(slotException);
                    }
                }
                else
                {
                    try
                    {
                        slot.PinForRestorationRetry();
                    }
                    catch (Exception slotException)
                    {
                        failures.Add(slotException);
                    }
                }

                if (failures.Count == 1)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryException).Throw();
                }

                throw new AggregateException(
                    rollbackSucceeded
                        ? "Blessing application failed and cleanup reported additional failures."
                        : "Blessing application and baseline restoration failed; ownership was retained for retry.",
                    failures);
            }
        }

        public IReadOnlyList<BlessingType> GetActiveBlessings(int targetEntityId)
        {
            if (targetEntityId == 0 || !activeBlessingsByEntityId.TryGetValue(targetEntityId, out var activeBlessings))
            {
                return NoBlessings;
            }

            return GetOrderedActiveBlessings(activeBlessings);
        }

        public bool RemoveTarget(IEnemyBlessingRuntime target)
        {
            using var mutation = EnterMutation();
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            RequireDefinition(target);

            var targetEntityId = target.EntityId;
            if (targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(target),
                    targetEntityId,
                    "Blessing runtimes require a non-zero entity ID.");
            }

            if (!activeBlessingsByEntityId.ContainsKey(targetEntityId))
            {
                return false;
            }

            var stats = EnemyRuntimeStats.Recompute(target.Definition, NoBlessings);
            var preservedHealthRatio = Mathf.Clamp01(target.HealthRatio);
            try
            {
                target.ApplyBlessingRuntimeStats(stats, preservedHealthRatio);
            }
            catch (Exception restorationException)
            {
                var failures = new List<Exception> { restorationException };
                PinTrackedSlotsForRestorationRetry(targetEntityId, failures);
                if (failures.Count == 1)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(restorationException).Throw();
                }

                throw new AggregateException(
                    "Blessing target baseline restoration failed; ownership was retained for retry.",
                    failures);
            }

            activeBlessingsByEntityId.Remove(targetEntityId);
            var releaseFailures = new List<Exception>();
            ReleaseTrackedSlotsAfterRestoration(targetEntityId, releaseFailures);
            ThrowFailures(
                releaseFailures,
                "Blessing target baseline restoration succeeded but slot release reported failures.");
            return true;
        }

        public void ForgetTarget(int targetEntityId)
        {
            using var mutation = EnterMutation();
            if (targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetEntityId), targetEntityId, "Target entity IDs must be non-zero.");
            }

            var failures = new List<Exception>();
            ForceForgetTrackedSlots(targetEntityId, failures);
            activeBlessingsByEntityId.Remove(targetEntityId);
            ThrowFailures(failures, "Blessing target was forgotten but slot cleanup reported failures.");
        }

        public void Reset()
        {
            using var mutation = EnterMutation();
            activeBlessingsByEntityId.Clear();
            lockedSlotsByEntityId.Clear();
        }
        private IDisposable EnterMutation()
        {
            if (isMutating)
            {
                throw new InvalidOperationException("Blessing-system mutation cannot be re-entered.");
            }

            isMutating = true;
            return new MutationScope(this);
        }

        private sealed class MutationScope : IDisposable
        {
            private BlessingSystem owner;

            public MutationScope(BlessingSystem owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                owner.isMutating = false;
                owner = null;
            }
        }
        private static void RequireDefinition(IEnemyBlessingRuntime target)
        {
            if (target.Definition == null)
            {
                throw new ArgumentException("Blessing runtimes require an EnemyDefinition.", nameof(target));
            }
        }
        private static bool IsLiveTarget(IEnemyBlessingRuntime target, Health health, int expectedEntityId)
        {
            return !(target is UnityEngine.Object targetObject && targetObject == null) &&
                   health != null &&
                   target.EntityId == expectedEntityId &&
                   health.EntityId == expectedEntityId &&
                   !health.IsDead;
        }

        private static bool IsValidHealthRatio(float healthRatio)
        {
            return !float.IsNaN(healthRatio) &&
                   !float.IsInfinity(healthRatio) &&
                   healthRatio >= 0f &&
                   healthRatio <= 1f;
        }

        private void RecordLockedSlot(int targetEntityId, BlessingType type, BlessingSlot slot)
        {
            if (!lockedSlotsByEntityId.TryGetValue(targetEntityId, out var lockedSlots))
            {
                lockedSlots = new Dictionary<BlessingType, BlessingSlot>();
                lockedSlotsByEntityId.Add(targetEntityId, lockedSlots);
            }

            lockedSlots.Add(type, slot);
        }

        private void RemoveLockedSlot(int targetEntityId, BlessingType type, BlessingSlot slot)
        {
            if (!lockedSlotsByEntityId.TryGetValue(targetEntityId, out var lockedSlots) ||
                !lockedSlots.TryGetValue(type, out var trackedSlot) ||
                !ReferenceEquals(trackedSlot, slot))
            {
                return;
            }

            lockedSlots.Remove(type);
            if (lockedSlots.Count == 0)
            {
                lockedSlotsByEntityId.Remove(targetEntityId);
            }
        }

        private void PinTrackedSlotsForRestorationRetry(int targetEntityId, List<Exception> failures)
        {
            if (!lockedSlotsByEntityId.TryGetValue(targetEntityId, out var lockedSlots))
            {
                return;
            }

            for (var index = 0; index < ImplementedBlessingTypes.Length; index++)
            {
                if (!lockedSlots.TryGetValue(ImplementedBlessingTypes[index], out var slot) ||
                    slot.LockedTargetEntityId != targetEntityId)
                {
                    continue;
                }

                try
                {
                    slot.PinForRestorationRetry();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        private void ReleaseTrackedSlotsAfterRestoration(int targetEntityId, List<Exception> failures)
        {
            if (!lockedSlotsByEntityId.TryGetValue(targetEntityId, out var lockedSlots))
            {
                return;
            }

            for (var index = 0; index < ImplementedBlessingTypes.Length; index++)
            {
                if (!lockedSlots.TryGetValue(ImplementedBlessingTypes[index], out var slot))
                {
                    continue;
                }

                try
                {
                    slot.ReleaseAfterRestoration(targetEntityId);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            lockedSlotsByEntityId.Remove(targetEntityId);
        }

        private void ForceForgetTrackedSlots(int targetEntityId, List<Exception> failures)
        {
            if (!lockedSlotsByEntityId.TryGetValue(targetEntityId, out var lockedSlots))
            {
                return;
            }

            for (var index = 0; index < ImplementedBlessingTypes.Length; index++)
            {
                if (!lockedSlots.TryGetValue(ImplementedBlessingTypes[index], out var slot))
                {
                    continue;
                }

                try
                {
                    slot.ForceForgetTarget(targetEntityId);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            lockedSlotsByEntityId.Remove(targetEntityId);
        }

        private static void ThrowFailures(List<Exception> failures, string aggregateMessage)
        {
            if (failures.Count == 0)
            {
                return;
            }

            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            throw new AggregateException(aggregateMessage, failures);
        }
        private static IReadOnlyList<BlessingType> GetOrderedActiveBlessings(IEnumerable<BlessingType> activeBlessings)
        {
            var orderedActiveBlessings = new List<BlessingType>(activeBlessings);
            orderedActiveBlessings.Sort();
            return new ReadOnlyCollection<BlessingType>(orderedActiveBlessings);
        }

    }
}
