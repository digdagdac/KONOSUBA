using System;
using UnityEngine;

namespace Overbless.Runtime
{
    /// <summary>
    /// The authored cast. This is design data: the runtime reads it and never writes
    /// to it, and the editor rebuilds it from the approved character direction.
    /// </summary>
    [CreateAssetMenu(menuName = "Overbless/Character Identity Catalog")]
    public sealed class CharacterIdentityCatalog : ScriptableObject
    {
        [SerializeField] private CharacterIdentity[] identities = Array.Empty<CharacterIdentity>();

        /// <summary>Number of authored identities. Exposed for contract checks.</summary>
        public int Count => identities.Length;

        public CharacterIdentity GetAt(int index)
        {
            if (index < 0 || index >= identities.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return identities[index];
        }

        public CharacterIdentity GetRequired(CharacterRole role)
        {
            var found = false;
            CharacterIdentity result = null;
            for (var index = 0; index < identities.Length; index++)
            {
                var identity = identities[index];
                if (identity == null || identity.Role != role)
                {
                    continue;
                }

                if (found)
                {
                    throw new InvalidOperationException($"Duplicate character identity for {role}.");
                }

                found = true;
                result = identity;
            }

            if (!found)
            {
                throw new InvalidOperationException($"Missing character identity for {role}.");
            }

            return result;
        }

        /// <summary>
        /// Resolves an enemy to its cast member through the enemy data it was built
        /// from. Returns false for an enemy the cast does not cover so a caller can
        /// stay silent instead of guessing a name.
        /// </summary>
        public bool TryGetByDefinition(EnemyDefinition definition, out CharacterIdentity identity)
        {
            identity = null;
            if (definition == null)
            {
                return false;
            }

            for (var index = 0; index < identities.Length; index++)
            {
                var candidate = identities[index];
                if (candidate == null || candidate.Definition != definition)
                {
                    continue;
                }

                if (identity != null)
                {
                    throw new InvalidOperationException(
                        $"Duplicate character identity for enemy definition '{definition.name}'.");
                }

                identity = candidate;
            }

            return identity != null;
        }

        /// <summary>
        /// Fails closed on a cast that could confuse a tester: a missing role, a
        /// repeated name, a repeated motif colour, or two members sharing enemy data.
        /// </summary>
        public void Validate()
        {
            if (identities.Length != 4)
            {
                throw new InvalidOperationException(
                    "Character identity catalog requires exactly four identities: player, dasher, archer and minion.");
            }

            var seenRoles = 0;
            for (var index = 0; index < identities.Length; index++)
            {
                var identity = identities[index];
                if (identity == null)
                {
                    throw new InvalidOperationException("Character identity catalog contains an empty entry.");
                }

                identity.Validate();
                var roleBit = 1 << (int)identity.Role;
                if ((seenRoles & roleBit) != 0)
                {
                    throw new InvalidOperationException($"Character identity catalog repeats role {identity.Role}.");
                }

                seenRoles |= roleBit;

                for (var other = index + 1; other < identities.Length; other++)
                {
                    var candidate = identities[other];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(identity.DisplayName, candidate.DisplayName, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Character identity catalog repeats the name '{identity.DisplayName}'.");
                    }

                    if (identity.MotifColor == candidate.MotifColor)
                    {
                        throw new InvalidOperationException(
                            $"Character identity catalog repeats a motif colour between '{identity.DisplayName}' and '{candidate.DisplayName}'.");
                    }

                    if (identity.Definition != null && identity.Definition == candidate.Definition)
                    {
                        throw new InvalidOperationException(
                            $"Character identity catalog binds two identities to enemy definition '{identity.Definition.name}'.");
                    }
                }
            }
        }
    }
}
