using System;
using System.Collections.Generic;
using Sundoll.Core;

namespace Sundoll.Application
{
    /// <summary>
    /// Keeps the map content index stable between revisions and applies accepted
    /// paint batches to only the cells they touched. A full rebuild remains the
    /// safe fallback for map replacement, undo/redo, or an unexpected revision.
    /// </summary>
    public sealed class M3ContentLookupCache
    {
        private readonly Dictionary<M3MapCellKey, string> contentByCell =
            new Dictionary<M3MapCellKey, string>();

        private M1MapDocument sourceMap;
        private int sourceRevision = -1;
        private int sourceWidth = -1;
        private int sourceHeight = -1;

        public int FullRebuildCount { get; private set; }
        public int IncrementalUpdateCount { get; private set; }
        public int LastUpdatedCellCount { get; private set; }

        public bool IsCurrent(M1MapDocument map, int revision)
        {
            return map != null && ReferenceEquals(sourceMap, map) &&
                   sourceRevision == revision && sourceWidth == map.width &&
                   sourceHeight == map.height;
        }

        public IReadOnlyDictionary<M3MapCellKey, string> ContentByCell => contentByCell;

        public bool TryApplyIncremental(
            M1MapDocument map,
            int revision,
            IList<M3CellMutation> mutations)
        {
            if (map == null || mutations == null ||
                !ReferenceEquals(sourceMap, map) || sourceRevision < 0 ||
                sourceWidth != map.width || sourceHeight != map.height ||
                revision != sourceRevision + 1)
            {
                return false;
            }

            foreach (var mutation in mutations)
            {
                if (mutation == null)
                {
                    return false;
                }

                var layerId = M3MapLayerIds.NormalizeLayerId(mutation.layerId, mutation.contentId);
                var key = new M3MapCellKey(mutation.x, mutation.y, layerId);
                if (mutation.erase)
                {
                    contentByCell.Remove(key);
                }
                else
                {
                    contentByCell[key] = mutation.contentId;
                }
            }

            sourceRevision = revision;
            IncrementalUpdateCount++;
            LastUpdatedCellCount = mutations.Count;
            return true;
        }

        public void Rebuild(M1MapDocument map, int revision)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            contentByCell.Clear();
            foreach (var cell in map.cells)
            {
                if (cell == null)
                {
                    continue;
                }

                var layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                contentByCell[new M3MapCellKey(cell.x, cell.y, layerId)] = cell.contentId;
            }

            sourceMap = map;
            sourceRevision = revision;
            sourceWidth = map.width;
            sourceHeight = map.height;
            FullRebuildCount++;
            LastUpdatedCellCount = contentByCell.Count;
        }

        public void Invalidate()
        {
            sourceMap = null;
            sourceRevision = -1;
            sourceWidth = -1;
            sourceHeight = -1;
            LastUpdatedCellCount = 0;
        }
    }
}
