using UnityEngine;

namespace Overbless.Runtime
{
    public sealed class DasherAI : EnemyBase
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;
        private const float WallStopOffset = 0.001f;

        private float nextAttackAt;
        private float recoveryEndsAt;
        private float chargeDistanceTravelled;

        protected override void TickBehavior(float deltaTime)
        {
            switch (CurrentAttackPhase)
            {
                case AttackPhase.Idle:
                    SetLocomotionMode(LocomotionMode.Idle);
                    TryStartChargeWarning();
                    break;
                case AttackPhase.Warning:
                    SetLocomotionMode(LocomotionMode.Idle);
                    AdvanceChargeWarning(deltaTime);
                    break;
                case AttackPhase.Executing:
                    SetLocomotionMode(LocomotionMode.Run);
                    SweepCharge(deltaTime);
                    break;
                case AttackPhase.Recovery:
                    SetLocomotionMode(LocomotionMode.Idle);
                    CompleteChargeRecoveryWhenReady();
                    break;
            }
        }

        protected override void OnEnemyDied(DeathEvent deathEvent)
        {
            chargeDistanceTravelled = 0f;
        }

        protected override void OnRestarted()
        {
            nextAttackAt = 0f;
            recoveryEndsAt = 0f;
            chargeDistanceTravelled = 0f;
        }

        private void TryStartChargeWarning()
        {
            if (Time.time < nextAttackAt || !TryGetPlayerTargetPosition(out var targetPosition))
            {
                return;
            }

            var offset = targetPosition - (Vector2)transform.position;
            if (offset.sqrMagnitude > RuntimeStats.EngagementRange * RuntimeStats.EngagementRange)
            {
                return;
            }

            if (offset.sqrMagnitude >= MinimumDirectionSqrMagnitude)
            {
                SetIntendedFacing(offset);
            }

            BeginAttackWarning(RuntimeStats.WarningDuration);
        }

        private void AdvanceChargeWarning(float deltaTime)
        {
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                CancelAttack();
                SetLocomotionMode(LocomotionMode.Idle);
                return;
            }

            var direction = targetPosition - (Vector2)transform.position;
            if (direction.sqrMagnitude >= MinimumDirectionSqrMagnitude)
            {
                SetIntendedFacing(direction);
            }

            if (!AdvanceAttackWarning(deltaTime))
            {
                return;
            }

            if (direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
            {
                CancelAttack();
                SetLocomotionMode(LocomotionMode.Idle);
                return;
            }

            LockAttack(
                direction,
                AttackShape.Line,
                RuntimeStats.AttackRange,
                RuntimeStats.AttackWidth,
                RuntimeStats.AttackDamage,
                Definition.DamageTargetMask);
            chargeDistanceTravelled = 0f;
            BeginAttackExecution();
        }

        private void SweepCharge(float deltaTime)
        {
            var context = AttackState.CurrentContext;
            var remainingDistance = context.Range - chargeDistanceTravelled;
            if (remainingDistance <= 0f)
            {
                BeginChargeRecovery();
                return;
            }

            var requestedDistance = Mathf.Min(RuntimeStats.ChargeSpeed * deltaTime, remainingDistance);
            if (requestedDistance <= 0f)
            {
                return;
            }

            var origin = (Vector2)transform.position;
            var travelDistance = requestedDistance;
            var stoppedByWall = TryGetObstacleDistance(
                origin,
                context.Width * 0.5f,
                context.NormalizedDirection,
                requestedDistance,
                out var wallDistance);

            if (stoppedByWall)
            {
                travelDistance = Mathf.Max(0f, wallDistance - WallStopOffset);
            }

            SweepAttackDamage(origin, context, travelDistance, null);
            MoveInDirection(context.NormalizedDirection, travelDistance);
            chargeDistanceTravelled += travelDistance;

            if (stoppedByWall || chargeDistanceTravelled >= context.Range)
            {
                BeginChargeRecovery();
            }
        }

        private void CompleteChargeRecoveryWhenReady()
        {
            if (Time.time < recoveryEndsAt)
            {
                return;
            }

            CompleteAttackRecovery();
            SetLocomotionMode(LocomotionMode.Idle);
            nextAttackAt = Time.time + RuntimeStats.AttackCooldown;
        }

        private void BeginChargeRecovery()
        {
            BeginAttackRecovery();
            recoveryEndsAt = Time.time + RuntimeStats.RecoveryDuration;
        }

    }
}
