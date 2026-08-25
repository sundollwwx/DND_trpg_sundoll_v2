using System.Collections;
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
            Assert.That(document.rootVisualElement.Q<VisualElement>("PieceLibraryList"), Is.Not.Null);
            // The real Workbench deliberately restores local visibility state;
            // normalize the shared test profile before exercising projection.
            root.LayerEditState.SetVisible("terrain", true);
            root.LayerEditState.SetLocked("terrain", false);
            Assert.That(root.LayerEditState.IsVisible("terrain"), Is.True);

            var projection = Object.FindFirstObjectByType<M3WorkbenchMapProjection>();
            Assert.That(projection, Is.Not.Null);
            Assert.That(projection.Tilemaps.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(projection.Tilemaps["terrain"].HasTile(new Vector3Int(2, 3, 0)), Is.True);

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
    }
}
