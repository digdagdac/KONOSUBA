using System;
using UnityEngine;

namespace Overbless.Runtime
{
    [CreateAssetMenu(menuName = "Overbless/Functional Audio Catalog")]
    public sealed class FunctionalAudioCatalog : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public FunctionalAudioEvent eventType;
            public AudioClip clip;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public AudioClip GetRequired(FunctionalAudioEvent eventType)
        {
            var found = false;
            AudioClip result = null;
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].eventType != eventType)
                {
                    continue;
                }

                if (found)
                {
                    throw new InvalidOperationException($"Duplicate audio mapping for {eventType}.");
                }

                found = true;
                result = entries[i].clip;
            }

            if (!found)
            {
                throw new InvalidOperationException($"Missing audio mapping for {eventType}.");
            }

            if (result == null)
            {
                throw new InvalidOperationException($"Audio mapping for {eventType} has no clip.");
            }

            return result;
        }
    }
}
