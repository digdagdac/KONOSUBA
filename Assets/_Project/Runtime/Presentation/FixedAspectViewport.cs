using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FixedAspectViewport : MonoBehaviour
    {
        public const float TargetAspect = 16f / 9f;
        private Camera targetCamera;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            Apply(Screen.width, Screen.height);
        }

        private void OnPreCull()
        {
            Apply(Screen.width, Screen.height);
        }

        /// <summary>
        /// Applies a 16:9 viewport to the attached camera. Non-positive dimensions intentionally leave its rect unchanged.
        /// </summary>
        public void Apply(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var aspect = (float)width / height;
            if (aspect > TargetAspect)
            {
                var normalizedWidth = TargetAspect / aspect;
                targetCamera.rect = new Rect((1f - normalizedWidth) * 0.5f, 0f, normalizedWidth, 1f);
            }
            else
            {
                var normalizedHeight = aspect / TargetAspect;
                targetCamera.rect = new Rect(0f, (1f - normalizedHeight) * 0.5f, 1f, normalizedHeight);
            }
        }
    }
}
