using System;
using NUnit.Framework;
using Overbless.Runtime;
using UnityEngine;

namespace Overbless.Tests.EditMode
{
    /// <summary>
    /// Locks the room-pack catalog so layout and HUD copy stay authorable from one source.
    /// </summary>
    public sealed class RoomPackCatalogTests
    {
        [Test]
        public void EveryApprovedVariantHasARoomPackWithSixDistinctSpawns()
        {
            foreach (M1RoomVariant variant in Enum.GetValues(typeof(M1RoomVariant)))
            {
                var pack = M1RoomPackCatalog.GetPack(variant);
                Assert.That(pack.Variant, Is.EqualTo(variant));
                Assert.That(pack.RoomLabel, Does.StartWith("ROOM"));
                Assert.That(string.IsNullOrWhiteSpace(pack.ObjectiveTitle), Is.False);
                Assert.That(string.IsNullOrWhiteSpace(pack.ObjectiveDetail), Is.False);
                Assert.That(pack.Spawns.Length, Is.EqualTo(6), $"{variant} must keep the six-actor spawn table.");

                var seen = new System.Collections.Generic.HashSet<M1RoomActor>();
                for (var index = 0; index < pack.Spawns.Length; index++)
                {
                    Assert.That(seen.Add(pack.Spawns[index].Actor), Is.True, $"{variant} has a duplicate actor spawn.");
                }
            }
        }

        [Test]
        public void RoomObjectivesTeachThePublishedRules()
        {
            Assert.That(
                M1RoomPackCatalog.GetPack(M1RoomVariant.M1GuidedValidation).ObjectiveTitle,
                Is.EqualTo("MAKE THEIR ATTACKS HIT EACH OTHER"));
            Assert.That(
                M1RoomPackCatalog.GetPack(M1RoomVariant.Room02).ObjectiveTitle,
                Is.EqualTo("ECHO REPLAYS THE LOCKED ATTACK"));
            Assert.That(
                M1RoomPackCatalog.GetPack(M1RoomVariant.Room03).ObjectiveTitle,
                Is.EqualTo("THE PILLAR SPLITS THE PATH"));
        }

        [Test]
        public void SpawnTemplateMatchesPackEntriesByActor()
        {
            var template = M1RoomPackCatalog.GetSpawnTemplate(M1RoomVariant.Room02);
            var pack = M1RoomPackCatalog.GetPack(M1RoomVariant.Room02);
            Assert.That(template.Length, Is.EqualTo(pack.Spawns.Length));
            for (var index = 0; index < template.Length; index++)
            {
                Assert.That(template[index].Actor, Is.EqualTo(pack.Spawns[index].Actor));
                Assert.That(template[index].Position, Is.EqualTo(pack.Spawns[index].Position));
            }
        }
    }
}
