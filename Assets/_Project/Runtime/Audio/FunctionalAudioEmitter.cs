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
        private bool webStarted;

        public event Action<FunctionalAudioRecord> Emitted;
        public bool WebStarted => webStarted;

        private void Update()
        {
            while (pending.Count > 0)
            {
                var emission = pending[0];
                if (emission.Clip.loadState == AudioDataLoadState.Failed)
                {
                    pending.RemoveAt(0);
                    pendingKeys.Remove(emission.Key);
                    throw new InvalidOperationException("Functional audio clip failed to load: " + emission.Clip.name + ".");
                }

                if (emission.Clip.loadState != AudioDataLoadState.Loaded)
                {
                    return;
                }

                pending.RemoveAt(0);
                pendingKeys.Remove(emission.Key);
                Play(emission.EventType, emission.Token, emission.Clip);
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

            if (pending.Count > 0 || clip.loadState != AudioDataLoadState.Loaded)
            {

                pendingKeys.Add(key);
                pending.Add(new PendingEmission(eventType, token, clip));
                return true;
            }

            Play(eventType, token, clip);
            return true;
        }

        public void ResetEmitter()
        {
            emitted.Clear();
            pendingKeys.Clear();
            pending.Clear();
            audioSource.Stop();
        }

        private void Play(FunctionalAudioEvent eventType, long token, AudioClip clip)
        {
            var key = (eventType, token);
            try
            {
                audioSource.PlayOneShot(clip);
                emitted.Add(key);
            }
            catch
            {
                emitted.Remove(key);
                throw;
            }

            Emitted?.Invoke(new FunctionalAudioRecord(eventType, token, Time.frameCount));
        }

        private readonly struct PendingEmission
        {
            public PendingEmission(FunctionalAudioEvent eventType, long token, AudioClip clip)
            {
                EventType = eventType;
                Token = token;
                Clip = clip;
            }

            public FunctionalAudioEvent EventType { get; }
            public long Token { get; }
            public AudioClip Clip { get; }
            public (FunctionalAudioEvent EventType, long Token) Key => (EventType, Token);
        }
    }
}
