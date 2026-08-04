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
    public enum M1RoomVariant
    {
        M1GuidedValidation,
        Room02,
        Room03
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

        private static M1RoomSpawn[] CreateDefaultSpawns()
        {
            return M1RoomPackCatalog.GetSpawnTemplate(M1RoomVariant.M1GuidedValidation);
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
        [SerializeField] private M1RoomVariant roomVariant = M1RoomVariant.M1GuidedValidation;

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
        public M1RoomVariant RoomVariant => roomVariant;
        public string RoomLabel => M1RoomPackCatalog.GetPack(roomVariant).RoomLabel;
        public string ObjectiveTitle => M1RoomPackCatalog.GetPack(roomVariant).ObjectiveTitle;
        public string ObjectiveDetail => M1RoomPackCatalog.GetPack(roomVariant).ObjectiveDetail;
        public IReadOnlyList<M1RoomSpawn> Spawns
        {
            get
            {
                if (spawns == null)
                {
                    throw new InvalidOperationException($"{roomVariant} room requires exactly {GetRequiredSpawns().Length} actor spawns.");
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

            throw new InvalidOperationException($"{roomVariant} room is missing the {actor} spawn.");
        }

        public void Validate()
        {
            var requiredSpawns = GetRequiredSpawns();

            if (seed != RequiredSeed)
            {
                throw new InvalidOperationException($"{roomVariant} room seed must be {RequiredSeed}.");
            }

            if (fixedTimeStep != RequiredFixedTimeStep)
            {
                throw new InvalidOperationException($"{roomVariant} fixed timestep must be {RequiredFixedTimeStep}.");
            }

            if (bounds != RequiredBounds)
            {
                throw new InvalidOperationException($"{roomVariant} room bounds must be x[-8,8], y[-4.5,4.5].");
            }

            if (soulsRequiredForExit != RequiredSoulCount)
            {
                throw new InvalidOperationException($"{roomVariant} exit must require exactly {RequiredSoulCount} souls.");
            }

            if (dasherWarningTriggerRange != 8f)
            {
                throw new InvalidOperationException($"{roomVariant} Dasher warning trigger range must be 8.");
            }

            if (dasherInitialPhase != AttackPhase.Idle || archerAInitialPhase != AttackPhase.Idle || archerBInitialPhase != AttackPhase.Idle)
            {
                throw new InvalidOperationException($"{roomVariant} Dasher and Archers must start idle.");
            }

            if (firstDasherTarget != M1RoomActor.Player)
            {
                throw new InvalidOperationException($"{roomVariant} Dasher must target the player first.");
            }

            if (spawns == null || spawns.Length != requiredSpawns.Length)
            {
                throw new InvalidOperationException($"{roomVariant} room requires exactly {requiredSpawns.Length} actor spawns.");
            }

            var encounteredActors = new HashSet<M1RoomActor>();
            for (var index = 0; index < spawns.Length; index++)
            {
                var spawn = spawns[index];
                if (!encounteredActors.Add(spawn.Actor))
                {
                    throw new InvalidOperationException($"{roomVariant} room contains a duplicate {spawn.Actor} spawn.");
                }

                ValidateExpectedSpawn(spawn, requiredSpawns);
            }
        }

        private M1RoomSpawn[] GetRequiredSpawns()
        {
            return M1RoomPackCatalog.GetPack(roomVariant).Spawns;
        }

        private void ValidateExpectedSpawn(M1RoomSpawn spawn, M1RoomSpawn[] requiredSpawns)
        {
            for (var index = 0; index < requiredSpawns.Length; index++)
            {
                var expected = requiredSpawns[index];
                if (spawn.Actor != expected.Actor)
                {
                    continue;
                }

                ValidateSpawn(spawn, expected);
                return;
            }

            throw new InvalidOperationException($"{roomVariant} room contains an unsupported actor {spawn.Actor}.");
        }

        private void ValidateSpawn(M1RoomSpawn actual, M1RoomSpawn expected)
        {
            if (actual.Position != expected.Position || actual.Facing != expected.Facing || actual.HasFacing != expected.HasFacing)
            {
                throw new InvalidOperationException($"{roomVariant} {actual.Actor} spawn does not match the approved room layout.");
            }
        }
    }
}
