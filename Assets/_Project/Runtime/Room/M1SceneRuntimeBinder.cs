using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class M1SceneRuntimeBinder : MonoBehaviour
    {
        [SerializeField] private Health playerHealth;
        [SerializeField] private BlessingTargeting blessingTargeting;
        [SerializeField] private EnemyBase[] enemies;
        [SerializeField] private M1RoomLifecycle roomLifecycle;

        private bool initialized;

        public bool IsInitialized => initialized;

        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            ValidateConfiguration();
            var registeredEntityIds = new List<int>();
            try
            {
                blessingTargeting.SetOwnerHealth(playerHealth);
                for (var index = 0; index < enemies.Length; index++)
                {
                    var enemy = enemies[index];
                    blessingTargeting.RegisterTarget(enemy, enemy.Health, enemy.transform);
                    registeredEntityIds.Add(enemy.EntityId);
                }

                initialized = true;
            }
            catch
            {
                for (var index = registeredEntityIds.Count - 1; index >= 0; index--)
                {
                    blessingTargeting.DeregisterTarget(registeredEntityIds[index]);
                }

                blessingTargeting.SetOwnerHealth(null);
                throw;
            }
        }

        private void ValidateConfiguration()
        {
            if (playerHealth == null || blessingTargeting == null)
            {
                throw new InvalidOperationException("M1 scene binder requires player health and blessing targeting references.");
            }

            if (roomLifecycle == null || !roomLifecycle.UsesBlessingTargeting(blessingTargeting))
            {
                throw new InvalidOperationException("M1 scene binder must share its room lifecycle and blessing targeting owner.");
            }

            if (enemies == null || enemies.Length != 5)
            {
                throw new InvalidOperationException("M1 scene binder requires exactly five enemies.");
            }

            var entityIds = new HashSet<int>();
            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                if (enemy == null || enemy.Health == null || enemy.EntityId == 0 || !entityIds.Add(enemy.EntityId))
                {
                    throw new InvalidOperationException("M1 scene binder enemies require unique non-zero entity IDs and Health references.");
                }

                if (!roomLifecycle.OwnsEnemyHealth(enemy.Health))
                {
                    throw new InvalidOperationException("M1 scene binder enemies must match the room lifecycle roster.");
                }
            }
        }
    }
}
