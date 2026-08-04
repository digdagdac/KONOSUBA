using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class ArcherProjectilePresenter : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;

        private ArcherAI archer;
        private AttackContext currentContext;
        private Vector2 currentPosition;
        private bool isVisible;
        private bool isSubscribed;

        public bool IsVisible => isVisible;
        public AttackContext CurrentContext => currentContext;
        public Vector2 CurrentPosition => currentPosition;

        public void Bind(ArcherAI archer)
        {
            var nextArcher = archer ?? throw new System.ArgumentNullException(nameof(archer));
            RequireLine();

            if (this.archer != nextArcher)
            {
                Unsubscribe();
                ClearPresentation();
                this.archer = nextArcher;
            }

            if (!isActiveAndEnabled)
            {
                return;
            }

            Subscribe();
            SynchronizeFromArcher();
        }

        private void OnEnable()
        {
            ClearPresentation();
            if (archer == null)
            {
                return;
            }

            RequireLine();
            Subscribe();
            SynchronizeFromArcher();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearPresentation();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ClearPresentation();
        }

        private void Subscribe()
        {
            if (isSubscribed || archer == null)
            {
                return;
            }

            archer.ProjectileFired += HandleProjectileFired;
            archer.ProjectileMoved += HandleProjectileMoved;
            archer.ProjectileStopped += HandleProjectileStopped;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (archer != null)
            {
                archer.ProjectileFired -= HandleProjectileFired;
                archer.ProjectileMoved -= HandleProjectileMoved;
                archer.ProjectileStopped -= HandleProjectileStopped;
            }

            isSubscribed = false;
        }

        private void SynchronizeFromArcher()
        {
            if (!isActiveAndEnabled || archer == null || !archer.IsProjectileActive)
            {
                ClearPresentation();
                return;
            }

            var context = archer.ProjectileContext ??
                          throw new System.InvalidOperationException(
                              "Archer projectile presenter cannot render an active projectile without an attack context.");
            RenderProjectile(context, archer.ProjectilePosition);
        }

        private void HandleProjectileFired(AttackContext context, Vector2 position)
        {
            SynchronizeFromArcher();
        }

        private void HandleProjectileMoved(AttackContext context, Vector2 position)
        {
            SynchronizeFromArcher();
        }

        private void HandleProjectileStopped(AttackContext context, Vector2 position)
        {
            SynchronizeFromArcher();
        }

        private void RenderProjectile(AttackContext context, Vector2 position)
        {
            if (context == null)
            {
                throw new System.ArgumentNullException(nameof(context));
            }

            var renderer = ConfigureRenderer();
            var visibleLength = context.Width * 2f;
            var halfLength = visibleLength * 0.5f;
            var offset = context.NormalizedDirection * halfLength;
            var center = new Vector3(position.x, position.y, 0f);

            renderer.startWidth = context.Width;
            renderer.endWidth = context.Width;
            renderer.SetPosition(0, center - (Vector3)offset);
            renderer.SetPosition(1, center + (Vector3)offset);
            renderer.enabled = true;

            currentContext = context;
            currentPosition = position;
            isVisible = true;
        }

        private LineRenderer ConfigureRenderer()
        {
            var renderer = RequireLine();
            renderer.useWorldSpace = true;
            renderer.loop = false;
            renderer.positionCount = 2;

            if (!renderer.useWorldSpace)
            {
                throw new System.InvalidOperationException("Archer projectile presenter requires a world-space LineRenderer.");
            }

            if (renderer.positionCount != 2)
            {
                throw new System.InvalidOperationException("Archer projectile presenter requires exactly two line points.");
            }

            return renderer;
        }

        private LineRenderer RequireLine()
        {
            return line ?? throw new System.InvalidOperationException("Archer projectile presenter requires a LineRenderer.");
        }

        private void ClearPresentation()
        {
            if (line != null)
            {
                line.enabled = false;
            }

            currentContext = null;
            currentPosition = default;
            isVisible = false;
        }
    }
}
