using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;

namespace Sundoll.Tests.EditMode
{
    public sealed class M1VerticalSliceTests
    {
        [Test]
        public void VerticalSliceCreatesPublishedMapBoardAndOnBoardPiece()
        {
            var bus = M1VerticalSlice.CreateDemoBus();

            Assert.That(bus.State.HasCompleteVerticalSlice(), Is.True);
            Assert.That(bus.State.publishedMap.sourceMapId, Is.EqualTo(bus.State.map.id));
            Assert.That(bus.State.scenario.publishedMapContentId, Is.EqualTo(bus.State.publishedMap.id));
            Assert.That(bus.State.board.scenarioId, Is.EqualTo(bus.State.scenario.id));
            Assert.That(bus.State.pieceInstance.location.kind, Is.EqualTo(M1PieceLocationKind.OnBoard));
            Assert.That(bus.State.pieceInstance.location.x, Is.EqualTo(1));
        }

        [Test]
        public void CreateScenarioRejectsBlankStableIds()
        {
            var bus = new M1CommandBus(
                M1WorldState.CreateEmpty(),
                new M1LocalAuthority(new AllowAllRulePolicy()));
            Assert.That(bus.Execute(new M1CreateProjectCommand(
                "scenario-id-project",
                bus.State.revision,
                "project-scenario-id",
                "Scenario IDs",
                "map-scenario-id")).accepted, Is.True);
            Assert.That(bus.Execute(new M1PublishMapContentCommand(
                "scenario-id-publish",
                bus.State.revision,
                "content-scenario-id")).accepted, Is.True);

            Assert.Throws<System.InvalidOperationException>(() => bus.Execute(new M1CreateScenarioCommand(
                "scenario-id-invalid",
                bus.State.revision,
                "scenario-valid",
                string.Empty)));
            Assert.That(bus.State.board, Is.Null);
            Assert.That(bus.State.scenario, Is.Null);
        }

        [Test]
        public void UndoAndRedoRestorePiecePosition()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var beforeMove = bus.State.pieceInstance.location.x;

            var receipt = bus.Execute(new M1MovePieceCommand("test-move", bus.State.revision, 3, 0));
            Assert.That(receipt.accepted, Is.True);
            Assert.That(bus.State.pieceInstance.location.x, Is.EqualTo(3));

            Assert.That(bus.Undo(), Is.True);
            Assert.That(bus.State.pieceInstance.location.x, Is.EqualTo(beforeMove));
            Assert.That(bus.Redo(), Is.True);
            Assert.That(bus.State.pieceInstance.location.x, Is.EqualTo(3));
        }

        [Test]
        public void LocalAuthorityIsIdempotentAndRejectsStaleCommand()
        {
            var state = M1WorldState.CreateEmpty();
            var authority = new M1LocalAuthority(new AllowAllRulePolicy());
            var create = new M1CreateProjectCommand("same-command", 0, "project", "Test", "map");

            var first = authority.Execute(state, create);
            var retry = authority.Execute(state, create);
            var stale = authority.Execute(state, new M1PaintCellCommand("stale", 0, 0, 0, "ground"));

            Assert.That(first.accepted, Is.True);
            Assert.That(retry.accepted, Is.True);
            Assert.That(retry.duplicate, Is.True);
            Assert.That(state.revision, Is.EqualTo(1));
            Assert.That(stale.conflict, Is.True);
            Assert.That(state.map.cells, Is.Empty);
        }

        [Test]
        public void SnapshotRoundTripRebuildsOnlyFromPureData()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var store = new M1MemorySnapshotStore();
            store.Save(bus.State);

            var loaded = store.Load();

            Assert.That(loaded.HasCompleteVerticalSlice(), Is.True);
            Assert.That(loaded.project.id, Is.EqualTo(bus.State.project.id));
            Assert.That(loaded.pieceInstance.location.x, Is.EqualTo(bus.State.pieceInstance.location.x));
            Assert.That(loaded, Is.Not.SameAs(bus.State));
            Assert.That(loaded.pieceInstance, Is.Not.SameAs(bus.State.pieceInstance));
        }

        [Test]
        public void CommandBusBoundsUndoSnapshotRetention()
        {
            var bus = new M1CommandBus(
                M1WorldState.CreateEmpty(),
                new M1LocalAuthority(new AllowAllRulePolicy()),
                2);

            var first = bus.Execute(new M1CreateProjectCommand(
                "history-project",
                0,
                "history-project",
                "History",
                "history-map"));
            Assert.That(first.accepted, Is.True);
            var second = bus.Execute(new M1PaintCellCommand(
                "history-paint-1",
                bus.State.revision,
                1,
                1,
                "terrain-ground"));
            Assert.That(second.accepted, Is.True);
            var third = bus.Execute(new M1PaintCellCommand(
                "history-paint-2",
                bus.State.revision,
                2,
                2,
                "terrain-ground"));
            Assert.That(third.accepted, Is.True);

            Assert.That(bus.MaxHistoryEntries, Is.EqualTo(2));
            Assert.That(bus.Undo(), Is.True);
            Assert.That(bus.Undo(), Is.True);
            Assert.That(bus.Undo(), Is.False);
        }
    }
}
