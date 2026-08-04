using System;
using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// Tells the player the run ended and how to continue. Death previously left the room
    /// silent, so a first-time player had no way to learn that <c>R</c> restarts it.
    /// </summary>
    /// <remarks>
    /// Presentation only. It observes the player life cycle and never restarts anything,
    /// which keeps the restart path owned by <see cref="RoomRestartController"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RunOutcomePresenter : MonoBehaviour
    {
        [SerializeField] private PlayerLifeCycle playerLifeCycle;
        [SerializeField] private GameObject defeatRoot;

        private bool subscribed;
        private bool started;

        /// <summary>True while the defeat panel is on screen.</summary>
        public bool IsDefeatVisible => defeatRoot != null && defeatRoot.activeSelf;

        private void Awake()
        {
            ValidateConfiguration();
            defeatRoot.SetActive(false);
        }

        private void OnEnable()
        {
            ValidateConfiguration();
            Subscribe();
            if (started)
            {
                SyncToLifeCycle();
            }
        }

        private void Start()
        {
            // The player sets its own alive state in Awake, so the first sync waits until every
            // Awake has run. Reading it earlier would flash a defeat panel on a live player.
            started = true;
            SyncToLifeCycle();
        }

        private void SyncToLifeCycle()
        {
            defeatRoot.SetActive(!playerLifeCycle.IsAlive);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            playerLifeCycle.Died += HandleDied;
            playerLifeCycle.Reset += HandleReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            playerLifeCycle.Died -= HandleDied;
            playerLifeCycle.Reset -= HandleReset;
            subscribed = false;
        }

        private void HandleDied(DeathEvent deathEvent)
        {
            defeatRoot.SetActive(true);
        }

        private void HandleReset()
        {
            defeatRoot.SetActive(false);
        }

        private void ValidateConfiguration()
        {
            if (playerLifeCycle == null || defeatRoot == null)
            {
                throw new InvalidOperationException("Run outcome presenter requires the player life cycle and its panel.");
            }
        }
    }
}
