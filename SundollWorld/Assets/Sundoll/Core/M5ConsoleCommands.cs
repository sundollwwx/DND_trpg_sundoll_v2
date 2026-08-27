using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    internal static class M5CommandSupport
    {
        public static M5ConsoleState Ensure(M1WorldState state)
        {
            state.EnsureSchema2Defaults();
            return M5ConsoleQueries.Ensure(state);
        }

        public static void RequireId(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(name + " is required.");
            }
        }

        public static M5MapSlot RequireMap(M5ConsoleState console, string mapId)
        {
            var map = console.FindMap(mapId);
            if (map == null)
            {
                throw new InvalidOperationException("M5 map was not found: " + mapId);
            }

            return map;
        }

        public static void CaptureActiveMap(M1WorldState state, M5ConsoleState console)
        {
            if (state.map == null)
            {
                throw new InvalidOperationException("Active map is required.");
            }

            var active = RequireMap(console, console.activeMapId);
            var captured = M5MapSlot.FromState(state, active.id, active.displayName);
            active.map = captured.map;
            active.publishedMap = captured.publishedMap;
        }
    }

    public sealed class M5CreateMapSlotCommand : M1Command
    {
        private readonly string mapId;
        private readonly string displayName;
        private readonly int width;
        private readonly int height;

        public M5CreateMapSlotCommand(string commandId, int baseRevision, string mapId, string displayName, int width, int height)
            : base(commandId, baseRevision)
        {
            this.mapId = mapId;
            this.displayName = displayName;
            this.width = width;
            this.height = height;
        }

        public override string Description => "创建地图：" + displayName;
        public override string CommandType => "M5.CreateMapSlot";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5CreateMapSlotCommandPayload
        {
            mapId = mapId,
            displayName = displayName,
            width = width,
            height = height
        };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            M5CommandSupport.RequireId(mapId, "Map ID");
            if (console.FindMap(mapId) != null)
            {
                throw new InvalidOperationException("M5 map already exists: " + mapId);
            }

            if (width < 1 || height < 1 || width > 4096 || height > 4096)
            {
                throw new InvalidOperationException("M5 map dimensions must be between 1 and 4096.");
            }

            M5CommandSupport.CaptureActiveMap(state, console);
            console.maps.Add(new M5MapSlot
            {
                id = mapId,
                displayName = string.IsNullOrWhiteSpace(displayName) ? mapId : displayName,
                map = new M1MapDocument
                {
                    id = mapId,
                    width = width,
                    height = height
                },
                publishedMap = null
            });
        }
    }

    public sealed class M5SwitchMapCommand : M1Command
    {
        private readonly string mapId;

        public M5SwitchMapCommand(string commandId, int baseRevision, string mapId)
            : base(commandId, baseRevision)
        {
            this.mapId = mapId;
        }

        public override string Description => "切换地图：" + mapId;
        public override string CommandType => "M5.SwitchMap";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5SwitchMapCommandPayload { mapId = mapId };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            M5CommandSupport.RequireId(mapId, "Map ID");
            var target = M5CommandSupport.RequireMap(console, mapId);
            if (console.activeMapId == mapId)
            {
                return;
            }

            M5CommandSupport.CaptureActiveMap(state, console);
            target.ApplyTo(state);
            console.activeMapId = mapId;
        }
    }

    public sealed class M5RenameMapCommand : M1Command
    {
        private readonly string mapId;
        private readonly string displayName;

        public M5RenameMapCommand(string commandId, int baseRevision, string mapId, string displayName)
            : base(commandId, baseRevision)
        {
            this.mapId = mapId;
            this.displayName = displayName;
        }

        public override string Description => "重命名地图";
        public override string CommandType => "M5.RenameMap";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5RenameMapCommandPayload { mapId = mapId, displayName = displayName };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            M5CommandSupport.RequireId(displayName, "Map display name");
            M5CommandSupport.RequireMap(console, mapId).displayName = displayName.Trim();
        }
    }

    public sealed class M5SetFogCommand : M1Command
    {
        private readonly string mapId;
        private readonly int x;
        private readonly int y;
        private readonly bool revealed;

        public M5SetFogCommand(string commandId, int baseRevision, string mapId, int x, int y, bool revealed)
            : base(commandId, baseRevision)
        {
            this.mapId = mapId;
            this.x = x;
            this.y = y;
            this.revealed = revealed;
        }

        public override string Description => revealed ? "揭示迷雾格" : "隐藏迷雾格";
        public override string CommandType => "M5.SetFog";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5SetFogCommandPayload
        {
            mapId = mapId,
            x = x,
            y = y,
            revealed = revealed
        };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            var map = M5CommandSupport.RequireMap(console, mapId);
            if (map.map == null || x < 0 || y < 0 || x >= map.map.width || y >= map.map.height)
            {
                throw new InvalidOperationException("Fog cell is outside the selected map.");
            }

            var cell = console.FindFogCell(mapId, x, y);
            if (cell == null)
            {
                console.fogCells.Add(new M5FogCell { mapId = mapId, x = x, y = y, revealed = revealed });
            }
            else
            {
                cell.revealed = revealed;
            }
        }
    }

    /// <summary>
    /// Applies one fog-brush stroke as a single command. Validation is done for
    /// the complete batch before any cell is changed, preserving atomicity for
    /// undo/redo and journal replay.
    /// </summary>
    public sealed class M5SetFogBatchCommand : M1Command
    {
        private readonly string mapId;
        private readonly List<M5FogCellMutation> mutations;

        public M5SetFogBatchCommand(
            string commandId,
            int baseRevision,
            string mapId,
            IEnumerable<M5FogCellMutation> mutations)
            : base(commandId, baseRevision)
        {
            this.mapId = mapId;
            if (mutations == null)
            {
                throw new ArgumentNullException(nameof(mutations));
            }

            this.mutations = new List<M5FogCellMutation>();
            foreach (var mutation in mutations)
            {
                this.mutations.Add(mutation == null ? null : mutation.DeepClone());
            }
        }

        public int MutationCount => mutations.Count;
        public override string Description => (mutations.Count > 0 && mutations[0] != null && mutations[0].revealed
            ? "揭示"
            : "隐藏") + "迷雾笔刷（" + mutations.Count + "格）";
        public override string CommandType => "M5.SetFogBatch";
        public override int PayloadVersion => 1;
        public override object CreatePayload()
        {
            var payload = new M5SetFogBatchCommandPayload { mapId = mapId };
            foreach (var mutation in mutations)
            {
                payload.mutations.Add(mutation == null ? null : mutation.DeepClone());
            }

            return payload;
        }

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            var map = M5CommandSupport.RequireMap(console, mapId);
            if (map.map == null || mutations.Count == 0)
            {
                throw new InvalidOperationException("A fog brush must contain at least one cell.");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mutation in mutations)
            {
                if (mutation == null ||
                    mutation.x < 0 || mutation.y < 0 ||
                    mutation.x >= map.map.width || mutation.y >= map.map.height)
                {
                    throw new InvalidOperationException("Fog brush contains a cell outside the selected map.");
                }

                if (!keys.Add(mutation.x + ":" + mutation.y))
                {
                    throw new InvalidOperationException("Fog brush contains a duplicate cell.");
                }
            }

            var existing = new Dictionary<string, M5FogCell>(StringComparer.Ordinal);
            foreach (var fogCell in console.fogCells)
            {
                if (fogCell != null && fogCell.mapId == mapId)
                {
                    existing[fogCell.x + ":" + fogCell.y] = fogCell;
                }
            }

            foreach (var mutation in mutations)
            {
                var key = mutation.x + ":" + mutation.y;
                if (existing.TryGetValue(key, out var fogCell))
                {
                    fogCell.revealed = mutation.revealed;
                }
                else
                {
                    fogCell = new M5FogCell
                    {
                        mapId = mapId,
                        x = mutation.x,
                        y = mutation.y,
                        revealed = mutation.revealed
                    };
                    console.fogCells.Add(fogCell);
                    existing[key] = fogCell;
                }
            }
        }
    }

    public sealed class M5UpsertAnnotationCommand : M1Command
    {
        private readonly string annotationId;
        private readonly string mapId;
        private readonly int x;
        private readonly int y;
        private readonly string text;
        private readonly string colorHex;
        private readonly bool visible;

        public M5UpsertAnnotationCommand(
            string commandId,
            int baseRevision,
            string annotationId,
            string mapId,
            int x,
            int y,
            string text,
            string colorHex,
            bool visible)
            : base(commandId, baseRevision)
        {
            this.annotationId = annotationId;
            this.mapId = mapId;
            this.x = x;
            this.y = y;
            this.text = text;
            this.colorHex = colorHex;
            this.visible = visible;
        }

        public override string Description => "更新动态标注";
        public override string CommandType => "M5.UpsertAnnotation";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5UpsertAnnotationCommandPayload
        {
            annotationId = annotationId,
            mapId = mapId,
            x = x,
            y = y,
            text = text,
            colorHex = colorHex,
            visible = visible
        };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            M5CommandSupport.RequireId(annotationId, "Annotation ID");
            var map = M5CommandSupport.RequireMap(console, mapId);
            if (map.map == null || x < 0 || y < 0 || x >= map.map.width || y >= map.map.height)
            {
                throw new InvalidOperationException("Annotation is outside the selected map.");
            }

            var annotation = console.FindAnnotation(annotationId);
            if (annotation == null)
            {
                console.annotations.Add(new M5DynamicAnnotation
                {
                    id = annotationId,
                    mapId = mapId,
                    x = x,
                    y = y,
                    text = text ?? string.Empty,
                    colorHex = string.IsNullOrWhiteSpace(colorHex) ? "#FFFFFF" : colorHex,
                    visible = visible
                });
            }
            else
            {
                annotation.mapId = mapId;
                annotation.x = x;
                annotation.y = y;
                annotation.text = text ?? string.Empty;
                annotation.colorHex = string.IsNullOrWhiteSpace(colorHex) ? "#FFFFFF" : colorHex;
                annotation.visible = visible;
            }
        }
    }

    public sealed class M5RemoveAnnotationCommand : M1Command
    {
        private readonly string annotationId;

        public M5RemoveAnnotationCommand(string commandId, int baseRevision, string annotationId)
            : base(commandId, baseRevision)
        {
            this.annotationId = annotationId;
        }

        public override string Description => "删除动态标注";
        public override string CommandType => "M5.RemoveAnnotation";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5RemoveAnnotationCommandPayload { annotationId = annotationId };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            M5CommandSupport.RequireId(annotationId, "Annotation ID");
            var annotation = console.FindAnnotation(annotationId);
            if (annotation == null)
            {
                throw new InvalidOperationException("Annotation was not found: " + annotationId);
            }

            console.annotations.Remove(annotation);
        }
    }

    public sealed class M5SetInteractionStateCommand : M1Command
    {
        private readonly string objectId;
        private readonly bool open;

        public M5SetInteractionStateCommand(string commandId, int baseRevision, string objectId, bool open)
            : base(commandId, baseRevision)
        {
            this.objectId = objectId;
            this.open = open;
        }

        public override string Description => open ? "打开交互对象" : "关闭交互对象";
        public override string CommandType => "M5.SetInteractionState";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M5SetInteractionStateCommandPayload { objectId = objectId, open = open };

        public override void Apply(M1WorldState state)
        {
            var console = M5CommandSupport.Ensure(state);
            M5CommandSupport.RequireId(objectId, "Object ID");
            var interaction = console.FindInteraction(objectId);
            if (interaction == null)
            {
                console.interactions.Add(new M5InteractionState { objectId = objectId, open = open });
            }
            else
            {
                interaction.open = open;
            }

            if (state.map != null && state.map.objects != null)
            {
                foreach (var mapObject in state.map.objects)
                {
                    if (mapObject != null && mapObject.id == objectId)
                    {
                        mapObject.state = open ? M3MapObjectOpenState.Open : M3MapObjectOpenState.Closed;
                        break;
                    }
                }
            }
        }
    }
}
