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
        private int frameIndex;
        private bool initialized;
        private bool subscribed;
        private CharacterDirection lockedAttackDirection;
        private AttackStateMachine subscribedAttackState;
        private bool hasLockedAttackDirection;
        private bool skipEnemyAdvanceOnce;

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
            SynchronizeEnemyPresentation();
        }

        private void OnDisable()
        {
            Unsubscribe();
            skipEnemyAdvanceOnce = false;
        }

        private void LateUpdate()
        {
            Initialize();
            if (hitRemaining > 0f)
            {
                hitRemaining = Mathf.Max(0f, hitRemaining - Time.deltaTime);
                if (hitRemaining <= 0f && driver != CharacterAnimationDriver.Player)
                {
                    RefreshEnemyPresentation();
                }
            }

            if (driver == CharacterAnimationDriver.Player)
            {
                UpdatePlayerPresentation();
                return;
            }
            if (skipEnemyAdvanceOnce)
            {
                skipEnemyAdvanceOnce = false;
                return;
            }

            AdvanceClip(Time.deltaTime);
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
            if (initialized && driver == CharacterAnimationDriver.Player)
            {
                currentDirection = initialDirection;
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
            currentDirection = driver == CharacterAnimationDriver.Player
                ? initialDirection
                : DirectionFromVector(enemy.IntendedFacing);
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

            if (driver != CharacterAnimationDriver.Player)
            {
                subscribedAttackState = enemy.AttackState;
                if (subscribedAttackState == null)
                {
                    throw new InvalidOperationException("Enemy animation requires an initialized attack state.");
                }
            }

            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
            if (driver != CharacterAnimationDriver.Player)
            {
                enemy.IntendedFacingChanged += HandleEnemyIntendedFacingChanged;
                enemy.LocomotionModeChanged += HandleEnemyLocomotionModeChanged;
                subscribedAttackState.ContextLocked += HandleEnemyAttackContextLocked;
                subscribedAttackState.PhaseChanged += HandleEnemyAttackPhaseChanged;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (health != null)
            {
                health.Damaged -= HandleDamaged;
                health.Died -= HandleDied;
            }

            if (enemy != null)
            {
                enemy.IntendedFacingChanged -= HandleEnemyIntendedFacingChanged;
                enemy.LocomotionModeChanged -= HandleEnemyLocomotionModeChanged;
            }

            if (subscribedAttackState != null)
            {
                subscribedAttackState.ContextLocked -= HandleEnemyAttackContextLocked;
                subscribedAttackState.PhaseChanged -= HandleEnemyAttackPhaseChanged;
                subscribedAttackState = null;
            }

            subscribed = false;
        }

        private void UpdatePlayerPresentation()
        {
            var movement = transform.position - previousPosition;
            previousPosition = transform.position;
            if (movement.sqrMagnitude > MovementThresholdSquared)
            {
                currentDirection = DirectionFromVector(movement);
            }

            var desiredState = ResolveState(movement.sqrMagnitude > MovementThresholdSquared);
            if (desiredState != currentState || currentClip == null || currentClip.Direction != currentDirection)
            {
                BeginClip(desiredState, currentDirection);
            }
            else
            {
                AdvanceClip(Time.deltaTime);
            }
        }

        private CharacterAnimationState ResolveState(bool playerMoved)
        {
            if (health.IsDead)
            {
                return CharacterAnimationState.Death;
            }

            switch (driver)
            {
                case CharacterAnimationDriver.Player:
                    if (hitRemaining > 0f)
                    {
                        return CharacterAnimationState.Hit;
                    }

                    if (dashAbility.IsDashing)
                    {
                        return CharacterAnimationState.Dash;
                    }

                    if (blessingTargeting.IsSelecting)
                    {
                        return CharacterAnimationState.BlessCast;
                    }

                    return playerMoved ? CharacterAnimationState.Walk : CharacterAnimationState.Idle;
                case CharacterAnimationDriver.MajorEnemy:
                case CharacterAnimationDriver.Minion:
                    return ResolveEnemyState();
                default:
                    throw new ArgumentOutOfRangeException(nameof(driver), driver, "Unsupported animation driver.");
            }
        }

        private CharacterAnimationState ResolveEnemyState()
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
                    if (hitRemaining > 0f)
                    {
                        return CharacterAnimationState.Hit;
                    }

                    return ResolveEnemyLocomotionState();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CharacterAnimationState ResolveEnemyLocomotionState()
        {
            switch (enemy.CurrentLocomotionMode)
            {
                case LocomotionMode.Idle:
                    return CharacterAnimationState.Idle;
                case LocomotionMode.Walk:
                    return CharacterAnimationState.Walk;
                case LocomotionMode.Run:
                    return CharacterAnimationState.Run;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CharacterDirection ResolveEnemyDirection()
        {
            switch (enemy.CurrentAttackPhase)
            {
                case AttackPhase.Locked:
                case AttackPhase.Executing:
                case AttackPhase.Recovery:
                    if (hasLockedAttackDirection)
                    {
                        return lockedAttackDirection;
                    }

                    break;
                case AttackPhase.Idle:
                case AttackPhase.Warning:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return DirectionFromVector(enemy.IntendedFacing);
        }

        private void RefreshEnemyPresentation()
        {
            if (!initialized || driver == CharacterAnimationDriver.Player)
            {
                return;
            }

            var desiredState = ResolveState(false);
            var desiredDirection = ResolveEnemyDirection();
            if (desiredState != currentState || currentClip == null || currentClip.Direction != desiredDirection)
            {
                BeginClip(desiredState, desiredDirection);
                skipEnemyAdvanceOnce = true;
            }
        }
        private void SynchronizeEnemyPresentation()
        {
            if (driver == CharacterAnimationDriver.Player)
            {
                return;
            }

            var attackState = enemy.AttackState;
            if (attackState.CurrentContext != null)
            {
                lockedAttackDirection = DirectionFromVector(attackState.CurrentContext.NormalizedDirection);
                hasLockedAttackDirection = true;
            }
            else if (attackState.Phase == AttackPhase.Idle)
            {
                hasLockedAttackDirection = false;
            }

            RefreshEnemyPresentation();
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
        private void HandleEnemyIntendedFacingChanged(Vector2 facing)
        {
            RefreshEnemyPresentation();
        }

        private void HandleEnemyLocomotionModeChanged(LocomotionMode mode)
        {
            RefreshEnemyPresentation();
        }

        private void HandleEnemyAttackContextLocked(AttackContext context)
        {
            if (context == null)
            {
                hasLockedAttackDirection = false;
                RefreshEnemyPresentation();
                return;
            }

            lockedAttackDirection = DirectionFromVector(context.NormalizedDirection);
            hasLockedAttackDirection = true;
            RefreshEnemyPresentation();
        }

        private void HandleEnemyAttackPhaseChanged(AttackPhase phase)
        {
            if (phase == AttackPhase.Idle)
            {
                hasLockedAttackDirection = false;
            }

            RefreshEnemyPresentation();
        }


        private void HandleDamaged(DamageEvent damageEvent)
        {
            if (!health.IsDead)
            {
                hitRemaining = HitDisplayDuration;
                if (driver != CharacterAnimationDriver.Player)
                {
                    RefreshEnemyPresentation();
                }
            }
        }

        private void HandleDied(DeathEvent deathEvent)
        {
            hitRemaining = 0f;
            if (driver != CharacterAnimationDriver.Player)
            {
                RefreshEnemyPresentation();
            }
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
