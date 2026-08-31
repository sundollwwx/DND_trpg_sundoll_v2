using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using Sundoll.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sundoll.Tests.PlayMode
{
    public sealed class M3WorkbenchPlayModeTests
    {
        [Test]
        public void M7StarterManifestHasUniqueAuditableRecords()
        {
            var manifest = M7StarterContentManifest.CreateBuiltIn();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var mapVisuals = 0;
            var pieceTokens = 0;
            foreach (var record in manifest.Records)
            {
                Assert.That(ids.Add(record.ContentId), Is.True, record.ContentId);
                Assert.That(record.Author, Is.Not.Empty);
                Assert.That(record.Source, Is.Not.Empty);
                Assert.That(record.License, Is.Not.Empty);
                Assert.That(record.Attribution, Is.Not.Empty);
                Assert.That(record.Sha256, Does.Match("^[0-9a-f]{64}$"));
                if (record.Kind == M7StarterContentKind.MapVisual)
                {
                    mapVisuals++;
                }
                else if (record.Kind == M7StarterContentKind.PieceToken)
                {
                    pieceTokens++;
                }
            }

            Assert.That(manifest.Records, Has.Count.EqualTo(55));
            Assert.That(mapVisuals, Is.EqualTo(43));
            Assert.That(pieceTokens, Is.EqualTo(12));
            Assert.That(manifest.PieceDefinitions, Has.Count.EqualTo(12));
        }

        [UnityTest]
        public IEnumerator M7StarterInstallerUsesBlobPipelineAndIsIdempotent()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "Sundoll-M7-Starter-" + Guid.NewGuid().ToString("N"));
            WorkbenchSession session = null;
            try
            {
                var saveSession = M2SaveSession.Open(projectRoot, M1VerticalSlice.CreateDemoBus().State);
                session = new WorkbenchSession(saveSession);
                var manifest = M7StarterContentManifest.CreateBuiltIn();

                var first = StarterContentInstaller.InstallMissing(session, manifest);
                Assert.That(first.Accepted, Is.True, string.Join("; ", first.Diagnostics));
                Assert.That(first.RegisteredAssets, Is.EqualTo(12));
                Assert.That(first.InstalledDefinitions, Is.EqualTo(12));
                Assert.That(session.CommandBus.State.pieceAssets, Has.Count.EqualTo(12));
                Assert.That(session.CommandBus.State.pieceDefinitions, Has.Count.EqualTo(12));
                foreach (var starter in manifest.PieceDefinitions)
                {
                    var definition = M4PieceQueries.FindDefinition(session.CommandBus.State, starter.DefinitionId);
                    var asset = M4PieceQueries.FindAsset(session.CommandBus.State, definition == null ? null : definition.assetId);
                    Assert.That(definition, Is.Not.Null, starter.DefinitionId);
                    Assert.That(asset, Is.Not.Null, starter.DefinitionId);
                    Assert.That(session.PieceAssetCatalog.IsAssetAvailable(asset), Is.True, starter.DefinitionId);
                    Assert.That(session.PieceAssetCatalog.IsThumbnailAvailable(asset), Is.True, starter.DefinitionId);
                }

                var hashBeforeSecondInstall = M2CanonicalStateHasher.Compute(session.CommandBus.State);
                var second = StarterContentInstaller.InstallMissing(session, manifest);
                Assert.That(second.Accepted, Is.True, string.Join("; ", second.Diagnostics));
                Assert.That(second.Changed, Is.False);
                Assert.That(second.SkippedDefinitions, Is.EqualTo(12));
                Assert.That(M2CanonicalStateHasher.Compute(session.CommandBus.State), Is.EqualTo(hashBeforeSecondInstall));

                session.SaveSession.Save(session.CommandBus.State);
                var reloaded = session.SaveSession.Reload();
                Assert.That(
                    M2CanonicalStateHasher.Compute(reloaded.state),
                    Is.EqualTo(M2CanonicalStateHasher.Compute(session.CommandBus.State)));
            }
            finally
            {
                session?.Dispose();
                if (Directory.Exists(projectRoot))
                {
                    Directory.Delete(projectRoot, true);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator M7PieceLibraryGridFiltersVirtualizedRows()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "Sundoll-M7-PieceGrid-" + Guid.NewGuid().ToString("N"));
            WorkbenchSession session = null;
            M7PieceLibraryGridController controller = null;
            try
            {
                session = new WorkbenchSession(M2SaveSession.Open(projectRoot, M1VerticalSlice.CreateDemoBus().State));
                foreach (var definition in new[]
                         {
                             new[] { "grid-red", "红色守卫", "守卫", "红色" },
                             new[] { "grid-blue", "蓝色法师", "法师", "蓝色" },
                             new[] { "grid-green", "绿色守卫", "守卫", "绿色" }
                         })
                {
                    var receipt = session.PieceLibrary.CreateDefinition(
                        definition[0],
                        definition[1],
                        definition[2],
                        new[] { definition[3], "测试" });
                    Assert.That(receipt.accepted, Is.True, receipt.message);
                    session.SaveSession.RecordAccepted(receipt, session.CommandBus.State);
                }

                controller = new M7PieceLibraryGridController(_ => { });
                controller.Bind(session);
                Assert.That(controller.Element.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(3));
                Assert.That(controller.Element.itemsSource.Count, Is.EqualTo(2));

                controller.SetCategoryFilter("守卫");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(2));
                Assert.That(controller.Element.itemsSource.Count, Is.EqualTo(1));

                controller.SetCategoryFilter(string.Empty);
                controller.SetAssetFilter("missing");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(3));

                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels(new[] { Color.cyan, Color.cyan, Color.cyan, Color.cyan });
                source.Apply(false, false);
                var imageBytes = source.EncodeToPNG();
                Object.Destroy(source);
                var imported = M4RuntimeImageImporter.Import(
                    session.PieceAssetCatalog,
                    imageBytes,
                    "png",
                    "image/png");
                Assert.That(imported.accepted, Is.True, imported.diagnostic);
                var assetReceipt = session.PieceLibrary.RegisterAsset(imported.asset);
                Assert.That(assetReceipt.accepted, Is.True, assetReceipt.message);
                session.SaveSession.RecordAccepted(assetReceipt, session.CommandBus.State);
                var definitionReceipt = session.PieceLibrary.UpdateDefinition(
                    "grid-red",
                    "红色守卫",
                    "守卫",
                    new[] { "红色", "测试" },
                    imported.asset.id);
                Assert.That(definitionReceipt.accepted, Is.True, definitionReceipt.message);
                session.SaveSession.RecordAccepted(definitionReceipt, session.CommandBus.State);

                controller.SetAssetFilter("available");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(1));
                controller.SetAssetFilter("missing");
                controller.SetCategoryFilter("守卫");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(1));

                controller.SetAssetFilter("all");
                controller.SetCategoryFilter(string.Empty);
                controller.SetSearch("守卫");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(2));
                Assert.That(controller.Element.itemsSource.Count, Is.EqualTo(1));

                controller.SetSearch("蓝色");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.EqualTo(1));
                controller.SetSearch("不存在");
                controller.Refresh();
                Assert.That(controller.FilteredDefinitionCount, Is.Zero);
            }
            finally
            {
                controller?.Dispose();
                session?.Dispose();
                if (Directory.Exists(projectRoot))
                {
                    Directory.Delete(projectRoot, true);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator M7PieceThumbnailCacheUsesBoundedLruAndRejectsMissingProxy()
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "Sundoll-M7-ThumbCache-" + Guid.NewGuid().ToString("N"));
            M7PieceThumbnailCache cache = null;
            try
            {
                var catalog = new M4PieceAssetCatalog(projectRoot);
                var assets = new List<M4PieceAsset>();
                byte[] lastPng = null;
                for (var index = 0; index < 5; index++)
                {
                    var source = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                    var color = Color.HSVToRGB(index / 5f, 0.7f, 0.9f);
                    var pixels = new Color[128 * 128];
                    for (var pixel = 0; pixel < pixels.Length; pixel++)
                    {
                        pixels[pixel] = color;
                    }

                    source.SetPixels(pixels);
                    source.Apply(false, false);
                    lastPng = source.EncodeToPNG();
                    Object.Destroy(source);
                    var imported = M4RuntimeImageImporter.Import(catalog, lastPng, "png", "image/png");
                    Assert.That(imported.accepted, Is.True, imported.diagnostic);
                    assets.Add(imported.asset);
                }

                cache = new M7PieceThumbnailCache(256L * 1024L);
                foreach (var asset in assets)
                {
                    Assert.That(cache.TryAcquire(asset, catalog, out var texture, out var diagnostic), Is.True, diagnostic);
                    Assert.That(texture.width, Is.LessThanOrEqualTo(128));
                    Assert.That(texture.height, Is.LessThanOrEqualTo(128));
                    cache.Release(asset.id);
                }

                Assert.That(cache.ResidentBytes, Is.LessThanOrEqualTo(256L * 1024L));
                Assert.That(cache.Count, Is.LessThanOrEqualTo(4));

                cache.Clear();
                var withoutThumbnail = catalog.Import(lastPng, "png", "image/png");
                Assert.That(
                    cache.TryAcquire(withoutThumbnail, catalog, out _, out var missingDiagnostic),
                    Is.False);
                Assert.That(missingDiagnostic, Does.Contain("缩略图"));
            }
            finally
            {
                cache?.Dispose();
                if (Directory.Exists(projectRoot))
                {
                    Directory.Delete(projectRoot, true);
                }
            }

            yield return null;
        }

        [Test]
        public void M7WorkbenchTabsShowOnlyTheSelectedPanel()
        {
            var controller = new M7WorkbenchTabController();
            var mapPanel = new VisualElement();
            var piecePanel = new VisualElement();
            var changedTo = string.Empty;
            controller.TabChanged += tabId => changedTo = tabId;
            controller.Add("map", "地图", mapPanel);
            controller.Add("pieces", "棋子", piecePanel);

            Assert.That(controller.Select("map"), Is.True);
            Assert.That(mapPanel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(piecePanel.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(controller.Select("pieces"), Is.True);
            Assert.That(mapPanel.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(piecePanel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(controller.CurrentTabId, Is.EqualTo("pieces"));
            Assert.That(changedTo, Is.EqualTo("pieces"));
            Assert.That(controller.Select("missing"), Is.False);
        }

        [UnityTest]
        public IEnumerator WorkbenchPrimaryWorkspacesAreMutuallyExclusiveAndMapBoundaryIsExplicit()
        {
            yield return SceneManager.LoadSceneAsync("M3Workbench", LoadSceneMode.Single);
            yield return null;

            var root = Object.FindFirstObjectByType<M3WorkbenchRoot>();
            var document = root.GetComponent<UIDocument>();
            Assert.That(root, Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("PrimaryWorkspace_map"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("PrimaryWorkspace_pieces"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("PrimaryWorkspace_host"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("MapEditorToolbar"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("MapTool_画笔"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("FitMapButton"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Label>("MapBoundaryFeedback"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Label>("MapBoundaryFeedback").text, Does.Contain("地图有效区"));
            Assert.That(document.rootVisualElement.Q<Button>("MapTool_画笔").ClassListContains("sw-map-tool-selected"), Is.True);

            var mapWorkspace = document.rootVisualElement.Q<VisualElement>("MapEditorWorkspace");
            var pieceWorkspace = document.rootVisualElement.Q<VisualElement>("PieceLibraryWorkspace");
            var hostWorkspace = document.rootVisualElement.Q<VisualElement>("HostConsoleWorkspace");
            Assert.That(mapWorkspace, Is.Not.Null);
            Assert.That(pieceWorkspace, Is.Not.Null);
            Assert.That(hostWorkspace, Is.Not.Null);
            var hostTools = document.rootVisualElement.Q<ScrollView>("HostToolsScroll");
            var hostMapManagement = document.rootVisualElement.Q<VisualElement>("HostMapManagement");
            var hostSessionOverview = document.rootVisualElement.Q<VisualElement>("HostSessionOverview");
            var hostFogTools = document.rootVisualElement.Q<VisualElement>("HostFogTools");
            var hostAnnotationTools = document.rootVisualElement.Q<VisualElement>("HostAnnotationTools");
            var hostInteractionTools = document.rootVisualElement.Q<VisualElement>("HostInteractionTools");
            Assert.That(hostTools, Is.Not.Null);
            Assert.That(hostMapManagement, Is.Not.Null);
            Assert.That(hostSessionOverview, Is.Not.Null);
            Assert.That(hostFogTools, Is.Not.Null);
            Assert.That(hostAnnotationTools, Is.Not.Null);
            Assert.That(hostInteractionTools, Is.Not.Null);
            Assert.That(hostMapManagement.ClassListContains("sw-host-tool-section"), Is.True);
            Assert.That(hostFogTools.ClassListContains("sw-host-tool-section"), Is.True);
            Assert.That(hostAnnotationTools.ClassListContains("sw-host-tool-section"), Is.True);
            Assert.That(hostInteractionTools.ClassListContains("sw-host-tool-section"), Is.True);
            Assert.That(hostWorkspace.Q<VisualElement>("PieceLibraryFilters"), Is.Null);
            Assert.That(pieceWorkspace.Q<VisualElement>("HostFogTools"), Is.Null);
            var hostTopBar = document.rootVisualElement.Q<VisualElement>("HostTopBarActions");
            Assert.That(hostTopBar, Is.Not.Null);
            Assert.That(hostTopBar.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(mapWorkspace.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(pieceWorkspace.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(hostWorkspace.style.display.value, Is.EqualTo(DisplayStyle.None));

            var boundary = root.transform.Find("MapBoundary").GetComponent<LineRenderer>();
            Assert.That(boundary, Is.Not.Null);
            Assert.That(boundary.positionCount, Is.EqualTo(5));
            Assert.That(boundary.enabled, Is.True);
            var boundaryFill = root.transform.Find("MapBoundaryFill").GetComponent<SpriteRenderer>();
            Assert.That(boundaryFill, Is.Not.Null);
            Assert.That(boundaryFill.sprite, Is.Not.Null);
            Assert.That(boundaryFill.enabled, Is.True);
            var gridOverlay = root.transform.Find("WorkbenchGrid").Find("WorkbenchGridOverlay");
            Assert.That(gridOverlay, Is.Not.Null);
            Assert.That(gridOverlay.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(gridOverlay.GetComponent<MeshRenderer>().enabled, Is.True);
            var materialThumbnail = document.rootVisualElement.Q<VisualElement>("Thumbnail");
            Assert.That(materialThumbnail, Is.Not.Null);

            Assert.That(root.SelectWorkspace("pieces"), Is.True);
            Assert.That(root.CurrentWorkspace, Is.EqualTo("pieces"));
            Assert.That(mapWorkspace.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(pieceWorkspace.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(hostWorkspace.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(boundary.enabled, Is.False);
            Assert.That(gridOverlay.GetComponent<MeshRenderer>().enabled, Is.False);
            Assert.That(root.IsPointerOverMap(new Vector2(100f, 100f)), Is.False);

            Assert.That(root.SelectWorkspace("host"), Is.True);
            Assert.That(root.CurrentWorkspace, Is.EqualTo("host"));
            Assert.That(mapWorkspace.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(pieceWorkspace.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(hostWorkspace.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(hostTopBar.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            Assert.That(root.SelectWorkspace("map"), Is.True);
            Assert.That(boundary.enabled, Is.True);
            Assert.That(root.TryScreenToCell(new Vector2(-100f, -100f), out _), Is.False);

            Object.Destroy(root.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorkbenchBootsProjectsLayersAndCanRebuildView()
        {
            yield return SceneManager.LoadSceneAsync("M3Workbench", LoadSceneMode.Single);
            yield return null;

            var root = Object.FindFirstObjectByType<M3WorkbenchRoot>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Editor, Is.Not.Null);
            var document = root.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<TextField>("PieceSearch"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("PieceLibraryFilters"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("PieceDefinitionEditor"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("PieceImageImport"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Label>("PieceDefinitionEditorTitle").text, Is.EqualTo("当前定义编辑"));
            Assert.That(document.rootVisualElement.Q<DropdownField>("PieceCategoryFilter"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<DropdownField>("PieceAssetFilter"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("PieceLibraryList"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("PickPieceImageFile"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("InstallStarterContent"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("RebindPieceImage"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<ListView>("PieceLibraryList"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("PieceInstanceList"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<ScrollView>("ToolPanelScroll"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("WorkbenchTab_map"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("WorkbenchTab_pieces"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("WorkbenchTab_hierarchy"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("WorkbenchTab_host"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("CreateHostBoard"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("MapVisualPalette"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("MapVisual_terrain-ground"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("MapVisual_terrain-water"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<ScrollView>("InspectorScroll"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Label>("InspectorTitle").text, Is.EqualTo("地图摘要"));
            Assert.That(document.rootVisualElement.Q<Label>("InspectorDiagnosticsBody"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<DropdownField>("PieceRelationTarget"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("DeleteSelectedPieces").enabledSelf, Is.False);
            Assert.That(document.rootVisualElement.Q<Button>("DeleteSelectedPieces"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("HostMapList"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("HostHierarchy"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("HostContextMenu"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("RenameHostMap"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<TextField>("FogX"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<TextField>("FogBrushRadius"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("RevealFogBrush"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("HideFogBrush"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("MoveAnnotationTool"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<TextField>("AnnotationText"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<TextField>("InteractionObjectId"), Is.Not.Null);
            var mapList = document.rootVisualElement.Q<VisualElement>("HostMapList");
            var hierarchy = document.rootVisualElement.Q<VisualElement>("HostHierarchy");
            var pieceList = document.rootVisualElement.Q<VisualElement>("PieceLibraryList");
            var mapListCount = mapList.childCount;
            var hierarchyCount = hierarchy.childCount;
            var pieceListCount = pieceList.childCount;
            VisualElement mapListFirstChild = mapListCount == 0 ? null : mapList.ElementAt(0);
            VisualElement hierarchyFirstChild = hierarchyCount == 0 ? null : hierarchy.ElementAt(0);
            VisualElement pieceListFirstChild = pieceListCount == 0 ? null : pieceList.ElementAt(0);
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(mapList.childCount, Is.EqualTo(mapListCount));
            Assert.That(hierarchy.childCount, Is.EqualTo(hierarchyCount));
            Assert.That(pieceList.childCount, Is.EqualTo(pieceListCount));
            if (mapListFirstChild != null)
            {
                Assert.That(mapList.ElementAt(0), Is.SameAs(mapListFirstChild));
            }

            if (hierarchyFirstChild != null)
            {
                Assert.That(hierarchy.ElementAt(0), Is.SameAs(hierarchyFirstChild));
            }

            if (pieceListFirstChild != null)
            {
                Assert.That(pieceList.ElementAt(0), Is.SameAs(pieceListFirstChild));
            }

            // The real Workbench deliberately restores local visibility state;
            // normalize the shared test profile before exercising projection.
            root.LayerEditState.SetVisible("terrain", true);
            root.LayerEditState.SetLocked("terrain", false);
            Assert.That(root.LayerEditState.IsVisible("terrain"), Is.True);

            var projection = Object.FindFirstObjectByType<M3WorkbenchMapProjection>();
            Assert.That(projection, Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<M5WorkbenchConsoleProjection>(), Is.Not.Null);
            Assert.That(projection.Tilemaps.Count, Is.GreaterThanOrEqualTo(5));
            var initialTerrain = root.Editor.PaintCell(2, 3, "terrain", "terrain-ground");
            Assert.That(initialTerrain.accepted, Is.True);
            projection.RefreshRegion(root.Editor.LastDirtyBounds);
            Assert.That(projection.Tilemaps["terrain"].HasTile(new Vector3Int(2, 3, 0)), Is.True);
            var waterTerrain = root.Editor.PaintCell(3, 3, "terrain", "terrain-water");
            Assert.That(waterTerrain.accepted, Is.True);
            projection.RefreshRegion(root.Editor.LastDirtyBounds);
            var groundTile = projection.Tilemaps["terrain"].GetTile<Tile>(new Vector3Int(2, 3, 0));
            var waterTile = projection.Tilemaps["terrain"].GetTile<Tile>(new Vector3Int(3, 3, 0));
            Assert.That(groundTile, Is.Not.Null);
            Assert.That(waterTile, Is.Not.Null);
            Assert.That(waterTile, Is.Not.SameAs(groundTile));
            Assert.That(waterTile.sprite.texture, Is.Not.SameAs(groundTile.sprite.texture));

            var revisionBefore = root.Editor.State.revision;
            var receipt = root.Editor.PaintCell(1, 1, "wall", "wall-solid");
            Assert.That(receipt.accepted, Is.True);
            projection.RefreshRegion(root.Editor.LastDirtyBounds);
            Assert.That(projection.Tilemaps["wall"].HasTile(new Vector3Int(1, 1, 0)), Is.True);
            Assert.That(root.Editor.State.revision, Is.EqualTo(revisionBefore + 1));

            root.LayerEditState.SetVisible("terrain", false);
            projection.ApplyLayerState();
            Assert.That(projection.Tilemaps["terrain"].GetComponent<TilemapRenderer>().enabled, Is.False);
            root.LayerEditState.SetLocked("wall", true);
            Assert.That(root.LayerEditState.CanEdit("wall"), Is.False);

            root.AddMapObjectAt(new Vector2Int(4, 4), M3MapObjectKind.Door);
            Assert.That(document.rootVisualElement.Q<Label>("InspectorTitle").text, Is.EqualTo("门对象"));
            Assert.That(document.rootVisualElement.Q<Label>("InspectorBody").text, Does.Contain("状态：关闭"));
            root.ShowMapContextMenu(new Vector2Int(4, 4), new Vector2(200f, 200f));
            Assert.That(root.IsContextMenuVisible, Is.True);
            Assert.That(document.rootVisualElement.Q<Button>("ContextToggle"), Is.Not.Null);
            root.DismissContextMenu();
            Assert.That(root.IsContextMenuVisible, Is.False);

            Object.Destroy(root.gameObject);
            yield return null;
            Assert.That(root == null, Is.True);
        }

        [UnityTest]
        public IEnumerator WorkbenchPieceSelectionDragAndBatchShortcutsUseOneProjectionFlow()
        {
            yield return SceneManager.LoadSceneAsync("M3Workbench", LoadSceneMode.Single);
            yield return null;

            var root = Object.FindFirstObjectByType<M3WorkbenchRoot>();
            var projection = Object.FindFirstObjectByType<M4WorkbenchPieceProjection>();
            var interaction = Object.FindFirstObjectByType<M4WorkbenchPieceInteractionController>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.PieceLibrary, Is.Not.Null);
            Assert.That(projection, Is.Not.Null);
            Assert.That(interaction, Is.Not.Null);
            Assert.That(root.EnsureCurrentMapHostBoard(), Is.True);
            Assert.That(root.Editor.State.board, Is.Not.Null);
            Assert.That(root.Editor.State.board.id, Is.Not.Empty);
            Assert.That(root.PieceLibrary.State, Is.SameAs(root.Editor.State));

            var definitionId = "play-interaction-definition";
            Assert.That(root.PieceLibrary.CreateDefinition(
                definitionId,
                "交互测试棋子",
                "Test",
                new[] { "playmode" }).accepted, Is.True);
            Assert.That(root.PieceLibrary.CreateInstance(definitionId, "play-interaction-a").accepted, Is.True);
            Assert.That(root.PieceLibrary.CreateInstance(definitionId, "play-interaction-b").accepted, Is.True);
            Assert.That(root.PieceLibrary.Place("play-interaction-a", 1, 1).accepted, Is.True);
            Assert.That(root.PieceLibrary.Place("play-interaction-b", 2, 1).accepted, Is.True);
            projection.RefreshAll();
            yield return null;

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var first = camera.WorldToScreenPoint(new Vector3(1f, 1f, 0f));
            var second = camera.WorldToScreenPoint(new Vector3(2f, 1f, 0f));
            var destination = camera.WorldToScreenPoint(new Vector3(3f, 2f, 0f));
            Assert.That(root.BeginPiecePointerAction(first, false), Is.True);
            root.EndPiecePointerAction(first);
            var document = root.GetComponent<UIDocument>();
            Assert.That(document.rootVisualElement.Q<Label>("InspectorTitle").text, Is.EqualTo("棋子状态"));
            Assert.That(document.rootVisualElement.Q<Button>("DeleteSelectedPieces").enabledSelf, Is.True);
            Assert.That(projection.Views["play-interaction-a"].transform.Find("SelectionHighlight-play-interaction-a"), Is.Not.Null);
            Assert.That(root.BeginPiecePointerAction(second, true), Is.True);
            root.EndPiecePointerAction(second);
            Assert.That(root.SelectedPieceCount, Is.EqualTo(2));
            Assert.That(root.GetComponent<UIDocument>().rootVisualElement.Q<Label>("InspectorTitle").text, Is.EqualTo("棋子多选"));
            Assert.That(document.rootVisualElement.Q<DropdownField>("PieceRelationTarget").enabledSelf, Is.False);

            Assert.That(root.BeginPiecePointerAction(first, false), Is.True);
            root.ContinuePiecePointerAction(destination);
            Assert.That(interaction.DragGhostCount, Is.EqualTo(2));
            root.EndPiecePointerAction(destination);
            yield return null;

            Assert.That(M4PieceQueries.FindInstance(root.PieceLibrary.State, "play-interaction-a").location.x, Is.EqualTo(3));
            Assert.That(M4PieceQueries.FindInstance(root.PieceLibrary.State, "play-interaction-b").location.x, Is.EqualTo(4));
            Assert.That(root.RotateSelectedPieces(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(root.PieceLibrary.State, "play-interaction-a").rotation, Is.EqualTo(90));
            Assert.That(root.DeleteSelectedPieces(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(root.PieceLibrary.State, "play-interaction-a"), Is.Null);
            Assert.That(root.SelectedPieceCount, Is.EqualTo(0));

            Object.Destroy(root.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorkbenchHostMapSwitchSavesDraftAndRestoresPerMapViewport()
        {
            yield return SceneManager.LoadSceneAsync("M3Workbench", LoadSceneMode.Single);
            yield return null;

            var root = Object.FindFirstObjectByType<M3WorkbenchRoot>();
            var camera = Camera.main;
            Assert.That(root, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);
            var firstMapId = root.Editor.State.map.id;
            Assert.That(root.CreateHostMap("play-host-second", "第二张主持地图", 20, 16), Is.True);

            camera.orthographicSize = 4f;
            camera.transform.position = new Vector3(2.5f, 3.5f, -10f);
            Assert.That(root.TrySwitchHostMap("play-host-second"), Is.True);
            yield return null;
            Assert.That(root.Editor.State.map.id, Is.EqualTo("play-host-second"));
            Assert.That(camera.orthographicSize, Is.EqualTo(9f).Within(0.001f));

            Assert.That(root.TrySwitchHostMap(firstMapId), Is.True);
            yield return null;
            Assert.That(root.Editor.State.map.id, Is.EqualTo(firstMapId));
            Assert.That(camera.orthographicSize, Is.EqualTo(4f).Within(0.001f));
            Assert.That(camera.transform.position.x, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(camera.transform.position.y, Is.EqualTo(3.5f).Within(0.001f));

            root.SaveSession.WaitForSave();
            var reloaded = root.SaveSession.Reload();
            Assert.That(reloaded.state.map.id, Is.EqualTo(firstMapId));
            Assert.That(M5ConsoleQueries.Ensure(reloaded.state).FindMap("play-host-second"), Is.Not.Null);

            Object.Destroy(root.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerPreviewUsesAudienceProjectionAndIsReadOnly()
        {
            yield return SceneManager.LoadSceneAsync("M3Workbench", LoadSceneMode.Single);
            yield return null;

            var root = Object.FindFirstObjectByType<M3WorkbenchRoot>();
            var projection = Object.FindFirstObjectByType<M3WorkbenchMapProjection>();
            var pieceProjection = Object.FindFirstObjectByType<M4WorkbenchPieceProjection>();
            var consoleProjection = Object.FindFirstObjectByType<M5WorkbenchConsoleProjection>();
            Assert.That(root, Is.Not.Null);
            Assert.That(projection, Is.Not.Null);
            Assert.That(pieceProjection, Is.Not.Null);
            Assert.That(consoleProjection, Is.Not.Null);
            Assert.That(root.HostPreviewMode, Is.False);
            Assert.That(projection.IsAudienceProjectionActive, Is.False);
            Assert.That(pieceProjection.IsAudienceProjectionActive, Is.False);

            root.ToggleHostPreviewMode();
            yield return null;
            Assert.That(root.HostPreviewMode, Is.True);
            Assert.That(projection.IsAudienceProjectionActive, Is.True);
            Assert.That(pieceProjection.IsAudienceProjectionActive, Is.True);
            Assert.That(consoleProjection.IsAudiencePreview, Is.True);

            var hadTerrainBeforePreviewEdit = root.Editor.State.map.TryGetCell(2, 2, M3MapLayerIds.Terrain, out _);
            root.BeginPointerAction(new Vector2Int(2, 2));
            Assert.That(root.Editor.State.map.TryGetCell(2, 2, M3MapLayerIds.Terrain, out _), Is.EqualTo(hadTerrainBeforePreviewEdit));
            root.ToggleHostPreviewMode();
            yield return null;
            Assert.That(root.HostPreviewMode, Is.False);
            Assert.That(projection.IsAudienceProjectionActive, Is.False);
            Assert.That(pieceProjection.IsAudienceProjectionActive, Is.False);
            Assert.That(consoleProjection.IsAudiencePreview, Is.False);

            Object.Destroy(root.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4PieceProjectionRebuildsPlaceholderPieceView()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var library = new M4PieceLibraryFacade(bus);
            var definition = library.CreateDefinition(
                "m4-play-definition",
                "PlayMode 占位棋子",
                "Placeholder",
                new[] { "playmode" });
            Assert.That(definition.accepted, Is.True, definition.message);
            var instance = library.CreateInstance("m4-play-definition", "m4-play-instance");
            Assert.That(instance.accepted, Is.True, instance.message);
            var placement = library.Place("m4-play-instance", 3, 4);
            Assert.That(placement.accepted, Is.True, placement.message);

            var projectionObject = new GameObject("M4PlayModeProjection");
            var projection = projectionObject.AddComponent<M4WorkbenchPieceProjection>();
            projection.Bind(bus);
            yield return null;

            Assert.That(projection.Views.ContainsKey("m4-play-instance"), Is.True);
            Assert.That(projection.Views["m4-play-instance"].transform.position, Is.EqualTo(new Vector3(3f, 4f, -0.5f)));

            Object.Destroy(projectionObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4RuntimeImageImporterStoresAssetAndThumbnail()
        {
            var root = Path.Combine(Path.GetTempPath(), "Sundoll-M4-Image-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = new Texture2D(4, 2, TextureFormat.RGBA32, false);
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 4; x++)
                    {
                        source.SetPixel(x, y, x < 2 ? Color.cyan : Color.magenta);
                    }
                }

                source.Apply(false, false);
                var bytes = source.EncodeToPNG();
                Object.Destroy(source);

                var catalog = new M4PieceAssetCatalog(root);
                var result = M4RuntimeImageImporter.Import(catalog, bytes, "png", "image/png");
                Assert.That(result.accepted, Is.True, result.diagnostic);
                Assert.That(result.width, Is.EqualTo(4));
                Assert.That(result.height, Is.EqualTo(2));
                Assert.That(catalog.IsAssetAvailable(result.asset), Is.True);
                Assert.That(catalog.IsThumbnailAvailable(result.asset), Is.True);

                var invalid = M4RuntimeImageImporter.Import(catalog, new byte[] { 1, 2, 3 }, "png", "image/png");
                Assert.That(invalid.accepted, Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator M7TextureCacheReusesAndReleasesRuntimeTexture()
        {
            var root = Path.Combine(Path.GetTempPath(), "Sundoll-M7-Texture-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
                source.Apply(false, false);
                var bytes = source.EncodeToPNG();
                Object.Destroy(source);

                var catalog = new M4PieceAssetCatalog(root);
                var imported = M4RuntimeImageImporter.Import(catalog, bytes, "png", "image/png");
                Assert.That(imported.accepted, Is.True, imported.diagnostic);
                using (var cache = new M7TextureCache())
                {
                    Assert.That(cache.TryAcquire(imported.asset, catalog, out var first, out var firstDiagnostic), Is.True, firstDiagnostic);
                    Assert.That(cache.TryAcquire(imported.asset, catalog, out var second, out var secondDiagnostic), Is.True, secondDiagnostic);
                    Assert.That(second, Is.SameAs(first));
                    Assert.That(cache.Count, Is.EqualTo(1));
                    cache.Release(imported.asset.id);
                    Assert.That(cache.Count, Is.EqualTo(1));
                    cache.Release(imported.asset.id);
                    Assert.That(cache.Count, Is.EqualTo(0));
                }
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator M4TextureProjectionBaselineSharesTextureAcross64Pieces()
        {
            var root = Path.Combine(Path.GetTempPath(), "Sundoll-M4-Texture-Baseline-" + Guid.NewGuid().ToString("N"));
            GameObject projectionObject = null;
            try
            {
                var source = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var pixels = new Color[16 * 16];
                for (var pixel = 0; pixel < pixels.Length; pixel++)
                {
                    pixels[pixel] = Color.cyan;
                }

                source.SetPixels(pixels);
                source.Apply(false, false);
                var bytes = source.EncodeToPNG();
                Object.Destroy(source);

                var catalog = new M4PieceAssetCatalog(root);
                var imported = M4RuntimeImageImporter.Import(catalog, bytes, "png", "image/png");
                Assert.That(imported.accepted, Is.True, imported.diagnostic);

                var bus = M1VerticalSlice.CreateDemoBus();
                var library = new M4PieceLibraryFacade(bus);
                var assetReceipt = library.RegisterAsset(imported.asset);
                Assert.That(assetReceipt.accepted, Is.True, assetReceipt.message);
                var definitionReceipt = library.CreateDefinition(
                    "m4-texture-baseline-definition",
                    "Texture baseline",
                    "Baseline",
                    new[] { "performance" },
                    imported.asset.id);
                Assert.That(definitionReceipt.accepted, Is.True, definitionReceipt.message);

                for (var index = 0; index < 64; index++)
                {
                    var instanceId = "m4-texture-baseline-" + index;
                    var instanceReceipt = library.CreateInstance("m4-texture-baseline-definition", instanceId);
                    Assert.That(instanceReceipt.accepted, Is.True, instanceReceipt.message);
                    var placementReceipt = library.Place(instanceId, index % 8, index / 8);
                    Assert.That(placementReceipt.accepted, Is.True, placementReceipt.message);
                }

                projectionObject = new GameObject("M4TextureBaselineProjection");
                var projection = projectionObject.AddComponent<M4WorkbenchPieceProjection>();
                projection.Bind(bus, catalog);
                var refresh = M7PerformanceProbe.Measure(() => projection.RefreshAll(), 5);

                Assert.That(projection.Views.Count, Is.EqualTo(64));
                Assert.That(projection.CachedTextureCount, Is.EqualTo(1));
                TestContext.WriteLine(
                    "M4 texture baseline | pieces=64 textures=" + projection.CachedTextureCount +
                    " refresh p95=" + refresh.p95Milliseconds.ToString("0.000") +
                    "ms max=" + refresh.maxMilliseconds.ToString("0.000") + "ms");
            }
            finally
            {
                if (projectionObject != null)
                {
                    Object.Destroy(projectionObject);
                }

                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator M5ConsoleProjectionRebuildsFogAndAnnotationViews()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var console = M5ConsoleQueries.Ensure(bus.State);
            var fog = bus.Execute(new M5SetFogCommand("play-m5-fog", bus.State.revision, console.activeMapId, 1, 1, false));
            Assert.That(fog.accepted, Is.True, fog.message);
            var annotation = bus.Execute(new M5UpsertAnnotationCommand(
                "play-m5-note",
                bus.State.revision,
                "note-play",
                console.activeMapId,
                1,
                1,
                "入口",
                "#FFFFFF",
                true));
            Assert.That(annotation.accepted, Is.True, annotation.message);

            var projectionObject = new GameObject("M5ConsoleProjectionTest");
            var projection = projectionObject.AddComponent<M5WorkbenchConsoleProjection>();
            projection.Bind(bus);
            yield return null;

            Assert.That(projection.FogViews.ContainsKey("fog-1-1"), Is.True);
            Assert.That(projection.AnnotationViews.ContainsKey("note-play"), Is.True);
            Object.Destroy(projectionObject);
            yield return null;
        }
    }
}
