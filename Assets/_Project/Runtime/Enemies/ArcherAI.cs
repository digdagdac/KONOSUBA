using System;
using UnityEngine;

namespace Overbless.Runtime
{
    public sealed class ArcherAI : EnemyBase
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;
        private const float DistanceTolerance = 0.05f;
        private const float WallStopOffset = 0.001f;

        private float nextAttackAt;
        private float recoveryEndsAt;
        private bool projectileActive;
        private Vector2 projectilePosition;
        private AttackContext activeProjectileContext;
        private Vector2 lastSeparationDirection = Vector2.right;

        public event Action<AttackContext, Vector2> ProjectileFired;
        public event Action<AttackContext, Vector2> ProjectileMoved;
        public event Action<AttackContext, Vector2> ProjectileStopped;

        public bool IsProjectileActive => projectileActive;
        public Vector2 ProjectilePosition => projectilePosition;
        public AttackContext ProjectileContext => activeProjectileContext;

        protected override void TickBehavior(float deltaTime)
        {
            switch (CurrentAttackPhase)
            {
                case AttackPhase.Idle:
                    MaintainDistanceAndTryStartWarning(deltaTime);
                    break;

                case AttackPhase.Warning:
                    AdvanceShotWarning(deltaTime);
                    break;

                case AttackPhase.Executing:
                    AdvanceProjectile(deltaTime);
                    break;

                case AttackPhase.Recovery:
                    CompleteShotRecoveryWhenReady();
                    break;
            }
        }
        protected override void OnDisable()
        {
            try
            {
                TerminateProjectile(false);
            }
            finally
            {
                base.OnDisable();
            }
        }

        protected override void OnEnemyDied(DeathEvent deathEvent)
        {
            TerminateProjectile(false);
        }

        protected override void OnRestarted()
        {
            Exception failure = null;
            try
            {
                TerminateProjectile(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                nextAttackAt = 0f;
                recoveryEndsAt = 0f;
                projectilePosition = transform.position;
                lastSeparationDirection = Vector2.right;
            }

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private void MaintainDistanceAndTryStartWarning(float deltaTime)
        {
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                return;
            }

            MaintainDistance(targetPosition, deltaTime);

            if (Time.time < nextAttackAt)
            {
                return;
            }

            var targetOffset = targetPosition - (Vector2)transform.position;
            if (targetOffset.sqrMagnitude > RuntimeStats.EngagementRange * RuntimeStats.EngagementRange)
            {
                return;
            }

            BeginAttackWarning(RuntimeStats.WarningDuration);
        }

        private void AdvanceShotWarning(float deltaTime)
        {
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                CancelAttack();
                return;
            }

            MaintainDistance(targetPosition, deltaTime);

            if (!AdvanceAttackWarning(deltaTime))
            {
                return;
            }

            var direction = targetPosition - (Vector2)transform.position;
            if (direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
            {
                CancelAttack();
                return;
            }

            var context = LockAttack(
                direction,
                AttackShape.Line,
                RuntimeStats.AttackRange,
                RuntimeStats.AttackWidth,
                RuntimeStats.AttackDamage,
                Definition.DamageTargetMask);
            projectilePosition = context.Origin;
            projectileActive = true;
            activeProjectileContext = context;
            BeginAttackExecution();
            var firedErrors = new System.Collections.Generic.List<Exception>();
            InvokeProjectileObservers(ProjectileFired, context, projectilePosition, firedErrors);
            ThrowProjectileErrors(firedErrors);
        }

        private void AdvanceProjectile(float deltaTime)
        {
            var context = AttackState.CurrentContext;
            var travelledDistance = Vector2.Distance(context.Origin, projectilePosition);
            var remainingDistance = context.Range - travelledDistance;
            if (remainingDistance <= 0f)
            {
                StopProjectile(context);
                return;
            }

            var requestedDistance = Mathf.Min(RuntimeStats.ProjectileSpeed * deltaTime, remainingDistance);
            if (requestedDistance <= 0f)
            {
                return;
            }

            var travelDistance = requestedDistance;
            var stoppedByWall = TryGetWallDistance(
                projectilePosition,
                context.Width * 0.5f,
                context.NormalizedDirection,
                requestedDistance,
                out var wallDistance);

            if (stoppedByWall)
            {
                travelDistance = Mathf.Max(0f, wallDistance - WallStopOffset);
            }

            ApplyDamageAlongSweep(projectilePosition, context, travelDistance);
            projectilePosition += context.NormalizedDirection * travelDistance;
            var observerErrors = new System.Collections.Generic.List<Exception>();
            InvokeProjectileObservers(ProjectileMoved, context, projectilePosition, observerErrors);

            if (stoppedByWall || travelledDistance + travelDistance >= context.Range)
            {
                try
                {
                    StopProjectile(context);
                }
                catch (Exception exception)
                {
                    observerErrors.Add(exception);
                }
            }

            ThrowProjectileErrors(observerErrors);
        }

        private void CompleteShotRecoveryWhenReady()
        {
            if (Time.time < recoveryEndsAt)
            {
                return;
            }

            CompleteAttackRecovery();
            nextAttackAt = Time.time + RuntimeStats.AttackCooldown;
        }

        private void MaintainDistance(Vector2 targetPosition, float deltaTime)
        {
            var currentPosition = (Vector2)transform.position;
            var offsetFromTarget = currentPosition - targetPosition;
            var currentDistance = offsetFromTarget.magnitude;
            var preferredDistance = RuntimeStats.PreferredDistance;
            var moveDistance = RuntimeStats.MovementSpeed * deltaTime;

            if (currentDistance > preferredDistance + DistanceTolerance)
            {
                MoveTowards(targetPosition, moveDistance);
            }
            else if (currentDistance < preferredDistance - DistanceTolerance)
            {
                if (currentDistance > MinimumDirectionSqrMagnitude)
                {
                    lastSeparationDirection = offsetFromTarget / currentDistance;
                }

                MoveInDirection(lastSeparationDirection, moveDistance);
            }
        }

        private void StopProjectile(AttackContext context)
        {
            TerminateProjectile(true);
        }

        private void TerminateProjectile(bool beginRecovery)
        {
            if (!projectileActive)
            {
                activeProjectileContext = null;
                return;
            }

            var context = activeProjectileContext;
            projectileActive = false;
            activeProjectileContext = null;

            Exception failure = null;
            if (beginRecovery)
            {
                try
                {
                    BeginAttackRecovery();
                    recoveryEndsAt = Time.time + RuntimeStats.RecoveryDuration;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            var stopErrors = new System.Collections.Generic.List<Exception>();
            InvokeProjectileObservers(ProjectileStopped, context, projectilePosition, stopErrors);
            if (failure != null)
            {
                stopErrors.Insert(0, failure);
            }

            ThrowProjectileErrors(stopErrors);

        }

        private static void InvokeProjectileObservers(
            Action<AttackContext, Vector2> observers,
            AttackContext context,
            Vector2 position,
            System.Collections.Generic.List<Exception> errors)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<AttackContext, Vector2> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(context, position);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }

        private static void ThrowProjectileErrors(System.Collections.Generic.List<Exception> errors)
        {
            if (errors.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }

            if (errors.Count > 1)
            {
                throw new AggregateException("Projectile observers failed.", errors);
            }
        }
        private bool TryGetWallDistance(
            Vector2 origin,
            float radius,
            Vector2 direction,
            float distance,
            out float nearestWallDistance)
        {
            nearestWallDistance = distance;
            var hits = Physics2D.CircleCastAll(origin, radius, direction, distance, Definition.WorldCollisionMask);
            var foundWall = false;

            foreach (var hit in hits)
            {
                if (hit.collider == null || IsOwnerCollider(hit.collider))
                {
                    continue;
                }

                if (!foundWall || hit.distance < nearestWallDistance)
                {
                    nearestWallDistance = hit.distance;
                    foundWall = true;
                }
            }

            return foundWall;
        }

        private void ApplyDamageAlongSweep(Vector2 origin, AttackContext context, float distance)
        {
            var hits = Physics2D.CircleCastAll(
                origin,
                context.Width * 0.5f,
                context.NormalizedDirection,
                distance,
                context.TargetMask);

            foreach (var hit in hits)
            {
                TryApplyAttackDamage(context, hit.collider);
            }
        }

        private bool IsOwnerCollider(Collider2D collider)
        {
            var colliderHealth = collider.GetComponentInParent<Health>();
            return colliderHealth != null && colliderHealth.EntityId == EntityId;
        }
    }
}
