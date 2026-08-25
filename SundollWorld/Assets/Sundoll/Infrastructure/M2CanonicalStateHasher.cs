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
            return ComputeInternal(state, true);
        }

        public static bool MatchesStoredHash(M1WorldState state, string expectedHash)
        {
            if (state == null || string.IsNullOrWhiteSpace(expectedHash))
            {
                return false;
            }

            if (string.Equals(Compute(state), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Schema 1 development saves predate map objects. Keep their
            // canonical bytes valid without mutating the old files; schema 2
            // writes always use the current hash above.
            return state.schemaVersion == 1 &&
                   string.Equals(ComputeLegacySchema1(state), expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeLegacySchema1(M1WorldState state)
        {
            return ComputeInternal(state, false);
        }

        private static string ComputeInternal(M1WorldState state, bool includeMapObjects)
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
            AppendMap(builder, state.map, layerAware, includeMapObjects);
            AppendPublishedMap(builder, state.publishedMap, layerAware, includeMapObjects);
            AppendScenario(builder, state.scenario);
            AppendBoard(builder, state.board);
            AppendPieceDefinition(builder, state.pieceDefinition);
            AppendPieceInstance(builder, state.pieceInstance);
            if (HasM4Data(state))
            {
                AppendM4Pieces(builder, state);
            }
            return M2FileIO.Sha256Utf8(builder.ToString());
        }

        private static void AppendProject(StringBuilder builder, M1ProjectDocument project)
        {
            AppendString(builder, project == null ? null : project.id);
            AppendString(builder, project == null ? null : project.displayName);
            AppendInt(builder, project == null ? 0 : project.schemaVersion);
        }

        private static void AppendMap(StringBuilder builder, M1MapDocument map, bool layerAware, bool includeMapObjects)
        {
            AppendString(builder, map == null ? null : map.id);
            AppendInt(builder, map == null ? 0 : map.width);
            AppendInt(builder, map == null ? 0 : map.height);
            AppendCells(builder, map == null ? null : map.cells, layerAware);
            if (includeMapObjects)
            {
                AppendObjects(builder, map == null ? null : map.objects);
            }
        }

        private static void AppendPublishedMap(StringBuilder builder, M1MapContentVersion map, bool layerAware, bool includeMapObjects)
        {
            AppendString(builder, map == null ? null : map.id);
            AppendString(builder, map == null ? null : map.sourceMapId);
            AppendInt(builder, map == null ? 0 : map.contentRevision);
            AppendCells(builder, map == null ? null : map.cells, layerAware);
            if (includeMapObjects)
            {
                AppendObjects(builder, map == null ? null : map.objects);
            }
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

        private static bool HasM4Data(M1WorldState state)
        {
            return state.pieceAssets != null && state.pieceAssets.Count > 0 ||
                   state.pieceDefinitions != null && state.pieceDefinitions.Count > 0 ||
                   state.pieceInstances != null && state.pieceInstances.Count > 0;
        }

        private static void AppendM4Pieces(StringBuilder builder, M1WorldState state)
        {
            var assets = new List<M4PieceAsset>();
            if (state.pieceAssets != null)
            {
                foreach (var asset in state.pieceAssets)
                {
                    if (asset != null)
                    {
                        assets.Add(asset);
                    }
                }
            }

            assets.Sort((left, right) => string.CompareOrdinal(left.id, right.id));
            AppendInt(builder, assets.Count);
            foreach (var asset in assets)
            {
                AppendOptionalString(builder, asset.id);
                AppendOptionalString(builder, asset.sha256);
                AppendOptionalString(builder, asset.extension);
                AppendOptionalString(builder, asset.mimeType);
                AppendLong(builder, asset.byteLength);
                AppendOptionalString(builder, asset.relativePath);
                AppendOptionalString(builder, asset.thumbnailSha256);
                AppendOptionalString(builder, asset.thumbnailRelativePath);
            }

            var definitions = new List<M4PieceDefinition>();
            if (state.pieceDefinitions != null)
            {
                foreach (var definition in state.pieceDefinitions)
                {
                    if (definition != null)
                    {
                        definitions.Add(definition);
                    }
                }
            }

            definitions.Sort((left, right) => string.CompareOrdinal(left.id, right.id));
            AppendInt(builder, definitions.Count);
            foreach (var definition in definitions)
            {
                AppendOptionalString(builder, definition.id);
                AppendOptionalString(builder, definition.displayName);
                AppendOptionalString(builder, definition.category);
                AppendOptionalString(builder, definition.assetId);
                AppendInt(builder, definition.footprintWidth);
                AppendInt(builder, definition.footprintHeight);
                var tags = definition.tags == null ? new List<string>() : new List<string>(definition.tags);
                tags.Sort(StringComparer.Ordinal);
                AppendInt(builder, tags.Count);
                foreach (var tag in tags)
                {
                    AppendOptionalString(builder, tag);
                }
            }

            var instances = new List<M4PieceInstance>();
            if (state.pieceInstances != null)
            {
                foreach (var instance in state.pieceInstances)
                {
                    if (instance != null)
                    {
                        instances.Add(instance);
                    }
                }
            }

            instances.Sort((left, right) => string.CompareOrdinal(left.id, right.id));
            AppendInt(builder, instances.Count);
            foreach (var instance in instances)
            {
                AppendOptionalString(builder, instance.id);
                AppendOptionalString(builder, instance.definitionId);
                AppendInt(builder, M4PieceInstance.NormalizeRotation(instance.rotation));
                AppendInt(builder, instance.flipped ? 1 : 0);
                AppendInt(builder, instance.visible ? 1 : 0);
                var location = instance.location;
                AppendInt(builder, location == null ? -1 : (int)location.kind);
                AppendOptionalString(builder, location == null ? null : location.boardId);
                AppendInt(builder, location == null ? 0 : location.x);
                AppendInt(builder, location == null ? 0 : location.y);
                AppendOptionalString(builder, location == null ? null : location.containerPieceId);
                AppendOptionalString(builder, location == null ? null : location.attachedToPieceId);
                AppendOptionalString(builder, location == null ? null : location.attachmentSlot);
                AppendInt(builder, location == null ? 0 : location.stackOrder);
            }
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

        private static void AppendObjects(StringBuilder builder, List<M3MapObject> objects)
        {
            var sorted = new List<M3MapObject>();
            if (objects != null)
            {
                foreach (var mapObject in objects)
                {
                    if (mapObject != null)
                    {
                        sorted.Add(mapObject);
                    }
                }
            }

            sorted.Sort((left, right) => string.CompareOrdinal(left.id, right.id));
            AppendInt(builder, sorted.Count);
            foreach (var mapObject in sorted)
            {
                AppendString(builder, mapObject.id);
                AppendInt(builder, (int)mapObject.kind);
                AppendInt(builder, mapObject.x);
                AppendInt(builder, mapObject.y);
                AppendInt(builder, M3MapObject.NormalizeRotation(mapObject.rotation));
                AppendInt(builder, (int)mapObject.state);
            }
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

        private static void AppendLong(StringBuilder builder, long value)
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

        private static void AppendOptionalString(StringBuilder builder, string value)
        {
            AppendString(builder, string.IsNullOrEmpty(value) ? null : value);
        }
    }
}
