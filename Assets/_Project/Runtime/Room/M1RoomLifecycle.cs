using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public sealed class RoomResetNotificationException : AggregateException
    {
        internal RoomResetNotificationException(IReadOnlyList<Exception> failures)
            : base("Room reset committed, but one or more room observers failed.", failures)
        {
        }
    }

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
        private long roomGeneration;

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

        private void OnDestroy()
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
                throw new InvalidOperationException("M1 room reset is already in progress.");
            }

            var resetGeneration = AdvanceRoomGeneration();
            isResetting = true;
            isResetStateValid = false;
            var coreFailures = new List<Exception>();
            var notificationFailures = new List<Exception>();

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
                    coreFailures.Add(exception);
                }

                var resetStateRestored = ResetRoomState(
                    coreFailures,
                    notificationFailures,
                    resetGeneration);
                isResetStateValid = enemyDeathsUnsubscribed && resetStateRestored;

                if (isResetStateValid)
                {
                    try
                    {
                        SubscribeToEnemyDeaths();
                    }
                    catch (Exception exception)
                    {
                        coreFailures.Add(exception);
                        isResetStateValid = false;
                    }
                }
            }
            finally
            {
                isResetting = false;
            }

            ThrowFailures("M1 room reset failed.", coreFailures);
            if (notificationFailures.Count != 0)
            {
                throw new RoomResetNotificationException(notificationFailures.AsReadOnly());
            }
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
                if (enemyHealths[index] != null)
                {
                    enemyHealths[index].Died -= HandleEnemyDeath;
                }
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

            if (!enemyHealth.IsDead ||
                enemyHealth.DeathToken != deathEvent.DeathToken)
            {
                return;
            }

            var eventGeneration = roomGeneration;
            var deathToken = new DeathTokenKey(deathEvent.EntityId, deathEvent.DeathToken);
            if (processedDeathTokens.Contains(deathToken))
            {
                return;
            }

            SoulFragment soul = null;
            if (spawnedSouls.Count < M1RoomDefinition.RequiredSoulCount)
            {
                soul = SpawnSoul(enemyHealth.transform.position);
            }

            processedDeathTokens.Add(deathToken);

            var observerErrors = new List<Exception>();
            if ((soul != null && !InvokeObservers(SoulSpawned, soul, eventGeneration, observerErrors)) ||
                !InvokeObservers(SealReturnRequested, deathEvent, eventGeneration, observerErrors) ||
                !InvokeObservers(EnemyDeathProcessed, deathEvent, eventGeneration, observerErrors))
            {
                ThrowObserverErrors(observerErrors);
                return;
            }

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

            var eventGeneration = roomGeneration;
            var failures = new List<Exception>();
            TryOpenPendingExit(failures);
            if (roomGeneration == eventGeneration)
            {
                NotifyExitOpened(failures, eventGeneration);
            }

            ThrowFailures("M1 exit open retry failed.", failures);
        }

        private void HandleSoulCollected(SoulFragment soul)
        {
            if (!isResetStateValid || isResetting)
            {
                return;
            }

            var eventGeneration = roomGeneration;
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
            if (roomGeneration != eventGeneration)
            {
                ThrowFailures("M1 soul collection failed.", failures);
                return;
            }

            if (!InvokeObservers(SoulCountChanged, soulCount, eventGeneration, failures))
            {
                ThrowFailures("M1 soul collection failed.", failures);
                return;
            }

            TryOpenPendingExit(failures);
            if (roomGeneration == eventGeneration)
            {
                NotifyExitOpened(failures, eventGeneration);
            }

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

        private void NotifyExitOpened(List<Exception> failures, long expectedGeneration)
        {
            if (!exitGate.IsOpen || hasNotifiedExitOpened || roomGeneration != expectedGeneration)
            {
                return;
            }

            hasNotifiedExitOpened = true;
            InvokeObservers(ExitOpened, expectedGeneration, failures);
        }

        private bool ResetRoomState(
            List<Exception> coreFailures,
            List<Exception> notificationFailures,
            long resetGeneration)
        {
            var initialFailureCount = coreFailures.Count;
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

            var restored = coreFailures.Count == initialFailureCount;
            if (restored)
            {
                InvokeObservers(SoulCountChanged, soulCount, resetGeneration, notificationFailures);
            }

            return restored;
        }

        private bool InvokeObservers<T>(
            Action<T> observers,
            T value,
            long expectedGeneration,
            List<Exception> errors)
        {
            if (observers == null)
            {
                return roomGeneration == expectedGeneration;
            }

            foreach (Action<T> observer in observers.GetInvocationList())
            {
                if (roomGeneration != expectedGeneration)
                {
                    return false;
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

            return roomGeneration == expectedGeneration;
        }

        private bool InvokeObservers(
            Action observers,
            long expectedGeneration,
            List<Exception> errors)
        {
            if (observers == null)
            {
                return roomGeneration == expectedGeneration;
            }

            foreach (Action observer in observers.GetInvocationList())
            {
                if (roomGeneration != expectedGeneration)
                {
                    return false;
                }

                try
                {
                    observer();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            return roomGeneration == expectedGeneration;
        }

        private long AdvanceRoomGeneration()
        {
            if (roomGeneration == long.MaxValue)
            {
                throw new InvalidOperationException("M1 room generation overflowed.");
            }

            roomGeneration++;
            return roomGeneration;
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
