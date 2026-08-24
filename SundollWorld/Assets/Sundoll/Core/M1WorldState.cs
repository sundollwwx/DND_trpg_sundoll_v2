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
        public int schemaVersion = 1;
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

        public int RuntimeIndexBuildCount => runtimeIndexBuildCount;

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
        public int schemaVersion = 1;
        public int revision;
        public M1ProjectDocument project;
        public M1MapDocument map;
        public M1MapContentVersion publishedMap;
        public M1ScenarioDocument scenario;
        public M1BoardInstance board;
        public M1PieceDefinition pieceDefinition;
        public M1PieceInstance pieceInstance;

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
                    cells = CloneCells(map.cells)
                },
                publishedMap = publishedMap == null ? null : new M1MapContentVersion
                {
                    id = publishedMap.id,
                    sourceMapId = publishedMap.sourceMapId,
                    contentRevision = publishedMap.contentRevision,
                    cells = CloneCells(publishedMap.cells)
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
    }
}
