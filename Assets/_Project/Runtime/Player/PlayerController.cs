using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float MinimumDirectionSqrMagnitude = 0.000001f;
        private const float WorldMinimumX = -8f;
        private const float WorldMaximumX = 8f;
        private const float WorldMinimumY = -4.5f;
        private const float WorldMaximumY = 4.5f;

        [SerializeField] private PlayerConfig config;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private DashAbility dashAbility;

        private Vector2 lastMoveDirection = Vector2.right;
        private Collider2D playerCollider;
        private bool movementEnabled = true;

        public bool IsMovementEnabled => movementEnabled;
        public Vector2 LastMoveDirection => lastMoveDirection;

        private void Awake()
        {
            playerCollider = playerTransform == null ? null : playerTransform.GetComponent<Collider2D>();
            ValidateConfiguration();
        }

        private void OnEnable()
        {
            inputRouter.DashRequested += HandleDashRequested;
        }

        private void OnDisable()
        {
            inputRouter.DashRequested -= HandleDashRequested;
        }

        private void Update()
        {
            if (!movementEnabled || dashAbility.IsDashing)
            {
                return;
            }

            var movement = inputRouter.Movement;
            ValidateFinite(movement, nameof(movement));

            if (movement.sqrMagnitude < MinimumDirectionSqrMagnitude)
            {
                return;
            }

            var direction = movement.normalized;
            lastMoveDirection = direction;
            var nextPosition = playerTransform.position + (Vector3)(direction * (config.MovementSpeed * Time.deltaTime));
            playerTransform.position = ClampToWorld(nextPosition, playerCollider.bounds.extents);
        }

        public void SetMovementEnabled(bool value)
        {
            movementEnabled = value;
        }

        public void ResetController()
        {
            lastMoveDirection = Vector2.right;
        }

        private void HandleDashRequested()
        {
            if (!movementEnabled)
            {
                return;
            }

            var dashDirection = inputRouter.Movement;
            ValidateFinite(dashDirection, nameof(dashDirection));

            if (dashDirection.sqrMagnitude >= MinimumDirectionSqrMagnitude)
            {
                lastMoveDirection = dashDirection.normalized;
            }

            dashAbility.TryStart(lastMoveDirection);
        }

        private void ValidateConfiguration()
        {
            if (config == null)
            {
                throw new InvalidOperationException("PlayerController requires a PlayerConfig reference.");
            }

            if (playerTransform == null)
            {
                throw new InvalidOperationException("PlayerController requires a player transform reference.");
            }

            if (playerCollider == null)
            {
                throw new InvalidOperationException("PlayerController requires a Collider2D on the player transform.");
            }

            if (inputRouter == null)
            {
                throw new InvalidOperationException("PlayerController requires a PlayerInputRouter reference.");
            }

            if (dashAbility == null)
            {
                throw new InvalidOperationException("PlayerController requires a DashAbility reference.");
            }

            config.ValidateConfiguration();
        }

        internal static Vector3 ClampToWorld(Vector3 position, Vector2 extents)
        {
            if (!IsFinite(extents.x) ||
                !IsFinite(extents.y) ||
                extents.x < 0f ||
                extents.y < 0f ||
                extents.x > (WorldMaximumX - WorldMinimumX) * 0.5f ||
                extents.y > (WorldMaximumY - WorldMinimumY) * 0.5f)
            {
                throw new ArgumentOutOfRangeException(nameof(extents), extents, "Player extents must be finite and fit within the world bounds.");
            }

            return new Vector3(
                Mathf.Clamp(position.x, WorldMinimumX + extents.x, WorldMaximumX - extents.x),
                Mathf.Clamp(position.y, WorldMinimumY + extents.y, WorldMaximumY - extents.y),
                position.z);
        }
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
        private static void ValidateFinite(Vector2 value, string parameterName)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Movement input must be finite.");
            }
        }
    }
}
