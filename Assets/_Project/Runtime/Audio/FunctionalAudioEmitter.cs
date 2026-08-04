using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overbless.Runtime
{
    [DisallowMultipleComponent]
    public sealed class FunctionalAudioEmitter : MonoBehaviour
    {
        [SerializeField] private FunctionalAudioCatalog catalog;
        [SerializeField] private AudioSource audioSource;

        private readonly HashSet<(FunctionalAudioEvent EventType, long Token)> emitted =
            new HashSet<(FunctionalAudioEvent EventType, long Token)>();
        private readonly HashSet<(FunctionalAudioEvent EventType, long Token)> pendingKeys =
            new HashSet<(FunctionalAudioEvent EventType, long Token)>();
        private readonly List<PendingEmission> pending = new List<PendingEmission>();
        private readonly List<PendingEmission> accepted = new List<PendingEmission>();
        private readonly Queue<PendingNotification> notifications =
            new Queue<PendingNotification>();
        private bool isDraining;
        private long resetGeneration;
        private bool webStarted;

        public event Action<FunctionalAudioRecord> Emitted;
        public bool WebStarted => webStarted;

        private void Update()
        {
            if (!webStarted)
            {
                return;
            }

            while (pending.Count > 0)
            {
                var emission = pending[0];
                switch (emission.Clip.loadState)
                {
                    case AudioDataLoadState.Loaded:
                        pending.RemoveAt(0);
                        accepted.Add(emission);
                        DrainAcceptedEmissions();
                        continue;

                    case AudioDataLoadState.Loading:
                        return;

                    case AudioDataLoadState.Unloaded:
                        StartPendingLoad(emission);
                        return;

                    case AudioDataLoadState.Failed:
                        FailPendingEmission(emission, "the clip failed to load.", null);
                        continue;

                    default:
                        FailPendingEmission(
                            emission,
                            "the clip entered an unsupported load state.",
                            null);
                        continue;
                }
            }
        }

        public void SetWebStarted()
        {
            if (webStarted)
            {
                return;
            }

            if (catalog == null || audioSource == null)
            {
                throw new InvalidOperationException("Functional audio requires a catalog and source.");
            }

            foreach (FunctionalAudioEvent eventType in Enum.GetValues(typeof(FunctionalAudioEvent)))
            {
                var clip = catalog.GetRequired(eventType);
                if (clip.loadState == AudioDataLoadState.Failed)
                {
                    throw new InvalidOperationException("Functional audio clip failed to load: " + clip.name + ".");
                }
            }

            webStarted = true;
        }

        public bool Emit(FunctionalAudioEvent eventType, long token)
        {
            if (!webStarted)
            {
                return false;
            }

            if (token <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(token));
            }

            var key = (eventType, token);
            if (emitted.Contains(key) || pendingKeys.Contains(key))
            {
                return false;
            }

            var clip = catalog.GetRequired(eventType);
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                throw new InvalidOperationException("Functional audio clip failed to load: " + clip.name + ".");
            }

            pendingKeys.Add(key);
            var emission = new PendingEmission(eventType, token, clip);
            if (pending.Count > 0 || clip.loadState != AudioDataLoadState.Loaded)
            {
                pending.Add(emission);
                return true;
            }

            accepted.Add(emission);
            DrainAcceptedEmissions();
            return true;
        }

        public void ResetEmitter()
        {
            if (resetGeneration == long.MaxValue)
            {
                throw new InvalidOperationException(
                    "Functional audio reset generation is exhausted.");
            }

            resetGeneration++;
            emitted.Clear();
            pendingKeys.Clear();
            pending.Clear();
            accepted.Clear();
            notifications.Clear();
            audioSource.Stop();
        }

        private void Play(PendingEmission emission)
        {
            var key = emission.Key;
            try
            {
                audioSource.PlayOneShot(emission.Clip);
                emitted.Add(key);
                pendingKeys.Remove(key);
            }
            catch
            {
                emitted.Remove(key);
                pendingKeys.Remove(key);
                throw;
            }

            notifications.Enqueue(
                new PendingNotification(
                    new FunctionalAudioRecord(
                        emission.EventType,
                        emission.Token,
                        Time.frameCount),
                    resetGeneration));
        }

        private void DrainAcceptedEmissions()
        {
            if (isDraining)
            {
                return;
            }

            isDraining = true;
            try
            {
                while (notifications.Count > 0 || accepted.Count > 0)
                {
                    if (notifications.Count > 0)
                    {
                        NotifyEmitted(notifications.Dequeue());
                        continue;
                    }

                    var emission = accepted[0];
                    accepted.RemoveAt(0);
                    Play(emission);
                }
            }
            finally
            {
                isDraining = false;
            }
        }

        private void StartPendingLoad(PendingEmission emission)
        {
            if (emission.LoadRequested)
            {
                FailPendingEmission(
                    emission,
                    "the clip remained unloaded after loading was requested.",
                    null);
                return;
            }

            bool loadStarted;
            try
            {
                loadStarted = emission.Clip.LoadAudioData();
            }
            catch (Exception exception)
            {
                FailPendingEmission(
                    emission,
                    "LoadAudioData threw before loading could start.",
                    exception);
                return;
            }

            if (!loadStarted)
            {
                FailPendingEmission(
                    emission,
                    "LoadAudioData could not start loading.",
                    null);
                return;
            }

            pending[0] = emission.WithLoadRequested();
        }

        private void FailPendingEmission(
            PendingEmission emission,
            string reason,
            Exception exception)
        {
            pending.RemoveAt(0);
            pendingKeys.Remove(emission.Key);
            Debug.LogError(
                "Functional audio cue " + emission.EventType +
                " was removed because " + reason +
                " Clip: " + emission.Clip.name + ".",
                this);

            if (exception != null)
            {
                Debug.LogException(exception, this);
            }
        }

        private void NotifyEmitted(PendingNotification notification)
        {
            var observers = Emitted;
            if (observers == null)
            {
                return;
            }

            foreach (Action<FunctionalAudioRecord> observer in observers.GetInvocationList())
            {
                if (notification.ResetGeneration != resetGeneration)
                {
                    return;
                }

                try
                {
                    observer(notification.Record);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private readonly struct PendingNotification
        {
            public PendingNotification(
                FunctionalAudioRecord record,
                long resetGeneration)
            {
                Record = record;
                ResetGeneration = resetGeneration;
            }

            public FunctionalAudioRecord Record { get; }
            public long ResetGeneration { get; }
        }
        private readonly struct PendingEmission
        {
            public PendingEmission(
                FunctionalAudioEvent eventType,
                long token,
                AudioClip clip,
                bool loadRequested = false)
            {
                EventType = eventType;
                Token = token;
                Clip = clip;
                LoadRequested = loadRequested;
            }

            public FunctionalAudioEvent EventType { get; }
            public long Token { get; }
            public AudioClip Clip { get; }
            public bool LoadRequested { get; }
            public (FunctionalAudioEvent EventType, long Token) Key => (EventType, Token);

            public PendingEmission WithLoadRequested()
            {
                return new PendingEmission(EventType, Token, Clip, true);
            }
        }
    }
}
