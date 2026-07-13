using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class PlayerLifeCycle : MonoBehaviour
    {
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Health health;
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private DashAbility dashAbility;

        private Vector3 spawnLocalPosition;
        private Quaternion spawnLocalRotation;
        private Vector3 spawnLocalScale;
        private bool movementEnabledAtSpawn;
        private bool isAlive;
        private bool isResetting;
        private bool isRecoveryPending;

        public event Action<DeathEvent> Died;
        public event Action Reset;

        public bool IsAlive => isAlive;
        public PlayerInputRouter InputRouter => inputRouter;

        private void Awake()
        {
            ValidateConfiguration();
            CaptureSpawnState();
            isAlive = !health.IsDead;
            inputRouter.SetRestartInputEnabled(true);
            isRecoveryPending = false;

            if (!isAlive)
            {
                DisableGameplay();
            }
        }

        private void OnEnable()
        {
            health.Died += HandleDeath;
            ReconcileHealthState();
        }

        private void OnDisable()
        {
            health.Died -= HandleDeath;
        }

        public void ResetPlayer()
        {
            if (isResetting)
            {
                return;
            }

            isResetting = true;
            isRecoveryPending = true;
            var failures = new List<Exception>();
            var resetCommitted = false;

            try
            {
                try
                {
                    playerController.SetMovementEnabled(false);
                    inputRouter.AcquireInputBlock(PlayerInputBlocker.LifeCycle);
                    inputRouter.SetRestartInputEnabled(false);
                    dashAbility.ResetAbility();
                    health.ResetHealth();
                    RestoreSpawnTransform();
                    playerController.ResetController();
                    inputRouter.ResetInputState();
                    playerController.SetMovementEnabled(movementEnabledAtSpawn);
                    isAlive = true;
                    inputRouter.SetRestartInputEnabled(true);
                    isRecoveryPending = false;
                    inputRouter.ReleaseInputBlock(PlayerInputBlocker.LifeCycle);
                    resetCommitted = true;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                if (resetCommitted)
                {
                    InvokeObservers(Reset, failures);
                }
            }
            finally
            {
                try
                {
                    if (!resetCommitted)
                    {
                        FailClosedAfterResetFailure(failures);
                    }
                }
                finally
                {
                    isResetting = false;
                }
            }

            ThrowFailures("Player reset failed.", failures);
        }

        private void HandleDeath(DeathEvent deathEvent)
        {
            if (!isAlive)
            {
                return;
            }

            isAlive = false;
            isRecoveryPending = false;
            var failures = new List<Exception>();

            try
            {
                DisableGameplay();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            InvokeObservers(Died, deathEvent, failures);
            ThrowFailures("Player death handling failed.", failures);
        }
        private void FailClosedAfterResetFailure(List<Exception> failures)
        {
            isRecoveryPending = true;
            try
            {
                playerController.SetMovementEnabled(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                inputRouter.AcquireInputBlock(PlayerInputBlocker.LifeCycle);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                playerController.ResetController();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                dashAbility.ResetAbility();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                isAlive = !health.IsDead;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                isAlive = false;
            }

            try
            {
                inputRouter.SetRestartInputEnabled(true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private static void InvokeObservers(Action observers, List<Exception> failures)
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
                    failures.Add(exception);
                }
            }
        }

        private static void InvokeObservers<T>(Action<T> observers, T value, List<Exception> failures)
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
                    failures.Add(exception);
                }
            }
        }

        private static void ThrowFailures(string operation, List<Exception> failures)
        {
            if (failures.Count > 0)
            {
                throw new AggregateException(operation, failures);
            }
        }
        private void ReconcileHealthState()
        {
            if (!health.IsDead)
            {
                isAlive = true;
                inputRouter.SetRestartInputEnabled(true);
                if (isRecoveryPending)
                {
                    playerController.SetMovementEnabled(false);
                    inputRouter.AcquireInputBlock(PlayerInputBlocker.LifeCycle);
                    return;
                }

                inputRouter.ReleaseInputBlock(PlayerInputBlocker.LifeCycle);
                return;
            }

            isAlive = false;
            DisableGameplay();
        }


        private void CaptureSpawnState()
        {
            spawnLocalPosition = playerTransform.localPosition;
            spawnLocalRotation = playerTransform.localRotation;
            spawnLocalScale = playerTransform.localScale;
            movementEnabledAtSpawn = playerController.IsMovementEnabled;
        }

        private void RestoreSpawnTransform()
        {
            playerTransform.localPosition = spawnLocalPosition;
            playerTransform.localRotation = spawnLocalRotation;
            playerTransform.localScale = spawnLocalScale;
        }

        private void DisableGameplay()
        {
            playerController.SetMovementEnabled(false);
            inputRouter.SetRestartInputEnabled(true);
            inputRouter.AcquireInputBlock(PlayerInputBlocker.LifeCycle);
            playerController.ResetController();
            dashAbility.ResetAbility();
        }

        private void ValidateConfiguration()
        {
            if (playerTransform == null)
            {
                throw new InvalidOperationException("PlayerLifeCycle requires a player transform reference.");
            }

            if (health == null)
            {
                throw new InvalidOperationException("PlayerLifeCycle requires a Health reference.");
            }

            if (inputRouter == null)
            {
                throw new InvalidOperationException("PlayerLifeCycle requires a PlayerInputRouter reference.");
            }

            if (playerController == null)
            {
                throw new InvalidOperationException("PlayerLifeCycle requires a PlayerController reference.");
            }

            if (dashAbility == null)
            {
                throw new InvalidOperationException("PlayerLifeCycle requires a DashAbility reference.");
            }
        }
    }
}
