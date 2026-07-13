using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class M1RoomLifecycle : MonoBehaviour
    {
        [SerializeField] private M1RoomDefinition definition;
        [SerializeField] private Health[] enemyHealths;
        [SerializeField] private SoulFragment soulFragmentPrefab;
        [SerializeField] private Transform soulParent;
        [SerializeField] private ExitGate exitGate;
        [SerializeField] private BlessingTargeting blessingTargeting;

        private readonly Dictionary<int, Health> enemiesByEntityId = new Dictionary<int, Health>();
        private readonly HashSet<DeathTokenKey> processedDeathTokens = new HashSet<DeathTokenKey>();
        private readonly List<SoulFragment> spawnedSouls = new List<SoulFragment>();
        private readonly HashSet<SoulFragment> collectedSouls = new HashSet<SoulFragment>();

        private DamageLedger damageLedger;
        private bool isInitialized;
        private bool isSubscribed;
        private bool isResetting;
        private bool isResetStateValid;
        private bool isExitOpenPending;
        private bool isOpeningExit;
        private bool hasNotifiedExitOpened;
        private int soulCount;

        public event Action<DeathEvent> SealReturnRequested;
        public event Action<DeathEvent> EnemyDeathProcessed;
        public event Action<SoulFragment> SoulSpawned;
        public event Action<int> SoulCountChanged;
        public event Action ExitOpened;

        public int DamageLedgerCount
        {
            get
            {
                EnsureInitialized();
                return damageLedger.Count;
            }
        }

        public int SoulCount => soulCount;
        public bool IsExitOpenPending => isExitOpenPending;
        public bool IsExitOpen
        {
            get
            {
                EnsureInitialized();
                return exitGate.IsOpen;
            }
        }
        public bool OwnsEnemyHealth(Health health)
        {
            EnsureInitialized();
            return health != null && enemiesByEntityId.TryGetValue(health.EntityId, out var configuredHealth) &&
                   configuredHealth == health;
        }

        public bool UsesBlessingTargeting(BlessingTargeting targeting)
        {
            EnsureInitialized();
            return blessingTargeting == targeting;
        }

        private void Awake()
        {
            ResetForRoom();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (isResetStateValid)
            {
                SubscribeToEnemyDeaths();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromEnemyDeaths();
        }

        public bool TryApplyDamage(IDamageable target, in DamageEvent damageEvent)
        {
            EnsureInitialized();
            return isResetStateValid && !isResetting && damageLedger.TryApply(target, damageEvent);
        }

        public void ResetForRoom()
        {
            EnsureInitialized();
            if (isResetting)
            {
                return;
            }

            isResetting = true;
            isResetStateValid = false;
            var failures = new List<Exception>();

            try
            {
                var enemyDeathsUnsubscribed = false;
                try
                {
                    UnsubscribeFromEnemyDeaths();
                    enemyDeathsUnsubscribed = !isSubscribed;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                var resetStateRestored = ResetRoomState(failures);
                isResetStateValid = enemyDeathsUnsubscribed && resetStateRestored;

                if (isResetStateValid && isActiveAndEnabled)
                {
                    try
                    {
                        SubscribeToEnemyDeaths();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                        isResetStateValid = false;
                    }
                }
            }
            finally
            {
                isResetting = false;
            }

            ThrowFailures("M1 room reset failed.", failures);
        }

        private void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            ValidateConfiguration();
            damageLedger = new DamageLedger();
            isInitialized = true;
        }

        private void SubscribeToEnemyDeaths()
        {
            if (!isResetStateValid || isSubscribed)
            {
                return;
            }

            for (var index = 0; index < enemyHealths.Length; index++)
            {
                enemyHealths[index].Died += HandleEnemyDeath;
            }

            isSubscribed = true;
        }

        private void UnsubscribeFromEnemyDeaths()
        {
            if (!isSubscribed)
            {
                return;
            }

            for (var index = 0; index < enemyHealths.Length; index++)
            {
                enemyHealths[index].Died -= HandleEnemyDeath;
            }

            isSubscribed = false;
        }

        private void HandleEnemyDeath(DeathEvent deathEvent)
        {
            if (!isResetStateValid || isResetting)
            {
                return;
            }
            if (!enemiesByEntityId.TryGetValue(deathEvent.EntityId, out var enemyHealth))
            {
                throw new InvalidOperationException($"M1 received a death event from unwired entity {deathEvent.EntityId}.");
            }

            var deathToken = new DeathTokenKey(deathEvent.EntityId, deathEvent.DeathToken);
            if (processedDeathTokens.Contains(deathToken))
            {
                return;
            }

            var soul = SpawnSoul(enemyHealth.transform.position);
            processedDeathTokens.Add(deathToken);

            var observerErrors = new List<Exception>();
            InvokeObservers(SoulSpawned, soul, observerErrors);
            InvokeObservers(SealReturnRequested, deathEvent, observerErrors);
            InvokeObservers(EnemyDeathProcessed, deathEvent, observerErrors);
            ThrowObserverErrors(observerErrors);
        }

        private SoulFragment SpawnSoul(Vector3 position)
        {
            var soul = Instantiate(soulFragmentPrefab, position, Quaternion.identity, soulParent);
            soul.Initialize(HandleSoulCollected);
            spawnedSouls.Add(soul);
            return soul;
        }

        public void RetryPendingExitOpen()
        {
            EnsureInitialized();
            if (!isResetStateValid || !isExitOpenPending || isOpeningExit)
            {
                return;
            }

            var failures = new List<Exception>();
            TryOpenPendingExit(failures);
            NotifyExitOpened(failures);
            ThrowFailures("M1 exit open retry failed.", failures);
        }

        private void HandleSoulCollected(SoulFragment soul)
        {
            if (!isResetStateValid || isResetting)
            {
                return;
            }

            if (!spawnedSouls.Contains(soul))
            {
                throw new InvalidOperationException("M1 received a collection event from an unowned soul fragment.");
            }

            if (!collectedSouls.Add(soul))
            {
                return;
            }

            soulCount++;
            var failures = new List<Exception>();
            if (soulCount >= definition.SoulsRequiredForExit && !exitGate.IsOpen)
            {
                isExitOpenPending = true;
            }

            TryOpenPendingExit(failures);
            InvokeObservers(SoulCountChanged, soulCount, failures);
            TryOpenPendingExit(failures);
            NotifyExitOpened(failures);
            ThrowFailures("M1 soul collection failed.", failures);
        }

        private void TryOpenPendingExit(List<Exception> failures)
        {
            if (!isExitOpenPending || isOpeningExit)
            {
                return;
            }

            isOpeningExit = true;
            try
            {
                try
                {
                    exitGate.Open();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                if (!exitGate.IsOpen)
                {
                    return;
                }

                isExitOpenPending = false;
            }
            finally
            {
                isOpeningExit = false;
            }
        }

        private void NotifyExitOpened(List<Exception> failures)
        {
            if (!exitGate.IsOpen || hasNotifiedExitOpened)
            {
                return;
            }

            hasNotifiedExitOpened = true;
            InvokeObservers(ExitOpened, failures);
        }

        private bool ResetRoomState(List<Exception> failures)
        {
            var coreFailures = new List<Exception>();
            for (var index = 0; index < spawnedSouls.Count; index++)
            {
                var soul = spawnedSouls[index];
                if (soul == null)
                {
                    continue;
                }

                try
                {
                    soul.Release();
                }
                catch (Exception exception)
                {
                    coreFailures.Add(exception);
                }

                try
                {
                    Destroy(soul.gameObject);
                }
                catch (Exception exception)
                {
                    coreFailures.Add(exception);
                }
            }

            spawnedSouls.Clear();
            collectedSouls.Clear();
            processedDeathTokens.Clear();

            try
            {
                damageLedger.Clear();
            }
            catch (Exception exception)
            {
                coreFailures.Add(exception);
            }

            soulCount = 0;
            isExitOpenPending = false;
            hasNotifiedExitOpened = false;

            try
            {
                exitGate.ResetGate();
            }
            catch (Exception exception)
            {
                coreFailures.Add(exception);
            }

            failures.AddRange(coreFailures);
            InvokeObservers(SoulCountChanged, soulCount, failures);
            return coreFailures.Count == 0;
        }

        private static void InvokeObservers<T>(Action<T> observers, T value, List<Exception> errors)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action<T> observer in observers.GetInvocationList())
            {
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

        private static void InvokeObservers(Action observers, List<Exception> errors)
        {
            if (observers == null)
            {
                return;
            }

            foreach (Action observer in observers.GetInvocationList())
            {
                try
                {
                    observer();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }

        private static void ThrowObserverErrors(List<Exception> errors)
        {
            if (errors.Count > 0)
            {
                throw new AggregateException("One or more room lifecycle observers failed after the state transition committed.", errors);
            }
        }
        private static void ThrowFailures(string operation, List<Exception> failures)
        {
            if (failures.Count > 0)
            {
                throw new AggregateException(operation, failures);
            }
        }
        private void ValidateConfiguration()
        {
            if (definition == null)
            {
                throw new InvalidOperationException("M1RoomLifecycle requires an M1 room definition.");
            }

            definition.Validate();

            if (enemyHealths == null || enemyHealths.Length != 5)
            {
                throw new InvalidOperationException("M1RoomLifecycle requires exactly five enemy Health references.");
            }

            enemiesByEntityId.Clear();
            for (var index = 0; index < enemyHealths.Length; index++)
            {
                var health = enemyHealths[index];
                if (health == null)
                {
                    throw new InvalidOperationException("M1RoomLifecycle has an unassigned enemy Health reference.");
                }

                if (health.EntityId == 0 || !enemiesByEntityId.TryAdd(health.EntityId, health))
                {
                    throw new InvalidOperationException("M1RoomLifecycle enemy Health references require unique non-zero entity IDs.");
                }
            }

            if (soulFragmentPrefab == null)
            {
                throw new InvalidOperationException("M1RoomLifecycle requires a SoulFragment prefab.");
            }

            if (soulParent == null)
            {
                throw new InvalidOperationException("M1RoomLifecycle requires a soul parent transform.");
            }

            if (exitGate == null)
            {
                throw new InvalidOperationException("M1RoomLifecycle requires an ExitGate.");
            }

            if (blessingTargeting == null)
            {
                throw new InvalidOperationException("M1RoomLifecycle requires a BlessingTargeting controller for seal returns.");
            }
        }

        private readonly struct DeathTokenKey : IEquatable<DeathTokenKey>
        {
            private readonly int entityId;
            private readonly long deathToken;

            public DeathTokenKey(int entityId, long deathToken)
            {
                this.entityId = entityId;
                this.deathToken = deathToken;
            }

            public bool Equals(DeathTokenKey other)
            {
                return entityId == other.entityId && deathToken == other.deathToken;
            }

            public override bool Equals(object obj)
            {
                return obj is DeathTokenKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (entityId * 397) ^ deathToken.GetHashCode();
                }
            }
        }
    }
}
