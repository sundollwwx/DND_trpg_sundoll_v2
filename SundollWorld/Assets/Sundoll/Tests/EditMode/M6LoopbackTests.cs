using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;

namespace Sundoll.Tests.EditMode
{
    public sealed class M6LoopbackTests
    {
        [Test]
        public void TwoClientsConvergeThroughSnapshotAndDeltaTail()
        {
            var authority = M1VerticalSlice.CreateDemoBus();
            M5ConsoleQueries.Ensure(authority.State);
            var hub = new M6LoopbackHub(authority);
            var first = hub.Connect("gm");
            var second = hub.Connect("player");

            var receipt = first.Submit(new M5UpsertAnnotationCommand(
                "m6-annotation",
                first.Revision,
                "note-1",
                authority.State.m5Console.activeMapId,
                1,
                1,
                "入口",
                "#FFFFFF",
                true));
            Assert.That(receipt.accepted, Is.True, receipt.message);

            var tail = hub.GetTail("player", second.Revision);
            Assert.That(tail, Has.Count.EqualTo(1));
            Assert.That(second.ApplyDelta(tail[0]), Is.True, second.LastDiagnostic);
            Assert.That(second.State.m5Console.FindAnnotation("note-1").text, Is.EqualTo("入口"));
            Assert.That(second.Revision, Is.EqualTo(first.Revision));
        }

        [Test]
        public void StaleClientCommandConflictsAndAudienceProjectionHidesPieces()
        {
            var authority = M1VerticalSlice.CreateDemoBus();
            var hub = new M6LoopbackHub(authority);
            var full = hub.Connect("full");
            var restricted = hub.Connect("restricted", new M6AudiencePolicy { includeHiddenPieces = false });

            var first = full.Submit(new M5RenameMapCommand("m6-rename", full.Revision, "map-m1", "主持地图"));
            Assert.That(first.accepted, Is.True);
            var stale = restricted.Submit(new M5RenameMapCommand("m6-stale", restricted.Revision, "map-m1", "旧命名"));
            Assert.That(stale.conflict, Is.True);
            Assert.That(restricted.State.pieceInstances, Is.Empty);
        }

        [Test]
        public void AudienceProjectionFiltersPrivatePieceStateAndHostNotes()
        {
            var authority = M1VerticalSlice.CreateDemoBus();
            var library = new M4PieceLibraryFacade(authority);
            var definition = library.CreateDefinition(
                "audience-definition",
                "Audience Token",
                "Token",
                new[] { "test" });
            Assert.That(definition.accepted, Is.True, definition.message);
            var hidden = library.CreateInstance("audience-definition", "audience-hidden");
            Assert.That(hidden.accepted, Is.True, hidden.message);
            var visible = library.CreateInstance("audience-definition", "audience-visible");
            Assert.That(visible.accepted, Is.True, visible.message);
            Assert.That(library.Place("audience-hidden", 2, 2).accepted, Is.True);
            Assert.That(library.Place("audience-visible", 3, 2).accepted, Is.True);
            Assert.That(library.SetRuntimeState(
                "audience-hidden",
                new M4PieceRuntimeState { audienceVisible = false }).accepted, Is.True);
            Assert.That(library.SetRuntimeState(
                "audience-visible",
                new M4PieceRuntimeState
                {
                    hostNote = "秘密线索",
                    resourceBars = new System.Collections.Generic.List<M4PieceResourceBar>
                    {
                        new M4PieceResourceBar { id = "public", displayName = "公开资源", current = 1, maximum = 2, visibleToAudience = true },
                        new M4PieceResourceBar { id = "private", displayName = "主持资源", current = 3, maximum = 3, visibleToAudience = false }
                    },
                    statuses = new System.Collections.Generic.List<M4PieceStatusEntry>
                    {
                        new M4PieceStatusEntry { id = "public-status", displayName = "公开状态", visibleToAudience = true },
                        new M4PieceStatusEntry { id = "private-status", displayName = "隐藏状态", visibleToAudience = false }
                    }
                }).accepted, Is.True);

            var snapshot = M6ProjectionBuilder.CreateSnapshot(
                authority.State,
                "player",
                new M6AudiencePolicy { includeHiddenPieces = false });
            var projected = UnityEngine.JsonUtility.FromJson<M1WorldState>(snapshot.stateJson);
            projected.EnsureSchema2Defaults();

            Assert.That(M4PieceQueries.FindInstance(projected, "audience-hidden"), Is.Null);
            var projectedVisible = M4PieceQueries.FindInstance(projected, "audience-visible");
            Assert.That(projectedVisible, Is.Not.Null);
            Assert.That(projectedVisible.runtimeState.hostNote, Is.Empty);
            Assert.That(projectedVisible.runtimeState.resourceBars, Has.Count.EqualTo(1));
            Assert.That(projectedVisible.runtimeState.resourceBars[0].id, Is.EqualTo("public"));
            Assert.That(projectedVisible.runtimeState.statuses, Has.Count.EqualTo(1));
            Assert.That(projectedVisible.runtimeState.statuses[0].id, Is.EqualTo("public-status"));
        }
    }
}
