using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Opt-in desktop soak harness for the M7 release gate. It exercises the
    /// same facades used by the Workbench and rebuilds disposable projections,
    /// while keeping all state in an isolated temporary project.
    /// </summary>
    public sealed class M7DesktopSoakCapture : MonoBehaviour
    {
        public const string CommandLineArgument = "-sundoll-m7-soak";

        private const int DefaultDurationSeconds = 180;
        private const int DefaultCycleIntervalFrames = 60;
        private const int DefaultSaveEveryCycles = 8;
        private const int DefaultViewRebuildEveryCycles = 12;
        private const int MaxSaveWaitFrames = 900;

        private readonly string[] pieceIds =
        {
            "m7-soak-piece-0",
            "m7-soak-piece-1",
            "m7-soak-piece-2",
            "m7-soak-piece-3"
        };

        private M3WorkbenchRoot workbench;
        private M3MapEditorFacade editor;
        private M4PieceLibraryFacade pieceLibrary;
        private M5ConsoleFacade console;
        private M2SaveSession saveSession;
        private M3WorkbenchMapProjection mapProjection;
        private M4WorkbenchPieceProjection pieceProjection;
        private M5WorkbenchConsoleProjection consoleProjection;
        private M7BuiltinMapVisualCatalog visualCatalog;
        private string outputPath;
        private string primaryMapId;
        private string alternateMapId;
        private string doorObjectId = "m7-soak-door";
        private string annotationId = "m7-soak-annotation";
        private string lastPhase = "initializing";
        private string finalCanonicalHash;
        private string reloadedCanonicalHash;
        private int durationSeconds;
        private int cycleIntervalFrames;
        private int saveEveryCycles;
        private int viewRebuildEveryCycles;
        private int cycleCount;
        private int frameCount;
        private int acceptedCommandCount;
        private int mutationCount;
        private int saveRequestCount;
        private int completedSaveCount;
        private int viewRebuildCount;
        private int audienceProjectionCount;
        private float elapsedSeconds;

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
            durationSeconds = ReadPositiveIntArgument(
                "-sundoll-m7-soak-seconds",
                DefaultDurationSeconds);
            cycleIntervalFrames = ReadPositiveIntArgument(
                "-sundoll-m7-soak-cycle-frames",
                DefaultCycleIntervalFrames);
            saveEveryCycles = ReadPositiveIntArgument(
                "-sundoll-m7-soak-save-cycles",
                DefaultSaveEveryCycles);
            viewRebuildEveryCycles = ReadPositiveIntArgument(
                "-sundoll-m7-soak-view-cycles",
                DefaultViewRebuildEveryCycles);
            outputPath = ReadStringArgument(
                "-sundoll-m7-soak-output",
                Path.Combine(
                    UnityEngine.Application.temporaryCachePath,
                    "SundollWorld-M7DesktopSoak.json"));
            StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            yield return null;

            var routine = CaptureRoutineBody();
            while (true)
            {
                var hasNext = false;
                object current = null;
                Exception routineFailure = null;
                try
                {
                    hasNext = routine.MoveNext();
                    if (hasNext)
                    {
                        current = routine.Current;
                    }
                }
                catch (Exception exception)
                {
                    routineFailure = exception;
                }

                if (routineFailure != null)
                {
                    WriteFailure(routineFailure);
                    Debug.LogException(routineFailure);
                    UnityEngine.Application.Quit(1);
                    yield break;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return current;
            }
        }

        private IEnumerator CaptureRoutineBody()
        {
            lastPhase = "prepare scenario";
            PrepareScenario();
            yield return null;

            lastPhase = "initial save";
            yield return SaveAndVerify("M7 soak initial save");

            var startedAt = Time.realtimeSinceStartupAsDouble;
            while (cycleCount == 0 || Time.realtimeSinceStartupAsDouble - startedAt < durationSeconds)
            {
                if (frameCount % cycleIntervalFrames == 0)
                {
                    lastPhase = "cycle " + cycleCount;
                    ExecuteCycle(cycleCount);
                    cycleCount++;

                    if (cycleCount % saveEveryCycles == 0)
                    {
                        lastPhase = "save cycle " + cycleCount;
                        yield return SaveAndVerify("M7 soak cycle " + cycleCount);
                    }

                    if (cycleCount % viewRebuildEveryCycles == 0)
                    {
                        lastPhase = "view rebuild " + cycleCount;
                        yield return RebuildDisposableViews();
                    }
                }

                saveSession.RefreshSaveStatus();
                frameCount++;
                yield return null;
            }

            elapsedSeconds = (float)(Time.realtimeSinceStartupAsDouble - startedAt);
            lastPhase = "final save";
            yield return SaveAndVerify("M7 soak final save");
            VerifyProjectionState();
            finalCanonicalHash = M2CanonicalStateHasher.Compute(workbench.CommandBusForDiagnostics.State);
            lastPhase = "persistence reopen";
            reloadedCanonicalHash = VerifyPersistence();
            if (!string.Equals(finalCanonicalHash, reloadedCanonicalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Canonical hash changed after persistence reopen: " +
                    finalCanonicalHash + " != " + reloadedCanonicalHash);
            }

            WriteResult(BuildResult());
            Debug.Log(
                "M7 desktop soak completed: output=" + outputPath +
                "; duration=" + elapsedSeconds.ToString("0.0") + "s" +
                "; cycles=" + cycleCount +
                "; commands=" + acceptedCommandCount +
                "; saves=" + completedSaveCount +
                "; viewRebuilds=" + viewRebuildCount);
            UnityEngine.Application.Quit(0);
        }

        private void PrepareScenario()
        {
            if (workbench.CommandBusForDiagnostics == null ||
                workbench.PieceLibraryForDiagnostics == null ||
                workbench.MapProjectionForDiagnostics == null ||
                workbench.PieceProjectionForDiagnostics == null ||
                workbench.ConsoleProjectionForDiagnostics == null)
            {
                throw new InvalidOperationException("M7 desktop soak started before Workbench composition completed.");
            }

            editor = workbench.Editor;
            pieceLibrary = workbench.PieceLibraryForDiagnostics;
            console = new M5ConsoleFacade(workbench.CommandBusForDiagnostics);
            saveSession = workbench.SaveSession;
            mapProjection = workbench.MapProjectionForDiagnostics;
            pieceProjection = workbench.PieceProjectionForDiagnostics;
            consoleProjection = workbench.ConsoleProjectionForDiagnostics;
            visualCatalog = new M7BuiltinMapVisualCatalog();

            var state = workbench.CommandBusForDiagnostics.State;
            if (state.map == null)
            {
                throw new InvalidOperationException("M7 desktop soak requires an active map.");
            }

            state.map.width = 256;
            state.map.height = 256;
            primaryMapId = state.map.id;
            var consoleState = M5ConsoleQueries.Ensure(state);
            foreach (var mapSlot in consoleState.maps)
            {
                if (mapSlot != null && mapSlot.id == primaryMapId && mapSlot.map != null)
                {
                    mapSlot.map.width = 256;
                    mapSlot.map.height = 256;
                }
            }

            alternateMapId = "m7-soak-alt-map";
            if (consoleState.FindMap(alternateMapId) == null)
            {
                RecordAccepted(console.CreateMap(alternateMapId, "M7 Soak Alternate", 256, 256));
            }

            var definitionId = "m7-soak-definition";
            RecordAccepted(pieceLibrary.CreateDefinition(
                definitionId,
                "M7 Soak Piece",
                "Performance",
                new[] { "m7", "soak" }));
            for (var index = 0; index < pieceIds.Length; index++)
            {
                RecordAccepted(pieceLibrary.CreateInstance(definitionId, pieceIds[index]));
                RecordAccepted(pieceLibrary.Place(pieceIds[index], 12 + index, 12));
            }

            var initialCells = new List<M3CellMutation>
            {
                new M3CellMutation(10, 10, M3MapLayerIds.Terrain, "terrain-ground", false),
                new M3CellMutation(11, 10, M3MapLayerIds.Wall, "wall-solid", false),
                new M3CellMutation(12, 10, M3MapLayerIds.Object, "object-crate", false),
                new M3CellMutation(13, 10, M3MapLayerIds.Interaction, "interaction-trap", false),
                new M3CellMutation(14, 10, M3MapLayerIds.StaticAnnotation, "annotation-note", false)
            };
            RecordAccepted(editor.PaintCells(initialCells));
            RecordAccepted(editor.AddMapObject(doorObjectId, M3MapObjectKind.Door, 16, 10));
            RecordAccepted(editor.AddMapObject("m7-soak-chest", M3MapObjectKind.Chest, 18, 10));
            RecordAccepted(console.SetFogBatch(primaryMapId, new[]
            {
                new M5FogCellMutation(20, 20, false),
                new M5FogCellMutation(21, 20, false),
                new M5FogCellMutation(20, 21, false)
            }));
            RecordAccepted(console.UpsertAnnotation(
                annotationId,
                primaryMapId,
                22,
                20,
                "M7 Soak",
                "#CBAA61",
                true));
            RefreshAllViews();
        }

        private void ExecuteCycle(int cycle)
        {
            var baseX = 8 + cycle % 48;
            var baseY = 8 + (cycle / 48) % 48;
            var terrain = cycle % 2 == 0 ? "terrain-grass" : "terrain-stone";
            var mutations = new List<M3CellMutation>
            {
                new M3CellMutation(baseX, baseY, M3MapLayerIds.Terrain, terrain, false),
                new M3CellMutation(baseX + 1, baseY, M3MapLayerIds.Wall, "wall-solid", false),
                new M3CellMutation(baseX + 2, baseY, M3MapLayerIds.Object, "object-crate", false),
                new M3CellMutation(baseX + 3, baseY, M3MapLayerIds.Interaction, "interaction-trigger", false),
                new M3CellMutation(baseX + 4, baseY, M3MapLayerIds.StaticAnnotation, "annotation-note", false)
            };
            RecordAccepted(editor.PaintCells(mutations));
            mapProjection.RefreshRegion(editor.LastDirtyBounds);

            if (!editor.Undo())
            {
                throw new InvalidOperationException("Soak Undo did not produce a history entry.");
            }

            mutationCount++;
            saveSession.RecordMutation(
                "m7-soak-undo-" + cycle,
                "M7 soak Undo",
                editor.State);
            if (!editor.Redo())
            {
                throw new InvalidOperationException("Soak Redo did not restore the history entry.");
            }

            mutationCount++;
            saveSession.RecordMutation(
                "m7-soak-redo-" + cycle,
                "M7 soak Redo",
                editor.State);
            RefreshAllViews();

            for (var index = 0; index < pieceIds.Length; index++)
            {
                var pieceX = baseX + index;
                var pieceY = baseY + 2;
                RecordAccepted(pieceLibrary.Move(pieceIds[index], pieceX, pieceY));
                RecordAccepted(pieceLibrary.SetPresentation(
                    pieceIds[index],
                    ((cycle + index) % 4) * 90,
                    cycle % 2 == 1,
                    !(cycle % 5 == 0 && index == pieceIds.Length - 1)));
            }

            pieceProjection.RefreshAll();
            RecordAccepted(console.SetFogBatch(primaryMapId, new[]
            {
                new M5FogCellMutation(baseX, baseY + 5, cycle % 2 == 0),
                new M5FogCellMutation(baseX + 1, baseY + 5, cycle % 3 != 0),
                new M5FogCellMutation(baseX + 2, baseY + 5, cycle % 4 != 0)
            }));
            RecordAccepted(console.UpsertAnnotation(
                annotationId,
                primaryMapId,
                baseX,
                baseY + 6,
                "Soak " + cycle,
                cycle % 2 == 0 ? "#CBAA61" : "#C15C55",
                true));
            consoleProjection.RefreshAll();

            if (cycle % 2 == 0)
            {
                RecordAccepted(editor.ToggleMapObject(doorObjectId));
            }
            else
            {
                RecordAccepted(editor.RotateMapObjectClockwise(doorObjectId));
            }

            if (cycle % 4 == 0)
            {
                RecordAccepted(editor.PublishMapContent());
            }

            RecordAccepted(console.SwitchMap(alternateMapId));
            RefreshAllViews();
            RecordAccepted(console.SwitchMap(primaryMapId));
            RefreshAllViews();
            ExerciseAudienceProjection();
        }

        private void ExerciseAudienceProjection()
        {
            var snapshot = M6ProjectionBuilder.CreateSnapshot(
                workbench.CommandBusForDiagnostics.State,
                "m7-soak-audience",
                new M6AudiencePolicy
                {
                    revealAllFog = false,
                    includeHiddenPieces = false
                });
            var projectedState = JsonUtility.FromJson<M1WorldState>(snapshot.stateJson);
            mapProjection.SetAudienceProjection(projectedState);
            pieceProjection.SetAudienceProjection(projectedState);
            consoleProjection.SetAudiencePreview(true);
            audienceProjectionCount++;
            mapProjection.SetAudienceProjection(null);
            pieceProjection.SetAudienceProjection(null);
            consoleProjection.SetAudiencePreview(false);
        }

        private IEnumerator SaveAndVerify(string reason)
        {
            var operation = saveSession.QueueSave(reason);
            saveRequestCount++;
            var waitedFrames = 0;
            while (!operation.IsCompleted && waitedFrames < MaxSaveWaitFrames)
            {
                saveSession.RefreshSaveStatus();
                waitedFrames++;
                yield return null;
            }

            saveSession.RefreshSaveStatus();
            if (!operation.IsCompleted)
            {
                throw new TimeoutException("M7 soak save did not complete: " + reason);
            }

            if (operation.Status != M2SaveStatus.Safe || saveSession.SaveStatus == M2SaveStatus.Failed)
            {
                throw new IOException(
                    "M7 soak save failed: " + reason + "; " +
                    (operation.Error == null ? saveSession.LastSaveError : operation.Error.Message));
            }

            completedSaveCount++;
        }

        private IEnumerator RebuildDisposableViews()
        {
            GameObject mapObject = null;
            GameObject pieceObject = null;
            GameObject consoleObject = null;
            try
            {
                mapObject = new GameObject("M7SoakMapView");
                var disposableMap = mapObject.AddComponent<M3WorkbenchMapProjection>();
                disposableMap.Bind(editor, workbench.LayerEditState, visualCatalog);
                pieceObject = new GameObject("M7SoakPieceView");
                var disposablePieces = pieceObject.AddComponent<M4WorkbenchPieceProjection>();
                disposablePieces.Bind(workbench.CommandBusForDiagnostics);
                consoleObject = new GameObject("M7SoakConsoleView");
                var disposableConsole = consoleObject.AddComponent<M5WorkbenchConsoleProjection>();
                disposableConsole.Bind(workbench.CommandBusForDiagnostics);
                viewRebuildCount++;
                yield return null;
            }
            finally
            {
                if (mapObject != null)
                {
                    Destroy(mapObject);
                }

                if (pieceObject != null)
                {
                    Destroy(pieceObject);
                }

                if (consoleObject != null)
                {
                    Destroy(consoleObject);
                }
            }

            yield return null;
        }

        private void RefreshAllViews()
        {
            mapProjection.RefreshAll();
            pieceProjection.RefreshAll();
            consoleProjection.RefreshAll();
        }

        private void VerifyProjectionState()
        {
            if (!mapProjection.IsAudienceProjectionActive &&
                !pieceProjection.IsAudienceProjectionActive &&
                !consoleProjection.IsAudiencePreview)
            {
                return;
            }

            throw new InvalidOperationException("M7 soak left a disposable audience projection active.");
        }

        private string VerifyPersistence()
        {
            var validation = saveSession.Validate();
            if (!validation.valid)
            {
                throw new InvalidDataException("M7 soak save validation failed: " + validation.diagnostic);
            }

            var reopened = M2SaveSession.Open(
                saveSession.ProjectRoot,
                workbench.CommandBusForDiagnostics.State);
            try
            {
                var reopenedState = reopened.State;
                var hash = M2CanonicalStateHasher.Compute(reopenedState);
                if (reopened.SaveStatus != M2SaveStatus.Safe)
                {
                    throw new InvalidDataException("M7 soak reopened session is not safe: " + reopened.SaveStatus);
                }

                var expectedHash = M2CanonicalStateHasher.Compute(workbench.CommandBusForDiagnostics.State);
                if (!string.Equals(expectedHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    WriteStateDiagnostics(workbench.CommandBusForDiagnostics.State, reopenedState);
                }

                return hash;
            }
            finally
            {
                reopened.Dispose();
            }
        }

        private void WriteStateDiagnostics(M1WorldState expected, M1WorldState actual)
        {
            try
            {
                var expectedPath = outputPath + ".expected-state.json";
                var actualPath = outputPath + ".reopened-state.json";
                var directory = Path.GetDirectoryName(expectedPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(expectedPath, JsonUtility.ToJson(expected, true));
                File.WriteAllText(actualPath, JsonUtility.ToJson(actual, true));
                File.WriteAllText(
                    expectedPath + ".canonical.txt",
                    M2CanonicalStateHasher.GetCanonicalPayload(expected));
                File.WriteAllText(
                    actualPath + ".canonical.txt",
                    M2CanonicalStateHasher.GetCanonicalPayload(actual));
                Debug.Log(
                    "M7 soak wrote state diagnostics: expected=" + expectedPath +
                    "; reopened=" + actualPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("M7 soak could not write state diagnostics: " + exception.Message);
            }
        }

        private void RecordAccepted(M1CommandReceipt receipt)
        {
            if (receipt == null || !receipt.accepted || receipt.duplicate)
            {
                throw new InvalidOperationException(
                    "M7 soak command was not accepted: " +
                    (receipt == null ? "null receipt" : receipt.message));
            }

            saveSession.RecordAccepted(receipt, workbench.CommandBusForDiagnostics.State);
            acceptedCommandCount++;
        }

        private SoakResult BuildResult()
        {
            var state = workbench.CommandBusForDiagnostics.State;
            return new SoakResult
            {
                ok = true,
                unityVersion = UnityEngine.Application.unityVersion,
                platform = UnityEngine.Application.platform.ToString(),
                width = Screen.width,
                height = Screen.height,
                targetWidth = 2560,
                targetHeight = 1440,
                requestedDurationSeconds = durationSeconds,
                elapsedSeconds = elapsedSeconds,
                cycleIntervalFrames = cycleIntervalFrames,
                cycles = cycleCount,
                frames = frameCount,
                acceptedCommands = acceptedCommandCount,
                undoRedoMutations = mutationCount,
                saveRequests = saveRequestCount,
                completedSaves = completedSaveCount,
                viewRebuilds = viewRebuildCount,
                audienceProjectionExercises = audienceProjectionCount,
                finalRevision = state.revision,
                mapCells = state.map == null || state.map.cells == null ? 0 : state.map.cells.Count,
                pieceInstances = state.pieceInstances == null ? 0 : state.pieceInstances.Count,
                fogCells = state.m5Console == null || state.m5Console.fogCells == null
                    ? 0
                    : state.m5Console.fogCells.Count,
                annotations = state.m5Console == null || state.m5Console.annotations == null
                    ? 0
                    : state.m5Console.annotations.Count,
                finalSaveStatus = saveSession.SaveStatus.ToString(),
                finalCanonicalHash = finalCanonicalHash,
                reloadedCanonicalHash = reloadedCanonicalHash
            };
        }

        private void WriteResult(SoakResult result)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, JsonUtility.ToJson(result, true));
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

                File.WriteAllText(outputPath, JsonUtility.ToJson(new SoakFailure
                {
                    ok = false,
                    unityVersion = UnityEngine.Application.unityVersion,
                    platform = UnityEngine.Application.platform.ToString(),
                    phase = lastPhase,
                    message = exception.ToString()
                }, true));
            }
            catch
            {
                // The original exception is already reported to the Player log.
            }
        }

        private static int ReadPositiveIntArgument(string name, int fallback)
        {
            var value = ReadStringArgument(name, string.Empty);
            return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
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
        private sealed class SoakResult
        {
            public bool ok;
            public string unityVersion;
            public string platform;
            public int width;
            public int height;
            public int targetWidth;
            public int targetHeight;
            public int requestedDurationSeconds;
            public float elapsedSeconds;
            public int cycleIntervalFrames;
            public int cycles;
            public int frames;
            public int acceptedCommands;
            public int undoRedoMutations;
            public int saveRequests;
            public int completedSaves;
            public int viewRebuilds;
            public int audienceProjectionExercises;
            public int finalRevision;
            public int mapCells;
            public int pieceInstances;
            public int fogCells;
            public int annotations;
            public string finalSaveStatus;
            public string finalCanonicalHash;
            public string reloadedCanonicalHash;
        }

        [Serializable]
        private sealed class SoakFailure
        {
            public bool ok;
            public string unityVersion;
            public string platform;
            public string phase;
            public string message;
        }
    }
}
