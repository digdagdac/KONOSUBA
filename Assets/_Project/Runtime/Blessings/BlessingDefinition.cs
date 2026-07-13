using System;

namespace Overbless.Runtime
{
    public sealed class BlessingDefinition
    {
        public static readonly BlessingDefinition Haste = new BlessingDefinition(
            BlessingType.Haste,
            "Haste",
            1.5f,
            1.35f,
            0.75f,
            1.35f,
            1f,
            1f,
            1f,
            1f);

        public static readonly BlessingDefinition Giant = new BlessingDefinition(
            BlessingType.Giant,
            "Giant",
            1f,
            1f,
            1f,
            1f,
            1.35f,
            1.75f,
            1.4f,
            1.5f);

        private BlessingDefinition(
            BlessingType type,
            string id,
            float movementSpeedMultiplier,
            float attackSpeedMultiplier,
            float attackCooldownMultiplier,
            float projectileSpeedMultiplier,
            float scaleMultiplier,
            float maximumHealthMultiplier,
            float attackRangeMultiplier,
            float massMultiplier)
        {
            Type = type;
            Id = id;
            MovementSpeedMultiplier = movementSpeedMultiplier;
            AttackSpeedMultiplier = attackSpeedMultiplier;
            AttackCooldownMultiplier = attackCooldownMultiplier;
            ProjectileSpeedMultiplier = projectileSpeedMultiplier;
            ScaleMultiplier = scaleMultiplier;
            MaximumHealthMultiplier = maximumHealthMultiplier;
            AttackRangeMultiplier = attackRangeMultiplier;
            MassMultiplier = massMultiplier;
        }

        public BlessingType Type { get; }
        public string Id { get; }
        public float MovementSpeedMultiplier { get; }
        public float AttackSpeedMultiplier { get; }
        public float AttackCooldownMultiplier { get; }
        public float ProjectileSpeedMultiplier { get; }
        public float ScaleMultiplier { get; }
        public float MaximumHealthMultiplier { get; }
        public float AttackRangeMultiplier { get; }
        public float MassMultiplier { get; }

        public static BlessingDefinition Get(BlessingType type)
        {
            switch (type)
            {
                case BlessingType.Haste:
                    return Haste;
                case BlessingType.Giant:
                    return Giant;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Only implemented blessing types have definitions.");
            }
        }
    }
}
