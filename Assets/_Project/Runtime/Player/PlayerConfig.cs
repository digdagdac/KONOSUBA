using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [CreateAssetMenu(menuName = "Overbless/Player Config", fileName = "PlayerConfig")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [SerializeField, Min(0.001f)] private float movementSpeed = 5f;
        [SerializeField, Min(0.001f)] private float dashDistance = 3.5f;
        [SerializeField, Min(0.001f)] private float dashDuration = 0.18f;
        [SerializeField, Min(0.001f)] private float dashInvulnerabilityDuration = 0.22f;
        [SerializeField, Min(0.001f)] private float dashCooldown = 1.2f;

        public float MovementSpeed => movementSpeed;
        public float DashDistance => dashDistance;
        public float DashDuration => dashDuration;
        public float DashInvulnerabilityDuration => dashInvulnerabilityDuration;
        public float DashCooldown => dashCooldown;

        public void ValidateConfiguration()
        {
            ValidatePositiveFinite(movementSpeed, nameof(movementSpeed));
            ValidatePositiveFinite(dashDistance, nameof(dashDistance));
            ValidatePositiveFinite(dashDuration, nameof(dashDuration));
            ValidatePositiveFinite(dashInvulnerabilityDuration, nameof(dashInvulnerabilityDuration));
            ValidatePositiveFinite(dashCooldown, nameof(dashCooldown));
            ValidatePositiveFinite(dashDistance / dashDuration, $"{nameof(dashDistance)} / {nameof(dashDuration)}");
        }

        private static void ValidatePositiveFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new InvalidOperationException($"Player config field '{name}' must be finite and positive.");
            }
        }
    }
}
