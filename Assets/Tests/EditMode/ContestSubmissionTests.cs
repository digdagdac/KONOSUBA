using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Overbless.Editor.Bootstrap;
using Overbless.Editor.Build;

namespace Overbless.Tests.EditMode
{
    /// <summary>
    /// Locks the submission shape: one continuous run from title to result, a build path that
    /// stays separate from the development contract, and a distribution decision that never
    /// claims the user-owned entry gate.
    /// </summary>
    public sealed class ContestSubmissionTests
    {
        private const string ApprovalPath = "Docs/Decisions/CONTEST_SUBMISSION_APPROVAL.json";
        private const string TitleArtSpecPath = "Docs/Submission/TITLE_ART_SPEC_KO.md";
        private const string TitleScenePath = "Assets/_Project/Scenes/Title.unity";
        private const string ResultScenePath = "Assets/_Project/Scenes/Result.unity";
        private const string GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";
        private const string Room02ScenePath = "Assets/_Project/Scenes/Room_02.unity";
        private const string Room03ScenePath = "Assets/_Project/Scenes/Room_03.unity";

        [Test]
        public void SubmissionBuildCoversTheWholeRunInPlayOrder()
        {
            Assert.That(
                ContestWebGLBuilder.Scenes,
                Is.EqualTo(new[]
                {
                    TitleScenePath,
                    GuidedScenePath,
                    Room02ScenePath,
                    Room03ScenePath,
                    ResultScenePath
                }),
                "A reviewer opens one link, so the build must contain the whole run in play order.");

            for (var index = 0; index < ContestWebGLBuilder.Scenes.Length; index++)
            {
                Assert.That(
                    File.Exists(Path.GetFullPath(ContestWebGLBuilder.Scenes[index])),
                    Is.True,
                    $"'{ContestWebGLBuilder.Scenes[index]}' must exist before a submission build.");
            }
        }

        [Test]
        public void TheRunAdvancesFromTitleThroughEveryRoomToTheResultAndBack()
        {
            AssertScenePointsAt(TitleScenePath, "nextScene: M1_GuidedValidation");
            AssertScenePointsAt(GuidedScenePath, "nextScene: Room_02");
            AssertScenePointsAt(Room02ScenePath, "nextScene: Room_03");
            AssertScenePointsAt(Room03ScenePath, "nextScene: Result");
            AssertScenePointsAt(ResultScenePath, "nextScene: Title");
        }

        [Test]
        public void EveryRoomExplainsTheStartGateAndTheDefeatRecovery()
        {
            foreach (var scenePath in new[] { GuidedScenePath, Room02ScenePath, Room03ScenePath })
            {
                var scene = ReadText(scenePath);
                Assert.That(scene, Does.Contain("StartPrompt"), $"'{scenePath}' must contain the start prompt.");
                Assert.That(scene, Does.Contain("CLICK TO BEGIN"), $"'{scenePath}' must say what it waits for.");
                Assert.That(scene, Does.Contain("RunOutcome"), $"'{scenePath}' must contain the defeat panel.");
                Assert.That(
                    scene,
                    Does.Contain("PRESS  R  TO RESTART THIS ROOM"),
                    $"'{scenePath}' must say how to recover from a defeat.");
            }
        }

        [Test]
        public void TitleScreenStandsInUntilTheKeyVisualIsDelivered()
        {
            var keyVisualExists = File.Exists(Path.GetFullPath(M1ContentBootstrap.TitleKeyVisualPath));
            var title = ReadText(TitleScenePath);

            Assert.That(
                title.Contains("RepresentativePortrait", StringComparison.Ordinal),
                Is.EqualTo(!keyVisualExists),
                keyVisualExists
                    ? "The delivered key visual must replace the representative stand-in."
                    : "Without a key visual the title must stand in with the authoritative combat sprite.");

            Assert.That(title, Does.Contain("KeyVisual"), "The title must keep the key-visual slot either way.");
            Assert.That(
                title,
                Does.Contain("CLICK OR PRESS ANY KEY TO START"),
                "The title must tell a reviewer how to start.");
            Assert.That(
                File.Exists(Path.GetFullPath(TitleArtSpecPath)),
                Is.True,
                "The art that is still missing must stay documented with its generation instructions.");
        }

        [Test]
        public void SubmissionBuildStaysSeparateFromTheDevelopmentContract()
        {
            Assert.That(ContestWebGLBuilder.OutputDirectory, Is.Not.EqualTo(DevelopmentWebGLBuilder.OutputDirectory));
            Assert.That(ContestWebGLBuilder.OutputDirectory, Is.Not.EqualTo(DevelopmentWebGLBuilder.M2OutputDirectory));
            Assert.That(
                ContestWebGLBuilder.PublishBranch,
                Is.EqualTo("gh-pages"),
                "GitHub Pages serves a branch here, because a 'docs' directory would collide with the tracked 'Docs' tree on a case-insensitive filesystem.");
            Assert.That(
                File.Exists(Path.GetFullPath(ContestWebGLBuilder.PublishScript)),
                Is.True,
                "The publish step must stay a reviewable script rather than an editor side effect.");
            Assert.That(
                ContestWebGLBuilder.PageTitle,
                Does.Contain("이 멋진 적에게 축복을"),
                "The browser tab carries the Korean title, because the engine font has no Hangul glyphs.");
        }

        [Test]
        public void PostprocessedSubmissionTemplateVersionsEveryWebGlPayload()
        {
            var root = Path.Combine(Path.GetTempPath(), "overbless-submission-cache-" + Guid.NewGuid().ToString("N"));
            var buildDirectory = Path.Combine(root, "Build");
            Directory.CreateDirectory(buildDirectory);
            try
            {
                File.WriteAllText(
                    Path.Combine(root, "index.html"),
                    "<html><head><title>Default</title></head><body>" +
                    "<div id=\"unity-build-title\">Default</div>" +
                    "<script>" +
                    "var buildUrl = \"Build\";" +
                    "var loaderUrl = buildUrl + \"/Overbless_Web.loader.js\";" +
                    "var config = {" +
                    "dataUrl: buildUrl + \"/Overbless_Web.data.unityweb\"," +
                    "frameworkUrl: buildUrl + \"/Overbless_Web.framework.js.unityweb\"," +
                    "codeUrl: buildUrl + \"/Overbless_Web.wasm.unityweb\"};" +
                    "</script></body></html>");

                foreach (var fileName in new[]
                {
                    "Overbless_Web.loader.js",
                    "Overbless_Web.data.unityweb",
                    "Overbless_Web.framework.js.unityweb",
                    "Overbless_Web.wasm.unityweb"
                })
                {
                    File.WriteAllText(Path.Combine(buildDirectory, fileName), fileName);
                }

                var postprocess = typeof(ContestWebGLBuilder).GetMethod(
                    "PostprocessTemplate",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(postprocess, Is.Not.Null);
                postprocess.Invoke(null, new object[] { root });

                var html = File.ReadAllText(Path.Combine(root, "index.html"));
                var version = ExtractBuildVersion(html, "Overbless_Web.loader.js");
                Assert.That(version, Is.Not.Empty);
                Assert.That(
                    ExtractBuildVersion(html, "Overbless_Web.data.unityweb"),
                    Is.EqualTo(version));
                Assert.That(
                    ExtractBuildVersion(html, "Overbless_Web.framework.js.unityweb"),
                    Is.EqualTo(version));
                Assert.That(
                    ExtractBuildVersion(html, "Overbless_Web.wasm.unityweb"),
                    Is.EqualTo(version));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void DistributionDecisionIsRecordedWithoutClaimingTheEntryGate()
        {
            var approval = ReadText(ApprovalPath);

            Assert.That(approval, Does.Contain("\"decision\": \"approved\""));
            Assert.That(approval, Does.Contain("\"decidedBy\": \"user\""));
            Assert.That(approval, Does.Contain("NAN 2026"));
            Assert.That(
                approval,
                Does.Contain("Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json"),
                "A new decision must reference the one it extends instead of editing it.");
            Assert.That(
                approval,
                Does.Contain("does not create, replace, sign or imply an M2EntryGate PASS"),
                "The distribution decision must state that it is not the entry gate.");
            Assert.That(approval, Does.Contain("Golem runtime activation"), "Excluded scope must stay listed.");

            var predecessor = ReadText("Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json");
            Assert.That(
                predecessor,
                Does.Contain("\"decidedAtUtc\": \"2026-07-14T04:48:59Z\""),
                "The earlier approval is write-once evidence and must keep its recorded bytes.");
        }

        [Test]
        public void RoomObjectivesTeachTheNewRuleWithoutCoaching()
        {
            // First-room language must teach induction before Echo/pillar appear.
            AssertScenePointsAt(GuidedScenePath, "MAKE THEIR ATTACKS HIT EACH OTHER");
            AssertScenePointsAt(GuidedScenePath, "HASTE OR GIANT");
            AssertScenePointsAt(GuidedScenePath, "COLLECT 3 SOULS");

            // Room 02 must surface Echo before a player can discover it by accident.
            AssertScenePointsAt(Room02ScenePath, "ECHO REPLAYS THE LOCKED ATTACK");
            AssertScenePointsAt(Room02ScenePath, "BLESS WITH ECHO");

            // Room 03 must name the pillar so the new obstacle is not silent geometry.
            AssertScenePointsAt(Room03ScenePath, "THE PILLAR SPLITS THE PATH");
            AssertScenePointsAt(Room03ScenePath, "ROUTE AROUND THE PILLAR");
        }

        [Test]
        public void EditorBuildSettingsListsTheWholeRunInPlayOrder()
        {
            var settings = ReadText("ProjectSettings/EditorBuildSettings.asset");
            var expected = new[]
            {
                TitleScenePath,
                GuidedScenePath,
                Room02ScenePath,
                Room03ScenePath,
                ResultScenePath
            };

            var lastIndex = -1;
            foreach (var scenePath in expected)
            {
                var index = settings.IndexOf(scenePath, StringComparison.Ordinal);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"EditorBuildSettings must list '{scenePath}'.");
                Assert.That(index, Is.GreaterThan(lastIndex), $"'{scenePath}' must appear after earlier run scenes.");
                lastIndex = index;
            }
        }

        private static void AssertScenePointsAt(string scenePath, string expectedFragment)
        {
            Assert.That(
                ReadText(scenePath),
                Does.Contain(expectedFragment),
                $"'{scenePath}' must serialize '{expectedFragment}'.");
        }

        private static string ReadText(string relativePath)
        {
            var fullPath = Path.GetFullPath(relativePath);
            Assert.That(File.Exists(fullPath), Is.True, $"'{relativePath}' must exist.");
            return File.ReadAllText(fullPath);
        }

        private static string ExtractBuildVersion(string html, string fileName)
        {
            var prefix = fileName + "?v=";
            var start = html.IndexOf(prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"'{fileName}' must use a build-versioned URL.");

            start += prefix.Length;
            var end = html.IndexOf('"', start);
            Assert.That(end, Is.GreaterThan(start), $"'{fileName}' must include a non-empty build version.");
            return html.Substring(start, end - start);
        }
    }
}
