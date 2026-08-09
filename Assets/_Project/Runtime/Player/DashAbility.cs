using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class DashAbility : MonoBehaviour
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;

        [SerializeField] private PlayerConfig config;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private Health health;

        private readonly object invulnerabilitySource = new object();
        private Vector2 lockedDirection;
        private float dashDuration;
        private float dashSpeed;
        private float dashElapsed;
        private float cooldownRemaining;
        private float invulnerabilityRemaining;
        private bool isDashing;
        private bool temporaryInvulnerabilityActive;
        private Collider2D playerCollider;

        public Vector2 LockedDirection => lockedDirection;
        public bool IsDashing => isDashing;
        public bool IsCoolingDown => cooldownRemaining > 0f;
        public float CooldownRemaining => cooldownRemaining;
        public float CooldownDuration => config == null ? 0f : config.DashCooldown;
        public bool CanDash =>
            isActiveAndEnabled &&
            Time.timeScale > 0f &&
            !isDashing &&
            cooldownRemaining <= 0f &&
            !health.IsDead;

        private void Awake()
        {
            playerCollider = playerTransform == null ? null : playerTransform.GetComponent<Collider2D>();
            ValidateConfiguration();
        }

        private void OnDisable()
        {
            ResetAbility();
        }

        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            AdvanceCooldown(deltaTime);
            AdvanceInvulnerability(deltaTime);

            if (!isDashing)
            {
                return;
            }

            var remainingDuration = dashDuration - dashElapsed;
            var dashDeltaTime = Mathf.Min(deltaTime, remainingDuration);
            if (dashDeltaTime > 0f)
            {
                var desiredDelta = lockedDirection * (dashSpeed * dashDeltaTime);
                playerTransform.position = PlayerWorldMovement.ResolvePosition(
                    playerCollider,
                    playerTransform.position,
                    desiredDelta,
                    playerCollider.bounds.extents);
                dashElapsed += dashDeltaTime;
            }

            if (dashElapsed >= dashDuration)
            {
                isDashing = false;
            }
        }

        public bool TryStart(Vector2 direction)
        {
            ValidateConfiguration();

            if (!CanDash)
            {
                return false;
            }

            if (!IsFinite(direction.x) || !IsFinite(direction.y) || direction.sqrMagnitude < MinimumDirectionSqrMagnitude)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Dash direction must be finite and non-zero.");
            }

            lockedDirection = direction.normalized;
            dashDuration = config.DashDuration;
            dashSpeed = config.DashDistance / dashDuration;
            dashElapsed = 0f;
            cooldownRemaining = config.DashCooldown;
            invulnerabilityRemaining = config.DashInvulnerabilityDuration;
            health.SetInvulnerabilitySource(invulnerabilitySource, true);
            temporaryInvulnerabilityActive = true;
            isDashing = true;
            return true;
        }

        public void ResetAbility()
        {
            isDashing = false;
            dashElapsed = 0f;
            dashDuration = 0f;
            dashSpeed = 0f;
            cooldownRemaining = 0f;
            lockedDirection = Vector2.zero;
            EndTemporaryInvulnerability();
        }

        private void AdvanceCooldown(float deltaTime)
        {
            if (cooldownRemaining > 0f)
            {
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
            }
        }

        private void AdvanceInvulnerability(float deltaTime)
        {
            if (!temporaryInvulnerabilityActive)
            {
                return;
            }

            invulnerabilityRemaining -= deltaTime;
            if (invulnerabilityRemaining <= 0f)
            {
                EndTemporaryInvulnerability();
            }
        }

        private void EndTemporaryInvulnerability()
        {
            if (!temporaryInvulnerabilityActive)
            {
                return;
            }

            health.SetInvulnerabilitySource(invulnerabilitySource, false);
            temporaryInvulnerabilityActive = false;
            invulnerabilityRemaining = 0f;
        }

        private void ValidateConfiguration()
        {
            if (config == null)
            {
                throw new InvalidOperationException("DashAbility requires a PlayerConfig reference.");
            }

            if (playerTransform == null)
            {
                throw new InvalidOperationException("DashAbility requires a player transform reference.");
            }
            if (playerCollider == null)
            {
                throw new InvalidOperationException("DashAbility requires a Collider2D on the player transform.");
            }

            if (health == null)
            {
                throw new InvalidOperationException("DashAbility requires a Health reference.");
            }

            config.ValidateConfiguration();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
