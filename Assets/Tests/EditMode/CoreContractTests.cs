using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEngine;

namespace Overbless.Tests.EditMode
{
    public sealed class CoreContractTests
    {
        private readonly List<UnityEngine.Object> objectsToDestroy = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = objectsToDestroy.Count - 1; index >= 0; index--)
            {
                if (objectsToDestroy[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objectsToDestroy[index]);
                }
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void DamageLedger_KeysAcceptedDamageByAttackAndTargetAndRejectsSelfDamage()
        {
            var ledger = new DamageLedger();
            var firstTarget = new RecordingDamageable(2);
            var secondTarget = new RecordingDamageable(3);
            var firstDamage = new DamageEvent(1, 1, 2, 3);
            var secondDamage = new DamageEvent(1, 1, 3, 3);
            var selfDamage = new DamageEvent(2, 2, 2, 3);
            var duplicateWithDifferentAttacker = new DamageEvent(1, 99, 2, 7);

            Assert.That(ledger.TryApply(firstTarget, firstDamage), Is.True);
            Assert.That(ledger.TryApply(secondTarget, secondDamage), Is.True);
            Assert.That(ledger.TryApply(firstTarget, firstDamage), Is.False);
            Assert.That(ledger.TryApply(firstTarget, duplicateWithDifferentAttacker), Is.False);
            Assert.That(ledger.TryApply(firstTarget, selfDamage), Is.False);

            Assert.That(firstTarget.AppliedDamage, Is.EquivalentTo(new[] { firstDamage }));
            Assert.That(secondTarget.AppliedDamage, Is.EquivalentTo(new[] { secondDamage }));
            Assert.That(ledger.Count, Is.EqualTo(2));
        }

        [Test]
        public void Health_PreservesRatioAndEmitsOneDeathUntilReset()
        {
            var health = CreateHealth(7, 10);
            var deathCount = 0;
            DeathEvent deathEvent = default;
            health.Died += value =>
            {
                deathCount++;
                deathEvent = value;
            };

            Assert.That(health.TryApplyDamage(new DamageEvent(1, 1, 7, 4)), Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(6));

            health.SetMaximumHealthPreservingRatio(20);
            Assert.That(health.CurrentHealth, Is.EqualTo(12));
            health.SetMaximumHealthPreservingRatio(5);
            Assert.That(health.CurrentHealth, Is.EqualTo(3));

            Assert.That(health.TryApplyDamage(new DamageEvent(2, 1, 7, 3)), Is.True);
            Assert.That(health.IsDead, Is.True);
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(deathCount, Is.EqualTo(1));
            Assert.That(deathEvent.EntityId, Is.EqualTo(7));
            Assert.That(deathEvent.DeathToken, Is.EqualTo(1));
            Assert.That(health.TryApplyDamage(new DamageEvent(3, 1, 7, 1)), Is.False);

            health.SetMaximumHealthPreservingRatio(10);
            Assert.That(health.CurrentHealth, Is.Zero);
            Assert.That(health.MaximumHealth, Is.EqualTo(10));

            health.ResetHealth();
            Assert.That(health.IsDead, Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(10));
            Assert.That(health.DeathToken, Is.EqualTo(1));
            Assert.That(health.TryApplyDamage(new DamageEvent(4, 2, 7, 10)), Is.True);
            Assert.That(health.IsDead, Is.True);
            Assert.That(deathCount, Is.EqualTo(2));
            Assert.That(deathEvent.DeathToken, Is.EqualTo(2));
        }

        [Test]
        public void Health_LethalDamageNotifiesAllObserversBeforeSurfacingFailures()
        {
            var health = CreateHealth(8, 1);
            var laterDamageObserved = false;
            var deathObserved = false;
            health.Damaged += _ => throw new Exception("damaged observer failure");
            health.Damaged += _ => laterDamageObserved = true;
            health.Died += _ => deathObserved = true;

            Assert.Throws<InvalidOperationException>(
                () => health.TryApplyDamage(new DamageEvent(10, 1, 8, 1)));
            Assert.That(health.IsDead, Is.True);
            Assert.That(laterDamageObserved, Is.True);
            Assert.That(deathObserved, Is.True);
        }

        [Test]
        public void Health_ReentrantResetAndRekillNeverPublishesTheOlderDeath()
        {
            var health = CreateHealth(18, 1);
            var observedDeathTokens = new List<long>();
            var reentered = false;

            health.Damaged += damageEvent =>
            {
                if (reentered || damageEvent.AttackInstanceId != 20)
                {
                    return;
                }

                reentered = true;
                health.ResetHealth();
                Assert.That(
                    health.TryApplyDamage(new DamageEvent(21, 2, 18, 1)),
                    Is.True);
            };
            health.Died += deathEvent => observedDeathTokens.Add(deathEvent.DeathToken);

            Assert.That(
                health.TryApplyDamage(new DamageEvent(20, 1, 18, 1)),
                Is.True);
            Assert.That(health.IsDead, Is.True);
            Assert.That(health.DeathToken, Is.EqualTo(2));
            Assert.That(observedDeathTokens, Is.EqualTo(new[] { 2L }));
        }

        [Test]
        public void Health_ReentrantResetFromDeathStopsLaterOldLifeObservers()
        {
            var health = CreateHealth(19, 1);
            var laterDeathObserved = false;

            health.Died += _ => health.ResetHealth();
            health.Died += _ => laterDeathObserved = true;

            Assert.That(
                health.TryApplyDamage(new DamageEvent(22, 1, 19, 1)),
                Is.True);
            Assert.That(health.IsDead, Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(1));
            Assert.That(health.DeathToken, Is.EqualTo(1));
            Assert.That(laterDeathObserved, Is.False);
        }

        [Test]
        public void AttackStateMachine_LockCancelAndResetDisposeEachContextOnce()
        {
            var machine = new AttackStateMachine(9);
            var lockedContexts = new List<AttackContext>();
            var disposedContexts = new List<AttackContext>();
            machine.ContextLocked += lockedContexts.Add;
            machine.ContextDisposed += disposedContexts.Add;

            machine.BeginWarning(0f);
            Assert.That(machine.WarningDuration, Is.EqualTo(AttackStateMachine.MinimumWarningDuration));
            Assert.Throws<InvalidOperationException>(() => Lock(machine));
            Assert.That(machine.AdvanceWarning(0.5f), Is.False);
            Assert.That(machine.AdvanceWarning(1f), Is.True);

            var lockedContext = Lock(machine);
            Assert.That(machine.Phase, Is.EqualTo(AttackPhase.Locked));
            Assert.That(machine.CurrentContext, Is.SameAs(lockedContext));
            Assert.That(lockedContexts, Is.EquivalentTo(new[] { lockedContext }));

            machine.BeginExecuting();
            machine.Cancel();
            machine.Cancel();

            Assert.That(machine.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(machine.CurrentContext, Is.Null);
            Assert.That(machine.WarningDuration, Is.Zero);
            Assert.That(machine.WarningElapsed, Is.Zero);
            Assert.That(disposedContexts, Is.EquivalentTo(new[] { lockedContext }));

            machine.BeginWarning(1f);
            Assert.That(machine.AdvanceWarning(1f), Is.True);
            var resetContext = Lock(machine);
            Assert.That(resetContext.AttackInstanceId, Is.GreaterThan(lockedContext.AttackInstanceId));
            machine.Reset();
            Assert.That(machine.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(machine.CurrentContext, Is.Null);
            Assert.That(machine.WarningDuration, Is.Zero);
            Assert.That(machine.WarningElapsed, Is.Zero);
            Assert.That(disposedContexts, Is.EquivalentTo(new[] { lockedContext, resetContext }));
        }
        [Test]
        public void AttackStateMachine_IssuesGloballyUniqueIdsAcrossMachines()
        {
            var firstMachine = new AttackStateMachine(9);
            var secondMachine = new AttackStateMachine(10);
            firstMachine.BeginWarning(0f);
            secondMachine.BeginWarning(0f);
            Assert.That(firstMachine.AdvanceWarning(1f), Is.True);
            Assert.That(secondMachine.AdvanceWarning(1f), Is.True);

            var firstContext = Lock(firstMachine);
            var secondContext = Lock(secondMachine);

            Assert.That(firstContext.AttackInstanceId, Is.GreaterThan(0));
            Assert.That(secondContext.AttackInstanceId, Is.GreaterThan(0));
            Assert.That(secondContext.AttackInstanceId, Is.Not.EqualTo(firstContext.AttackInstanceId));
        }

        [Test]
        public void AttackStateMachine_ReentrantObserversCannotPublishStaleLockOrCorruptRecovery()
        {
            var cancelledMachine = new AttackStateMachine(10);
            var lockedPhaseObserved = false;
            var staleLockedContextObserved = false;
            cancelledMachine.PhaseChanged += phase =>
            {
                if (phase == AttackPhase.Locked)
                {
                    lockedPhaseObserved = true;
                }
            };
            cancelledMachine.ContextLocked += _ => cancelledMachine.Cancel();
            cancelledMachine.ContextLocked += _ => staleLockedContextObserved = true;
            cancelledMachine.BeginWarning(0f);
            cancelledMachine.AdvanceWarning(1f);

            Assert.Throws<InvalidOperationException>(() => Lock(cancelledMachine));
            Assert.That(cancelledMachine.Phase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(cancelledMachine.CurrentContext, Is.Null);
            Assert.That(lockedPhaseObserved, Is.False);
            Assert.That(staleLockedContextObserved, Is.False);

            var phaseCancelledMachine = new AttackStateMachine(12);
            var staleLockedPhaseObserved = false;
            phaseCancelledMachine.PhaseChanged += phase =>
            {
                if (phase == AttackPhase.Locked)
                {
                    phaseCancelledMachine.Cancel();
                }
            };
            phaseCancelledMachine.PhaseChanged += phase =>
            {
                if (phase == AttackPhase.Locked)
                {
                    staleLockedPhaseObserved = true;
                }
            };
            phaseCancelledMachine.BeginWarning(0f);
            phaseCancelledMachine.AdvanceWarning(1f);

            Assert.Throws<InvalidOperationException>(() => Lock(phaseCancelledMachine));
            Assert.That(staleLockedPhaseObserved, Is.False);

            var recoveryMachine = new AttackStateMachine(11);
            recoveryMachine.BeginWarning(0f);
            recoveryMachine.AdvanceWarning(1f);
            Lock(recoveryMachine);
            recoveryMachine.BeginExecuting();
            recoveryMachine.ContextDisposed += _ => throw new Exception("observer failure");

            Assert.Throws<InvalidOperationException>(() => recoveryMachine.BeginRecovery());
            Assert.That(recoveryMachine.Phase, Is.EqualTo(AttackPhase.Recovery));
            Assert.That(recoveryMachine.CurrentContext, Is.Null);
        }
        [Test]
        public void PlayerInputRouter_RequiresEveryOwnerToReleaseItsOwnBlock()
        {
            var player = Track(new GameObject("InputRouterTest"));
            var router = player.AddComponent<PlayerInputRouter>();

            router.AcquireInputBlock(PlayerInputBlocker.LifeCycle);
            router.AcquireInputBlock(PlayerInputBlocker.FocusGate);
            Assert.That(router.IsInputEnabled, Is.False);

            router.ReleaseInputBlock(PlayerInputBlocker.LifeCycle);
            Assert.That(router.IsInputEnabled, Is.False);

            router.ReleaseInputBlock(PlayerInputBlocker.FocusGate);
            Assert.That(router.IsInputEnabled, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => router.AcquireInputBlock((PlayerInputBlocker)99));
            router.SetRestartInputEnabled(true);
            var laterRestartObserverRan = false;
            router.RestartRequested += () => throw new InvalidOperationException("expected observer failure");
            router.RestartRequested += () => laterRestartObserverRan = true;
            var restartHandler = typeof(PlayerInputRouter).GetMethod(
                "HandleRestart",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(restartHandler, Is.Not.Null);
            var callbackContext = Activator.CreateInstance(
                restartHandler.GetParameters()[0].ParameterType);
            var restartFailure = Assert.Throws<TargetInvocationException>(
                () => restartHandler.Invoke(
                    router,
                    new[] { callbackContext }));
            Assert.That(restartFailure.InnerException, Is.TypeOf<AggregateException>());
            Assert.That(laterRestartObserverRan, Is.True);
        }

        [Test]
        public void Blessings_RejectDuplicatesOrderEffectsDeterministicallyAndUseExactMultipliers()
        {
            var definition = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
            var health = CreateHealth(11, definition.MaximumHealth);
            var target = new RecordingBlessingTarget(11, definition, health);
            var system = new BlessingSystem();
            var hasteSlot = new BlessingSlot(BlessingDefinition.Haste);
            var duplicateHasteSlot = new BlessingSlot(BlessingDefinition.Haste);
            var giantSlot = new BlessingSlot(BlessingDefinition.Giant);
            var duplicateGiantSlot = new BlessingSlot(BlessingDefinition.Giant);

            try
            {
                Assert.That(health.TryApplyDamage(new DamageEvent(1, 1, health.EntityId, 5)), Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(5));

                Assert.That(system.TryApply(hasteSlot, target, health, out var hasteApplication), Is.True);
                Assert.That(hasteApplication.ActiveBlessings, Is.EqualTo(new[] { BlessingType.Haste }));
                Assert.That(system.TryApply(duplicateHasteSlot, target, health, out _), Is.False);
                Assert.That(health.MaximumHealth, Is.EqualTo(definition.MaximumHealth));
                Assert.That(health.CurrentHealth, Is.EqualTo(5));

                Assert.That(system.TryApply(giantSlot, target, health, out var giantApplication), Is.True);
                Assert.That(giantApplication.ActiveBlessings, Is.EqualTo(new[] { BlessingType.Haste, BlessingType.Giant }));
                Assert.That(system.GetActiveBlessings(11), Is.EqualTo(new[] { BlessingType.Haste, BlessingType.Giant }));

                var canonical = EnemyRuntimeStats.Recompute(
                    definition,
                    new[] { BlessingType.Haste, BlessingType.Giant });
                var reversed = EnemyRuntimeStats.Recompute(
                    definition,
                    new[] { BlessingType.Giant, BlessingType.Haste });
                var duplicated = EnemyRuntimeStats.Recompute(
                    definition,
                    new[] { BlessingType.Haste, BlessingType.Giant, BlessingType.Haste });

                AssertEquivalentStats(canonical, reversed);
                AssertEquivalentStats(canonical, duplicated);
                Assert.That(target.LastStats.MaximumHealth, Is.EqualTo(canonical.MaximumHealth));
                Assert.That(target.LastHealthRatio, Is.EqualTo(0.5f));
                Assert.That(health.MaximumHealth, Is.EqualTo(canonical.MaximumHealth));
                Assert.That(health.CurrentHealth, Is.EqualTo(Mathf.RoundToInt(canonical.MaximumHealth * 0.5f)));

                var healthAfterGiant = health.CurrentHealth;
                Assert.That(system.TryApply(duplicateGiantSlot, target, health, out _), Is.False);
                Assert.That(health.MaximumHealth, Is.EqualTo(canonical.MaximumHealth));
                Assert.That(health.CurrentHealth, Is.EqualTo(healthAfterGiant));
                Assert.That(canonical.MovementSpeed, Is.EqualTo(definition.MovementSpeed * BlessingDefinition.Haste.MovementSpeedMultiplier));
                Assert.That(canonical.AttackCooldown, Is.EqualTo(definition.AttackCooldown * BlessingDefinition.Haste.AttackCooldownMultiplier));
                Assert.That(canonical.ProjectileSpeed, Is.EqualTo(definition.ProjectileSpeed * BlessingDefinition.Haste.ProjectileSpeedMultiplier));
                Assert.That(canonical.AttackSpeedMultiplier, Is.EqualTo(BlessingDefinition.Haste.AttackSpeedMultiplier));
                Assert.That(canonical.MaximumHealth, Is.EqualTo(Mathf.CeilToInt(
                    definition.MaximumHealth * BlessingDefinition.Giant.MaximumHealthMultiplier)));
                Assert.That(canonical.AttackRange, Is.EqualTo(definition.AttackRange * BlessingDefinition.Giant.AttackRangeMultiplier));
                Assert.That(canonical.ScaleMultiplier, Is.EqualTo(BlessingDefinition.Giant.ScaleMultiplier));
                Assert.That(canonical.MassMultiplier, Is.EqualTo(BlessingDefinition.Giant.MassMultiplier));
            }
            finally
            {
                hasteSlot.Dispose();
                duplicateHasteSlot.Dispose();
                giantSlot.Dispose();
                duplicateGiantSlot.Dispose();
            }

        }
        [Test]
        public void BlessingSystem_RejectsReentrantMutationAndRetainsOwnershipWhenRestoreFails()
        {
            var definition = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
            var health = CreateHealth(12, definition.MaximumHealth);
            var target = new RecordingBlessingTarget(12, definition, health);
            var system = new BlessingSystem();
            var hasteSlot = new BlessingSlot(BlessingDefinition.Haste);
            var giantSlot = new BlessingSlot(BlessingDefinition.Giant);

            try
            {
                var reentrantResetRejected = false;
                target.Applying = (_, __) =>
                {
                    Assert.Throws<InvalidOperationException>(() => system.Reset());
                    reentrantResetRejected = true;
                    target.Applying = null;
                };

                Assert.That(system.TryApply(hasteSlot, target, health, out _), Is.True);
                Assert.Throws<InvalidOperationException>(() => system.Reset());
                Assert.That(reentrantResetRejected, Is.True);

                target.Applying = (_, __) => throw new InvalidOperationException("target apply failed");
                Assert.Throws<AggregateException>(() => system.TryApply(giantSlot, target, health, out _));
                Assert.That(
                    system.GetActiveBlessings(target.EntityId),
                    Is.EqualTo(new[] { BlessingType.Haste, BlessingType.Giant }));
                Assert.That(giantSlot.IsAvailable, Is.False);
                Assert.That(hasteSlot.IsPinnedForRestorationRetry, Is.True);
                Assert.That(giantSlot.IsPinnedForRestorationRetry, Is.True);

                target.Applying = null;
                Assert.That(system.RemoveTarget(target), Is.True);
                Assert.That(system.GetActiveBlessings(target.EntityId), Is.Empty);
                Assert.DoesNotThrow(() => system.Reset());
            }
            finally
            {
                hasteSlot.Dispose();
                giantSlot.Dispose();
            }
        }

        [Test]
        public void HudController_PublishesOnlyChangedValidStates()
        {
            var hudObject = Track(new GameObject("HUD"));
            var hud = hudObject.AddComponent<HUDController>();
            var changes = 0;
            hud.StateChanged += _ => changes++;

            var ready = new HudState(6, 6, 1f, true, 0f, true, true, 0, false);
            hud.SetState(ready);
            hud.SetState(ready);

            Assert.That(hud.HasState, Is.True);
            Assert.That(hud.State.Health, Is.EqualTo(6));
            Assert.That(hud.State.Dash01, Is.EqualTo(1f));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(
                () => hud.SetState(new HudState(7, 6, 1f, true, 0f, true, true, 0, false)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => hud.SetState(new HudState(6, 6, float.NaN, true, 0f, true, true, 0, false)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void WorldHealthBar_TracksHealthRatioFromLeftToRight()
        {
            var health = CreateHealth(21, 10);
            var barObject = Track(new GameObject("WorldHealthBar"));
            barObject.SetActive(false);
            var backgroundObject = new GameObject("Background", typeof(LineRenderer));
            backgroundObject.transform.SetParent(barObject.transform, false);
            var fillObject = new GameObject("Fill", typeof(LineRenderer));
            fillObject.transform.SetParent(barObject.transform, false);
            var bar = barObject.AddComponent<WorldHealthBar>();
            var background = backgroundObject.GetComponent<LineRenderer>();
            var fill = fillObject.GetComponent<LineRenderer>();
            background.positionCount = 2;
            fill.positionCount = 2;
            SetPrivateField(bar, "health", health);
            SetPrivateField(bar, "backgroundLine", background);
            SetPrivateField(bar, "fillLine", fill);
            barObject.SetActive(true);

            bar.Refresh();
            Assert.That(fill.GetPosition(0).x, Is.EqualTo(-0.36f).Within(0.0001f));
            Assert.That(fill.GetPosition(1).x, Is.EqualTo(0.36f).Within(0.0001f));

            Assert.That(health.TryApplyDamage(new DamageEvent(1, 1, 21, 5)), Is.True);
            bar.Refresh();
            Assert.That(fill.GetPosition(1).x, Is.EqualTo(0f).Within(0.0001f));
        }
        private static AttackContext Lock(AttackStateMachine machine)
        {
            return machine.Lock(1f, Vector2.zero, Vector2.right, AttackShape.Line, 2f, 1f, 1, 1 << 8);
        }

        private static void AssertEquivalentStats(EnemyRuntimeStats expected, EnemyRuntimeStats actual)
        {
            Assert.That(actual.MaximumHealth, Is.EqualTo(expected.MaximumHealth));
            Assert.That(actual.MovementSpeed, Is.EqualTo(expected.MovementSpeed));
            Assert.That(actual.AttackCooldown, Is.EqualTo(expected.AttackCooldown));
            Assert.That(actual.ProjectileSpeed, Is.EqualTo(expected.ProjectileSpeed));
            Assert.That(actual.AttackRange, Is.EqualTo(expected.AttackRange));
            Assert.That(actual.AttackSpeedMultiplier, Is.EqualTo(expected.AttackSpeedMultiplier));
            Assert.That(actual.ScaleMultiplier, Is.EqualTo(expected.ScaleMultiplier));
            Assert.That(actual.MassMultiplier, Is.EqualTo(expected.MassMultiplier));
            Assert.That(actual.HasHaste, Is.EqualTo(expected.HasHaste));
            Assert.That(actual.HasGiant, Is.EqualTo(expected.HasGiant));
        }

        private Health CreateHealth(int entityId, int maximumHealth)
        {
            var gameObject = Track(new GameObject($"Health-{entityId}"));
            gameObject.SetActive(false);
            var health = gameObject.AddComponent<Health>();
            SetPrivateField(health, "entityId", entityId);
            SetPrivateField(health, "maximumHealth", maximumHealth);
            gameObject.SetActive(true);
            health.ResetHealth();
            return health;
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            objectsToDestroy.Add(value);
            return value;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected serialized field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private sealed class RecordingDamageable : IDamageable
        {
            public RecordingDamageable(int entityId)
            {
                EntityId = entityId;
            }

            public List<DamageEvent> AppliedDamage { get; } = new List<DamageEvent>();
            public int EntityId { get; }
            public bool IsDead { get; private set; }

            public bool TryApplyDamage(in DamageEvent damageEvent)
            {
                AppliedDamage.Add(damageEvent);
                return true;
            }
        }

        private sealed class RecordingBlessingTarget : IEnemyBlessingRuntime
        {
            public RecordingBlessingTarget(int entityId, EnemyDefinition definition, Health health)
            {
                EntityId = entityId;
                Definition = definition;
                Health = health;
            }

            public int EntityId { get; }
            public EnemyDefinition Definition { get; }
            public Health Health { get; }
            public float HealthRatio => (float)Health.CurrentHealth / Health.MaximumHealth;
            public EnemyRuntimeStats LastStats { get; private set; }
            public float LastHealthRatio { get; private set; }
            public Action<EnemyRuntimeStats, float> Applying { get; set; }

            public void ApplyBlessingRuntimeStats(EnemyRuntimeStats stats, float healthRatio)
            {
                Applying?.Invoke(stats, healthRatio);
                Health.SetMaximumHealthAndRatio(stats.MaximumHealth, healthRatio);
                LastStats = stats;
                LastHealthRatio = healthRatio;
            }
        }
    }
}
