using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    internal static class M4PieceCommandSupport
    {
        public static void EnsureLists(M1WorldState state)
        {
            state.EnsureSchema2Defaults();
        }

        public static void RequireId(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(name + " is required.");
            }
        }

        public static M4PieceInstance RequireInstance(M1WorldState state, string instanceId)
        {
            var instance = M4PieceQueries.FindInstance(state, instanceId);
            if (instance == null)
            {
                throw new InvalidOperationException("Piece instance was not found: " + instanceId);
            }

            if (instance.location == null)
            {
                instance.location = M4PieceLocation.Unplaced();
            }

            return instance;
        }

        public static void RequireProposedLocation(M1WorldState state, string instanceId, M4PieceLocation location)
        {
            if (!M4PieceStateValidator.TryValidateLocation(
                    state,
                    instanceId,
                    location,
                    state.pieceInstances,
                    out var diagnostic))
            {
                throw new InvalidOperationException(diagnostic);
            }
        }

        public static bool ContainsInstance(M1WorldState state, string instanceId)
        {
            return M4PieceQueries.FindInstance(state, instanceId) != null;
        }
    }

    public sealed class M4RegisterPieceAssetCommand : M1Command
    {
        private readonly M4PieceAsset asset;

        public M4RegisterPieceAssetCommand(string commandId, int baseRevision, M4PieceAsset asset)
            : base(commandId, baseRevision)
        {
            this.asset = asset == null ? null : asset.DeepClone();
        }

        public override string Description => "登记棋子视觉资产";
        public override string CommandType => "M4.RegisterPieceAsset";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => asset == null ? null : new M4RegisterPieceAssetCommandPayload
        {
            assetId = asset.id,
            sha256 = asset.sha256,
            extension = asset.extension,
            mimeType = asset.mimeType,
            byteLength = asset.byteLength,
            relativePath = asset.relativePath,
            thumbnailSha256 = asset.thumbnailSha256,
            thumbnailRelativePath = asset.thumbnailRelativePath
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            if (asset == null)
            {
                throw new InvalidOperationException("Piece asset is required.");
            }

            M4PieceCommandSupport.RequireId(asset.id, "Asset ID");
            if (M4PieceQueries.FindAsset(state, asset.id) != null)
            {
                throw new InvalidOperationException("Piece asset already exists: " + asset.id);
            }

            state.pieceAssets.Add(asset.DeepClone());
        }
    }

    public sealed class M4CreatePieceDefinitionCommand : M1Command
    {
        private readonly string definitionId;
        private readonly string displayName;
        private readonly string category;
        private readonly List<string> tags;
        private readonly string assetId;
        private readonly int footprintWidth;
        private readonly int footprintHeight;

        public M4CreatePieceDefinitionCommand(
            string commandId,
            int baseRevision,
            string definitionId,
            string displayName,
            string category,
            IEnumerable<string> tags,
            string assetId,
            int footprintWidth = 1,
            int footprintHeight = 1)
            : base(commandId, baseRevision)
        {
            this.definitionId = definitionId;
            this.displayName = displayName;
            this.category = category;
            this.tags = tags == null ? new List<string>() : new List<string>(tags);
            this.assetId = assetId;
            this.footprintWidth = footprintWidth;
            this.footprintHeight = footprintHeight;
        }

        public override string Description => "创建棋子定义";
        public override string CommandType => "M4.CreatePieceDefinition";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4CreatePieceDefinitionCommandPayload
        {
            definitionId = definitionId,
            displayName = displayName,
            category = category,
            tags = new List<string>(tags),
            assetId = assetId,
            footprintWidth = footprintWidth,
            footprintHeight = footprintHeight
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            M4PieceCommandSupport.RequireId(definitionId, "Definition ID");
            if (M4PieceQueries.FindDefinition(state, definitionId) != null)
            {
                throw new InvalidOperationException("Piece definition already exists: " + definitionId);
            }

            if (!string.IsNullOrWhiteSpace(assetId) && M4PieceQueries.FindAsset(state, assetId) == null)
            {
                throw new InvalidOperationException("Piece asset was not found: " + assetId);
            }

            if (footprintWidth < 1 || footprintHeight < 1)
            {
                throw new InvalidOperationException("Piece footprint must be at least 1x1.");
            }

            state.pieceDefinitions.Add(new M4PieceDefinition
            {
                id = definitionId,
                displayName = displayName ?? string.Empty,
                category = category ?? string.Empty,
                tags = new List<string>(tags),
                assetId = assetId,
                footprintWidth = footprintWidth,
                footprintHeight = footprintHeight
            });
        }
    }

    public sealed class M4CreatePieceInstanceCommand : M1Command
    {
        private readonly string definitionId;
        private readonly string instanceId;

        public M4CreatePieceInstanceCommand(string commandId, int baseRevision, string definitionId, string instanceId)
            : base(commandId, baseRevision)
        {
            this.definitionId = definitionId;
            this.instanceId = instanceId;
        }

        public override string Description => "创建棋子实例";
        public override string CommandType => "M4.CreatePieceInstance";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4CreatePieceInstanceCommandPayload
        {
            definitionId = definitionId,
            instanceId = instanceId
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            M4PieceCommandSupport.RequireId(instanceId, "Instance ID");
            M4PieceCommandSupport.RequireId(definitionId, "Definition ID");
            if (M4PieceQueries.FindDefinition(state, definitionId) == null)
            {
                throw new InvalidOperationException("Piece definition was not found: " + definitionId);
            }

            if (M4PieceQueries.FindInstance(state, instanceId) != null)
            {
                throw new InvalidOperationException("Piece instance already exists: " + instanceId);
            }

            state.pieceInstances.Add(new M4PieceInstance
            {
                id = instanceId,
                definitionId = definitionId,
                location = M4PieceLocation.Unplaced(),
                visible = true
            });
        }
    }

    public sealed class M4PlacePieceCommand : M1Command
    {
        private readonly string instanceId;
        private readonly int x;
        private readonly int y;

        public M4PlacePieceCommand(string commandId, int baseRevision, string instanceId, int x, int y)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
            this.x = x;
            this.y = y;
        }

        public override string Description => $"放置棋子到 ({x}, {y})";
        public override string CommandType => "M4.PlacePiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4PlacePieceCommandPayload { instanceId = instanceId, x = x, y = y };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            var location = M4PieceLocation.OnBoard(
                state.board == null ? null : state.board.id,
                x,
                y,
                M4PieceQueries.NextStackOrder(state, state.board == null ? null : state.board.id, x, y, instanceId));
            M4PieceCommandSupport.RequireProposedLocation(state, instanceId, location);
            instance.location = location;
        }
    }

    public sealed class M4MovePieceCommand : M1Command
    {
        private readonly string instanceId;
        private readonly int x;
        private readonly int y;

        public M4MovePieceCommand(string commandId, int baseRevision, string instanceId, int x, int y)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
            this.x = x;
            this.y = y;
        }

        public override string Description => $"移动棋子到 ({x}, {y})";
        public override string CommandType => "M4.MovePiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4MovePieceCommandPayload { instanceId = instanceId, x = x, y = y };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            if (instance.location.kind != M1PieceLocationKind.OnBoard)
            {
                throw new InvalidOperationException("Only an on-board piece can move.");
            }

            var location = M4PieceLocation.OnBoard(
                instance.location.boardId,
                x,
                y,
                M4PieceQueries.NextStackOrder(state, instance.location.boardId, x, y, instanceId));
            M4PieceCommandSupport.RequireProposedLocation(state, instanceId, location);
            instance.location = location;
        }
    }

    /// <summary>
    /// Moves a selection as one authoritative transaction. Every proposed
    /// destination is validated before the first instance is changed, so an
    /// invalid drag cannot leave a partially moved selection behind.
    /// </summary>
    public sealed class M4MovePiecesCommand : M1Command
    {
        private readonly List<M4PieceMoveMutation> mutations;

        public M4MovePiecesCommand(
            string commandId,
            int baseRevision,
            IEnumerable<M4PieceMoveMutation> mutations)
            : base(commandId, baseRevision)
        {
            if (mutations == null)
            {
                throw new ArgumentNullException(nameof(mutations));
            }

            this.mutations = new List<M4PieceMoveMutation>();
            foreach (var mutation in mutations)
            {
                this.mutations.Add(mutation == null ? null : mutation.DeepClone());
            }
        }

        public int MutationCount => mutations.Count;
        public override string Description => "移动棋子（" + mutations.Count + "个）";
        public override string CommandType => "M4.MovePieces";
        public override int PayloadVersion => 1;

        public override object CreatePayload()
        {
            var payload = new M4MovePiecesCommandPayload();
            foreach (var mutation in mutations)
            {
                payload.mutations.Add(mutation == null ? null : mutation.DeepClone());
            }

            return payload;
        }

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            if (mutations.Count == 0)
            {
                throw new InvalidOperationException("A multi-piece move must contain at least one destination.");
            }

            var movedIds = new HashSet<string>(StringComparer.Ordinal);
            var instances = new List<M4PieceInstance>();
            foreach (var mutation in mutations)
            {
                if (mutation == null || string.IsNullOrWhiteSpace(mutation.instanceId) || !movedIds.Add(mutation.instanceId))
                {
                    throw new InvalidOperationException("A multi-piece move must contain unique piece IDs.");
                }

                var instance = M4PieceCommandSupport.RequireInstance(state, mutation.instanceId);
                if (instance.location == null || instance.location.kind != M1PieceLocationKind.OnBoard)
                {
                    throw new InvalidOperationException("Only on-board pieces can be moved as a selection.");
                }

                var proposed = M4PieceLocation.OnBoard(instance.location.boardId, mutation.x, mutation.y, 0);
                M4PieceCommandSupport.RequireProposedLocation(state, instance.id, proposed);
                instances.Add(instance);
            }

            // Allocate stack orders after excluding every selected source. This
            // keeps a selection dropped onto one cell deterministic and avoids
            // accidental duplicate orders from the source positions.
            var nextStackByCell = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var candidate in state.pieceInstances)
            {
                if (candidate == null || movedIds.Contains(candidate.id) || candidate.location == null ||
                    candidate.location.kind != M1PieceLocationKind.OnBoard)
                {
                    continue;
                }

                var key = StackKey(candidate.location.boardId, candidate.location.x, candidate.location.y);
                var next = candidate.location.stackOrder + 1;
                if (!nextStackByCell.TryGetValue(key, out var current) || next > current)
                {
                    nextStackByCell[key] = next;
                }
            }

            for (var index = 0; index < mutations.Count; index++)
            {
                var mutation = mutations[index];
                var instance = instances[index];
                var key = StackKey(instance.location.boardId, mutation.x, mutation.y);
                if (!nextStackByCell.TryGetValue(key, out var stackOrder))
                {
                    stackOrder = 0;
                }

                instance.location = M4PieceLocation.OnBoard(instance.location.boardId, mutation.x, mutation.y, stackOrder);
                nextStackByCell[key] = stackOrder + 1;
            }
        }

        private static string StackKey(string boardId, int x, int y)
        {
            return (boardId ?? string.Empty) + ":" + x + ":" + y;
        }
    }

    public sealed class M4MovePieceToContainerCommand : M1Command
    {
        private readonly string instanceId;
        private readonly string containerPieceId;

        public M4MovePieceToContainerCommand(string commandId, int baseRevision, string instanceId, string containerPieceId)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
            this.containerPieceId = containerPieceId;
        }

        public override string Description => "将棋子收入容器";
        public override string CommandType => "M4.MovePieceToContainer";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4MovePieceToContainerCommandPayload
        {
            instanceId = instanceId,
            containerPieceId = containerPieceId
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            if (!M4PieceCommandSupport.ContainsInstance(state, containerPieceId))
            {
                throw new InvalidOperationException("Container piece was not found: " + containerPieceId);
            }

            var location = M4PieceLocation.InContainer(containerPieceId);
            M4PieceCommandSupport.RequireProposedLocation(state, instanceId, location);
            instance.location = location;
        }
    }

    public sealed class M4AttachPieceCommand : M1Command
    {
        private readonly string instanceId;
        private readonly string targetPieceId;
        private readonly string attachmentSlot;

        public M4AttachPieceCommand(string commandId, int baseRevision, string instanceId, string targetPieceId, string attachmentSlot)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
            this.targetPieceId = targetPieceId;
            this.attachmentSlot = attachmentSlot;
        }

        public override string Description => "附着棋子";
        public override string CommandType => "M4.AttachPiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4AttachPieceCommandPayload
        {
            instanceId = instanceId,
            targetPieceId = targetPieceId,
            attachmentSlot = attachmentSlot
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            if (!M4PieceCommandSupport.ContainsInstance(state, targetPieceId))
            {
                throw new InvalidOperationException("Attachment target was not found: " + targetPieceId);
            }

            var location = M4PieceLocation.Attached(targetPieceId, attachmentSlot ?? string.Empty);
            M4PieceCommandSupport.RequireProposedLocation(state, instanceId, location);
            instance.location = location;
        }
    }

    public sealed class M4DetachPieceCommand : M1Command
    {
        private readonly string instanceId;

        public M4DetachPieceCommand(string commandId, int baseRevision, string instanceId)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
        }

        public override string Description => "解除棋子关系";
        public override string CommandType => "M4.DetachPiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4DetachPieceCommandPayload { instanceId = instanceId };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            if (instance.location.kind != M1PieceLocationKind.InContainer &&
                instance.location.kind != M1PieceLocationKind.Attached)
            {
                throw new InvalidOperationException("Only a contained or attached piece can be detached.");
            }

            instance.location = M4PieceLocation.Unplaced();
        }
    }

    public sealed class M4SetPiecePresentationCommand : M1Command
    {
        private readonly string instanceId;
        private readonly int rotation;
        private readonly bool flipped;
        private readonly bool visible;

        public M4SetPiecePresentationCommand(string commandId, int baseRevision, string instanceId, int rotation, bool flipped, bool visible)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
            this.rotation = rotation;
            this.flipped = flipped;
            this.visible = visible;
        }

        public override string Description => "更新棋子显示状态";
        public override string CommandType => "M4.SetPiecePresentation";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4SetPiecePresentationCommandPayload
        {
            instanceId = instanceId,
            rotation = rotation,
            flipped = flipped,
            visible = visible
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            if (M4PieceInstance.NormalizeRotation(rotation) != rotation)
            {
                throw new InvalidOperationException("Piece rotation must be 0, 90, 180 or 270 degrees.");
            }

            instance.rotation = rotation;
            instance.flipped = flipped;
            instance.visible = visible;
        }
    }

    /// <summary>
    /// Applies rotation, flip, or visibility changes to a selection in one
    /// undo/Journaling operation. It deliberately carries complete resulting
    /// presentation state, not a UI-relative action such as "rotate now".
    /// </summary>
    public sealed class M4SetPiecePresentationsCommand : M1Command
    {
        private readonly List<M4PiecePresentationMutation> mutations;

        public M4SetPiecePresentationsCommand(
            string commandId,
            int baseRevision,
            IEnumerable<M4PiecePresentationMutation> mutations)
            : base(commandId, baseRevision)
        {
            if (mutations == null)
            {
                throw new ArgumentNullException(nameof(mutations));
            }

            this.mutations = new List<M4PiecePresentationMutation>();
            foreach (var mutation in mutations)
            {
                this.mutations.Add(mutation == null ? null : mutation.DeepClone());
            }
        }

        public int MutationCount => mutations.Count;
        public override string Description => "更新棋子显示（" + mutations.Count + "个）";
        public override string CommandType => "M4.SetPiecePresentations";
        public override int PayloadVersion => 1;

        public override object CreatePayload()
        {
            var payload = new M4SetPiecePresentationsCommandPayload();
            foreach (var mutation in mutations)
            {
                payload.mutations.Add(mutation == null ? null : mutation.DeepClone());
            }

            return payload;
        }

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            if (mutations.Count == 0)
            {
                throw new InvalidOperationException("A piece presentation update must contain at least one piece.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var instances = new List<M4PieceInstance>();
            foreach (var mutation in mutations)
            {
                if (mutation == null || string.IsNullOrWhiteSpace(mutation.instanceId) || !seen.Add(mutation.instanceId))
                {
                    throw new InvalidOperationException("A piece presentation update must contain unique piece IDs.");
                }

                if (M4PieceInstance.NormalizeRotation(mutation.rotation) != mutation.rotation)
                {
                    throw new InvalidOperationException("Piece rotation must be 0, 90, 180 or 270 degrees.");
                }

                instances.Add(M4PieceCommandSupport.RequireInstance(state, mutation.instanceId));
            }

            for (var index = 0; index < mutations.Count; index++)
            {
                var mutation = mutations[index];
                var instance = instances[index];
                instance.rotation = mutation.rotation;
                instance.flipped = mutation.flipped;
                instance.visible = mutation.visible;
            }
        }
    }

    public sealed class M4SetPieceStackOrderCommand : M1Command
    {
        private readonly string instanceId;
        private readonly int requestedStackOrder;

        public M4SetPieceStackOrderCommand(string commandId, int baseRevision, string instanceId, int requestedStackOrder)
            : base(commandId, baseRevision)
        {
            this.instanceId = instanceId;
            this.requestedStackOrder = requestedStackOrder;
        }

        public override string Description => "调整棋子堆叠顺序";
        public override string CommandType => "M4.SetPieceStackOrder";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4SetPieceStackOrderCommandPayload
        {
            instanceId = instanceId,
            stackOrder = requestedStackOrder
        };

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            var instance = M4PieceCommandSupport.RequireInstance(state, instanceId);
            if (instance.location == null || instance.location.kind != M1PieceLocationKind.OnBoard)
            {
                throw new InvalidOperationException("Only an on-board piece can change stack order.");
            }

            var sameCell = new List<M4PieceInstance>();
            foreach (var candidate in state.pieceInstances)
            {
                if (candidate != null && candidate.location != null &&
                    candidate.location.kind == M1PieceLocationKind.OnBoard &&
                    candidate.location.boardId == instance.location.boardId &&
                    candidate.location.x == instance.location.x &&
                    candidate.location.y == instance.location.y)
                {
                    sameCell.Add(candidate);
                }
            }

            sameCell.Sort((left, right) =>
            {
                var result = left.location.stackOrder.CompareTo(right.location.stackOrder);
                return result != 0 ? result : string.CompareOrdinal(left.id, right.id);
            });
            sameCell.Remove(instance);
            var targetIndex = Math.Max(0, Math.Min(sameCell.Count, requestedStackOrder));
            sameCell.Insert(targetIndex, instance);
            for (var index = 0; index < sameCell.Count; index++)
            {
                sameCell[index].location.stackOrder = index;
            }
        }
    }

    /// <summary>
    /// Removes one or more instances while retaining their reusable piece
    /// definitions and assets. A selection cannot delete a target that still
    /// owns an unselected contained or attached instance; this turns a likely
    /// data-loss gesture into a clear atomic rejection instead.
    /// </summary>
    public sealed class M4DeletePiecesCommand : M1Command
    {
        private readonly List<string> instanceIds;

        public M4DeletePiecesCommand(string commandId, int baseRevision, IEnumerable<string> instanceIds)
            : base(commandId, baseRevision)
        {
            if (instanceIds == null)
            {
                throw new ArgumentNullException(nameof(instanceIds));
            }

            this.instanceIds = new List<string>();
            foreach (var instanceId in instanceIds)
            {
                this.instanceIds.Add(instanceId);
            }
        }

        public int InstanceCount => instanceIds.Count;
        public override string Description => "删除棋子（" + instanceIds.Count + "个）";
        public override string CommandType => "M4.DeletePieces";
        public override int PayloadVersion => 1;

        public override object CreatePayload()
        {
            return new M4DeletePiecesCommandPayload { instanceIds = new List<string>(instanceIds) };
        }

        public override void Apply(M1WorldState state)
        {
            M4PieceCommandSupport.EnsureLists(state);
            if (instanceIds.Count == 0)
            {
                throw new InvalidOperationException("A delete operation must contain at least one piece.");
            }

            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instanceId in instanceIds)
            {
                if (string.IsNullOrWhiteSpace(instanceId) || !removed.Add(instanceId))
                {
                    throw new InvalidOperationException("A delete operation must contain unique piece IDs.");
                }

                M4PieceCommandSupport.RequireInstance(state, instanceId);
            }

            foreach (var instance in state.pieceInstances)
            {
                if (instance == null || removed.Contains(instance.id) || instance.location == null)
                {
                    continue;
                }

                var targetId = instance.location.kind == M1PieceLocationKind.InContainer
                    ? instance.location.containerPieceId
                    : instance.location.kind == M1PieceLocationKind.Attached
                        ? instance.location.attachedToPieceId
                        : null;
                if (!string.IsNullOrWhiteSpace(targetId) && removed.Contains(targetId))
                {
                    throw new InvalidOperationException(
                        "Cannot delete a piece while an unselected piece is contained in or attached to it.");
                }
            }

            state.pieceInstances.RemoveAll(instance => instance != null && removed.Contains(instance.id));
        }
    }
}
