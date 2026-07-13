using System;
using UnityEngine;

namespace Overbless.Runtime
{
    public sealed class BlessingSlot : IDisposable
    {
        public const float ReturnDelay = 0.5f;

        private Health lockedTarget;
        private int lockedTargetEntityId;
        private long lockedDeathToken;
        private long scheduledDeathToken;
        private float returnAtScaledTime;
        private bool isLocked;
        private bool isReturnScheduled;
        private bool isPinnedForRestorationRetry;
        private bool isDisposed;
        private long lockGeneration;
        private Action<DeathEvent> lockedTargetDeathHandler;

        public BlessingSlot(BlessingDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public event Action<BlessingSlot> AvailabilityChanged;

        public BlessingDefinition Definition { get; }
        public bool IsAvailable => !isLocked;
        public bool IsPinnedForRestorationRetry => isPinnedForRestorationRetry;
        public Health LockedTarget => lockedTarget;
        public int LockedTargetEntityId => lockedTargetEntityId;
        public long ScheduledDeathToken => scheduledDeathToken;

        public bool TryLock(Health target)
        {
            ThrowIfDisposed();

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (!IsAvailable || target.IsDead)
            {
                return false;
            }

            lockedTarget = target;
            lockedTargetEntityId = target.EntityId;
            lockedDeathToken = target.DeathToken;
            scheduledDeathToken = 0;
            isLocked = true;
            isReturnScheduled = false;
            isPinnedForRestorationRetry = false;
            var transitionGeneration = ++lockGeneration;
            lockedTargetDeathHandler = deathEvent => HandleTargetDied(target, deathEvent);
            target.Died += lockedTargetDeathHandler;
            try
            {
                NotifyAvailabilityChanged(
                    transitionGeneration,
                    () => isLocked &&
                          ReferenceEquals(lockedTarget, target) &&
                          lockGeneration == transitionGeneration);
            }
            catch (Exception primaryException)
            {
                var failures = new System.Collections.Generic.List<Exception> { primaryException };
                if (isLocked &&
                    ReferenceEquals(lockedTarget, target) &&
                    lockGeneration == transitionGeneration)
                {
                    var deathHandler = lockedTargetDeathHandler;
                    ClearLockState();
                    var compensationGeneration = ++lockGeneration;

                    if (deathHandler != null)
                    {
                        try
                        {
                            target.Died -= deathHandler;
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    }

                    try
                    {
                        NotifyAvailabilityChanged(
                            compensationGeneration,
                            () => !isLocked && lockGeneration == compensationGeneration);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                if (failures.Count == 1)
                {
                    global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryException).Throw();
                }

                throw new AggregateException(
                    "Blessing-slot lock notification failed and availability compensation reported additional failures.",
                    failures);
            }

            return isLocked &&
                   ReferenceEquals(lockedTarget, target) &&
                   lockGeneration == transitionGeneration;
        }

        public bool Advance(float scaledTime)
        {
            ThrowIfDisposed();

            if (!IsFinite(scaledTime))
            {
                throw new ArgumentOutOfRangeException(nameof(scaledTime), scaledTime, "Scaled time must be finite.");
            }

            if (isPinnedForRestorationRetry || !isReturnScheduled || scaledTime < returnAtScaledTime)
            {
                return false;
            }

            return ReleaseLock(false);
        }

        public void CancelLock()
        {
            if (!isDisposed)
            {
                ReleaseLock(false);
            }
        }
        public bool CancelLockFor(int targetEntityId)
        {
            if (isDisposed || targetEntityId == 0 || !isLocked || lockedTargetEntityId != targetEntityId)
            {
                return false;
            }

            return ReleaseLock(false);
        }
        public void PinForRestorationRetry()
        {
            ThrowIfDisposed();
            if (!isLocked)
            {
                throw new InvalidOperationException("Only locked blessing slots can be pinned for restoration retry.");
            }

            isPinnedForRestorationRetry = true;
            scheduledDeathToken = 0;
            returnAtScaledTime = 0f;
            isReturnScheduled = false;
        }

        public bool ReleaseAfterRestoration(int targetEntityId)
        {
            ThrowIfDisposed();
            if (targetEntityId == 0 || !isLocked || lockedTargetEntityId != targetEntityId)
            {
                return false;
            }

            return ReleaseLock(true);
        }

        public bool ForceForgetTarget(int targetEntityId)
        {
            ThrowIfDisposed();
            if (targetEntityId == 0 || !isLocked || lockedTargetEntityId != targetEntityId)
            {
                return false;
            }

            return ReleaseLock(true);
        }



        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            try
            {
                ReleaseLock(true);
            }
            finally
            {
                AvailabilityChanged = null;
            }
        }

        private void HandleTargetDied(Health source, DeathEvent deathEvent)
        {
            if (!isLocked ||
                !ReferenceEquals(source, lockedTarget) ||
                deathEvent.EntityId != lockedTargetEntityId ||
                deathEvent.DeathToken <= lockedDeathToken ||
                isReturnScheduled ||
                isPinnedForRestorationRetry)
            {
                return;
            }

            scheduledDeathToken = deathEvent.DeathToken;
            returnAtScaledTime = Time.time + ReturnDelay;
            isReturnScheduled = true;
        }

        private bool ReleaseLock(bool allowPinnedRetryRelease)
        {
            if (!isLocked || (isPinnedForRestorationRetry && !allowPinnedRetryRelease))
            {
                return false;
            }

            var target = lockedTarget;
            var deathHandler = lockedTargetDeathHandler;
            ClearLockState();
            var transitionGeneration = ++lockGeneration;

            if (!ReferenceEquals(target, null) && deathHandler != null)
            {
                target.Died -= deathHandler;
            }

            NotifyAvailabilityChanged(
                transitionGeneration,
                () => !isLocked && lockGeneration == transitionGeneration);
            return true;
        }

        private void ClearLockState()
        {
            lockedTargetDeathHandler = null;
            lockedTarget = null;
            lockedTargetEntityId = 0;
            lockedDeathToken = 0;
            scheduledDeathToken = 0;
            returnAtScaledTime = 0f;
            isLocked = false;
            isReturnScheduled = false;
            isPinnedForRestorationRetry = false;
        }

        private void NotifyAvailabilityChanged(long transitionGeneration, Func<bool> isCurrent)
        {
            if (AvailabilityChanged == null)
            {
                return;
            }

            var failures = new System.Collections.Generic.List<Exception>();
            foreach (Action<BlessingSlot> observer in AvailabilityChanged.GetInvocationList())
            {
                if (!isCurrent())
                {
                    break;
                }

                try
                {
                    observer(this);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 1)
            {
                throw new InvalidOperationException("Blessing-slot availability observer failed.", failures[0]);
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("Blessing-slot availability observers failed.", failures);
            }
        }
        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(BlessingSlot));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
