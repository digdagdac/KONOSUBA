using System;
using Overbless.Runtime;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Legacy v003-sheet support retained for manual recovery only. v004 now owns the monster
    /// animation sets as individual normalized frame sprites, so this type must not rebuild the
    /// sets automatically when an editor domain reloads.
    /// </summary>
    [InitializeOnLoad]
    internal static class M1CharacterMotionV003ImportBootstrap
    {
        private const string AnimationRoot = "Assets/_Project/Art/M1Production/Characters/Animation/MotionsV003";
        private const string DataRoot = "Assets/_Project/Data/Animations";

        private static readonly RoleSpec[] Roles =
        {
            new RoleSpec("player", new[] { "idle", "move", "dash", "bless_cast", "hit", "death" }),
            new RoleSpec("dasher", new[] { "idle", "walk", "run", "attack_charge", "attack_execute", "recover", "hit", "death" }),
            new RoleSpec("archer", new[] { "idle", "walk", "run", "attack_charge", "attack_execute", "recover", "hit", "death" }),
            new RoleSpec("minion", new[] { "idle", "walk", "run", "attack_charge", "attack_execute", "recover", "hit", "death" })
        };

        static M1CharacterMotionV003ImportBootstrap()
        {
            // Intentionally no automatic refresh. Use the explicit curation menu only when
            // restoring legacy v003 assets for an archival investigation.
        }

        private static void RefreshAnimationSetsWhenReady()
        {
            if (EditorApplication.isCompiling || !AllMotionSheetsAreImported() || !RequiresRefresh())
            {
                return;
            }

            MonsterMotionCurationBootstrap.RefreshCuratedMonsterMotion();
            Debug.Log("Overbless: refreshed M1 directional animation sets for v003 motion sheets.");
        }

        private static bool AllMotionSheetsAreImported()
        {
            for (var roleIndex = 0; roleIndex < Roles.Length; roleIndex++)
            {
                var role = Roles[roleIndex];
                for (var stateIndex = 0; stateIndex < role.States.Length; stateIndex++)
                {
                    var path = MotionPath(role.Role, role.States[stateIndex]);
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool RequiresRefresh()
        {
            for (var roleIndex = 0; roleIndex < Roles.Length; roleIndex++)
            {
                var role = Roles[roleIndex];
                var animationSet = AssetDatabase.LoadAssetAtPath<DirectionalAnimationSet>(
                    $"{DataRoot}/{char.ToUpperInvariant(role.Role[0])}{role.Role.Substring(1)}DirectionalAnimationSet.asset");
                if (animationSet == null || !animationSet.Supports(CharacterAnimationState.Idle, CharacterDirection.South))
                {
                    return true;
                }

                var idle = animationSet.GetClip(CharacterAnimationState.Idle, CharacterDirection.South);
                if (!idle.GetFrame(0).name.EndsWith("_v003", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string MotionPath(string role, string state)
        {
            return $"{AnimationRoot}/chr_{role}_{state}_motion_v003.png";
        }

        private readonly struct RoleSpec
        {
            public RoleSpec(string role, string[] states)
            {
                Role = role;
                States = states;
            }

            public string Role { get; }
            public string[] States { get; }
        }
    }
}
