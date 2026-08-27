using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    /// <summary>
    /// A content-addressed visual asset. The file may be missing on the current
    /// machine; the definition and piece instances must remain usable then.
    /// </summary>
    [Serializable]
    public sealed class M4PieceAsset
    {
        public string id;
        public string sha256;
        public string extension;
        public string mimeType;
        public long byteLength;
        public string relativePath;
        public string thumbnailSha256;
        public string thumbnailRelativePath;

        public M4PieceAsset DeepClone()
        {
            return new M4PieceAsset
            {
                id = id,
                sha256 = sha256,
                extension = extension,
                mimeType = mimeType,
                byteLength = byteLength,
                relativePath = relativePath,
                thumbnailSha256 = thumbnailSha256,
                thumbnailRelativePath = thumbnailRelativePath
            };
        }
    }

    [Serializable]
    public sealed class M4PieceDefinition
    {
        public string id;
        public string displayName;
        public string category;
        public List<string> tags = new List<string>();
        public string assetId;
        public int footprintWidth = 1;
        public int footprintHeight = 1;

        public M4PieceDefinition DeepClone()
        {
            return new M4PieceDefinition
            {
                id = id,
                displayName = displayName,
                category = category,
                tags = tags == null ? new List<string>() : new List<string>(tags),
                assetId = assetId,
                footprintWidth = footprintWidth,
                footprintHeight = footprintHeight
            };
        }
    }

    [Serializable]
    public sealed class M4PieceLocation
    {
        public M1PieceLocationKind kind = M1PieceLocationKind.Unplaced;
        public string boardId;
        public int x;
        public int y;
        public string containerPieceId;
        public string attachedToPieceId;
        public string attachmentSlot;
        public int stackOrder;

        public M4PieceLocation DeepClone()
        {
            return new M4PieceLocation
            {
                kind = kind,
                boardId = boardId,
                x = x,
                y = y,
                containerPieceId = containerPieceId,
                attachedToPieceId = attachedToPieceId,
                attachmentSlot = attachmentSlot,
                stackOrder = stackOrder
            };
        }

        public static M4PieceLocation Unplaced()
        {
            return new M4PieceLocation();
        }

        public static M4PieceLocation OnBoard(string boardId, int x, int y, int stackOrder)
        {
            return new M4PieceLocation
            {
                kind = M1PieceLocationKind.OnBoard,
                boardId = boardId,
                x = x,
                y = y,
                stackOrder = Math.Max(0, stackOrder)
            };
        }

        public static M4PieceLocation InContainer(string containerPieceId)
        {
            return new M4PieceLocation
            {
                kind = M1PieceLocationKind.InContainer,
                containerPieceId = containerPieceId
            };
        }

        public static M4PieceLocation Attached(string targetPieceId, string attachmentSlot)
        {
            return new M4PieceLocation
            {
                kind = M1PieceLocationKind.Attached,
                attachedToPieceId = targetPieceId,
                attachmentSlot = attachmentSlot
            };
        }
    }

    [Serializable]
    public sealed class M4PieceInstance
    {
        public string id;
        public string definitionId;
        public M4PieceLocation location = new M4PieceLocation();
        public int rotation;
        public bool flipped;
        public bool visible = true;

        public M4PieceInstance DeepClone()
        {
            return new M4PieceInstance
            {
                id = id,
                definitionId = definitionId,
                location = location == null ? null : location.DeepClone(),
                rotation = NormalizeRotation(rotation),
                flipped = flipped,
                visible = visible
            };
        }

        public static int NormalizeRotation(int value)
        {
            var normalized = value % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized == 0 || normalized == 90 || normalized == 180 || normalized == 270
                ? normalized
                : 0;
        }
    }

    public static class M4PieceStateValidator
    {
        public static bool TryValidate(M1WorldState state, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (state == null)
            {
                diagnostic = "World state is null.";
                return false;
            }

            var assets = state.pieceAssets ?? new List<M4PieceAsset>();
            var definitions = state.pieceDefinitions ?? new List<M4PieceDefinition>();
            var instances = state.pieceInstances ?? new List<M4PieceInstance>();
            var assetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.id) || !assetIds.Add(asset.id))
                {
                    diagnostic = "Piece assets must have unique non-empty IDs.";
                    return false;
                }
            }

            var definitionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.id) || !definitionIds.Add(definition.id))
                {
                    diagnostic = "Piece definitions must have unique non-empty IDs.";
                    return false;
                }

                if (definition.footprintWidth < 1 || definition.footprintHeight < 1)
                {
                    diagnostic = "Piece footprint must be at least 1x1.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(definition.assetId) && !assetIds.Contains(definition.assetId))
                {
                    diagnostic = "Piece definition references an unknown asset: " + definition.assetId;
                    return false;
                }
            }

            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instance in instances)
            {
                if (instance == null || string.IsNullOrWhiteSpace(instance.id) || !instanceIds.Add(instance.id))
                {
                    diagnostic = "Piece instances must have unique non-empty IDs.";
                    return false;
                }

                if (!definitionIds.Contains(instance.definitionId))
                {
                    diagnostic = "Piece instance references an unknown definition: " + instance.definitionId;
                    return false;
                }

                if (instance.location == null || !TryValidateLocation(state, instance.id, instance.location, instances, out diagnostic))
                {
                    return false;
                }

                if (M4PieceInstance.NormalizeRotation(instance.rotation) != instance.rotation)
                {
                    diagnostic = "Piece rotation must be 0, 90, 180 or 270 degrees.";
                    return false;
                }
            }

            return true;
        }

        public static bool TryValidateLocation(
            M1WorldState state,
            string instanceId,
            M4PieceLocation location,
            IReadOnlyList<M4PieceInstance> instances,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (location == null)
            {
                diagnostic = "Piece location is required.";
                return false;
            }

            var instanceLookup = new Dictionary<string, M4PieceInstance>(StringComparer.Ordinal);
            if (instances != null)
            {
                foreach (var instance in instances)
                {
                    if (instance != null && !string.IsNullOrWhiteSpace(instance.id))
                    {
                        instanceLookup[instance.id] = instance;
                    }
                }
            }

            switch (location.kind)
            {
                case M1PieceLocationKind.Unplaced:
                    return true;
                case M1PieceLocationKind.OnBoard:
                    if (state.board == null || string.IsNullOrWhiteSpace(location.boardId) || location.boardId != state.board.id)
                    {
                        diagnostic = "An on-board piece must reference the active board.";
                        return false;
                    }

                    if (state.map != null &&
                        (location.x < 0 || location.x >= state.map.width || location.y < 0 || location.y >= state.map.height))
                    {
                        diagnostic = "Piece location is outside the map.";
                        return false;
                    }

                    if (location.stackOrder < 0)
                    {
                        diagnostic = "Piece stack order cannot be negative.";
                        return false;
                    }

                    return true;
                case M1PieceLocationKind.InContainer:
                    if (string.IsNullOrWhiteSpace(location.containerPieceId) ||
                        location.containerPieceId == instanceId ||
                        !instanceLookup.ContainsKey(location.containerPieceId))
                    {
                        diagnostic = "A contained piece must reference another piece.";
                        return false;
                    }

                    return !HasRelationCycle(instanceId, location.containerPieceId, instanceLookup, false, out diagnostic);
                case M1PieceLocationKind.Attached:
                    if (string.IsNullOrWhiteSpace(location.attachedToPieceId) ||
                        location.attachedToPieceId == instanceId ||
                        !instanceLookup.ContainsKey(location.attachedToPieceId))
                    {
                        diagnostic = "An attached piece must reference another piece.";
                        return false;
                    }

                    return !HasRelationCycle(instanceId, location.attachedToPieceId, instanceLookup, true, out diagnostic);
                default:
                    diagnostic = "Unknown piece location kind.";
                    return false;
            }
        }

        private static bool HasRelationCycle(
            string instanceId,
            string targetId,
            Dictionary<string, M4PieceInstance> instances,
            bool attached,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            var visited = new HashSet<string>(StringComparer.Ordinal) { instanceId };
            var currentId = targetId;
            while (!string.IsNullOrWhiteSpace(currentId) && instances.TryGetValue(currentId, out var current))
            {
                if (!visited.Add(currentId))
                {
                    diagnostic = "Piece container/attachment relationships cannot contain a cycle.";
                    return true;
                }

                if (current.location == null)
                {
                    break;
                }

                if (current.location.kind == M1PieceLocationKind.InContainer)
                {
                    currentId = current.location.containerPieceId;
                }
                else if (current.location.kind == M1PieceLocationKind.Attached)
                {
                    currentId = current.location.attachedToPieceId;
                }
                else
                {
                    break;
                }
            }

            return false;
        }
    }

    public static class M4PieceQueries
    {
        public static M4PieceAsset FindAsset(M1WorldState state, string assetId)
        {
            if (state == null || state.pieceAssets == null)
            {
                return null;
            }

            foreach (var asset in state.pieceAssets)
            {
                if (asset != null && asset.id == assetId)
                {
                    return asset;
                }
            }

            return null;
        }

        public static M4PieceDefinition FindDefinition(M1WorldState state, string definitionId)
        {
            if (state == null || state.pieceDefinitions == null)
            {
                return null;
            }

            foreach (var definition in state.pieceDefinitions)
            {
                if (definition != null && definition.id == definitionId)
                {
                    return definition;
                }
            }

            return null;
        }

        public static M4PieceInstance FindInstance(M1WorldState state, string instanceId)
        {
            if (state == null || state.pieceInstances == null)
            {
                return null;
            }

            foreach (var instance in state.pieceInstances)
            {
                if (instance != null && instance.id == instanceId)
                {
                    return instance;
                }
            }

            return null;
        }

        public static M4PieceInstance FindTopmostBoardInstanceAt(
            M1WorldState state,
            string boardId,
            int x,
            int y,
            bool visibleOnly = true)
        {
            if (state == null || state.pieceInstances == null || string.IsNullOrWhiteSpace(boardId))
            {
                return null;
            }

            M4PieceInstance topmost = null;
            var topmostStackOrder = int.MinValue;
            foreach (var instance in state.pieceInstances)
            {
                if (instance == null || string.IsNullOrWhiteSpace(instance.id) ||
                    (visibleOnly && !instance.visible) || instance.location == null ||
                    instance.location.kind != M1PieceLocationKind.OnBoard ||
                    instance.location.boardId != boardId || instance.location.x != x || instance.location.y != y)
                {
                    continue;
                }

                if (topmost == null || instance.location.stackOrder >= topmostStackOrder)
                {
                    topmost = instance;
                    topmostStackOrder = instance.location.stackOrder;
                }
            }

            return topmost;
        }

        public static int NextStackOrder(M1WorldState state, string boardId, int x, int y, string ignoredInstanceId = null)
        {
            var next = 0;
            if (state == null || state.pieceInstances == null)
            {
                return next;
            }

            foreach (var instance in state.pieceInstances)
            {
                if (instance == null || instance.id == ignoredInstanceId || instance.location == null ||
                    instance.location.kind != M1PieceLocationKind.OnBoard || instance.location.boardId != boardId ||
                    instance.location.x != x || instance.location.y != y)
                {
                    continue;
                }

                next = Math.Max(next, instance.location.stackOrder + 1);
            }

            return next;
        }
    }
}
