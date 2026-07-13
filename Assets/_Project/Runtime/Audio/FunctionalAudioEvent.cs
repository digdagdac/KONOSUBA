using System;

namespace Overbless.Runtime
{
    public enum FunctionalAudioEvent
    {
        DasherReady,
        ArcherReady,
        AttackLocked,
        PlayerHit,
        SoulCollected,
        ExitOpened
    }

    public readonly struct FunctionalAudioRecord
    {
        public FunctionalAudioRecord(FunctionalAudioEvent eventType, long token, int frame)
        {
            if (token <= 0) throw new ArgumentOutOfRangeException(nameof(token));
            EventType = eventType;
            Token = token;
            Frame = frame;
        }

        public FunctionalAudioEvent EventType { get; }
        public long Token { get; }
        public int Frame { get; }
    }
}
