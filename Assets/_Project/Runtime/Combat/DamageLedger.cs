using System;
using System.Collections.Generic;

namespace Overbless.Runtime
{
    public sealed class DamageLedger
    {
        private readonly HashSet<DamageKey> acceptedDamage;

        public DamageLedger(int initialCapacity = 16)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), initialCapacity, "Initial capacity cannot be negative.");
            }

            acceptedDamage = new HashSet<DamageKey>(initialCapacity);
        }

        public int Count => acceptedDamage.Count;

        public bool TryRegister(in DamageEvent damageEvent)
        {
            damageEvent.Validate();

            if (damageEvent.AttackerEntityId == damageEvent.TargetEntityId)
            {
                return false;
            }

            return acceptedDamage.Add(new DamageKey(damageEvent.AttackInstanceId, damageEvent.TargetEntityId));
        }

        public bool TryApply(IDamageable target, in DamageEvent damageEvent)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            damageEvent.Validate();

            if (target.EntityId == 0)
            {
                throw new InvalidOperationException("Damageable targets require a non-zero entity ID.");
            }

            if (target.EntityId != damageEvent.TargetEntityId)
            {
                return false;
            }

            if (!TryRegister(damageEvent))
            {
                return false;
            }

            return target.TryApplyDamage(damageEvent);
        }

        public void Clear()
        {
            acceptedDamage.Clear();
        }

        private readonly struct DamageKey : IEquatable<DamageKey>
        {
            private readonly long attackInstanceId;
            private readonly int targetEntityId;

            public DamageKey(long attackInstanceId, int targetEntityId)
            {
                this.attackInstanceId = attackInstanceId;
                this.targetEntityId = targetEntityId;
            }

            public bool Equals(DamageKey other)
            {
                return attackInstanceId == other.attackInstanceId
                    && targetEntityId == other.targetEntityId;
            }

            public override bool Equals(object obj)
            {
                return obj is DamageKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (((int)attackInstanceId ^ (int)(attackInstanceId >> 32)) * 397) ^ targetEntityId;
                }
            }
        }
    }
}
