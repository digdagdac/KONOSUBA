using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class RoomRestartController : MonoBehaviour
    {
        [SerializeField] private PlayerLifeCycle playerLifeCycle;
        [SerializeField] private EnemyBase[] enemies;
        [SerializeField] private BlessingTargeting blessingTargeting;
        [SerializeField] private M1RoomLifecycle roomLifecycle;

        private TransformSnapshot playerSnapshot;
        private TransformSnapshot[] enemySnapshots;
        private bool isInitialized;
        private bool isRestarting;

        public event Action Restarted;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            playerLifeCycle.InputRouter.RestartRequested += RestartRoom;
        }

        private void OnDisable()
        {
            if (isInitialized)
            {
                playerLifeCycle.InputRouter.RestartRequested -= RestartRoom;
            }
        }

        public void RestartRoom()
        {
            EnsureInitialized();
            if (isRestarting)
            {
                return;
            }

            isRestarting = true;
            var coreFailures = new List<Exception>();
            var notificationFailures = new List<Exception>();
            var coreCommitted = false;
            try
            {
                try
                {
                    playerLifeCycle.InputRouter.AcquireInputBlock(PlayerInputBlocker.RoomRestart);
                }
                catch (Exception exception)
                {
                    coreFailures.Add(exception);
                }

                if (coreFailures.Count == 0)
                {
                    try
                    {
                        playerLifeCycle.ResetPlayer();
                    }
                    catch (PlayerResetNotificationException exception)
                    {
                        notificationFailures.AddRange(exception.InnerExceptions);
                    }
                    catch (Exception exception)
                    {
                        coreFailures.Add(exception);
                    }

                    try
                    {
                        ResetRoomDependents(notificationFailures);
                    }
                    catch (Exception exception)
                    {
                        coreFailures.Add(exception);
                    }
                }

                if (coreFailures.Count == 0)
                {
                    try
                    {
                        playerLifeCycle.InputRouter.ReleaseInputBlock(PlayerInputBlocker.RoomRestart);
                        coreCommitted = true;
                    }
                    catch (Exception exception)
                    {
                        coreFailures.Add(exception);
                    }
                }

                if (coreCommitted)
                {
                    try
                    {
                        NotifyRestarted();
                    }
                    catch (Exception exception)
                    {
                        notificationFailures.Add(exception);
                    }
                }
                else
                {
                    try
                    {
                        playerLifeCycle.InputRouter.SetRestartInputEnabled(true);
                    }
                    catch (Exception exception)
                    {
                        coreFailures.Add(exception);
                    }
                }
            }
            finally
            {
                isRestarting = false;
            }

            ThrowFailures("Room restart failed.", coreFailures);
            ThrowFailures("Room restart committed, but observer notification failed.", notificationFailures);
        }

        private void ResetRoomDependents(List<Exception> notificationFailures)
        {
            var failures = new List<Exception>();
            for (var index = 0; index < enemies.Length; index++)
            {
                try
                {
                    enemies[index].ResetForRoom();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                blessingTargeting.HandleRoomRestart();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                RestoreInitialTransforms();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                roomLifecycle.ResetForRoom();
            }
            catch (RoomResetNotificationException exception)
            {
                notificationFailures.AddRange(exception.InnerExceptions);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures("Room restart phase failed.", failures);
        }

        private static void ThrowFailures(string operation, List<Exception> failures)
        {
            if (failures.Count > 0)
            {
                throw new AggregateException(operation, failures);
            }
        }

        private void NotifyRestarted()
        {
            if (Restarted == null)
            {
                return;
            }

            var errors = new List<Exception>();
            foreach (Action observer in Restarted.GetInvocationList())
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

            if (errors.Count == 1)
            {
                throw new InvalidOperationException("Room-restart observer failed.", errors[0]);
            }

            if (errors.Count > 1)
            {
                throw new AggregateException("Room-restart observers failed.", errors);
            }
        }
        private void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            ValidateConfiguration();
            playerSnapshot = new TransformSnapshot(playerLifeCycle.transform);
            enemySnapshots = new TransformSnapshot[enemies.Length];
            for (var index = 0; index < enemies.Length; index++)
            {
                enemySnapshots[index] = new TransformSnapshot(enemies[index].transform);
            }

            isInitialized = true;
        }

        private void RestoreInitialTransforms()
        {
            playerSnapshot.Restore(playerLifeCycle.transform);
            for (var index = 0; index < enemies.Length; index++)
            {
                enemySnapshots[index].Restore(enemies[index].transform);
            }
        }

        private void ValidateConfiguration()
        {
            if (playerLifeCycle == null)
            {
                throw new InvalidOperationException("RoomRestartController requires a PlayerLifeCycle.");
            }

            if (enemies == null || enemies.Length != 5)
            {
                throw new InvalidOperationException("RoomRestartController requires exactly five M1 enemies.");
            }

            var enemyEntityIds = new HashSet<int>();
            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                if (enemy == null)
                {
                    throw new InvalidOperationException("RoomRestartController has an unassigned enemy.");
                }

                if (enemy.EntityId == 0 || !enemyEntityIds.Add(enemy.EntityId))
                {
                    throw new InvalidOperationException("RoomRestartController enemies require unique non-zero entity IDs.");
                }
            }


            if (blessingTargeting == null)
            {
                throw new InvalidOperationException("RoomRestartController requires a BlessingTargeting controller.");
            }


            if (roomLifecycle == null)
            {
                throw new InvalidOperationException("RoomRestartController requires an M1RoomLifecycle.");
            }
            if (!roomLifecycle.UsesBlessingTargeting(blessingTargeting))
            {
                throw new InvalidOperationException("RoomRestartController and M1RoomLifecycle must share the same BlessingTargeting controller.");
            }

            for (var index = 0; index < enemies.Length; index++)
            {
                if (!roomLifecycle.OwnsEnemyHealth(enemies[index].Health))
                {
                    throw new InvalidOperationException("RoomRestartController enemies must match M1RoomLifecycle enemy Health references.");
                }
            }
        }

        private readonly struct TransformSnapshot
        {
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 localScale;

            public TransformSnapshot(Transform transform)
            {
                position = transform.position;
                rotation = transform.rotation;
                localScale = transform.localScale;
            }

            public void Restore(Transform transform)
            {
                transform.SetPositionAndRotation(position, rotation);
                transform.localScale = localScale;
            }
        }
    }
}
