using System;
using System.IO;
using Overbless.Runtime;
using UnityEditor;
using UnityEngine;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Rebuilds the character identity catalog from the approved character direction in
    /// <c>Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json</c>. The strings live here in one
    /// place so a test can compare the shipped catalog against the approval instead of
    /// trusting a hand-edited asset.
    /// </summary>
    /// <remarks>
    /// This is local unsealed implementation of approved M2 scope. It creates no
    /// <c>M2EntryGate</c> decision, and it deliberately omits Atra because golem runtime
    /// activation stays out of scope.
    /// </remarks>
    public static class M2CharacterIdentityBootstrap
    {
        public const string CatalogPath = "Assets/_Project/Data/Characters/CharacterIdentityCatalog.asset";

        private const string EnemyDataRoot = "Assets/_Project/Data/Enemies";
        private const string ProductionCharactersRoot = "Assets/_Project/Art/M1Production/Characters";

        /// <summary>
        /// One authored cast member. Kept as editor data so the catalog asset is
        /// reproducible from source instead of being edited by hand.
        /// </summary>
        private readonly struct IdentitySpec
        {
            public IdentitySpec(
                CharacterRole role,
                string displayName,
                string ageLine,
                string roleLine,
                string habitLine,
                Color motifColor,
                string enemyDefinitionAsset,
                string representativeSpriteAsset)
            {
                Role = role;
                DisplayName = displayName;
                AgeLine = ageLine;
                RoleLine = roleLine;
                HabitLine = habitLine;
                MotifColor = motifColor;
                EnemyDefinitionAsset = enemyDefinitionAsset;
                RepresentativeSpriteAsset = representativeSpriteAsset;
            }

            public CharacterRole Role { get; }
            public string DisplayName { get; }
            public string AgeLine { get; }
            public string RoleLine { get; }
            public string HabitLine { get; }
            public Color MotifColor { get; }
            public string EnemyDefinitionAsset { get; }
            public string RepresentativeSpriteAsset { get; }
        }

        /// <summary>
        /// The approved cast. Each habit line names behaviour a tester can watch happen,
        /// so the personality claim stays checkable against the attack it produces.
        /// </summary>
        private static readonly IdentitySpec[] Specs =
        {
            new IdentitySpec(
                CharacterRole.Player,
                "RIVELLA",
                "AGE 22",
                "CYNICAL FORMER SAINT",
                "SHE NEVER STRIKES. SHE OVERBLESSES THEM UNTIL THEY RUIN EACH OTHER.",
                new Color32(64, 214, 236, 255),
                null,
                "chr_player_idle_south_a_v001.png"),
            new IdentitySpec(
                CharacterRole.Dasher,
                "VERA",
                "AGE 24",
                "RED-LIGHTNING CHARGE KNIGHT",
                "SHE ONLY ACCELERATES IN STRAIGHT LINES AND REFUSES TO STEER AFTER THE LOCK.",
                new Color32(255, 96, 84, 255),
                "Enemy_Dasher.asset",
                "chr_dasher_idle_south_a_v001.png"),
            new IdentitySpec(
                CharacterRole.Archer,
                "LUME",
                "AGE 23",
                "ECLIPSE ARCHER",
                "SHE STAYS CALM AT RANGE AND KEEPS FIRING DOWN THE LANE SHE ALREADY COMMITTED TO.",
                new Color32(179, 121, 255, 255),
                "Enemy_Archer.asset",
                "chr_archer_idle_south_a_v001.png"),
            new IdentitySpec(
                CharacterRole.Minion,
                "MOKO",
                string.Empty,
                "CURSED-DOLL SWARM",
                "IT COPIES WHOEVER IS CLOSEST AND SWINGS THE MOMENT ITS SHORT WARNING ENDS.",
                new Color32(163, 230, 53, 255),
                "Enemy_Minion.asset",
                "chr_minion_idle_south_a_v001.png")
        };

        [MenuItem("Overbless/M2 Assets/Rebuild Character Identity Catalog (Local Unsealed QA)")]
        public static void CreateOrUpdate()
        {
            CreateOrUpdateCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Rebuilt the character identity catalog for local unsealed technical QA. No M2 entry-gate decision is implied.");
        }

        public static void CreateOrUpdateForBatchMode()
        {
            CreateOrUpdate();
        }

        /// <summary>Creates or rewrites the catalog asset and returns it.</summary>
        public static CharacterIdentityCatalog CreateOrUpdateCatalog()
        {
            var directory = Path.GetDirectoryName(CatalogPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<CharacterIdentityCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CharacterIdentityCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            var identities = serialized.FindProperty("identities");
            if (identities == null)
            {
                throw new InvalidOperationException("Character identity catalog is missing its identities array.");
            }

            identities.arraySize = Specs.Length;
            for (var index = 0; index < Specs.Length; index++)
            {
                WriteIdentity(identities.GetArrayElementAtIndex(index), Specs[index]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.ImportAsset(CatalogPath, ImportAssetOptions.ForceSynchronousImport);

            var persisted = AssetDatabase.LoadAssetAtPath<CharacterIdentityCatalog>(CatalogPath);
            if (persisted == null)
            {
                throw new InvalidOperationException($"Character identity catalog did not persist to '{CatalogPath}'.");
            }

            persisted.Validate();
            return persisted;
        }

        private static void WriteIdentity(SerializedProperty element, IdentitySpec spec)
        {
            SetEnum(element, "role", (int)spec.Role);
            SetString(element, "displayName", spec.DisplayName);
            SetString(element, "ageLine", spec.AgeLine);
            SetString(element, "roleLine", spec.RoleLine);
            SetString(element, "habitLine", spec.HabitLine);
            SetColor(element, "motifColor", spec.MotifColor);
            SetEnum(element, "portraitSource", (int)CharacterPortraitSource.RepresentativeCombatSprite);
            SetObject(
                element,
                "definition",
                spec.EnemyDefinitionAsset == null
                    ? null
                    : RequireAsset<EnemyDefinition>(EnemyDataRoot + "/" + spec.EnemyDefinitionAsset));
            SetObject(
                element,
                "representativePortrait",
                RequireAsset<Sprite>(ProductionCharactersRoot + "/" + spec.RepresentativeSpriteAsset));

            // Cel portrait sheets are not produced yet. The panels stay empty and the
            // portrait source stays representative so no record can claim final art.
            SetObject(element, "neutralPortrait", null);
            SetObject(element, "confidentPortrait", null);
            SetObject(element, "hurtPortrait", null);
            SetObject(element, "recoveryPortrait", null);
        }

        private static SerializedProperty RequireField(SerializedProperty element, string field)
        {
            var property = element.FindPropertyRelative(field);
            if (property == null)
            {
                throw new InvalidOperationException($"Character identity is missing serialized field '{field}'.");
            }

            return property;
        }

        private static void SetString(SerializedProperty element, string field, string value)
        {
            RequireField(element, field).stringValue = value;
        }

        private static void SetEnum(SerializedProperty element, string field, int value)
        {
            RequireField(element, field).enumValueIndex = value;
        }

        private static void SetColor(SerializedProperty element, string field, Color value)
        {
            RequireField(element, field).colorValue = value;
        }

        private static void SetObject(SerializedProperty element, string field, UnityEngine.Object value)
        {
            RequireField(element, field).objectReferenceValue = value;
        }

        private static T RequireAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Character identity catalog requires '{assetPath}' to exist as {typeof(T).Name}.");
            }

            return asset;
        }
    }
}
