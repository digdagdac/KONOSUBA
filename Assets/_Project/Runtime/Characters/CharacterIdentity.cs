using System;
using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// One cast member: the name a tester can repeat, the personality line, and the
    /// attack habit that personality produces. Enemy entries also carry the
    /// <see cref="EnemyDefinition"/> they belong to so the runtime resolves an enemy
    /// to its identity through authored data instead of inspecting component types.
    /// </summary>
    [Serializable]
    public sealed class CharacterIdentity
    {
        [SerializeField] private CharacterRole role;
        [SerializeField] private string displayName;
        [SerializeField] private string ageLine;
        [SerializeField] private string roleLine;
        [SerializeField] private string habitLine;
        [SerializeField] private Color motifColor = Color.white;
        [SerializeField] private EnemyDefinition definition;
        [SerializeField] private CharacterPortraitSource portraitSource;
        [SerializeField] private Sprite representativePortrait;
        [SerializeField] private Sprite neutralPortrait;
        [SerializeField] private Sprite confidentPortrait;
        [SerializeField] private Sprite hurtPortrait;
        [SerializeField] private Sprite recoveryPortrait;

        public CharacterRole Role => role;

        /// <summary>The name shown to the player, for example <c>RIVELLA</c>.</summary>
        public string DisplayName => displayName;

        /// <summary>An age line for the human cast, empty for the non-human cast.</summary>
        public string AgeLine => ageLine;

        /// <summary>The one-line character concept, for example <c>ECLIPSE ARCHER</c>.</summary>
        public string RoleLine => roleLine;

        /// <summary>The habit that ties the personality to observable attack behaviour.</summary>
        public string HabitLine => habitLine;

        public Color MotifColor => motifColor;

        /// <summary>The enemy data this identity belongs to, or null for the player.</summary>
        public EnemyDefinition Definition => definition;

        public CharacterPortraitSource PortraitSource => portraitSource;

        /// <summary>True once every expression comes from a delivered cel portrait sheet.</summary>
        public bool HasCelPortraitSheet => portraitSource == CharacterPortraitSource.CelPortraitSheet;

        /// <summary>
        /// Resolves the sprite for one expression. Until a cel sheet is delivered every
        /// expression resolves to the same representative combat sprite, and the caller
        /// carries the expression difference in the card framing instead.
        /// </summary>
        public Sprite GetPortrait(CharacterExpression expression)
        {
            var portrait = expression switch
            {
                CharacterExpression.Neutral => neutralPortrait,
                CharacterExpression.Confident => confidentPortrait,
                CharacterExpression.Hurt => hurtPortrait,
                CharacterExpression.Recovery => recoveryPortrait,
                _ => throw new ArgumentOutOfRangeException(nameof(expression))
            };

            return portrait != null ? portrait : representativePortrait;
        }

        /// <summary>
        /// Fails closed on an identity that could mislead a tester or overstate the art
        /// that exists. Called by the catalog, never by gameplay code.
        /// </summary>
        public void Validate()
        {
            RequireText(displayName, nameof(displayName));
            RequireText(roleLine, nameof(roleLine));
            RequireText(habitLine, nameof(habitLine));

            if (role == CharacterRole.Player && definition != null)
            {
                throw new InvalidOperationException(
                    $"Character identity '{displayName}' is the player and must not reference enemy data.");
            }

            if (role != CharacterRole.Player && definition == null)
            {
                throw new InvalidOperationException(
                    $"Character identity '{displayName}' must reference the enemy definition it belongs to.");
            }

            if (Mathf.Abs(motifColor.a - 1f) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Character identity '{displayName}' requires an opaque motif colour.");
            }

            if (representativePortrait == null)
            {
                throw new InvalidOperationException(
                    $"Character identity '{displayName}' requires a representative portrait sprite.");
            }

            var celPanels =
                (neutralPortrait != null ? 1 : 0) +
                (confidentPortrait != null ? 1 : 0) +
                (hurtPortrait != null ? 1 : 0) +
                (recoveryPortrait != null ? 1 : 0);

            if (portraitSource == CharacterPortraitSource.CelPortraitSheet && celPanels != 4)
            {
                throw new InvalidOperationException(
                    $"Character identity '{displayName}' claims a cel portrait sheet but is missing expression panels.");
            }

            if (portraitSource == CharacterPortraitSource.RepresentativeCombatSprite && celPanels != 0)
            {
                throw new InvalidOperationException(
                    $"Character identity '{displayName}' carries cel panels but still declares a representative portrait source.");
            }
        }

        private void RequireText(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Character identity requires '{field}'.");
            }
        }
    }
}
