using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Overbless.Tests.PlayMode
{
    /// <summary>
    /// Covers the authored contest route as a player experiences it. This deliberately uses
    /// the room lifecycle and gate contracts rather than test-only scene loading shortcuts.
    /// </summary>
    public sealed class ContestRunFlowTests
    {
        private const string TitleScenePath = "Assets/_Project/Scenes/Title.unity";
        private const string GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";
        private const string Room02ScenePath = "Assets/_Project/Scenes/Room_02.unity";
        private const string Room03ScenePath = "Assets/_Project/Scenes/Room_03.unity";
        private const string ResultScenePath = "Assets/_Project/Scenes/Result.unity";
        private const float SceneLoadTimeoutSeconds = 5f;

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
#if UNITY_EDITOR
            yield return LeaveFreshEmptyScene();
#endif

            for (var index = inputDevicesToRemove.Count - 1; index >= 0; index--)
            {
                InputSystem.RemoveDevice(inputDevicesToRemove[index]);
            }

            inputDevicesToRemove.Clear();
            Time.timeScale = timeScaleBeforeTest;
            AudioListener.pause = audioPausedBeforeTest;
            InputSystem.settings.updateMode = inputUpdateModeBeforeTest;
            InputSystem.settings.backgroundBehavior = inputBackgroundBehaviorBeforeTest;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInputBehaviorBeforeTest;
            Application.runInBackground = runInBackgroundBeforeTest;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContestRun_TraversesAuthoredTitleToResultSequence()
        {
#if !UNITY_EDITOR
            Assert.Ignore("The complete contest route is an editor-only PlayMode integration test.");
            yield break;
#else
            var mouse = AddMouse();

            yield return LoadSceneSingle(TitleScenePath);
            var title = FindComponentInScene<TrustedInputScreen>(GetLoadedScene(TitleScenePath));
            Assert.That(title.NextScene, Is.EqualTo("M1_GuidedValidation"));
            yield return AdvanceWithTrustedMouse(title, mouse);
            yield return WaitForSingleScene(GuidedScenePath);

            yield return CompleteRoomAndAdvance(GuidedScenePath, "Room_02", mouse);
            yield return WaitForSingleScene(Room02ScenePath);

            yield return CompleteRoomAndAdvance(Room02ScenePath, "Room_03", mouse);
            yield return WaitForSingleScene(Room03ScenePath);

            yield return CompleteRoomAndAdvance(Room03ScenePath, "Result", mouse);
            yield return WaitForSingleScene(ResultScenePath);

            var result = FindComponentInScene<TrustedInputScreen>(GetLoadedScene(ResultScenePath));
            Assert.That(result.NextScene, Is.EqualTo("Title"));
            Assert.That(result.HasAdvanced, Is.False, "The result screen must remain visible until a new trusted input.");
#endif
        }

#if UNITY_EDITOR
        private static IEnumerator LeaveFreshEmptyScene()
        {
            var cleanupScene = SceneManager.CreateScene($"ContestRunFlowCleanup_{Guid.NewGuid():N}");
            Assert.That(SceneManager.SetActiveScene(cleanupScene), Is.True);

            yield return UnloadSceneIfLoaded(TitleScenePath);
            yield return UnloadSceneIfLoaded(GuidedScenePath);
            yield return UnloadSceneIfLoaded(Room02ScenePath);
            yield return UnloadSceneIfLoaded(Room03ScenePath);
            yield return UnloadSceneIfLoaded(ResultScenePath);

            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(cleanupScene));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
        }

        private static IEnumerator UnloadSceneIfLoaded(string scenePath)
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

        private static IEnumerator LoadSceneSingle(string scenePath)
        {
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return WaitForSingleScene(scenePath);
        }

        private IEnumerator CompleteRoomAndAdvance(string scenePath, string expectedNextScene, Mouse mouse)
        {
            var scene = GetLoadedScene(scenePath);
            var webGate = FindComponentInScene<WebStartGate>(scene);
            var lifecycle = FindComponentInScene<M1RoomLifecycle>(scene);
            var exitGate = FindComponentInScene<ExitGate>(scene);
            var sequence = FindComponentInScene<RoomSequenceController>(scene);
            var player = FindComponentInScene<PlayerLifeCycle>(scene);
            var enemies = FindComponentsInScene<EnemyBase>(scene);
            var spawnedSouls = new List<SoulFragment>();

            Assert.That(sequence.NextScene, Is.EqualTo(expectedNextScene));
            Assert.That(enemies.Count, Is.EqualTo(5));
            Assert.That(webGate.IsAwaitingGesture, Is.True);
            yield return StartWithTrustedGesture(webGate, mouse);

            lifecycle.SoulSpawned += spawnedSouls.Add;
            try
            {
                for (var index = 0; index < enemies.Count; index++)
                {
                    var enemy = enemies[index];
                    Assert.That(
                        lifecycle.TryApplyDamage(
                            enemy.Health,
                            new DamageEvent(
                                70000 + index,
                                9000,
                                enemy.EntityId,
                                enemy.Health.CurrentHealth)),
                        Is.True,
                        $"Room lifecycle rejected the lethal event for {enemy.name}.");
                    Assert.That(enemy.Health.IsDead, Is.True, $"{enemy.name} did not die from the accepted lethal event.");
                }
            }
            finally
            {
                lifecycle.SoulSpawned -= spawnedSouls.Add;
            }

            Assert.That(spawnedSouls.Count, Is.EqualTo(M1RoomDefinition.RequiredSoulCount));
            for (var index = 0; index < spawnedSouls.Count; index++)
            {
                Assert.That(spawnedSouls[index].TryCollect(player), Is.True, "Each lifecycle-spawned soul must collect once.");
            }

            Assert.That(lifecycle.SoulCount, Is.EqualTo(M1RoomDefinition.RequiredSoulCount));
            Assert.That(exitGate.IsOpen, Is.True, "Collecting the required souls must open the room exit.");
            Assert.That(exitGate.TryEnter(player), Is.True, "The living player must enter the opened exit.");
            Assert.That(sequence.HasHandledEntry, Is.True);
        }

        private static IEnumerator AdvanceWithTrustedMouse(TrustedInputScreen screen, Mouse mouse)
        {
            Assert.That(screen, Is.Not.Null);
            Assert.That(screen.HasAdvanced, Is.False);

            yield return SetMouseStateAndPumpUpdate(screen, mouse, false);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return SetMouseStateAndPumpUpdate(screen, mouse, true);

            Assert.That(screen.HasAdvanced, Is.True, "The title screen did not accept a fresh trusted mouse gesture.");
        }

        private static IEnumerator StartWithTrustedGesture(WebStartGate webGate, Mouse mouse)
        {
            yield return SetMouseStateAndPumpUpdate(webGate, mouse, false);
            yield return SetMouseStateAndPumpUpdate(webGate, mouse, true);

            Assert.That(webGate.IsStarted, Is.True);
            Assert.That(webGate.IsAwaitingGesture, Is.False);
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
        }

        private static IEnumerator SetMouseStateAndPumpUpdate(MonoBehaviour target, Mouse mouse, bool pressed)
        {
            mouse.MakeCurrent();
            InputSystem.EnableDevice(mouse);
            var state = new MouseState { position = mouse.position.ReadValue() };
            if (pressed)
            {
                state = state.WithButton(MouseButton.Left);
            }

            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
            target.SendMessage("Update", SendMessageOptions.RequireReceiver);
            yield return null;
        }

        private static IEnumerator WaitForSingleScene(string expectedScenePath)
        {
            var deadline = Time.realtimeSinceStartup + SceneLoadTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var scene = SceneManager.GetSceneByPath(expectedScenePath);
                if (scene.IsValid() && scene.isLoaded && SceneManager.GetActiveScene().path == expectedScenePath)
                {
                    Assert.That(SceneManager.sceneCount, Is.EqualTo(1), "Contest flow must use single-scene transitions.");
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for single-scene transition to {expectedScenePath}.");
        }

        private Mouse AddMouse()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            inputDevicesToRemove.Add(mouse);
            return mouse;
        }

        private static Scene GetLoadedScene(string scenePath)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, $"Scene {scenePath} must be loaded.");
            return scene;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            var components = FindComponentsInScene<T>(scene);
            Assert.That(components.Count, Is.EqualTo(1), $"Expected exactly one {typeof(T).Name} in {scene.path}.");
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
#endif
    }
}
