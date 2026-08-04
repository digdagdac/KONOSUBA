namespace Overbless.Runtime
{
    /// <summary>
    /// The approved cast slots. Each slot maps one original character onto one
    /// gameplay archetype so a tester can connect a personality to the attack
    /// habit it produces.
    /// </summary>
    /// <remarks>
    /// Atra, the ancient guardian in the approved character direction, is absent
    /// on purpose. Golem runtime activation stays outside the current scope, so
    /// shipping an identity for it would activate an excluded actor.
    /// </remarks>
    public enum CharacterRole
    {
        Player,
        Dasher,
        Archer,
        Minion
    }

    /// <summary>
    /// The four approved portrait expressions. The runtime never invents a fifth
    /// state, so a delivered cel sheet maps one panel per member.
    /// </summary>
    public enum CharacterExpression
    {
        Neutral,
        Confident,
        Hurt,
        Recovery
    }

    /// <summary>
    /// Declares which art a character currently presents with. The catalog has to
    /// say this out loud: a representative combat sprite must never be recorded or
    /// reviewed as if the final cel portrait had been delivered.
    /// </summary>
    public enum CharacterPortraitSource
    {
        /// <summary>The authoritative pixel combat sprite stands in for the portrait.</summary>
        RepresentativeCombatSprite,

        /// <summary>All four expressions come from a delivered cel portrait sheet.</summary>
        CelPortraitSheet
    }
}
