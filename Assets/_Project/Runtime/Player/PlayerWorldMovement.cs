using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// Resolves player translation against the World layer so solid room geometry
    /// (pillars) blocks walk and dash paths instead of being teleported through.
    /// </summary>
    internal static class PlayerWorldMovement
    {
        private const float ContactSkin = 0.02f;
        private const float MinimumMoveSqrMagnitude = 0.00000001f;
        private const int HitCapacity = 8;

        private static readonly RaycastHit2D[] HitBuffer = new RaycastHit2D[HitCapacity];
        private static ContactFilter2D worldFilter;
        private static bool worldFilterReady;

        public static Vector3 ResolvePosition(
            Collider2D collider,
            Vector3 currentPosition,
            Vector2 desiredDelta,
            Vector2 extents)
        {
            if (collider == null)
            {
                throw new System.ArgumentNullException(nameof(collider));
            }

            if (desiredDelta.sqrMagnitude < MinimumMoveSqrMagnitude)
            {
                return PlayerController.ClampToWorld(currentPosition, extents);
            }

            EnsureWorldFilter();
            var distance = desiredDelta.magnitude;
            var direction = desiredDelta / distance;
            Physics2D.SyncTransforms();
            var hitCount = collider.Cast(direction, worldFilter, HitBuffer, distance + ContactSkin);
            if (hitCount > 0)
            {
                var closest = float.MaxValue;
                for (var index = 0; index < hitCount; index++)
                {
                    var hit = HitBuffer[index];
                    if (hit.collider == null || hit.collider.isTrigger)
                    {
                        continue;
                    }

                    closest = Mathf.Min(closest, hit.distance);
                }

                if (closest < float.MaxValue)
                {
                    distance = Mathf.Max(0f, closest - ContactSkin);
                }
            }

            var resolved = currentPosition + (Vector3)(direction * distance);
            return PlayerController.ClampToWorld(resolved, extents);
        }

        private static void EnsureWorldFilter()
        {
            if (worldFilterReady)
            {
                return;
            }

            worldFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                useDepth = false
            };
            worldFilter.SetLayerMask(LayerMask.GetMask("World"));
            worldFilterReady = true;
        }
    }
}
