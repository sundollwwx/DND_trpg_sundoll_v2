using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    /// <summary>
    /// Authoritative host-console state. Map content stays in the existing
    /// M1 fields for compatibility; this additive model owns the multi-map
    /// selection and host-only presentation state introduced in M5.
    /// </summary>
    [Serializable]
    public sealed class M5MapSlot
    {
        public string id;
        public string displayName;
        public M1MapDocument map;
        public M1MapContentVersion publishedMap;

        public M5MapSlot DeepClone()
        {
            return new M5MapSlot
            {
                id = id,
                displayName = displayName,
                map = CloneMap(map),
                publishedMap = ClonePublishedMap(publishedMap)
            };
        }

        public static M5MapSlot FromState(M1WorldState state, string mapId, string displayName)
        {
            if (state == null || state.map == null)
            {
                throw new InvalidOperationException("A map is required to create an M5 map slot.");
            }

            return new M5MapSlot
            {
                id = string.IsNullOrWhiteSpace(mapId) ? state.map.id : mapId,
                displayName = string.IsNullOrWhiteSpace(displayName) ? state.map.id : displayName,
                map = CloneMap(state.map),
                publishedMap = ClonePublishedMap(state.publishedMap)
            };
        }

        public void ApplyTo(M1WorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state.map = CloneMap(map);
            state.publishedMap = ClonePublishedMap(publishedMap);
            if (state.board != null && state.publishedMap != null)
            {
                state.board.publishedMapContentId = state.publishedMap.id;
            }
        }

        private static M1MapDocument CloneMap(M1MapDocument source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new M1MapDocument
            {
                id = source.id,
                width = source.width,
                height = source.height,
                cells = new List<M1MapCell>(),
                objects = new List<M3MapObject>()
            };
            if (source.cells != null)
            {
                foreach (var cell in source.cells)
                {
                    if (cell == null)
                    {
                        continue;
                    }

                    result.cells.Add(new M1MapCell
                    {
                        x = cell.x,
                        y = cell.y,
                        layerId = cell.layerId,
                        contentId = cell.contentId
                    });
                }
            }

            if (source.objects != null)
            {
                foreach (var mapObject in source.objects)
                {
                    if (mapObject != null)
                    {
                        result.objects.Add(mapObject.DeepClone());
                    }
                }
            }

            return result;
        }

        private static M1MapContentVersion ClonePublishedMap(M1MapContentVersion source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new M1MapContentVersion
            {
                id = source.id,
                sourceMapId = source.sourceMapId,
                contentRevision = source.contentRevision,
                cells = new List<M1MapCell>(),
                objects = new List<M3MapObject>()
            };
            if (source.cells != null)
            {
                foreach (var cell in source.cells)
                {
                    if (cell == null)
                    {
                        continue;
                    }

                    result.cells.Add(new M1MapCell
                    {
                        x = cell.x,
                        y = cell.y,
                        layerId = cell.layerId,
                        contentId = cell.contentId
                    });
                }
            }

            if (source.objects != null)
            {
                foreach (var mapObject in source.objects)
                {
                    if (mapObject != null)
                    {
                        result.objects.Add(mapObject.DeepClone());
                    }
                }
            }

            return result;
        }
    }

    [Serializable]
    public sealed class M5FogCell
    {
        public string mapId;
        public int x;
        public int y;
        public bool revealed;

        public M5FogCell DeepClone()
        {
            return new M5FogCell { mapId = mapId, x = x, y = y, revealed = revealed };
        }
    }

    /// <summary>
    /// One cell mutation captured by the host fog brush. The map ID belongs to
    /// the enclosing command so a whole brush stroke is one atomic operation.
    /// </summary>
    [Serializable]
    public sealed class M5FogCellMutation
    {
        public int x;
        public int y;
        public bool revealed;

        public M5FogCellMutation()
        {
        }

        public M5FogCellMutation(int x, int y, bool revealed)
        {
            this.x = x;
            this.y = y;
            this.revealed = revealed;
        }

        public M5FogCellMutation DeepClone()
        {
            return new M5FogCellMutation(x, y, revealed);
        }
    }

    [Serializable]
    public sealed class M5DynamicAnnotation
    {
        public string id;
        public string mapId;
        public int x;
        public int y;
        public string text;
        public string colorHex = "#FFFFFF";
        public bool visible = true;

        public M5DynamicAnnotation DeepClone()
        {
            return new M5DynamicAnnotation
            {
                id = id,
                mapId = mapId,
                x = x,
                y = y,
                text = text,
                colorHex = colorHex,
                visible = visible
            };
        }
    }

    [Serializable]
    public sealed class M5InteractionState
    {
        public string objectId;
        public bool open;

        public M5InteractionState DeepClone()
        {
            return new M5InteractionState { objectId = objectId, open = open };
        }
    }

    [Serializable]
    public sealed class M5ConsoleState
    {
        public int formatVersion = 1;
        public string activeMapId;
        public List<M5MapSlot> maps = new List<M5MapSlot>();
        public List<M5FogCell> fogCells = new List<M5FogCell>();
        public List<M5DynamicAnnotation> annotations = new List<M5DynamicAnnotation>();
        public List<M5InteractionState> interactions = new List<M5InteractionState>();

        public M5ConsoleState DeepClone()
        {
            var clone = new M5ConsoleState
            {
                formatVersion = formatVersion,
                activeMapId = activeMapId,
                maps = new List<M5MapSlot>(),
                fogCells = new List<M5FogCell>(),
                annotations = new List<M5DynamicAnnotation>(),
                interactions = new List<M5InteractionState>()
            };

            if (maps != null)
            {
                foreach (var map in maps)
                {
                    if (map != null)
                    {
                        clone.maps.Add(map.DeepClone());
                    }
                }
            }

            if (fogCells != null)
            {
                foreach (var cell in fogCells)
                {
                    if (cell != null)
                    {
                        clone.fogCells.Add(cell.DeepClone());
                    }
                }
            }

            if (annotations != null)
            {
                foreach (var annotation in annotations)
                {
                    if (annotation != null)
                    {
                        clone.annotations.Add(annotation.DeepClone());
                    }
                }
            }

            if (interactions != null)
            {
                foreach (var interaction in interactions)
                {
                    if (interaction != null)
                    {
                        clone.interactions.Add(interaction.DeepClone());
                    }
                }
            }

            return clone;
        }

        public void EnsureDefaults(M1WorldState state)
        {
            if (maps == null)
            {
                maps = new List<M5MapSlot>();
            }

            if (fogCells == null)
            {
                fogCells = new List<M5FogCell>();
            }

            if (annotations == null)
            {
                annotations = new List<M5DynamicAnnotation>();
            }

            if (interactions == null)
            {
                interactions = new List<M5InteractionState>();
            }

            if (maps.Count == 0 && state != null && state.map != null)
            {
                maps.Add(M5MapSlot.FromState(state, state.map.id, state.project == null ? state.map.id : state.project.displayName));
            }

            if (string.IsNullOrWhiteSpace(activeMapId) && maps.Count > 0)
            {
                activeMapId = maps[0].id;
            }
        }

        public M5MapSlot FindMap(string mapId)
        {
            if (maps == null)
            {
                return null;
            }

            foreach (var map in maps)
            {
                if (map != null && string.Equals(map.id, mapId, StringComparison.Ordinal))
                {
                    return map;
                }
            }

            return null;
        }

        public M5FogCell FindFogCell(string mapId, int x, int y)
        {
            if (fogCells == null)
            {
                return null;
            }

            foreach (var cell in fogCells)
            {
                if (cell != null && cell.mapId == mapId && cell.x == x && cell.y == y)
                {
                    return cell;
                }
            }

            return null;
        }

        public bool IsRevealed(string mapId, int x, int y)
        {
            var cell = FindFogCell(mapId, x, y);
            return cell == null || cell.revealed;
        }

        public M5DynamicAnnotation FindAnnotation(string annotationId)
        {
            if (annotations == null)
            {
                return null;
            }

            foreach (var annotation in annotations)
            {
                if (annotation != null && annotation.id == annotationId)
                {
                    return annotation;
                }
            }

            return null;
        }

        public M5InteractionState FindInteraction(string objectId)
        {
            if (interactions == null)
            {
                return null;
            }

            foreach (var interaction in interactions)
            {
                if (interaction != null && interaction.objectId == objectId)
                {
                    return interaction;
                }
            }

            return null;
        }
    }

    public static class M5ConsoleQueries
    {
        public static M5ConsoleState Ensure(M1WorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.m5Console == null)
            {
                state.m5Console = new M5ConsoleState();
            }

            state.m5Console.EnsureDefaults(state);
            return state.m5Console;
        }
    }
}
