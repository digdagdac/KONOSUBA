using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class DirectionalSpriteAnimator : MonoBehaviour
    {
        private const float MovementThresholdSquared = 0.0000001f;
        private const float HitDisplayDuration = 0.18f;
        private const float DiagonalBoundaryTangent = 0.41421356237f;

        // Damage and death previously changed only the sprite state. A brief tint
        // and a death fade add a second, redundant channel so a hit stays readable
        // when the silhouette is small or overlapped.
        private const float DeathFadeDuration = 0.45f;
        private const float DeathFadeMinimumAlpha = 0.3f;
        private static readonly Color NeutralTint = Color.white;
        private static readonly Color HitTint = new Color32(255, 140, 140, 255);

        [SerializeField] private CharacterAnimationDriver driver;
        [SerializeField] private CharacterDirection initialDirection = CharacterDirection.South;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private DirectionalAnimationSet animationSet;
        [SerializeField] private Health health;
        [SerializeField] private DashAbility dashAbility;
        [SerializeField] private BlessingTargeting blessingTargeting;
        [SerializeField] private EnemyBase enemy;

        private DirectionalAnimationClip currentClip;
        private CharacterAnimationState currentState;
        private CharacterDirection currentDirection;
        private Vector3 previousPosition;
        private float frameElapsed;
        private float hitRemaining;
        private float deathElapsed;
        private int frameIndex;
        private bool initialized;
        private bool subscribed;

        public CharacterAnimationState CurrentState => currentState;
        public CharacterDirection CurrentDirection => currentDirection;
        public int CurrentFrameIndex => frameIndex;
        public Sprite CurrentSprite => spriteRenderer == null ? null : spriteRenderer.sprite;
        public DirectionalAnimationSet AnimationSet => animationSet;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            Initialize();
            var movement = transform.position - previousPosition;
            previousPosition = transform.position;
            if (movement.sqrMagnitude > MovementThresholdSquared)
            {
                currentDirection = DirectionFromVector(movement);
            }

            if (hitRemaining > 0f)
            {
                hitRemaining = Mathf.Max(0f, hitRemaining - Time.deltaTime);
            }

            deathElapsed = health.IsDead ? deathElapsed + Time.deltaTime : 0f;

            var desiredState = ResolveState(movement.sqrMagnitude > MovementThresholdSquared);
            if (desiredState != currentState || currentClip == null || currentClip.Direction != currentDirection)
            {
                BeginClip(desiredState, currentDirection);
            }
            else
            {
                AdvanceClip(Time.deltaTime);
            }

            ApplyFeedbackTint();
        }

        /// <summary>
        /// Applies the damage and death tint. This animator already owns the
        /// renderer's sprite, so it also owns its colour; no other system writes it.
        /// </summary>
        private void ApplyFeedbackTint()
        {
            if (health.IsDead)
            {
                var fade = DeathFadeDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(deathElapsed / DeathFadeDuration);
                spriteRenderer.color = new Color(
                    NeutralTint.r,
                    NeutralTint.g,
                    NeutralTint.b,
                    Mathf.Lerp(NeutralTint.a, DeathFadeMinimumAlpha, fade));
                return;
            }

            spriteRenderer.color = hitRemaining > 0f ? HitTint : NeutralTint;
        }

        public void SetInitialFacing(Vector2 facing)
        {
            if (float.IsNaN(facing.x) || float.IsInfinity(facing.x) ||
                float.IsNaN(facing.y) || float.IsInfinity(facing.y) ||
                facing.sqrMagnitude <= MovementThresholdSquared)
            {
                throw new ArgumentOutOfRangeException(nameof(facing), facing, "Facing must be finite and non-zero.");
            }

            initialDirection = DirectionFromVector(facing);
            currentDirection = initialDirection;
            if (initialized)
            {
                BeginClip(currentState, currentDirection);
            }
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            ValidateConfiguration();
            animationSet.Validate();
            currentDirection = initialDirection;
            previousPosition = transform.position;
            initialized = true;
            BeginClip(CharacterAnimationState.Idle, currentDirection);
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || health == null)
            {
                return;
            }

            health.Damaged -= HandleDamaged;
            health.Died -= HandleDied;
            subscribed = false;
        }

        private CharacterAnimationState ResolveState(bool moved)
        {
            if (health.IsDead)
            {
                return CharacterAnimationState.Death;
            }

            if (hitRemaining > 0f)
            {
                return CharacterAnimationState.Hit;
            }

            switch (driver)
            {
                case CharacterAnimationDriver.Player:
                    if (dashAbility.IsDashing)
                    {
                        return CharacterAnimationState.Dash;
                    }

                    if (blessingTargeting.IsSelecting)
                    {
                        return CharacterAnimationState.BlessCast;
                    }

                    return moved ? CharacterAnimationState.Move : CharacterAnimationState.Idle;
                case CharacterAnimationDriver.MajorEnemy:
                    return ResolveMajorEnemyState(moved);
                case CharacterAnimationDriver.Minion:
                    return ResolveMinionState(moved);
                default:
                    throw new ArgumentOutOfRangeException(nameof(driver), driver, "Unsupported animation driver.");
            }
        }

        private CharacterAnimationState ResolveMajorEnemyState(bool moved)
        {
            switch (enemy.CurrentAttackPhase)
            {
                case AttackPhase.Warning:
                case AttackPhase.Locked:
                    return CharacterAnimationState.AttackCharge;
                case AttackPhase.Executing:
                    return CharacterAnimationState.AttackExecute;
                case AttackPhase.Recovery:
                    return CharacterAnimationState.Recover;
                case AttackPhase.Idle:
                    return moved ? CharacterAnimationState.Move : CharacterAnimationState.Idle;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CharacterAnimationState ResolveMinionState(bool moved)
        {
            switch (enemy.CurrentAttackPhase)
            {
                case AttackPhase.Warning:
                case AttackPhase.Locked:
                case AttackPhase.Executing:
                    return CharacterAnimationState.BasicAttack;
                case AttackPhase.Idle:
                case AttackPhase.Recovery:
                    return moved ? CharacterAnimationState.Move : CharacterAnimationState.Idle;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void BeginClip(CharacterAnimationState state, CharacterDirection direction)
        {
            currentClip = animationSet.GetClip(state, direction);
            currentState = state;
            currentDirection = direction;
            frameIndex = 0;
            frameElapsed = 0f;
            ApplyFrame();
        }

        private void AdvanceClip(float deltaTime)
        {
            if (deltaTime <= 0f || currentClip.FrameCount <= 1)
            {
                return;
            }

            frameElapsed += deltaTime;
            var frameDuration = 1f / currentClip.FramesPerSecond;
            while (frameElapsed >= frameDuration)
            {
                frameElapsed -= frameDuration;
                if (frameIndex + 1 < currentClip.FrameCount)
                {
                    frameIndex++;
                }
                else if (currentClip.Loop)
                {
                    frameIndex = 0;
                }
                else
                {
                    frameIndex = currentClip.FrameCount - 1;
                    frameElapsed = 0f;
                    break;
                }

                ApplyFrame();
            }
        }

        private void ApplyFrame()
        {
            spriteRenderer.sprite = currentClip.GetFrame(frameIndex);
        }

        private void HandleDamaged(DamageEvent damageEvent)
        {
            if (!health.IsDead)
            {
                hitRemaining = HitDisplayDuration;
            }
        }

        private void HandleDied(DeathEvent deathEvent)
        {
            hitRemaining = 0f;
            deathElapsed = 0f;
        }

        private void ValidateConfiguration()
        {
            if (spriteRenderer == null || animationSet == null || health == null)
            {
                throw new InvalidOperationException("Directional sprite animator requires renderer, animation set and health.");
            }

            switch (driver)
            {
                case CharacterAnimationDriver.Player:
                    if (dashAbility == null || blessingTargeting == null || enemy != null)
                    {
                        throw new InvalidOperationException("Player animation requires DashAbility and BlessingTargeting only.");
                    }
                    break;
                case CharacterAnimationDriver.MajorEnemy:
                case CharacterAnimationDriver.Minion:
                    if (enemy == null || dashAbility != null || blessingTargeting != null)
                    {
                        throw new InvalidOperationException("Enemy animation requires an EnemyBase reference only.");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(driver), driver, "Unsupported animation driver.");
            }
        }

        private static CharacterDirection DirectionFromVector(Vector2 direction)
        {
            var horizontalMagnitude = Mathf.Abs(direction.x);
            var verticalMagnitude = Mathf.Abs(direction.y);
            if (verticalMagnitude <= horizontalMagnitude * DiagonalBoundaryTangent)
            {
                return direction.x >= 0f ? CharacterDirection.East : CharacterDirection.West;
            }

            if (horizontalMagnitude <= verticalMagnitude * DiagonalBoundaryTangent)
            {
                return direction.y >= 0f ? CharacterDirection.North : CharacterDirection.South;
            }

            if (direction.x >= 0f)
            {
                return direction.y >= 0f ? CharacterDirection.NorthEast : CharacterDirection.SouthEast;
            }

            return direction.y >= 0f ? CharacterDirection.NorthWest : CharacterDirection.SouthWest;
        }
    }
}
