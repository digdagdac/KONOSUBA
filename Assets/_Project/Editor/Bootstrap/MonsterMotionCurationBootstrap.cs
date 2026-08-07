using UnityEditor;

namespace Overbless.Editor.Bootstrap
{
    /// <summary>
    /// Rebuilds only the monster animation assets after an approved frame-cycle curation.
    /// It deliberately does not rewrite scenes, prefabs, or project settings.
    /// </summary>
    public static class MonsterMotionCurationBootstrap
    {
        [MenuItem("Overbless/M1/Refresh Curated Monster Motion")]
        public static void RefreshCuratedMonsterMotion()
        {
            M1DirectionalAnimationBootstrap.CreateOrUpdate();
            AssetDatabase.SaveAssets();
        }
    }
}
