using System;
using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// Authoritative layout packs for the current submitted room set.
    /// Spawn coordinates and HUD objective copy live here so bootstrap, validation, and
    /// new packs do not hardcode the same values in separate switch blocks.
    /// </summary>
    public static class M1RoomPackCatalog
    {
        public readonly struct Pack
        {
            public Pack(
                M1RoomVariant variant,
                string roomLabel,
                string objectiveTitle,
                string objectiveDetail,
                M1RoomSpawn[] spawns)
            {
                if (string.IsNullOrWhiteSpace(roomLabel))
                {
                    throw new ArgumentException("A room pack requires a room label.", nameof(roomLabel));
                }

                if (string.IsNullOrWhiteSpace(objectiveTitle))
                {
                    throw new ArgumentException("A room pack requires an objective title.", nameof(objectiveTitle));
                }

                if (string.IsNullOrWhiteSpace(objectiveDetail))
                {
                    throw new ArgumentException("A room pack requires objective detail.", nameof(objectiveDetail));
                }

                if (spawns == null || spawns.Length == 0)
                {
                    throw new ArgumentException("A room pack requires spawn entries.", nameof(spawns));
                }

                Variant = variant;
                RoomLabel = roomLabel;
                ObjectiveTitle = objectiveTitle;
                ObjectiveDetail = objectiveDetail;
                Spawns = spawns;
            }

            public M1RoomVariant Variant { get; }
            public string RoomLabel { get; }
            public string ObjectiveTitle { get; }
            public string ObjectiveDetail { get; }
            public M1RoomSpawn[] Spawns { get; }
        }

        private static readonly Pack Guided = new Pack(
            M1RoomVariant.M1GuidedValidation,
            "ROOM  01",
            "MAKE THEIR ATTACKS HIT EACH OTHER",
            "HASTE OR GIANT  ·  COLLECT 3 SOULS  ·  REACH THE EXIT",
            new[]
            {
                new M1RoomSpawn(M1RoomActor.Player, new Vector2(0f, -2.5f), Vector2.up, true),
                new M1RoomSpawn(M1RoomActor.Dasher, new Vector2(0f, 3f), Vector2.down, true),
                new M1RoomSpawn(M1RoomActor.ArcherA, new Vector2(0f, -0.5f), Vector2.down, true),
                new M1RoomSpawn(M1RoomActor.ArcherB, new Vector2(-4f, 1.5f), Vector2.right, true),
                new M1RoomSpawn(M1RoomActor.MinionA, new Vector2(4f, 1.5f), Vector2.zero, false),
                new M1RoomSpawn(M1RoomActor.MinionB, new Vector2(4f, -1.5f), Vector2.zero, false)
            });

        private static readonly Pack Room02 = new Pack(
            M1RoomVariant.Room02,
            "ROOM  02",
            "ECHO REPLAYS THE LOCKED ATTACK",
            "BLESS WITH ECHO  ·  USE THE REPLAY  ·  3 SOULS THEN EXIT",
            new[]
            {
                new M1RoomSpawn(M1RoomActor.Player, new Vector2(-6.4f, -2f), Vector2.right, true),
                new M1RoomSpawn(M1RoomActor.ArcherA, new Vector2(-1.2f, -2f), Vector2.left, true),
                new M1RoomSpawn(M1RoomActor.MinionA, new Vector2(-3.4f, -2f), Vector2.zero, false),
                new M1RoomSpawn(M1RoomActor.Dasher, new Vector2(4.2f, -1.4f), Vector2.left, true),
                new M1RoomSpawn(M1RoomActor.ArcherB, new Vector2(5.8f, 2.5f), Vector2.left, true),
                new M1RoomSpawn(M1RoomActor.MinionB, new Vector2(3.5f, 1.2f), Vector2.zero, false)
            });

        private static readonly Pack Room03 = new Pack(
            M1RoomVariant.Room03,
            "ROOM  03",
            "THE PILLAR SPLITS THE PATH",
            "ROUTE AROUND THE PILLAR  ·  ECHO + HASTE/GIANT  ·  3 SOULS",
            new[]
            {
                new M1RoomSpawn(M1RoomActor.Player, new Vector2(-6.4f, -1.8f), Vector2.right, true),
                new M1RoomSpawn(M1RoomActor.ArcherA, new Vector2(-1.2f, -1.8f), Vector2.left, true),
                new M1RoomSpawn(M1RoomActor.Dasher, new Vector2(4.2f, -1.5f), Vector2.left, true),
                new M1RoomSpawn(M1RoomActor.ArcherB, new Vector2(5.8f, 2.4f), Vector2.left, true),
                new M1RoomSpawn(M1RoomActor.MinionA, new Vector2(3.4f, 1.1f), Vector2.zero, false),
                new M1RoomSpawn(M1RoomActor.MinionB, new Vector2(5.4f, -0.1f), Vector2.zero, false)
            });

        /// <summary>Returns the layout and HUD copy pack for an approved room variant.</summary>
        public static Pack GetPack(M1RoomVariant variant)
        {
            switch (variant)
            {
                case M1RoomVariant.M1GuidedValidation:
                    return Guided;
                case M1RoomVariant.Room02:
                    return Room02;
                case M1RoomVariant.Room03:
                    return Room03;
                default:
                    throw new InvalidOperationException($"Unsupported room pack variant {variant}.");
            }
        }

        /// <summary>Returns a clone of the approved spawn table for a variant.</summary>
        public static M1RoomSpawn[] GetSpawnTemplate(M1RoomVariant variant)
        {
            var pack = GetPack(variant);
            var clone = new M1RoomSpawn[pack.Spawns.Length];
            Array.Copy(pack.Spawns, clone, pack.Spawns.Length);
            return clone;
        }
    }
}
