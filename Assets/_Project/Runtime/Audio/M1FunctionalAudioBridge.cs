using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class M1FunctionalAudioBridge : MonoBehaviour
    {
        [SerializeField] private FunctionalAudioEmitter emitter;
        [SerializeField] private Health playerHealth;
        [SerializeField] private EnemyBase[] enemies;
        [SerializeField] private M1RoomLifecycle roomLifecycle;
        [SerializeField] private RoomRestartController restartController;
        private readonly Dictionary<AttackStateMachine, Action<AttackPhase>> phaseHandlers =
            new Dictionary<AttackStateMachine, Action<AttackPhase>>();
        private readonly Dictionary<AttackIdentity, long> attackTokens =
            new Dictionary<AttackIdentity, long>();

        private long nextReadyToken;
        private long nextSoulToken;
        private long nextExitToken;
        private long nextAttackToken;
        private bool subscribed;
        private bool started;

        private void OnEnable()
        {
            if (started)
            {
                Subscribe();
            }
        }

        private void Start()
        {
            Subscribe();
            started = true;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            ValidateConfiguration();
            subscribed = true;

            try
            {
                playerHealth.Damaged += HandlePlayerDamaged;
                roomLifecycle.SoulCountChanged += HandleSoulCountChanged;
                roomLifecycle.ExitOpened += HandleExitOpened;
                restartController.Restarted += HandleRoomRestarted;

                for (var index = 0; index < enemies.Length; index++)
                {
                    var enemy = enemies[index];
                    var attackState = enemy.AttackState;
                    if (enemy is DasherAI || enemy is ArcherAI)
                    {
                        Action<AttackPhase> phaseHandler = phase => HandleEnemyAttackPhaseChanged(enemy, phase);
                        phaseHandlers.Add(attackState, phaseHandler);
                        attackState.PhaseChanged += phaseHandler;
                    }

                    attackState.ContextLocked += HandleAttackLocked;
                }
            }
            catch
            {
                Unsubscribe();
                throw;
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            playerHealth.Damaged -= HandlePlayerDamaged;
            roomLifecycle.SoulCountChanged -= HandleSoulCountChanged;
            roomLifecycle.ExitOpened -= HandleExitOpened;
            restartController.Restarted -= HandleRoomRestarted;

            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                if (enemy == null)
                {
                    continue;
                }

                var attackState = enemy.AttackState;
                if (attackState == null)
                {
                    continue;
                }

                if (phaseHandlers.TryGetValue(attackState, out var phaseHandler))
                {
                    attackState.PhaseChanged -= phaseHandler;
                }

                attackState.ContextLocked -= HandleAttackLocked;
            }

            phaseHandlers.Clear();
            subscribed = false;
        }

        private void HandleEnemyAttackPhaseChanged(EnemyBase enemy, AttackPhase phase)
        {
            if (phase != AttackPhase.Warning)
            {
                return;
            }

            FunctionalAudioEvent eventType;
            if (enemy is DasherAI)
            {
                eventType = FunctionalAudioEvent.DasherReady;
            }
            else if (enemy is ArcherAI)
            {
                eventType = FunctionalAudioEvent.ArcherReady;
            }
            else
            {
                return;
            }

            nextReadyToken = IncrementToken(nextReadyToken);
            emitter.Emit(eventType, nextReadyToken);
        }

        private void HandleAttackLocked(AttackContext context)
        {
            emitter.Emit(
                FunctionalAudioEvent.AttackLocked,
                GetAttackToken(context.AttackerEntityId, context.AttackInstanceId));
        }

        private void HandlePlayerDamaged(DamageEvent damageEvent)
        {
            emitter.Emit(
                FunctionalAudioEvent.PlayerHit,
                GetAttackToken(damageEvent.AttackerEntityId, damageEvent.AttackInstanceId));
        }

        private void HandleSoulCountChanged(int soulCount)
        {
            if (soulCount <= 0)
            {
                return;
            }

            nextSoulToken = IncrementToken(nextSoulToken);
            emitter.Emit(FunctionalAudioEvent.SoulCollected, nextSoulToken);
        }

        private void HandleExitOpened()
        {
            nextExitToken = IncrementToken(nextExitToken);
            emitter.Emit(FunctionalAudioEvent.ExitOpened, nextExitToken);
        }

        private void HandleRoomRestarted()
        {
            emitter.ResetEmitter();
            nextReadyToken = 0;
            nextSoulToken = 0;
            nextExitToken = 0;
            nextAttackToken = 0;
            attackTokens.Clear();
        }

        private long GetAttackToken(int attackerEntityId, long attackInstanceId)
        {
            if (attackerEntityId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attackerEntityId),
                    attackerEntityId,
                    "Attack audio requires a non-zero attacker entity ID.");
            }

            if (attackInstanceId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attackInstanceId),
                    attackInstanceId,
                    "Attack audio requires a positive attack instance ID.");
            }

            var identity = new AttackIdentity(attackerEntityId, attackInstanceId);
            if (attackTokens.TryGetValue(identity, out var token))
            {
                return token;
            }

            nextAttackToken = IncrementToken(nextAttackToken);
            attackTokens.Add(identity, nextAttackToken);
            return nextAttackToken;
        }

        private static long IncrementToken(long value)
        {
            if (value == long.MaxValue)
            {
                throw new InvalidOperationException("Functional audio token overflowed.");
            }

            return value + 1;
        }

        private void ValidateConfiguration()
        {
            if (emitter == null || playerHealth == null || roomLifecycle == null || restartController == null)
            {
                throw new InvalidOperationException("M1 functional audio bridge requires emitter, player, room, and restart references.");
            }

            if (enemies == null || enemies.Length != 5)
            {
                throw new InvalidOperationException("M1 functional audio bridge requires exactly five enemies.");
            }

            var uniqueEnemies = new HashSet<EnemyBase>();
            var uniqueAttackStates = new HashSet<AttackStateMachine>();
            var dasherCount = 0;
            var archerCount = 0;
            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                if (enemy == null)
                {
                    throw new InvalidOperationException("M1 functional audio bridge contains an unassigned enemy.");
                }

                if (!uniqueEnemies.Add(enemy))
                {
                    throw new InvalidOperationException("M1 functional audio bridge contains a duplicate enemy.");
                }

                var attackState = enemy.AttackState;
                if (attackState == null)
                {
                    throw new InvalidOperationException("M1 functional audio bridge contains an enemy without an attack state.");
                }

                if (!uniqueAttackStates.Add(attackState))
                {
                    throw new InvalidOperationException("M1 functional audio bridge contains enemies sharing an attack state.");
                }

                if (enemy is DasherAI)
                {
                    dasherCount++;
                }
                else if (enemy is ArcherAI)
                {
                    archerCount++;
                }
            }

            if (dasherCount != 1 || archerCount != 2)
            {
                throw new InvalidOperationException("M1 functional audio bridge requires one dasher and two archers.");
            }
        }

        private readonly struct AttackIdentity : IEquatable<AttackIdentity>
        {
            public AttackIdentity(int attackerEntityId, long attackInstanceId)
            {
                AttackerEntityId = attackerEntityId;
                AttackInstanceId = attackInstanceId;
            }

            private int AttackerEntityId { get; }
            private long AttackInstanceId { get; }

            public bool Equals(AttackIdentity other)
            {
                return AttackerEntityId == other.AttackerEntityId &&
                       AttackInstanceId == other.AttackInstanceId;
            }

            public override bool Equals(object obj)
            {
                return obj is AttackIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (AttackerEntityId * 397) ^ AttackInstanceId.GetHashCode();
                }
            }
        }
    }
}
