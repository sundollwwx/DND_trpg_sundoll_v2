using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Sundoll.EditorTools
{
    public static class M7BuildValidation
    {
        public const string OutputPath = "../Builds/SundollWorld-v03-M7-macOS-universal.app";

        [MenuItem("Sundoll/M7/Build macOS Universal")]
        public static void BuildMacOSUniversal()
        {
            var scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled build scenes are configured.");
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
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
                      "; size=" + report.summary.totalSize +
                      "; errors=" + report.summary.totalErrors +
                      "; warnings=" + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
            {
                throw new InvalidOperationException("M7 macOS universal build failed: " + report.summary.result);
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
