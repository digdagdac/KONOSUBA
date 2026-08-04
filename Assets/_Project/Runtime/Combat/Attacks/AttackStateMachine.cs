using System;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum AttackPhase
    {
        Idle,
        Warning,
        Locked,
        Executing,
        Recovery
    }

    public sealed class AttackStateMachine
    {
        public const float MinimumWarningDuration = 0.55f;
        private static long lastIssuedAttackInstanceId;

        private readonly int attackerEntityId;
        private float warningDuration;
        private float warningElapsed;
        private AttackContext currentContext;
        private long transitionVersion;

        public AttackStateMachine(int attackerEntityId)
        {
            if (attackerEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackerEntityId), attackerEntityId, "Attacker entity IDs must be non-zero.");
            }

            this.attackerEntityId = attackerEntityId;
            Phase = AttackPhase.Idle;
        }

        public event Action<AttackPhase> PhaseChanged;
        public event Action<AttackContext> ContextLocked;
        public event Action<AttackContext> ContextDisposed;

        public int AttackerEntityId => attackerEntityId;
        public AttackPhase Phase { get; private set; }
        public float WarningDuration => warningDuration;
        public float WarningElapsed => warningElapsed;
        public bool IsWarningComplete => Phase == AttackPhase.Warning && warningElapsed >= warningDuration;
        public AttackContext CurrentContext => currentContext;
        private static long IssueAttackInstanceId()
        {
            var issued = System.Threading.Interlocked.Increment(ref lastIssuedAttackInstanceId);
            if (issued <= 0)
            {
                throw new InvalidOperationException("Global attack instance IDs overflowed.");
            }

            return issued;
        }

        public void BeginWarning(float requestedWarningDuration)
        {
            RequirePhase(AttackPhase.Idle);

            if (!IsFinite(requestedWarningDuration) || requestedWarningDuration < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedWarningDuration), requestedWarningDuration, "Warning duration must be finite and non-negative.");
            }

            warningDuration = Mathf.Max(requestedWarningDuration, MinimumWarningDuration);
            warningElapsed = 0f;
            SetPhase(AttackPhase.Warning);
        }

        public bool AdvanceWarning(float deltaTime)
        {
            RequirePhase(AttackPhase.Warning);

            if (!IsFinite(deltaTime) || deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Warning delta time must be finite and non-negative.");
            }

            if (deltaTime >= warningDuration - warningElapsed)
            {
                warningElapsed = warningDuration;
            }
            else
            {
                warningElapsed += deltaTime;
            }

            return IsWarningComplete;
        }

        public AttackContext Lock(
            float lockedAt,
            Vector2 origin,
            Vector2 direction,
            AttackShape shape,
            float range,
            float width,
            int damage,
            LayerMask targetMask)
        {
            RequirePhase(AttackPhase.Warning);

            if (!IsWarningComplete)
            {
                throw new InvalidOperationException("An attack cannot lock before its warning duration has elapsed.");
            }

            var lockedContext = new AttackContext(
                IssueAttackInstanceId(),
                attackerEntityId,
                lockedAt,
                origin,
                direction,
                shape,
                range,
                width,
                damage,
                targetMask);
            currentContext = lockedContext;
            Phase = AttackPhase.Locked;
            var lockVersion = ++transitionVersion;

            var observerErrors = new System.Collections.Generic.List<Exception>();
            InvokeObservers(
                ContextLocked,
                lockedContext,
                observerErrors,
                () => transitionVersion == lockVersion &&
                      ReferenceEquals(currentContext, lockedContext) &&
                      Phase == AttackPhase.Locked);
            if (ReferenceEquals(currentContext, lockedContext) && Phase == AttackPhase.Locked)
            {
                InvokeObservers(
                    PhaseChanged,
                    AttackPhase.Locked,
                    observerErrors,
                    () => transitionVersion == lockVersion &&
                          ReferenceEquals(currentContext, lockedContext) &&
                          Phase == AttackPhase.Locked);
            }

            ThrowObserverErrors(observerErrors);
            if (!ReferenceEquals(currentContext, lockedContext))
            {
                throw new InvalidOperationException("The attack lock was cancelled by an observer.");
            }

            return lockedContext;
        }

        public void BeginExecuting()
        {
            RequirePhase(AttackPhase.Locked);
            EnsureContext();
            SetPhase(AttackPhase.Executing);
        }
        public AttackContext CreateIndependentContextCopy()
        {
            if (Phase != AttackPhase.Locked && Phase != AttackPhase.Executing)
            {
                throw new InvalidOperationException(
                    $"Independent attack contexts can only be created while locked or executing, but was {Phase}.");
            }

            EnsureContext();
            return new AttackContext(
                IssueAttackInstanceId(),
                currentContext.AttackerEntityId,
                currentContext.LockedAt,
                currentContext.Origin,
                currentContext.NormalizedDirection,
                currentContext.Shape,
                currentContext.Range,
                currentContext.Width,
                currentContext.Damage,
                currentContext.TargetMask);
        }

        public void BeginRecovery()
        {
            RequirePhase(AttackPhase.Executing);
            EnsureContext();

            var disposedContext = currentContext;
            currentContext = null;
            Phase = AttackPhase.Recovery;
            var recoveryVersion = ++transitionVersion;

            var observerErrors = new System.Collections.Generic.List<Exception>();
            InvokeObservers(
                PhaseChanged,
                AttackPhase.Recovery,
                observerErrors,
                () => transitionVersion == recoveryVersion && Phase == AttackPhase.Recovery);
            InvokeObservers(ContextDisposed, disposedContext, observerErrors, null);
            ThrowObserverErrors(observerErrors);
        }

        public void CompleteRecovery()
        {
            RequirePhase(AttackPhase.Recovery);
            SetPhase(AttackPhase.Idle);
        }

        public void Cancel()
        {
            var disposedContext = currentContext;
            currentContext = null;
            warningDuration = 0f;
            warningElapsed = 0f;
            var phaseChanged = Phase != AttackPhase.Idle;
            Phase = AttackPhase.Idle;
            var cancelVersion = ++transitionVersion;

            var observerErrors = new System.Collections.Generic.List<Exception>();
            if (phaseChanged)
            {
                InvokeObservers(
                    PhaseChanged,
                    AttackPhase.Idle,
                    observerErrors,
                    () => transitionVersion == cancelVersion && Phase == AttackPhase.Idle);
            }

            if (disposedContext != null)
            {
                InvokeObservers(ContextDisposed, disposedContext, observerErrors, null);
            }

            ThrowObserverErrors(observerErrors);
        }

        public void HandleOwnerDeath()
        {
            Cancel();
        }

        public void Reset()
        {
            Cancel();
        }


        private void EnsureContext()
        {
            if (currentContext == null)
            {
                throw new InvalidOperationException("Locked and executing attacks require a context.");
            }
        }

        private void RequirePhase(AttackPhase expectedPhase)
        {
            if (Phase != expectedPhase)
            {
                throw new InvalidOperationException($"Expected attack phase {expectedPhase}, but was {Phase}.");
            }
        }

        private void SetPhase(AttackPhase value)
        {
            Phase = value;
            var phaseVersion = ++transitionVersion;
            var observerErrors = new System.Collections.Generic.List<Exception>();
            InvokeObservers(
                PhaseChanged,
                value,
                observerErrors,
                () => transitionVersion == phaseVersion && Phase == value);
            ThrowObserverErrors(observerErrors);
        }

        private static void InvokeObservers<T>(
            Action<T> observers,
            T value,
            System.Collections.Generic.List<Exception> errors,
            Func<bool> continueCondition)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<T> observer in observers.GetInvocationList())
            {
                if (continueCondition != null && !continueCondition())
                {
                    break;
                }

                try
                {
                    observer(value);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }

        private static void ThrowObserverErrors(System.Collections.Generic.List<Exception> errors)
        {
            if (errors.Count == 1)
            {
                throw new InvalidOperationException("Attack-state observer failed.", errors[0]);
            }

            if (errors.Count > 1)
            {
                throw new AggregateException("Attack-state observers failed.", errors);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
