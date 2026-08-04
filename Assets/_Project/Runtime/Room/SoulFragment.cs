using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class SoulFragment : MonoBehaviour
    {
        [SerializeField] private Collider2D collectionTrigger;

        private Action<SoulFragment> collected;
        private bool isInitialized;
        private bool isCollected;
        private bool isReleased;

        public bool IsCollected => isCollected;

        private void Awake()
        {
            ValidateConfiguration();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var playerLifeCycle = other.GetComponentInParent<PlayerLifeCycle>();
            if (playerLifeCycle == null)
            {
                return;
            }

            TryCollect(playerLifeCycle);
        }

        public void Initialize(Action<SoulFragment> collectedHandler)
        {
            if (isReleased)
            {
                throw new InvalidOperationException("A released soul fragment cannot be initialized.");
            }

            if (collectedHandler == null)
            {
                throw new ArgumentNullException(nameof(collectedHandler));
            }

            if (isInitialized)
            {
                throw new InvalidOperationException("A soul fragment can only be initialized once.");
            }

            collected = collectedHandler;
            isInitialized = true;
        }

        public bool TryCollect(PlayerLifeCycle playerLifeCycle)
        {
            if (playerLifeCycle == null)
            {
                throw new ArgumentNullException(nameof(playerLifeCycle));
            }

            if (isReleased)
            {
                throw new InvalidOperationException("A released soul fragment cannot be collected.");
            }
            if (!isInitialized)
            {
                throw new InvalidOperationException("Soul fragments must be initialized by M1RoomLifecycle before collection.");
            }

            if (isCollected || !playerLifeCycle.IsAlive)
            {
                return false;
            }

            isCollected = true;
            try
            {
                collected.Invoke(this);
            }
            finally
            {
                gameObject.SetActive(false);
            }

            return true;
        }

        public void Release()
        {
            if (isReleased)
            {
                return;
            }

            isReleased = true;
            collected = null;
            isInitialized = false;
            gameObject.SetActive(false);
        }

        private void ValidateConfiguration()
        {
            if (collectionTrigger == null)
            {
                throw new InvalidOperationException("SoulFragment requires a collection trigger.");
            }

            if (!collectionTrigger.isTrigger)
            {
                throw new InvalidOperationException("SoulFragment collection collider must be a trigger.");
            }
            if (collectionTrigger.GetComponent<SoulFragment>() != this)
            {
                throw new InvalidOperationException("SoulFragment collection trigger must be on the SoulFragment GameObject.");
            }
        }
    }
}
