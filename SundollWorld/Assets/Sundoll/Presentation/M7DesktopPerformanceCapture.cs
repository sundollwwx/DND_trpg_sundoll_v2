using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Opt-in desktop Player performance capture for the M7 release gate.
    /// It is activated only with -sundoll-m7-perf and uses an isolated runtime
    /// project so a measurement cannot modify a user's normal workspace.
    /// </summary>
    public sealed class M7DesktopPerformanceCapture : MonoBehaviour
    {
        public const string CommandLineArgument = "-sundoll-m7-perf";

        private const int DefaultWarmupFrames = 120;
        private const int DefaultSampleFrames = 900;
        private const int PieceCount = 1000;
        private const float TargetFrameMilliseconds = 1000f / 60f;
        private const long TargetAllocationBytes = 1024;
        private const int MeasurementTargetFrameRate = -1;

        private readonly List<float> frameMilliseconds = new List<float>(DefaultSampleFrames);
        private readonly List<long> frameAllocations = new List<long>(DefaultSampleFrames);
        private M3WorkbenchRoot workbench;
        private Camera workbenchCamera;
        private int warmupFrames;
        private int sampleFrames;
        private int targetFrameRate;
        private string outputPath;

        public static bool IsRequested()
        {
            var arguments = Environment.GetCommandLineArgs();
            foreach (var argument in arguments)
            {
                if (string.Equals(argument, CommandLineArgument, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public void Begin(M3WorkbenchRoot nextWorkbench)
        {
            workbench = nextWorkbench ?? throw new ArgumentNullException(nameof(nextWorkbench));
            warmupFrames = ReadPositiveIntArgument("-sundoll-m7-perf-warmup", DefaultWarmupFrames);
            sampleFrames = ReadPositiveIntArgument("-sundoll-m7-perf-frames", DefaultSampleFrames);
            targetFrameRate = ReadIntegerArgument(
                "-sundoll-m7-perf-target-fps",
                MeasurementTargetFrameRate);
            outputPath = ReadStringArgument(
                "-sundoll-m7-perf-output",
                Path.Combine(UnityEngine.Application.temporaryCachePath, "SundollWorld-M7DesktopPerformance.json"));
            StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            yield return null;

            var preparationFailure = TryPrepareScenario();
            if (preparationFailure != null)
            {
                CompleteFailure(preparationFailure);
                yield break;
            }

            yield return null;

            for (var index = 0; index < warmupFrames; index++)
            {
                AnimateCamera(index);
                yield return null;
            }

            frameMilliseconds.Clear();
            frameAllocations.Clear();
            frameMilliseconds.Capacity = Math.Max(frameMilliseconds.Capacity, sampleFrames);
            frameAllocations.Capacity = Math.Max(frameAllocations.Capacity, sampleFrames);

            for (var index = 0; index < sampleFrames; index++)
            {
                var startTime = Time.realtimeSinceStartup;
                var startAllocation = GC.GetAllocatedBytesForCurrentThread();
                AnimateCamera(index + warmupFrames);
                yield return null;
                var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                var allocation = Math.Max(0L, GC.GetAllocatedBytesForCurrentThread() - startAllocation);
                frameMilliseconds.Add(elapsed);
                frameAllocations.Add(allocation);
            }

            var captureFailure = TryWriteResult(out var result);
            if (captureFailure != null)
            {
                CompleteFailure(captureFailure);
                yield break;
            }

            Debug.Log(
                "M7 desktop performance capture completed: output=" + outputPath +
                "; window=" + result.width + "x" + result.height +
                "; frame p95=" + result.frameP95Milliseconds.ToString("0.000") + "ms" +
                "; allocation p95=" + result.allocationP95Bytes + "B" +
                "; over-budget=" + result.framesOverBudget + "/" + result.sampleFrames);
            UnityEngine.Application.Quit(0);
        }

        private Exception TryPrepareScenario()
        {
            try
            {
                PrepareScenario();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private Exception TryWriteResult(out DesktopPerformanceResult result)
        {
            result = null;
            try
            {
                result = BuildResult();
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, JsonUtility.ToJson(result, true));
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private void CompleteFailure(Exception exception)
        {
            WriteFailure(exception);
            Debug.LogException(exception);
            UnityEngine.Application.Quit(1);
        }

        private void PrepareScenario()
        {
            if (workbench.CommandBusForDiagnostics == null ||
                workbench.PieceLibraryForDiagnostics == null ||
                workbench.PieceProjectionForDiagnostics == null)
            {
                throw new InvalidOperationException("M7 desktop performance capture started before Workbench composition completed.");
            }

            workbenchCamera = workbench.WorkbenchCameraForDiagnostics;
            if (workbenchCamera == null)
            {
                throw new InvalidOperationException("Workbench camera is missing.");
            }

            var state = workbench.CommandBusForDiagnostics.State;
            state.map.width = 256;
            state.map.height = 256;
            workbenchCamera.orthographic = true;
            workbenchCamera.orthographicSize = 144f;
            workbenchCamera.transform.position = new Vector3(127.5f, 127.5f, -10f);
            QualitySettings.vSyncCount = 0;
            // The default remains uncapped so the existing render-capacity gate
            // is comparable. Passing -sundoll-m7-perf-target-fps 60 measures
            // production pacing with the same vSync-off policy as the Workbench.
            UnityEngine.Application.targetFrameRate = targetFrameRate;

            var definitionId = "m7-desktop-performance-definition";
            var library = workbench.PieceLibraryForDiagnostics;
            var definition = library.CreateDefinition(
                definitionId,
                "M7 Desktop Performance",
                "Performance",
                new[] { "m7", "performance" });
            if (!definition.accepted && M4PieceQueries.FindDefinition(state, definitionId) == null)
            {
                throw new InvalidOperationException("Could not create the desktop performance definition: " + definition.message);
            }

            for (var index = 0; index < PieceCount; index++)
            {
                var instanceId = "m7-desktop-performance-piece-" + index;
                var created = library.CreateInstance(definitionId, instanceId);
                if (!created.accepted && M4PieceQueries.FindInstance(state, instanceId) == null)
                {
                    throw new InvalidOperationException("Could not create performance piece " + index + ": " + created.message);
                }

                var placed = library.Place(instanceId, index % 256, (index / 256) % 256);
                if (!placed.accepted)
                {
                    throw new InvalidOperationException("Could not place performance piece " + index + ": " + placed.message);
                }
            }

            workbench.PieceProjectionForDiagnostics.RefreshAll();
            if (workbench.PieceProjectionForDiagnostics.Views.Count != PieceCount)
            {
                throw new InvalidOperationException(
                    "Expected " + PieceCount + " rendered pieces but found " +
                    workbench.PieceProjectionForDiagnostics.Views.Count + ".");
            }
        }

        private void AnimateCamera(int index)
        {
            var phase = index * 0.025f;
            var panX = 127.5f + Mathf.Sin(phase) * 3f;
            var panY = 127.5f + Mathf.Cos(phase * 0.83f) * 2f;
            workbenchCamera.transform.position = new Vector3(panX, panY, -10f);
            workbenchCamera.orthographicSize = 144f + Mathf.Sin(phase * 0.61f) * 3f;
        }

        private DesktopPerformanceResult BuildResult()
        {
            var sortedFrames = new List<float>(frameMilliseconds);
            var sortedAllocations = new List<long>(frameAllocations);
            sortedFrames.Sort();
            sortedAllocations.Sort();

            var result = new DesktopPerformanceResult
            {
                unityVersion = UnityEngine.Application.unityVersion,
                platform = UnityEngine.Application.platform.ToString(),
                width = Screen.width,
                height = Screen.height,
                targetWidth = 2560,
                targetHeight = 1440,
                targetFps = 60,
                measurementTargetFrameRate = targetFrameRate,
                measurementVSyncCount = QualitySettings.vSyncCount,
                mapWidth = 256,
                mapHeight = 256,
                visiblePieces = PieceCount,
                warmupFrames = warmupFrames,
                sampleFrames = frameMilliseconds.Count,
                frameP50Milliseconds = Percentile(sortedFrames, 0.50f),
                frameP95Milliseconds = Percentile(sortedFrames, 0.95f),
                frameMaxMilliseconds = sortedFrames[sortedFrames.Count - 1],
                allocationP50Bytes = Percentile(sortedAllocations, 0.50f),
                allocationP95Bytes = Percentile(sortedAllocations, 0.95f),
                allocationMaxBytes = sortedAllocations[sortedAllocations.Count - 1]
            };

            foreach (var sample in frameMilliseconds)
            {
                if (sample > TargetFrameMilliseconds)
                {
                    result.framesOverBudget++;
                }
            }

            result.frameBudgetPass = result.frameP95Milliseconds <= TargetFrameMilliseconds;
            result.allocationBudgetPass = result.allocationP95Bytes <= TargetAllocationBytes;
            result.overallBudgetPass = result.frameBudgetPass && result.allocationBudgetPass;
            return result;
        }

        private void WriteFailure(Exception exception)
        {
            try
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, JsonUtility.ToJson(new DesktopPerformanceFailure
                {
                    unityVersion = UnityEngine.Application.unityVersion,
                    platform = UnityEngine.Application.platform.ToString(),
                    message = exception.ToString()
                }, true));
            }
            catch
            {
                // The original exception is already reported to the Player log.
            }
        }

        private static float Percentile(List<float> sortedValues, float percentile)
        {
            var index = Mathf.Clamp(Mathf.CeilToInt(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
            return sortedValues[index];
        }

        private static long Percentile(List<long> sortedValues, float percentile)
        {
            var index = Mathf.Clamp(Mathf.CeilToInt(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
            return sortedValues[index];
        }

        private static int ReadPositiveIntArgument(string name, int fallback)
        {
            var value = ReadStringArgument(name, string.Empty);
            return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
        }

        private static int ReadIntegerArgument(string name, int fallback)
        {
            var value = ReadStringArgument(name, string.Empty);
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static string ReadStringArgument(string name, string fallback)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(arguments[index + 1]) ? fallback : arguments[index + 1];
                }
            }

            return fallback;
        }

        [Serializable]
        private sealed class DesktopPerformanceResult
        {
            public string unityVersion;
            public string platform;
            public int width;
            public int height;
            public int targetWidth;
            public int targetHeight;
            public int targetFps;
            public int measurementTargetFrameRate;
            public int measurementVSyncCount;
            public int mapWidth;
            public int mapHeight;
            public int visiblePieces;
            public int warmupFrames;
            public int sampleFrames;
            public float frameP50Milliseconds;
            public float frameP95Milliseconds;
            public float frameMaxMilliseconds;
            public long allocationP50Bytes;
            public long allocationP95Bytes;
            public long allocationMaxBytes;
            public int framesOverBudget;
            public bool frameBudgetPass;
            public bool allocationBudgetPass;
            public bool overallBudgetPass;
        }

        [Serializable]
        private sealed class DesktopPerformanceFailure
        {
            public string unityVersion;
            public string platform;
            public string message;
        }
    }
}
