using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Overbless/Enemies/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [Header("Base")]
        [SerializeField, Min(1)] private int maximumHealth = 10;
        [SerializeField, Min(0.01f)] private float movementSpeed = 2f;
        [SerializeField, Min(0.01f)] private float attackCooldown = 2f;
        [SerializeField, Min(1)] private int attackDamage = 1;
        [SerializeField, Min(0.01f)] private float engagementRange = 6f;
        [SerializeField, Min(0.01f)] private float attackRange = 6f;
        [SerializeField, Min(0.01f)] private float attackWidth = 0.5f;
        [SerializeField, Min(0f)] private float warningDuration = AttackStateMachine.MinimumWarningDuration;
        [SerializeField, Min(0f)] private float recoveryDuration = 0.35f;

        [Header("Dasher")]
        [SerializeField, Min(0.01f)] private float chargeSpeed = 8f;

        [Header("Archer")]
        [SerializeField, Min(0.01f)] private float projectileSpeed = 10f;
        [SerializeField, Min(0f)] private float preferredDistance = 4f;

        [Header("Collision")]
        [SerializeField] private LayerMask damageTargetMask = (1 << 8) | (1 << 9);
        [SerializeField] private LayerMask worldCollisionMask = 1 << 12;


        public int MaximumHealth => maximumHealth;
        public float MovementSpeed => movementSpeed;
        public float AttackCooldown => attackCooldown;
        public int AttackDamage => attackDamage;
        public float EngagementRange => engagementRange;
        public float AttackRange => attackRange;
        public float AttackWidth => attackWidth;
        public float WarningDuration => warningDuration;
        public float RecoveryDuration => recoveryDuration;
        public float ChargeSpeed => chargeSpeed;
        public float ProjectileSpeed => projectileSpeed;
        public float PreferredDistance => preferredDistance;
        public LayerMask DamageTargetMask => damageTargetMask;
        public LayerMask WorldCollisionMask => worldCollisionMask;

        internal void ValidateConfiguration()
        {
            if (maximumHealth <= 0)
            {
                throw new InvalidOperationException("Enemy definitions require positive maximum health.");
            }

            RequirePositive(movementSpeed, nameof(movementSpeed));
            RequirePositive(attackCooldown, nameof(attackCooldown));

            if (attackDamage <= 0)
            {
                throw new InvalidOperationException("Enemy definitions require positive attack damage.");
            }

            RequirePositive(engagementRange, nameof(engagementRange));
            RequirePositive(attackRange, nameof(attackRange));
            RequirePositive(attackWidth, nameof(attackWidth));
            RequireNonNegative(warningDuration, nameof(warningDuration));
            RequireNonNegative(recoveryDuration, nameof(recoveryDuration));
            RequirePositive(chargeSpeed, nameof(chargeSpeed));
            RequirePositive(projectileSpeed, nameof(projectileSpeed));
            RequireNonNegative(preferredDistance, nameof(preferredDistance));

            if (preferredDistance > Mathf.Min(engagementRange, attackRange))
            {
                throw new InvalidOperationException("Archer preferred distance must be within both engagement and attack range.");
            }

            if (damageTargetMask.value == 0)
            {
                throw new InvalidOperationException("Enemy definitions require a non-empty damage target mask.");
            }

            if (worldCollisionMask.value == 0)
            {
                throw new InvalidOperationException("Enemy definitions require a non-empty world collision mask.");
            }
        }

        private static void RequirePositive(float value, string name)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new InvalidOperationException($"Enemy definition field {name} must be finite and positive.");
            }
        }

        private static void RequireNonNegative(float value, string name)
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new InvalidOperationException($"Enemy definition field {name} must be finite and non-negative.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
