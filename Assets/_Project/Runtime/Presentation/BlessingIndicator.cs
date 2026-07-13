using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class BlessingIndicator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer hasteRenderer;
        [SerializeField] private SpriteRenderer giantRenderer;

        private EnemyBase owner;

        public void Bind(EnemyBase enemy)
        {
            var nextOwner = enemy ?? throw new System.ArgumentNullException(nameof(enemy));
            RequireRenderers();

            if (owner == nextOwner)
            {
                Render(nextOwner.RuntimeStats);
                return;
            }

            Unsubscribe();
            owner = nextOwner;
            owner.RuntimeStatsChanged += HandleRuntimeStatsChanged;
            Render(owner.RuntimeStats);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void HandleRuntimeStatsChanged(
            EnemyRuntimeStats previousStats,
            EnemyRuntimeStats currentStats)
        {
            Render(currentStats);
        }

        private void Render(EnemyRuntimeStats stats)
        {
            RequireRenderers();
            hasteRenderer.enabled = stats.HasHaste;
            giantRenderer.enabled = stats.HasGiant;
        }

        private void Unsubscribe()
        {
            if (owner != null)
            {
                owner.RuntimeStatsChanged -= HandleRuntimeStatsChanged;
            }

            owner = null;
        }

        private void RequireRenderers()
        {
            if (hasteRenderer == null || giantRenderer == null)
            {
                throw new System.InvalidOperationException("Blessing indicator requires independent Haste and Giant renderers.");
            }
        }
    }
}
