using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public sealed class MinionAI : EnemyBase
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;
        // Match the authored AttackExecute clip (5 frames @ 14 fps ≈ 0.357s) so the
        // full strike animation is visible before recovery.
        internal const float ExecuteDuration = 5f / 14f;

        private float nextAttackEligibleAt;
        private float executeEndsAt;
        private float recoveryEndsAt;
        private bool damageApplied;
        private readonly List<Collider2D> attackOverlapResults = new List<Collider2D>();

        protected override void TickBehavior(float deltaTime)
        {
            switch (CurrentAttackPhase)
            {
                case AttackPhase.Idle:
                    TickIdle(deltaTime);
                    break;
                case AttackPhase.Warning:
                    TickWarning(deltaTime);
                    break;
                case AttackPhase.Executing:
                    TickExecuting();
                    break;
                case AttackPhase.Recovery:
                    TickRecovery();
                    break;
            }
        }

        protected override void OnDisable()
        {
            try
            {
                base.OnDisable();
            }
            finally
            {
                ResetAttackTiming();
            }
        }

        protected override void OnEnemyDied(DeathEvent deathEvent)
        {
            ResetAttackTiming();
        }

        protected override void OnRestarted()
        {
            ResetAttackTiming();
        }

        private void TickIdle(float deltaTime)
        {
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                SetLocomotionMode(LocomotionMode.Idle);
                return;
            }

            var offset = targetPosition - (Vector2)transform.position;
            if (offset.sqrMagnitude >= MinimumDirectionSqrMagnitude)
            {
                SetIntendedFacing(offset);
            }

            if (offset.sqrMagnitude > RuntimeStats.AttackRange * RuntimeStats.AttackRange)
            {
                SetLocomotionMode(LocomotionMode.Run);
                MoveTowards(targetPosition, RuntimeStats.RunSpeed * deltaTime);
                return;
            }

            SetLocomotionMode(LocomotionMode.Idle);
            if (Time.time >= nextAttackEligibleAt)
            {
                BeginAttackWarning(RuntimeStats.WarningDuration);
            }
        }

        private void TickWarning(float deltaTime)
        {
            SetLocomotionMode(LocomotionMode.Idle);
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                CancelAttack();
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
                direction = IntendedFacing;
            }

            LockAttack(
                direction,
                AttackShape.Circle,
                RuntimeStats.AttackRange,
                RuntimeStats.AttackWidth,
                RuntimeStats.AttackDamage,
                Definition.DamageTargetMask);
            damageApplied = false;
            var judgmentAt = Time.time;
            executeEndsAt = judgmentAt + ExecuteDuration;
            nextAttackEligibleAt =
                judgmentAt + RuntimeStats.RecoveryDuration + RuntimeStats.AttackCooldown;
            BeginAttackExecution();
            ApplyAttackDamageOnce();
        }

        private void TickExecuting()
        {
            SetLocomotionMode(LocomotionMode.Idle);
            ApplyAttackDamageOnce();
            if (Time.time < executeEndsAt)
            {
                return;
            }

            BeginAttackRecovery();
            recoveryEndsAt = Time.time + RuntimeStats.RecoveryDuration;
        }

        private void ApplyAttackDamageOnce()
        {
            if (damageApplied)
            {
                return;
            }

            var context = AttackState.CurrentContext;
            var center = context.Origin + (context.NormalizedDirection * (context.Range * 0.5f));
            var radius = Mathf.Max(context.Width * 0.5f, context.Range * 0.5f);
            attackOverlapResults.Clear();
            var filter = new ContactFilter2D();
            filter.SetLayerMask(context.TargetMask);
            filter.useTriggers = true;
            Physics2D.OverlapCircle(center, radius, filter, attackOverlapResults);

            try
            {
                for (var index = 0; index < attackOverlapResults.Count; index++)
                {
                    TryApplyAttackDamage(context, attackOverlapResults[index]);
                }
            }
            finally
            {
                attackOverlapResults.Clear();
            }

            damageApplied = true;
        }

        private void TickRecovery()
        {
            SetLocomotionMode(LocomotionMode.Idle);
            if (Time.time < recoveryEndsAt)
            {
                return;
            }

            CompleteAttackRecovery();
        }

        private void ResetAttackTiming()
        {
            nextAttackEligibleAt = 0f;
            executeEndsAt = 0f;
            recoveryEndsAt = 0f;
            damageApplied = false;
            attackOverlapResults.Clear();
        }
    }
}
