using System;
using System.Collections.Generic;

namespace Sundoll.Application
{
    public sealed class M3LayerEditState
    {
        private readonly HashSet<string> knownLayerIds;
        private readonly List<string> layerOrder = new List<string>();
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

                layerOrder.Add(layerId);
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

        public IReadOnlyList<string> LayerOrder => layerOrder;

        public int LayerCount => layerOrder.Count;

        public int IndexOf(string layerId)
        {
            EnsureKnown(layerId);
            return layerOrder.IndexOf(layerId);
        }

        public void SetLayerOrder(IEnumerable<string> orderedLayerIds)
        {
            if (orderedLayerIds == null)
            {
                throw new ArgumentNullException(nameof(orderedLayerIds));
            }

            var nextOrder = new List<string>();
            foreach (var layerId in orderedLayerIds)
            {
                EnsureKnown(layerId);
                if (nextOrder.Contains(layerId))
                {
                    throw new ArgumentException("Layer order contains duplicates.", nameof(orderedLayerIds));
                }

                nextOrder.Add(layerId);
            }

            if (nextOrder.Count != layerOrder.Count)
            {
                throw new ArgumentException("Layer order must contain every known layer.", nameof(orderedLayerIds));
            }

            layerOrder.Clear();
            layerOrder.AddRange(nextOrder);
        }

        public bool MoveLayer(string layerId, int direction)
        {
            EnsureKnown(layerId);
            if (direction == 0)
            {
                return false;
            }

            var index = layerOrder.IndexOf(layerId);
            var target = Math.Max(0, Math.Min(layerOrder.Count - 1, index + Math.Sign(direction)));
            if (target == index)
            {
                return false;
            }

            layerOrder[index] = layerOrder[target];
            layerOrder[target] = layerId;
            return true;
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
