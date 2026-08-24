using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    public static class M3MapLayerIds
    {
        public const string Terrain = "terrain";
        public const string Wall = "wall";
        public const string Object = "object";
        public const string Interaction = "interaction";
        public const string StaticAnnotation = "static-annotation";

        public static string InferLayerId(string contentId)
        {
            if (string.IsNullOrEmpty(contentId))
            {
                return Terrain;
            }

            if (contentId.StartsWith("wall-", StringComparison.Ordinal))
            {
                return Wall;
            }

            if (contentId.StartsWith("object-", StringComparison.Ordinal))
            {
                return Object;
            }

            if (contentId.StartsWith("interaction-", StringComparison.Ordinal))
            {
                return Interaction;
            }

            if (contentId.StartsWith("annotation-", StringComparison.Ordinal) ||
                contentId.StartsWith("static-annotation-", StringComparison.Ordinal))
            {
                return StaticAnnotation;
            }

            return Terrain;
        }

        public static string NormalizeLayerId(string layerId, string contentId)
        {
            return string.IsNullOrWhiteSpace(layerId) ? InferLayerId(contentId) : layerId;
        }

        public static int RenderPriority(string layerId)
        {
            switch (layerId)
            {
                case StaticAnnotation:
                    return 5;
                case Interaction:
                    return 4;
                case Object:
                    return 3;
                case Wall:
                    return 2;
                default:
                    return 1;
            }
        }
    }

    public struct M3MapCellKey : IEquatable<M3MapCellKey>
    {
        public readonly int x;
        public readonly int y;
        public readonly string layerId;

        public M3MapCellKey(int x, int y, string layerId)
        {
            this.x = x;
            this.y = y;
            this.layerId = layerId;
        }

        public bool Equals(M3MapCellKey other)
        {
            return x == other.x && y == other.y &&
                   string.Equals(layerId, other.layerId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is M3MapCellKey && Equals((M3MapCellKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = x;
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ (layerId == null ? 0 : StringComparer.Ordinal.GetHashCode(layerId));
                return hash;
            }
        }
    }

    [Serializable]
    public sealed class M3CellMutation
    {
        public int x;
        public int y;
        public string layerId;
        public string contentId;
        public bool erase;

        public M3CellMutation(int x, int y, string contentId, bool erase)
            : this(x, y, M3MapLayerIds.InferLayerId(contentId), contentId, erase)
        {
        }

        public M3CellMutation(int x, int y, string layerId, string contentId, bool erase)
        {
            this.x = x;
            this.y = y;
            this.layerId = M3MapLayerIds.NormalizeLayerId(layerId, contentId);
            this.contentId = contentId;
            this.erase = erase;
        }
    }

    [Serializable]
    public sealed class M3MapCellDelta
    {
        public int x;
        public int y;
        public string layerId;
        public bool beforeExists;
        public string beforeContentId;
        public bool afterExists;
        public string afterContentId;

        public M3MapCellKey Key => new M3MapCellKey(x, y, layerId);
    }

    [Serializable]
    public sealed class WorldChangeSet
    {
        public int formatVersion = 1;
        public List<M3MapCellDelta> mapCellDeltas = new List<M3MapCellDelta>();

        public WorldChangeSet()
        {
        }

        public WorldChangeSet(IEnumerable<M3MapCellDelta> deltas)
        {
            if (deltas == null)
            {
                throw new ArgumentNullException(nameof(deltas));
            }

            foreach (var delta in deltas)
            {
                if (delta == null)
                {
                    throw new ArgumentException("World change set cannot contain a null delta.", nameof(deltas));
                }

                mapCellDeltas.Add(new M3MapCellDelta
                {
                    x = delta.x,
                    y = delta.y,
                    layerId = delta.layerId,
                    beforeExists = delta.beforeExists,
                    beforeContentId = delta.beforeContentId,
                    afterExists = delta.afterExists,
                    afterContentId = delta.afterContentId
                });
            }
        }

        public int MapCellDeltaCount => mapCellDeltas.Count;
        public bool HasMapBounds => mapCellDeltas.Count > 0;

        public void GetMapBounds(out int minX, out int minY, out int maxX, out int maxY)
        {
            if (!HasMapBounds)
            {
                throw new InvalidOperationException("World change set has no map bounds.");
            }

            minX = maxX = mapCellDeltas[0].x;
            minY = maxY = mapCellDeltas[0].y;
            for (var index = 1; index < mapCellDeltas.Count; index++)
            {
                var delta = mapCellDeltas[index];
                minX = Math.Min(minX, delta.x);
                minY = Math.Min(minY, delta.y);
                maxX = Math.Max(maxX, delta.x);
                maxY = Math.Max(maxY, delta.y);
            }
        }

        public void ApplyForward(M1WorldState state)
        {
            Apply(state, true);
        }

        public void ApplyInverse(M1WorldState state)
        {
            Apply(state, false);
        }

        private void Apply(M1WorldState state, bool forward)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.map == null)
            {
                throw new InvalidOperationException("Map does not exist.");
            }

            for (var offset = 0; offset < mapCellDeltas.Count; offset++)
            {
                var index = forward ? offset : mapCellDeltas.Count - 1 - offset;
                var delta = mapCellDeltas[index];
                var exists = forward ? delta.afterExists : delta.beforeExists;
                var contentId = forward ? delta.afterContentId : delta.beforeContentId;
                if (exists)
                {
                    state.map.SetRuntimeCell(delta.Key, contentId);
                }
                else
                {
                    state.map.RemoveRuntimeCell(delta.Key);
                }
            }
        }
    }

    public sealed class M3PaintCellsCommand : M1Command, IWorldChangeSetCommand
    {
        private readonly List<M3CellMutation> mutations;

        public M3PaintCellsCommand(string commandId, int baseRevision, IEnumerable<M3CellMutation> mutations)
            : base(commandId, baseRevision)
        {
            if (mutations == null)
            {
                throw new ArgumentNullException(nameof(mutations));
            }

            this.mutations = new List<M3CellMutation>();
            foreach (var mutation in mutations)
            {
                if (mutation == null)
                {
                    throw new ArgumentException("Cell mutations cannot contain null entries.", nameof(mutations));
                }

                this.mutations.Add(new M3CellMutation(
                    mutation.x,
                    mutation.y,
                    mutation.layerId,
                    mutation.contentId,
                    mutation.erase));
            }

            if (this.mutations.Count == 0)
            {
                throw new ArgumentException("At least one cell mutation is required.", nameof(mutations));
            }
        }

        public override string Description => $"编辑 {mutations.Count} 个格子";

        public override void Apply(M1WorldState state)
        {
            CreateChangeSet(state).ApplyForward(state);
        }

        public WorldChangeSet CreateChangeSet(M1WorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.map == null)
            {
                throw new InvalidOperationException("Map does not exist.");
            }

            ValidateMutations(state.map);

            var deltasByKey = new Dictionary<M3MapCellKey, M3MapCellDelta>();
            var orderedDeltas = new List<M3MapCellDelta>();
            foreach (var mutation in mutations)
            {
                var key = new M3MapCellKey(mutation.x, mutation.y, mutation.layerId);
                if (!deltasByKey.TryGetValue(key, out var delta))
                {
                    var beforeExists = state.map.TryGetRuntimeCell(key, out var beforeCell);
                    delta = new M3MapCellDelta
                    {
                        x = mutation.x,
                        y = mutation.y,
                        layerId = mutation.layerId,
                        beforeExists = beforeExists,
                        beforeContentId = beforeExists ? beforeCell.contentId : null
                    };
                    deltasByKey.Add(key, delta);
                    orderedDeltas.Add(delta);
                }

                delta.afterExists = !mutation.erase;
                delta.afterContentId = mutation.erase ? null : mutation.contentId;
            }

            return new WorldChangeSet(orderedDeltas);
        }

        private void ValidateMutations(M1MapDocument map)
        {
            foreach (var mutation in mutations)
            {
                if (mutation.x < 0 || mutation.x >= map.width || mutation.y < 0 || mutation.y >= map.height)
                {
                    throw new InvalidOperationException($"Cell ({mutation.x}, {mutation.y}) is outside the map.");
                }

                if (string.IsNullOrWhiteSpace(mutation.layerId))
                {
                    throw new InvalidOperationException("A cell mutation requires a layer ID.");
                }

                if (!mutation.erase && string.IsNullOrWhiteSpace(mutation.contentId))
                {
                    throw new InvalidOperationException("A painted cell requires a content ID.");
                }
            }
        }

    }
}
