#if UNITY_EDITOR
using System.IO;
using Sundoll.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Sundoll.Editor
{
    public static class M3SceneSetup
    {
        private const string ScenePath = "Assets/Sundoll/Scenes/M3Workbench.unity";

        [MenuItem("Sundoll/M3/Create Workbench Scene")]
        public static void CreateWorkbenchScene()
        {
            var directory = Path.Combine(Application.dataPath, "Sundoll", "Scenes");
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("M3Workbench");
            root.AddComponent<M3WorkbenchRoot>();

            var cameraObject = new GameObject("WorkbenchCamera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;

            var gridObject = new GameObject("WorkbenchGrid");
            gridObject.transform.SetParent(root.transform, false);
            gridObject.AddComponent<Grid>();
            gridObject.AddComponent<M3WorkbenchMapProjection>();
            foreach (var layerId in new[] { "terrain", "wall", "object", "interaction", "static-annotation" })
            {
                var layerObject = new GameObject(layerId);
                layerObject.transform.SetParent(gridObject.transform, false);
                layerObject.AddComponent<Tilemap>();
                layerObject.AddComponent<TilemapRenderer>();
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene("Assets/Sundoll/Scenes/M1Bootstrap.unity", true)
            };
            AssetDatabase.SaveAssets();
            Selection.activeObject = root;
            Debug.Log("M3_WORKBENCH_SCENE_CREATED=" + ScenePath + "; CAMERA=" + camera.name);
        }
    }
}
#endif
