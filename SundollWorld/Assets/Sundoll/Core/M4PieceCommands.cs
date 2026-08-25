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
}
