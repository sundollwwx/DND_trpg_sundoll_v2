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
    }
}
