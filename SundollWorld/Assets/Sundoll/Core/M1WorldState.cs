using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    public enum M1PieceLocationKind
    {
        Unplaced = 0,
        OnBoard = 1,
        InContainer = 2,
        Attached = 3
    }

    [Serializable]
    public sealed class M1ProjectDocument
    {
        public string id;
        public string displayName;
        public int schemaVersion = 2;
    }

    [Serializable]
    public sealed class M1MapCell
    {
        public int x;
        public int y;
        // Optional in legacy saves; the effective layer is inferred from contentId when absent.
        public string layerId;
        public string contentId;
    }

    [Serializable]
    public sealed class M1MapDocument
    {
        [NonSerialized] private Dictionary<M3MapCellKey, int> runtimeCellIndex;
        [NonSerialized] private int runtimeIndexedCellCount = -1;
        [NonSerialized] private int runtimeIndexBuildCount;

        public string id;
        public int width;
        public int height;
        public List<M1MapCell> cells = new List<M1MapCell>();
        // Added in world schema 2. Legacy schema 1 JSON may omit this field.
        public List<M3MapObject> objects = new List<M3MapObject>();

        public int RuntimeIndexBuildCount => runtimeIndexBuildCount;

        public bool TryGetCell(int x, int y, string layerId, out M1MapCell cell)
        {
            var key = new M3MapCellKey(x, y, M3MapLayerIds.NormalizeLayerId(layerId, null));
            return TryGetRuntimeCell(key, out cell);
        }

        internal bool TryGetRuntimeCell(M3MapCellKey key, out M1MapCell cell)
        {
            EnsureRuntimeIndex();
            if (runtimeCellIndex.TryGetValue(key, out var index))
            {
                cell = cells[index];
                return true;
            }

            cell = null;
            return false;
        }

        internal void SetRuntimeCell(M3MapCellKey key, string contentId)
        {
            EnsureRuntimeIndex();
            if (runtimeCellIndex.TryGetValue(key, out var index))
            {
                cells[index].layerId = key.layerId;
                cells[index].contentId = contentId;
                return;
            }

            cells.Add(new M1MapCell
            {
                x = key.x,
                y = key.y,
                layerId = key.layerId,
                contentId = contentId
            });
            runtimeCellIndex.Add(key, cells.Count - 1);
            runtimeIndexedCellCount = cells.Count;
        }

        internal void RemoveRuntimeCell(M3MapCellKey key)
        {
            EnsureRuntimeIndex();
            if (!runtimeCellIndex.TryGetValue(key, out var index))
            {
                return;
            }

            var lastIndex = cells.Count - 1;
            if (index != lastIndex)
            {
                var movedCell = cells[lastIndex];
                cells[index] = movedCell;
                var movedKey = new M3MapCellKey(
                    movedCell.x,
                    movedCell.y,
                    M3MapLayerIds.NormalizeLayerId(movedCell.layerId, movedCell.contentId));
                runtimeCellIndex[movedKey] = index;
            }

            cells.RemoveAt(lastIndex);
            runtimeCellIndex.Remove(key);
            runtimeIndexedCellCount = cells.Count;
        }

        private void EnsureRuntimeIndex()
        {
            if (cells == null)
            {
                cells = new List<M1MapCell>();
            }

            if (runtimeCellIndex != null && runtimeIndexedCellCount == cells.Count)
            {
                return;
            }

            runtimeCellIndex = new Dictionary<M3MapCellKey, int>();
            var canonicalCells = new List<M1MapCell>(cells.Count);
            foreach (var cell in cells)
            {
                if (cell == null)
                {
                    continue;
                }

                cell.layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                var key = new M3MapCellKey(cell.x, cell.y, cell.layerId);
                if (runtimeCellIndex.TryGetValue(key, out var existingIndex))
                {
                    canonicalCells[existingIndex] = cell;
                }
                else
                {
                    runtimeCellIndex.Add(key, canonicalCells.Count);
                    canonicalCells.Add(cell);
                }
            }

            cells.Clear();
            cells.AddRange(canonicalCells);
            runtimeIndexedCellCount = cells.Count;
            runtimeIndexBuildCount++;
        }
    }

    [Serializable]
    public sealed class M1MapContentVersion
    {
        public string id;
        public string sourceMapId;
        public int contentRevision;
        public List<M1MapCell> cells = new List<M1MapCell>();
        public List<M3MapObject> objects = new List<M3MapObject>();
    }

    public enum M3MapObjectKind
    {
        Door = 0,
        Chest = 1
    }

    public enum M3MapObjectOpenState
    {
        Closed = 0,
        Open = 1
    }

    [Serializable]
    public sealed class M3MapObject
    {
        public string id;
        public M3MapObjectKind kind;
        public int x;
        public int y;
        // Only 0, 90, 180 and 270 are valid. The value is kept as an int for
        // stable JSON and simple input binding.
        public int rotation;
        public M3MapObjectOpenState state = M3MapObjectOpenState.Closed;

        public M3MapObject DeepClone()
        {
            return new M3MapObject
            {
                id = id,
                kind = kind,
                x = x,
                y = y,
                rotation = NormalizeRotation(rotation),
                state = state
            };
        }

        public static int NormalizeRotation(int value)
        {
            var normalized = value % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            if (normalized != 0 && normalized != 90 && normalized != 180 && normalized != 270)
            {
                return 0;
            }

            return normalized;
        }
    }

    [Serializable]
    public sealed class M1ScenarioDocument
    {
        public string id;
        public string publishedMapContentId;
        public string boardId;
    }

    [Serializable]
    public sealed class M1BoardInstance
    {
        public string id;
        public string scenarioId;
        public string publishedMapContentId;
    }

    [Serializable]
    public sealed class M1PieceDefinition
    {
        public string id;
        public string displayName;
        public string visualKey;
    }

    [Serializable]
    public sealed class M1PieceLocation
    {
        public M1PieceLocationKind kind;
        public string boardId;
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class M1PieceInstance
    {
        public string id;
        public string definitionId;
        public M1PieceLocation location = new M1PieceLocation();
    }

    [Serializable]
    public sealed class M1WorldState
    {
        public int schemaVersion = 2;
        public int revision;
        public M1ProjectDocument project;
        public M1MapDocument map;
        public M1MapContentVersion publishedMap;
        public M1ScenarioDocument scenario;
        public M1BoardInstance board;
        public M1PieceDefinition pieceDefinition;
        public M1PieceInstance pieceInstance;
        // M4 collections are additive to the M1 singular fields. The latter
        // remain readable for existing pre-M4 development saves.
        public List<M4PieceAsset> pieceAssets = new List<M4PieceAsset>();
        public List<M4PieceDefinition> pieceDefinitions = new List<M4PieceDefinition>();
        public List<M4PieceInstance> pieceInstances = new List<M4PieceInstance>();
        // M5 host-console state is additive so existing pre-M5 saves remain
        // readable and can be upgraded through the explicit migration path.
        public M5ConsoleState m5Console = new M5ConsoleState();

        public static M1WorldState CreateEmpty()
        {
            return new M1WorldState();
        }

        public bool HasCompleteVerticalSlice()
        {
            return project != null && map != null && publishedMap != null &&
                   scenario != null && board != null && pieceDefinition != null &&
                   pieceInstance != null && pieceInstance.location != null &&
                   pieceInstance.location.kind == M1PieceLocationKind.OnBoard &&
                   pieceInstance.location.boardId == board.id;
        }

        public M1WorldState DeepClone()
        {
            var clone = new M1WorldState
            {
                schemaVersion = schemaVersion,
                revision = revision,
                project = project == null ? null : new M1ProjectDocument
                {
                    id = project.id,
                    displayName = project.displayName,
                    schemaVersion = project.schemaVersion
                },
                map = map == null ? null : new M1MapDocument
                {
                    id = map.id,
                    width = map.width,
                    height = map.height,
                    cells = CloneCells(map.cells),
                    objects = CloneObjects(map.objects)
                },
                publishedMap = publishedMap == null ? null : new M1MapContentVersion
                {
                    id = publishedMap.id,
                    sourceMapId = publishedMap.sourceMapId,
                    contentRevision = publishedMap.contentRevision,
                    cells = CloneCells(publishedMap.cells),
                    objects = CloneObjects(publishedMap.objects)
                },
                scenario = scenario == null ? null : new M1ScenarioDocument
                {
                    id = scenario.id,
                    publishedMapContentId = scenario.publishedMapContentId,
                    boardId = scenario.boardId
                },
                board = board == null ? null : new M1BoardInstance
                {
                    id = board.id,
                    scenarioId = board.scenarioId,
                    publishedMapContentId = board.publishedMapContentId
                },
                pieceDefinition = pieceDefinition == null ? null : new M1PieceDefinition
                {
                    id = pieceDefinition.id,
                    displayName = pieceDefinition.displayName,
                    visualKey = pieceDefinition.visualKey
                }
            };

            clone.pieceAssets = ClonePieceAssets(pieceAssets);
            clone.pieceDefinitions = ClonePieceDefinitions(pieceDefinitions);
            clone.pieceInstances = ClonePieceInstances(pieceInstances);
            clone.m5Console = m5Console == null ? null : m5Console.DeepClone();

            if (pieceInstance != null)
            {
                clone.pieceInstance = new M1PieceInstance
                {
                    id = pieceInstance.id,
                    definitionId = pieceInstance.definitionId,
                    location = pieceInstance.location == null ? null : new M1PieceLocation
                    {
                        kind = pieceInstance.location.kind,
                        boardId = pieceInstance.location.boardId,
                        x = pieceInstance.location.x,
                        y = pieceInstance.location.y
                    }
                };
            }

            return clone;
        }

        public void CopyFrom(M1WorldState source)
        {
            var clone = source.DeepClone();
            schemaVersion = clone.schemaVersion;
            revision = clone.revision;
            project = clone.project;
            map = clone.map;
            publishedMap = clone.publishedMap;
            scenario = clone.scenario;
            board = clone.board;
            pieceDefinition = clone.pieceDefinition;
            pieceInstance = clone.pieceInstance;
            pieceAssets = clone.pieceAssets;
            pieceDefinitions = clone.pieceDefinitions;
            pieceInstances = clone.pieceInstances;
            m5Console = clone.m5Console;
        }

        /// <summary>
        /// Supplies the schema-2 defaults when a legacy schema-1 JSON document
        /// omitted newly introduced lists. This deliberately does not rewrite
        /// the schema marker; callers that migrate a file can opt into that
        /// explicit decision after integrity checks have completed.
        /// </summary>
        public void EnsureSchema2Defaults()
        {
            if (project != null && project.schemaVersion <= 0)
            {
                project.schemaVersion = 2;
            }

            if (map != null)
            {
                if (map.cells == null)
                {
                    map.cells = new List<M1MapCell>();
                }

                if (map.objects == null)
                {
                    map.objects = new List<M3MapObject>();
                }
            }

            if (publishedMap != null)
            {
                if (publishedMap.cells == null)
                {
                    publishedMap.cells = new List<M1MapCell>();
                }

                if (publishedMap.objects == null)
                {
                    publishedMap.objects = new List<M3MapObject>();
                }
            }

            if (pieceAssets == null)
            {
                pieceAssets = new List<M4PieceAsset>();
            }

            if (pieceDefinitions == null)
            {
                pieceDefinitions = new List<M4PieceDefinition>();
            }

            if (pieceInstances == null)
            {
                pieceInstances = new List<M4PieceInstance>();
            }

            if (m5Console == null)
            {
                m5Console = new M5ConsoleState();
            }
        }

        private static List<M1MapCell> CloneCells(List<M1MapCell> source)
        {
            var result = new List<M1MapCell>();
            if (source == null)
            {
                return result;
            }

            foreach (var cell in source)
            {
                if (cell == null)
                {
                    continue;
                }

                result.Add(new M1MapCell
                {
                    x = cell.x,
                    y = cell.y,
                    layerId = cell.layerId,
                    contentId = cell.contentId
                });
            }

            return result;
        }

        private static List<M3MapObject> CloneObjects(List<M3MapObject> source)
        {
            var result = new List<M3MapObject>();
            if (source == null)
            {
                return result;
            }

            foreach (var mapObject in source)
            {
                if (mapObject != null)
                {
                    result.Add(mapObject.DeepClone());
                }
            }

            return result;
        }

        private static List<M4PieceAsset> ClonePieceAssets(List<M4PieceAsset> source)
        {
            var result = new List<M4PieceAsset>();
            if (source == null)
            {
                return result;
            }

            foreach (var asset in source)
            {
                if (asset != null)
                {
                    result.Add(asset.DeepClone());
                }
            }

            return result;
        }

        private static List<M4PieceDefinition> ClonePieceDefinitions(List<M4PieceDefinition> source)
        {
            var result = new List<M4PieceDefinition>();
            if (source == null)
            {
                return result;
            }

            foreach (var definition in source)
            {
                if (definition != null)
                {
                    result.Add(definition.DeepClone());
                }
            }

            return result;
        }

        private static List<M4PieceInstance> ClonePieceInstances(List<M4PieceInstance> source)
        {
            var result = new List<M4PieceInstance>();
            if (source == null)
            {
                return result;
            }

            foreach (var instance in source)
            {
                if (instance != null)
                {
                    result.Add(instance.DeepClone());
                }
            }

            return result;
        }
    }
}
