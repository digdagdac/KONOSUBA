using System;

namespace Overbless.Runtime
{
    public interface IDamageSource
    {
        int EntityId { get; }
    }

    public interface IDamageable
    {
        int EntityId { get; }
        bool IsDead { get; }
        bool TryApplyDamage(in DamageEvent damageEvent);
    }

    public readonly struct DamageEvent
    {
        public DamageEvent(long attackInstanceId, int attackerEntityId, int targetEntityId, int damage)
        {
            if (attackInstanceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackInstanceId), attackInstanceId, "Attack instance IDs must be positive.");
            }

            if (attackerEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackerEntityId), attackerEntityId, "Attacker entity IDs must be non-zero.");
            }

            if (targetEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetEntityId), targetEntityId, "Target entity IDs must be non-zero.");
            }

            if (damage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage, "Damage must be positive.");
            }

            AttackInstanceId = attackInstanceId;
            AttackerEntityId = attackerEntityId;
            TargetEntityId = targetEntityId;
            Damage = damage;
        }

        public long AttackInstanceId { get; }
        public int AttackerEntityId { get; }
        public int TargetEntityId { get; }
        public int Damage { get; }

        internal void Validate()
        {
            if (AttackInstanceId <= 0)
            {
                throw new InvalidOperationException("Damage events require a positive attack instance ID.");
            }

            if (AttackerEntityId == 0)
            {
                throw new InvalidOperationException("Damage events require a non-zero attacker entity ID.");
            }

            if (TargetEntityId == 0)
            {
                throw new InvalidOperationException("Damage events require a non-zero target entity ID.");
            }

            if (Damage <= 0)
            {
                throw new InvalidOperationException("Damage events require positive damage.");
            }
        }
    }

    public readonly struct DeathEvent
    {
        public DeathEvent(int entityId, long deathToken, DamageEvent damageEvent)
        {
            if (entityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Entity IDs must be non-zero.");
            }

            if (deathToken <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deathToken), deathToken, "Death tokens must be positive.");
            }

            damageEvent.Validate();
            if (entityId != damageEvent.TargetEntityId)
            {
                throw new ArgumentException("Death event entity ID must match the damage event target entity ID.", nameof(damageEvent));
            }

            EntityId = entityId;
            DeathToken = deathToken;
            DamageEvent = damageEvent;
        }

        public int EntityId { get; }
        public long DeathToken { get; }
        public DamageEvent DamageEvent { get; }
    }
}
