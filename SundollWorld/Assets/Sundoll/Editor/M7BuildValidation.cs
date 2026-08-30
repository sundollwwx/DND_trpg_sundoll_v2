using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sundoll.EditorTools
{
    public static class M7BuildValidation
    {
        public const string OutputPath = "../Builds/SundollWorld-v03-M7-macOS-universal.app";
        public const string WindowsOutputPath = "../Builds/SundollWorld-v03-M7-Windows-x64/SundollWorld.exe";
        public const string EditModeResultPath = "TestResults_EditMode_20260825_m5_hierarchy_context.xml";
        public const string PlayModeResultPath = "TestResults_PlayMode_20260825_m5_hierarchy_context.xml";

        [MenuItem("Sundoll/M7/Build macOS Universal")]
        public static void BuildMacOSUniversal()
        {
            var scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled build scenes are configured.");
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            var standaloneTarget = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Standalone);
            PlayerSettings.SetScriptingBackend(standaloneTarget, ScriptingImplementation.IL2CPP);
            var scriptingBackend = PlayerSettings.GetScriptingBackend(standaloneTarget);
            if (scriptingBackend != ScriptingImplementation.IL2CPP)
            {
                throw new InvalidOperationException(
                    "M7 macOS universal build requires IL2CPP, but the active backend is " + scriptingBackend + ".");
            }

            EditorUserBuildSettings.SetPlatformSettings(
                BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX),
                "Architecture",
                "x64arm64");
            var absoluteOutput = Path.GetFullPath(Path.Combine(Application.dataPath, OutputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = absoluteOutput,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            });

            Debug.Log("M7 macOS universal build result: " + report.summary.result +
                      "; output=" + absoluteOutput +
                      "; backend=" + scriptingBackend +
                      "; size=" + report.summary.totalSize +
                      "; errors=" + report.summary.totalErrors +
                      "; warnings=" + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("M7 macOS universal build failed: " + report.summary.result);
            }
        }

        [MenuItem("Sundoll/M7/Build Windows x64 IL2CPP")]
        public static void BuildWindows64Il2Cpp()
        {
            var scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled build scenes are configured.");
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);
            var standaloneTarget = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Standalone);
            PlayerSettings.SetScriptingBackend(standaloneTarget, ScriptingImplementation.IL2CPP);
            var scriptingBackend = PlayerSettings.GetScriptingBackend(standaloneTarget);
            if (scriptingBackend != ScriptingImplementation.IL2CPP)
            {
                throw new InvalidOperationException(
                    "M7 Windows x64 build requires IL2CPP, but the active backend is " + scriptingBackend + ".");
            }

            EditorUserBuildSettings.SetPlatformSettings(
                BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneWindows64),
                "Architecture",
                "x86_64");
            var absoluteOutput = Path.GetFullPath(Path.Combine(Application.dataPath, WindowsOutputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = absoluteOutput,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            Debug.Log("M7 Windows x64 IL2CPP build result: " + report.summary.result +
                      "; output=" + absoluteOutput +
                      "; backend=" + scriptingBackend +
                      "; size=" + report.summary.totalSize +
                      "; errors=" + report.summary.totalErrors +
                      "; warnings=" + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("M7 Windows x64 IL2CPP build failed: " + report.summary.result);
            }
        }

        [MenuItem("Sundoll/M7/Run EditMode Tests (Programmatic)")]
        public static void RunEditModeTestsProgrammatic()
        {
            RunTests(TestMode.EditMode, EditModeResultPath, true);
        }

        [MenuItem("Sundoll/M7/Run PlayMode Tests (Programmatic)")]
        public static void RunPlayModeTestsProgrammatic()
        {
            RunTests(TestMode.PlayMode, PlayModeResultPath, false);
        }

        private static void RunTests(TestMode testMode, string resultFileName, bool synchronous)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var resultPath = Path.Combine(Application.dataPath, "..", resultFileName);
            api.RegisterCallbacks(new TestCallbacks(resultPath));
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = testMode,
                assemblyNames = new[]
                {
                    testMode == TestMode.EditMode ? "Sundoll.Tests.EditMode" : "Sundoll.Tests.PlayMode"
                }
            })
            {
                runSynchronously = synchronous
            });
        }

        private sealed class TestCallbacks : ICallbacks
        {
            private readonly string resultPath;

            public TestCallbacks(string resultPath)
            {
                this.resultPath = resultPath;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("M7 programmatic test run started: " + testsToRun.TestCaseCount);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                TestRunnerApi.SaveResultToFile(result, resultPath);
                Debug.Log("M7 programmatic test run finished: " + result.TestStatus +
                          "; passed=" + result.PassCount + "; failed=" + result.FailCount +
                          "; skipped=" + result.SkipCount + "; result=" + resultPath);
                EditorApplication.Exit(result.TestStatus.ToString() == "Passed" ? 0 : 1);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus.ToString() == "Failed")
                {
                    Debug.LogError("M7 failed test: " + result.FullName + "\n" + result.Message);
                }
            }
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }
    }
}
