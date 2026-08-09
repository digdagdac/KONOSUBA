using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// Renders eligible / hovered blessing targets published by
    /// <see cref="BlessingTargeting.TargetStatesChanged"/>. Without a subscriber the
    /// core cast loop is silent about who can receive a blessing.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class BlessingTargetFeedbackPresenter : MonoBehaviour
    {
        private const int RingSegments = 28;
        private const float EligibleRadius = 0.55f;
        private const float HoverRadius = 0.72f;
        private const float EligibleStroke = 0.06f;
        private const float HoverStroke = 0.09f;

        private static readonly Color EligibleColor = new Color32(255, 238, 143, 170);
        private static readonly Color HoverColor = new Color32(255, 255, 255, 230);
        private static readonly Color PreviewColor = new Color32(179, 121, 255, 220);

        [SerializeField] private BlessingTargeting blessingTargeting;

        private readonly List<LineRenderer> ringPool = new List<LineRenderer>(8);
        private bool subscribed;

        private void Awake()
        {
            if (blessingTargeting == null)
            {
                blessingTargeting = GetComponent<BlessingTargeting>();
            }
        }

        private void OnEnable()
        {
            Subscribe();
            if (blessingTargeting != null)
            {
                RenderStates(blessingTargeting.GetTargetStates());
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            HideAll();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed || blessingTargeting == null)
            {
                return;
            }

            blessingTargeting.TargetStatesChanged += HandleTargetStatesChanged;
            blessingTargeting.SelectionUiChanged += HandleSelectionUiChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || blessingTargeting == null)
            {
                return;
            }

            blessingTargeting.TargetStatesChanged -= HandleTargetStatesChanged;
            blessingTargeting.SelectionUiChanged -= HandleSelectionUiChanged;
            subscribed = false;
        }

        private void HandleSelectionUiChanged(BlessingSelectionState selection)
        {
            if (!selection.IsSelecting)
            {
                HideAll();
            }
        }

        private void HandleTargetStatesChanged(IReadOnlyList<BlessingTargetState> states)
        {
            RenderStates(states);
        }

        private void RenderStates(IReadOnlyList<BlessingTargetState> states)
        {
            if (blessingTargeting == null || !blessingTargeting.IsSelecting || states == null || states.Count == 0)
            {
                HideAll();
                return;
            }

            var visible = 0;
            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];
                if (!state.IsEligible && !state.IsOutlined && !state.HasPreview)
                {
                    continue;
                }

                var ring = RequireRing(visible);
                ConfigureRing(
                    ring,
                    state.WorldPosition,
                    state.HasPreview ? HoverRadius : EligibleRadius,
                    state.HasPreview ? HoverStroke : EligibleStroke,
                    state.HasPreview ? PreviewColor : state.IsOutlined ? HoverColor : EligibleColor);
                ring.enabled = true;
                visible++;
            }

            for (var index = visible; index < ringPool.Count; index++)
            {
                ringPool[index].enabled = false;
            }
        }

        private void HideAll()
        {
            for (var index = 0; index < ringPool.Count; index++)
            {
                if (ringPool[index] != null)
                {
                    ringPool[index].enabled = false;
                }
            }
        }

        private LineRenderer RequireRing(int index)
        {
            while (ringPool.Count <= index)
            {
                var child = new GameObject($"BlessingTargetRing_{ringPool.Count}", typeof(LineRenderer));
                child.transform.SetParent(transform, false);
                var line = child.GetComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = RingSegments;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sortingLayerName = "UI";
                line.sortingOrder = 40;
                if (line.sharedMaterial == null)
                {
                    line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                }

                ringPool.Add(line);
            }

            return ringPool[index];
        }

        private static void ConfigureRing(
            LineRenderer line,
            Vector3 center,
            float radius,
            float stroke,
            Color color)
        {
            line.startWidth = line.endWidth = stroke;
            line.startColor = line.endColor = color;
            for (var index = 0; index < RingSegments; index++)
            {
                var angle = index * Mathf.PI * 2f / RingSegments;
                line.SetPosition(
                    index,
                    new Vector3(
                        center.x + Mathf.Cos(angle) * radius,
                        center.y + Mathf.Sin(angle) * radius,
                        center.z));
            }
        }
    }
}
