using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public sealed class MinionAI : EnemyBase
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;

        private float nextAttackAt;
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
                    ExecuteAttack();
                    break;
                case AttackPhase.Recovery:
                    TickRecovery();
                    break;
            }
        }

        protected override void OnEnemyDied(DeathEvent deathEvent)
        {
            damageApplied = false;
        }

        protected override void OnRestarted()
        {
            nextAttackAt = 0f;
            recoveryEndsAt = 0f;
            damageApplied = false;
        }

        private void TickIdle(float deltaTime)
        {
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                return;
            }

            var offset = targetPosition - (Vector2)transform.position;
            if (offset.sqrMagnitude > RuntimeStats.AttackRange * RuntimeStats.AttackRange)
            {
                MoveTowards(targetPosition, RuntimeStats.MovementSpeed * deltaTime);
                return;
            }

            if (Time.time >= nextAttackAt)
            {
                BeginAttackWarning(RuntimeStats.WarningDuration);
            }
        }

        private void TickWarning(float deltaTime)
        {
            if (!TryGetPlayerTargetPosition(out var targetPosition))
            {
                CancelAttack();
                return;
            }

            if (!AdvanceAttackWarning(deltaTime))
            {
                return;
            }

            var direction = targetPosition - (Vector2)transform.position;
            if (direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
            {
                direction = Vector2.down;
            }

            LockAttack(
                direction,
                AttackShape.Circle,
                RuntimeStats.AttackRange,
                RuntimeStats.AttackWidth,
                RuntimeStats.AttackDamage,
                Definition.DamageTargetMask);
            damageApplied = false;
            BeginAttackExecution();
        }

        private void ExecuteAttack()
        {
            if (!damageApplied)
            {
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

            BeginAttackRecovery();
            recoveryEndsAt = Time.time + RuntimeStats.RecoveryDuration;
        }

        private void TickRecovery()
        {
            if (Time.time < recoveryEndsAt)
            {
                return;
            }

            CompleteAttackRecovery();
            nextAttackAt = Time.time + RuntimeStats.AttackCooldown;
        }
    }
}
