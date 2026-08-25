using System.Collections;
using NUnit.Framework;
using Sundoll.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

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
    }
}
