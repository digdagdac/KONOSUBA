using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine.TestTools;

namespace Overbless.Tests.PlayMode
{
    public sealed class M2IntegrationTests
    {
        private const string Room02ScenePath = "Assets/_Project/Scenes/Room_02.unity";
        private const string Room03ScenePath = "Assets/_Project/Scenes/Room_03.unity";
        private const float PositionTolerance = 0.001f;
        private const float EchoNoFireBeforeSeconds = 0.649f;
        private const float EchoTimingTolerance = 0.001f;

        private readonly List<InputDevice> inputDevicesToRemove = new List<InputDevice>();
        private float timeScaleBeforeTest;
        private bool audioPausedBeforeTest;
        private InputSettings.UpdateMode inputUpdateModeBeforeTest;
        private InputSettings.BackgroundBehavior inputBackgroundBehaviorBeforeTest;
        private InputSettings.EditorInputBehaviorInPlayMode editorInputBehaviorBeforeTest;
        private bool runInBackgroundBeforeTest;

        [SetUp]
        public void SetUp()
        {
            timeScaleBeforeTest = Time.timeScale;
            audioPausedBeforeTest = AudioListener.pause;
            inputUpdateModeBeforeTest = InputSystem.settings.updateMode;
            inputBackgroundBehaviorBeforeTest = InputSystem.settings.backgroundBehavior;
            editorInputBehaviorBeforeTest = InputSystem.settings.editorInputBehaviorInPlayMode;
            runInBackgroundBeforeTest = Application.runInBackground;

            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                yield return UnloadScene(Room02ScenePath);
                yield return UnloadScene(Room03ScenePath);

                for (var index = inputDevicesToRemove.Count - 1; index >= 0; index--)
                {
                    InputSystem.RemoveDevice(inputDevicesToRemove[index]);
                }

                inputDevicesToRemove.Clear();
                yield return null;
            }
            finally
            {
                Time.timeScale = timeScaleBeforeTest;
                AudioListener.pause = audioPausedBeforeTest;
                InputSystem.settings.updateMode = inputUpdateModeBeforeTest;
                InputSystem.settings.backgroundBehavior = inputBackgroundBehaviorBeforeTest;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInputBehaviorBeforeTest;
                Application.runInBackground = runInBackgroundBeforeTest;
            }
        }

        [UnityTest]
        public IEnumerator Room02AndRoom03_ExposeApprovedCurrentRoomContracts()
        {
            LoadedRoom room02 = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room02 = loadedRoom);

            AssertRoomContract(
                room02,
                "Room_02",
                M1RoomVariant.Room02,
                new Vector2(-6.4f, -2f),
                new Vector2(4.2f, -1.4f),
                new Vector2(-1.2f, -2f),
                new Vector2(5.8f, 2.5f),
                new Vector2(-3.4f, -2f),
                new Vector2(3.5f, 1.2f),
                "Room_03");
            Assert.That(FindGameObjectsInScene(room02.Scene, "WorldPillar"), Is.Empty);

            yield return UnloadScene(Room02ScenePath);

            LoadedRoom room03 = null;
            yield return LoadRoom(Room03ScenePath, loadedRoom => room03 = loadedRoom);

            AssertRoomContract(
                room03,
                "Room_03",
                M1RoomVariant.Room03,
                new Vector2(-6.4f, -1.8f),
                new Vector2(4.2f, -1.5f),
                new Vector2(-1.2f, -1.8f),
                new Vector2(5.8f, 2.4f),
                new Vector2(3.4f, 1.1f),
                new Vector2(5.4f, -0.1f),
                string.Empty);

            var pillars = FindGameObjectsInScene(room03.Scene, "WorldPillar");
            Assert.That(pillars.Count, Is.EqualTo(1));
            AssertWorldPillarContract(pillars[0]);
        }

        [UnityTest]
        public IEnumerator Room02_FiveDistinctEnemyDeathsProcessAllDeathsAndSpawnOnlyThreeSouls()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var processedDeaths = new List<DeathEvent>();
            var spawnedSouls = new List<SoulFragment>();
            room.RoomLifecycle.EnemyDeathProcessed += processedDeaths.Add;
            room.RoomLifecycle.SoulSpawned += spawnedSouls.Add;

            for (var index = 0; index < room.Enemies.Count; index++)
            {
                var enemy = room.Enemies[index];
                var damage = new DamageEvent(
                    10000 + index,
                    9000,
                    enemy.EntityId,
                    enemy.Health.CurrentHealth);

                Assert.That(
                    room.RoomLifecycle.TryApplyDamage(enemy.Health, damage),
                    Is.True,
                    $"Room lifecycle rejected the lethal event for {enemy.name}.");
            }

            room.RoomLifecycle.EnemyDeathProcessed -= processedDeaths.Add;
            room.RoomLifecycle.SoulSpawned -= spawnedSouls.Add;

            Assert.That(processedDeaths.Count, Is.EqualTo(5));
            for (var index = 0; index < room.Enemies.Count; index++)
            {
                var enemy = room.Enemies[index];
                Assert.That(enemy.Health.IsDead, Is.True, $"{enemy.name} did not die from its accepted lethal event.");
                Assert.That(CountDeathsFor(processedDeaths, enemy.EntityId), Is.EqualTo(1));
            }

            Assert.That(spawnedSouls.Count, Is.EqualTo(M1RoomDefinition.RequiredSoulCount));
            Assert.That(room.RoomLifecycle.SoulCount, Is.Zero, "Uncollected souls must not alter the collected total.");
        }

        [UnityTest]
        public IEnumerator Room02_EchoRepeatsOneIndependentLockedContextWithoutRetargetingAndPresenterMirrorsState()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var archer = room.ArcherA;
            var presenter = FindComponentInChildren<EchoProjectilePresenter>(archer.transform);
            Assert.That(presenter, Is.Not.Null);
            AssertEchoPresenterHasNoPhysicsOrDamage(presenter);

            var pendingRenderer = FindSpriteRenderer(presenter, "PendingLine");
            var projectileRenderer = FindSpriteRenderer(presenter, "ProjectileBody");
            var observation = new EchoObservation();
            archer.ProjectileFired += observation.RecordPrimary;
            archer.EchoProjectileFired += observation.RecordEcho;

            yield return ApplyEchoAndWaitForPending(room, archer);

            var primaryContext = observation.PrimaryContext;
            var pendingContext = archer.PendingEchoContext;
            var scheduledAt = observation.PrimaryFiredAt;
            var dueAt = archer.PendingEchoExecutionAt;

            Assert.That(observation.PrimaryFireCount, Is.EqualTo(1));
            Assert.That(primaryContext, Is.Not.Null);
            Assert.That(pendingContext, Is.Not.Null);
            Assert.That(pendingContext, Is.Not.SameAs(primaryContext));
            Assert.That(pendingContext.AttackInstanceId, Is.GreaterThan(0));
            Assert.That(pendingContext.AttackInstanceId, Is.Not.EqualTo(primaryContext.AttackInstanceId));
            Assert.That(dueAt - scheduledAt, Is.EqualTo(BlessingDefinition.EchoRepeatDelaySeconds).Within(EchoTimingTolerance));
            AssertRepeatMatchesLockedContext(primaryContext, pendingContext);
            Assert.That(pendingRenderer.enabled, Is.EqualTo(archer.IsEchoPending));
            Assert.That(projectileRenderer.enabled, Is.EqualTo(archer.IsEchoProjectileActive));

            room.Player.transform.position = new Vector3(7.5f, 4f, room.Player.transform.position.z);
            archer.transform.position = new Vector3(7.5f, -4f, archer.transform.position.z);
            Physics2D.SyncTransforms();
            AssertRepeatMatchesLockedContext(pendingContext, archer.PendingEchoContext);

            while (Time.time < scheduledAt + EchoNoFireBeforeSeconds)
            {
                Assert.That(observation.EchoFireCount, Is.Zero, "Echo fired before 0.649 seconds had elapsed.");
                yield return null;
            }

            yield return WaitForCondition(
                () => observation.EchoFireCount != 0,
                BlessingDefinition.EchoRepeatDelaySeconds + 0.5f,
                "Echo did not fire after its scheduled 0.65 second delay.");

            Assert.That(observation.EchoFireCount, Is.EqualTo(1));
            Assert.That(
                observation.EchoFiredAt,
                Is.GreaterThanOrEqualTo(scheduledAt + BlessingDefinition.EchoRepeatDelaySeconds - EchoTimingTolerance));
            Assert.That(observation.EchoFiredAt, Is.GreaterThanOrEqualTo(scheduledAt + EchoNoFireBeforeSeconds));
            Assert.That(observation.EchoContext, Is.SameAs(pendingContext));
            AssertRepeatMatchesLockedContext(pendingContext, observation.EchoContext);
            Assert.That(archer.IsEchoPending, Is.False);
            Assert.That(archer.IsEchoProjectileActive, Is.True);
            Assert.That(pendingRenderer.enabled, Is.EqualTo(archer.IsEchoPending));
            Assert.That(projectileRenderer.enabled, Is.EqualTo(archer.IsEchoProjectileActive));

            yield return new WaitForSeconds(0.1f);
            Assert.That(observation.EchoFireCount, Is.EqualTo(1), "One locked attack must schedule only one echo repeat.");

            archer.ProjectileFired -= observation.RecordPrimary;
            archer.EchoProjectileFired -= observation.RecordEcho;
        }

        [UnityTest]
        public IEnumerator Room02_EchoPendingRepeatCancelsWhenBlessingTargetIsRemoved()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var observation = new EchoObservation();
            var archer = room.ArcherA;
            archer.EchoProjectileFired += observation.RecordEcho;
            yield return ApplyEchoAndWaitForPending(room, archer);

            Assert.That(room.BlessingTargeting.DeregisterTarget(archer.EntityId), Is.True);
            yield return AssertEchoCancelledBeforeDue(archer, observation);
            archer.EchoProjectileFired -= observation.RecordEcho;
        }

        [UnityTest]
        public IEnumerator Room02_EchoPendingRepeatCancelsWhenArcherDies()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var observation = new EchoObservation();
            var archer = room.ArcherA;
            archer.EchoProjectileFired += observation.RecordEcho;
            yield return ApplyEchoAndWaitForPending(room, archer);

            var lethalDamage = new DamageEvent(20001, 9000, archer.EntityId, archer.Health.CurrentHealth);
            Assert.That(room.RoomLifecycle.TryApplyDamage(archer.Health, lethalDamage), Is.True);
            Assert.That(archer.Health.IsDead, Is.True);
            yield return AssertEchoCancelledBeforeDue(archer, observation);
            archer.EchoProjectileFired -= observation.RecordEcho;
        }

        [UnityTest]
        public IEnumerator Room02_EchoPendingRepeatCancelsWhenRoomRestarts()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var observation = new EchoObservation();
            var archer = room.ArcherA;
            archer.EchoProjectileFired += observation.RecordEcho;
            yield return ApplyEchoAndWaitForPending(room, archer);

            room.RestartController.RestartRoom();
            yield return AssertEchoCancelledBeforeDue(archer, observation);
            archer.EchoProjectileFired -= observation.RecordEcho;
        }

        [UnityTest]
        public IEnumerator Room02_EchoPendingRepeatCancelsWhenArcherDisables()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var observation = new EchoObservation();
            var archer = room.ArcherA;
            archer.EchoProjectileFired += observation.RecordEcho;
            yield return ApplyEchoAndWaitForPending(room, archer);

            archer.enabled = false;
            yield return AssertEchoCancelledBeforeDue(archer, observation);
            archer.EchoProjectileFired -= observation.RecordEcho;
        }

        [UnityTest]
        public IEnumerator Room03_WorldPillarStopsPrimaryAndEchoProjectilesBeforeTheyCrossItsCollider()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room03ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var pillar = FindGameObjectsInScene(room.Scene, "WorldPillar")[0];
            var pillarCollider = pillar.GetComponent<BoxCollider2D>();
            var archer = room.ArcherA;
            AttackContext primaryStoppedContext = null;
            AttackContext echoStoppedContext = null;
            var primaryStoppedPosition = Vector2.zero;
            var echoStoppedPosition = Vector2.zero;
            Action<AttackContext, Vector2> recordPrimaryStop = (context, position) =>
            {
                primaryStoppedContext = context;
                primaryStoppedPosition = position;
            };
            Action<AttackContext, Vector2> recordEchoStop = (context, position) =>
            {
                echoStoppedContext = context;
                echoStoppedPosition = position;
            };
            archer.ProjectileStopped += recordPrimaryStop;
            archer.EchoProjectileStopped += recordEchoStop;

            yield return ApplyEchoAndWaitForPending(room, archer);
            yield return WaitForCondition(
                () => primaryStoppedContext != null && echoStoppedContext != null,
                5f,
                "The pillar did not stop both the primary and repeated Echo projectiles.");

            archer.ProjectileStopped -= recordPrimaryStop;
            archer.EchoProjectileStopped -= recordEchoStop;

            Assert.That(primaryStoppedContext.NormalizedDirection.x, Is.LessThan(-0.9f));
            Assert.That(echoStoppedContext.AttackInstanceId, Is.Not.EqualTo(primaryStoppedContext.AttackInstanceId));
            AssertRepeatMatchesLockedContext(primaryStoppedContext, echoStoppedContext);
            Assert.That(primaryStoppedPosition.x, Is.GreaterThanOrEqualTo(pillarCollider.bounds.max.x - PositionTolerance));
            Assert.That(echoStoppedPosition.x, Is.GreaterThanOrEqualTo(pillarCollider.bounds.max.x - PositionTolerance));
            Assert.That(primaryStoppedPosition.x, Is.LessThan(primaryStoppedContext.Origin.x));
            Assert.That(echoStoppedPosition.x, Is.LessThan(echoStoppedContext.Origin.x));
        }

        [UnityTest]
        public IEnumerator Room02_ThrowingAttackObserversDoNotStrandOrDelayEcho()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var archer = room.ArcherA;
            var observation = new EchoObservation();
            var lockFailureRaised = false;
            var movedFailureRaised = false;
            Action<AttackContext> throwOnceOnLock = _ =>
            {
                if (!lockFailureRaised)
                {
                    lockFailureRaised = true;
                    archer.AttackState.BeginExecuting();
                    throw new InvalidOperationException("expected lock observer failure");
                }
            };
            Action<AttackContext, Vector2> throwOnceOnMove = (_, __) =>
            {
                if (!movedFailureRaised)
                {
                    movedFailureRaised = true;
                    throw new InvalidOperationException("expected projectile move observer failure");
                }
            };

            archer.AttackState.ContextLocked += throwOnceOnLock;
            archer.ProjectileMoved += throwOnceOnMove;
            archer.ProjectileFired += observation.RecordPrimary;
            archer.EchoProjectileFired += observation.RecordEcho;
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("expected lock observer failure"));
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("expected projectile move observer failure"));

            Assert.That(room.BlessingTargeting.Select(BlessingType.Echo), Is.True);
            Assert.That(room.BlessingTargeting.SetHoveredTarget(archer.EntityId), Is.True);
            Assert.That(room.BlessingTargeting.ApplyHoveredTarget(), Is.True);
            yield return WaitForCondition(
                () => observation.EchoFireCount == 1,
                3f,
                "Echo did not execute after isolated attack observers failed.");

            archer.AttackState.ContextLocked -= throwOnceOnLock;
            archer.ProjectileMoved -= throwOnceOnMove;
            archer.ProjectileFired -= observation.RecordPrimary;
            archer.EchoProjectileFired -= observation.RecordEcho;

            Assert.That(lockFailureRaised, Is.True);
            Assert.That(movedFailureRaised, Is.True);
            Assert.That(archer.CurrentAttackPhase, Is.Not.EqualTo(AttackPhase.Locked));
            Assert.That(observation.PrimaryFireCount, Is.EqualTo(1));
            Assert.That(observation.EchoFireCount, Is.EqualTo(1));
            Assert.That(
                observation.EchoFiredAt - observation.PrimaryFiredAt,
                Is.InRange(
                    BlessingDefinition.EchoRepeatDelaySeconds - EchoTimingTolerance,
                    BlessingDefinition.EchoRepeatDelaySeconds + 0.2f));
        }

        [UnityTest]
        public IEnumerator Room02_DeathObserverFailureStillCancelsPendingEcho()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var archer = room.ArcherA;
            yield return ApplyEchoAndWaitForPending(room, archer);

            var deathObserverFailureRaised = false;
            Action<AttackPhase> throwOnceOnIdle = phase =>
            {
                if (phase == AttackPhase.Idle && !deathObserverFailureRaised)
                {
                    deathObserverFailureRaised = true;
                    throw new InvalidOperationException("expected death attack-state observer failure");
                }
            };
            archer.AttackState.PhaseChanged += throwOnceOnIdle;

            var lethalDamage = new DamageEvent(20002, 9000, archer.EntityId, archer.Health.CurrentHealth);
            Assert.Catch<Exception>(() => room.RoomLifecycle.TryApplyDamage(archer.Health, lethalDamage));
            archer.AttackState.PhaseChanged -= throwOnceOnIdle;
            yield return null;

            Assert.That(deathObserverFailureRaised, Is.True);
            Assert.That(archer.Health.IsDead, Is.True);
            Assert.That(archer.CurrentAttackPhase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(archer.IsProjectileActive, Is.False);
            Assert.That(archer.IsEchoPending, Is.False);
            Assert.That(archer.IsEchoProjectileActive, Is.False);
        }

        [UnityTest]
        public IEnumerator Room02_RecoveryObserverFailureCannotBypassArcherCooldown()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var archer = room.ArcherA;
            for (var index = 0; index < room.Enemies.Count; index++)
            {
                if (room.Enemies[index] != archer)
                {
                    room.Enemies[index].enabled = false;
                }
            }

            var firedAt = new List<float>();
            archer.ProjectileFired += (_, __) => firedAt.Add(Time.time);
            var recoveryFailureRaised = false;
            Action<AttackPhase> throwOnceOnRecoveredIdle = phase =>
            {
                if (phase == AttackPhase.Idle && firedAt.Count > 0 && !recoveryFailureRaised)
                {
                    recoveryFailureRaised = true;
                    throw new InvalidOperationException("expected recovery observer failure");
                }
            };
            archer.AttackState.PhaseChanged += throwOnceOnRecoveredIdle;
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("expected recovery observer failure"));

            yield return WaitForCondition(
                () => firedAt.Count >= 2,
                8f,
                "Archer did not produce a second attack for cooldown verification.");

            archer.AttackState.PhaseChanged -= throwOnceOnRecoveredIdle;
            Assert.That(recoveryFailureRaised, Is.True);
            Assert.That(
                firedAt[1] - firedAt[0],
                Is.GreaterThanOrEqualTo(archer.RuntimeStats.AttackCooldown - EchoTimingTolerance));
        }

        private static void AssertRoomContract(
            LoadedRoom room,
            string definitionName,
            M1RoomVariant expectedVariant,
            Vector2 expectedPlayerPosition,
            Vector2 expectedDasherPosition,
            Vector2 expectedArcherAPosition,
            Vector2 expectedArcherBPosition,
            Vector2 expectedMinionAPosition,
            Vector2 expectedMinionBPosition,
            string expectedNextScene)
        {
            Assert.That(room.Binder, Is.Not.Null);
            Assert.That(room.Binder.IsInitialized, Is.True);
            Assert.That(room.RoomLifecycle, Is.Not.Null);
            Assert.That(room.Enemies.Count, Is.EqualTo(5));

            var definition = FindLoadedRoomDefinition(definitionName);
            Assert.That(definition.RoomVariant, Is.EqualTo(expectedVariant));
            definition.Validate();

            AssertPosition(room.Player.transform, expectedPlayerPosition, "Player");
            AssertPosition(FindEnemy(room.Enemies, "Dasher").transform, expectedDasherPosition, "Dasher");
            AssertPosition(FindEnemy(room.Enemies, "Archer_A").transform, expectedArcherAPosition, "Archer_A");
            AssertPosition(FindEnemy(room.Enemies, "Archer_B").transform, expectedArcherBPosition, "Archer_B");
            AssertPosition(FindEnemy(room.Enemies, "Minion_A").transform, expectedMinionAPosition, "Minion_A");
            AssertPosition(FindEnemy(room.Enemies, "Minion_B").transform, expectedMinionBPosition, "Minion_B");

            Assert.That(room.Hud, Is.Not.Null);
            Assert.That(room.Hud.IsBound, Is.True);
            Assert.That(room.Hud.IsViewConfigured, Is.True, "The third Echo HUD card must have its view references.");
            Assert.That(FindGameObjectsInScene(room.Scene, "EchoCard").Count, Is.EqualTo(1));
            Assert.That(room.Hud.TryGetState(out var hudState), Is.True);
            Assert.That(hudState.EchoAvailable, Is.True);
            Assert.That(hudState.EchoAvailable, Is.EqualTo(room.BlessingTargeting.IsAvailable(BlessingType.Echo)));

            Assert.That(room.SequenceController, Is.Not.Null);
            if (string.IsNullOrEmpty(expectedNextScene))
            {
                Assert.That(string.IsNullOrWhiteSpace(room.SequenceController.NextScene), Is.True);
            }
            else
            {
                Assert.That(room.SequenceController.NextScene, Is.EqualTo(expectedNextScene));
            }

            var echoPresenters = FindComponentsInScene<EchoProjectilePresenter>(room.Scene);
            Assert.That(echoPresenters.Count, Is.EqualTo(2));
            for (var index = 0; index < echoPresenters.Count; index++)
            {
                AssertEchoPresenterHasNoPhysicsOrDamage(echoPresenters[index]);
            }
        }

        private static void AssertWorldPillarContract(GameObject pillar)
        {
            Assert.That(LayerMask.NameToLayer("World"), Is.EqualTo(12));
            Assert.That(pillar.layer, Is.EqualTo(12));
            Assert.That(pillar.layer, Is.EqualTo(LayerMask.NameToLayer("World")));

            var collider = pillar.GetComponent<BoxCollider2D>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.size.x, Is.EqualTo(1.2f).Within(PositionTolerance));
            Assert.That(collider.size.y, Is.EqualTo(1.8f).Within(PositionTolerance));
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(pillar.GetComponent<Rigidbody2D>(), Is.Null);
            Assert.That(pillar.GetComponent<Health>(), Is.Null);
            var behaviours = pillar.GetComponents<MonoBehaviour>();
            for (var index = 0; index < behaviours.Length; index++)
            {
                Assert.That(behaviours[index] is IDamageable, Is.False);
                Assert.That(behaviours[index] is IDamageSource, Is.False);
            }
        }

        private static void AssertEchoPresenterHasNoPhysicsOrDamage(EchoProjectilePresenter presenter)
        {
            Assert.That(presenter.GetComponentsInChildren<Rigidbody2D>(true), Is.Empty);
            Assert.That(presenter.GetComponentsInChildren<Collider2D>(true), Is.Empty);
            Assert.That(presenter.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(presenter.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(presenter.GetComponentsInChildren<Health>(true), Is.Empty);

            var behaviours = presenter.GetComponentsInChildren<MonoBehaviour>(true);
            for (var index = 0; index < behaviours.Length; index++)
            {
                Assert.That(behaviours[index] is IDamageable, Is.False);
                Assert.That(behaviours[index] is IDamageSource, Is.False);
            }
        }

        private static IEnumerator ApplyEchoAndWaitForPending(
            LoadedRoom room,
            ArcherAI archer)
        {
            Assert.That(room.BlessingTargeting.Select(BlessingType.Echo), Is.True);
            Assert.That(room.BlessingTargeting.SetHoveredTarget(archer.EntityId), Is.True);
            Assert.That(room.BlessingTargeting.ApplyHoveredTarget(), Is.True);

            yield return WaitForCondition(
                () => archer.IsEchoPending,
                3f,
                "Echo did not schedule from the Archer's locked primary attack.");

            Assert.That(archer.PendingEchoContext, Is.Not.Null);
            yield return null;
        }

        private static IEnumerator AssertEchoCancelledBeforeDue(ArcherAI archer, EchoObservation observation)
        {
            Assert.That(archer.IsEchoPending, Is.False);
            Assert.That(archer.IsEchoProjectileActive, Is.False);

            yield return null;

            var presenter = FindComponentInChildren<EchoProjectilePresenter>(archer.transform);
            Assert.That(FindSpriteRenderer(presenter, "PendingLine").enabled, Is.EqualTo(archer.IsEchoPending));
            Assert.That(FindSpriteRenderer(presenter, "ProjectileBody").enabled, Is.EqualTo(archer.IsEchoProjectileActive));

            yield return new WaitForSeconds(BlessingDefinition.EchoRepeatDelaySeconds + 0.1f);
            Assert.That(observation.EchoFireCount, Is.Zero, "Cancelled Echo must not fire after the pending delay expires.");
            Assert.That(archer.IsEchoPending, Is.False);
            Assert.That(archer.IsEchoProjectileActive, Is.False);
        }

        private IEnumerator LoadRoom(string scenePath, Action<LoadedRoom> loaded)
        {
            yield return UnloadScene(scenePath);

#if UNITY_EDITOR
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
#else
            var load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
#endif
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            var scene = SceneManager.GetSceneByPath(scenePath);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            loaded(CreateLoadedRoom(scene));
        }

        private static IEnumerator UnloadScene(string scenePath)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                yield break;
            }

            var unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null)
            {
                yield return unload;
            }
        }

        private Mouse AddMouse()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            inputDevicesToRemove.Add(mouse);
            return mouse;
        }

        private static IEnumerator StartWithTrustedGesture(WebStartGate webGate, Mouse mouse)
        {
            Assert.That(webGate, Is.Not.Null);
            Assert.That(webGate.IsAwaitingGesture, Is.True);
            Assert.That(Time.timeScale, Is.Zero);

            yield return SetTrustedGestureState(webGate, mouse, false);
            yield return SetTrustedGestureState(webGate, mouse, true);

            Assert.That(webGate.IsStarted, Is.True);
            Assert.That(webGate.IsAwaitingGesture, Is.False);
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
        }

        private static IEnumerator SetTrustedGestureState(WebStartGate webGate, Mouse mouse, bool pressed)
        {
            mouse.MakeCurrent();
            InputSystem.EnableDevice(mouse);
            var state = new MouseState
            {
                position = mouse.position.ReadValue()
            };
            if (pressed)
            {
                state = state.WithButton(MouseButton.Left);
            }

            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
            webGate.SendMessage("Update", SendMessageOptions.RequireReceiver);
            yield return null;
        }

        private static IEnumerator WaitForCondition(Func<bool> condition, float timeoutSeconds, string failureMessage)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, failureMessage);
        }

        private static LoadedRoom CreateLoadedRoom(Scene scene)
        {
            var enemies = FindComponentsInScene<EnemyBase>(scene);
            Assert.That(enemies.Count, Is.EqualTo(5));

            var archerA = FindEnemy(enemies, "Archer_A") as ArcherAI;
            Assert.That(archerA, Is.Not.Null);

            return new LoadedRoom(
                scene,
                FindComponentInScene<M1SceneRuntimeBinder>(scene),
                FindComponentInScene<M1RoomLifecycle>(scene),
                FindComponentInScene<BlessingTargeting>(scene),
                FindComponentInScene<HUDController>(scene),
                FindComponentInScene<WebStartGate>(scene),
                FindComponentInScene<RoomRestartController>(scene),
                FindComponentInScene<RoomSequenceController>(scene),
                FindComponentInScene<PlayerLifeCycle>(scene),
                enemies,
                archerA);
        }

        private static M1RoomDefinition FindLoadedRoomDefinition(string definitionName)
        {
            var definitions = Resources.FindObjectsOfTypeAll<M1RoomDefinition>();
            for (var index = 0; index < definitions.Length; index++)
            {
                if (definitions[index].name == definitionName)
                {
                    return definitions[index];
                }
            }

            Assert.Fail($"Loaded scene did not retain its {definitionName} room definition.");
            return null;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            var components = FindComponentsInScene<T>(scene);
            Assert.That(components.Count, Is.EqualTo(1), $"Expected exactly one {typeof(T).Name} in {scene.path}.");
            return components[0];
        }

        private static T FindComponentInChildren<T>(Transform root) where T : Component
        {
            var components = root.GetComponentsInChildren<T>(true);
            Assert.That(components.Length, Is.EqualTo(1), $"Expected exactly one {typeof(T).Name} under {root.name}.");
            return components[0];
        }

        private static List<T> FindComponentsInScene<T>(Scene scene) where T : Component
        {
            var components = new List<T>();
            var roots = scene.GetRootGameObjects();
            for (var index = 0; index < roots.Length; index++)
            {
                components.AddRange(roots[index].GetComponentsInChildren<T>(true));
            }

            return components;
        }

        private static List<GameObject> FindGameObjectsInScene(Scene scene, string objectName)
        {
            var matches = new List<GameObject>();
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                for (var transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                {
                    if (transforms[transformIndex].name == objectName)
                    {
                        matches.Add(transforms[transformIndex].gameObject);
                    }
                }
            }

            return matches;
        }

        private static EnemyBase FindEnemy(IReadOnlyList<EnemyBase> enemies, string name)
        {
            for (var index = 0; index < enemies.Count; index++)
            {
                if (enemies[index].name == name)
                {
                    return enemies[index];
                }
            }

            Assert.Fail($"Scene enemy '{name}' is missing.");
            return null;
        }

        private static SpriteRenderer FindSpriteRenderer(EchoProjectilePresenter presenter, string objectName)
        {
            var renderers = presenter.GetComponentsInChildren<SpriteRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].gameObject.name == objectName)
                {
                    return renderers[index];
                }
            }

            Assert.Fail($"Echo presenter is missing its {objectName} renderer.");
            return null;
        }

        private static void AssertPosition(Transform transform, Vector2 expected, string actorName)
        {
            Assert.That(
                Vector2.Distance(transform.position, expected),
                Is.LessThanOrEqualTo(PositionTolerance),
                $"{actorName} spawn does not match the canonical room position.");
        }

        private static int CountDeathsFor(IReadOnlyList<DeathEvent> deaths, int entityId)
        {
            var count = 0;
            for (var index = 0; index < deaths.Count; index++)
            {
                if (deaths[index].EntityId == entityId)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertRepeatMatchesLockedContext(AttackContext expected, AttackContext actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.AttackerEntityId, Is.EqualTo(expected.AttackerEntityId));
            Assert.That(actual.LockedAt, Is.EqualTo(expected.LockedAt));
            Assert.That(actual.Origin, Is.EqualTo(expected.Origin));
            Assert.That(actual.NormalizedDirection, Is.EqualTo(expected.NormalizedDirection));
            Assert.That(actual.Shape, Is.EqualTo(expected.Shape));
            Assert.That(actual.Range, Is.EqualTo(expected.Range));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Damage, Is.EqualTo(expected.Damage));
            Assert.That(actual.TargetMask.value, Is.EqualTo(expected.TargetMask.value));
        }

        private sealed class LoadedRoom
        {
            public LoadedRoom(
                Scene scene,
                M1SceneRuntimeBinder binder,
                M1RoomLifecycle roomLifecycle,
                BlessingTargeting blessingTargeting,
                HUDController hud,
                WebStartGate webGate,
                RoomRestartController restartController,
                RoomSequenceController sequenceController,
                PlayerLifeCycle player,
                List<EnemyBase> enemies,
                ArcherAI archerA)
            {
                Scene = scene;
                Binder = binder;
                RoomLifecycle = roomLifecycle;
                BlessingTargeting = blessingTargeting;
                Hud = hud;
                WebGate = webGate;
                RestartController = restartController;
                SequenceController = sequenceController;
                Player = player;
                Enemies = enemies;
                ArcherA = archerA;
            }

            public Scene Scene { get; }
            public M1SceneRuntimeBinder Binder { get; }
            public M1RoomLifecycle RoomLifecycle { get; }
            public BlessingTargeting BlessingTargeting { get; }
            public HUDController Hud { get; }
            public WebStartGate WebGate { get; }
            public RoomRestartController RestartController { get; }
            public RoomSequenceController SequenceController { get; }
            public PlayerLifeCycle Player { get; }
            public List<EnemyBase> Enemies { get; }
            public ArcherAI ArcherA { get; }
        }

        private sealed class EchoObservation
        {
            public int PrimaryFireCount { get; private set; }
            public AttackContext PrimaryContext { get; private set; }
            public float PrimaryFiredAt { get; private set; }
            public int EchoFireCount { get; private set; }
            public AttackContext EchoContext { get; private set; }
            public float EchoFiredAt { get; private set; }

            public void RecordPrimary(AttackContext context, Vector2 position)
            {
                PrimaryFireCount++;
                if (PrimaryContext == null)
                {
                    PrimaryContext = context;
                    PrimaryFiredAt = Time.time;
                }
            }

            public void RecordEcho(AttackContext context, Vector2 position)
            {
                EchoFireCount++;
                if (EchoContext == null)
                {
                    EchoContext = context;
                    EchoFiredAt = Time.time;
                }
            }
        }
    }
}
