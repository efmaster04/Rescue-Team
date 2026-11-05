using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SelVadisi.EditorBuild
{
    /// Opens the big flood map once, right after it has been copied into the project.
    [InitializeOnLoad]
    static class SVAutoOpen
    {
        const string Flag = "WorldSource/SelVadisi_AutoOpen.flag";

        static SVAutoOpen()
        {
            if (Application.isBatchMode || SVBuild.InHostGame || !File.Exists(Flag)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Flag)) return;
                File.Delete(Flag);
                if (!File.Exists(SVBuild.ScenePath)) return;
                var list = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(SVBuild.ScenePath, true) };
                foreach (var s in EditorBuildSettings.scenes) if (s.path != SVBuild.ScenePath) list.Add(s);
                EditorBuildSettings.scenes = list.ToArray();
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(SVBuild.ScenePath);
            };
        }
    }
}
