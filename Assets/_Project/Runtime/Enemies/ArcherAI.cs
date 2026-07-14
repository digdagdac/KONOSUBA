using System;
using System.Collections.Generic;
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
        private long projectileGeneration;
        private AttackContext projectileStopNotificationContext;
        private bool echoBlessingActive;
        private bool echoPending;
        private float echoExecutionAt;
        private float echoProjectileSpeed;
        private Vector2 echoProjectilePosition;
        private AttackContext pendingEchoContext;
        private AttackContext echoSourceContext;
        private bool echoProjectileActive;
        private AttackContext activeEchoProjectileContext;
        private long echoProjectileGeneration;
        private AttackContext echoStopNotificationContext;
        private Vector2 lastSeparationDirection = Vector2.right;

        public event Action<AttackContext, Vector2> ProjectileFired;
        public event Action<AttackContext, Vector2> ProjectileMoved;
        public event Action<AttackContext, Vector2> ProjectileStopped;
        public event Action<AttackContext, Vector2> EchoProjectileFired;
        public event Action<AttackContext, Vector2> EchoProjectileMoved;
        public event Action<AttackContext, Vector2> EchoProjectileStopped;

        public bool IsProjectileActive => projectileActive;
        public Vector2 ProjectilePosition => projectilePosition;
        public AttackContext ProjectileContext => activeProjectileContext;
        public bool IsEchoPending => echoPending;
        public AttackContext PendingEchoContext => echoPending ? pendingEchoContext : null;
        public float PendingEchoExecutionAt => echoPending ? echoExecutionAt : 0f;
        public bool IsEchoProjectileActive => echoProjectileActive;
        public Vector2 EchoProjectilePosition => echoProjectilePosition;
        public AttackContext EchoProjectileContext => activeEchoProjectileContext;

        public override bool SupportsBehavioralBlessing(BlessingType type)
        {
            return type == BlessingType.Echo;
        }

        public override void ApplyBehavioralBlessings(IReadOnlyList<BlessingType> activeBlessings)
        {
            if (activeBlessings == null)
            {
                throw new ArgumentNullException(nameof(activeBlessings));
            }

            var nextEchoBlessingActive = false;
            for (var index = 0; index < activeBlessings.Count; index++)
            {
                if (activeBlessings[index] == BlessingType.Echo)
                {
                    nextEchoBlessingActive = true;
                    break;
                }
            }

            if (echoBlessingActive == nextEchoBlessingActive)
            {
                return;
            }

            echoBlessingActive = nextEchoBlessingActive;
            if (!echoBlessingActive)
            {
                var errors = new List<Exception>();
                CancelEcho(true, errors);
                ThrowProjectileErrors(errors);
            }
        }

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
                    AdvanceProjectiles(deltaTime);
                    break;

                case AttackPhase.Recovery:
                    CompleteShotRecoveryWhenReady();
                    break;
            }
        }

        protected override void OnDisable()
        {
            var errors = new List<Exception>();
            try
            {
                TerminateProjectile(false, errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                CancelEcho(false, errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                base.OnDisable();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            ThrowProjectileErrors(errors);
        }

        protected override void OnDestroy()
        {
            var errors = new List<Exception>();
            try
            {
                TerminateProjectile(false, errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                CancelEcho(false, errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                base.OnDestroy();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            ThrowProjectileErrors(errors);
        }

        protected override void OnEnemyDied(DeathEvent deathEvent)
        {
            var errors = new List<Exception>();
            TerminateProjectile(false, errors);
            CancelEcho(false, errors);
            ThrowProjectileErrors(errors);
        }

        protected override void OnRestarted()
        {
            var errors = new List<Exception>();
            TerminateProjectile(false, errors);
            CancelEcho(false, errors);
            nextAttackAt = 0f;
            recoveryEndsAt = 0f;
            projectilePosition = transform.position;
            echoProjectilePosition = transform.position;
            lastSeparationDirection = Vector2.right;
            ThrowProjectileErrors(errors);
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

            var errors = new List<Exception>();
            AttackContext context = null;
            try
            {
                context = LockAttack(
                    direction,
                    AttackShape.Line,
                    RuntimeStats.AttackRange,
                    RuntimeStats.AttackWidth,
                    RuntimeStats.AttackDamage,
                    Definition.DamageTargetMask);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
                if ((CurrentAttackPhase == AttackPhase.Locked ||
                     CurrentAttackPhase == AttackPhase.Executing) &&
                    AttackState.CurrentContext != null)
                {
                    context = AttackState.CurrentContext;
                }
                else
                {
                    try
                    {
                        CancelAttack();
                    }
                    catch (Exception cleanupException)
                    {
                        errors.Add(cleanupException);
                    }

                    ThrowProjectileErrors(errors);
                    return;
                }
            }

            projectilePosition = context.Origin;
            projectileActive = true;
            activeProjectileContext = context;
            var generation = ++projectileGeneration;

            if (echoBlessingActive)
            {
                try
                {
                    ScheduleEcho(
                        AttackState.CreateIndependentContextCopy(),
                        context,
                        RuntimeStats.ProjectileSpeed);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                    CancelEcho(false, errors);
                }
            }

            if (CurrentAttackPhase == AttackPhase.Locked)
            {
                try
                {
                    BeginAttackExecution();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (IsPrimaryProjectileCurrent(context, generation))
            {
                InvokeProjectileObservers(
                    ProjectileFired,
                    context,
                    projectilePosition,
                    errors,
                    () => IsPrimaryProjectileCurrent(context, generation));
            }

            if (!IsPrimaryProjectileCurrent(context, generation))
            {
                CancelEcho(false, errors);
                TerminateProjectile(false, errors);
            }

            ThrowProjectileErrors(errors);
        }

        private void AdvanceProjectiles(float deltaTime)
        {
            var errors = new List<Exception>();
            try
            {
                AdvanceProjectile(deltaTime);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            if (CurrentAttackPhase == AttackPhase.Executing)
            {
                try
                {
                    StartEchoProjectileWhenDue();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (CurrentAttackPhase == AttackPhase.Executing)
            {
                try
                {
                    AdvanceEchoProjectile(deltaTime);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            ThrowProjectileErrors(errors);
        }

        private void AdvanceProjectile(float deltaTime)
        {
            if (!projectileActive)
            {
                return;
            }

            var context = activeProjectileContext;
            var generation = projectileGeneration;
            if (!IsPrimaryProjectileCurrent(context, generation))
            {
                var inactiveErrors = new List<Exception>();
                TerminateProjectile(false, inactiveErrors);
                ThrowProjectileErrors(inactiveErrors);
                return;
            }

            var travelledDistance = Vector2.Distance(context.Origin, projectilePosition);
            var remainingDistance = context.Range - travelledDistance;
            if (remainingDistance <= 0f)
            {
                var completedErrors = new List<Exception>();
                TerminateProjectile(true, completedErrors);
                ThrowProjectileErrors(completedErrors);
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

            var errors = new List<Exception>();
            if (!ApplyDamageAlongSweep(
                    projectilePosition,
                    context,
                    travelDistance,
                    () => IsPrimaryProjectileCurrent(context, generation)))
            {
                TerminateProjectile(false, errors);
                ThrowProjectileErrors(errors);
                return;
            }

            projectilePosition += context.NormalizedDirection * travelDistance;
            InvokeProjectileObservers(
                ProjectileMoved,
                context,
                projectilePosition,
                errors,
                () => IsPrimaryProjectileCurrent(context, generation));

            if (!IsPrimaryProjectileCurrent(context, generation))
            {
                TerminateProjectile(false, errors);
            }
            else if (stoppedByWall || travelledDistance + travelDistance >= context.Range)
            {
                TerminateProjectile(true, errors);
            }

            ThrowProjectileErrors(errors);
        }

        private void StartEchoProjectileWhenDue()
        {
            if (!echoPending || Time.time < echoExecutionAt)
            {
                return;
            }

            if (CurrentAttackPhase != AttackPhase.Executing ||
                !ReferenceEquals(AttackState.CurrentContext, echoSourceContext))
            {
                var cancelledErrors = new List<Exception>();
                CancelEcho(false, cancelledErrors);
                ThrowProjectileErrors(cancelledErrors);
                return;
            }

            var context = pendingEchoContext;
            var sourceContext = echoSourceContext;
            var speed = echoProjectileSpeed;
            echoPending = false;
            pendingEchoContext = null;
            echoProjectilePosition = context.Origin;
            echoProjectileActive = true;
            activeEchoProjectileContext = context;
            var generation = ++echoProjectileGeneration;
            var errors = new List<Exception>();

            if (IsEchoProjectileCurrent(context, sourceContext, generation))
            {
                InvokeProjectileObservers(
                    EchoProjectileFired,
                    context,
                    echoProjectilePosition,
                    errors,
                    () => IsEchoProjectileCurrent(context, sourceContext, generation));
            }

            if (!IsEchoProjectileCurrent(context, sourceContext, generation))
            {
                TerminateEchoProjectile(false, errors);
            }
            else
            {
                echoProjectileSpeed = speed;
            }

            ThrowProjectileErrors(errors);
        }

        private void AdvanceEchoProjectile(float deltaTime)
        {
            if (!echoProjectileActive)
            {
                return;
            }

            var context = activeEchoProjectileContext;
            var sourceContext = echoSourceContext;
            var generation = echoProjectileGeneration;
            if (!IsEchoProjectileCurrent(context, sourceContext, generation))
            {
                var inactiveErrors = new List<Exception>();
                TerminateEchoProjectile(false, inactiveErrors);
                ThrowProjectileErrors(inactiveErrors);
                return;
            }

            var travelledDistance = Vector2.Distance(context.Origin, echoProjectilePosition);
            var remainingDistance = context.Range - travelledDistance;
            if (remainingDistance <= 0f)
            {
                var completedErrors = new List<Exception>();
                TerminateEchoProjectile(true, completedErrors);
                ThrowProjectileErrors(completedErrors);
                return;
            }

            var requestedDistance = Mathf.Min(echoProjectileSpeed * deltaTime, remainingDistance);
            if (requestedDistance <= 0f)
            {
                return;
            }

            var travelDistance = requestedDistance;
            var stoppedByWall = TryGetWallDistance(
                echoProjectilePosition,
                context.Width * 0.5f,
                context.NormalizedDirection,
                requestedDistance,
                out var wallDistance);
            if (stoppedByWall)
            {
                travelDistance = Mathf.Max(0f, wallDistance - WallStopOffset);
            }

            var errors = new List<Exception>();
            if (!ApplyDamageAlongSweep(
                    echoProjectilePosition,
                    context,
                    travelDistance,
                    () => IsEchoProjectileCurrent(context, sourceContext, generation)))
            {
                TerminateEchoProjectile(false, errors);
                ThrowProjectileErrors(errors);
                return;
            }

            echoProjectilePosition += context.NormalizedDirection * travelDistance;
            InvokeProjectileObservers(
                EchoProjectileMoved,
                context,
                echoProjectilePosition,
                errors,
                () => IsEchoProjectileCurrent(context, sourceContext, generation));

            if (!IsEchoProjectileCurrent(context, sourceContext, generation))
            {
                TerminateEchoProjectile(false, errors);
            }
            else if (stoppedByWall || travelledDistance + travelDistance >= context.Range)
            {
                TerminateEchoProjectile(true, errors);
            }

            ThrowProjectileErrors(errors);
        }

        private void CompleteShotRecoveryWhenReady()
        {
            if (Time.time < recoveryEndsAt)
            {
                return;
            }

            nextAttackAt = Time.time + RuntimeStats.AttackCooldown;
            CompleteAttackRecovery();
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

        private void ScheduleEcho(AttackContext echoContext, AttackContext sourceContext, float projectileSpeed)
        {
            echoPending = true;
            echoExecutionAt = Time.time + BlessingDefinition.EchoRepeatDelaySeconds;
            echoProjectileSpeed = projectileSpeed;
            pendingEchoContext = echoContext;
            echoSourceContext = sourceContext;
        }

        private void CancelEcho(bool allowRecovery, List<Exception> errors)
        {
            if (echoPending)
            {
                echoPending = false;
                echoExecutionAt = 0f;
                echoProjectileSpeed = 0f;
                pendingEchoContext = null;
                echoSourceContext = null;
                echoStopNotificationContext = null;
                ++echoProjectileGeneration;
            }

            if (echoProjectileActive)
            {
                TerminateEchoProjectile(allowRecovery, errors);
                return;
            }

            if (echoStopNotificationContext != null)
            {
                echoExecutionAt = 0f;
                echoProjectileSpeed = 0f;
                pendingEchoContext = null;
                echoSourceContext = null;
                echoStopNotificationContext = null;
                ++echoProjectileGeneration;
            }

            if (allowRecovery)
            {
                TryBeginAttackRecovery(errors);
            }
        }

        private void TerminateProjectile(bool allowRecovery, List<Exception> errors)
        {
            if (!projectileActive)
            {
                activeProjectileContext = null;
                projectileStopNotificationContext = null;
                ++projectileGeneration;
                return;
            }

            var context = activeProjectileContext;
            var position = projectilePosition;
            projectileActive = false;
            activeProjectileContext = null;
            projectileStopNotificationContext = context;
            var notificationGeneration = ++projectileGeneration;

            if (allowRecovery)
            {
                TryBeginAttackRecovery(errors);
            }

            InvokeProjectileObservers(
                ProjectileStopped,
                context,
                position,
                errors,
                () => IsPrimaryStopNotificationCurrent(context, notificationGeneration));
            if (projectileGeneration == notificationGeneration)
            {
                projectileStopNotificationContext = null;
            }
        }

        private void TerminateEchoProjectile(bool allowRecovery, List<Exception> errors)
        {
            if (!echoProjectileActive)
            {
                activeEchoProjectileContext = null;
                echoExecutionAt = 0f;
                echoProjectileSpeed = 0f;
                echoSourceContext = null;
                echoStopNotificationContext = null;
                ++echoProjectileGeneration;
                if (allowRecovery)
                {
                    TryBeginAttackRecovery(errors);
                }

                return;
            }

            var context = activeEchoProjectileContext;
            var position = echoProjectilePosition;
            echoProjectileActive = false;
            activeEchoProjectileContext = null;
            echoPending = false;
            echoExecutionAt = 0f;
            pendingEchoContext = null;
            echoProjectileSpeed = 0f;
            echoSourceContext = null;
            echoStopNotificationContext = context;
            var notificationGeneration = ++echoProjectileGeneration;

            if (allowRecovery)
            {
                TryBeginAttackRecovery(errors);
            }

            InvokeProjectileObservers(
                EchoProjectileStopped,
                context,
                position,
                errors,
                () => IsEchoStopNotificationCurrent(context, notificationGeneration));
            if (echoProjectileGeneration == notificationGeneration)
            {
                echoStopNotificationContext = null;
            }
        }

        private void TryBeginAttackRecovery(List<Exception> errors)
        {
            if (AttackState == null ||
                projectileActive ||
                echoPending ||
                echoProjectileActive ||
                CurrentAttackPhase != AttackPhase.Executing)
            {
                return;
            }

            try
            {
                BeginAttackRecovery();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            if (CurrentAttackPhase == AttackPhase.Recovery && AttackState.CurrentContext == null)
            {
                recoveryEndsAt = Time.time + RuntimeStats.RecoveryDuration;
            }
        }

        private bool IsPrimaryProjectileCurrent(AttackContext context, long generation)
        {
            return projectileActive &&
                   projectileGeneration == generation &&
                   ReferenceEquals(activeProjectileContext, context) &&
                   CurrentAttackPhase == AttackPhase.Executing &&
                   ReferenceEquals(AttackState.CurrentContext, context);
        }

        private bool IsEchoProjectileCurrent(AttackContext context, AttackContext sourceContext, long generation)
        {
            return echoProjectileActive &&
                   echoProjectileGeneration == generation &&
                   ReferenceEquals(activeEchoProjectileContext, context) &&
                   ReferenceEquals(echoSourceContext, sourceContext) &&
                   CurrentAttackPhase == AttackPhase.Executing &&
                   ReferenceEquals(AttackState.CurrentContext, sourceContext);
        }

        private bool IsPrimaryStopNotificationCurrent(AttackContext context, long notificationGeneration)
        {
            return !projectileActive &&
                   projectileGeneration == notificationGeneration &&
                   ReferenceEquals(projectileStopNotificationContext, context);
        }

        private bool IsEchoStopNotificationCurrent(AttackContext context, long notificationGeneration)
        {
            return !echoProjectileActive &&
                   echoProjectileGeneration == notificationGeneration &&
                   ReferenceEquals(echoStopNotificationContext, context);
        }

        private static void InvokeProjectileObservers(
            Action<AttackContext, Vector2> observers,
            AttackContext context,
            Vector2 position,
            List<Exception> errors,
            Func<bool> continueCondition)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<AttackContext, Vector2> observer in observers.GetInvocationList())
            {
                if (continueCondition != null && !continueCondition())
                {
                    break;
                }

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

        private static void ThrowProjectileErrors(List<Exception> errors)
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

        private bool ApplyDamageAlongSweep(
            Vector2 origin,
            AttackContext context,
            float distance,
            Func<bool> continueCondition)
        {
            var hits = Physics2D.CircleCastAll(
                origin,
                context.Width * 0.5f,
                context.NormalizedDirection,
                distance,
                context.TargetMask);

            foreach (var hit in hits)
            {
                if (!continueCondition())
                {
                    return false;
                }

                TryApplyAttackDamage(context, hit.collider);
            }

            return continueCondition();
        }

        private bool IsOwnerCollider(Collider2D collider)
        {
            var colliderHealth = collider.GetComponentInParent<Health>();
            return colliderHealth != null && colliderHealth.EntityId == EntityId;
        }
    }
}
