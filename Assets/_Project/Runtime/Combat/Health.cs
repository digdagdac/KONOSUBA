using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private int entityId;
        [SerializeField, Min(1)] private int maximumHealth = 1;
        [SerializeField] private bool startsInvulnerable;

        private readonly System.Collections.Generic.HashSet<object> invulnerabilitySources =
            new System.Collections.Generic.HashSet<object>();
        private int currentHealth;
        private bool manualInvulnerability;
        private bool isDead;
        private long deathToken;
        private long mutationGeneration;

        public event Action<DamageEvent> Damaged;
        public event Action<DeathEvent> Died;

        public int EntityId => entityId;
        public int MaximumHealth => maximumHealth;
        public int CurrentHealth => currentHealth;
        public bool IsInvulnerable => manualInvulnerability || invulnerabilitySources.Count > 0;
        public bool IsDead => isDead;
        public long DeathToken => deathToken;

        private void Awake()
        {
            ResetHealth();
        }

        public void SetInvulnerable(bool value)
        {
            manualInvulnerability = value;
        }

        public void SetInvulnerabilitySource(object source, bool active)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (active)
            {
                invulnerabilitySources.Add(source);
            }
            else
            {
                invulnerabilitySources.Remove(source);
            }
        }
        public void SetMaximumHealthPreservingRatio(int value)
        {
            if (maximumHealth <= 0)
            {
                throw new InvalidOperationException("Health requires positive maximum health.");
            }

            var ratio = (float)currentHealth / maximumHealth;
            SetMaximumHealthAndRatio(value, ratio);
        }

        public void SetMaximumHealthAndRatio(int value, float ratio)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum health must be positive.");
            }

            if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio < 0f || ratio > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Health ratio must be finite and within [0,1].");
            }

            maximumHealth = value;
            if (isDead)
            {
                currentHealth = 0;
                return;
            }

            currentHealth = Mathf.Clamp(Mathf.RoundToInt(maximumHealth * ratio), 1, maximumHealth);
        }

        public bool TryApplyDamage(in DamageEvent damageEvent)
        {
            damageEvent.Validate();

            if (damageEvent.TargetEntityId != entityId || damageEvent.AttackerEntityId == entityId)
            {
                return false;
            }

            if (isDead || IsInvulnerable)
            {
                return false;
            }

            var nextHealth = damageEvent.Damage >= currentHealth ? 0 : currentHealth - damageEvent.Damage;
            var diedFromDamage = nextHealth == 0;
            if (diedFromDamage && deathToken == long.MaxValue)
            {
                throw new InvalidOperationException("Death token overflowed.");
            }

            var publicationGeneration = AdvanceMutationGeneration();
            currentHealth = nextHealth;
            DeathEvent deathEvent = default;

            if (diedFromDamage)
            {
                isDead = true;
                deathToken++;
                deathEvent = new DeathEvent(entityId, deathToken, damageEvent);
            }

            var observerErrors = new System.Collections.Generic.List<Exception>();
            InvokeObservers(
                Damaged,
                damageEvent,
                observerErrors,
                () => mutationGeneration == publicationGeneration);

            if (diedFromDamage && IsCurrentDeath(publicationGeneration, deathEvent.DeathToken))
            {
                InvokeObservers(
                    Died,
                    deathEvent,
                    observerErrors,
                    () => IsCurrentDeath(publicationGeneration, deathEvent.DeathToken));
            }

            ThrowObserverErrors(observerErrors);
            return true;
        }

        private static void InvokeObservers<T>(
            Action<T> observers,
            T value,
            System.Collections.Generic.List<Exception> errors,
            Func<bool> continueCondition)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<T> observer in observers.GetInvocationList())
            {
                if (continueCondition != null && !continueCondition())
                {
                    break;
                }

                try
                {
                    observer(value);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }

        private static void ThrowObserverErrors(System.Collections.Generic.List<Exception> errors)
        {
            if (errors.Count == 1)
            {
                throw new InvalidOperationException("Health observer failed.", errors[0]);
            }

            if (errors.Count > 1)
            {
                throw new AggregateException("Health observers failed.", errors);
            }
        }
        public void ResetHealth()
        {
            ValidateConfiguration();
            AdvanceMutationGeneration();

            currentHealth = maximumHealth;
            manualInvulnerability = startsInvulnerable;
            invulnerabilitySources.Clear();
            isDead = false;
        }
        private long AdvanceMutationGeneration()
        {
            if (mutationGeneration == long.MaxValue)
            {
                throw new InvalidOperationException("Health mutation generation overflowed.");
            }

            mutationGeneration++;
            return mutationGeneration;
        }

        private bool IsCurrentDeath(long expectedGeneration, long expectedDeathToken)
        {
            return mutationGeneration == expectedGeneration &&
                   isDead &&
                   deathToken == expectedDeathToken;
        }

        private void ValidateConfiguration()
        {
            if (entityId == 0)
            {
                throw new InvalidOperationException("Health requires a non-zero stable entity ID.");
            }

            if (maximumHealth <= 0)
            {
                throw new InvalidOperationException("Health requires positive maximum health.");
            }
        }
    }
}
