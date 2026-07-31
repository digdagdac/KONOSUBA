using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Overbless.Tests.PlayMode
{
    /// <summary>
    /// Deterministic timing capture for the monster locomotion contract.
    /// Every measurement is driven by a fixed capture delta so the same commit
    /// always produces the same seconds, which lets a baseline run and a
    /// candidate run be compared without human timing.
    /// The harness deliberately avoids v002-only runtime API so the identical
    /// file can be executed against a pre-v002 checkout to record the baseline.
    /// </summary>
    public sealed class MonsterLocomotionTimingTests
    {
        private const string TimingEvidenceRelativePath = "Evidence/Verification/monster-locomotion-timing.json";
        private const string TimingSchema = "overbless.monster-locomotion-timing/v1";
        private const float ToleranceRatio = 0.1f;
        private const float ArcherDistanceTolerance = 0.05f;
        private const float ChaseStartMargin = 3f;
        private const float RetreatStartOffset = 0.5f;
        private const int MaximumFramesPerMeasurement = 2400;

        private static readonly List<TimingSample> Samples = new List<TimingSample>();

        // Rigidbody2D writes made in Update only reach the transform the AI reads on the next
        // physics step, so the capture step is aligned with the fixed step. One physics step per
        // captured frame keeps every measurement deterministic and free of sync stalls.
        private static float frameDeltaSeconds = 0.02f;

        private readonly List<UnityEngine.Object> objectsToDestroy = new List<UnityEngine.Object>();
        private float captureDeltaTimeBeforeTest;
        private float timeScaleBeforeTest;

        [SetUp]
        public void SetUp()
        {
            Samples.Clear();
            captureDeltaTimeBeforeTest = Time.captureDeltaTime;
            timeScaleBeforeTest = Time.timeScale;
            frameDeltaSeconds = Time.fixedDeltaTime;
            Time.timeScale = 1f;
            Time.captureDeltaTime = frameDeltaSeconds;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                for (var index = objectsToDestroy.Count - 1; index >= 0; index--)
                {
                    if (objectsToDestroy[index] != null)
                    {
                        UnityEngine.Object.Destroy(objectsToDestroy[index]);
                    }
                }

                objectsToDestroy.Clear();
                yield return null;
            }
            finally
            {
                Time.captureDeltaTime = captureDeltaTimeBeforeTest;
                Time.timeScale = timeScaleBeforeTest;
            }
        }

        [UnityTest]
        public IEnumerator ArcherApproachAndRetreatKeepConstantSpeedWithoutStalling()
        {
            var definitions = new EnemyDefinitionSet();
            yield return definitions.Load();

            var chase = CreateTarget("Archer Chase Target", new Vector2(-6.5f, 0f));
            var archer = CreateEnemy<ArcherAI>(definitions.Archer, 8101, new Vector2(6.5f, 0f), chase);
            yield return null;
            AssertDeterministicFrameStep();

            var stats = archer.Enemy.RuntimeStats;
            var startDistance = Vector2.Distance(archer.Transform.position, chase.position);
            Assert.That(
                startDistance,
                Is.GreaterThan(stats.EngagementRange + (ChaseStartMargin * 0.5f)),
                "The chase measurement must start outside engagement range.");

            var chaseRun = new MovementRun(archer.Transform, chase);
            yield return DriveUntil(
                chaseRun,
                () => Vector2.Distance(archer.Transform.position, chase.position) <= stats.EngagementRange,
                "archer never reached engagement range");
            RecordMovementSample("chase_to_range", "archer", chaseRun);

            DestroyFixture(archer);
            yield return null;

            var band = CreateTarget("Archer Retreat Target", Vector2.zero);
            var retreatStart = stats.PreferredDistance - RetreatStartOffset;
            var retreater = CreateEnemy<ArcherAI>(definitions.Archer, 8102, new Vector2(retreatStart, 0f), band);
            yield return null;

            var safeDistance = stats.PreferredDistance - ArcherDistanceTolerance;
            var retreatRun = new MovementRun(retreater.Transform, band);
            yield return DriveUntil(
                retreatRun,
                () => Vector2.Distance(retreater.Transform.position, band.position) >= safeDistance,
                "archer never retreated back into its preferred band");
            RecordMovementSample("retreat_to_safe_band", "archer", retreatRun);

            DestroyFixture(retreater);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MinionCadenceKeepsWarningAndJudgmentTimingWithAndWithoutHaste()
        {
            var definitions = new EnemyDefinitionSet();
            yield return definitions.Load();

            yield return MeasureMinionCadence(definitions.Minion, 8201, false);
            yield return MeasureMinionCadence(definitions.Minion, 8202, true);
        }

        [UnityTest]
        public IEnumerator CapturedLocomotionTimingIsWrittenAsRepeatableEvidence()
        {
            yield return ArcherApproachAndRetreatKeepConstantSpeedWithoutStalling();
            yield return MinionCadenceKeepsWarningAndJudgmentTimingWithAndWithoutHaste();

            Assert.That(Samples.Count, Is.EqualTo(7), "Every contract metric must be captured before evidence is written.");
            var evidencePath = ResolveProjectPath(TimingEvidenceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath));
            File.WriteAllText(evidencePath, BuildEvidenceJson(), new UTF8Encoding(false));
            Assert.That(File.Exists(evidencePath), Is.True, $"Timing evidence was not written: {TimingEvidenceRelativePath}");
        }

        private IEnumerator MeasureMinionCadence(EnemyDefinition definition, int entityId, bool haste)
        {
            var suffix = haste ? "haste" : "plain";
            var target = CreateTarget($"Minion Cadence Target {suffix}", Vector2.zero);
            var minion = CreateEnemy<MinionAI>(definition, entityId, new Vector2(0.5f, 0f), target);
            if (haste)
            {
                minion.Enemy.RecomputeRuntimeStats(new[] { BlessingType.Haste });
            }

            var timeline = new PhaseTimeline(minion.Enemy);
            yield return null;
            AssertDeterministicFrameStep();

            var stats = minion.Enemy.RuntimeStats;
            Assert.That(stats.HasHaste, Is.EqualTo(haste), "Blessing state must match the requested cadence scenario.");

            yield return DriveUntil(
                null,
                () => timeline.HasSecondJudgment,
                $"minion never produced two judgments ({suffix})");

            var warningDuration = Mathf.Max(stats.WarningDuration, AttackStateMachine.MinimumWarningDuration);
            if (!haste)
            {
                RecordCadenceSample(
                    "preparation_to_judgment",
                    "minion",
                    timeline.FirstJudgmentTime - timeline.FirstWarningTime,
                    warningDuration);
            }

            RecordCadenceSample(
                $"judgment_to_next_eligible_warning_{suffix}",
                "minion",
                timeline.SecondWarningTime - timeline.FirstJudgmentTime,
                stats.RecoveryDuration + stats.AttackCooldown);
            RecordCadenceSample(
                $"judgment_to_next_judgment_{suffix}",
                "minion",
                timeline.SecondJudgmentTime - timeline.FirstJudgmentTime,
                stats.RecoveryDuration + stats.AttackCooldown + warningDuration);

            timeline.Dispose();
            DestroyFixture(minion);
            yield return null;
        }

        private static IEnumerator DriveUntil(MovementRun run, Func<bool> completed, string failureMessage)
        {
            var startTime = Time.time;
            for (var frame = 0; frame < MaximumFramesPerMeasurement; frame++)
            {
                if (completed())
                {
                    if (run != null)
                    {
                        run.Complete(Time.time - startTime, frame);
                    }

                    yield break;
                }

                if (run != null)
                {
                    run.Sample();
                }

                yield return null;
            }

            Assert.Fail($"Timing measurement timed out after {MaximumFramesPerMeasurement} frames: {failureMessage}");
        }

        private static void AssertDeterministicFrameStep()
        {
            Assert.That(
                Time.deltaTime,
                Is.EqualTo(frameDeltaSeconds).Within(0.0005f),
                "Timing capture requires a deterministic fixed frame step.");
        }

        private static void RecordMovementSample(string metric, string role, MovementRun run)
        {
            Assert.That(run.Completed, Is.True, $"Movement metric '{metric}' never completed.");
            Assert.That(run.MaximumFrameDisplacement, Is.GreaterThan(0f), $"Movement metric '{metric}' never moved.");

            var steadySpeed = run.MaximumFrameDisplacement / frameDeltaSeconds;
            var expectedSeconds = run.TravelDistance / steadySpeed;
            AssertWithinTolerance(metric, run.ElapsedSeconds, expectedSeconds);
            Samples.Add(
                new TimingSample(
                    metric,
                    role,
                    run.ElapsedSeconds,
                    expectedSeconds,
                    run.Frames,
                    run.TravelDistance,
                    steadySpeed,
                    run.StartDistance,
                    run.EndDistance));
        }

        private static void RecordCadenceSample(string metric, string role, float measuredSeconds, float expectedSeconds)
        {
            AssertWithinTolerance(metric, measuredSeconds, expectedSeconds);
            Samples.Add(new TimingSample(metric, role, measuredSeconds, expectedSeconds, -1, 0f, 0f, 0f, 0f));
        }

        private static void AssertWithinTolerance(string metric, float measuredSeconds, float expectedSeconds)
        {
            Assert.That(measuredSeconds, Is.GreaterThan(0f), $"Metric '{metric}' produced a non-positive duration.");
            var allowance = (expectedSeconds * ToleranceRatio) + frameDeltaSeconds;
            Assert.That(
                Mathf.Abs(measuredSeconds - expectedSeconds),
                Is.LessThanOrEqualTo(allowance),
                $"Metric '{metric}' drifted from its contract: measured {measuredSeconds:F4}s, expected {expectedSeconds:F4}s.");
        }

        private static string BuildEvidenceJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"schema\":\"").Append(TimingSchema).Append("\",");
            builder.Append("\"capturedUtc\":\"")
                .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                .Append("\",");
            builder.Append("\"unityVersion\":\"").Append(Application.unityVersion).Append("\",");
            builder.Append("\"frameDeltaSeconds\":").Append(Format(frameDeltaSeconds)).Append(',');
            builder.Append("\"toleranceRatio\":").Append(Format(ToleranceRatio)).Append(',');
            builder.Append("\"metrics\":[");
            for (var index = 0; index < Samples.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                Samples[index].Append(builder);
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static string Format(float value)
        {
            return value.ToString("F6", CultureInfo.InvariantCulture);
        }

        private static string ResolveProjectPath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private Transform CreateTarget(string name, Vector2 position)
        {
            var target = Track(new GameObject(name));
            target.transform.position = position;
            return target.transform;
        }

        private EnemyFixture CreateEnemy<TEnemy>(
            EnemyDefinition definition,
            int entityId,
            Vector2 position,
            Transform target)
            where TEnemy : EnemyBase
        {
            var host = Track(new GameObject($"Timing {typeof(TEnemy).Name} {entityId}"));
            host.SetActive(false);
            host.transform.position = position;
            var body = host.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            var bodyCollider = host.AddComponent<CircleCollider2D>();
            bodyCollider.radius = 0.25f;
            var health = host.AddComponent<Health>();
            SetPrivateField(health, "entityId", entityId);
            SetPrivateField(health, "maximumHealth", 50);
            var enemy = host.AddComponent<TEnemy>();
            SetPrivateField(enemy, "definition", definition);
            SetPrivateField(enemy, "health", health);
            SetPrivateField(enemy, "spawnTransform", host.transform);
            host.SetActive(true);
            enemy.SetPlayerTarget(target);
            return new EnemyFixture(host, enemy);
        }

        private void DestroyFixture(EnemyFixture fixture)
        {
            objectsToDestroy.Remove(fixture.Host);
            UnityEngine.Object.Destroy(fixture.Host);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            objectsToDestroy.Add(value);
            return value;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? target.GetType().BaseType?.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private sealed class EnemyDefinitionSet
        {
            private const string GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";

            public EnemyDefinition Archer { get; private set; }
            public EnemyDefinition Minion { get; private set; }
            public EnemyDefinition Dasher { get; private set; }

            public IEnumerator Load()
            {
                var captureDeltaTime = Time.captureDeltaTime;
                Time.captureDeltaTime = 0f;
                var load = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                    GuidedScenePath,
                    UnityEngine.SceneManagement.LoadSceneMode.Additive);
                Assert.That(load, Is.Not.Null, "The guided scene must be loadable to read authored enemy definitions.");
                yield return load;
                yield return null;

                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(GuidedScenePath);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var enemies = roots[rootIndex].GetComponentsInChildren<EnemyBase>(true);
                    for (var enemyIndex = 0; enemyIndex < enemies.Length; enemyIndex++)
                    {
                        var enemy = enemies[enemyIndex];
                        if (enemy is ArcherAI && Archer == null)
                        {
                            Archer = enemy.Definition;
                        }
                        else if (enemy is MinionAI && Minion == null)
                        {
                            Minion = enemy.Definition;
                        }
                        else if (enemy is DasherAI && Dasher == null)
                        {
                            Dasher = enemy.Definition;
                        }
                    }
                }

                var unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
                if (unload != null)
                {
                    yield return unload;
                }

                yield return null;
                Time.captureDeltaTime = captureDeltaTime;
                Time.timeScale = 1f;
                Assert.That(Archer, Is.Not.Null, "The authored archer definition was not found in the guided scene.");
                Assert.That(Minion, Is.Not.Null, "The authored minion definition was not found in the guided scene.");
                Assert.That(Dasher, Is.Not.Null, "The authored dasher definition was not found in the guided scene.");
            }
        }

        private sealed class EnemyFixture
        {
            public EnemyFixture(GameObject host, EnemyBase enemy)
            {
                Host = host;
                Enemy = enemy;
            }

            public GameObject Host { get; }
            public EnemyBase Enemy { get; }
            public Transform Transform => Host.transform;
        }

        private sealed class MovementRun
        {
            private readonly Transform mover;
            private readonly Transform reference;
            private readonly Vector2 startPosition;
            private Vector2 previousPosition;

            public MovementRun(Transform mover, Transform reference)
            {
                this.mover = mover;
                this.reference = reference;
                startPosition = mover.position;
                previousPosition = startPosition;
                StartDistance = Vector2.Distance(startPosition, reference.position);
            }

            public float StartDistance { get; }
            public float EndDistance { get; private set; }
            public float ElapsedSeconds { get; private set; }
            public int Frames { get; private set; }
            public float TravelDistance { get; private set; }
            public float MaximumFrameDisplacement { get; private set; }
            public bool Completed { get; private set; }

            public void Sample()
            {
                var current = (Vector2)mover.position;
                var displacement = Vector2.Distance(current, previousPosition);
                if (displacement > MaximumFrameDisplacement)
                {
                    MaximumFrameDisplacement = displacement;
                }

                previousPosition = current;
            }

            public void Complete(float elapsedSeconds, int frames)
            {
                Sample();
                ElapsedSeconds = elapsedSeconds;
                Frames = frames;
                EndDistance = Vector2.Distance(mover.position, reference.position);
                TravelDistance = Vector2.Distance((Vector2)mover.position, startPosition);
                Completed = true;
            }
        }

        private sealed class PhaseTimeline : IDisposable
        {
            private readonly AttackStateMachine attackState;
            private AttackPhase previousPhase = AttackPhase.Idle;
            private int warningCount;
            private int judgmentCount;

            public PhaseTimeline(EnemyBase enemy)
            {
                attackState = enemy.AttackState;
                attackState.PhaseChanged += HandlePhaseChanged;
            }

            public float FirstWarningTime { get; private set; }
            public float FirstJudgmentTime { get; private set; }
            public float SecondWarningTime { get; private set; }
            public float SecondJudgmentTime { get; private set; }
            public bool HasSecondJudgment => judgmentCount >= 2;

            public void Dispose()
            {
                attackState.PhaseChanged -= HandlePhaseChanged;
            }

            private void HandlePhaseChanged(AttackPhase phase)
            {
                if (phase == AttackPhase.Warning)
                {
                    warningCount++;
                    if (warningCount == 1)
                    {
                        FirstWarningTime = Time.time;
                    }
                    else if (warningCount == 2)
                    {
                        SecondWarningTime = Time.time;
                    }
                }
                else if (previousPhase == AttackPhase.Warning)
                {
                    judgmentCount++;
                    if (judgmentCount == 1)
                    {
                        FirstJudgmentTime = Time.time;
                    }
                    else if (judgmentCount == 2)
                    {
                        SecondJudgmentTime = Time.time;
                    }
                }

                previousPhase = phase;
            }
        }

        private readonly struct TimingSample
        {
            private readonly string metric;
            private readonly string role;
            private readonly float measuredSeconds;
            private readonly float expectedSeconds;
            private readonly int frames;
            private readonly float travelDistance;
            private readonly float steadySpeed;
            private readonly float startDistance;
            private readonly float endDistance;

            public TimingSample(
                string metric,
                string role,
                float measuredSeconds,
                float expectedSeconds,
                int frames,
                float travelDistance,
                float steadySpeed,
                float startDistance,
                float endDistance)
            {
                this.metric = metric;
                this.role = role;
                this.measuredSeconds = measuredSeconds;
                this.expectedSeconds = expectedSeconds;
                this.frames = frames;
                this.travelDistance = travelDistance;
                this.steadySpeed = steadySpeed;
                this.startDistance = startDistance;
                this.endDistance = endDistance;
            }

            public void Append(StringBuilder builder)
            {
                builder.Append("{\"metric\":\"").Append(metric).Append("\",");
                builder.Append("\"role\":\"").Append(role).Append("\",");
                builder.Append("\"measuredSeconds\":").Append(Format(measuredSeconds)).Append(',');
                builder.Append("\"contractSeconds\":").Append(Format(expectedSeconds)).Append(',');
                builder.Append("\"deltaPercentVsContract\":")
                    .Append(Format(expectedSeconds <= 0f ? 0f : ((measuredSeconds - expectedSeconds) / expectedSeconds) * 100f))
                    .Append(',');
                builder.Append("\"frames\":").Append(frames.ToString(CultureInfo.InvariantCulture)).Append(',');
                builder.Append("\"travelDistance\":").Append(Format(travelDistance)).Append(',');
                builder.Append("\"steadySpeed\":").Append(Format(steadySpeed)).Append(',');
                builder.Append("\"startDistance\":").Append(Format(startDistance)).Append(',');
                builder.Append("\"endDistance\":").Append(Format(endDistance)).Append('}');
            }
        }
    }
}
