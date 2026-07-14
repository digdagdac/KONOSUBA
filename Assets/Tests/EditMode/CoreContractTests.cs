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
        public void AttackStateMachine_IndependentCopiesPreservePayloadWithoutMutatingThePrimaryContext()
        {
            var machine = new AttackStateMachine(14);
            var phaseChanges = new List<AttackPhase>();
            var lockedContexts = new List<AttackContext>();
            var disposedContexts = new List<AttackContext>();
            machine.PhaseChanged += phaseChanges.Add;
            machine.ContextLocked += lockedContexts.Add;
            machine.ContextDisposed += disposedContexts.Add;

            Assert.Throws<InvalidOperationException>(() => machine.CreateIndependentContextCopy());
            Assert.That(phaseChanges, Is.Empty);
            Assert.That(lockedContexts, Is.Empty);
            Assert.That(disposedContexts, Is.Empty);

            machine.BeginWarning(0f);
            Assert.That(machine.AdvanceWarning(1f), Is.True);
            var primaryContext = Lock(machine);
            var lockedPhaseChangeCount = phaseChanges.Count;
            var lockedContextCount = lockedContexts.Count;
            var lockedDisposedCount = disposedContexts.Count;

            var lockedCopy = machine.CreateIndependentContextCopy();

            Assert.That(lockedCopy, Is.Not.SameAs(primaryContext));
            Assert.That(lockedCopy.AttackInstanceId, Is.GreaterThan(0));
            Assert.That(primaryContext.AttackInstanceId, Is.GreaterThan(0));
            Assert.That(lockedCopy.AttackInstanceId, Is.Not.EqualTo(primaryContext.AttackInstanceId));
            AssertEquivalentAttackPayload(primaryContext, lockedCopy);
            Assert.That(machine.Phase, Is.EqualTo(AttackPhase.Locked));
            Assert.That(machine.CurrentContext, Is.SameAs(primaryContext));
            Assert.That(phaseChanges.Count, Is.EqualTo(lockedPhaseChangeCount));
            Assert.That(lockedContexts.Count, Is.EqualTo(lockedContextCount));
            Assert.That(disposedContexts.Count, Is.EqualTo(lockedDisposedCount));

            machine.BeginExecuting();
            var executingPhaseChangeCount = phaseChanges.Count;
            var executingContextCount = lockedContexts.Count;
            var executingDisposedCount = disposedContexts.Count;

            var executingCopy = machine.CreateIndependentContextCopy();

            Assert.That(executingCopy, Is.Not.SameAs(primaryContext));
            Assert.That(executingCopy.AttackInstanceId, Is.GreaterThan(0));
            Assert.That(executingCopy.AttackInstanceId, Is.Not.EqualTo(primaryContext.AttackInstanceId));
            Assert.That(executingCopy.AttackInstanceId, Is.Not.EqualTo(lockedCopy.AttackInstanceId));
            AssertEquivalentAttackPayload(primaryContext, executingCopy);
            Assert.That(machine.Phase, Is.EqualTo(AttackPhase.Executing));
            Assert.That(machine.CurrentContext, Is.SameAs(primaryContext));
            Assert.That(phaseChanges.Count, Is.EqualTo(executingPhaseChangeCount));
            Assert.That(lockedContexts.Count, Is.EqualTo(executingContextCount));
            Assert.That(disposedContexts.Count, Is.EqualTo(executingDisposedCount));

            machine.BeginRecovery();
            var recoveryPhaseChangeCount = phaseChanges.Count;
            var recoveryContextCount = lockedContexts.Count;
            var recoveryDisposedCount = disposedContexts.Count;

            Assert.Throws<InvalidOperationException>(() => machine.CreateIndependentContextCopy());
            Assert.That(machine.Phase, Is.EqualTo(AttackPhase.Recovery));
            Assert.That(machine.CurrentContext, Is.Null);
            Assert.That(phaseChanges.Count, Is.EqualTo(recoveryPhaseChangeCount));
            Assert.That(lockedContexts.Count, Is.EqualTo(recoveryContextCount));
            Assert.That(disposedContexts.Count, Is.EqualTo(recoveryDisposedCount));
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
        public void BlessingTypeAndEchoDefinition_UseStableOrdinalsIdentityMultipliersAndExactDelay()
        {
            Assert.That((int)BlessingType.Haste, Is.EqualTo(0));
            Assert.That((int)BlessingType.Giant, Is.EqualTo(1));
            Assert.That((int)BlessingType.Echo, Is.EqualTo(2));

            var echo = BlessingDefinition.Echo;

            Assert.That(BlessingDefinition.Get(BlessingType.Echo), Is.SameAs(echo));
            Assert.That(echo.Type, Is.EqualTo(BlessingType.Echo));
            Assert.That(echo.Id, Is.EqualTo("Echo"));
            Assert.That(echo.MovementSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(echo.AttackSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(echo.AttackCooldownMultiplier, Is.EqualTo(1f));
            Assert.That(echo.ProjectileSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(echo.ScaleMultiplier, Is.EqualTo(1f));
            Assert.That(echo.MaximumHealthMultiplier, Is.EqualTo(1f));
            Assert.That(echo.AttackRangeMultiplier, Is.EqualTo(1f));
            Assert.That(echo.MassMultiplier, Is.EqualTo(1f));
            Assert.That(BlessingDefinition.EchoRepeatDelaySeconds, Is.EqualTo(0.65f));
        }

        [Test]
        public void EnemyRuntimeStats_EchoSetsOnlyItsBehaviorFlag()
        {
            var definition = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
            var baseline = EnemyRuntimeStats.Recompute(definition, Array.Empty<BlessingType>());
            var echo = EnemyRuntimeStats.Recompute(definition, new[] { BlessingType.Echo });

            Assert.That(baseline.HasEcho, Is.False);
            Assert.That(echo.HasEcho, Is.True);
            Assert.That(echo.HasHaste, Is.EqualTo(baseline.HasHaste));
            Assert.That(echo.HasGiant, Is.EqualTo(baseline.HasGiant));
            Assert.That(echo.MaximumHealth, Is.EqualTo(baseline.MaximumHealth));
            Assert.That(echo.MovementSpeed, Is.EqualTo(baseline.MovementSpeed));
            Assert.That(echo.AttackCooldown, Is.EqualTo(baseline.AttackCooldown));
            Assert.That(echo.WarningDuration, Is.EqualTo(baseline.WarningDuration));
            Assert.That(echo.RecoveryDuration, Is.EqualTo(baseline.RecoveryDuration));
            Assert.That(echo.AttackDamage, Is.EqualTo(baseline.AttackDamage));
            Assert.That(echo.EngagementRange, Is.EqualTo(baseline.EngagementRange));
            Assert.That(echo.AttackRange, Is.EqualTo(baseline.AttackRange));
            Assert.That(echo.AttackWidth, Is.EqualTo(baseline.AttackWidth));
            Assert.That(echo.ChargeSpeed, Is.EqualTo(baseline.ChargeSpeed));
            Assert.That(echo.ProjectileSpeed, Is.EqualTo(baseline.ProjectileSpeed));
            Assert.That(echo.PreferredDistance, Is.EqualTo(baseline.PreferredDistance));
            Assert.That(echo.AttackSpeedMultiplier, Is.EqualTo(baseline.AttackSpeedMultiplier));
            Assert.That(echo.ScaleMultiplier, Is.EqualTo(baseline.ScaleMultiplier));
            Assert.That(echo.MassMultiplier, Is.EqualTo(baseline.MassMultiplier));
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
        public void BlessingSystem_EchoRequiresSupportingEnemyAndRestoresOrderedBlessingsOnRemoval()
        {
            var definition = Track(ScriptableObject.CreateInstance<EnemyDefinition>());
            var unsupportedHealth = CreateHealth(13, definition.MaximumHealth);
            var unsupportedTarget = new RecordingBlessingTarget(13, definition, unsupportedHealth);
            var archer = CreateArcher(14, definition, out var archerHealth);
            var system = new BlessingSystem();
            var unsupportedEchoSlot = new BlessingSlot(BlessingDefinition.Echo);
            var echoSlot = new BlessingSlot(BlessingDefinition.Echo);
            var hasteSlot = new BlessingSlot(BlessingDefinition.Haste);

            try
            {
                Assert.That(system.CanApply(BlessingType.Echo, unsupportedTarget), Is.False);
                Assert.That(system.TryApply(unsupportedEchoSlot, unsupportedTarget, unsupportedHealth, out _), Is.False);
                Assert.That(system.GetActiveBlessings(unsupportedTarget.EntityId), Is.Empty);
                Assert.That(unsupportedEchoSlot.IsAvailable, Is.True);

                Assert.That(system.CanApply(BlessingType.Echo, archer), Is.True);
                Assert.That(system.TryApply(echoSlot, archer, archerHealth, out var echoApplication), Is.True);
                Assert.That(echoApplication.Type, Is.EqualTo(BlessingType.Echo));
                Assert.That(echoApplication.ActiveBlessings, Is.EqualTo(new[] { BlessingType.Echo }));
                Assert.That(archer.RuntimeStats.HasEcho, Is.True);
                Assert.That(system.TryApply(echoSlot, archer, archerHealth, out _), Is.False);

                Assert.That(system.TryApply(hasteSlot, archer, archerHealth, out var hasteApplication), Is.True);
                Assert.That(hasteApplication.ActiveBlessings, Is.EqualTo(new[] { BlessingType.Haste, BlessingType.Echo }));
                Assert.That(system.GetActiveBlessings(archer.EntityId), Is.EqualTo(
                    new[] { BlessingType.Haste, BlessingType.Echo }));

                Assert.That(system.RemoveTarget(archer), Is.True);
                Assert.That(system.GetActiveBlessings(archer.EntityId), Is.Empty);
                Assert.That(archer.RuntimeStats.HasEcho, Is.False);
                Assert.That(echoSlot.IsAvailable, Is.True);
                Assert.That(hasteSlot.IsAvailable, Is.True);
                Assert.That(system.RemoveTarget(archer), Is.False);
            }
            finally
            {
                unsupportedEchoSlot.Dispose();
                echoSlot.Dispose();
                hasteSlot.Dispose();
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
        private static void AssertEquivalentAttackPayload(AttackContext expected, AttackContext actual)
        {
            Assert.That(actual.AttackerEntityId, Is.EqualTo(expected.AttackerEntityId));
            Assert.That(actual.LockedAt, Is.EqualTo(expected.LockedAt));
            Assert.That(actual.Origin, Is.EqualTo(expected.Origin));
            Assert.That(actual.NormalizedDirection, Is.EqualTo(expected.NormalizedDirection));
            Assert.That(actual.Shape, Is.EqualTo(expected.Shape));
            Assert.That(actual.Range, Is.EqualTo(expected.Range));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Damage, Is.EqualTo(expected.Damage));
            Assert.That(actual.TargetMask, Is.EqualTo(expected.TargetMask));
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
            Assert.That(actual.HasEcho, Is.EqualTo(expected.HasEcho));
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

        private ArcherAI CreateArcher(int entityId, EnemyDefinition definition, out Health health)
        {
            var gameObject = Track(new GameObject($"Archer-{entityId}"));
            gameObject.SetActive(false);
            health = gameObject.AddComponent<Health>();
            gameObject.AddComponent<Rigidbody2D>();
            gameObject.AddComponent<CircleCollider2D>();
            var archer = gameObject.AddComponent<ArcherAI>();
            SetPrivateField(health, "entityId", entityId);
            SetPrivateField(health, "maximumHealth", definition.MaximumHealth);
            SetPrivateField(archer, "health", health);
            SetPrivateField(archer, "definition", definition);
            InvokeNonPublicMethod(archer, "Awake");
            health.ResetHealth();
            return archer;
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            objectsToDestroy.Add(value);
            return value;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = null;
            for (var type = target.GetType(); type != null && field == null; type = type.BaseType)
            {
                field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }

            Assert.That(field, Is.Not.Null, $"Expected serialized field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static void InvokeNonPublicMethod(object target, string methodName)
        {
            MethodInfo method = null;
            for (var type = target.GetType(); type != null && method == null; type = type.BaseType)
            {
                method = type.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }

            Assert.That(method, Is.Not.Null, $"Expected non-public method '{methodName}'.");
            method.Invoke(target, null);
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
