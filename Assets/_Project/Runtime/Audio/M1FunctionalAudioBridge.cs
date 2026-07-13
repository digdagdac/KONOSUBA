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
        private readonly Dictionary<CueIdentity, long> cueTokens =
            new Dictionary<CueIdentity, long>();
        private readonly Dictionary<AttackStateMachine, long> warningGenerations =
            new Dictionary<AttackStateMachine, long>();
        private readonly HashSet<AttackStateMachine> warningStates =
            new HashSet<AttackStateMachine>();

        private long roomEpoch = 1;
        private long nextCueToken;
        private long nextAttackToken;
        private int lastSoulCount;
        private long exitOpenedEpoch;
        private bool runtimeSubscribed;
        private bool restartSubscribed;
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
            started = true;
            Subscribe();
        }

        private void OnDisable()
        {
            UnsubscribeRuntimeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeRuntimeEvents();
            UnsubscribeFromRestart();
        }

        private void Subscribe()
        {
            if (runtimeSubscribed)
            {
                return;
            }

            ValidateConfiguration();
            runtimeSubscribed = true;

            try
            {
                SubscribeToRestart();
                playerHealth.Damaged += HandlePlayerDamaged;
                roomLifecycle.SoulCountChanged += HandleSoulCountChanged;
                roomLifecycle.ExitOpened += HandleExitOpened;

                for (var index = 0; index < enemies.Length; index++)
                {
                    var enemy = enemies[index];
                    var attackState = enemy.AttackState;
                    if (enemy is DasherAI || enemy is ArcherAI)
                    {
                        Action<AttackPhase> phaseHandler =
                            phase => HandleEnemyAttackPhaseChanged(enemy, attackState, phase);
                        phaseHandlers.Add(attackState, phaseHandler);
                        attackState.PhaseChanged += phaseHandler;
                    }

                    attackState.ContextLocked += HandleAttackLocked;
                }
                ReconcileCurrentRoomState();
            }
            catch
            {
                UnsubscribeRuntimeEvents();
                UnsubscribeFromRestart();
                throw;
            }
        }

        private void ReconcileCurrentRoomState()
        {
            lastSoulCount = roomLifecycle.SoulCount;
            if (roomLifecycle.IsExitOpen)
            {
                exitOpenedEpoch = roomEpoch;
            }

            warningStates.Clear();
            for (var index = 0; index < enemies.Length; index++)
            {
                var enemy = enemies[index];
                var attackState = enemy.AttackState;
                if ((enemy is DasherAI || enemy is ArcherAI) &&
                    attackState.Phase == AttackPhase.Warning)
                {
                    warningStates.Add(attackState);
                }
            }
        }
        private void UnsubscribeRuntimeEvents()
        {
            if (!runtimeSubscribed)
            {
                return;
            }

            playerHealth.Damaged -= HandlePlayerDamaged;
            roomLifecycle.SoulCountChanged -= HandleSoulCountChanged;
            roomLifecycle.ExitOpened -= HandleExitOpened;

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
            runtimeSubscribed = false;
        }

        private void HandleEnemyAttackPhaseChanged(
            EnemyBase enemy,
            AttackStateMachine attackState,
            AttackPhase phase)
        {
            if (phase != AttackPhase.Warning)
            {
                warningStates.Remove(attackState);
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

            if (!warningStates.Add(attackState))
            {
                return;
            }

            var warningGeneration = GetNextWarningGeneration(attackState);
            emitter.Emit(
                eventType,
                GetCueToken(
                    CueDomain.Ready,
                    attackState.AttackerEntityId,
                    warningGeneration));
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
            if (soulCount <= 0 || soulCount <= lastSoulCount)
            {
                return;
            }

            lastSoulCount = soulCount;
            emitter.Emit(
                FunctionalAudioEvent.SoulCollected,
                GetCueToken(CueDomain.Soul, soulCount, 0));
        }

        private void HandleExitOpened()
        {
            if (exitOpenedEpoch == roomEpoch)
            {
                return;
            }

            exitOpenedEpoch = roomEpoch;
            emitter.Emit(
                FunctionalAudioEvent.ExitOpened,
                GetCueToken(CueDomain.Exit, 0, 0));
        }

        private void HandleRoomRestarted()
        {
            roomEpoch = IncrementToken(roomEpoch);
            nextCueToken = 0;
            nextAttackToken = 0;
            lastSoulCount = 0;
            exitOpenedEpoch = 0;
            attackTokens.Clear();
            cueTokens.Clear();
            warningGenerations.Clear();
            warningStates.Clear();
            emitter.ResetEmitter();
        }

        private void SubscribeToRestart()
        {
            if (restartSubscribed)
            {
                return;
            }

            restartController.Restarted += HandleRoomRestarted;
            restartSubscribed = true;
        }

        private void UnsubscribeFromRestart()
        {
            if (!restartSubscribed)
            {
                return;
            }

            restartController.Restarted -= HandleRoomRestarted;
            restartSubscribed = false;
        }

        private long GetNextWarningGeneration(AttackStateMachine attackState)
        {
            if (!warningGenerations.TryGetValue(attackState, out var generation))
            {
                generation = 0;
            }

            generation = IncrementToken(generation);
            warningGenerations[attackState] = generation;
            return generation;
        }

        private long GetCueToken(CueDomain domain, int entityId, long occurrence)
        {
            var identity = new CueIdentity(roomEpoch, domain, entityId, occurrence);
            if (cueTokens.TryGetValue(identity, out var token))
            {
                return token;
            }

            nextCueToken = IncrementToken(nextCueToken);
            cueTokens.Add(identity, nextCueToken);
            return nextCueToken;
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

        private enum CueDomain
        {
            Ready,
            Soul,
            Exit
        }

        private readonly struct CueIdentity : IEquatable<CueIdentity>
        {
            private readonly long roomEpoch;
            private readonly CueDomain domain;
            private readonly int entityId;
            private readonly long occurrence;

            public CueIdentity(long roomEpoch, CueDomain domain, int entityId, long occurrence)
            {
                this.roomEpoch = roomEpoch;
                this.domain = domain;
                this.entityId = entityId;
                this.occurrence = occurrence;
            }

            public bool Equals(CueIdentity other)
            {
                return roomEpoch == other.roomEpoch &&
                       domain == other.domain &&
                       entityId == other.entityId &&
                       occurrence == other.occurrence;
            }

            public override bool Equals(object obj)
            {
                return obj is CueIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = roomEpoch.GetHashCode();
                    hash = (hash * 397) ^ (int)domain;
                    hash = (hash * 397) ^ entityId;
                    return (hash * 397) ^ occurrence.GetHashCode();
                }
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
