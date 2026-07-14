using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class RoomSequenceController : MonoBehaviour
    {
        [SerializeField] private ExitGate exitGate;
        [SerializeField] private string nextScene;

        private bool isSubscribed;
        private bool hasHandledEntry;

        public event Action Completed;

        public string NextScene => nextScene;
        public bool HasHandledEntry => hasHandledEntry;

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            if (exitGate == null)
            {
                throw new InvalidOperationException("RoomSequenceController requires an ExitGate.");
            }

            exitGate.Entered += HandleExitEntered;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (exitGate != null)
            {
                exitGate.Entered -= HandleExitEntered;
            }

            isSubscribed = false;
        }

        private void HandleExitEntered()
        {
            if (hasHandledEntry)
            {
                return;
            }

            hasHandledEntry = true;
            if (!string.IsNullOrWhiteSpace(nextScene))
            {
                SceneManager.LoadScene(nextScene, LoadSceneMode.Single);
                return;
            }

            Completed?.Invoke();
        }
    }
}
