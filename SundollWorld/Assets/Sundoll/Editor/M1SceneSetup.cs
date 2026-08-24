#if UNITY_EDITOR
using System.IO;
using Sundoll.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sundoll.Editor
{
    public static class M1SceneSetup
    {
        private const string ScenePath = "Assets/Sundoll/Scenes/M1Bootstrap.unity";

        [MenuItem("Sundoll/M1/Create Bootstrap Scene")]
        private static void CreateBootstrapScene()
        {
            var directory = Path.Combine(Application.dataPath, "Sundoll", "Scenes");
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootstrapObject = new GameObject("M1Bootstrap");
            bootstrapObject.AddComponent<M1Bootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            AssetDatabase.SaveAssets();
            Selection.activeObject = bootstrapObject;
            Debug.Log($"M1_BOOTSTRAP_SCENE_CREATED={ScenePath}");
        }
    }
}
#endif
