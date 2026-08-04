using System;
using System.IO;
using NUnit.Framework;
using Overbless.Editor.Bootstrap;
using Overbless.Runtime;
using UnityEditor;
using UnityEngine;

namespace Overbless.Tests.EditMode
{
    /// <summary>
    /// Locks the shipped cast to the approved character direction. The approval file is
    /// the source of truth, so a name, age or archetype mapping cannot drift into the
    /// build without this failing, and the catalog cannot claim art that does not exist.
    /// </summary>
    public sealed class M2CharacterIdentityTests
    {
        private const string ApprovalPath = "Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json";
        private const string CelSourceDirectory = "Docs/AI_Usage/sources/m2_character_appeal_v002";
        private const string GuidedScenePath = "Assets/_Project/Scenes/M1_GuidedValidation.unity";

        private static readonly string[] M2ScenePaths =
        {
            "Assets/_Project/Scenes/Room_02.unity",
            "Assets/_Project/Scenes/Room_03.unity"
        };

        [Test]
        public void CatalogMatchesTheApprovedCharacterDirection()
        {
            var approval = ReadApproval();
            var catalog = LoadCatalog();
            catalog.Validate();

            Assert.That(catalog.Count, Is.EqualTo(4), "The shipped cast covers the player and the three active archetypes.");

            AssertApprovedMember(approval, catalog, CharacterRole.Player, "RIVELLA", "AGE 22", null);
            AssertApprovedMember(approval, catalog, CharacterRole.Dasher, "VERA", "AGE 24", "Enemy_Dasher");
            AssertApprovedMember(approval, catalog, CharacterRole.Archer, "LUME", "AGE 23", "Enemy_Archer");
            AssertApprovedMember(approval, catalog, CharacterRole.Minion, "MOKO", string.Empty, "Enemy_Minion");
        }

        [Test]
        public void CatalogOmitsTheGuardianBecauseGolemActivationStaysOutOfScope()
        {
            var approval = ReadApproval();
            Assert.That(
                approval.IndexOf("Atra", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "The approval still describes the guardian, so this exclusion has to stay deliberate.");

            var catalog = LoadCatalog();
            for (var index = 0; index < catalog.Count; index++)
            {
                Assert.That(
                    catalog.GetAt(index).DisplayName,
                    Is.Not.EqualTo("ATRA"),
                    "Shipping a guardian identity would activate an excluded actor.");
            }
        }

        [Test]
        public void CatalogDeclaresRepresentativeArtUntilCelSheetsAreDelivered()
        {
            var celSheetsExist = Directory.Exists(Path.GetFullPath(CelSourceDirectory));
            var catalog = LoadCatalog();

            for (var index = 0; index < catalog.Count; index++)
            {
                var identity = catalog.GetAt(index);
                Assert.That(
                    identity.HasCelPortraitSheet,
                    Is.EqualTo(celSheetsExist),
                    $"'{identity.DisplayName}' must claim a cel portrait sheet only while '{CelSourceDirectory}' holds the delivered sources.");

                foreach (CharacterExpression expression in Enum.GetValues(typeof(CharacterExpression)))
                {
                    Assert.That(
                        identity.GetPortrait(expression),
                        Is.Not.Null,
                        $"'{identity.DisplayName}' must resolve a portrait for {expression} even before cel art exists.");
                }
            }
        }

        [Test]
        public void CardFramingSeparatesEveryExpressionForTheSameCastMember()
        {
            var catalog = LoadCatalog();
            var identity = catalog.GetRequired(CharacterRole.Dasher);
            var expressions = (CharacterExpression[])Enum.GetValues(typeof(CharacterExpression));

            for (var first = 0; first < expressions.Length; first++)
            {
                for (var second = first + 1; second < expressions.Length; second++)
                {
                    var left = CharacterAppealPresenter.ComposeFrameColor(identity.MotifColor, expressions[first]);
                    var right = CharacterAppealPresenter.ComposeFrameColor(identity.MotifColor, expressions[second]);
                    Assert.That(
                        left,
                        Is.Not.EqualTo(right),
                        $"{expressions[first]} and {expressions[second]} must not render the same frame while one sprite stands in for every expression.");
                }
            }

            var human = catalog.GetRequired(CharacterRole.Archer);
            Assert.That(CharacterAppealPresenter.ComposeRoleLine(human), Does.Contain(human.AgeLine));
            Assert.That(CharacterAppealPresenter.ComposeRoleLine(human), Does.Contain(human.RoleLine));

            var nonHuman = catalog.GetRequired(CharacterRole.Minion);
            Assert.That(
                CharacterAppealPresenter.ComposeRoleLine(nonHuman),
                Is.EqualTo(nonHuman.RoleLine),
                "A non-human cast member must not print an empty age separator.");
        }

        [Test]
        public void CatalogRejectsACastThatCouldConfuseATester()
        {
            var catalog = ScriptableObject.CreateInstance<CharacterIdentityCatalog>();
            try
            {
                Assert.Catch<InvalidOperationException>(
                    () => catalog.Validate(),
                    "An empty catalog must not validate.");

                var serialized = new SerializedObject(catalog);
                var identities = serialized.FindProperty("identities");
                identities.arraySize = 4;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.Catch<InvalidOperationException>(
                    () => catalog.Validate(),
                    "Four blank identities must not validate.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void OnlyTheMilestoneTwoScenesCarryTheCharacterCard()
        {
            foreach (var scenePath in M2ScenePaths)
            {
                var scene = ReadText(scenePath);
                Assert.That(
                    scene,
                    Does.Contain("CharacterAppeal"),
                    $"'{scenePath}' must carry the character card holder.");
            }

            var guided = ReadText(GuidedScenePath);
            Assert.That(
                guided,
                Does.Not.Contain("CharacterAppeal"),
                "The guided scene must physically exclude the character card, the same way it excludes Echo.");
        }

        private static void AssertApprovedMember(
            string approval,
            CharacterIdentityCatalog catalog,
            CharacterRole role,
            string expectedName,
            string expectedAgeLine,
            string expectedDefinitionAsset)
        {
            var identity = catalog.GetRequired(role);
            Assert.That(identity.DisplayName, Is.EqualTo(expectedName));
            Assert.That(identity.AgeLine, Is.EqualTo(expectedAgeLine));
            Assert.That(
                approval.IndexOf(ToApprovalName(expectedName), StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                $"'{expectedName}' must appear in the approved character direction.");

            if (!string.IsNullOrEmpty(expectedAgeLine))
            {
                var age = expectedAgeLine.Substring("AGE ".Length);
                Assert.That(
                    approval.IndexOf("age " + age, StringComparison.Ordinal),
                    Is.GreaterThanOrEqualTo(0),
                    $"The approved age for '{expectedName}' must match the catalog.");
            }

            if (expectedDefinitionAsset == null)
            {
                Assert.That(identity.Definition, Is.Null);
            }
            else
            {
                Assert.That(identity.Definition, Is.Not.Null);
                Assert.That(identity.Definition.name, Is.EqualTo(expectedDefinitionAsset));
            }

            Assert.That(identity.HabitLine, Is.Not.Empty);
            Assert.That(identity.HabitLine, Is.EqualTo(identity.HabitLine.ToUpperInvariant()));
        }

        private static string ToApprovalName(string displayName)
        {
            return displayName.Substring(0, 1) + displayName.Substring(1).ToLowerInvariant();
        }

        private static CharacterIdentityCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterIdentityCatalog>(
                M2CharacterIdentityBootstrap.CatalogPath);
            Assert.That(
                catalog,
                Is.Not.Null,
                $"'{M2CharacterIdentityBootstrap.CatalogPath}' must exist. Rebuild it with M2CharacterIdentityBootstrap.");
            return catalog;
        }

        private static string ReadApproval()
        {
            return ReadText(ApprovalPath);
        }

        private static string ReadText(string relativePath)
        {
            var fullPath = Path.GetFullPath(relativePath);
            Assert.That(File.Exists(fullPath), Is.True, $"'{relativePath}' must exist.");
            return File.ReadAllText(fullPath);
        }
    }
}
