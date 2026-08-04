using System;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum AttackShape
    {
        Line,
        Circle,
        Arc
    }

    public sealed class AttackContext
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;

        public AttackContext(
            long attackInstanceId,
            int attackerEntityId,
            float lockedAt,
            Vector2 origin,
            Vector2 direction,
            AttackShape shape,
            float range,
            float width,
            int damage,
            LayerMask targetMask)
        {
            if (attackInstanceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackInstanceId), attackInstanceId, "Attack instance IDs must be positive.");
            }

            if (attackerEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackerEntityId), attackerEntityId, "Attacker entity IDs must be non-zero.");
            }

            if (!IsFinite(lockedAt) || lockedAt < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(lockedAt), lockedAt, "Lock time must be finite and non-negative.");
            }

            if (!IsFinite(origin.x) || !IsFinite(origin.y))
            {
                throw new ArgumentOutOfRangeException(nameof(origin), origin, "Origin must have finite coordinates.");
            }

            if (!IsFinite(direction.x) || !IsFinite(direction.y))
            {
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Direction must have finite coordinates.");
            }

            var directionSqrMagnitude = direction.sqrMagnitude;
            if (!IsFinite(directionSqrMagnitude) || directionSqrMagnitude < MinimumDirectionSqrMagnitude)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Direction must have a non-zero magnitude.");
            }

            if (shape != AttackShape.Line && shape != AttackShape.Circle && shape != AttackShape.Arc)
            {
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Attack shape is invalid.");
            }

            if (!IsFinite(range) || range <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Range must be finite and positive.");
            }

            if (!IsFinite(width) || width <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be finite and positive.");
            }

            if (damage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage, "Damage must be positive.");
            }

            if (targetMask.value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetMask), targetMask, "Target mask cannot be empty.");
            }

            AttackInstanceId = attackInstanceId;
            AttackerEntityId = attackerEntityId;
            LockedAt = lockedAt;
            Origin = origin;
            NormalizedDirection = direction / Mathf.Sqrt(directionSqrMagnitude);
            Shape = shape;
            Range = range;
            Width = width;
            Damage = damage;
            TargetMask = targetMask;
        }

        public long AttackInstanceId { get; }
        public int AttackerEntityId { get; }
        public float LockedAt { get; }
        public Vector2 Origin { get; }
        public Vector2 NormalizedDirection { get; }
        public AttackShape Shape { get; }
        public float Range { get; }
        public float Width { get; }
        public int Damage { get; }
        public LayerMask TargetMask { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
