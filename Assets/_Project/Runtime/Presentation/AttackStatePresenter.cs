using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class AttackStatePresenter : MonoBehaviour
    {
        private const int CircleSegments = 32;
        private const float MinimumLockedVisibilitySeconds = 0.08f;
        private const float CircleStrokeWidth = 0.06f;
        private const float MinimumLineStrokeWidth = 0.04f;

        [SerializeField] private LineRenderer line;
        [SerializeField] private Color warningColor = Color.yellow;
        [SerializeField] private Color lockedColor = Color.red;

        private EnemyBase owner;
        private AttackStateMachine state;
        private float lockedVisibleUntilUnscaledTime;

        public void Bind(EnemyBase enemy)
        {
            var nextOwner = enemy ?? throw new System.ArgumentNullException(nameof(enemy));
            var nextState = nextOwner.AttackState ??
                            throw new System.InvalidOperationException("Attack presenter cannot bind before its enemy attack state is initialized.");
            RequireLine();
            ValidatePhase(nextState.Phase);

            if (state == nextState)
            {
                owner = nextOwner;
                HandlePhase(nextState.Phase);
                return;
            }

            Unsubscribe();
            owner = nextOwner;
            state = nextState;
            state.PhaseChanged += HandlePhase;
            state.ContextLocked += HandleLocked;
            HandlePhase(state.Phase);
        }

        private void Update()
        {
            if (state == null)
            {
                return;
            }

            if (state.Phase == AttackPhase.Warning)
            {
                RenderWarningPreview();
            }
            else if ((state.Phase == AttackPhase.Executing || state.Phase == AttackPhase.Recovery) &&
                     Time.unscaledTime >= lockedVisibleUntilUnscaledTime)
            {
                RequireLine().enabled = false;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (state != null)
            {
                state.PhaseChanged -= HandlePhase;
                state.ContextLocked -= HandleLocked;
            }

            state = null;
            owner = null;
        }

        private void HandlePhase(AttackPhase phase)
        {
            var renderer = RequireLine();
            switch (phase)
            {
                case AttackPhase.Idle:
                    lockedVisibleUntilUnscaledTime = 0f;
                    renderer.enabled = false;
                    return;
                case AttackPhase.Executing:
                case AttackPhase.Recovery:
                    renderer.enabled = Time.unscaledTime < lockedVisibleUntilUnscaledTime;
                    return;
                case AttackPhase.Warning:
                    renderer.startColor = renderer.endColor = warningColor;
                    RenderWarningPreview();
                    renderer.enabled = true;
                    return;
                case AttackPhase.Locked:
                    lockedVisibleUntilUnscaledTime =
                        Mathf.Max(lockedVisibleUntilUnscaledTime, Time.unscaledTime + MinimumLockedVisibilitySeconds);
                    renderer.startColor = renderer.endColor = lockedColor;
                    renderer.enabled = true;
                    return;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(phase), phase, "Unsupported attack phase.");
            }
        }

        private void RenderWarningPreview()
        {
            if (owner == null)
            {
                return;
            }

            var origin = (Vector2)owner.transform.position;
            var direction = owner.PlayerTarget == null
                ? Vector2.down
                : (Vector2)owner.PlayerTarget.position - origin;
            var shape = owner is MinionAI ? AttackShape.Circle : AttackShape.Line;
            RenderGeometry(origin, direction, shape, owner.RuntimeStats.AttackRange, owner.RuntimeStats.AttackWidth);
        }

        private void HandleLocked(AttackContext context)
        {
            RenderGeometry(
                context.Origin,
                context.NormalizedDirection,
                context.Shape,
                context.Range,
                context.Width);
            lockedVisibleUntilUnscaledTime = Time.unscaledTime + MinimumLockedVisibilitySeconds;
        }

        private void RenderGeometry(
            Vector2 origin,
            Vector2 direction,
            AttackShape shape,
            float range,
            float width)
        {
            var renderer = RequireLine();
            var normalizedDirection = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector2.down;
            // Line telegraphs must match the damaging corridor width; a thin "hint"
            // stroke made edge hits look unfair relative to authoritative geometry.
            renderer.startWidth = renderer.endWidth = shape == AttackShape.Circle
                ? CircleStrokeWidth
                : Mathf.Max(width, MinimumLineStrokeWidth);

            switch (shape)
            {
                case AttackShape.Line:
                    renderer.loop = false;
                    renderer.positionCount = 2;
                    renderer.SetPosition(0, origin);
                    renderer.SetPosition(1, origin + normalizedDirection * range);
                    return;
                case AttackShape.Circle:
                    var center = origin + normalizedDirection * (range * 0.5f);
                    var radius = Mathf.Max(width * 0.5f, range * 0.5f);
                    renderer.loop = true;
                    renderer.positionCount = CircleSegments;
                    for (var index = 0; index < CircleSegments; index++)
                    {
                        var angle = index * Mathf.PI * 2f / CircleSegments;
                        renderer.SetPosition(
                            index,
                            center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                    }

                    return;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(shape), shape, "Unsupported attack preview shape.");
            }
        }

        private static void ValidatePhase(AttackPhase phase)
        {
            switch (phase)
            {
                case AttackPhase.Idle:
                case AttackPhase.Warning:
                case AttackPhase.Locked:
                case AttackPhase.Executing:
                case AttackPhase.Recovery:
                    return;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(phase), phase, "Unsupported attack phase.");
            }
        }

        private LineRenderer RequireLine()
        {
            return line ?? throw new System.InvalidOperationException("Attack presenter requires a LineRenderer.");
        }
    }
}
