using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class EchoProjectilePresenter : MonoBehaviour
    {
        private static readonly Color PendingColor = new Color32(179, 121, 255, 180);
        private static readonly Color ProjectileColor = new Color32(198, 151, 255, 255);

        [SerializeField] private SpriteRenderer pendingLineRenderer;
        [SerializeField] private SpriteRenderer projectileRenderer;

        private ArcherAI archer;
        private bool isSubscribed;

        public void Bind(ArcherAI archer)
        {
            var nextArcher = archer ?? throw new ArgumentNullException(nameof(archer));
            RequireRenderers();

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
            SynchronizePendingFromArcher();
            SynchronizeProjectileFromArcher();
        }

        private void Awake()
        {
            BindToParent();
        }

        private void OnEnable()
        {
            if (archer == null)
            {
                BindToParent();
                return;
            }

            RequireRenderers();
            Subscribe();
            SynchronizePendingFromArcher();
            SynchronizeProjectileFromArcher();
        }

        private void Update()
        {
            SynchronizePendingFromArcher();
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

        private void BindToParent()
        {
            Bind(GetComponentInParent<ArcherAI>() ?? throw new InvalidOperationException(
                "EchoProjectilePresenter requires an ArcherAI parent."));
        }

        private void Subscribe()
        {
            if (isSubscribed || archer == null)
            {
                return;
            }

            archer.EchoProjectileFired += HandleEchoProjectileFired;
            archer.EchoProjectileMoved += HandleEchoProjectileMoved;
            archer.EchoProjectileStopped += HandleEchoProjectileStopped;
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
                archer.EchoProjectileFired -= HandleEchoProjectileFired;
                archer.EchoProjectileMoved -= HandleEchoProjectileMoved;
                archer.EchoProjectileStopped -= HandleEchoProjectileStopped;
            }

            isSubscribed = false;
        }

        private void SynchronizePendingFromArcher()
        {
            if (!isActiveAndEnabled || archer == null || !archer.IsEchoPending)
            {
                ClearPendingPresentation();
                return;
            }

            var context = archer.PendingEchoContext ?? throw new InvalidOperationException(
                "Echo projectile presenter cannot render a pending echo without an attack context.");
            var executionAt = archer.PendingEchoExecutionAt;
            RenderPending(context, executionAt);
        }

        private void SynchronizeProjectileFromArcher()
        {
            if (!isActiveAndEnabled || archer == null || !archer.IsEchoProjectileActive)
            {
                ClearProjectilePresentation();
                return;
            }

            var context = archer.EchoProjectileContext ?? throw new InvalidOperationException(
                "Echo projectile presenter cannot render an active echo without an attack context.");
            RenderProjectile(context, archer.EchoProjectilePosition);
        }

        private void HandleEchoProjectileFired(AttackContext context, Vector2 position)
        {
            SynchronizePendingFromArcher();
            SynchronizeProjectileFromArcher();
        }

        private void HandleEchoProjectileMoved(AttackContext context, Vector2 position)
        {
            SynchronizeProjectileFromArcher();
        }

        private void HandleEchoProjectileStopped(AttackContext context, Vector2 position)
        {
            SynchronizePendingFromArcher();
            SynchronizeProjectileFromArcher();
        }

        private void RenderPending(AttackContext context, float executionAt)
        {
            var renderer = RequirePendingLineRenderer();
            var direction = context.NormalizedDirection;
            var center = context.Origin + direction * (context.Range * 0.5f);
            var remainingDelay = Mathf.Max(0f, executionAt - Time.time);
            var alpha = Mathf.Lerp(0.45f, 0.9f, 1f - Mathf.Clamp01(remainingDelay));

            renderer.transform.position = new Vector3(center.x, center.y, 0f);
            renderer.transform.rotation = Quaternion.FromToRotation(Vector3.right, direction);
            // Context range/width are already world sizes. Parent Giant scale must not
            // multiply them a second time when converting to localScale.
            renderer.transform.localScale = WorldToLocalScale(
                renderer.transform,
                new Vector3(context.Range, Mathf.Max(context.Width, 0.08f), 1f));
            renderer.color = new Color(PendingColor.r, PendingColor.g, PendingColor.b, alpha);
            renderer.enabled = true;
        }

        private void RenderProjectile(AttackContext context, Vector2 position)
        {
            var renderer = RequireProjectileRenderer();
            renderer.transform.position = new Vector3(position.x, position.y, 0f);
            renderer.transform.rotation = Quaternion.FromToRotation(Vector3.right, context.NormalizedDirection);
            var diameter = Mathf.Max(context.Width * 2f, 0.4f);
            renderer.transform.localScale = WorldToLocalScale(
                renderer.transform,
                new Vector3(diameter, diameter, 1f));
            renderer.color = ProjectileColor;
            renderer.enabled = true;
        }

        private static Vector3 WorldToLocalScale(Transform target, Vector3 worldScale)
        {
            var parent = target.parent;
            if (parent == null)
            {
                return worldScale;
            }

            var lossy = parent.lossyScale;
            return new Vector3(
                worldScale.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldScale.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                worldScale.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
        }

        private void ClearPresentation()
        {
            ClearPendingPresentation();
            ClearProjectilePresentation();
        }

        private void ClearPendingPresentation()
        {
            if (pendingLineRenderer != null)
            {
                pendingLineRenderer.enabled = false;
            }
        }

        private void ClearProjectilePresentation()
        {
            if (projectileRenderer != null)
            {
                projectileRenderer.enabled = false;
            }
        }

        private void RequireRenderers()
        {
            RequirePendingLineRenderer();
            RequireProjectileRenderer();
        }

        private SpriteRenderer RequirePendingLineRenderer()
        {
            return pendingLineRenderer ?? throw new InvalidOperationException(
                "EchoProjectilePresenter requires a pending-line SpriteRenderer.");
        }

        private SpriteRenderer RequireProjectileRenderer()
        {
            return projectileRenderer ?? throw new InvalidOperationException(
                "EchoProjectilePresenter requires a projectile SpriteRenderer.");
        }
    }
}
