using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Application
{
    public sealed class M3MapEditorFacade
    {
        private M1CommandBus commandBus;

        public M3MapEditorFacade(M1CommandBus commandBus)
        {
            Bind(commandBus);
        }

        public M1WorldState State => commandBus.State;
        public string LastAction => commandBus.LastAction;
        public M3GridBounds LastDirtyBounds { get; private set; } = M3GridBounds.Empty;

        public void Bind(M1CommandBus nextCommandBus)
        {
            commandBus = nextCommandBus ?? throw new ArgumentNullException(nameof(nextCommandBus));
        }

        public M1CommandReceipt PaintCell(int x, int y, string contentId)
        {
            return PaintCells(new[] { new M3CellMutation(x, y, contentId, false) });
        }

        public M1CommandReceipt PaintCell(int x, int y, string layerId, string contentId)
        {
            return PaintCells(new[] { new M3CellMutation(x, y, layerId, contentId, false) });
        }

        public M1CommandReceipt EraseCell(int x, int y)
        {
            var mutations = new List<M3CellMutation>();
            if (State.map != null && State.map.cells != null)
            {
                foreach (var cell in State.map.cells)
                {
                    if (cell != null && cell.x == x && cell.y == y)
                    {
                        mutations.Add(new M3CellMutation(
                            x,
                            y,
                            M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId),
                            null,
                            true));
                    }
                }
            }

            if (mutations.Count == 0)
            {
                mutations.Add(new M3CellMutation(x, y, M3MapLayerIds.Terrain, null, true));
            }

            return PaintCells(mutations);
        }

        public M1CommandReceipt EraseCell(int x, int y, string layerId)
        {
            return PaintCells(new[] { new M3CellMutation(x, y, layerId, null, true) });
        }

        public M1CommandReceipt PaintCells(IList<M3CellMutation> mutations)
        {
            var receipt = commandBus.Execute(new M3PaintCellsCommand(
                "m3-map-edit-" + Guid.NewGuid().ToString("N"),
                commandBus.State.revision,
                mutations));

            if (receipt.accepted && !receipt.duplicate)
            {
                UpdateDirtyBoundsFromLastChangeSet();
            }

            return receipt;
        }

        public M1CommandReceipt PublishMapContent()
        {
            return commandBus.Execute(new M1PublishMapContentCommand(
                "m3-publish-map-" + Guid.NewGuid().ToString("N"),
                commandBus.State.revision,
                "map-content-m3-" + Guid.NewGuid().ToString("N")));
        }

        public M1CommandReceipt CreateScenarioBoard(string scenarioId, string boardId)
        {
            return commandBus.Execute(new M1CreateScenarioCommand(
                "m3-create-scenario-" + Guid.NewGuid().ToString("N"),
                commandBus.State.revision,
                scenarioId,
                boardId));
        }

        public bool TryPickTopmost(int x, int y, M3LayerEditState layerState, out M1MapCell pickedCell)
        {
            if (layerState == null)
            {
                throw new ArgumentNullException(nameof(layerState));
            }

            pickedCell = null;
            var bestOrder = -1;
            if (State.map == null || State.map.cells == null)
            {
                return false;
            }

            foreach (var cell in State.map.cells)
            {
                if (cell == null || cell.x != x || cell.y != y)
                {
                    continue;
                }

                var layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                if (!layerState.IsVisible(layerId))
                {
                    continue;
                }

                var order = layerState.IndexOf(layerId);
                if (pickedCell == null || order > bestOrder)
                {
                    pickedCell = cell;
                    bestOrder = order;
                }
            }

            return pickedCell != null;
        }

        public M3MapClipboard CopySelection(M3GridBounds selection, M3LayerEditState layerState)
        {
            if (layerState == null)
            {
                throw new ArgumentNullException(nameof(layerState));
            }

            var clipboard = new M3MapClipboard
            {
                width = selection.Width,
                height = selection.Height
            };
            if (selection.IsEmpty || State.map == null || State.map.cells == null)
            {
                return clipboard;
            }

            foreach (var cell in State.map.cells)
            {
                if (cell == null || !selection.Contains(cell.x, cell.y))
                {
                    continue;
                }

                var layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                if (!layerState.IsVisible(layerId))
                {
                    continue;
                }

                clipboard.cells.Add(new M3ClipboardCell
                {
                    offsetX = cell.x - selection.MinX,
                    offsetY = cell.y - selection.MinY,
                    layerId = layerId,
                    contentId = cell.contentId
                });
            }

            return clipboard;
        }

        public M1CommandReceipt CutSelection(
            M3GridBounds selection,
            M3LayerEditState layerState,
            out M3MapClipboard clipboard)
        {
            clipboard = CopySelection(selection, layerState);
            var mutations = new List<M3CellMutation>();
            if (State.map != null && State.map.cells != null && !selection.IsEmpty)
            {
                foreach (var cell in State.map.cells)
                {
                    if (cell == null || !selection.Contains(cell.x, cell.y))
                    {
                        continue;
                    }

                    var layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                    if (layerState.IsVisible(layerId) && layerState.CanEdit(layerId))
                    {
                        mutations.Add(new M3CellMutation(cell.x, cell.y, layerId, null, true));
                    }
                }
            }

            return mutations.Count == 0
                ? RejectedReceipt("选区没有可剪切的可编辑内容。")
                : PaintCells(mutations);
        }

        public M1CommandReceipt PasteClipboard(
            M3MapClipboard clipboard,
            int anchorX,
            int anchorY,
            M3LayerEditState layerState)
        {
            if (clipboard == null || clipboard.IsEmpty)
            {
                return RejectedReceipt("剪贴板为空。");
            }

            if (layerState == null)
            {
                throw new ArgumentNullException(nameof(layerState));
            }

            var mutations = new List<M3CellMutation>();
            foreach (var cell in clipboard.cells)
            {
                if (cell == null)
                {
                    continue;
                }

                var x = anchorX + cell.offsetX;
                var y = anchorY + cell.offsetY;
                if (State.map == null || x < 0 || x >= State.map.width || y < 0 || y >= State.map.height)
                {
                    return RejectedReceipt("粘贴内容越界，整批事务已拒绝。");
                }

                if (!layerState.CanEdit(cell.layerId))
                {
                    return RejectedReceipt("粘贴目标包含锁定图层，整批事务已拒绝。");
                }

                mutations.Add(new M3CellMutation(x, y, cell.layerId, cell.contentId, false));
            }

            return mutations.Count == 0
                ? RejectedReceipt("剪贴板没有有效内容。")
                : PaintCells(mutations);
        }

        public M1CommandReceipt AddMapObject(string objectId, M3MapObjectKind kind, int x, int y, int rotation = 0)
        {
            return ExecuteObjectCommand(objectId, kind, x, y, rotation, M3MapObjectAction.Add);
        }

        public M1CommandReceipt OpenMapObject(string objectId)
        {
            return ExecuteObjectCommand(objectId, M3MapObjectKind.Door, 0, 0, 0, M3MapObjectAction.Open);
        }

        public M1CommandReceipt CloseMapObject(string objectId)
        {
            return ExecuteObjectCommand(objectId, M3MapObjectKind.Door, 0, 0, 0, M3MapObjectAction.Close);
        }

        public M1CommandReceipt ToggleMapObject(string objectId)
        {
            return ExecuteObjectCommand(objectId, M3MapObjectKind.Door, 0, 0, 0, M3MapObjectAction.Toggle);
        }

        public M1CommandReceipt RotateMapObjectClockwise(string objectId)
        {
            return ExecuteObjectCommand(objectId, M3MapObjectKind.Door, 0, 0, 0, M3MapObjectAction.RotateClockwise);
        }

        public M1CommandReceipt RemoveMapObject(string objectId)
        {
            return ExecuteObjectCommand(objectId, M3MapObjectKind.Door, 0, 0, 0, M3MapObjectAction.Remove);
        }

        public M3MapObject FindMapObject(string objectId)
        {
            if (State.map == null || State.map.objects == null)
            {
                return null;
            }

            foreach (var mapObject in State.map.objects)
            {
                if (mapObject != null && string.Equals(mapObject.id, objectId, StringComparison.Ordinal))
                {
                    return mapObject;
                }
            }

            return null;
        }

        private M1CommandReceipt ExecuteObjectCommand(
            string objectId,
            M3MapObjectKind kind,
            int x,
            int y,
            int rotation,
            M3MapObjectAction action)
        {
            var receipt = commandBus.Execute(new M3MapObjectCommand(
                "m3-map-object-" + Guid.NewGuid().ToString("N"),
                commandBus.State.revision,
                objectId,
                kind,
                x,
                y,
                rotation,
                action));
            LastDirtyBounds = M3GridViewport.FullMapBounds(State.map.width, State.map.height);
            return receipt;
        }

        private static M1CommandReceipt RejectedReceipt(string message)
        {
            return new M1CommandReceipt
            {
                accepted = false,
                message = message,
                revisionBefore = 0,
                revisionAfter = 0
            };
        }

        public bool Undo()
        {
            var undone = commandBus.Undo();
            if (undone)
            {
                UpdateDirtyBoundsFromLastChangeSet();
            }

            return undone;
        }

        public bool Redo()
        {
            var redone = commandBus.Redo();
            if (redone)
            {
                UpdateDirtyBoundsFromLastChangeSet();
            }

            return redone;
        }

        private void UpdateDirtyBoundsFromLastChangeSet()
        {
            var changeSet = commandBus.LastChangeSet;
            if (changeSet == null || !changeSet.HasMapBounds)
            {
                LastDirtyBounds = M3GridViewport.FullMapBounds(State.map.width, State.map.height);
                return;
            }

            changeSet.GetMapBounds(out var minX, out var minY, out var maxX, out var maxY);
            LastDirtyBounds = new M3GridBounds(minX, minY, maxX, maxY)
                .ClampToMap(State.map.width, State.map.height);
        }
    }
}
