using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Tests.EditMode
{
    public sealed class M3MapEditorTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "Sundoll-M3-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void BatchPaintIsAtomicAndUndoable()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            var initialRevision = bus.State.revision;

            var receipt = editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(0, 0, "terrain-ground", false),
                new M3CellMutation(1, 0, "wall-solid", false)
            });

            Assert.That(receipt.accepted, Is.True);
            Assert.That(bus.State.revision, Is.EqualTo(initialRevision + 1));
            Assert.That(FindCell(bus.State.map, 0, 0).contentId, Is.EqualTo("terrain-ground"));
            Assert.That(FindCell(bus.State.map, 1, 0).contentId, Is.EqualTo("wall-solid"));

            Assert.That(editor.Undo(), Is.True);
            Assert.That(FindCell(bus.State.map, 0, 0), Is.Null);
            Assert.That(FindCell(bus.State.map, 1, 0), Is.Null);
            Assert.That(editor.Redo(), Is.True);
            Assert.That(FindCell(bus.State.map, 1, 0).contentId, Is.EqualTo("wall-solid"));
        }

        [Test]
        public void InvalidBatchDoesNotPartiallyModifyMap()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);

            Assert.Throws<InvalidOperationException>(() => editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(0, 0, "terrain-ground", false),
                new M3CellMutation(99, 0, "wall-solid", false)
            }));

            Assert.That(FindCell(bus.State.map, 0, 0), Is.Null);
        }

        [Test]
        public void EraseRemovesCellAndPublishCopiesDraft()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            editor.PaintCell(4, 4, "object-marker");
            Assert.That(FindCell(bus.State.map, 4, 4), Is.Not.Null);

            editor.EraseCell(4, 4);
            Assert.That(FindCell(bus.State.map, 4, 4), Is.Null);
            editor.PaintCell(4, 4, "object-marker");
            var publish = editor.PublishMapContent();

            Assert.That(publish.accepted, Is.True);
            Assert.That(FindCell(bus.State.publishedMap, 4, 4).contentId, Is.EqualTo("object-marker"));
        }

        [Test]
        public void MapDraftSurvivesM2SaveAndReload()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var session = M2SaveSession.Open(root, bus.State);
            var editor = new M3MapEditorFacade(bus);
            var receipt = editor.PaintCell(5, 5, "wall-solid");
            Assert.That(receipt.accepted, Is.True);
            session.RecordAccepted(receipt, bus.State);
            session.Save(bus.State);

            var reopened = M2SaveSession.Open(root, bus.State);
            Assert.That(FindCell(reopened.State.map, 5, 5).contentId, Is.EqualTo("wall-solid"));
            Assert.That(M2CanonicalStateHasher.Compute(reopened.State), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
        }

        [Test]
        public void TerrainAndWallCanCoexistAtSameCoordinate()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);

            var receipt = editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(2, 2, M3MapLayerIds.Terrain, "terrain-ground", false),
                new M3CellMutation(2, 2, M3MapLayerIds.Wall, "wall-solid", false)
            });

            Assert.That(receipt.accepted, Is.True);
            Assert.That(FindCell(bus.State.map, 2, 2, M3MapLayerIds.Terrain).contentId, Is.EqualTo("terrain-ground"));
            Assert.That(FindCell(bus.State.map, 2, 2, M3MapLayerIds.Wall).contentId, Is.EqualTo("wall-solid"));
            Assert.That(CountCellsAt(bus.State.map, 2, 2), Is.EqualTo(2));
        }

        [Test]
        public void EraseOnlyRemovesTheSelectedLayer()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(2, 2, M3MapLayerIds.Terrain, "terrain-ground", false),
                new M3CellMutation(2, 2, M3MapLayerIds.Wall, "wall-solid", false)
            });

            var erase = editor.EraseCell(2, 2, M3MapLayerIds.Wall);

            Assert.That(erase.accepted, Is.True);
            Assert.That(FindCell(bus.State.map, 2, 2, M3MapLayerIds.Terrain), Is.Not.Null);
            Assert.That(FindCell(bus.State.map, 2, 2, M3MapLayerIds.Wall), Is.Null);
        }

        [Test]
        public void MultiLayerDraftSurvivesM2SaveAndReloadWithCanonicalHash()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var session = M2SaveSession.Open(root, bus.State);
            var editor = new M3MapEditorFacade(bus);
            var receipt = editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(3, 3, M3MapLayerIds.Terrain, "terrain-ground", false),
                new M3CellMutation(3, 3, M3MapLayerIds.Object, "object-marker", false),
                new M3CellMutation(3, 3, M3MapLayerIds.Interaction, "interaction-door", false)
            });

            session.RecordAccepted(receipt, bus.State);
            session.Save(bus.State);
            var reopened = M2SaveSession.Open(root, bus.State);

            Assert.That(FindCell(reopened.State.map, 3, 3, M3MapLayerIds.Terrain), Is.Not.Null);
            Assert.That(FindCell(reopened.State.map, 3, 3, M3MapLayerIds.Object), Is.Not.Null);
            Assert.That(FindCell(reopened.State.map, 3, 3, M3MapLayerIds.Interaction), Is.Not.Null);
            Assert.That(M2CanonicalStateHasher.Compute(reopened.State), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
        }

        [Test]
        public void PublishRetainsEveryContentLayer()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(1, 1, M3MapLayerIds.Terrain, "terrain-ground", false),
                new M3CellMutation(1, 1, M3MapLayerIds.Wall, "wall-solid", false),
                new M3CellMutation(1, 1, M3MapLayerIds.StaticAnnotation, "annotation-note", false)
            });

            var publish = editor.PublishMapContent();

            Assert.That(publish.accepted, Is.True);
            Assert.That(FindCell(bus.State.publishedMap, 1, 1, M3MapLayerIds.Terrain), Is.Not.Null);
            Assert.That(FindCell(bus.State.publishedMap, 1, 1, M3MapLayerIds.Wall), Is.Not.Null);
            Assert.That(FindCell(bus.State.publishedMap, 1, 1, M3MapLayerIds.StaticAnnotation), Is.Not.Null);
        }

        [Test]
        public void DirtyRegionAndDeltaUndoStayScopedToTheChangedCells()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            var mapReference = bus.State.map;
            var publishedReference = bus.State.publishedMap;

            editor.PaintCells(new List<M3CellMutation>
            {
                new M3CellMutation(1, 2, "terrain-ground", false),
                new M3CellMutation(4, 5, "wall-solid", false)
            });

            Assert.That(editor.LastDirtyBounds.IsEmpty, Is.False);
            Assert.That(editor.LastDirtyBounds.MinX, Is.EqualTo(1));
            Assert.That(editor.LastDirtyBounds.MinY, Is.EqualTo(2));
            Assert.That(editor.LastDirtyBounds.MaxX, Is.EqualTo(4));
            Assert.That(editor.LastDirtyBounds.MaxY, Is.EqualTo(5));
            Assert.That(bus.LastChangeSet.formatVersion, Is.EqualTo(1));
            Assert.That(bus.LastChangeSet.MapCellDeltaCount, Is.EqualTo(2));
            Assert.That(bus.State.map.RuntimeIndexBuildCount, Is.EqualTo(1));

            Assert.That(editor.Undo(), Is.True);
            Assert.That(bus.State.map, Is.SameAs(mapReference));
            Assert.That(bus.State.publishedMap, Is.SameAs(publishedReference));
            Assert.That(editor.LastDirtyBounds.MinX, Is.EqualTo(1));
            Assert.That(editor.LastDirtyBounds.MinY, Is.EqualTo(2));
            Assert.That(editor.LastDirtyBounds.MaxX, Is.EqualTo(4));
            Assert.That(editor.LastDirtyBounds.MaxY, Is.EqualTo(5));
            Assert.That(bus.State.map.RuntimeIndexBuildCount, Is.EqualTo(1));

            Assert.That(editor.Redo(), Is.True);
            Assert.That(bus.State.map, Is.SameAs(mapReference));
            Assert.That(bus.State.publishedMap, Is.SameAs(publishedReference));
            Assert.That(editor.LastDirtyBounds.MinX, Is.EqualTo(1));
            Assert.That(editor.LastDirtyBounds.MaxX, Is.EqualTo(4));
            Assert.That(bus.State.map.RuntimeIndexBuildCount, Is.EqualTo(1));
        }

        [Test]
        public void WorldChangeSetJsonRoundTripAppliesForwardAndInverse()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var beforeHash = M2CanonicalStateHasher.Compute(bus.State);
            var command = new M3PaintCellsCommand(
                "round-trip-delta",
                bus.State.revision,
                new[]
                {
                    new M3CellMutation(2, 3, M3MapLayerIds.Terrain, "terrain-water", false),
                    new M3CellMutation(4, 5, M3MapLayerIds.Wall, "wall-solid", false)
                });

            var serialized = JsonUtility.ToJson(command.CreateChangeSet(bus.State), false);
            var changeSet = JsonUtility.FromJson<WorldChangeSet>(serialized);

            Assert.That(changeSet.formatVersion, Is.EqualTo(1));
            Assert.That(changeSet.MapCellDeltaCount, Is.EqualTo(2));
            changeSet.ApplyForward(bus.State);
            Assert.That(FindCell(bus.State.map, 2, 3, M3MapLayerIds.Terrain).contentId, Is.EqualTo("terrain-water"));
            Assert.That(FindCell(bus.State.map, 4, 5, M3MapLayerIds.Wall).contentId, Is.EqualTo("wall-solid"));

            changeSet.ApplyInverse(bus.State);
            Assert.That(FindCell(bus.State.map, 2, 3, M3MapLayerIds.Terrain).contentId, Is.EqualTo("placeholder-ground"));
            Assert.That(FindCell(bus.State.map, 4, 5, M3MapLayerIds.Wall), Is.Null);
            Assert.That(M2CanonicalStateHasher.Compute(bus.State), Is.EqualTo(beforeHash));
        }

        [Test]
        public void ContentLookupCacheAppliesDirtyCellsWithoutFullRebuild()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var cache = new M3ContentLookupCache();
            cache.Rebuild(bus.State.map, bus.State.revision);
            var initialRevision = bus.State.revision;
            var existingKey = new M3MapCellKey(2, 3, M3MapLayerIds.Terrain);
            Assert.That(cache.ContentByCell[existingKey], Is.EqualTo("placeholder-ground"));

            var mutation = new M3CellMutation(1, 1, M3MapLayerIds.Wall, "wall-solid", false);
            var editor = new M3MapEditorFacade(bus);
            Assert.That(editor.PaintCells(new[] { mutation }).accepted, Is.True);
            Assert.That(cache.TryApplyIncremental(
                bus.State.map,
                bus.State.revision,
                new[] { mutation }), Is.True);

            var wallKey = new M3MapCellKey(1, 1, M3MapLayerIds.Wall);
            Assert.That(cache.ContentByCell[wallKey], Is.EqualTo("wall-solid"));
            Assert.That(cache.ContentByCell[existingKey], Is.EqualTo("placeholder-ground"));
            Assert.That(cache.FullRebuildCount, Is.EqualTo(1));
            Assert.That(cache.IncrementalUpdateCount, Is.EqualTo(1));
            Assert.That(cache.LastUpdatedCellCount, Is.EqualTo(1));

            var erase = new M3CellMutation(1, 1, M3MapLayerIds.Wall, null, true);
            Assert.That(editor.PaintCells(new[] { erase }).accepted, Is.True);
            Assert.That(cache.TryApplyIncremental(
                bus.State.map,
                bus.State.revision,
                new[] { erase }), Is.True);
            Assert.That(cache.ContentByCell.ContainsKey(wallKey), Is.False);
            Assert.That(cache.FullRebuildCount, Is.EqualTo(1));
            Assert.That(bus.State.revision, Is.EqualTo(initialRevision + 2));
        }

        [Test]
        public void VisibleBoundsOnlyIncludesCellsInsideViewport()
        {
            var bounds = M3GridViewport.CalculateVisibleBounds(
                256,
                256,
                100f,
                100f,
                0f,
                0f,
                10f);

            Assert.That(bounds.IsEmpty, Is.False);
            Assert.That(bounds.MinX, Is.EqualTo(0));
            Assert.That(bounds.MaxX, Is.EqualTo(9));
            Assert.That(bounds.MinY, Is.EqualTo(246));
            Assert.That(bounds.MaxY, Is.EqualTo(255));
            Assert.That(bounds.CellCount, Is.EqualTo(100));
            Assert.That(bounds.Contains(9, 246), Is.True);
            Assert.That(bounds.Contains(10, 246), Is.False);
        }

        [Test]
        public void LayerEditStateDefaultsToVisibleAndUnlocked()
        {
            var layerState = new M3LayerEditState(new[]
            {
                M3MapLayerIds.Terrain,
                M3MapLayerIds.Wall
            });

            Assert.That(layerState.IsVisible(M3MapLayerIds.Terrain), Is.True);
            Assert.That(layerState.IsLocked(M3MapLayerIds.Terrain), Is.False);
            Assert.That(layerState.CanEdit(M3MapLayerIds.Wall), Is.True);
        }

        [Test]
        public void LayerEditStateTogglesVisibilityAndLockWithoutChangingContent()
        {
            var layerState = new M3LayerEditState(new[]
            {
                M3MapLayerIds.Terrain,
                M3MapLayerIds.Wall
            });

            Assert.That(layerState.ToggleVisible(M3MapLayerIds.Wall), Is.False);
            Assert.That(layerState.IsVisible(M3MapLayerIds.Wall), Is.False);
            Assert.That(layerState.ToggleLocked(M3MapLayerIds.Wall), Is.True);
            Assert.That(layerState.IsLocked(M3MapLayerIds.Wall), Is.True);
            Assert.That(layerState.CanEdit(M3MapLayerIds.Wall), Is.False);

            layerState.SetVisible(M3MapLayerIds.Wall, true);
            layerState.SetLocked(M3MapLayerIds.Wall, false);
            Assert.That(layerState.IsVisible(M3MapLayerIds.Wall), Is.True);
            Assert.That(layerState.CanEdit(M3MapLayerIds.Wall), Is.True);
        }

        [Test]
        public void LayerEditStateRejectsUnknownLayer()
        {
            var layerState = new M3LayerEditState(new[] { M3MapLayerIds.Terrain });

            Assert.Throws<ArgumentException>(() => layerState.IsVisible(M3MapLayerIds.Wall));
            Assert.Throws<ArgumentException>(() => layerState.SetLocked(M3MapLayerIds.Wall, true));
        }

        [Test]
        public void WorkspaceStateRoundTripsWithoutChangingWorldHash()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var layerIds = new[]
            {
                M3MapLayerIds.Terrain,
                M3MapLayerIds.Wall,
                M3MapLayerIds.Object
            };
            var layerState = new M3LayerEditState(layerIds);
            layerState.SetVisible(M3MapLayerIds.Wall, false);
            layerState.SetLocked(M3MapLayerIds.Object, true);
            var beforeHash = M2CanonicalStateHasher.Compute(bus.State);
            var store = new M3WorkspaceStateStore(root);

            store.Save(bus.State.map.id, layerState, layerIds);
            var loaded = store.Load(bus.State.map.id, layerIds);

            Assert.That(loaded.loaded, Is.True);
            Assert.That(loaded.state.IsVisible(M3MapLayerIds.Wall), Is.False);
            Assert.That(loaded.state.IsLocked(M3MapLayerIds.Object), Is.True);
            Assert.That(M2CanonicalStateHasher.Compute(bus.State), Is.EqualTo(beforeHash));
        }

        [Test]
        public void WorkspaceStateUsesDefaultsWhenMissingOrCorrupt()
        {
            var layerIds = new[] { M3MapLayerIds.Terrain, M3MapLayerIds.Wall };
            var store = new M3WorkspaceStateStore(root);

            var missing = store.Load("map-missing", layerIds);
            Assert.That(missing.loaded, Is.False);
            Assert.That(missing.state.IsVisible(M3MapLayerIds.Terrain), Is.True);
            Assert.That(missing.state.IsLocked(M3MapLayerIds.Wall), Is.False);

            File.WriteAllText(store.StatePath, "{not-json");
            var corrupt = store.Load("map-missing", layerIds);
            Assert.That(corrupt.loaded, Is.False);
            Assert.That(corrupt.diagnostic, Is.Not.Null.And.Not.Empty);
            Assert.That(corrupt.state.IsVisible(M3MapLayerIds.Wall), Is.True);
        }

        [Test]
        public void StrokeRasterizerIncludesEndpointsAndHasNoGaps()
        {
            var points = M3GridStrokeRasterizer.Rasterize(0, 0, 4, 2);

            Assert.That(points.Count, Is.EqualTo(5));
            Assert.That(points[0].x, Is.EqualTo(0));
            Assert.That(points[0].y, Is.EqualTo(0));
            Assert.That(points[points.Count - 1].x, Is.EqualTo(4));
            Assert.That(points[points.Count - 1].y, Is.EqualTo(2));

            for (var index = 1; index < points.Count; index++)
            {
                Assert.That(Math.Abs(points[index].x - points[index - 1].x), Is.LessThanOrEqualTo(1));
                Assert.That(Math.Abs(points[index].y - points[index - 1].y), Is.LessThanOrEqualTo(1));
            }
        }

        [Test]
        public void RasterizedStrokeCommitsAsOneAtomicPaintCommand()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            var initialRevision = bus.State.revision;
            var mutations = new List<M3CellMutation>();

            foreach (var point in M3GridStrokeRasterizer.Rasterize(0, 0, 4, 2))
            {
                mutations.Add(new M3CellMutation(point.x, point.y, "wall-solid", false));
            }

            var receipt = editor.PaintCells(mutations);

            Assert.That(receipt.accepted, Is.True);
            Assert.That(bus.State.revision, Is.EqualTo(initialRevision + 1));
            Assert.That(FindCell(bus.State.map, 0, 0).contentId, Is.EqualTo("wall-solid"));
            Assert.That(FindCell(bus.State.map, 2, 1).contentId, Is.EqualTo("wall-solid"));
            Assert.That(FindCell(bus.State.map, 4, 2).contentId, Is.EqualTo("wall-solid"));

            Assert.That(editor.Undo(), Is.True);
            Assert.That(FindCell(bus.State.map, 0, 0), Is.Null);
            Assert.That(FindCell(bus.State.map, 4, 2), Is.Null);
        }

        [Test]
        public void MediumMapBatchPaintSavesAndReloads()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            bus.State.map.width = 256;
            bus.State.map.height = 256;
            bus.State.map.cells.Clear();
            var session = M2SaveSession.Open(root, bus.State);
            var editor = new M3MapEditorFacade(bus);
            var mutations = new List<M3CellMutation>(256 * 256);

            for (var y = 0; y < 256; y++)
            {
                for (var x = 0; x < 256; x++)
                {
                    mutations.Add(new M3CellMutation(x, y, "terrain-ground", false));
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var receipt = editor.PaintCells(mutations);
            stopwatch.Stop();

            Assert.That(receipt.accepted, Is.True);
            Assert.That(bus.State.map.cells.Count, Is.EqualTo(256 * 256));
            Assert.That(bus.State.map.RuntimeIndexBuildCount, Is.EqualTo(1));
            Assert.That(FindCell(bus.State.map, 0, 0).contentId, Is.EqualTo("terrain-ground"));
            Assert.That(FindCell(bus.State.map, 255, 255).contentId, Is.EqualTo("terrain-ground"));

            session.RecordAccepted(receipt, bus.State);
            var saveStopwatch = Stopwatch.StartNew();
            session.Save(bus.State);
            saveStopwatch.Stop();

            var reopened = M2SaveSession.Open(root, bus.State);
            Assert.That(reopened.State.map.width, Is.EqualTo(256));
            Assert.That(reopened.State.map.height, Is.EqualTo(256));
            Assert.That(reopened.State.map.cells.Count, Is.EqualTo(256 * 256));
            Assert.That(M2CanonicalStateHasher.Compute(reopened.State), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
            TestContext.WriteLine($"256x256 batch apply: {stopwatch.ElapsedMilliseconds} ms; save/reload: {saveStopwatch.ElapsedMilliseconds} ms");
        }

        [Test]
        public void RectangleRasterizerSupportsOutlineAndFill()
        {
            var outline = M3GridShapeRasterizer.RasterizeRectangle(1, 1, 3, 3, false);
            var filled = M3GridShapeRasterizer.RasterizeRectangle(1, 1, 3, 3, true);

            Assert.That(outline.Count, Is.EqualTo(8));
            Assert.That(filled.Count, Is.EqualTo(9));
            Assert.That(ContainsPoint(outline, 1, 1), Is.True);
            Assert.That(ContainsPoint(outline, 2, 2), Is.False);
            Assert.That(ContainsPoint(filled, 2, 2), Is.True);
        }

        [Test]
        public void FloodFillStopsAtDifferentContent()
        {
            var blocked = new HashSet<string> { "1,0", "1,1", "1,2" };
            var filled = M3GridShapeRasterizer.FloodFill(
                3,
                3,
                0,
                1,
                (x, y) => blocked.Contains(x + "," + y) ? "wall-solid" : null);

            Assert.That(filled.Count, Is.EqualTo(3));
            Assert.That(ContainsPoint(filled, 0, 0), Is.True);
            Assert.That(ContainsPoint(filled, 2, 1), Is.False);
        }

        [Test]
        public void ClipboardUsesSelectionOriginAndRejectsOutOfBoundsAsOneBatch()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            var layers = new M3LayerEditState(new[]
            {
                M3MapLayerIds.Terrain,
                M3MapLayerIds.Wall,
                M3MapLayerIds.Object,
                M3MapLayerIds.Interaction,
                M3MapLayerIds.StaticAnnotation
            });
            editor.PaintCells(new[]
            {
                new M3CellMutation(2, 2, M3MapLayerIds.Wall, "wall-solid", false),
                new M3CellMutation(3, 3, M3MapLayerIds.Object, "object-marker", false)
            });

            var clipboard = editor.CopySelection(new M3GridBounds(2, 2, 3, 3), layers);
            Assert.That(clipboard.width, Is.EqualTo(2));
            Assert.That(clipboard.height, Is.EqualTo(2));
            // The demo's legacy placeholder at (2,3) is also inside the
            // selection; all visible layers are intentionally copied.
            Assert.That(clipboard.cells.Count, Is.EqualTo(3));
            Assert.That(clipboard.cells.Exists(cell => cell.offsetX == 0 && cell.offsetY == 0), Is.True);

            var revisionBefore = bus.State.revision;
            var rejected = editor.PasteClipboard(clipboard, 7, 7, layers);
            Assert.That(rejected.accepted, Is.False);
            Assert.That(bus.State.revision, Is.EqualTo(revisionBefore));
            Assert.That(FindCell(bus.State.map, 7, 7, M3MapLayerIds.Wall), Is.Null);

            var pasted = editor.PasteClipboard(clipboard, 4, 4, layers);
            Assert.That(pasted.accepted, Is.True);
            Assert.That(FindCell(bus.State.map, 4, 4, M3MapLayerIds.Wall).contentId, Is.EqualTo("wall-solid"));
            Assert.That(clipboard.RotateClockwise().width, Is.EqualTo(2));
        }

        [Test]
        public void PickUsesTopmostVisibleLayerAndLockedLayerRemainsPickable()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            editor.PaintCells(new[]
            {
                new M3CellMutation(2, 2, M3MapLayerIds.Terrain, "terrain-ground", false),
                new M3CellMutation(2, 2, M3MapLayerIds.Wall, "wall-solid", false),
                new M3CellMutation(2, 2, M3MapLayerIds.Object, "object-marker", false)
            });
            var layers = new M3LayerEditState(new[]
            {
                M3MapLayerIds.Terrain,
                M3MapLayerIds.Wall,
                M3MapLayerIds.Object,
                M3MapLayerIds.Interaction,
                M3MapLayerIds.StaticAnnotation
            });
            layers.SetLocked(M3MapLayerIds.Object, true);

            Assert.That(editor.TryPickTopmost(2, 2, layers, out var picked), Is.True);
            Assert.That(picked.contentId, Is.EqualTo("object-marker"));
            Assert.That(layers.CanEdit(M3MapLayerIds.Object), Is.False);
        }

        [Test]
        public void MapObjectActionIsVersionedUndoableAndPublishedByDeepCopy()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            var added = editor.AddMapObject("door-entrance", M3MapObjectKind.Door, 3, 3, 90);
            Assert.That(added.accepted, Is.True);
            Assert.That(editor.FindMapObject("door-entrance").rotation, Is.EqualTo(90));

            var toggle = editor.ToggleMapObject("door-entrance");
            Assert.That(toggle.accepted, Is.True);
            Assert.That(editor.FindMapObject("door-entrance").state, Is.EqualTo(M3MapObjectOpenState.Open));
            Assert.That(editor.RotateMapObjectClockwise("door-entrance").accepted, Is.True);
            Assert.That(editor.FindMapObject("door-entrance").rotation, Is.EqualTo(180));

            var envelope = M2CommandEnvelopeCodec.Encode(new M3MapObjectCommand(
                "door-close-envelope",
                bus.State.revision,
                "door-entrance",
                M3MapObjectKind.Door,
                0,
                0,
                0,
                M3MapObjectAction.Close));
            Assert.That(M2CommandEnvelopeCodec.Decode(envelope).CommandType, Is.EqualTo("M3.MapObject"));

            Assert.That(editor.PublishMapContent().accepted, Is.True);
            Assert.That(bus.State.publishedMap.objects.Count, Is.EqualTo(1));
            bus.State.map.objects[0].rotation = 270;
            Assert.That(bus.State.publishedMap.objects[0].rotation, Is.EqualTo(180));
            Assert.That(editor.Undo(), Is.True);
        }

        [Test]
        public void LegacySchemaReadSuppliesSchema2ObjectDefaults()
        {
            var legacy = JsonUtility.FromJson<M1WorldState>(
                "{\"schemaVersion\":1,\"map\":{\"id\":\"legacy-map\",\"width\":8,\"height\":8}}" );
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy.schemaVersion, Is.EqualTo(1));
            legacy.EnsureSchema2Defaults();
            Assert.That(legacy.map.cells, Is.Not.Null);
            Assert.That(legacy.map.objects, Is.Not.Null);
            Assert.That(legacy.publishedMap, Is.Not.Null);
            Assert.That(legacy.publishedMap.objects, Is.Not.Null);
        }

        [Test]
        public void WorkspaceStateFormat2RestoresOrderToolAndView()
        {
            var layerIds = new[]
            {
                M3MapLayerIds.Terrain,
                M3MapLayerIds.Wall,
                M3MapLayerIds.Object,
                M3MapLayerIds.Interaction,
                M3MapLayerIds.StaticAnnotation
            };
            var state = new M3LayerEditState(layerIds);
            state.MoveLayer(M3MapLayerIds.Terrain, 1);
            var store = new M3WorkspaceStateStore(root);
            store.Save("map-m3", state, layerIds, "选择", M3MapLayerIds.Object, 12.5f, 4f, 5f);

            var loaded = store.Load("map-m3", layerIds);
            Assert.That(loaded.loaded, Is.True);
            Assert.That(loaded.currentTool, Is.EqualTo("选择"));
            Assert.That(loaded.currentLayerId, Is.EqualTo(M3MapLayerIds.Object));
            Assert.That(loaded.zoom, Is.EqualTo(12.5f));
            Assert.That(loaded.panX, Is.EqualTo(4f));
            Assert.That(loaded.state.LayerOrder[1], Is.EqualTo(M3MapLayerIds.Terrain));
        }

        private static M1MapCell FindCell(M1MapDocument map, int x, int y)
        {
            foreach (var cell in map.cells)
            {
                if (cell != null && cell.x == x && cell.y == y)
                {
                    return cell;
                }
            }

            return null;
        }

        private static M1MapCell FindCell(M1MapDocument map, int x, int y, string layerId)
        {
            foreach (var cell in map.cells)
            {
                if (cell != null && cell.x == x && cell.y == y &&
                    M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId) == layerId)
                {
                    return cell;
                }
            }

            return null;
        }

        private static int CountCellsAt(M1MapDocument map, int x, int y)
        {
            var count = 0;
            foreach (var cell in map.cells)
            {
                if (cell != null && cell.x == x && cell.y == y)
                {
                    count++;
                }
            }

            return count;
        }

        private static M1MapCell FindCell(M1MapContentVersion map, int x, int y)
        {
            foreach (var cell in map.cells)
            {
                if (cell != null && cell.x == x && cell.y == y)
                {
                    return cell;
                }
            }

            return null;
        }

        private static M1MapCell FindCell(M1MapContentVersion map, int x, int y, string layerId)
        {
            foreach (var cell in map.cells)
            {
                if (cell != null && cell.x == x && cell.y == y &&
                    M3MapLayerIds.NormalizeLayerId(cell.layerId, cell.contentId) == layerId)
                {
                    return cell;
                }
            }

            return null;
        }

        private static bool ContainsPoint(List<M3GridPoint> points, int x, int y)
        {
            foreach (var point in points)
            {
                if (point.x == x && point.y == y)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
