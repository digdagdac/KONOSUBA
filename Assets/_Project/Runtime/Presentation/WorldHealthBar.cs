using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorldHealthBar : MonoBehaviour
    {
        private const float BarWidth = 0.72f;

        [SerializeField] private Health health;
        [SerializeField] private LineRenderer backgroundLine;
        [SerializeField] private LineRenderer fillLine;

        public Health Health => health;

        private void LateUpdate()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (health == null || backgroundLine == null || fillLine == null)
            {
                throw new InvalidOperationException("World health bar requires Health and two line renderers.");
            }

            var ratio = Mathf.Clamp01((float)health.CurrentHealth / health.MaximumHealth);
            backgroundLine.SetPosition(0, new Vector3(-BarWidth * 0.5f, 0f, 0f));
            backgroundLine.SetPosition(1, new Vector3(BarWidth * 0.5f, 0f, 0f));
            fillLine.SetPosition(0, new Vector3(-BarWidth * 0.5f, 0f, 0f));
            fillLine.SetPosition(1, new Vector3(-BarWidth * 0.5f + BarWidth * ratio, 0f, 0f));
            fillLine.startColor = fillLine.endColor = ratio <= 0.34f
                ? new Color32(255, 92, 102, 255)
                : new Color32(92, 230, 185, 255);
        }
    }
}
