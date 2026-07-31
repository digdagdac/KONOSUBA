using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    public enum LocomotionMode
    {
        Idle = 0,
        Walk = 1,
        Run = 2
    }

    [RequireComponent(typeof(Health))]
    public abstract class EnemyBase : MonoBehaviour, IDamageSource, IEnemyBlessingRuntime
    {
        private static readonly BlessingType[] NoBlessings = Array.Empty<BlessingType>();
        private const float WorldMinimumX = -8f;
        private const float WorldMaximumX = 8f;
        private const float WorldMinimumY = -4.5f;
        private const float WorldMaximumY = 4.5f;

        [SerializeField] private EnemyDefinition definition;
        [SerializeField] private Health health;
        [SerializeField] private Transform playerTarget;
        [SerializeField] private Transform spawnTransform;
        [SerializeField] private Vector2 initialIntendedFacing = Vector2.down;

        private readonly DamageLedger damageLedger = new DamageLedger();
        private readonly List<RaycastHit2D> movementHits = new List<RaycastHit2D>();
        // Separate reusable buffers: an obstacle probe and a damage sweep run
        // back to back inside a single frame and must not share storage.
        private readonly List<RaycastHit2D> obstacleHits = new List<RaycastHit2D>();
        private readonly List<RaycastHit2D> damageSweepHits = new List<RaycastHit2D>();
        private readonly Dictionary<Collider2D, bool> ownedColliderCache =
            new Dictionary<Collider2D, bool>();
        private Transform cachedPlayerHealthSource;
        private Health cachedPlayerHealth;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private EnemyRuntimeStats runtimeStats;
        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private Vector3 baseLocalScale;
        private float baseMass;
        private AttackStateMachine attackState;
        private Vector2 intendedFacing;
        private LocomotionMode currentLocomotionMode;

        public event Action<EnemyRuntimeStats, EnemyRuntimeStats> RuntimeStatsChanged;
        public event Action<EnemyBase> Restarted;
        public event Action<EnemyRuntimeStats, EnemyRuntimeStats, float> BlessingRuntimeStatsApplied;
        public event Action<Vector2> IntendedFacingChanged;
        public event Action<LocomotionMode> LocomotionModeChanged;

        public int EntityId => health.EntityId;
        public Health Health => health;
        public EnemyDefinition Definition => definition;
        public Transform PlayerTarget => playerTarget;
        public Transform SpawnTransform => spawnTransform;
        public DamageLedger DamageLedger => damageLedger;
        public AttackStateMachine AttackState => attackState;
        public AttackPhase CurrentAttackPhase => attackState.Phase;
        public EnemyRuntimeStats RuntimeStats => runtimeStats;
        public bool IsDead => health.IsDead;
        public Vector2 IntendedFacing => intendedFacing;
        public LocomotionMode CurrentLocomotionMode => currentLocomotionMode;
        public float HealthRatio => health.MaximumHealth == 0
            ? 0f
            : (float)health.CurrentHealth / health.MaximumHealth;

        protected virtual void Awake()
        {
            if (health == null)
            {
                health = GetComponent<Health>();
            }

            if (health == null)
            {
                throw new InvalidOperationException("EnemyBase requires a Health component.");
            }

            if (health.EntityId == 0)
            {
                throw new InvalidOperationException("EnemyBase requires Health to have a non-zero stable entity ID.");
            }

            if (definition == null)
            {
                throw new InvalidOperationException("EnemyBase requires an EnemyDefinition.");
            }
            InitializeMovementIntent();

            var authoredSpawn = spawnTransform == null ? transform : spawnTransform;
            spawnPosition = authoredSpawn.position;
            spawnRotation = authoredSpawn.rotation;
            baseLocalScale = transform.localScale;
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            if (body == null || bodyCollider == null)
            {
                throw new InvalidOperationException("EnemyBase requires Rigidbody2D and Collider2D components for collision-aware movement.");
            }

            baseMass = body.mass;
            attackState = new AttackStateMachine(EntityId);
            var attackPresenter = GetComponentInChildren<AttackStatePresenter>(true);
            if (attackPresenter != null)
            {
                attackPresenter.Bind(this);
            }
            if (this is ArcherAI archer)
            {
                var projectilePresenter = GetComponentInChildren<ArcherProjectilePresenter>(true);
                if (projectilePresenter != null)
                {
                    projectilePresenter.Bind(archer);
                }
            }
            health.Died += HandleDeath;
            ApplyRuntimeStats(EnemyRuntimeStats.Recompute(definition, NoBlessings), HealthRatio);
            var blessingIndicator = GetComponentInChildren<BlessingIndicator>(true);
            if (blessingIndicator != null)
            {
                blessingIndicator.Bind(this);
            }
            OnEnemyInitialized();
        }

        protected virtual void OnDisable()
        {
            try
            {
                if (attackState != null)
                {
                    attackState.Cancel();
                }
            }
            finally
            {
                ResetMovementIntent();
            }
        }

        protected virtual void OnDestroy()
        {
            if (health != null)
            {
                health.Died -= HandleDeath;
            }
        }

        private void Update()
        {
            if (health.IsDead)
            {
                ResetMovementIntent();
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            TickBehavior(deltaTime);
        }

        public void SetPlayerTarget(Transform target)
        {
            playerTarget = target;
            if (target == null)
            {
                ResetMovementIntent();
            }
        }

        public void RecomputeRuntimeStats(IReadOnlyCollection<BlessingType> activeBlessingIds)
        {
            ApplyRuntimeStats(EnemyRuntimeStats.Recompute(definition, activeBlessingIds), HealthRatio);
        }

        public void ApplyBlessingRuntimeStats(EnemyRuntimeStats stats, float healthRatio)
        {
            if (float.IsNaN(healthRatio) ||
                float.IsInfinity(healthRatio) ||
                healthRatio < 0f ||
                healthRatio > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(healthRatio), healthRatio, "Health ratio must be finite and within [0,1].");
            }

            ApplyRuntimeStats(stats, healthRatio);
        }

        public virtual bool SupportsBehavioralBlessing(BlessingType type)
        {
            return false;
        }

        public virtual void ApplyBehavioralBlessings(IReadOnlyList<BlessingType> activeBlessings)
        {
        }

        public void Restart()
        {
            var failures = new List<Exception>();
            try
            {
                ResetMovementIntent();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                attackState.Reset();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            damageLedger.Clear();
            ownedColliderCache.Clear();
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = spawnPosition;
            body.rotation = spawnRotation.eulerAngles.z;

            try
            {
                health.ResetHealth();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                OnRestarted();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (Restarted != null)
            {
                foreach (Action<EnemyBase> observer in Restarted.GetInvocationList())
                {
                    try
                    {
                        observer(this);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            ThrowFailures(failures, "Enemy restart observers failed.");
        }

        public void ResetForRoom()
        {
            var failures = new List<Exception>();
            try
            {
                ApplyRuntimeStats(EnemyRuntimeStats.Recompute(definition, NoBlessings), HealthRatio);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                Restart();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures, "Enemy room reset failed.");
        }

        protected bool TryGetPlayerTargetPosition(out Vector2 position)
        {
            if (playerTarget == null)
            {
                ResetMovementIntent();
                position = default;
                return false;
            }

            var targetHealth = GetCachedPlayerHealth();
            if (targetHealth != null && targetHealth.IsDead)
            {
                ResetMovementIntent();
                position = default;
                return false;
            }

            position = playerTarget.position;
            return true;
        }
        protected void SetIntendedFacing(Vector2 facing)
        {
            var normalizedFacing = NormalizeFacing(facing, nameof(facing));
            if (intendedFacing == normalizedFacing)
            {
                return;
            }

            intendedFacing = normalizedFacing;
            IntendedFacingChanged?.Invoke(intendedFacing);
        }

        protected void SetLocomotionMode(LocomotionMode mode)
        {
            if (mode != LocomotionMode.Idle &&
                mode != LocomotionMode.Walk &&
                mode != LocomotionMode.Run)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported locomotion mode.");
            }

            if (currentLocomotionMode == mode)
            {
                return;
            }

            currentLocomotionMode = mode;
            LocomotionModeChanged?.Invoke(currentLocomotionMode);
        }
        private void InitializeMovementIntent()
        {
            intendedFacing = NormalizeFacing(initialIntendedFacing, nameof(initialIntendedFacing));
            currentLocomotionMode = LocomotionMode.Idle;
        }

        private void ResetMovementIntent()
        {
            SetIntendedFacing(initialIntendedFacing);
            SetLocomotionMode(LocomotionMode.Idle);
        }

        private static Vector2 NormalizeFacing(Vector2 facing, string name)
        {
            if (float.IsNaN(facing.x) ||
                float.IsInfinity(facing.x) ||
                float.IsNaN(facing.y) ||
                float.IsInfinity(facing.y))
            {
                throw new ArgumentOutOfRangeException(name, facing, "Facing must be finite and non-zero.");
            }

            var magnitude = facing.magnitude;
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude) || magnitude <= 0f)
            {
                throw new ArgumentOutOfRangeException(name, facing, "Facing must be finite and non-zero.");
            }

            return facing / magnitude;
        }


        /// <summary>
        /// Resolves the player's Health once per target transform. This runs every
        /// frame from the AI tick, so the hierarchy walk must not repeat per frame.
        /// </summary>
        private Health GetCachedPlayerHealth()
        {
            if (!ReferenceEquals(cachedPlayerHealthSource, playerTarget))
            {
                cachedPlayerHealthSource = playerTarget;
                cachedPlayerHealth = playerTarget == null
                    ? null
                    : playerTarget.GetComponentInParent<Health>();
            }

            return cachedPlayerHealth;
        }

        /// <summary>
        /// Reusable circle sweep. The allocating <c>CircleCastAll</c> variants ran
        /// every frame while a projectile or charge was live and dominated the
        /// per-frame garbage on the WebGL frame-time budget.
        /// </summary>
        private static int CircleSweep(
            Vector2 origin,
            float radius,
            Vector2 direction,
            float distance,
            LayerMask mask,
            List<RaycastHit2D> results)
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(mask);
            // CircleCastAll honours the global trigger-query setting; preserve it
            // so switching to the filtered overload cannot change hit results.
            filter.useTriggers = Physics2D.queriesHitTriggers;
            results.Clear();
            return Physics2D.CircleCast(origin, radius, direction, filter, results, distance);
        }

        /// <summary>
        /// Finds the nearest world obstacle along a swept circle, ignoring the
        /// attacker's own colliders. Shared by the archer projectile and the
        /// dasher charge, which previously carried duplicate copies.
        /// </summary>
        protected bool TryGetObstacleDistance(
            Vector2 origin,
            float radius,
            Vector2 direction,
            float distance,
            out float nearestObstacleDistance)
        {
            nearestObstacleDistance = distance;
            var hitCount = CircleSweep(origin, radius, direction, distance, definition.WorldCollisionMask, obstacleHits);
            var foundObstacle = false;

            for (var index = 0; index < hitCount && index < obstacleHits.Count; index++)
            {
                var hit = obstacleHits[index];
                if (hit.collider == null || IsOwnedCollider(hit.collider))
                {
                    continue;
                }

                if (!foundObstacle || hit.distance < nearestObstacleDistance)
                {
                    nearestObstacleDistance = hit.distance;
                    foundObstacle = true;
                }
            }

            obstacleHits.Clear();
            return foundObstacle;
        }

        /// <summary>
        /// Applies locked-attack damage along a swept circle. <paramref name="continueCondition"/>
        /// aborts the sweep when the owning attack is invalidated mid-iteration;
        /// the return value reports whether the sweep is still authoritative.
        /// </summary>
        protected bool SweepAttackDamage(
            Vector2 origin,
            AttackContext attackContext,
            float distance,
            Func<bool> continueCondition)
        {
            var hitCount = CircleSweep(
                origin,
                attackContext.Width * 0.5f,
                attackContext.NormalizedDirection,
                distance,
                attackContext.TargetMask,
                damageSweepHits);

            try
            {
                for (var index = 0; index < hitCount && index < damageSweepHits.Count; index++)
                {
                    if (continueCondition != null && !continueCondition())
                    {
                        return false;
                    }

                    TryApplyAttackDamage(attackContext, damageSweepHits[index].collider);
                }
            }
            finally
            {
                damageSweepHits.Clear();
            }

            return continueCondition == null || continueCondition();
        }

        /// <summary>
        /// Caches whether a collider belongs to this attacker. The uncached
        /// hierarchy walk ran for every hit of every sweep frame.
        /// </summary>
        protected bool IsOwnedCollider(Collider2D collider)
        {
            if (collider == null)
            {
                return false;
            }

            if (ownedColliderCache.TryGetValue(collider, out var owned))
            {
                return owned;
            }

            var colliderHealth = collider.GetComponentInParent<Health>();
            owned = colliderHealth != null && colliderHealth.EntityId == EntityId;
            ownedColliderCache[collider] = owned;
            return owned;
        }

        protected void MoveTowards(Vector2 targetPosition, float maximumDistanceDelta)
        {
            if (maximumDistanceDelta <= 0f)
            {
                return;
            }

            MoveWithCollision(Vector2.MoveTowards((Vector2)transform.position, targetPosition, maximumDistanceDelta));
        }

        protected void MoveInDirection(Vector2 normalizedDirection, float distance)
        {
            if (distance <= 0f)
            {
                return;
            }

            var current = (Vector2)transform.position;
            MoveWithCollision(current + normalizedDirection * distance);
        }

        protected void BeginAttackWarning(float requestedWarningDuration)
        {
            attackState.BeginWarning(requestedWarningDuration);
            damageLedger.Clear();
        }

        protected bool AdvanceAttackWarning(float deltaTime)
        {
            return attackState.AdvanceWarning(deltaTime);
        }

        protected AttackContext LockAttack(
            Vector2 direction,
            AttackShape shape,
            float range,
            float width,
            int damage,
            LayerMask targetMask)
        {
            return attackState.Lock(
                Time.time,
                transform.position,
                direction,
                shape,
                range,
                width,
                damage,
                targetMask);
        }

        protected void BeginAttackExecution()
        {
            attackState.BeginExecuting();
        }

        protected void BeginAttackRecovery()
        {
            attackState.BeginRecovery();
        }

        protected void CompleteAttackRecovery()
        {
            attackState.CompleteRecovery();
        }

        protected void CancelAttack()
        {
            attackState.Cancel();
        }

        protected bool TryApplyAttackDamage(AttackContext attackContext, Collider2D collider)
        {
            if (collider == null)
            {
                return false;
            }

            var target = collider.GetComponentInParent<Health>();
            if (target == null || target.EntityId == 0 || target.EntityId == attackContext.AttackerEntityId)
            {
                return false;
            }

            var damageEvent = new DamageEvent(
                attackContext.AttackInstanceId,
                attackContext.AttackerEntityId,
                target.EntityId,
                attackContext.Damage);

            return damageLedger.TryRegister(in damageEvent) && target.TryApplyDamage(damageEvent);
        }

        private void ApplyRuntimeStats(EnemyRuntimeStats nextStats, float healthRatio)
        {
            health.SetMaximumHealthAndRatio(nextStats.MaximumHealth, healthRatio);
            transform.localScale = baseLocalScale * nextStats.ScaleMultiplier;
            Physics2D.SyncTransforms();
            ConstrainBodyToWorldBounds();
            if (body != null)
            {
                body.mass = baseMass * nextStats.MassMultiplier;
            }
            var previousStats = runtimeStats;
            runtimeStats = nextStats;
            OnRuntimeStatsChanged(previousStats, runtimeStats);
            RuntimeStatsChanged?.Invoke(previousStats, runtimeStats);
            BlessingRuntimeStatsApplied?.Invoke(previousStats, runtimeStats, healthRatio);
        }

        protected virtual void OnEnemyInitialized()
        {
        }

        protected virtual void OnRuntimeStatsChanged(EnemyRuntimeStats previousStats, EnemyRuntimeStats currentStats)
        {
        }

        private void ConstrainBodyToWorldBounds()
        {
            var extents = bodyCollider.bounds.extents;
            var position = body.position;
            body.position = new Vector2(
                Mathf.Clamp(position.x, WorldMinimumX + extents.x, WorldMaximumX - extents.x),
                Mathf.Clamp(position.y, WorldMinimumY + extents.y, WorldMaximumY - extents.y));
        }
        private void MoveWithCollision(Vector2 desiredPosition)
        {
            var current = body.position;
            var delta = desiredPosition - current;
            var distance = delta.magnitude;
            if (distance <= 0f)
            {
                return;
            }

            var direction = delta / distance;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(definition.WorldCollisionMask);
            filter.useTriggers = false;
            movementHits.Clear();
            body.Cast(direction, filter, movementHits, distance);

            var allowedDistance = distance;
            for (var index = 0; index < movementHits.Count; index++)
            {
                var hit = movementHits[index];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.distance <= 0.001f && Vector2.Dot(direction, hit.normal) > 0f)
                {
                    continue;
                }

                if (hit.distance < allowedDistance)
                {
                    allowedDistance = Mathf.Max(0f, hit.distance - 0.001f);
                }
            }

            var extents = bodyCollider.bounds.extents;
            var next = current + direction * allowedDistance;
            body.position = new Vector2(
                Mathf.Clamp(next.x, WorldMinimumX + extents.x, WorldMaximumX - extents.x),
                Mathf.Clamp(next.y, WorldMinimumY + extents.y, WorldMaximumY - extents.y));
            movementHits.Clear();
        }
        protected virtual void OnEnemyDied(DeathEvent deathEvent)
        {
        }

        protected virtual void OnRestarted()
        {
        }

        private static void ThrowFailures(List<Exception> failures, string aggregateMessage)
        {
            if (failures.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(aggregateMessage, failures);
            }
        }
        protected abstract void TickBehavior(float deltaTime);

        private void HandleDeath(DeathEvent deathEvent)
        {
            var failures = new List<Exception>();
            try
            {
                ResetMovementIntent();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                attackState.HandleOwnerDeath();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                OnEnemyDied(deathEvent);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            ThrowFailures(failures, "Enemy death cleanup failed.");
        }
    }

}
