using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public readonly struct EnemyRuntimeStats
    {
        private EnemyRuntimeStats(
            int maximumHealth,
            float movementSpeed,
            float attackCooldown,
            float warningDuration,
            float recoveryDuration,
            int attackDamage,
            float engagementRange,
            float attackRange,
            float attackWidth,
            float chargeSpeed,
            float projectileSpeed,
            float preferredDistance,
            float attackSpeedMultiplier,
            float scaleMultiplier,
            float massMultiplier,
            bool hasHaste,
            bool hasGiant,
            bool hasEcho)
        {
            MaximumHealth = maximumHealth;
            MovementSpeed = movementSpeed;
            AttackCooldown = attackCooldown;
            WarningDuration = warningDuration;
            RecoveryDuration = recoveryDuration;
            AttackDamage = attackDamage;
            EngagementRange = engagementRange;
            AttackRange = attackRange;
            AttackWidth = attackWidth;
            ChargeSpeed = chargeSpeed;
            ProjectileSpeed = projectileSpeed;
            PreferredDistance = preferredDistance;
            AttackSpeedMultiplier = attackSpeedMultiplier;
            ScaleMultiplier = scaleMultiplier;
            MassMultiplier = massMultiplier;
            HasHaste = hasHaste;
            HasGiant = hasGiant;
            HasEcho = hasEcho;
        }

        public int MaximumHealth { get; }
        public float MovementSpeed { get; }
        public float AttackCooldown { get; }
        public float WarningDuration { get; }
        public float RecoveryDuration { get; }
        public int AttackDamage { get; }
        public float EngagementRange { get; }
        public float AttackRange { get; }
        public float AttackWidth { get; }
        public float ChargeSpeed { get; }
        public float ProjectileSpeed { get; }
        public float PreferredDistance { get; }
        public float AttackSpeedMultiplier { get; }
        public float ScaleMultiplier { get; }
        public float MassMultiplier { get; }
        public bool HasHaste { get; }
        public bool HasGiant { get; }
        public bool HasEcho { get; }

        public static EnemyRuntimeStats Recompute(
            EnemyDefinition definition,
            IReadOnlyCollection<BlessingType> activeBlessingIds)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (activeBlessingIds == null)
            {
                throw new ArgumentNullException(nameof(activeBlessingIds));
            }

            definition.ValidateConfiguration();

            var hasHaste = false;
            var hasGiant = false;
            var hasEcho = false;

            foreach (var blessingId in activeBlessingIds)
            {
                if (blessingId == BlessingType.Haste)
                {
                    hasHaste = true;
                }
                else if (blessingId == BlessingType.Giant)
                {
                    hasGiant = true;
                }
                else if (blessingId == BlessingType.Echo)
                {
                    hasEcho = true;
                }
            }

            var movementMultiplier = 1f;
            var attackSpeedMultiplier = 1f;
            var attackCooldownMultiplier = 1f;
            var projectileSpeedMultiplier = 1f;
            var healthMultiplier = 1f;
            var attackRangeMultiplier = 1f;
            var scaleMultiplier = 1f;
            var massMultiplier = 1f;

            if (hasHaste)
            {
                var haste = BlessingDefinition.Get(BlessingType.Haste);
                movementMultiplier *= haste.MovementSpeedMultiplier;
                attackSpeedMultiplier *= haste.AttackSpeedMultiplier;
                attackCooldownMultiplier *= haste.AttackCooldownMultiplier;
                projectileSpeedMultiplier *= haste.ProjectileSpeedMultiplier;
            }

            if (hasGiant)
            {
                var giant = BlessingDefinition.Get(BlessingType.Giant);
                healthMultiplier *= giant.MaximumHealthMultiplier;
                attackRangeMultiplier *= giant.AttackRangeMultiplier;
                scaleMultiplier *= giant.ScaleMultiplier;
                massMultiplier *= giant.MassMultiplier;
            }

            RequirePositiveFinite(healthMultiplier, nameof(healthMultiplier));
            var maximumHealth = ScaleMaximumHealth(definition.MaximumHealth, healthMultiplier);
            var movementSpeed = definition.MovementSpeed * movementMultiplier;
            var attackCooldown = definition.AttackCooldown * attackCooldownMultiplier;
            var warningDuration = Mathf.Max(
                definition.WarningDuration / attackSpeedMultiplier,
                AttackStateMachine.MinimumWarningDuration);
            var recoveryDuration = definition.RecoveryDuration;
            var engagementRange = definition.EngagementRange * attackRangeMultiplier;
            var attackRange = definition.AttackRange * attackRangeMultiplier;
            var attackWidth = definition.AttackWidth;
            var chargeSpeed = definition.ChargeSpeed * movementMultiplier;
            var projectileSpeed = definition.ProjectileSpeed * projectileSpeedMultiplier;
            var preferredDistance = definition.PreferredDistance;

            RequirePositiveFinite(movementSpeed, nameof(movementSpeed));
            RequirePositiveFinite(attackCooldown, nameof(attackCooldown));
            RequirePositiveFinite(warningDuration, nameof(warningDuration));
            RequireNonNegativeFinite(recoveryDuration, nameof(recoveryDuration));
            RequirePositiveFinite(engagementRange, nameof(engagementRange));
            RequirePositiveFinite(attackRange, nameof(attackRange));
            RequirePositiveFinite(attackWidth, nameof(attackWidth));
            RequirePositiveFinite(chargeSpeed, nameof(chargeSpeed));
            RequirePositiveFinite(projectileSpeed, nameof(projectileSpeed));
            RequireNonNegativeFinite(preferredDistance, nameof(preferredDistance));
            RequirePositiveFinite(attackSpeedMultiplier, nameof(attackSpeedMultiplier));
            RequirePositiveFinite(scaleMultiplier, nameof(scaleMultiplier));
            RequirePositiveFinite(massMultiplier, nameof(massMultiplier));

            return new EnemyRuntimeStats(
                maximumHealth,
                movementSpeed,
                attackCooldown,
                warningDuration,
                recoveryDuration,
                definition.AttackDamage,
                engagementRange,
                attackRange,
                attackWidth,
                chargeSpeed,
                projectileSpeed,
                preferredDistance,
                attackSpeedMultiplier,
                scaleMultiplier,
                massMultiplier,
                hasHaste,
                hasGiant,
                hasEcho);
        }

        private static void RequirePositiveFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new InvalidOperationException($"Derived enemy stat {name} must be finite and positive.");
            }
        }

        private static void RequireNonNegativeFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new InvalidOperationException($"Derived enemy stat {name} must be finite and non-negative.");
            }
        }
        private static int ScaleMaximumHealth(int maximumHealth, float multiplier)
        {
            var scaledMaximumHealth = (double)maximumHealth * multiplier;
            if (double.IsNaN(scaledMaximumHealth) || double.IsInfinity(scaledMaximumHealth) || scaledMaximumHealth <= 0d)
            {
                throw new InvalidOperationException("Derived maximum health must be finite and positive.");
            }

            return scaledMaximumHealth >= int.MaxValue
                ? int.MaxValue
                : checked((int)Math.Ceiling(scaledMaximumHealth));
        }
    }
}
