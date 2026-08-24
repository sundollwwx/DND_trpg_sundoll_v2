using System;
using System.Collections.Generic;
using System.Text;
using Sundoll.Core;

namespace Sundoll.Infrastructure
{
    public static class M2CanonicalStateHasher
    {
        public static string Compute(M1WorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var builder = new StringBuilder(2048);
            var layerAware = HasExplicitLayerIds(state);
            AppendInt(builder, state.schemaVersion);
            AppendInt(builder, state.revision);
            AppendProject(builder, state.project);
            AppendMap(builder, state.map, layerAware);
            AppendPublishedMap(builder, state.publishedMap, layerAware);
            AppendScenario(builder, state.scenario);
            AppendBoard(builder, state.board);
            AppendPieceDefinition(builder, state.pieceDefinition);
            AppendPieceInstance(builder, state.pieceInstance);
            return M2FileIO.Sha256Utf8(builder.ToString());
        }

        private static void AppendProject(StringBuilder builder, M1ProjectDocument project)
        {
            AppendString(builder, project == null ? null : project.id);
            AppendString(builder, project == null ? null : project.displayName);
            AppendInt(builder, project == null ? 0 : project.schemaVersion);
        }

        private static void AppendMap(StringBuilder builder, M1MapDocument map, bool layerAware)
        {
            AppendString(builder, map == null ? null : map.id);
            AppendInt(builder, map == null ? 0 : map.width);
            AppendInt(builder, map == null ? 0 : map.height);
            AppendCells(builder, map == null ? null : map.cells, layerAware);
        }

        private static void AppendPublishedMap(StringBuilder builder, M1MapContentVersion map, bool layerAware)
        {
            AppendString(builder, map == null ? null : map.id);
            AppendString(builder, map == null ? null : map.sourceMapId);
            AppendInt(builder, map == null ? 0 : map.contentRevision);
            AppendCells(builder, map == null ? null : map.cells, layerAware);
        }

        private static void AppendScenario(StringBuilder builder, M1ScenarioDocument scenario)
        {
            AppendString(builder, scenario == null ? null : scenario.id);
            AppendString(builder, scenario == null ? null : scenario.publishedMapContentId);
            AppendString(builder, scenario == null ? null : scenario.boardId);
        }

        private static void AppendBoard(StringBuilder builder, M1BoardInstance board)
        {
            AppendString(builder, board == null ? null : board.id);
            AppendString(builder, board == null ? null : board.scenarioId);
            AppendString(builder, board == null ? null : board.publishedMapContentId);
        }

        private static void AppendPieceDefinition(StringBuilder builder, M1PieceDefinition piece)
        {
            AppendString(builder, piece == null ? null : piece.id);
            AppendString(builder, piece == null ? null : piece.displayName);
            AppendString(builder, piece == null ? null : piece.visualKey);
        }

        private static void AppendPieceInstance(StringBuilder builder, M1PieceInstance piece)
        {
            AppendString(builder, piece == null ? null : piece.id);
            AppendString(builder, piece == null ? null : piece.definitionId);
            if (piece == null || piece.location == null)
            {
                AppendInt(builder, -1);
                AppendString(builder, null);
                AppendInt(builder, 0);
                AppendInt(builder, 0);
                return;
            }

            AppendInt(builder, (int)piece.location.kind);
            AppendString(builder, piece.location.boardId);
            AppendInt(builder, piece.location.x);
            AppendInt(builder, piece.location.y);
        }

        private static void AppendCells(StringBuilder builder, List<M1MapCell> cells, bool layerAware)
        {
            var sorted = new List<M1MapCell>();
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    if (cell != null)
                    {
                        sorted.Add(cell);
                    }
                }
            }

            sorted.Sort(CompareCells);
            AppendInt(builder, sorted.Count);
            foreach (var cell in sorted)
            {
                AppendInt(builder, cell.x);
                AppendInt(builder, cell.y);
                if (layerAware)
                {
                    AppendString(builder, M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId));
                }
                AppendString(builder, cell.contentId);
            }
        }

        private static int CompareCells(M1MapCell left, M1MapCell right)
        {
            var result = left.x.CompareTo(right.x);
            if (result != 0)
            {
                return result;
            }

            result = left.y.CompareTo(right.y);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(left.layerId, right.layerId);
            if (result != 0)
            {
                return result;
            }

            return string.CompareOrdinal(left.contentId, right.contentId);
        }

        private static bool HasExplicitLayerIds(M1WorldState state)
        {
            return HasExplicitLayerIds(state.map == null ? null : state.map.cells) ||
                   HasExplicitLayerIds(state.publishedMap == null ? null : state.publishedMap.cells);
        }

        private static bool HasExplicitLayerIds(List<M1MapCell> cells)
        {
            if (cells == null)
            {
                return false;
            }

            foreach (var cell in cells)
            {
                if (cell != null && !string.IsNullOrWhiteSpace(cell.layerId))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendInt(StringBuilder builder, int value)
        {
            builder.Append(value).Append(';');
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("-1:");
                return;
            }

            builder.Append(value.Length).Append(':').Append(value).Append(';');
        }
    }
}
