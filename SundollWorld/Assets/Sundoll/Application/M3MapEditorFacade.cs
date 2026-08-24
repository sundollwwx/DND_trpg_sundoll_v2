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

            if (receipt.accepted)
            {
                var dirtyRegion = new M3DirtyRegion();
                foreach (var mutation in mutations)
                {
                    dirtyRegion.Include(mutation.x, mutation.y);
                }

                LastDirtyBounds = dirtyRegion.Bounds.ClampToMap(State.map.width, State.map.height);
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

        public bool Undo()
        {
            var undone = commandBus.Undo();
            if (undone)
            {
                LastDirtyBounds = M3GridViewport.FullMapBounds(State.map.width, State.map.height);
            }

            return undone;
        }

        public bool Redo()
        {
            var redone = commandBus.Redo();
            if (redone)
            {
                LastDirtyBounds = M3GridViewport.FullMapBounds(State.map.width, State.map.height);
            }

            return redone;
        }
    }
}
