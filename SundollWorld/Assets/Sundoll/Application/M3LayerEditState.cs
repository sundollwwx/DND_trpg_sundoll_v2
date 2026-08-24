using System;
using System.Collections.Generic;

namespace Sundoll.Application
{
    public sealed class M3LayerEditState
    {
        private readonly HashSet<string> knownLayerIds;
        private readonly HashSet<string> hiddenLayerIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> lockedLayerIds = new HashSet<string>(StringComparer.Ordinal);

        public M3LayerEditState(IEnumerable<string> layerIds)
        {
            if (layerIds == null)
            {
                throw new ArgumentNullException(nameof(layerIds));
            }

            knownLayerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layerId in layerIds)
            {
                if (string.IsNullOrWhiteSpace(layerId) || !knownLayerIds.Add(layerId))
                {
                    throw new ArgumentException("Layer IDs must be non-empty and unique.", nameof(layerIds));
                }
            }

            if (knownLayerIds.Count == 0)
            {
                throw new ArgumentException("At least one layer ID is required.", nameof(layerIds));
            }
        }

        public bool IsVisible(string layerId)
        {
            EnsureKnown(layerId);
            return !hiddenLayerIds.Contains(layerId);
        }

        public bool IsLocked(string layerId)
        {
            EnsureKnown(layerId);
            return lockedLayerIds.Contains(layerId);
        }

        public bool CanEdit(string layerId)
        {
            return !IsLocked(layerId);
        }

        public void SetVisible(string layerId, bool visible)
        {
            EnsureKnown(layerId);
            if (visible)
            {
                hiddenLayerIds.Remove(layerId);
            }
            else
            {
                hiddenLayerIds.Add(layerId);
            }
        }

        public void SetLocked(string layerId, bool locked)
        {
            EnsureKnown(layerId);
            if (locked)
            {
                lockedLayerIds.Add(layerId);
            }
            else
            {
                lockedLayerIds.Remove(layerId);
            }
        }

        public bool ToggleVisible(string layerId)
        {
            var visible = !IsVisible(layerId);
            SetVisible(layerId, visible);
            return visible;
        }

        public bool ToggleLocked(string layerId)
        {
            var locked = !IsLocked(layerId);
            SetLocked(layerId, locked);
            return locked;
        }

        private void EnsureKnown(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId) || !knownLayerIds.Contains(layerId))
            {
                throw new ArgumentException("Unknown map layer: " + layerId, nameof(layerId));
            }
        }
    }
}
