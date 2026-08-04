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
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Overbless.Tests.PlayMode
{
    public sealed class M2IntegrationTests
    {
        private const string Room02ScenePath = "Assets/_Project/Scenes/Room_02.unity";
        private const string Room03ScenePath = "Assets/_Project/Scenes/Room_03.unity";
        private const float PositionTolerance = 0.001f;
        private const float EchoNoFireBeforeSeconds = 0.649f;
        private const float EchoTimingTolerance = 0.001f;
#if UNITY_EDITOR
        private const string M1GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";
        private const string M1DasherPrefabPath = "Assets/_Project/Prefabs/M1/Dasher.prefab";
        private const string M1ArcherPrefabPath = "Assets/_Project/Prefabs/M1/Archer.prefab";
        private const string M1MinionPrefabPath = "Assets/_Project/Prefabs/M1/Minion.prefab";
        private const string M2DasherPrefabPath = "Assets/_Project/Prefabs/M2/Dasher.prefab";
        private const string M2ArcherPrefabPath = "Assets/_Project/Prefabs/M2/Archer.prefab";
        private const string M2MinionPrefabPath = "Assets/_Project/Prefabs/M2/Minion.prefab";
        private const string DasherAnimationSetPath =
            "Assets/_Project/Data/Animations/DasherDirectionalAnimationSet.asset";
        private const string ArcherAnimationSetPath =
            "Assets/_Project/Data/Animations/ArcherDirectionalAnimationSet.asset";
        private const string MinionAnimationSetPath =
            "Assets/_Project/Data/Animations/MinionDirectionalAnimationSet.asset";
        private const string DasherAnimationSetGuid = "de31bab5f86db3b4b93ff29ee5d5e7bd";
        private const string ArcherAnimationSetGuid = "6aa8444b1250c9d428491b9aec9da017";
        private const string MinionAnimationSetGuid = "f27245187b875204d94cb75d9482a7f9";

        private static readonly CharacterDirection[] MonsterAnimationDirections =
        {
            CharacterDirection.South,
            CharacterDirection.North,
            CharacterDirection.East,
            CharacterDirection.West,
            CharacterDirection.SouthEast,
            CharacterDirection.SouthWest,
            CharacterDirection.NorthEast,
            CharacterDirection.NorthWest
        };

        private static readonly CharacterAnimationState[] MonsterAnimationStates =
        {
            CharacterAnimationState.Idle,
            CharacterAnimationState.Walk,
            CharacterAnimationState.Run,
            CharacterAnimationState.AttackCharge,
            CharacterAnimationState.AttackExecute,
            CharacterAnimationState.Recover,
            CharacterAnimationState.Hit,
            CharacterAnimationState.Death
        };
#endif

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
        public IEnumerator Room02AndRoom03_M2MonsterPrefabsShareV002AnimationSetsWithM1AndKeepOwnershipIsolated()
        {
#if UNITY_EDITOR
            LoadedRoom room02 = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room02 = loadedRoom);
            AssertM2MonsterAnimationBindings(room02);
            yield return UnloadScene(Room02ScenePath);

            LoadedRoom room03 = null;
            yield return LoadRoom(Room03ScenePath, loadedRoom => room03 = loadedRoom);
            AssertM2MonsterAnimationBindings(room03);
#else
            Assert.Ignore("Prefab and GUID ownership verification requires Unity Editor asset access.");
            yield break;
#endif
        }


        [UnityTest]
        public IEnumerator Room02_OpenExitEntryCompletesSequenceBeforeRoom03LoadsWithM2Scope()
        {
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);

            var exitGate = FindComponentInScene<ExitGate>(room.Scene);
            Assert.That(exitGate, Is.Not.Null);
            var completed = false;
            room.SequenceController.Completed += () => completed = true;
#if UNITY_EDITOR
            var sequenceSerialized = new SerializedObject(room.SequenceController);
            sequenceSerialized.FindProperty("nextScene").stringValue = string.Empty;
            sequenceSerialized.ApplyModifiedPropertiesWithoutUndo();
#else
            Assert.Ignore("Scene-transition verification requires Unity Editor scene loading.");
#endif
            Assert.That(exitGate.Open(), Is.True);
            Assert.That(exitGate.TryEnter(room.Player), Is.True);
            Assert.That(completed, Is.True, "Room_02 exit entry must complete the configured sequence exactly once.");
            Assert.That(room.SequenceController.HasHandledEntry, Is.True);

#if UNITY_EDITOR
            var loadRoom03 = EditorSceneManager.LoadSceneAsyncInPlayMode(
                Room03ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            Assert.That(loadRoom03, Is.Not.Null);
            while (!loadRoom03.isDone)
            {
                yield return null;
            }
#endif
            yield return UnloadScene(Room02ScenePath);

            var loadedRoom03 = SceneManager.GetSceneByPath(Room03ScenePath);
            Assert.That(loadedRoom03.isLoaded, Is.True);
            Assert.That(SceneManager.GetSceneByPath(Room02ScenePath).isLoaded, Is.False);
            Assert.That(FindComponentInScene<BlessingTargeting>(loadedRoom03).EchoEnabled, Is.True);
            Assert.That(FindGameObjectsInScene(loadedRoom03, "WorldPillar").Count, Is.EqualTo(1));
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

            Physics2D.SyncTransforms();
            var worldMask = LayerMask.GetMask("World");
            var bounds = pillarCollider.bounds;
            var leftEdgeHit = Physics2D.Raycast(
                new Vector2(bounds.min.x - 1f, bounds.center.y),
                Vector2.right,
                3f,
                worldMask);
            var rightEdgeHit = Physics2D.Raycast(
                new Vector2(bounds.max.x + 1f, bounds.center.y),
                Vector2.left,
                3f,
                worldMask);
            Assert.That(leftEdgeHit.collider, Is.SameAs(pillarCollider));
            Assert.That(rightEdgeHit.collider, Is.SameAs(pillarCollider));
            Assert.That(leftEdgeHit.point.x, Is.EqualTo(bounds.min.x).Within(0.01f));
            Assert.That(rightEdgeHit.point.x, Is.EqualTo(bounds.max.x).Within(0.01f));

            var pillarRenderer = pillar.GetComponentInChildren<SpriteRenderer>();
            var playerRenderer = room.Player.GetComponentInChildren<SpriteRenderer>();
            Assert.That(
                SortingLayer.GetLayerValueFromName(playerRenderer.sortingLayerName),
                Is.GreaterThan(SortingLayer.GetLayerValueFromName(pillarRenderer.sortingLayerName)),
                "Actor-versus-pillar depth ordering must be explicit and stable.");

            for (var enemyIndex = 0; enemyIndex < room.Enemies.Count; enemyIndex++)
            {
                room.Enemies[enemyIndex].enabled = false;
            }

            var playerHealth = room.Player.GetComponent<Health>();
            var healthBeforePillarContact = playerHealth.CurrentHealth;
            room.Player.transform.position = bounds.center;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(
                playerHealth.CurrentHealth,
                Is.EqualTo(healthBeforePillarContact),
                "Static pillar contact must not damage the player.");
        }
        [UnityTest]
        public IEnumerator Room03_EchoAndPillarCollisionKeepArcherIntentAndVisualDirectionAIOwned()
        {
            var mouse = AddMouse();
            LoadedRoom room = null;
            yield return LoadRoom(Room03ScenePath, loadedRoom => room = loadedRoom);
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            var pillar = FindGameObjectsInScene(room.Scene, "WorldPillar")[0];
            var pillarCollider = pillar.GetComponent<BoxCollider2D>();
            var archer = room.ArcherA;
            var archerCollider = archer.GetComponent<Collider2D>();
            var animator = FindComponentInChildren<DirectionalSpriteAnimator>(archer.transform);
            Assert.That(pillarCollider, Is.Not.Null);
            Assert.That(archerCollider, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);

            var pillarBounds = pillarCollider.bounds;
            room.Player.transform.position = new Vector3(-7.5f, pillarBounds.center.y, room.Player.transform.position.z);
            archer.transform.position = new Vector3(
                pillarBounds.max.x + archerCollider.bounds.extents.x + 0.02f,
                pillarBounds.center.y,
                archer.transform.position.z);
            Physics2D.SyncTransforms();

            var primaryStopped = false;
            var echoStopped = false;
            var directionsAtPillarStops = new List<CharacterDirection>();
            var facingChanges = new List<Vector2>();
            var locomotionChanges = new List<LocomotionMode>();
            Action<AttackContext, Vector2> recordPrimaryStop = (_, __) =>
            {
                primaryStopped = true;
                directionsAtPillarStops.Add(animator.CurrentDirection);
            };
            Action<AttackContext, Vector2> recordEchoStop = (_, __) =>
            {
                echoStopped = true;
                directionsAtPillarStops.Add(animator.CurrentDirection);
            };
            archer.IntendedFacingChanged += facingChanges.Add;
            archer.LocomotionModeChanged += locomotionChanges.Add;
            archer.ProjectileStopped += recordPrimaryStop;
            archer.EchoProjectileStopped += recordEchoStop;

            yield return WaitForCondition(
                () => archer.CurrentAttackPhase == AttackPhase.Warning &&
                      archer.CurrentLocomotionMode == LocomotionMode.Walk &&
                      animator.CurrentState == CharacterAnimationState.AttackCharge,
                2f,
                "Archer AI did not own a walking warning state while approaching the pillar.");
            AssertArcherFacingAndVisualDirectionRemainWest(archer, animator);

            yield return ApplyEchoAndWaitForPending(room, archer);
            yield return WaitForCondition(
                () => primaryStopped && echoStopped,
                3f,
                "The pillar did not stop both Echo projectiles in the AI-intent scenario.");

            yield return WaitForCondition(
                () => archer.CurrentAttackPhase == AttackPhase.Idle &&
                      archer.CurrentLocomotionMode == LocomotionMode.Walk &&
                      animator.CurrentState == CharacterAnimationState.Walk,
                3f,
                "Archer AI did not resume its collision-blocked walking intent after Echo resolved.");

            for (var sample = 0; sample < 5; sample++)
            {
                AssertArcherFacingAndVisualDirectionRemainWest(archer, animator);
                Assert.That(
                    archerCollider.Distance(pillarCollider).distance,
                    Is.LessThanOrEqualTo(0.02f),
                    "Archer should remain collision-blocked by the pillar while its AI retains walk intent.");
                yield return null;
            }

            archer.IntendedFacingChanged -= facingChanges.Add;
            archer.LocomotionModeChanged -= locomotionChanges.Add;
            archer.ProjectileStopped -= recordPrimaryStop;
            archer.EchoProjectileStopped -= recordEchoStop;

            Assert.That(directionsAtPillarStops, Is.EqualTo(new[]
            {
                CharacterDirection.West,
                CharacterDirection.West
            }));
            for (var index = 0; index < facingChanges.Count; index++)
            {
                Assert.That(facingChanges[index].x, Is.LessThan(-0.99f));
                Assert.That(Mathf.Abs(facingChanges[index].y), Is.LessThan(0.01f));
            }

            for (var index = 0; index < locomotionChanges.Count; index++)
            {
                Assert.That(
                    locomotionChanges[index],
                    Is.EqualTo(LocomotionMode.Walk).Or.EqualTo(LocomotionMode.Idle),
                    "Pillar contact must not synthesize a locomotion mode outside the Archer AI state machine.");
            }
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

            Assert.That(room.BlessingTargeting, Is.Not.Null);
            Assert.That(room.BlessingTargeting.EchoEnabled, Is.True, $"{definitionName} must explicitly enable Echo.");
            Assert.That(room.BlessingTargeting.IsAvailable(BlessingType.Echo), Is.True);

            Assert.That(room.Hud, Is.Not.Null);
            Assert.That(room.Hud.IsBound, Is.True);
            Assert.That(room.Hud.IsViewConfigured, Is.True, "The third Echo HUD card must have its view references.");
            var echoCards = FindGameObjectsInScene(room.Scene, "EchoCard");
            Assert.That(echoCards.Count, Is.EqualTo(1));
            var echoCard = echoCards[0];
            Assert.That(echoCard.GetComponent<Image>(), Is.Not.Null);
            var echoIcon = GetRequiredDirectChildComponent<Image>(echoCard.transform, "Icon");
            Assert.That(echoIcon.sprite, Is.Not.Null, "The Echo card icon must be assigned.");
            Assert.That(GetRequiredDirectChildComponent<Text>(echoCard.transform, "Title").text, Is.EqualTo("3 ECHO"));
            Assert.That(
                GetRequiredDirectChildComponent<Text>(echoCard.transform, "Detail").text,
                Is.EqualTo("REPEAT LOCKED ATTACK"));
            Assert.That(GetRequiredDirectChildComponent<Text>(echoCard.transform, "Status"), Is.Not.Null);
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

#if UNITY_EDITOR
            AssertM2PrefabAndVisualOwnership(room, echoIcon, echoPresenters);
#endif
        }

        private static void AssertWorldPillarContract(GameObject pillar)
        {
            Assert.That(LayerMask.NameToLayer("World"), Is.EqualTo(12));
            Assert.That(pillar.layer, Is.EqualTo(12));
            Assert.That(pillar.layer, Is.EqualTo(LayerMask.NameToLayer("World")));
            Assert.That(pillar.isStatic, Is.True);

            var collider = pillar.GetComponent<BoxCollider2D>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.size.x, Is.EqualTo(1.2f).Within(PositionTolerance));
            Assert.That(collider.size.y, Is.EqualTo(1.8f).Within(PositionTolerance));
            Assert.That(collider.offset.x, Is.EqualTo(0f).Within(PositionTolerance));
            Assert.That(collider.offset.y, Is.EqualTo(0.28f).Within(PositionTolerance));
            Assert.That(collider.isTrigger, Is.False);

            var renderer = pillar.GetComponentInChildren<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sprite, Is.Not.Null);
            Assert.That(renderer.spriteSortPoint, Is.EqualTo(SpriteSortPoint.Pivot));
            Assert.That(renderer.sortingLayerName, Is.EqualTo("World"));
            Assert.That(renderer.drawMode, Is.EqualTo(SpriteDrawMode.Simple));
            Assert.That(renderer.transform.localScale.x, Is.EqualTo(1.2f).Within(PositionTolerance));
            Assert.That(renderer.transform.localScale.y, Is.EqualTo(1.8f).Within(PositionTolerance));

#if UNITY_EDITOR
            AssertSpriteAssetPath(
                renderer.sprite,
                "Assets/_Project/Art/M2Production/Environment/env_static_world_pillar_south_a_v002.png");
#endif

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
#if UNITY_EDITOR
        private static void AssertM2PrefabAndVisualOwnership(
            LoadedRoom room,
            Image echoIcon,
            IReadOnlyList<EchoProjectilePresenter> echoPresenters)
        {

            AssertSpriteAssetPath(
                echoIcon.sprite,
                "Assets/_Project/Art/M2Production/UI/ui_icon_bless_echo_a_v002.png");

            var indicators = FindComponentsInScene<BlessingIndicator>(room.Scene);
            Assert.That(indicators.Count, Is.EqualTo(5));
            for (var index = 0; index < indicators.Count; index++)
            {
                var echoRenderer = GetRequiredDirectChildComponent<SpriteRenderer>(indicators[index].transform, "Echo");
                AssertSpriteAssetPath(
                    echoRenderer.sprite,
                    "Assets/_Project/Art/M2Production/UI/ui_icon_echo_status_a_v002.png");
            }

            for (var index = 0; index < echoPresenters.Count; index++)
            {
                AssertSpriteAssetPath(
                    FindSpriteRenderer(echoPresenters[index], "PendingLine").sprite,
                    "Assets/_Project/Art/M2Production/VFX/vfx_echo_line_telegraph_a_v002.png");
                AssertSpriteAssetPath(
                    FindSpriteRenderer(echoPresenters[index], "ProjectileBody").sprite,
                    "Assets/_Project/Art/M2Production/VFX/vfx_echo_double_silhouette_a_v002.png");
            }
        }

        private static void AssertSpriteAssetPath(Sprite sprite, string expectedPath)
        {
            Assert.That(sprite, Is.Not.Null, $"Expected sprite at '{expectedPath}'.");
            Assert.That(
                AssetDatabase.GetAssetPath(sprite),
                Is.EqualTo(expectedPath),
                $"Sprite '{sprite.name}' must resolve to its approved v002 path.");
        }
        private static void AssertM2MonsterAnimationBindings(LoadedRoom room)
        {
            Assert.That(
                AssetDatabase.AssetPathToGUID(room.Scene.path),
                Is.Not.Empty,
                $"{room.Scene.path} must remain an independently owned M2 scene asset.");
            Assert.That(
                AssetDatabase.AssetPathToGUID(room.Scene.path),
                Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(M1GuidedScenePath)));

            AssertMonsterPrefabPair(
                M1DasherPrefabPath,
                M2DasherPrefabPath,
                DasherAnimationSetPath,
                DasherAnimationSetGuid,
                false);
            AssertMonsterPrefabPair(
                M1ArcherPrefabPath,
                M2ArcherPrefabPath,
                ArcherAnimationSetPath,
                ArcherAnimationSetGuid,
                false);
            AssertMonsterPrefabPair(
                M1MinionPrefabPath,
                M2MinionPrefabPath,
                MinionAnimationSetPath,
                MinionAnimationSetGuid,
                true);

            AssertM2EnemyAnimationBinding(
                FindEnemy(room.Enemies, "Dasher"),
                M1DasherPrefabPath,
                M2DasherPrefabPath,
                DasherAnimationSetPath,
                DasherAnimationSetGuid,
                false);
            AssertM2EnemyAnimationBinding(
                FindEnemy(room.Enemies, "Archer_A"),
                M1ArcherPrefabPath,
                M2ArcherPrefabPath,
                ArcherAnimationSetPath,
                ArcherAnimationSetGuid,
                false);
            AssertM2EnemyAnimationBinding(
                FindEnemy(room.Enemies, "Archer_B"),
                M1ArcherPrefabPath,
                M2ArcherPrefabPath,
                ArcherAnimationSetPath,
                ArcherAnimationSetGuid,
                false);
            AssertM2EnemyAnimationBinding(
                FindEnemy(room.Enemies, "Minion_A"),
                M1MinionPrefabPath,
                M2MinionPrefabPath,
                MinionAnimationSetPath,
                MinionAnimationSetGuid,
                true);
            AssertM2EnemyAnimationBinding(
                FindEnemy(room.Enemies, "Minion_B"),
                M1MinionPrefabPath,
                M2MinionPrefabPath,
                MinionAnimationSetPath,
                MinionAnimationSetGuid,
                true);
        }

        private static void AssertMonsterPrefabPair(
            string m1PrefabPath,
            string m2PrefabPath,
            string animationSetPath,
            string expectedAnimationSetGuid,
            bool isMinion)
        {
            var m1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m1PrefabPath);
            var m2Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m2PrefabPath);
            var expectedAnimationSet = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(animationSetPath);
            Assert.That(m1Prefab, Is.Not.Null);
            Assert.That(m2Prefab, Is.Not.Null);
            Assert.That(expectedAnimationSet, Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(m1PrefabPath), Is.Not.Empty);
            Assert.That(AssetDatabase.AssetPathToGUID(m2PrefabPath), Is.Not.Empty);
            Assert.That(
                AssetDatabase.AssetPathToGUID(m2PrefabPath),
                Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(m1PrefabPath)),
                $"M2 prefab '{m2PrefabPath}' must remain isolated from its M1 counterpart.");

            var m1Animator = FindComponentInChildren<DirectionalSpriteAnimator>(m1Prefab.transform);
            var m2Animator = FindComponentInChildren<DirectionalSpriteAnimator>(m2Prefab.transform);
            Assert.That(m1Animator.AnimationSet, Is.SameAs(expectedAnimationSet));
            Assert.That(m2Animator.AnimationSet, Is.SameAs(expectedAnimationSet));
            AssertMonsterAnimationSetContract(expectedAnimationSet, expectedAnimationSetGuid, isMinion);
        }

        private static void AssertM2EnemyAnimationBinding(
            EnemyBase enemy,
            string m1PrefabPath,
            string m2PrefabPath,
            string animationSetPath,
            string expectedAnimationSetGuid,
            bool isMinion)
        {
            var expectedAnimationSet = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(animationSetPath);
            var animator = FindComponentInChildren<DirectionalSpriteAnimator>(enemy.transform);
            Assert.That(animator.AnimationSet, Is.SameAs(expectedAnimationSet));
            AssertMonsterAnimationSetContract(animator.AnimationSet, expectedAnimationSetGuid, isMinion);
        }

        private static void AssertMonsterAnimationSetContract(
            DirectionalAnimationSet animationSet,
            string expectedAnimationSetGuid,
            bool isMinion)
        {
            Assert.That(animationSet, Is.Not.Null);
            Assert.That(
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(animationSet)),
                Is.EqualTo(expectedAnimationSetGuid));
            animationSet.Validate();
            Assert.That(animationSet.ClipCount, Is.EqualTo(64));

            for (var stateIndex = 0; stateIndex < MonsterAnimationStates.Length; stateIndex++)
            {
                for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
                {
                    Assert.That(
                        animationSet.Supports(
                            MonsterAnimationStates[stateIndex],
                            MonsterAnimationDirections[directionIndex]),
                        Is.True,
                        $"{animationSet.Role} is missing {MonsterAnimationStates[stateIndex]}/{MonsterAnimationDirections[directionIndex]}.");
                }
            }

            for (var directionIndex = 0; directionIndex < MonsterAnimationDirections.Length; directionIndex++)
            {
                var direction = MonsterAnimationDirections[directionIndex];
                AssertMonsterClipContract(
                    animationSet,
                    CharacterAnimationState.Walk,
                    direction,
                    6,
                    8f,
                    true);
                AssertMonsterClipContract(
                    animationSet,
                    CharacterAnimationState.Run,
                    direction,
                    8,
                    12f,
                    true);
                AssertMonsterClipContract(
                    animationSet,
                    CharacterAnimationState.AttackCharge,
                    direction,
                    6,
                    8f,
                    false);
                AssertMonsterClipContract(
                    animationSet,
                    CharacterAnimationState.AttackExecute,
                    direction,
                    6,
                    isMinion ? 24f : 14f,
                    false);
                AssertMonsterClipContract(
                    animationSet,
                    CharacterAnimationState.Recover,
                    direction,
                    4,
                    7f,
                    false);
            }
        }

        private static void AssertMonsterClipContract(
            DirectionalAnimationSet animationSet,
            CharacterAnimationState state,
            CharacterDirection direction,
            int expectedFrameCount,
            float expectedFramesPerSecond,
            bool expectedLoop)
        {
            var clip = animationSet.GetClip(state, direction);
            Assert.That(clip.FrameCount, Is.EqualTo(expectedFrameCount));
            Assert.That(clip.FramesPerSecond, Is.EqualTo(expectedFramesPerSecond).Within(PositionTolerance));
            Assert.That(clip.Loop, Is.EqualTo(expectedLoop));
        }
#endif
        private static void AssertArcherFacingAndVisualDirectionRemainWest(
            ArcherAI archer,
            DirectionalSpriteAnimator animator)
        {
            Assert.That(archer.IntendedFacing.x, Is.LessThan(-0.99f));
            Assert.That(Mathf.Abs(archer.IntendedFacing.y), Is.LessThan(0.01f));
            Assert.That(animator.CurrentDirection, Is.EqualTo(CharacterDirection.West));
        }

        /// <summary>
        /// The character card is approved for first encounter, blessing choice, victory and
        /// defeat only. Every assertion here reads the card in the same call that raises it,
        /// so live enemy behaviour cannot race the expectation.
        /// </summary>
        [UnityTest]
        public IEnumerator Room02_CharacterCardOpensOnlyAtApprovedMomentsAndIntroducesEachRivalOnce()
        {
            LoadedRoom room = null;
            yield return LoadRoom(Room02ScenePath, loadedRoom => room = loadedRoom);

            var presenter = FindComponentInScene<CharacterAppealPresenter>(room.Scene);
            Assert.That(presenter, Is.Not.Null, "Room_02 must carry the character card.");
            Assert.That(
                presenter.IsCardVisible,
                Is.False,
                $"No card may open before the trusted gesture, but it shows '{presenter.CurrentIdentity?.DisplayName}'.");
            Assert.That(presenter.CurrentIdentity, Is.Null);
            Assert.That(
                presenter.IsIntroduced(CharacterRole.Archer),
                Is.False,
                "A warning raised before the run starts must not spend an introduction.");
            Assert.That(presenter.IsIntroduced(CharacterRole.Dasher), Is.False);
            Assert.That(presenter.IsIntroduced(CharacterRole.Minion), Is.False);

            var dasher = FindEnemy(room.Enemies, "Dasher");
            var minionA = FindEnemy(room.Enemies, "Minion_A");
            var minionB = FindEnemy(room.Enemies, "Minion_B");

            var mouse = AddMouse();
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            // Blessing choice. The signal is synchronous, so the card is read before any
            // enemy can tick again.
            Assert.That(room.BlessingTargeting.Select(BlessingType.Haste), Is.True);
            Assert.That(room.BlessingTargeting.SetHoveredTarget(minionA.EntityId), Is.True);
            Assert.That(room.BlessingTargeting.ApplyHoveredTarget(), Is.True);
            Assert.That(presenter.IsCardVisible, Is.True, "A blessing choice is an approved card moment.");
            Assert.That(presenter.CurrentIdentity.DisplayName, Is.EqualTo("MOKO"));
            Assert.That(presenter.CurrentExpression, Is.EqualTo(CharacterExpression.Confident));
            Assert.That(presenter.IsIntroduced(CharacterRole.Minion), Is.True);

            // First encounter. The Dasher spawns outside its engagement range in Room_02, so
            // this warning is the first one it commits to.
            Assert.That(dasher.CurrentAttackPhase, Is.EqualTo(AttackPhase.Idle));
            Assert.That(presenter.IsIntroduced(CharacterRole.Dasher), Is.False);
            dasher.AttackState.BeginWarning(dasher.RuntimeStats.WarningDuration);
            Assert.That(presenter.IsCardVisible, Is.True, "A first warning must introduce its rival.");
            Assert.That(presenter.CurrentIdentity.DisplayName, Is.EqualTo("VERA"));
            Assert.That(presenter.CurrentExpression, Is.EqualTo(CharacterExpression.Neutral));
            Assert.That(presenter.IsIntroduced(CharacterRole.Dasher), Is.True);

            // The second minion is the same cast member, so it must not introduce Moko twice.
            if (minionB.CurrentAttackPhase == AttackPhase.Idle)
            {
                minionB.AttackState.BeginWarning(minionB.RuntimeStats.WarningDuration);
                Assert.That(
                    presenter.CurrentIdentity.DisplayName,
                    Is.EqualTo("VERA"),
                    "An already introduced cast member must not take the card back from the current one.");
            }

            var playerHealth = room.Player.GetComponent<Health>();
            Assert.That(playerHealth, Is.Not.Null);
            Assert.That(
                playerHealth.TryApplyDamage(
                    new DamageEvent(30001, 91001, playerHealth.EntityId, playerHealth.MaximumHealth)),
                Is.True);
            Assert.That(room.Player.IsAlive, Is.False);
            Assert.That(presenter.IsCardVisible, Is.True, "Defeat is an approved card moment.");
            Assert.That(presenter.CurrentIdentity.DisplayName, Is.EqualTo("RIVELLA"));
            Assert.That(presenter.CurrentExpression, Is.EqualTo(CharacterExpression.Hurt));
        }

        /// <summary>
        /// Victory is the fourth approved moment. Room_03 ends the sequence, so entering its
        /// exit completes the run in place instead of loading another scene. The enemies are
        /// switched off first so the closing behaviour of the card can be observed on its own.
        /// </summary>
        [UnityTest]
        public IEnumerator Room03_CharacterCardClosesTheRunWithoutTakingInputFromTheExit()
        {
            LoadedRoom room = null;
            yield return LoadRoom(Room03ScenePath, loadedRoom => room = loadedRoom);

            var presenter = FindComponentInScene<CharacterAppealPresenter>(room.Scene);
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.IsCardVisible, Is.False);
            Assert.That(room.SequenceController.NextScene, Is.Empty, "Room_03 must end the sequence in place.");

            var mouse = AddMouse();
            yield return StartWithTrustedGesture(room.WebGate, mouse);

            for (var index = 0; index < room.Enemies.Count; index++)
            {
                room.Enemies[index].gameObject.SetActive(false);
            }

            yield return null;

            var exit = FindComponentInScene<ExitGate>(room.Scene);
            Assert.That(exit.Open(), Is.True);
            Assert.That(exit.TryEnter(room.Player), Is.True);

            Assert.That(room.SequenceController.HasHandledEntry, Is.True);
            Assert.That(presenter.IsCardVisible, Is.True, "Victory is an approved card moment.");
            Assert.That(presenter.CurrentIdentity.DisplayName, Is.EqualTo("RIVELLA"));
            Assert.That(presenter.CurrentExpression, Is.EqualTo(CharacterExpression.Confident));

            yield return new WaitForSecondsRealtime(presenter.HoldSeconds + 0.25f);
            Assert.That(presenter.IsCardVisible, Is.False, "A card must close itself without input.");
            Assert.That(presenter.CurrentIdentity, Is.Null);

            var cardHolder = FindGameObjectsInScene(room.Scene, "CharacterAppeal");
            Assert.That(cardHolder.Count, Is.EqualTo(1));
            var cardGraphics = FindComponentsInScene<Graphic>(room.Scene);
            var inspected = 0;
            for (var index = 0; index < cardGraphics.Count; index++)
            {
                var graphic = cardGraphics[index];
                if (!graphic.transform.IsChildOf(cardHolder[0].transform))
                {
                    continue;
                }

                inspected++;
                Assert.That(
                    graphic.raycastTarget,
                    Is.False,
                    $"'{graphic.name}' on the character card must not take raycasts away from the world.");
            }

            Assert.That(inspected, Is.GreaterThan(0), "The card must own the graphics this asserts on.");
        }

        private static T GetRequiredDirectChildComponent<T>(Transform parent, string childName) where T : Component
        {
            var child = parent.Find(childName);
            Assert.That(child, Is.Not.Null, $"'{parent.name}' is missing required child '{childName}'.");
            var component = child.GetComponent<T>();
            Assert.That(component, Is.Not.Null, $"'{parent.name}/{childName}' is missing {typeof(T).Name}.");
            return component;
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
