using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum CharacterAnimationState
    {
        Idle,
        Move,
        Dash,
        BlessCast,
        AttackCharge,
        AttackExecute,
        Recover,
        BasicAttack,
        Hit,
        Death
    }

    public enum CharacterDirection
    {
        South = 0,
        North = 1,
        East = 2,
        West = 3,
        SouthEast = 4,
        SouthWest = 5,
        NorthEast = 6,
        NorthWest = 7,
    }

    public enum CharacterAnimationDriver
    {
        Player,
        MajorEnemy,
        Minion
    }

    [Serializable]
    public sealed class DirectionalAnimationClip
    {
        [SerializeField] private CharacterAnimationState state;
        [SerializeField] private CharacterDirection direction;
        [SerializeField, Min(0.01f)] private float framesPerSecond = 8f;
        [SerializeField] private bool loop;
        [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();

        public CharacterAnimationState State => state;
        public CharacterDirection Direction => direction;
        public float FramesPerSecond => framesPerSecond;
        public bool Loop => loop;
        public int FrameCount => frames == null ? 0 : frames.Length;

        public Sprite GetFrame(int index)
        {
            Validate();
            if (index < 0 || index >= frames.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return frames[index];
        }

        public void Validate()
        {
            if (float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) || framesPerSecond <= 0f)
            {
                throw new InvalidOperationException($"{state}/{direction} requires a positive finite frame rate.");
            }

            if (frames == null || frames.Length == 0)
            {
                throw new InvalidOperationException($"{state}/{direction} requires at least one frame.");
            }

            for (var index = 0; index < frames.Length; index++)
            {
                if (frames[index] == null)
                {
                    throw new InvalidOperationException($"{state}/{direction} frame {index} is missing.");
                }
            }
        }
    }

    [CreateAssetMenu(fileName = "DirectionalAnimationSet", menuName = "Overbless/Presentation/Directional Animation Set")]
    public sealed class DirectionalAnimationSet : ScriptableObject
    {
        [SerializeField] private string role = string.Empty;
        [SerializeField] private DirectionalAnimationClip[] clips = Array.Empty<DirectionalAnimationClip>();

        public string Role => role;
        public int ClipCount => clips == null ? 0 : clips.Length;

        public DirectionalAnimationClip GetClip(CharacterAnimationState state, CharacterDirection direction)
        {
            if (clips == null)
            {
                throw new InvalidOperationException("Directional animation clips are unavailable.");
            }

            for (var index = 0; index < clips.Length; index++)
            {
                var clip = clips[index];
                if (clip != null && clip.State == state && clip.Direction == direction)
                {
                    clip.Validate();
                    return clip;
                }
            }

            throw new InvalidOperationException($"Animation set '{role}' is missing {state}/{direction}.");
        }

        public bool Supports(CharacterAnimationState state, CharacterDirection direction)
        {
            if (clips == null)
            {
                return false;
            }

            for (var index = 0; index < clips.Length; index++)
            {
                var clip = clips[index];
                if (clip != null && clip.State == state && clip.Direction == direction)
                {
                    return true;
                }
            }

            return false;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new InvalidOperationException("Directional animation set requires a role.");
            }

            if (clips == null || clips.Length == 0)
            {
                throw new InvalidOperationException($"Directional animation set '{role}' requires clips.");
            }

            var keys = new HashSet<int>();
            for (var index = 0; index < clips.Length; index++)
            {
                var clip = clips[index];
                if (clip == null)
                {
                    throw new InvalidOperationException($"Directional animation set '{role}' contains a null clip.");
                }

                clip.Validate();
                var key = ((int)clip.State << 8) | (int)clip.Direction;
                if (!keys.Add(key))
                {
                    throw new InvalidOperationException(
                        $"Directional animation set '{role}' contains duplicate {clip.State}/{clip.Direction} clips.");
                }
            }
        }
    }
}
