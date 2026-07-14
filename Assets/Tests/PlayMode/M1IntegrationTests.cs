using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Overbless.Tests.PlayMode
{
    public sealed class M1IntegrationTests
    {
        private readonly List<UnityEngine.Object> objectsToDestroy = new List<UnityEngine.Object>();
        private readonly List<InputDevice> inputDevicesToRemove = new List<InputDevice>();
        private const string GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";
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
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            inputBackgroundBehaviorBeforeTest = InputSystem.settings.backgroundBehavior;
            editorInputBehaviorBeforeTest = InputSystem.settings.editorInputBehaviorInPlayMode;
            runInBackgroundBeforeTest = Application.runInBackground;
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
                var guidedScene = SceneManager.GetSceneByPath(GuidedScenePath);
                if (guidedScene.IsValid() && guidedScene.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(guidedScene);
                    if (unload != null)
                    {
                        yield return unload;
                    }
                }

                for (var index = objectsToDestroy.Count - 1; index >= 0; index--)
                {
                    if (objectsToDestroy[index] != null)
                    {
                        UnityEngine.Object.Destroy(objectsToDestroy[index]);
                    }
                }

                objectsToDestroy.Clear();

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
        public IEnumerator GuidedScene_RequiresTrustedGestureAndRearmsAfterFocusLoss()
        {
            var keyboard = AddKeyboard();
            var mouse = AddMouse();
            yield return LoadGuidedScene();

            var scene = SceneManager.GetSceneByPath(GuidedScenePath);
            var binder = FindComponentInScene<M1SceneRuntimeBinder>(scene);
            var room = FindComponentInScene<M1RoomLifecycle>(scene);
            var restart = FindComponentInScene<RoomRestartController>(scene);
            var audioBridge = FindComponentInScene<M1FunctionalAudioBridge>(scene);
            var webGate = FindComponentInScene<WebStartGate>(scene);
            var pause = FindComponentInScene<PauseController>(scene);
            var targeting = FindComponentInScene<BlessingTargeting>(scene);
            var inputRouter = FindComponentInScene<PlayerInputRouter>(scene);
            var emitter = FindComponentInScene<FunctionalAudioEmitter>(scene);
            var player = FindComponentInScene<PlayerLifeCycle>(scene);
            var enemies = FindComponentsInScene<EnemyBase>(scene);
            var hud = FindComponentInScene<HUDController>(scene);
            var healthBars = FindComponentsInScene<WorldHealthBar>(scene);
            var directionalAnimators = FindComponentsInScene<DirectionalSpriteAnimator>(scene);
            var playerAnimator = player == null ? null : player.GetComponent<DirectionalSpriteAnimator>();

            Assert.That(binder, Is.Not.Null);
            Assert.That(binder.IsInitialized, Is.True);
            Assert.That(room, Is.Not.Null);
            Assert.That(restart, Is.Not.Null);
            Assert.That(audioBridge, Is.Not.Null);
            Assert.That(webGate, Is.Not.Null);
            Assert.That(pause, Is.Not.Null);
            Assert.That(targeting, Is.Not.Null);
            Assert.That(inputRouter, Is.Not.Null);
            Assert.That(emitter, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(enemies.Count, Is.EqualTo(5));
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.IsBound, Is.True);
            Assert.That(hud.IsViewConfigured, Is.True);
            Assert.That(hud.HasState, Is.True);
            Assert.That(hud.State.Health, Is.EqualTo(hud.State.MaximumHealth));
            Assert.That(hud.State.Souls, Is.Zero);
            Assert.That(hud.State.ExitOpen, Is.False);
            Assert.That(healthBars.Count, Is.EqualTo(5));
            for (var index = 0; index < healthBars.Count; index++)
            {
                Assert.That(healthBars[index].Health, Is.Not.Null);
            }

            Assert.That(directionalAnimators.Count, Is.EqualTo(6));
            for (var index = 0; index < directionalAnimators.Count; index++)
            {
                Assert.That(directionalAnimators[index].AnimationSet, Is.Not.Null);
                Assert.That(directionalAnimators[index].CurrentSprite, Is.Not.Null);
                directionalAnimators[index].AnimationSet.Validate();
            }

            Assert.That(playerAnimator, Is.Not.Null);
            Assert.That(playerAnimator.CurrentDirection, Is.EqualTo(CharacterDirection.North));
            Assert.That(playerAnimator.CurrentState, Is.EqualTo(CharacterAnimationState.Idle));
            Assert.That(playerAnimator.AnimationSet.ClipCount, Is.EqualTo(48));
            Assert.That(
                playerAnimator.AnimationSet.GetClip(CharacterAnimationState.Move, CharacterDirection.East).FrameCount,
                Is.EqualTo(6));
            Assert.That(
                playerAnimator.AnimationSet.GetClip(CharacterAnimationState.Move, CharacterDirection.SouthEast).FrameCount,
                Is.EqualTo(6));
            Assert.That(
                playerAnimator.AnimationSet.GetClip(CharacterAnimationState.Move, CharacterDirection.SouthWest).FrameCount,
                Is.EqualTo(6));
            Assert.That(
                playerAnimator.AnimationSet.GetClip(CharacterAnimationState.Move, CharacterDirection.NorthEast).FrameCount,
                Is.EqualTo(6));
            Assert.That(
                playerAnimator.AnimationSet.GetClip(CharacterAnimationState.Move, CharacterDirection.NorthWest).FrameCount,
                Is.EqualTo(6));
            AssertPosition(enemies, "Dasher", new Vector2(0f, 3f));
            AssertPosition(enemies, "Archer_A", new Vector2(0f, -0.5f));
            AssertPosition(enemies, "Archer_B", new Vector2(-4f, 1.5f));
            AssertPosition(enemies, "Minion_A", new Vector2(4f, 1.5f));
            AssertPosition(enemies, "Minion_B", new Vector2(4f, -1.5f));

            Assert.That(webGate.IsAwaitingGesture, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(AudioListener.pause, Is.True);
            Assert.That(inputRouter.IsInputEnabled, Is.False);
            Assert.That(emitter.WebStarted, Is.False);

            var startedCount = 0;
            var focusRecoveredCount = 0;
            webGate.Started += () => startedCount++;
            webGate.FocusRecovered += () => focusRecoveredCount++;

            yield return SetTrustedGestureState(webGate, mouse, false);
            yield return SetTrustedGestureState(webGate, mouse, true);

            Assert.That(webGate.IsStarted, Is.True);
            Assert.That(webGate.IsAwaitingGesture, Is.False);
            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(focusRecoveredCount, Is.Zero);
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(inputRouter.IsInputEnabled, Is.True);
            Assert.That(emitter.WebStarted, Is.True);

            yield return SetKeyboardState(keyboard);
            yield return SetTrustedGestureState(webGate, mouse, false);
            SendFocusChanged(false, webGate, inputRouter, targeting);
            yield return null;

            Assert.That(webGate.IsAwaitingGesture, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(AudioListener.pause, Is.True);
            Assert.That(inputRouter.IsInputEnabled, Is.False);

            SendFocusChanged(true, webGate, inputRouter, targeting);
            yield return null;
            Assert.That(webGate.IsAwaitingGesture, Is.True);
            Assert.That(inputRouter.IsInputEnabled, Is.False);

            yield return SetTrustedGestureState(webGate, mouse, true);

            Assert.That(webGate.IsStarted, Is.True);
            Assert.That(webGate.IsAwaitingGesture, Is.False);
            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(focusRecoveredCount, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(inputRouter.IsInputEnabled, Is.True);

            yield return SetTrustedGestureState(webGate, mouse, false);
            SendFocusChanged(true, inputRouter, targeting);
            var eastMovementStart = player.transform.position;
            yield return SetKeyboardState(keyboard, Key.D);
            Assert.That(inputRouter.IsInputEnabled, Is.True);
            Assert.That(inputRouter.Movement.x, Is.GreaterThan(0f), "D key did not route movement through PlayerInputRouter.");
            yield return null;
            Assert.That(playerAnimator.CurrentDirection, Is.EqualTo(CharacterDirection.East));
            Assert.That(playerAnimator.CurrentState, Is.EqualTo(CharacterAnimationState.Move));
            Assert.That(player.transform.position.x, Is.GreaterThan(eastMovementStart.x));

            var northEastMovementStart = player.transform.position;
            yield return SetKeyboardState(keyboard, Key.W, Key.D);
            yield return null;
            Assert.That(playerAnimator.CurrentDirection, Is.EqualTo(CharacterDirection.NorthEast));
            Assert.That(playerAnimator.CurrentState, Is.EqualTo(CharacterAnimationState.Move));
            Assert.That(player.transform.position.x, Is.GreaterThan(northEastMovementStart.x));
            Assert.That(player.transform.position.y, Is.GreaterThan(northEastMovementStart.y));

            var dash = player.GetComponent<DashAbility>();
            Assert.That(dash, Is.Not.Null);
            var dashStart = player.transform.position;
            yield return SetKeyboardState(keyboard, Key.D, Key.Space);
            yield return SetKeyboardState(keyboard, Key.D);
            Assert.That(playerAnimator.CurrentDirection, Is.EqualTo(CharacterDirection.East));
            Assert.That(playerAnimator.CurrentState, Is.EqualTo(CharacterAnimationState.Dash));
            Assert.That(dash.IsCoolingDown, Is.True);
            Assert.That(player.transform.position.x, Is.GreaterThan(dashStart.x));
            yield return SetKeyboardState(keyboard);
            var cancellationObserverRan = false;
            Action<BlessingSelectionState> cancelDuringSelection = state =>
            {
                if (!cancellationObserverRan && state.IsSelecting)
                {
                    cancellationObserverRan = true;
                    targeting.CancelSelection();
                }
            };
            targeting.SelectionUiChanged += cancelDuringSelection;
            var supersededSelectionResult = targeting.Select(BlessingType.Haste);
            targeting.SelectionUiChanged -= cancelDuringSelection;
            Assert.That(cancellationObserverRan, Is.True);
            Assert.That(supersededSelectionResult, Is.False);
            Assert.That(targeting.IsSelecting, Is.False);

            Assert.That(targeting.Select(BlessingType.Haste), Is.True);
            var firstHoverTarget = enemies[0].EntityId;
            var replacementHoverTarget = enemies[1].EntityId;
            var hoverObserverRan = false;
            var nestedHoverResult = false;
            Action<IReadOnlyList<BlessingTargetState>> replaceHoverDuringPublication = _ =>
            {
                if (!hoverObserverRan)
                {
                    hoverObserverRan = true;
                    nestedHoverResult = targeting.SetHoveredTarget(replacementHoverTarget);
                }
            };
            targeting.TargetStatesChanged += replaceHoverDuringPublication;
            var supersededHoverResult = targeting.SetHoveredTarget(firstHoverTarget);
            targeting.TargetStatesChanged -= replaceHoverDuringPublication;
            Assert.That(hoverObserverRan, Is.True);
            Assert.That(nestedHoverResult, Is.True);
            Assert.That(supersededHoverResult, Is.False);

            var replacementHasPreview = false;
            foreach (var state in targeting.GetTargetStates())
            {
                if (state.TargetEntityId == replacementHoverTarget)
                {
                    replacementHasPreview = state.HasPreview;
                    break;
                }
            }

            Assert.That(replacementHasPreview, Is.True);
            var reselectAttempted = false;
            var reselectDuringCancellation = true;
            Action<BlessingSelectionState> reselectOnCancellation = state =>
            {
                if (!state.IsSelecting)
                {
                    reselectAttempted = true;
                    reselectDuringCancellation = targeting.Select(BlessingType.Giant);
                }
            };
            targeting.SelectionUiChanged += reselectOnCancellation;
            targeting.HandlePause();
            targeting.SelectionUiChanged -= reselectOnCancellation;
            Assert.That(reselectAttempted, Is.True);
            Assert.That(reselectDuringCancellation, Is.False);
            Assert.That(targeting.IsSelecting, Is.False);
            var disableObserverRan = false;
            var selectWhileDisabled = true;
            Action<BlessingSelectionState> selectDuringDisable = state =>
            {
                if (!disableObserverRan && !state.IsSelecting)
                {
                    disableObserverRan = true;
                    selectWhileDisabled = targeting.Select(BlessingType.Haste);
                }
            };
            targeting.SelectionUiChanged += selectDuringDisable;
            targeting.enabled = false;
            targeting.SelectionUiChanged -= selectDuringDisable;
            Assert.That(disableObserverRan, Is.True);
            Assert.That(selectWhileDisabled, Is.False);
            Assert.That(targeting.IsSelecting, Is.False);
            targeting.enabled = true;
            var driftTarget = enemies[0];
            var registeredEntityId = driftTarget.EntityId;
            try
            {
                SetPrivateField(driftTarget.Health, "entityId", registeredEntityId + 10000);
                Assert.That(targeting.Select(BlessingType.Haste), Is.True);
                Assert.That(targeting.SetHoveredTarget(registeredEntityId), Is.False);
            }
            finally
            {
                targeting.CancelSelection();
                SetPrivateField(driftTarget.Health, "entityId", registeredEntityId);
            }
        }

        [UnityTest]
        public IEnumerator GuidedScene_BlessingsSoulsAudioPauseAndRestartCommitObservableState()
        {
            var keyboard = AddKeyboard();
            var mouse = AddMouse();
            yield return LoadGuidedScene();

            var scene = SceneManager.GetSceneByPath(GuidedScenePath);
            var webGate = FindComponentInScene<WebStartGate>(scene);
            var inputRouter = FindComponentInScene<PlayerInputRouter>(scene);
            var targeting = FindComponentInScene<BlessingTargeting>(scene);
            var pause = FindComponentInScene<PauseController>(scene);
            var restart = FindComponentInScene<RoomRestartController>(scene);
            var room = FindComponentInScene<M1RoomLifecycle>(scene);
            var emitter = FindComponentInScene<FunctionalAudioEmitter>(scene);
            var audioBridge = FindComponentInScene<M1FunctionalAudioBridge>(scene);
            var player = FindComponentInScene<PlayerLifeCycle>(scene);
            var camera = FindComponentInScene<Camera>(scene);
            var enemies = FindComponentsInScene<EnemyBase>(scene);
            var dasher = AssertPosition(enemies, "Dasher", new Vector2(0f, 3f));
            var initialDasherPosition = dasher.transform.position;
            var archer = FindEnemy(enemies, "Archer_A").GetComponent<ArcherAI>();
            var archerB = FindEnemy(enemies, "Archer_B").GetComponent<ArcherAI>();
            var giantTarget = FindEnemy(enemies, "Minion_A");
            var reusableTarget = FindEnemy(enemies, "Minion_B");
            var dash = player == null ? null : player.GetComponent<DashAbility>();

            Assert.That(webGate, Is.Not.Null);
            Assert.That(inputRouter, Is.Not.Null);
            Assert.That(targeting, Is.Not.Null);
            Assert.That(pause, Is.Not.Null);
            Assert.That(restart, Is.Not.Null);
            Assert.That(room, Is.Not.Null);
            Assert.That(emitter, Is.Not.Null);
            Assert.That(audioBridge, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);
            Assert.That(archer, Is.Not.Null);
            Assert.That(archerB, Is.Not.Null);
            Assert.That(dash, Is.Not.Null);
            var archerProjectilePresenter = archer.GetComponentInChildren<ArcherProjectilePresenter>(true);
            var archerProjectileLine = archerProjectilePresenter == null
                ? null
                : archerProjectilePresenter.GetComponentInChildren<LineRenderer>(true);
            Assert.That(archerProjectilePresenter, Is.Not.Null);
            Assert.That(archerProjectileLine, Is.Not.Null);
            Assert.That(archerProjectilePresenter.IsVisible, Is.False);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.Null);
            Assert.That(archerProjectileLine.enabled, Is.False);
            Assert.That(archerProjectileLine.useWorldSpace, Is.True);
            Assert.That(archerProjectileLine.positionCount, Is.EqualTo(2));
            Assert.That(archerProjectileLine.gameObject.GetComponent<Collider2D>(), Is.Null);
            Assert.That(archerProjectileLine.gameObject.GetComponent<Rigidbody2D>(), Is.Null);

            yield return SetTrustedGestureState(webGate, mouse, false);
            yield return SetTrustedGestureState(webGate, mouse, true);
            yield return SetTrustedGestureState(webGate, mouse, false);
            yield return SetKeyboardState(keyboard);
            SendFocusChanged(true, inputRouter, targeting);
            Assert.That(webGate.IsStarted, Is.True);

            var emittedEvents = new List<FunctionalAudioRecord>();
            var spawnedSouls = new List<SoulFragment>();
            var processedDeaths = new List<DeathEvent>();
            emitter.Emitted += emittedEvents.Add;
            var isolatedAudioObserverReached = false;
            Action<FunctionalAudioRecord> throwingAudioObserver = _ =>
                throw new InvalidOperationException("expected functional audio observer failure");
            Action<FunctionalAudioRecord> laterAudioObserver = _ =>
                isolatedAudioObserverReached = true;
            emitter.Emitted += throwingAudioObserver;
            emitter.Emitted += laterAudioObserver;
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("expected functional audio observer failure"));
            Assert.That(
                emitter.Emit(FunctionalAudioEvent.PlayerHit, long.MaxValue),
                Is.True);
            yield return WaitForCondition(
                () => isolatedAudioObserverReached,
                2f,
                "A throwing functional-audio observer suppressed a later observer.");
            emitter.Emitted -= throwingAudioObserver;
            emitter.Emitted -= laterAudioObserver;
            var reentrantOrder = new List<long>();
            var outerToken = long.MaxValue - 1;
            var nestedToken = long.MaxValue - 2;
            Action<FunctionalAudioRecord> emitNestedAudio = record =>
            {
                if (record.Token == outerToken)
                {
                    Assert.That(
                        emitter.Emit(FunctionalAudioEvent.PlayerHit, nestedToken),
                        Is.True);
                }
            };
            Action<FunctionalAudioRecord> recordReentrantOrder = record =>
            {
                if (record.Token == outerToken || record.Token == nestedToken)
                {
                    reentrantOrder.Add(record.Token);
                }
            };
            emitter.Emitted += emitNestedAudio;
            emitter.Emitted += recordReentrantOrder;
            Assert.That(
                emitter.Emit(FunctionalAudioEvent.PlayerHit, outerToken),
                Is.True);
            yield return WaitForCondition(
                () => reentrantOrder.Count == 2,
                2f,
                "Reentrant functional-audio emissions did not drain.");
            emitter.Emitted -= emitNestedAudio;
            emitter.Emitted -= recordReentrantOrder;
            Assert.That(
                reentrantOrder,
                Is.EqualTo(new[] { outerToken, nestedToken }),
                "Every observer must receive accepted functional-audio cues in FIFO order.");
            var resetDuringNotificationToken = long.MaxValue - 3;
            var postResetToken = long.MaxValue - 4;
            var staleResetRecordReachedLaterObserver = false;
            var postResetRecordReachedLaterObserver = false;
            Action<FunctionalAudioRecord> resetDuringNotification = record =>
            {
                if (record.Token == resetDuringNotificationToken)
                {
                    emitter.ResetEmitter();
                    Assert.That(
                        emitter.Emit(FunctionalAudioEvent.PlayerHit, postResetToken),
                        Is.True);
                }
            };
            Action<FunctionalAudioRecord> observeResetBoundary = record =>
            {
                if (record.Token == resetDuringNotificationToken)
                {
                    staleResetRecordReachedLaterObserver = true;
                }

                if (record.Token == postResetToken)
                {
                    postResetRecordReachedLaterObserver = true;
                }
            };
            emitter.Emitted += resetDuringNotification;
            emitter.Emitted += observeResetBoundary;
            Assert.That(
                emitter.Emit(
                    FunctionalAudioEvent.PlayerHit,
                    resetDuringNotificationToken),
                Is.True);
            yield return WaitForCondition(
                () => postResetRecordReachedLaterObserver,
                2f,
                "Post-reset functional-audio emission did not drain.");
            emitter.Emitted -= resetDuringNotification;
            emitter.Emitted -= observeResetBoundary;
            Assert.That(
                staleResetRecordReachedLaterObserver,
                Is.False,
                "A pre-reset functional-audio record crossed the reset generation boundary.");
            room.SoulSpawned += spawnedSouls.Add;
            room.EnemyDeathProcessed += processedDeaths.Add;

            var hastedShots = new List<AttackContext>();
            var hastedDamageEvents = new List<DamageEvent>();
            AttackContext speedSampleContext = null;
            var speedSamplePosition = Vector2.zero;
            var speedSampleTime = 0f;
            var measuredHasteProjectileSpeed = -1f;
            var firstHastedProjectileStoppedAt = -1f;
            var beforeRestartAttackIds = new HashSet<long>();
            var afterRestartAttackIds = new HashSet<long>();
            var capturePostRestartAttacks = false;
            var projectilePresentationMismatches = new List<string>();
            var projectileFiredSamples = 0;
            var projectileMovedSamples = 0;
            var projectileStoppedSamples = 0;
            Action<string, AttackContext, Vector2> observeActiveProjectile = (eventName, context, position) =>
            {
                try
                {
                    if (context == null)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile {eventName} callback did not provide an attack context.");
                        return;
                    }

                    if (!archerProjectilePresenter.IsVisible)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile presenter was hidden during a {eventName} callback.");
                    }

                    if (!object.ReferenceEquals(archerProjectilePresenter.CurrentContext, context))
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile presenter context did not match the {eventName} callback.");
                    }

                    if (!object.ReferenceEquals(archer.ProjectileContext, context))
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer authoritative context did not match the {eventName} callback.");
                    }

                    if (Vector2.Distance(archerProjectilePresenter.CurrentPosition, position) > 0.01f)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile presenter position did not match the {eventName} callback.");
                    }

                    if (Vector2.Distance(archer.ProjectilePosition, position) > 0.01f)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer authoritative position did not match the {eventName} callback.");
                    }

                    if (archerProjectileLine.positionCount != 2)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile line did not have two points during a {eventName} callback.");
                        return;
                    }

                    var firstPoint = archerProjectileLine.GetPosition(0);
                    var secondPoint = archerProjectileLine.GetPosition(1);
                    var midpoint = (firstPoint + secondPoint) * 0.5f;
                    var lineDirection = new Vector2(
                        secondPoint.x - firstPoint.x,
                        secondPoint.y - firstPoint.y).normalized;
                    var lineLength = Vector3.Distance(firstPoint, secondPoint);
                    if (Vector2.Distance(new Vector2(midpoint.x, midpoint.y), position) > 0.01f)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile line midpoint did not match the {eventName} callback.");
                    }

                    if (Vector2.Distance(lineDirection, context.NormalizedDirection) > 0.01f)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile line direction did not match the {eventName} callback.");
                    }

                    if (Mathf.Abs(archerProjectileLine.startWidth - context.Width) > 0.001f ||
                        Mathf.Abs(archerProjectileLine.endWidth - context.Width) > 0.001f)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile line width did not match the locked {eventName} context.");
                    }

                    if (Mathf.Abs(lineLength - context.Width * 2f) > 0.01f)
                    {
                        projectilePresentationMismatches.Add(
                            $"Archer projectile line length did not match the locked {eventName} context.");
                    }
                }
                catch (Exception exception)
                {
                    projectilePresentationMismatches.Add(
                        $"Archer projectile {eventName} observer failed: {exception.Message}");
                }
            };
            Action<AttackContext, Vector2> observeStoppedProjectile = (context, position) =>
            {
                try
                {
                    if (archerProjectilePresenter.IsVisible)
                    {
                        projectilePresentationMismatches.Add(
                            "Archer projectile presenter remained visible during a stopped callback.");
                    }

                    if (archerProjectileLine.enabled)
                    {
                        projectilePresentationMismatches.Add(
                            "Archer projectile line remained enabled during a stopped callback.");
                    }
                }
                catch (Exception exception)
                {
                    projectilePresentationMismatches.Add(
                        $"Archer projectile stopped observer failed: {exception.Message}");
                }
            };
            Action<AttackContext> recordAttack = context =>
            {
                if (capturePostRestartAttacks)
                {
                    afterRestartAttackIds.Add(context.AttackInstanceId);
                }
                else
                {
                    beforeRestartAttackIds.Add(context.AttackInstanceId);
                }
            };

            for (var index = 0; index < enemies.Count; index++)
            {
                enemies[index].AttackState.ContextLocked += recordAttack;
            }
            giantTarget.Health.Damaged += damageEvent =>
            {
                if (damageEvent.AttackerEntityId == archer.EntityId)
                {
                    hastedDamageEvents.Add(damageEvent);
                }
            };
            archer.ProjectileFired += (context, position) =>
            {
                projectileFiredSamples++;
                observeActiveProjectile("fired", context, position);
            };
            archer.ProjectileMoved += (context, position) =>
            {
                projectileMovedSamples++;
                observeActiveProjectile("moved", context, position);
            };
            archer.ProjectileStopped += (context, position) =>
            {
                projectileStoppedSamples++;
                observeStoppedProjectile(context, position);
            };
            archer.ProjectileMoved += (context, position) =>
            {
                if (measuredHasteProjectileSpeed >= 0f)
                {
                    return;
                }

                if (speedSampleContext == null ||
                    speedSampleContext.AttackInstanceId != context.AttackInstanceId)
                {
                    speedSampleContext = context;
                    speedSamplePosition = position;
                    speedSampleTime = Time.time;
                    return;
                }

                var sampleDuration = Time.time - speedSampleTime;
                if (sampleDuration > 0f)
                {
                    measuredHasteProjectileSpeed =
                        Vector2.Distance(speedSamplePosition, position) / sampleDuration;
                }
            };
            archer.ProjectileStopped += (context, _) =>
            {
                if (firstHastedProjectileStoppedAt < 0f &&
                    hastedShots.Count > 0 &&
                    context.AttackInstanceId == hastedShots[0].AttackInstanceId)
                {
                    firstHastedProjectileStoppedAt = Time.time;
                }
            };

            archer.ProjectileFired += (context, _) =>
            {
                hastedShots.Add(context);
                RouteAttackToTarget(giantTarget, context);
            };

            yield return ApplyBlessingWithInput(
                keyboard,
                mouse,
                camera,
                targeting,
                archer,
                Key.Digit1);
            Assert.That(archer.RuntimeStats.HasHaste, Is.True);
            Assert.That(targeting.IsAvailable(BlessingType.Haste), Is.False);

            yield return BeginBlessingSelectionWithInput(
                keyboard,
                mouse,
                camera,
                targeting,
                giantTarget,
                Key.Digit2);
            yield return SetKeyboardState(keyboard, Key.Escape);
            yield return SetKeyboardState(keyboard);
            Assert.That(targeting.IsSelecting, Is.False);
            Assert.That(pause.IsPaused, Is.False);

            yield return BeginBlessingSelectionWithInput(
                keyboard,
                mouse,
                camera,
                targeting,
                giantTarget,
                Key.Digit2);
            yield return SetMouseState(mouse, GetScreenPosition(camera, giantTarget), false, true);
            yield return SetMouseState(mouse, GetScreenPosition(camera, giantTarget));
            Assert.That(targeting.IsSelecting, Is.False);
            Assert.That(pause.IsPaused, Is.False);

            yield return WaitForConditionWhileDriving(
                () => hastedShots.Count >= 2,
                keyboard,
                player,
                dash,
                16f,
                "The Haste-blessed archer did not fire twice.");
            var matchedHasteDamage = false;
            for (var damageIndex = 0; damageIndex < hastedDamageEvents.Count; damageIndex++)
            {
                for (var shotIndex = 0; shotIndex < hastedShots.Count; shotIndex++)
                {
                    if (hastedDamageEvents[damageIndex].AttackInstanceId ==
                        hastedShots[shotIndex].AttackInstanceId)
                    {
                        matchedHasteDamage = true;
                        break;
                    }
                }
            }

            Assert.That(matchedHasteDamage, Is.True, "Observed health loss did not match a recorded Haste attack.");
            Assert.That(measuredHasteProjectileSpeed, Is.GreaterThan(0f));
            Assert.That(
                measuredHasteProjectileSpeed,
                Is.EqualTo(archer.RuntimeStats.ProjectileSpeed).Within(0.25f));
            Assert.That(firstHastedProjectileStoppedAt, Is.GreaterThan(hastedShots[0].LockedAt));
            var observedHasteCooldown =
                hastedShots[1].LockedAt -
                firstHastedProjectileStoppedAt -
                archer.RuntimeStats.RecoveryDuration -
                archer.RuntimeStats.WarningDuration;
            Assert.That(
                observedHasteCooldown,
                Is.EqualTo(archer.RuntimeStats.AttackCooldown).Within(0.25f));
            Assert.That(
                archer.RuntimeStats.AttackCooldown,
                Is.LessThan(archer.Definition.AttackCooldown));
            Assert.That(ContainsAudioEvent(emittedEvents, FunctionalAudioEvent.AttackLocked), Is.True);
            Assert.That(projectileFiredSamples, Is.GreaterThanOrEqualTo(1));
            Assert.That(projectileMovedSamples, Is.GreaterThanOrEqualTo(1));
            Assert.That(projectileStoppedSamples, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                projectilePresentationMismatches,
                Is.Empty,
                string.Join("\n", projectilePresentationMismatches));

            yield return WaitForConditionWhileDriving(
                () => archer.IsProjectileActive,
                keyboard,
                player,
                dash,
                8f,
                "The Haste-blessed archer did not begin another projectile attack.");

            yield return SetKeyboardState(keyboard, Key.Escape);
            Assert.That(pause.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            var pausedEncounterPosition = archer.transform.position;
            var pausedFriendlyHealth = giantTarget.Health.CurrentHealth;
            var pausedPhase = archer.CurrentAttackPhase;
            var pausedContext = archer.ProjectileContext;
            var pausedProjectilePosition = archer.ProjectilePosition;
            Assert.That(pausedContext, Is.Not.Null);
            var pausedPresenterPosition = archerProjectilePresenter.CurrentPosition;
            Assert.That(archerProjectilePresenter.IsVisible, Is.True);
            Assert.That(archerProjectileLine.enabled, Is.True);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.SameAs(pausedContext));
            Assert.That(
                Vector2.Distance(archerProjectilePresenter.CurrentPosition, pausedProjectilePosition),
                Is.LessThanOrEqualTo(0.01f));
            yield return SetKeyboardState(keyboard);
            yield return AdvanceRealtimeFrames(4);

            Assert.That(archer.transform.position, Is.EqualTo(pausedEncounterPosition));
            Assert.That(giantTarget.Health.CurrentHealth, Is.EqualTo(pausedFriendlyHealth));
            Assert.That(archer.CurrentAttackPhase, Is.EqualTo(pausedPhase));
            Assert.That(archer.ProjectileContext, Is.SameAs(pausedContext));
            Assert.That(archer.ProjectilePosition, Is.EqualTo(pausedProjectilePosition));
            Assert.That(archerProjectilePresenter.IsVisible, Is.True);
            Assert.That(archerProjectileLine.enabled, Is.True);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.SameAs(pausedContext));
            Assert.That(
                Vector2.Distance(archerProjectilePresenter.CurrentPosition, pausedPresenterPosition),
                Is.LessThanOrEqualTo(0.01f));
            Assert.That(
                Vector2.Distance(archerProjectilePresenter.CurrentPosition, archer.ProjectilePosition),
                Is.LessThanOrEqualTo(0.01f));

            yield return SetKeyboardState(keyboard, Key.Escape);
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
            yield return SetKeyboardState(keyboard);
            yield return WaitForConditionWhileDriving(
                () => !archer.IsProjectileActive ||
                      archer.ProjectileContext != pausedContext ||
                      archer.ProjectilePosition != pausedProjectilePosition,
                keyboard,
                player,
                dash,
                4f,
                "Encounter attack state did not resume after Escape.");

            yield return ApplyBlessingWithInput(
                keyboard,
                mouse,
                camera,
                targeting,
                giantTarget,
                Key.Digit2);
            Assert.That(giantTarget.RuntimeStats.HasGiant, Is.True);
            Assert.That(giantTarget.Health.MaximumHealth, Is.EqualTo(giantTarget.RuntimeStats.MaximumHealth));
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.False);

            AttackContext giantContext = null;
            giantTarget.AttackState.ContextLocked += context =>
            {
                giantContext = context;
                RouteAttackToTarget(reusableTarget, context);
            };
            yield return MovePlayerToEnemyWithInput(keyboard, player, giantTarget);
            yield return WaitForCondition(
                () => giantContext != null && reusableTarget.Health.CurrentHealth < reusableTarget.Health.MaximumHealth,
                4f,
                "The Giant-blessed minion did not produce a friendly-fire attack.");
            Assert.That(giantContext.Range, Is.EqualTo(giantTarget.RuntimeStats.AttackRange).Within(0.001f));
            Assert.That(giantContext.Range, Is.GreaterThan(giantTarget.Definition.AttackRange));

            Action<AttackContext> routeFirstTarget = context =>
            {
                if (context.AttackerEntityId != giantTarget.EntityId)
                {
                    RouteAttackToTarget(giantTarget, context);
                }
            };
            Action<AttackContext, Vector2> routeFirstTargetProjectile = (context, _) =>
            {
                RouteAttackToTarget(giantTarget, context);
            };
            SubscribeCombatTargetRouting(enemies, routeFirstTarget, routeFirstTargetProjectile);
            ReduceToOneHealth(giantTarget, 9000001);

            yield return WaitForConditionWhileDriving(
                () => giantTarget.Health.IsDead,
                keyboard,
                player,
                dash,
                30f,
                "Real enemy attacks did not defeat the Giant-blessed target.");
            var giantDeathCount = 0;
            DeathEvent giantDeathEvent = default;
            for (var index = 0; index < processedDeaths.Count; index++)
            {
                if (processedDeaths[index].EntityId == giantTarget.EntityId)
                {
                    giantDeathCount++;
                    giantDeathEvent = processedDeaths[index];
                }
            }

            Assert.That(giantDeathCount, Is.EqualTo(1));
            Assert.That(giantDeathEvent.DamageEvent.AttackerEntityId, Is.Not.EqualTo(giantTarget.EntityId));
            Assert.That(giantDeathEvent.DamageEvent.AttackInstanceId, Is.GreaterThan(0));
            var giantSoul = FindClosestUncollectedSoul(spawnedSouls, giantTarget.transform.position);
            Assert.That(giantSoul, Is.Not.Null);
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.False);

            yield return new WaitForSeconds(BlessingSlot.ReturnDelay * 0.75f);
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.False);
            yield return new WaitForSeconds(BlessingSlot.ReturnDelay * 0.5f);
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.True);

            Assert.That(
                Vector2.Distance(giantSoul.transform.position, giantTarget.transform.position),
                Is.LessThanOrEqualTo(0.5f));
            yield return CollectSoulWithPlayerInput(keyboard, player, giantSoul);
            Assert.That(room.SoulCount, Is.EqualTo(1));
            Assert.That(giantSoul.IsCollected, Is.True);

            yield return ApplyBlessingWithInput(
                keyboard,
                mouse,
                camera,
                targeting,
                reusableTarget,
                Key.Digit2);
            Assert.That(reusableTarget.RuntimeStats.HasGiant, Is.True);
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.False);

            Action<AttackContext> routeReusableTarget = context =>
            {
                if (context.AttackerEntityId != reusableTarget.EntityId)
                {
                    RouteAttackToTarget(reusableTarget, context);
                }
            };
            Action<AttackContext, Vector2> routeReusableTargetProjectile = (context, _) =>
            {
                RouteAttackToTarget(reusableTarget, context);
            };
            SubscribeCombatTargetRouting(enemies, routeReusableTarget, routeReusableTargetProjectile);
            var reusableDeathCountBefore = CountDeathsFor(processedDeaths, reusableTarget.EntityId);
            ReduceToOneHealth(reusableTarget, 9000002);

            yield return WaitForConditionWhileDriving(
                () => reusableTarget.Health.IsDead,
                keyboard,
                player,
                dash,
                30f,
                "Real enemy attacks did not defeat the reused Giant-slot target.");
            Assert.That(
                CountDeathsFor(processedDeaths, reusableTarget.EntityId),
                Is.EqualTo(reusableDeathCountBefore + 1));
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.False);

            var restartCount = 0;
            restart.Restarted += () => restartCount++;
            var highestAttackIdBeforeRestart = 0L;
            foreach (var attackId in beforeRestartAttackIds)
            {
                if (attackId > highestAttackIdBeforeRestart)
                {
                    highestAttackIdBeforeRestart = attackId;
                }
            }

            audioBridge.enabled = false;
            yield return null;
            yield return SetKeyboardState(keyboard, Key.R);
            yield return SetKeyboardState(keyboard);
            audioBridge.enabled = true;
            yield return null;
            Assert.That(restartCount, Is.EqualTo(1));
            Assert.That(room.SoulCount, Is.Zero);
            Assert.That(room.IsExitOpen, Is.False);
            Assert.That(targeting.IsAvailable(BlessingType.Haste), Is.True);
            Assert.That(targeting.IsAvailable(BlessingType.Giant), Is.True);
            Assert.That(archer.RuntimeStats.HasHaste, Is.False);
            Assert.That(giantTarget.RuntimeStats.HasGiant, Is.False);
            Assert.That(Vector2.Distance(dasher.transform.position, initialDasherPosition), Is.LessThanOrEqualTo(0.001f));
            Assert.That(archerProjectilePresenter.IsVisible, Is.False);
            Assert.That(archerProjectileLine.enabled, Is.False);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.Null);
            Assert.That(
                projectilePresentationMismatches,
                Is.Empty,
                string.Join("\n", projectilePresentationMismatches));

            capturePostRestartAttacks = true;
            var secondCycleSoulStart = spawnedSouls.Count;
            var secondCycleTargets = new List<EnemyBase>
            {
                archer,
                archerB,
                giantTarget,
                reusableTarget
            };
            for (var index = 0; index < secondCycleTargets.Count; index++)
            {
                ReduceToOneHealth(secondCycleTargets[index], 9100000 + index);
            }
            Action<AttackContext> routeSecondCycle = context =>
            {
                RouteLivingTargetsIntoAttackArea(secondCycleTargets, context);
            };
            Action<AttackContext, Vector2> routeSecondCycleProjectile = (context, _) =>
            {
                RouteLivingTargetsIntoAttackArea(secondCycleTargets, context);
            };
            SubscribeCombatTargetRouting(enemies, routeSecondCycle, routeSecondCycleProjectile);

            yield return WaitForConditionWhileDriving(
                () => spawnedSouls.Count >= secondCycleSoulStart + M1RoomDefinition.RequiredSoulCount &&
                      afterRestartAttackIds.Count > 0,
                keyboard,
                player,
                dash,
                45f,
                "The restarted room did not complete a second combat defeat cycle.");

            Assert.That(afterRestartAttackIds.Count, Is.GreaterThan(0));
            foreach (var attackId in afterRestartAttackIds)
            {
                Assert.That(attackId, Is.GreaterThan(highestAttackIdBeforeRestart));
            }

            for (var index = 0; index < M1RoomDefinition.RequiredSoulCount; index++)
            {
                yield return CollectSoulWithPlayerInput(
                    keyboard,
                    player,
                    spawnedSouls[secondCycleSoulStart + index]);
            }

            Assert.That(room.SoulCount, Is.GreaterThanOrEqualTo(M1RoomDefinition.RequiredSoulCount));
            Assert.That(room.IsExitOpen, Is.True);
            Assert.That(ContainsAudioEvent(emittedEvents, FunctionalAudioEvent.SoulCollected), Is.True);
            Assert.That(ContainsAudioEvent(emittedEvents, FunctionalAudioEvent.ExitOpened), Is.True);
            var soulAudioCountBeforeDuplicate =
                CountAudioEvents(emittedEvents, FunctionalAudioEvent.SoulCollected);
            var exitAudioCountBeforeDuplicate =
                CountAudioEvents(emittedEvents, FunctionalAudioEvent.ExitOpened);
            var soulAudioHandler = typeof(M1FunctionalAudioBridge).GetMethod(
                "HandleSoulCountChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var exitAudioHandler = typeof(M1FunctionalAudioBridge).GetMethod(
                "HandleExitOpened",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(soulAudioHandler, Is.Not.Null);
            Assert.That(exitAudioHandler, Is.Not.Null);
            soulAudioHandler.Invoke(audioBridge, new object[] { room.SoulCount - 1 });
            soulAudioHandler.Invoke(audioBridge, new object[] { room.SoulCount });
            exitAudioHandler.Invoke(audioBridge, null);
            exitAudioHandler.Invoke(audioBridge, null);
            yield return null;
            Assert.That(
                CountAudioEvents(emittedEvents, FunctionalAudioEvent.SoulCollected),
                Is.EqualTo(soulAudioCountBeforeDuplicate));
            Assert.That(
                CountAudioEvents(emittedEvents, FunctionalAudioEvent.ExitOpened),
                Is.EqualTo(exitAudioCountBeforeDuplicate));
            Action throwingPlayerResetObserver = () =>
                throw new InvalidOperationException("expected player reset observer failure");
            Action<int> throwingRoomResetObserver = count =>
            {
                if (count == 0)
                {
                    throw new InvalidOperationException("expected room reset observer failure");
                }
            };
            player.Reset += throwingPlayerResetObserver;
            room.SoulCountChanged += throwingRoomResetObserver;
            Assert.Throws<AggregateException>(() => restart.RestartRoom());
            player.Reset -= throwingPlayerResetObserver;
            room.SoulCountChanged -= throwingRoomResetObserver;
            Assert.That(restartCount, Is.EqualTo(2));
            Assert.That(player.IsAlive, Is.True);
            Assert.That(inputRouter.IsInputEnabled, Is.True);
            Assert.That(room.SoulCount, Is.Zero);
            Assert.That(room.IsExitOpen, Is.False);
            var reentrantRestartObserved = false;
            var reentrantRestartFailures = new List<Exception>();
            archerProjectilePresenter.enabled = false;
            yield return null;
            Action<AttackContext, Vector2> restartDuringProjectilePublication = (context, position) =>
            {
                if (reentrantRestartObserved)
                {
                    return;
                }

                reentrantRestartObserved = true;
                try
                {
                    restart.RestartRoom();
                }
                catch (Exception exception)
                {
                    reentrantRestartFailures.Add(exception);
                }
            };
            archer.ProjectileFired += restartDuringProjectilePublication;
            archerProjectilePresenter.enabled = true;
            yield return null;

            yield return WaitForConditionWhileDriving(
                () => reentrantRestartObserved,
                keyboard,
                player,
                dash,
                15f,
                "The Archer did not publish a projectile for the reentrant restart check.");
            archer.ProjectileFired -= restartDuringProjectilePublication;
            yield return null;

            Assert.That(reentrantRestartFailures, Is.Empty);
            Assert.That(restartCount, Is.EqualTo(3));
            Assert.That(archer.IsProjectileActive, Is.False);
            Assert.That(archer.ProjectileContext, Is.Null);
            Assert.That(archerProjectilePresenter.IsVisible, Is.False);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.Null);
            Assert.That(archerProjectileLine.enabled, Is.False);
            var reentrantDisableObserved = false;
            archerProjectilePresenter.enabled = false;
            yield return null;
            Action<AttackContext, Vector2> disableDuringProjectilePublication = (context, position) =>
            {
                if (reentrantDisableObserved)
                {
                    return;
                }

                reentrantDisableObserved = true;
                archerProjectilePresenter.enabled = false;
            };
            archer.ProjectileFired += disableDuringProjectilePublication;
            archerProjectilePresenter.enabled = true;
            yield return null;

            yield return WaitForConditionWhileDriving(
                () => reentrantDisableObserved,
                keyboard,
                player,
                dash,
                15f,
                "The Archer did not publish a projectile for the reentrant disable check.");
            archer.ProjectileFired -= disableDuringProjectilePublication;

            Assert.That(archer.IsProjectileActive, Is.True);
            Assert.That(archerProjectilePresenter.enabled, Is.False);
            Assert.That(archerProjectilePresenter.IsVisible, Is.False);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.Null);
            Assert.That(archerProjectileLine.enabled, Is.False);

            archerProjectilePresenter.enabled = true;
            Assert.That(archerProjectilePresenter.IsVisible, Is.True);
            Assert.That(archerProjectilePresenter.CurrentContext, Is.SameAs(archer.ProjectileContext));
            Assert.That(
                Vector2.Distance(archerProjectilePresenter.CurrentPosition, archer.ProjectilePosition),
                Is.LessThanOrEqualTo(0.01f));

            restart.RestartRoom();
            Assert.That(restartCount, Is.EqualTo(4));
            Assert.That(archer.IsProjectileActive, Is.False);
            Assert.That(archerProjectilePresenter.IsVisible, Is.False);
            Assert.That(archerProjectileLine.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator PlayerLifecycle_DeathAndResetRestoreConfiguredSpawnState()
        {
            var fixture = CreatePlayerFixture();
            yield return null;

            fixture.Player.transform.localPosition = new Vector3(4f, 2f, 0f);
            fixture.Player.transform.localScale = new Vector3(2f, 2f, 1f);
            Assert.That(fixture.Health.TryApplyDamage(new DamageEvent(1, 1, fixture.Health.EntityId, fixture.Health.MaximumHealth)), Is.True);

            Assert.That(fixture.LifeCycle.IsAlive, Is.False);
            Assert.That(fixture.InputRouter.IsInputEnabled, Is.False);
            Assert.That(fixture.Controller.IsMovementEnabled, Is.False);

            fixture.LifeCycle.ResetPlayer();

            Assert.That(fixture.LifeCycle.IsAlive, Is.True);
            Assert.That(fixture.Health.IsDead, Is.False);
            Assert.That(fixture.Health.CurrentHealth, Is.EqualTo(fixture.Health.MaximumHealth));
            Assert.That(fixture.Player.transform.localPosition, Is.EqualTo(fixture.SpawnLocalPosition));
            Assert.That(fixture.Player.transform.localScale, Is.EqualTo(fixture.SpawnLocalScale));
            Assert.That(fixture.InputRouter.IsInputEnabled, Is.True);
            Assert.That(fixture.Controller.IsMovementEnabled, Is.True);
            Action throwingResetObserver = () =>
                throw new InvalidOperationException("expected reset notification failure");
            fixture.LifeCycle.Reset += throwingResetObserver;
            Assert.Throws<PlayerResetNotificationException>(() => fixture.LifeCycle.ResetPlayer());
            fixture.LifeCycle.Reset -= throwingResetObserver;
            Assert.That(fixture.LifeCycle.IsAlive, Is.True);
            Assert.That(fixture.InputRouter.IsInputEnabled, Is.True);

            Action invalidateResetObserver = () =>
                fixture.Health.TryApplyDamage(
                    new DamageEvent(
                        2,
                        2,
                        fixture.Health.EntityId,
                        fixture.Health.MaximumHealth));
            fixture.LifeCycle.Reset += invalidateResetObserver;
            Assert.Throws<AggregateException>(() => fixture.LifeCycle.ResetPlayer());
            fixture.LifeCycle.Reset -= invalidateResetObserver;
            Assert.That(fixture.LifeCycle.IsAlive, Is.False);
            Assert.That(fixture.Health.IsDead, Is.True);
            Assert.That(fixture.InputRouter.IsInputEnabled, Is.False);
            fixture.LifeCycle.ResetPlayer();

            fixture.LifeCycle.enabled = false;
            Action<DeathEvent> resetBeforeLifecycleDeathObserver = _ => fixture.LifeCycle.ResetPlayer();
            fixture.Health.Died += resetBeforeLifecycleDeathObserver;
            fixture.LifeCycle.enabled = true;
            Assert.That(
                fixture.Health.TryApplyDamage(
                    new DamageEvent(
                        3,
                        3,
                        fixture.Health.EntityId,
                        fixture.Health.MaximumHealth)),
                Is.True);
            fixture.Health.Died -= resetBeforeLifecycleDeathObserver;
            Assert.That(fixture.Health.IsDead, Is.False);
            Assert.That(fixture.LifeCycle.IsAlive, Is.True);
            Assert.That(fixture.InputRouter.IsInputEnabled, Is.True);
            var staleDeathObserverRan = false;
            Action<DeathEvent> resetDuringDeathNotification = _ => fixture.LifeCycle.ResetPlayer();
            Action<DeathEvent> observeStaleDeath = _ => staleDeathObserverRan = true;
            fixture.LifeCycle.Died += resetDuringDeathNotification;
            fixture.LifeCycle.Died += observeStaleDeath;
            Assert.That(
                fixture.Health.TryApplyDamage(
                    new DamageEvent(
                        4,
                        4,
                        fixture.Health.EntityId,
                        fixture.Health.MaximumHealth)),
                Is.True);
            fixture.LifeCycle.Died -= resetDuringDeathNotification;
            fixture.LifeCycle.Died -= observeStaleDeath;
            Assert.That(staleDeathObserverRan, Is.False);
            Assert.That(fixture.LifeCycle.IsAlive, Is.True);
            Assert.That(fixture.Health.IsDead, Is.False);

            SetPrivateField(fixture.InputRouter, "movement", Vector2.right);
            Action<Vector2> throwingMovementObserver = _ =>
                throw new InvalidOperationException("expected movement observer failure");
            fixture.InputRouter.MovementChanged += throwingMovementObserver;
            Assert.Throws<InvalidOperationException>(
                () => fixture.Health.TryApplyDamage(
                    new DamageEvent(
                        5,
                        5,
                        fixture.Health.EntityId,
                        fixture.Health.MaximumHealth)));
            fixture.InputRouter.MovementChanged -= throwingMovementObserver;
            Assert.That(fixture.LifeCycle.IsAlive, Is.False);
            Assert.That(fixture.Controller.IsMovementEnabled, Is.False);
            Assert.That(fixture.InputRouter.IsInputEnabled, Is.False);
            fixture.LifeCycle.ResetPlayer();
        }

        [UnityTest]
        public IEnumerator M1RoomLifecycle_CollectingRequiredSoulsOpensExitAndResetClearsTransientState()
        {
            var player = CreatePlayerFixture();
            var fixture = CreateRoomFixture();
            yield return null;

            var spawnedSouls = new List<SoulFragment>();
            fixture.Lifecycle.SoulSpawned += soul =>
            {
                spawnedSouls.Add(soul);
                Track(soul.gameObject);
            };

            fixture.Lifecycle.enabled = false;
            fixture.Lifecycle.ResetForRoom();
            var firstEnemy = fixture.EnemyHealths[0];
            Assert.That(
                fixture.Lifecycle.TryApplyDamage(
                    firstEnemy,
                    new DamageEvent(1, 99, firstEnemy.EntityId, firstEnemy.MaximumHealth)),
                Is.True);
            Assert.That(firstEnemy.IsDead, Is.True);
            Assert.That(spawnedSouls.Count, Is.EqualTo(1));
            fixture.Lifecycle.enabled = true;

            for (var index = 1; index < M1RoomDefinition.RequiredSoulCount; index++)
            {
                var enemy = fixture.EnemyHealths[index];
                Assert.That(
                    fixture.Lifecycle.TryApplyDamage(
                        enemy,
                        new DamageEvent(index + 1, 99, enemy.EntityId, enemy.MaximumHealth)),
                    Is.True);
                Assert.That(enemy.IsDead, Is.True);
            }

            Assert.That(fixture.Lifecycle.DamageLedgerCount, Is.EqualTo(M1RoomDefinition.RequiredSoulCount));
            Assert.That(spawnedSouls.Count, Is.EqualTo(M1RoomDefinition.RequiredSoulCount));
            Assert.That(player.LifeCycle.IsAlive, Is.True);
            for (var index = 0; index < spawnedSouls.Count; index++)
            {
                Assert.That(spawnedSouls[index].TryCollect(player.LifeCycle), Is.True);
            }

            Assert.That(fixture.Lifecycle.SoulCount, Is.EqualTo(M1RoomDefinition.RequiredSoulCount));
            Assert.That(fixture.Lifecycle.IsExitOpen, Is.True);

            fixture.Lifecycle.ResetForRoom();

            Assert.That(fixture.Lifecycle.SoulCount, Is.Zero);
            Assert.That(fixture.Lifecycle.IsExitOpen, Is.False);
            Assert.That(fixture.Lifecycle.DamageLedgerCount, Is.Zero);
            for (var index = 0; index < spawnedSouls.Count; index++)
            {
                Assert.That(spawnedSouls[index].gameObject.activeSelf, Is.False);
            }
            Action<int> throwingResetNotification = count =>
            {
                if (count == 0)
                {
                    throw new InvalidOperationException("expected room notification failure");
                }
            };
            fixture.Lifecycle.SoulCountChanged += throwingResetNotification;
            Assert.Throws<RoomResetNotificationException>(() => fixture.Lifecycle.ResetForRoom());
            fixture.Lifecycle.SoulCountChanged -= throwingResetNotification;
            Assert.That(fixture.Lifecycle.SoulCount, Is.Zero);
            Assert.That(fixture.Lifecycle.DamageLedgerCount, Is.Zero);

            var oldCycleEnemy = fixture.EnemyHealths[3];
            var oldCycleEvent = default(DeathEvent);
            oldCycleEnemy.Died += deathEvent => oldCycleEvent = deathEvent;
            Assert.That(
                fixture.Lifecycle.TryApplyDamage(
                    oldCycleEnemy,
                    new DamageEvent(10, 99, oldCycleEnemy.EntityId, oldCycleEnemy.MaximumHealth)),
                Is.True);
            fixture.Lifecycle.ResetForRoom();
            oldCycleEnemy.ResetHealth();
            var spawnCountBeforeOldEvent = spawnedSouls.Count;
            var deathHandler = typeof(M1RoomLifecycle).GetMethod(
                "HandleEnemyDeath",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(deathHandler, Is.Not.Null);
            Assert.DoesNotThrow(() => deathHandler.Invoke(fixture.Lifecycle, new object[] { oldCycleEvent }));
            Assert.That(spawnedSouls.Count, Is.EqualTo(spawnCountBeforeOldEvent));

            var stalePositiveCountObserved = false;
            Action<int> restartRoomOnPositiveCount = count =>
            {
                if (count > 0)
                {
                    fixture.Lifecycle.ResetForRoom();
                }
            };
            Action<int> observeLaterCount = count =>
            {
                if (count > 0)
                {
                    stalePositiveCountObserved = true;
                }
            };
            fixture.Lifecycle.SoulCountChanged += restartRoomOnPositiveCount;
            fixture.Lifecycle.SoulCountChanged += observeLaterCount;
            var generationEnemy = fixture.EnemyHealths[4];
            Assert.That(
                fixture.Lifecycle.TryApplyDamage(
                    generationEnemy,
                    new DamageEvent(11, 99, generationEnemy.EntityId, generationEnemy.MaximumHealth)),
                Is.True);
            var generationSoul = spawnedSouls[spawnedSouls.Count - 1];
            Assert.That(generationSoul.TryCollect(player.LifeCycle), Is.True);
            fixture.Lifecycle.SoulCountChanged -= restartRoomOnPositiveCount;
            fixture.Lifecycle.SoulCountChanged -= observeLaterCount;
            Assert.That(stalePositiveCountObserved, Is.False);
            Assert.That(fixture.Lifecycle.SoulCount, Is.Zero);
            Assert.That(fixture.Lifecycle.IsExitOpen, Is.False);
        }

        private static IEnumerator LoadGuidedScene()
        {
            var existingScene = SceneManager.GetSceneByPath(GuidedScenePath);
            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(existingScene);
                if (unload != null)
                {
                    yield return unload;
                }
            }

            var load = SceneManager.LoadSceneAsync(GuidedScenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            var scene = SceneManager.GetSceneByPath(GuidedScenePath);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
        }

        private Keyboard AddKeyboard()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            inputDevicesToRemove.Add(keyboard);
            return keyboard;
        }
        private Mouse AddMouse()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            inputDevicesToRemove.Add(mouse);
            return mouse;
        }


        private static IEnumerator SetKeyboardState(Keyboard keyboard, params Key[] pressedKeys)
        {
            keyboard.MakeCurrent();
            InputSystem.EnableDevice(keyboard);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressedKeys));
            InputSystem.Update();
            PumpBlessingTargetingUpdates();
            for (var index = 0; index < pressedKeys.Length; index++)
            {
                Assert.That(
                    keyboard[pressedKeys[index]].isPressed,
                    Is.True,
                    $"Queued keyboard state did not press {pressedKeys[index]}.");
            }

            yield return null;
        }

        private static IEnumerator SetMouseState(
            Mouse mouse,
            Vector2 position,
            bool leftButtonPressed = false,
            bool rightButtonPressed = false)
        {
            mouse.MakeCurrent();
            InputSystem.EnableDevice(mouse);
            var state = new MouseState
            {
                position = position
            };
            if (leftButtonPressed)
            {
                state = state.WithButton(MouseButton.Left);
            }

            if (rightButtonPressed)
            {
                state = state.WithButton(MouseButton.Right);
            }

            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
            PumpBlessingTargetingUpdates();
            yield return null;
        }

        private static IEnumerator SetTrustedGestureState(
            WebStartGate webGate,
            Mouse mouse,
            bool pressed)
        {
            Assert.That(webGate, Is.Not.Null);
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
        private static void PumpBlessingTargetingUpdates()
        {
            var targetings = UnityEngine.Object.FindObjectsByType<BlessingTargeting>(FindObjectsSortMode.None);
            for (var index = 0; index < targetings.Length; index++)
            {
                if (targetings[index].isActiveAndEnabled)
                {
                    targetings[index].SendMessage("Update", SendMessageOptions.RequireReceiver);
                }
            }
        }

        private static IEnumerator BeginBlessingSelectionWithInput(
            Keyboard keyboard,
            Mouse mouse,
            Camera camera,
            BlessingTargeting targeting,
            EnemyBase target,
            Key selectionKey)
        {
            var screenPosition = GetScreenPosition(camera, target);
            yield return SetMouseState(mouse, screenPosition);
            yield return SetKeyboardState(keyboard, selectionKey);
            yield return SetKeyboardState(keyboard);
            Assert.That(targeting.IsSelecting, Is.True);
        }

        private static IEnumerator ApplyBlessingWithInput(
            Keyboard keyboard,
            Mouse mouse,
            Camera camera,
            BlessingTargeting targeting,
            EnemyBase target,
            Key selectionKey)
        {
            yield return BeginBlessingSelectionWithInput(
                keyboard,
                mouse,
                camera,
                targeting,
                target,
                selectionKey);
            yield return SetMouseState(mouse, GetScreenPosition(camera, target), true);
            yield return SetMouseState(mouse, GetScreenPosition(camera, target));
            Assert.That(targeting.IsSelecting, Is.False);
        }

        private static IEnumerator AdvanceRealtimeFrames(int frameCount)
        {
            for (var index = 0; index < frameCount; index++)
            {
                yield return new WaitForSecondsRealtime(0.02f);
            }
        }

        private static IEnumerator WaitForConditionWhileDriving(
            Func<bool> condition,
            Keyboard keyboard,
            PlayerLifeCycle player,
            DashAbility dash,
            float timeoutSeconds,
            string failureMessage)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(player.IsAlive, Is.True, "Player died before the observed encounter transition.");
                yield return DrivePlayerOneFrame(keyboard, player.transform, dash);
            }

            Assert.That(condition(), Is.True, failureMessage);
        }

        private static IEnumerator DrivePlayerOneFrame(Keyboard keyboard, Transform playerTransform, DashAbility dash)
        {
            var position = playerTransform.position;
            var direction = position.x >= 6f
                ? Key.W
                : position.y >= 3f
                    ? Key.A
                    : position.x <= -6f
                        ? Key.S
                        : position.y <= -3f
                            ? Key.D
                            : Key.D;

            if (dash != null && dash.CanDash)
            {
                yield return SetKeyboardState(keyboard, direction, Key.Space);
            }
            else
            {
                yield return SetKeyboardState(keyboard, direction);
            }
        }

        private static IEnumerator CollectSoulWithPlayerInput(
            Keyboard keyboard,
            PlayerLifeCycle player,
            SoulFragment soul)
        {
            Assert.That(soul, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 10f;
            while (!soul.IsCollected &&
                   Vector2.Distance(player.transform.position, soul.transform.position) > 0.1f &&
                   Time.realtimeSinceStartup < deadline)
            {
                Assert.That(player.IsAlive, Is.True, "Player died before reaching the soul fragment.");
                var offset = (Vector2)soul.transform.position - (Vector2)player.transform.position;
                var moveHorizontally = Mathf.Abs(offset.x) > 0.05f;
                var moveVertically = Mathf.Abs(offset.y) > 0.05f;
                if (moveHorizontally && moveVertically)
                {
                    yield return SetKeyboardState(
                        keyboard,
                        offset.x > 0f ? Key.D : Key.A,
                        offset.y > 0f ? Key.W : Key.S);
                }
                else if (moveHorizontally)
                {
                    yield return SetKeyboardState(keyboard, offset.x > 0f ? Key.D : Key.A);
                }
                else if (moveVertically)
                {
                    yield return SetKeyboardState(keyboard, offset.y > 0f ? Key.W : Key.S);
                }
                else
                {
                    break;
                }
            }

            yield return SetKeyboardState(keyboard);
            for (var index = 0; index < 4 && !soul.IsCollected; index++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(soul.IsCollected, Is.True, "Player input did not collect the spawned soul fragment.");
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

        private static IEnumerator MovePlayerToEnemyWithInput(
            Keyboard keyboard,
            PlayerLifeCycle player,
            EnemyBase target)
        {
            Assert.That(target, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 10f;
            while (Vector2.Distance(player.transform.position, target.transform.position) > 0.4f &&
                   Time.realtimeSinceStartup < deadline)
            {
                Assert.That(player.IsAlive, Is.True, "Player died before reaching the Giant-blessed enemy.");
                var offset = (Vector2)target.transform.position - (Vector2)player.transform.position;
                var moveHorizontally = Mathf.Abs(offset.x) > 0.05f;
                var moveVertically = Mathf.Abs(offset.y) > 0.05f;
                if (moveHorizontally && moveVertically)
                {
                    yield return SetKeyboardState(
                        keyboard,
                        offset.x > 0f ? Key.D : Key.A,
                        offset.y > 0f ? Key.W : Key.S);
                }
                else if (moveHorizontally)
                {
                    yield return SetKeyboardState(keyboard, offset.x > 0f ? Key.D : Key.A);
                }
                else if (moveVertically)
                {
                    yield return SetKeyboardState(keyboard, offset.y > 0f ? Key.W : Key.S);
                }
            }

            yield return SetKeyboardState(keyboard);
            Assert.That(
                Vector2.Distance(player.transform.position, target.transform.position),
                Is.LessThanOrEqualTo(0.4f),
                "Player input did not reach the Giant-blessed enemy.");
        }
        private static Vector2 GetScreenPosition(Camera camera, EnemyBase target)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(target, Is.Not.Null);
            var targetCollider = target.GetComponent<Collider2D>();
            Assert.That(targetCollider, Is.Not.Null);
            var screenPosition = camera.WorldToScreenPoint(targetCollider.bounds.center);
            return new Vector2(screenPosition.x, screenPosition.y);
        }

        private static void SubscribeCombatTargetRouting(
            IReadOnlyList<EnemyBase> enemies,
            Action<AttackContext> contextHandler,
            Action<AttackContext, Vector2> projectileHandler)
        {
            for (var index = 0; index < enemies.Count; index++)
            {
                var enemy = enemies[index];
                enemy.AttackState.ContextLocked += contextHandler;
                var archer = enemy as ArcherAI;
                if (archer != null)
                {
                    archer.ProjectileFired += projectileHandler;
                }
            }
        }

        private static void ReduceToOneHealth(EnemyBase target, long setupAttackId)
        {
            Assert.That(target, Is.Not.Null);
            var setupDamage = target.Health.CurrentHealth - 1;
            if (setupDamage <= 0)
            {
                return;
            }

            Assert.That(
                target.Health.TryApplyDamage(
                    new DamageEvent(setupAttackId, 9999, target.EntityId, setupDamage)),
                Is.True,
                "Nonlethal test setup damage was rejected.");
            Assert.That(target.Health.CurrentHealth, Is.EqualTo(1));
            Assert.That(target.Health.IsDead, Is.False);
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

        private static SoulFragment FindClosestUncollectedSoul(
            IReadOnlyList<SoulFragment> souls,
            Vector2 worldPosition)
        {
            SoulFragment closest = null;
            var closestDistance = float.PositiveInfinity;
            for (var index = 0; index < souls.Count; index++)
            {
                var soul = souls[index];
                if (soul == null || soul.IsCollected)
                {
                    continue;
                }

                var distance = Vector2.Distance(soul.transform.position, worldPosition);
                if (distance < closestDistance)
                {
                    closest = soul;
                    closestDistance = distance;
                }
            }

            return closest;
        }
        private static void RouteAttackToTarget(EnemyBase target, AttackContext context)
        {
            if (target == null || target.Health.IsDead || context.AttackerEntityId == target.EntityId)
            {
                return;
            }

            MoveEnemyToAttackArea(target, context);
        }

        private static void RouteLivingTargetsIntoAttackArea(
            IReadOnlyList<EnemyBase> targets,
            AttackContext context)
        {
            var positionedTarget = false;
            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                if (target == null || target.Health.IsDead || context.AttackerEntityId == target.EntityId)
                {
                    continue;
                }

                MoveEnemyToAttackArea(target, context, false);
                positionedTarget = true;
            }

            if (positionedTarget)
            {
                Physics2D.SyncTransforms();
            }
        }

        private static void MoveEnemyToAttackArea(
            EnemyBase target,
            AttackContext context,
            bool syncTransforms = true)
        {
            var attackPoint = context.Shape == AttackShape.Circle
                ? context.Origin + context.NormalizedDirection * (context.Range * 0.5f)
                : context.Origin + context.NormalizedDirection * Mathf.Min(1f, context.Range * 0.5f);
            var currentPosition = target.transform.position;
            target.transform.position = new Vector3(attackPoint.x, attackPoint.y, currentPosition.z);
            if (syncTransforms)
            {
                Physics2D.SyncTransforms();
            }
        }

        private static void SendFocusChanged(bool hasFocus, params Component[] components)
        {
            for (var index = 0; index < components.Length; index++)
            {
                components[index].SendMessage(
                    "OnApplicationFocus",
                    hasFocus,
                    SendMessageOptions.RequireReceiver);
            }
        }

        private static bool ContainsAudioEvent(
            IReadOnlyList<FunctionalAudioRecord> records,
            FunctionalAudioEvent expectedEvent)
        {
            for (var index = 0; index < records.Count; index++)
            {
                if (records[index].EventType == expectedEvent)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountAudioEvents(
            IReadOnlyList<FunctionalAudioRecord> records,
            FunctionalAudioEvent expectedEvent)
        {
            var count = 0;
            for (var index = 0; index < records.Count; index++)
            {
                if (records[index].EventType == expectedEvent)
                {
                    count++;
                }
            }

            return count;
        }

        private PlayerFixture CreatePlayerFixture()
        {
            var player = Track(new GameObject("PlayMode Player"));
            player.SetActive(false);
            player.AddComponent<CircleCollider2D>();
            var config = Track(ScriptableObject.CreateInstance<PlayerConfig>());
            var health = player.AddComponent<Health>();
            SetPrivateField(health, "entityId", 100);
            var inputRouter = player.AddComponent<PlayerInputRouter>();
            var dash = player.AddComponent<DashAbility>();
            SetPrivateField(dash, "config", config);
            SetPrivateField(dash, "playerTransform", player.transform);
            SetPrivateField(dash, "health", health);
            var controller = player.AddComponent<PlayerController>();
            SetPrivateField(controller, "config", config);
            SetPrivateField(controller, "playerTransform", player.transform);
            SetPrivateField(controller, "inputRouter", inputRouter);
            SetPrivateField(controller, "dashAbility", dash);
            var lifeCycle = player.AddComponent<PlayerLifeCycle>();
            SetPrivateField(lifeCycle, "playerTransform", player.transform);
            SetPrivateField(lifeCycle, "health", health);
            SetPrivateField(lifeCycle, "inputRouter", inputRouter);
            SetPrivateField(lifeCycle, "playerController", controller);
            SetPrivateField(lifeCycle, "dashAbility", dash);
            player.SetActive(true);

            return new PlayerFixture(player, health, inputRouter, controller, lifeCycle);
        }

        private RoomFixture CreateRoomFixture()
        {
            var definition = Track(ScriptableObject.CreateInstance<M1RoomDefinition>());
            var enemyHealths = new Health[5];
            for (var index = 0; index < enemyHealths.Length; index++)
            {
                enemyHealths[index] = CreateHealth(index + 1, 10);
            }

            var exitGate = CreateExitGate();
            var blessingTargeting = CreateBlessingTargeting();
            var soulPrefab = CreateSoulFragmentPrefab();
            var soulParent = Track(new GameObject("Soul Parent")).transform;
            var roomObject = Track(new GameObject("M1 Room Lifecycle"));
            roomObject.SetActive(false);
            var lifecycle = roomObject.AddComponent<M1RoomLifecycle>();
            SetPrivateField(lifecycle, "definition", definition);
            SetPrivateField(lifecycle, "enemyHealths", enemyHealths);
            SetPrivateField(lifecycle, "soulFragmentPrefab", soulPrefab);
            SetPrivateField(lifecycle, "soulParent", soulParent);
            SetPrivateField(lifecycle, "exitGate", exitGate);
            SetPrivateField(lifecycle, "blessingTargeting", blessingTargeting);
            roomObject.SetActive(true);

            return new RoomFixture(lifecycle, enemyHealths);
        }

        private Health CreateHealth(int entityId, int maximumHealth)
        {
            var gameObject = Track(new GameObject($"Enemy Health {entityId}"));
            gameObject.SetActive(false);
            var health = gameObject.AddComponent<Health>();
            SetPrivateField(health, "entityId", entityId);
            SetPrivateField(health, "maximumHealth", maximumHealth);
            gameObject.SetActive(true);
            return health;
        }

        private ExitGate CreateExitGate()
        {
            var gameObject = Track(new GameObject("Exit Gate"));
            gameObject.SetActive(false);
            var trigger = gameObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            var exitGate = gameObject.AddComponent<ExitGate>();
            SetPrivateField(exitGate, "entryTrigger", trigger);
            gameObject.SetActive(true);
            return exitGate;
        }

        private BlessingTargeting CreateBlessingTargeting()
        {
            var gameObject = Track(new GameObject("Blessing Targeting"));
            gameObject.SetActive(false);
            var targeting = gameObject.AddComponent<BlessingTargeting>();
            gameObject.SetActive(true);
            return targeting;
        }

        private SoulFragment CreateSoulFragmentPrefab()
        {
            var gameObject = Track(new GameObject("Soul Fragment Prefab"));
            gameObject.SetActive(false);
            var trigger = gameObject.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            var soulFragment = gameObject.AddComponent<SoulFragment>();
            SetPrivateField(soulFragment, "collectionTrigger", trigger);
            gameObject.SetActive(true);
            gameObject.SetActive(false);
            return soulFragment;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            var components = FindComponentsInScene<T>(scene);
            return components.Count == 0 ? null : components[0];
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

        private static EnemyBase AssertPosition(IReadOnlyList<EnemyBase> enemies, string name, Vector2 expected)
        {
            var enemy = FindEnemy(enemies, name);
            Assert.That(Vector2.Distance(enemy.transform.position, expected), Is.LessThanOrEqualTo(0.001f));
            return enemy;
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

        private sealed class PlayerFixture
        {
            public PlayerFixture(
                GameObject player,
                Health health,
                PlayerInputRouter inputRouter,
                PlayerController controller,
                PlayerLifeCycle lifeCycle)
            {
                Player = player;
                Health = health;
                InputRouter = inputRouter;
                Controller = controller;
                LifeCycle = lifeCycle;
                SpawnLocalPosition = player.transform.localPosition;
                SpawnLocalScale = player.transform.localScale;
            }

            public GameObject Player { get; }
            public Health Health { get; }
            public PlayerInputRouter InputRouter { get; }
            public PlayerController Controller { get; }
            public PlayerLifeCycle LifeCycle { get; }
            public Vector3 SpawnLocalPosition { get; }
            public Vector3 SpawnLocalScale { get; }
        }

        private sealed class RoomFixture
        {
            public RoomFixture(M1RoomLifecycle lifecycle, Health[] enemyHealths)
            {
                Lifecycle = lifecycle;
                EnemyHealths = enemyHealths;
            }

            public M1RoomLifecycle Lifecycle { get; }
            public Health[] EnemyHealths { get; }
        }
    }
}
