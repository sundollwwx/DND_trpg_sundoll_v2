using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Tests.EditMode
{
    public sealed class M5ConsoleTests
    {
        [Test]
        public void MultipleMapsSwitchWithoutLosingMapContent()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            Execute(bus, new M5CreateMapSlotCommand("m5-create-map", bus.State.revision, "map-forest", "森林", 12, 10));
            Execute(bus, new M3PaintCellsCommand(
                "m5-paint-forest",
                bus.State.revision,
                new[] { new M3CellMutation(2, 2, M3MapLayerIds.Terrain, "terrain-forest", false) }));
            Assert.That(bus.State.map.id, Is.EqualTo("map-m1"));

            Execute(bus, new M5SwitchMapCommand("m5-switch-forest", bus.State.revision, "map-forest"));
            Assert.That(bus.State.map.id, Is.EqualTo("map-forest"));
            Assert.That(bus.State.map.width, Is.EqualTo(12));
            Assert.That(bus.State.map.TryGetCell(2, 2, M3MapLayerIds.Terrain, out _), Is.False);

            Execute(bus, new M3PaintCellsCommand(
                "m5-paint-forest-2",
                bus.State.revision,
                new[] { new M3CellMutation(3, 3, M3MapLayerIds.Terrain, "terrain-forest", false) }));
            Execute(bus, new M5SwitchMapCommand("m5-switch-back", bus.State.revision, "map-m1"));
            Assert.That(bus.State.map.id, Is.EqualTo("map-m1"));
            Assert.That(bus.State.map.TryGetCell(2, 2, M3MapLayerIds.Terrain, out var cell), Is.True);
            Assert.That(cell.contentId, Is.EqualTo("terrain-forest"));
        }

        [Test]
        public void FogAnnotationsAndInteractionRoundTripThroughJournalEnvelope()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var console = M5ConsoleQueries.Ensure(bus.State);
            Execute(bus, new M5SetFogCommand("m5-fog", bus.State.revision, console.activeMapId, 1, 1, false));
            Execute(bus, new M5UpsertAnnotationCommand(
                "m5-annotation",
                bus.State.revision,
                "note-1",
                console.activeMapId,
                1,
                1,
                "入口",
                "#FFCC00",
                true));

            var command = new M5SetInteractionStateCommand("m5-interaction", bus.State.revision, "door-1", true);
            var envelope = M2CommandEnvelopeCodec.Encode(command);
            var decoded = M2CommandEnvelopeCodec.Decode(
                JsonUtility.FromJson<M1CommandEnvelope>(JsonUtility.ToJson(envelope, false)));
            Execute(bus, decoded);

            Assert.That(bus.State.m5Console.IsRevealed(console.activeMapId, 1, 1), Is.False);
            Assert.That(bus.State.m5Console.FindAnnotation("note-1").text, Is.EqualTo("入口"));
            Assert.That(bus.State.m5Console.FindInteraction("door-1").open, Is.True);
            Assert.That(M2CanonicalStateHasher.Compute(bus.State), Is.Not.Empty);
        }

        [Test]
        public void M5MapCommandCanUndoAndRedo()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var originalMapId = bus.State.map.id;
            Execute(bus, new M5CreateMapSlotCommand("m5-create-map", bus.State.revision, "map-second", "第二张图", 8, 8));
            Assert.That(M5ConsoleQueries.Ensure(bus.State).FindMap("map-second"), Is.Not.Null);
            Assert.That(bus.Undo(), Is.True);
            Assert.That(M5ConsoleQueries.Ensure(bus.State).FindMap("map-second"), Is.Null);
            Assert.That(bus.State.map.id, Is.EqualTo(originalMapId));
            Assert.That(bus.Redo(), Is.True);
            Assert.That(M5ConsoleQueries.Ensure(bus.State).FindMap("map-second"), Is.Not.Null);
        }

        [Test]
        public void MapObjectContextOperationsAddToggleRotateRemove()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var editor = new M3MapEditorFacade(bus);
            var added = editor.AddMapObject("door-context", M3MapObjectKind.Door, 4, 5);
            Assert.That(added.accepted, Is.True, added.message);
            Assert.That(editor.FindMapObject("door-context"), Is.Not.Null);

            Assert.That(editor.ToggleMapObject("door-context").accepted, Is.True);
            Assert.That(editor.FindMapObject("door-context").state, Is.EqualTo(M3MapObjectOpenState.Open));
            Assert.That(editor.RotateMapObjectClockwise("door-context").accepted, Is.True);
            Assert.That(editor.FindMapObject("door-context").rotation, Is.EqualTo(90));

            var removed = editor.RemoveMapObject("door-context");
            Assert.That(removed.accepted, Is.True, removed.message);
            Assert.That(editor.FindMapObject("door-context"), Is.Null);
            Assert.That(bus.Undo(), Is.True);
            Assert.That(editor.FindMapObject("door-context"), Is.Not.Null);
        }

        private static void Execute(M1CommandBus bus, M1Command command)
        {
            var receipt = bus.Execute(command);
            Assert.That(receipt.accepted, Is.True, receipt.message);
        }
    }
}
