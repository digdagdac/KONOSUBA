using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum M1RoomActor
    {
        Player,
        Dasher,
        ArcherA,
        ArcherB,
        MinionA,
        MinionB
    }

    [Serializable]
    public struct M1RoomSpawn
    {
        [SerializeField] private M1RoomActor actor;
        [SerializeField] private Vector2 position;
        [SerializeField] private Vector2 facing;
        [SerializeField] private bool hasFacing;

        public M1RoomSpawn(M1RoomActor actor, Vector2 position, Vector2 facing, bool hasFacing)
        {
            this.actor = actor;
            this.position = position;
            this.facing = facing;
            this.hasFacing = hasFacing;
        }

        public M1RoomActor Actor => actor;
        public Vector2 Position => position;
        public Vector2 Facing => facing;
        public bool HasFacing => hasFacing;
    }

    [CreateAssetMenu(fileName = "Room_M1_GuidedValidation", menuName = "Overbless/Room/M1 Definition")]
    public sealed class M1RoomDefinition : ScriptableObject
    {
        public const int RequiredSeed = 104729;
        public const float RequiredFixedTimeStep = 0.02f;
        public const int RequiredSoulCount = 3;

        private static readonly Rect RequiredBounds = new Rect(-8f, -4.5f, 16f, 9f);
        private static readonly M1RoomSpawn[] RequiredSpawns =
        {
            new M1RoomSpawn(M1RoomActor.Player, new Vector2(0f, -2.5f), Vector2.up, true),
            new M1RoomSpawn(M1RoomActor.Dasher, new Vector2(0f, 3f), Vector2.down, true),
            new M1RoomSpawn(M1RoomActor.ArcherA, new Vector2(0f, -0.5f), Vector2.down, true),
            new M1RoomSpawn(M1RoomActor.ArcherB, new Vector2(-4f, 1.5f), Vector2.right, true),
            new M1RoomSpawn(M1RoomActor.MinionA, new Vector2(4f, 1.5f), Vector2.zero, false),
            new M1RoomSpawn(M1RoomActor.MinionB, new Vector2(4f, -1.5f), Vector2.zero, false)
        };

        private static M1RoomSpawn[] CreateDefaultSpawns()
        {
            return (M1RoomSpawn[])RequiredSpawns.Clone();
        }

        [SerializeField] private int seed = RequiredSeed;
        [SerializeField] private float fixedTimeStep = RequiredFixedTimeStep;
        [SerializeField] private Rect bounds = RequiredBounds;
        [SerializeField] private int soulsRequiredForExit = RequiredSoulCount;
        [SerializeField] private float dasherWarningTriggerRange = 8f;
        [SerializeField] private AttackPhase dasherInitialPhase = AttackPhase.Idle;
        [SerializeField] private AttackPhase archerAInitialPhase = AttackPhase.Idle;
        [SerializeField] private AttackPhase archerBInitialPhase = AttackPhase.Idle;
        [SerializeField] private M1RoomActor firstDasherTarget = M1RoomActor.Player;
        [SerializeField] private M1RoomSpawn[] spawns = CreateDefaultSpawns();

        [NonSerialized] private M1RoomSpawn[] readOnlySpawnsSource;
        [NonSerialized] private IReadOnlyList<M1RoomSpawn> readOnlySpawns;

        public int Seed => seed;
        public float FixedTimeStep => fixedTimeStep;
        public Rect Bounds => bounds;
        public int SoulsRequiredForExit => soulsRequiredForExit;
        public float DasherWarningTriggerRange => dasherWarningTriggerRange;
        public AttackPhase DasherInitialPhase => dasherInitialPhase;
        public AttackPhase ArcherAInitialPhase => archerAInitialPhase;
        public AttackPhase ArcherBInitialPhase => archerBInitialPhase;
        public M1RoomActor FirstDasherTarget => firstDasherTarget;
        public IReadOnlyList<M1RoomSpawn> Spawns
        {
            get
            {
                if (spawns == null)
                {
                    throw new InvalidOperationException($"M1 room requires exactly {RequiredSpawns.Length} actor spawns.");
                }

                if (readOnlySpawns == null || !ReferenceEquals(readOnlySpawnsSource, spawns))
                {
                    readOnlySpawns = Array.AsReadOnly(spawns);
                    readOnlySpawnsSource = spawns;
                }

                return readOnlySpawns;
            }
        }

        public M1RoomSpawn GetSpawn(M1RoomActor actor)
        {
            Validate();

            for (var index = 0; index < spawns.Length; index++)
            {
                if (spawns[index].Actor == actor)
                {
                    return spawns[index];
                }
            }

            throw new InvalidOperationException($"M1 room is missing the {actor} spawn.");
        }

        public void Validate()
        {
            if (seed != RequiredSeed)
            {
                throw new InvalidOperationException($"M1 room seed must be {RequiredSeed}.");
            }

            if (fixedTimeStep != RequiredFixedTimeStep)
            {
                throw new InvalidOperationException($"M1 fixed timestep must be {RequiredFixedTimeStep}.");
            }

            if (bounds != RequiredBounds)
            {
                throw new InvalidOperationException("M1 room bounds must be x[-8,8], y[-4.5,4.5].");
            }

            if (soulsRequiredForExit != RequiredSoulCount)
            {
                throw new InvalidOperationException($"M1 exit must require exactly {RequiredSoulCount} souls.");
            }

            if (dasherWarningTriggerRange != 8f)
            {
                throw new InvalidOperationException("M1 Dasher warning trigger range must be 8.");
            }

            if (dasherInitialPhase != AttackPhase.Idle || archerAInitialPhase != AttackPhase.Idle || archerBInitialPhase != AttackPhase.Idle)
            {
                throw new InvalidOperationException("M1 Dasher and Archers must start idle.");
            }

            if (firstDasherTarget != M1RoomActor.Player)
            {
                throw new InvalidOperationException("M1 Dasher must target the player first.");
            }

            if (spawns == null || spawns.Length != RequiredSpawns.Length)
            {
                throw new InvalidOperationException($"M1 room requires exactly {RequiredSpawns.Length} actor spawns.");
            }

            var encounteredActors = new HashSet<M1RoomActor>();
            for (var index = 0; index < spawns.Length; index++)
            {
                var spawn = spawns[index];
                if (!encounteredActors.Add(spawn.Actor))
                {
                    throw new InvalidOperationException($"M1 room contains a duplicate {spawn.Actor} spawn.");
                }

                ValidateExpectedSpawn(spawn);
            }
        }

        private static void ValidateExpectedSpawn(M1RoomSpawn spawn)
        {
            for (var index = 0; index < RequiredSpawns.Length; index++)
            {
                var expected = RequiredSpawns[index];
                if (spawn.Actor != expected.Actor)
                {
                    continue;
                }

                ValidateSpawn(spawn, expected);
                return;
            }

            throw new InvalidOperationException($"M1 room contains an unsupported actor {spawn.Actor}.");
        }

        private static void ValidateSpawn(M1RoomSpawn actual, M1RoomSpawn expected)
        {
            if (actual.Position != expected.Position || actual.Facing != expected.Facing || actual.HasFacing != expected.HasFacing)
            {
                throw new InvalidOperationException($"M1 {actual.Actor} spawn does not match the approved guided-room layout.");
            }
        }
    }
}
