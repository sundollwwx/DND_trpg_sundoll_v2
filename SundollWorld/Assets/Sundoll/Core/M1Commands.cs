using System;

namespace Sundoll.Core
{
    public interface IWorldChangeSetCommand
    {
        WorldChangeSet CreateChangeSet(M1WorldState state);
    }

    public abstract class M1Command
    {
        protected M1Command(string commandId, int baseRevision)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("Command ID is required.", nameof(commandId));
            }

            CommandId = commandId;
            BaseRevision = baseRevision;
        }

        public string CommandId { get; }
        public int BaseRevision { get; }
        public virtual string CommandType => null;
        public virtual int PayloadVersion => 0;
        public virtual object CreatePayload() => null;
        public abstract string Description { get; }
        public abstract void Apply(M1WorldState state);
    }

    public sealed class M1CreateProjectCommand : M1Command
    {
        private readonly string projectId;
        private readonly string projectName;
        private readonly string mapId;

        public M1CreateProjectCommand(string commandId, int baseRevision, string projectId, string projectName, string mapId)
            : base(commandId, baseRevision)
        {
            this.projectId = projectId;
            this.projectName = projectName;
            this.mapId = mapId;
        }

        public override string Description => "创建项目与空白地图";
        public override string CommandType => "M1.CreateProject";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1CreateProjectCommandPayload
        {
            projectId = projectId,
            projectName = projectName,
            mapId = mapId
        };

        public override void Apply(M1WorldState state)
        {
            if (state.project != null || state.map != null)
            {
                throw new InvalidOperationException("Project already exists.");
            }

            state.project = new M1ProjectDocument { id = projectId, displayName = projectName };
            state.map = new M1MapDocument { id = mapId, width = 8, height = 8 };
        }
    }

    public sealed class M1PaintCellCommand : M1Command
    {
        private readonly int x;
        private readonly int y;
        private readonly string contentId;

        public M1PaintCellCommand(string commandId, int baseRevision, int x, int y, string contentId)
            : base(commandId, baseRevision)
        {
            this.x = x;
            this.y = y;
            this.contentId = contentId;
        }

        public override string Description => $"绘制格子 ({x}, {y})";
        public override string CommandType => "M1.PaintCell";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1PaintCellCommandPayload
        {
            x = x,
            y = y,
            contentId = contentId
        };

        public override void Apply(M1WorldState state)
        {
            if (state.map == null)
            {
                throw new InvalidOperationException("Map does not exist.");
            }

            if (x < 0 || x >= state.map.width || y < 0 || y >= state.map.height)
            {
                throw new InvalidOperationException("Cell is outside the map.");
            }

            var layerId = M3MapLayerIds.InferLayerId(contentId);
            foreach (var cell in state.map.cells)
            {
                if (cell == null)
                {
                    continue;
                }

                var existingLayerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId);
                if (cell.x == x && cell.y == y && existingLayerId == layerId)
                {
                    cell.layerId = layerId;
                    cell.contentId = contentId;
                    return;
                }
            }

            state.map.cells.Add(new M1MapCell
            {
                x = x,
                y = y,
                layerId = layerId,
                contentId = contentId
            });
        }
    }

    public sealed class M1PublishMapContentCommand : M1Command
    {
        private readonly string contentVersionId;

        public M1PublishMapContentCommand(string commandId, int baseRevision, string contentVersionId)
            : base(commandId, baseRevision)
        {
            this.contentVersionId = contentVersionId;
        }

        public override string Description => "发布地图内容版本";
        public override string CommandType => "M1.PublishMapContent";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1PublishMapContentCommandPayload
        {
            contentVersionId = contentVersionId
        };

        public override void Apply(M1WorldState state)
        {
            if (state.map == null)
            {
                throw new InvalidOperationException("Map does not exist.");
            }

            state.publishedMap = new M1MapContentVersion
            {
                id = contentVersionId,
                sourceMapId = state.map.id,
                contentRevision = state.revision + 1,
                cells = new System.Collections.Generic.List<M1MapCell>()
            };

            foreach (var cell in state.map.cells)
            {
                state.publishedMap.cells.Add(new M1MapCell
                {
                    x = cell.x,
                    y = cell.y,
                    layerId = M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId),
                    contentId = cell.contentId
                });
            }
        }
    }

    public sealed class M1CreateScenarioCommand : M1Command
    {
        private readonly string scenarioId;
        private readonly string boardId;

        public M1CreateScenarioCommand(string commandId, int baseRevision, string scenarioId, string boardId)
            : base(commandId, baseRevision)
        {
            this.scenarioId = scenarioId;
            this.boardId = boardId;
        }

        public override string Description => "创建场景与棋盘实例";
        public override string CommandType => "M1.CreateScenario";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1CreateScenarioCommandPayload
        {
            scenarioId = scenarioId,
            boardId = boardId
        };

        public override void Apply(M1WorldState state)
        {
            if (state.publishedMap == null)
            {
                throw new InvalidOperationException("A published map is required.");
            }

            state.scenario = new M1ScenarioDocument
            {
                id = scenarioId,
                publishedMapContentId = state.publishedMap.id,
                boardId = boardId
            };
            state.board = new M1BoardInstance
            {
                id = boardId,
                scenarioId = scenarioId,
                publishedMapContentId = state.publishedMap.id
            };
        }
    }

    public sealed class M1CreatePieceCommand : M1Command
    {
        private readonly string definitionId;
        private readonly string instanceId;
        private readonly string displayName;
        private readonly string visualKey;

        public M1CreatePieceCommand(
            string commandId,
            int baseRevision,
            string definitionId,
            string instanceId,
            string displayName,
            string visualKey)
            : base(commandId, baseRevision)
        {
            this.definitionId = definitionId;
            this.instanceId = instanceId;
            this.displayName = displayName;
            this.visualKey = visualKey;
        }

        public override string Description => "创建几何占位棋子";
        public override string CommandType => "M1.CreatePiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1CreatePieceCommandPayload
        {
            definitionId = definitionId,
            instanceId = instanceId,
            displayName = displayName,
            visualKey = visualKey
        };

        public override void Apply(M1WorldState state)
        {
            if (state.board == null)
            {
                throw new InvalidOperationException("A board is required.");
            }

            state.pieceDefinition = new M1PieceDefinition
            {
                id = definitionId,
                displayName = displayName,
                visualKey = visualKey
            };
            state.pieceInstance = new M1PieceInstance
            {
                id = instanceId,
                definitionId = definitionId,
                location = new M1PieceLocation { kind = M1PieceLocationKind.Unplaced }
            };
        }
    }

    public sealed class M1PlacePieceCommand : M1Command
    {
        private readonly int x;
        private readonly int y;

        public M1PlacePieceCommand(string commandId, int baseRevision, int x, int y)
            : base(commandId, baseRevision)
        {
            this.x = x;
            this.y = y;
        }

        public override string Description => $"放置棋子到 ({x}, {y})";
        public override string CommandType => "M1.PlacePiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1PlacePieceCommandPayload { x = x, y = y };

        public override void Apply(M1WorldState state)
        {
            if (state.board == null || state.pieceInstance == null)
            {
                throw new InvalidOperationException("A board and piece are required.");
            }

            state.pieceInstance.location = new M1PieceLocation
            {
                kind = M1PieceLocationKind.OnBoard,
                boardId = state.board.id,
                x = x,
                y = y
            };
        }
    }

    public sealed class M1MovePieceCommand : M1Command
    {
        private readonly int x;
        private readonly int y;

        public M1MovePieceCommand(string commandId, int baseRevision, int x, int y)
            : base(commandId, baseRevision)
        {
            this.x = x;
            this.y = y;
        }

        public override string Description => $"移动棋子到 ({x}, {y})";
        public override string CommandType => "M1.MovePiece";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M1MovePieceCommandPayload { x = x, y = y };

        public override void Apply(M1WorldState state)
        {
            if (state.board == null || state.pieceInstance == null || state.pieceInstance.location == null)
            {
                throw new InvalidOperationException("A placed piece is required.");
            }

            if (state.pieceInstance.location.kind != M1PieceLocationKind.OnBoard)
            {
                throw new InvalidOperationException("Only an on-board piece can move.");
            }

            state.pieceInstance.location.x = x;
            state.pieceInstance.location.y = y;
            state.pieceInstance.location.boardId = state.board.id;
        }
    }
}
