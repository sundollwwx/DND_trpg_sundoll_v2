using System;
using System.IO;
using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Tests.EditMode
{
    public sealed class M4PieceSystemTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "Sundoll-M4-" + Guid.NewGuid().ToString("N"));
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
        public void DefinitionsAndInstancesArePureDataAndRoundTrip()
        {
            var bus = CreateBus();
            Execute(bus, new M4RegisterPieceAssetCommand(
                "m4-asset",
                bus.State.revision,
                new M4PieceAsset
                {
                    id = "asset-placeholder",
                    sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    extension = "png",
                    mimeType = "image/png",
                    byteLength = 4,
                    relativePath = "assets/placeholder.png"
                }));
            Execute(bus, new M4CreatePieceDefinitionCommand(
                "m4-definition",
                bus.State.revision,
                "definition-token",
                "中性棋子",
                "Token",
                new[] { "中性", "测试" },
                "asset-placeholder"));
            Execute(bus, new M4CreatePieceInstanceCommand(
                "m4-instance",
                bus.State.revision,
                "definition-token",
                "instance-token"));

            Assert.That(M4PieceStateValidator.TryValidate(bus.State, out var diagnostic), Is.True, diagnostic);
            var json = JsonUtility.ToJson(bus.State, false);
            var loaded = JsonUtility.FromJson<M1WorldState>(json);
            loaded.EnsureSchema2Defaults();

            Assert.That(loaded.pieceDefinitions.Count, Is.EqualTo(1));
            Assert.That(loaded.pieceInstances[0].location.kind, Is.EqualTo(M1PieceLocationKind.Unplaced));
            Assert.That(loaded.pieceDefinitions[0].tags, Is.EqualTo(new[] { "中性", "测试" }));
            Assert.That(
                M2CanonicalStateHasher.Compute(loaded),
                Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)),
                "round-trip JSON=" + json);
            Assert.That(loaded.pieceDefinitions, Is.Not.SameAs(bus.State.pieceDefinitions));
        }

        [Test]
        public void BoardPlacementUsesStableStackOrder()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-a", bus.State.revision, "definition-token", "instance-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-b", bus.State.revision, "definition-token", "instance-b"));
            Execute(bus, new M4PlacePieceCommand("m4-place-a", bus.State.revision, "instance-a", 2, 2));
            Execute(bus, new M4PlacePieceCommand("m4-place-b", bus.State.revision, "instance-b", 2, 2));

            Assert.That(M4PieceQueries.FindInstance(bus.State, "instance-a").location.stackOrder, Is.EqualTo(0));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "instance-b").location.stackOrder, Is.EqualTo(1));

            Execute(bus, new M4MovePieceCommand("m4-move-b", bus.State.revision, "instance-b", 3, 2));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "instance-b").location.stackOrder, Is.EqualTo(0));
            Assert.That(M4PieceStateValidator.TryValidate(bus.State, out var diagnostic), Is.True, diagnostic);
        }

        [Test]
        public void ContainerAndAttachmentRelationshipsCanBeDetached()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-a", bus.State.revision, "definition-token", "instance-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-b", bus.State.revision, "definition-token", "instance-b"));
            Execute(bus, new M4MovePieceToContainerCommand("m4-container", bus.State.revision, "instance-a", "instance-b"));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "instance-a").location.kind, Is.EqualTo(M1PieceLocationKind.InContainer));

            Execute(bus, new M4DetachPieceCommand("m4-detach", bus.State.revision, "instance-a"));
            Execute(bus, new M4AttachPieceCommand("m4-attach", bus.State.revision, "instance-a", "instance-b", "rider"));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "instance-a").location.attachmentSlot, Is.EqualTo("rider"));
            Assert.That(M4PieceStateValidator.TryValidate(bus.State, out var diagnostic), Is.True, diagnostic);
        }

        [Test]
        public void RelationshipCycleIsRejectedWithoutChangingState()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-a", bus.State.revision, "definition-token", "instance-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-b", bus.State.revision, "definition-token", "instance-b"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance-c", bus.State.revision, "definition-token", "instance-c"));
            Execute(bus, new M4MovePieceToContainerCommand("m4-container-a", bus.State.revision, "instance-a", "instance-b"));
            Execute(bus, new M4AttachPieceCommand("m4-attach-c", bus.State.revision, "instance-c", "instance-a", "slot"));
            var revisionBefore = bus.State.revision;

            Assert.Throws<InvalidOperationException>(() => bus.Execute(
                new M4MovePieceToContainerCommand("m4-cycle", bus.State.revision, "instance-b", "instance-c")));

            Assert.That(bus.State.revision, Is.EqualTo(revisionBefore));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "instance-b").location.kind, Is.EqualTo(M1PieceLocationKind.Unplaced));
            Assert.That(M4PieceStateValidator.TryValidate(bus.State, out var diagnostic), Is.True, diagnostic);
        }

        [Test]
        public void PresentationStateSupportsRotationFlipAndVisibility()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance", bus.State.revision, "definition-token", "instance-token"));
            Execute(bus, new M4SetPiecePresentationCommand("m4-presentation", bus.State.revision, "instance-token", 90, true, false));

            var instance = M4PieceQueries.FindInstance(bus.State, "instance-token");
            Assert.That(instance.rotation, Is.EqualTo(90));
            Assert.That(instance.flipped, Is.True);
            Assert.That(instance.visible, Is.False);
        }

        [Test]
        public void M4CommandEnvelopeRoundTrips()
        {
            var bus = CreateBusWithDefinition();
            var command = new M4AttachPieceCommand(
                "m4-envelope",
                bus.State.revision,
                "instance-token",
                "target-token",
                "slot-a");
            var envelope = M2CommandEnvelopeCodec.Encode(command);
            var roundTrip = JsonUtility.FromJson<M1CommandEnvelope>(JsonUtility.ToJson(envelope, false));
            var decoded = M2CommandEnvelopeCodec.Decode(roundTrip);

            Assert.That(roundTrip.commandType, Is.EqualTo("M4.AttachPiece"));
            Assert.That(decoded.CommandId, Is.EqualTo(command.CommandId));
            Assert.That(decoded.PayloadVersion, Is.EqualTo(1));
        }

        [Test]
        public void DefinitionCategoryAndTagsUpdateThroughCommandEnvelope()
        {
            var bus = CreateBusWithDefinition();
            var command = new M4UpdatePieceDefinitionCommand(
                "m4-update-definition",
                bus.State.revision,
                "definition-token",
                "中性棋子",
                "Monster",
                new[] { "敌对", "可搜索" },
                null);
            var envelope = M2CommandEnvelopeCodec.Encode(command);
            var decoded = M2CommandEnvelopeCodec.Decode(
                JsonUtility.FromJson<M1CommandEnvelope>(JsonUtility.ToJson(envelope, false)));
            Execute(bus, decoded);

            var definition = M4PieceQueries.FindDefinition(bus.State, "definition-token");
            Assert.That(definition.category, Is.EqualTo("Monster"));
            Assert.That(definition.tags, Is.EqualTo(new[] { "敌对", "可搜索" }));
        }

        [Test]
        public void VersionedJournalReplaysM4MoveAndPreservesCanonicalHash()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-instance", bus.State.revision, "definition-token", "instance-token"));
            Execute(bus, new M4PlacePieceCommand("m4-place", bus.State.revision, "instance-token", 1, 1));
            var snapshot = bus.State.DeepClone();
            var journal = new M2JournalStore(root, "m4-stream");
            var receipt = bus.Execute(new M4MovePieceCommand("m4-journal-move", bus.State.revision, "instance-token", 4, 3));
            journal.Append(M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt), receipt.message, bus.State);

            Assert.That(journal.TryReplay(snapshot, 0, out var replay), Is.True);
            Assert.That(replay.complete, Is.True, replay.diagnostic);
            Assert.That(M4PieceQueries.FindInstance(replay.state, "instance-token").location.x, Is.EqualTo(4));
            Assert.That(M2CanonicalStateHasher.Compute(replay.state), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
        }

        [Test]
        public void AssetCatalogDeduplicatesBytesAndDetectsMissingFile()
        {
            var catalog = new M4PieceAssetCatalog(root);
            var bytes = new byte[] { 1, 2, 3, 4 };
            var first = catalog.Import(bytes, "png", "image/png");
            var second = catalog.Import(bytes, "png", "image/png");

            Assert.That(second.id, Is.EqualTo(first.id));
            Assert.That(catalog.IsAssetAvailable(first), Is.True);
            File.Delete(Path.Combine(root, first.relativePath));
            Assert.That(catalog.IsAssetAvailable(first), Is.False);
        }

        private static M1CommandBus CreateBus()
        {
            return M1VerticalSlice.CreateDemoBus();
        }

        private static M1CommandBus CreateBusWithDefinition()
        {
            var bus = CreateBus();
            Execute(bus, new M4CreatePieceDefinitionCommand(
                "m4-definition",
                bus.State.revision,
                "definition-token",
                "中性棋子",
                "Token",
                new[] { "中性" },
                null));
            return bus;
        }

        private static void Execute(M1CommandBus bus, M1Command command)
        {
            var receipt = bus.Execute(command);
            Assert.That(receipt.accepted, Is.True, receipt.message);
        }
    }
}
