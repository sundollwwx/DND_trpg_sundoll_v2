using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    public sealed class M4UpdatePieceDefinitionCommand : M1Command
    {
        private readonly string definitionId;
        private readonly string displayName;
        private readonly string category;
        private readonly List<string> tags;
        private readonly string assetId;
        private readonly int footprintWidth;
        private readonly int footprintHeight;

        public M4UpdatePieceDefinitionCommand(
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

        public override string Description => "更新棋子定义";
        public override string CommandType => "M4.UpdatePieceDefinition";
        public override int PayloadVersion => 1;
        public override object CreatePayload() => new M4UpdatePieceDefinitionCommandPayload
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
            var definition = M4PieceQueries.FindDefinition(state, definitionId);
            if (definition == null)
            {
                throw new InvalidOperationException("Piece definition was not found: " + definitionId);
            }

            if (!string.IsNullOrWhiteSpace(assetId) && M4PieceQueries.FindAsset(state, assetId) == null)
            {
                throw new InvalidOperationException("Piece asset was not found: " + assetId);
            }

            if (footprintWidth < 1 || footprintHeight < 1)
            {
                throw new InvalidOperationException("Piece footprint must be at least 1x1.");
            }

            definition.displayName = displayName ?? string.Empty;
            definition.category = category ?? string.Empty;
            definition.tags = new List<string>(tags);
            definition.assetId = assetId;
            definition.footprintWidth = footprintWidth;
            definition.footprintHeight = footprintHeight;
        }
    }
}
