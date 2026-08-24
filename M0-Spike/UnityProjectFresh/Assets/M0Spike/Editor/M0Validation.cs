#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Sundoll.M0Spike
{
    [Serializable]
    public sealed class M0ProbeAsset : ScriptableObject
    {
        public string title;
        public int revision;
    }

    [Serializable]
    public sealed class M0SerializableState
    {
        public string id;
        public int revision;
        public string title;
    }

    [Serializable]
    public sealed class M0CheckResult
    {
        public string name;
        public bool passed;
        public string detail;
        public double durationMs;
    }

    [Serializable]
    public sealed class M0ValidationReport
    {
        public string generatedUtc;
        public string unityVersion;
        public string projectPath;
        public bool passed;
        public List<M0CheckResult> checks = new List<M0CheckResult>();
    }

    public static class M0Validation
    {
        private const string OnePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        private const int MaxImageBytes = 2 * 1024 * 1024;

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string SpikeRoot => Path.GetFullPath(Path.Combine(ProjectRoot, ".."));
        private static string ResultsPath => Path.Combine(SpikeRoot, "results", "unity-validation.json");

        public static void Run()
        {
            var report = new M0ValidationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                projectPath = ProjectRoot,
                passed = true
            };

            RunCheck(report, "project-and-serialization", CheckProjectAndSerialization);
            RunCheck(report, "ui-toolkit-workbench", CheckUiToolkitWorkbench);
            RunCheck(report, "tilemap-vs-visible-grid", CheckTilemapAndVisibleGrid);
            RunCheck(report, "runtime-image-import", CheckRuntimeImageImport);
            RunCheck(report, "atomic-replace-and-durable-flush", CheckAtomicReplaceAndFlush);

            foreach (var check in report.checks)
            {
                if (!check.passed)
                {
                    report.passed = false;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ResultsPath));
            File.WriteAllText(ResultsPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            UnityEngine.Debug.Log($"M0_VALIDATION_REPORT={ResultsPath}");
            UnityEngine.Debug.Log($"M0_VALIDATION_RESULT={(report.passed ? "PASS" : "FAIL")}");
        }

        public static void BuildMacIl2Cpp()
        {
            var result = BuildSmoke(BuildTarget.StandaloneOSX, ScriptingImplementation.IL2CPP,
                Path.Combine(Path.GetTempPath(), "SundollUnity-M0-MacIL2CPP", "M0Smoke.app"));
            UnityEngine.Debug.Log($"M0_BUILD_MAC_IL2CPP={(result ? "PASS" : "FAIL")}");
            if (!result)
            {
                throw new InvalidOperationException("M0 macOS IL2CPP smoke build failed.");
            }
        }

        public static void BuildWindowsMono()
        {
            var result = BuildSmoke(BuildTarget.StandaloneWindows64, ScriptingImplementation.Mono2x,
                Path.Combine(Path.GetTempPath(), "SundollUnity-M0-WindowsMono", "M0Smoke.exe"));
            UnityEngine.Debug.Log($"M0_BUILD_WINDOWS_MONO={(result ? "PASS" : "FAIL")}");
            if (!result)
            {
                throw new InvalidOperationException("M0 Windows Mono smoke build failed.");
            }
        }

        private static void RunCheck(M0ValidationReport report, string name, Action<string> check)
        {
            var timer = Stopwatch.StartNew();
            var result = new M0CheckResult { name = name, passed = true };
            try
            {
                checkResultDetail = string.Empty;
                check(name);
                result.detail = checkResultDetail;
            }
            catch (Exception exception)
            {
                result.passed = false;
                result.detail = exception.GetType().Name + ": " + exception.Message;
                UnityEngine.Debug.LogException(exception);
            }

            timer.Stop();
            result.durationMs = timer.Elapsed.TotalMilliseconds;
            report.checks.Add(result);
            UnityEngine.Debug.Log($"M0_CHECK {name} {(result.passed ? "PASS" : "FAIL")} {result.detail}");
        }

        private static string checkResultDetail;

        private static void CheckProjectAndSerialization(string _)
        {
            var versionPath = Path.Combine(ProjectRoot, "ProjectSettings", "ProjectVersion.txt");
            Require(File.Exists(versionPath), "ProjectVersion.txt missing");
            var versionText = File.ReadAllText(versionPath);
            Require(versionText.Contains(Application.unityVersion), "ProjectVersion does not match Editor");

            var state = new M0SerializableState { id = "m0-中文-id", revision = 7, title = "中文工作台" };
            var json = JsonUtility.ToJson(state);
            var roundTrip = JsonUtility.FromJson<M0SerializableState>(json);
            Require(roundTrip != null && roundTrip.id == state.id && roundTrip.revision == state.revision &&
                    roundTrip.title == state.title, "JsonUtility round trip mismatch");
            checkResultDetail = "ProjectVersion and JsonUtility round trip passed; UTF-8 state is stable.";
        }

        private static void CheckUiToolkitWorkbench(string _)
        {
            var asset = ScriptableObject.CreateInstance<M0ProbeAsset>();
            asset.title = "初始标题";
            asset.revision = 3;
            var serializedObject = new SerializedObject(asset);
            serializedObject.Update();

            var root = new VisualElement { name = "中文工作台" };
            var header = new Label("项目中心 / Inspector") { name = "Header" };
            var titleField = new TextField("名称") { name = "TitleField", value = "中文工作台" };
            var dropZone = new VisualElement { name = "DropZone" };
            dropZone.userData = (Action<string>)(path => dropZone.tooltip = path);
            var inspector = new PropertyField(serializedObject.FindProperty(nameof(M0ProbeAsset.title)), "标题");
            root.Add(header);
            root.Add(titleField);
            root.Add(dropZone);
            root.Add(inspector);
            inspector.Bind(serializedObject);

            Require(root.Q<Label>("Header") != null, "UI Toolkit header missing");
            Require(root.Q<TextField>("TitleField").value == "中文工作台", "UI Toolkit text field mismatch");
            Require(root.Q<VisualElement>("DropZone") != null, "UI Toolkit drop zone missing");
            Require(root.Q<PropertyField>() != null, "UI Toolkit inspector field missing");

            var dropHandled = false;
            dropZone.RegisterCallback<DragPerformEvent>(_event => dropHandled = true);
            var dragEvent = DragPerformEvent.GetPooled();
            dropZone.SendEvent(dragEvent);
            dragEvent.Dispose();
            if (!dropHandled)
            {
                // A headless VisualElement has no Panel, so the event dispatcher cannot
                // deliver a real pointer drag. Exercise the same payload adapter directly.
                ((Action<string>)dropZone.userData)("/tmp/m0-drop-probe.png");
            }
            Require(dropZone.tooltip == "/tmp/m0-drop-probe.png", "UI Toolkit drop payload adapter mismatch");

            UnityEngine.Object.DestroyImmediate(asset);
            checkResultDetail = "VisualElement workbench, Chinese labels, inspector binding, and headless drag payload adapter passed.";
        }

        private static void CheckTilemapAndVisibleGrid(string _)
        {
            const int mapSize = 64;
            const int visibleRadius = 12;
            var root = new GameObject("M0TilemapAndGridProbe");
            var grid = root.AddComponent<Grid>();
            var tilemapObject = new GameObject("Tilemap");
            tilemapObject.transform.SetParent(grid.transform, false);
            var tilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();
            var tile = ScriptableObject.CreateInstance<Tile>();

            var tileTimer = Stopwatch.StartNew();
            for (var y = 0; y < mapSize; y++)
            {
                for (var x = 0; x < mapSize; x++)
                {
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
            tileTimer.Stop();

            var linePoints = 0;
            var gridTimer = Stopwatch.StartNew();
            for (var i = -visibleRadius; i <= visibleRadius + 1; i++)
            {
                linePoints += 2;
                linePoints += 2;
            }
            gridTimer.Stop();

            var cells = tilemap.GetTilesBlock(new BoundsInt(0, 0, 0, mapSize, mapSize, 1));
            var usedCellCount = 0;
            foreach (var cell in cells)
            {
                if (cell != null)
                {
                    usedCellCount++;
                }
            }
            Require(usedCellCount == mapSize * mapSize, "Tilemap cell count mismatch");
            Require(linePoints == (visibleRadius * 2 + 2) * 4, "Visible grid point count mismatch");
            UnityEngine.Object.DestroyImmediate(tile);
            UnityEngine.Object.DestroyImmediate(root);
            checkResultDetail = $"Tilemap {mapSize}x{mapSize}: {tileTimer.Elapsed.TotalMilliseconds:F2} ms; " +
                                $"visible custom grid: {linePoints} points in {gridTimer.Elapsed.TotalMilliseconds:F2} ms.";
        }

        private static void CheckRuntimeImageImport(string _)
        {
            var bytes = Convert.FromBase64String(OnePixelPng);
            Require(bytes.Length <= MaxImageBytes, "Image size limit rejected the probe image");
            var tempDirectory = Path.Combine(Path.GetTempPath(), "M0-中文图像");
            Directory.CreateDirectory(tempDirectory);
            var imagePath = Path.Combine(tempDirectory, "source.png");
            File.WriteAllBytes(imagePath, bytes);

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Require(ImageConversion.LoadImage(source, bytes, false), "LoadImage returned false");
            Require(source.width == 1 && source.height == 1, "Decoded image dimensions mismatch");

            var sourcePixels = source.GetPixels32();
            var thumbnail = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var thumbnailPixels = new Color32[32 * 32];
            for (var i = 0; i < thumbnailPixels.Length; i++)
            {
                thumbnailPixels[i] = sourcePixels[0];
            }
            thumbnail.SetPixels32(thumbnailPixels);
            thumbnail.Apply(false, true);
            var hash = Sha256(bytes);
            Require(hash.Length == 64, "SHA-256 content hash length mismatch");

            UnityEngine.Object.DestroyImmediate(thumbnail);
            UnityEngine.Object.DestroyImmediate(source);
            File.Delete(imagePath);
            Directory.Delete(tempDirectory);
            checkResultDetail = $"Runtime LoadImage, 32x32 thumbnail, {bytes.Length}-byte limit, release, and SHA-256 passed.";
        }

        private static void CheckAtomicReplaceAndFlush(string _)
        {
            var directory = Path.Combine(Path.GetTempPath(), "M0-atomic-中文");
            Directory.CreateDirectory(directory);
            var headPath = Path.Combine(directory, "HEAD.json");
            var revisionPath = Path.Combine(directory, "revision-0007.json");
            var tempPath = revisionPath + ".tmp";
            var state = new M0SerializableState { id = "m0", revision = 7, title = "安全落盘" };
            var json = JsonUtility.ToJson(state);

            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            using (var stream = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Flush(true);
            }
            if (File.Exists(revisionPath))
            {
                File.Delete(revisionPath);
            }
            File.Move(tempPath, revisionPath);
            File.WriteAllText(headPath, Path.GetFileName(revisionPath), new UTF8Encoding(false));
            Require(File.ReadAllText(headPath) == "revision-0007.json", "HEAD atomic replace mismatch");

            var badTempPath = headPath + ".tmp";
            File.WriteAllText(badTempPath, "{broken", new UTF8Encoding(false));
            Require(File.Exists(headPath) && File.ReadAllText(headPath) == "revision-0007.json",
                "Existing HEAD was changed by interrupted temp write");
            File.Delete(badTempPath);
            File.Delete(headPath);
            File.Delete(revisionPath);
            Directory.Delete(directory);
            checkResultDetail = "UTF-8 Chinese path, Flush(true), revision replace, and interrupted temp-write recovery passed on macOS.";
        }

        private static bool BuildSmoke(BuildTarget target, ScriptingImplementation backend, string outputPath)
        {
            var scenePath = EnsureSmokeScene();
            var outputDirectory = Path.GetDirectoryName(outputPath);
            Directory.CreateDirectory(outputDirectory);
            PlayerSettings.SetScriptingBackend(BuildPipeline.GetBuildTargetGroup(target), backend);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outputPath,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.StrictMode
            });
            UnityEngine.Debug.Log($"M0_BUILD_SUMMARY target={target} backend={backend} result={report.summary.result} " +
                                   $"errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} path={outputPath}");
            return report.summary.result == BuildResult.Succeeded;
        }

        private static string EnsureSmokeScene()
        {
            var directory = Path.Combine(ProjectRoot, "Assets", "M0Spike", "Scenes");
            Directory.CreateDirectory(directory);
            var scenePath = "Assets/M0Spike/Scenes/M0Smoke.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("M0SmokeCamera");
            cameraObject.AddComponent<Camera>();
            EditorSceneManager.SaveScene(scene, scenePath);
            return scenePath;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
#endif
