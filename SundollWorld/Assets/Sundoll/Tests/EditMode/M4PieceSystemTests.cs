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
        public void TopmostVisibleBoardInstanceCanBePickedAtCell()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-pick-a", bus.State.revision, "definition-token", "pick-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-pick-b", bus.State.revision, "definition-token", "pick-b"));
            Execute(bus, new M4PlacePieceCommand("m4-pick-place-a", bus.State.revision, "pick-a", 2, 2));
            Execute(bus, new M4PlacePieceCommand("m4-pick-place-b", bus.State.revision, "pick-b", 2, 2));

            var topmost = M4PieceQueries.FindTopmostBoardInstanceAt(bus.State, bus.State.board.id, 2, 2);
            Assert.That(topmost.id, Is.EqualTo("pick-b"));

            Execute(bus, new M4SetPiecePresentationCommand(
                "m4-pick-hide", bus.State.revision, "pick-b", topmost.rotation, topmost.flipped, false));
            var next = M4PieceQueries.FindTopmostBoardInstanceAt(bus.State, bus.State.board.id, 2, 2);
            Assert.That(next.id, Is.EqualTo("pick-a"));
        }

        [Test]
        public void StackOrderCanBeAdjustedWithoutSortingTheWholeWorldState()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-stack-a", bus.State.revision, "definition-token", "stack-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-stack-b", bus.State.revision, "definition-token", "stack-b"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-stack-c", bus.State.revision, "definition-token", "stack-c"));
            Execute(bus, new M4PlacePieceCommand("m4-stack-place-a", bus.State.revision, "stack-a", 2, 2));
            Execute(bus, new M4PlacePieceCommand("m4-stack-place-b", bus.State.revision, "stack-b", 2, 2));
            Execute(bus, new M4PlacePieceCommand("m4-stack-place-c", bus.State.revision, "stack-c", 2, 2));

            Execute(bus, new M4SetPieceStackOrderCommand("m4-stack-move", bus.State.revision, "stack-a", 2));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "stack-a").location.stackOrder, Is.EqualTo(2));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "stack-b").location.stackOrder, Is.EqualTo(0));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "stack-c").location.stackOrder, Is.EqualTo(1));
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
        public void RuntimeStateRoundTripsThroughCommandUndoRedoAndCanonicalHash()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand(
                "m4-runtime-instance",
                bus.State.revision,
                "definition-token",
                "runtime-instance"));
            var beforeHash = M2CanonicalStateHasher.Compute(bus.State);
            var runtimeState = new M4PieceRuntimeState
            {
                hostNote = "只给主持人看的线索",
                audienceVisible = true,
                resourceBars = new System.Collections.Generic.List<M4PieceResourceBar>
                {
                    new M4PieceResourceBar
                    {
                        id = "hp",
                        displayName = "生命",
                        current = 7,
                        maximum = 10,
                        visibleToAudience = true
                    }
                },
                statuses = new System.Collections.Generic.List<M4PieceStatusEntry>
                {
                    new M4PieceStatusEntry
                    {
                        id = "marked",
                        displayName = "标记",
                        detail = "已被追踪",
                        visibleToAudience = false
                    }
                },
                customFields = new System.Collections.Generic.List<M4PieceCustomField>
                {
                    new M4PieceCustomField
                    {
                        key = "阵营",
                        value = "中立",
                        visibleToAudience = true
                    }
                }
            };

            var receipt = bus.Execute(new M4SetPieceRuntimeStateCommand(
                "m4-runtime-state",
                bus.State.revision,
                "runtime-instance",
                runtimeState));
            Assert.That(receipt.accepted, Is.True, receipt.message);
            Assert.That(M2CanonicalStateHasher.Compute(bus.State), Is.Not.EqualTo(beforeHash));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "runtime-instance").runtimeState.hostNote, Is.EqualTo("只给主持人看的线索"));

            Assert.That(bus.Undo(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "runtime-instance").runtimeState.resourceBars, Is.Empty);
            Assert.That(bus.Redo(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "runtime-instance").runtimeState.resourceBars[0].current, Is.EqualTo(7));

            var loaded = JsonUtility.FromJson<M1WorldState>(JsonUtility.ToJson(bus.State, false));
            loaded.EnsureSchema2Defaults();
            Assert.That(M2CanonicalStateHasher.Compute(loaded), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
            Assert.That(M4PieceStateValidator.TryValidate(loaded, out var diagnostic), Is.True, diagnostic);
        }

        [Test]
        public void RuntimeStateCommandEnvelopeAndJournalReplayPreserveState()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand(
                "m4-runtime-envelope-instance",
                bus.State.revision,
                "definition-token",
                "runtime-envelope-instance"));
            var snapshot = bus.State.DeepClone();
            var command = new M4SetPieceRuntimeStateCommand(
                "m4-runtime-envelope",
                bus.State.revision,
                "runtime-envelope-instance",
                new M4PieceRuntimeState
                {
                    hostNote = "journal note",
                    resourceBars = new System.Collections.Generic.List<M4PieceResourceBar>
                    {
                        new M4PieceResourceBar { id = "charge", displayName = "充能", current = 2, maximum = 3 }
                    }
                });
            var receipt = bus.Execute(command);
            Assert.That(receipt.accepted, Is.True, receipt.message);

            var envelope = M2CommandEnvelopeCodec.Encode(command);
            var decoded = M2CommandEnvelopeCodec.Decode(
                JsonUtility.FromJson<M1CommandEnvelope>(JsonUtility.ToJson(envelope, false)));
            Assert.That(decoded, Is.TypeOf<M4SetPieceRuntimeStateCommand>());

            var journal = new M2JournalStore(root, "m4-runtime-state-stream");
            journal.Append(M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt), receipt.message, bus.State);
            Assert.That(journal.TryReplay(snapshot, 0, out var replay), Is.True, replay.diagnostic);
            Assert.That(replay.complete, Is.True, replay.diagnostic);
            Assert.That(
                M2CanonicalStateHasher.Compute(replay.state),
                Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
            Assert.That(
                M4PieceQueries.FindInstance(replay.state, "runtime-envelope-instance").runtimeState.hostNote,
                Is.EqualTo("journal note"));
        }

        [Test]
        public void InvalidRuntimeStateIsRejectedWithoutMutation()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand(
                "m4-runtime-invalid-instance",
                bus.State.revision,
                "definition-token",
                "runtime-invalid-instance"));
            var hashBefore = M2CanonicalStateHasher.Compute(bus.State);
            var revisionBefore = bus.State.revision;

            Assert.Throws<InvalidOperationException>(() => bus.Execute(new M4SetPieceRuntimeStateCommand(
                "m4-runtime-invalid",
                bus.State.revision,
                "runtime-invalid-instance",
                new M4PieceRuntimeState
                {
                    resourceBars = new System.Collections.Generic.List<M4PieceResourceBar>
                    {
                        new M4PieceResourceBar { id = "hp", current = 11, maximum = 10 }
                    }
                })));

            Assert.That(bus.State.revision, Is.EqualTo(revisionBefore));
            Assert.That(M2CanonicalStateHasher.Compute(bus.State), Is.EqualTo(hashBefore));
        }

        [Test]
        public void MultiPieceMoveIsOneAtomicUndoableJournalOperation()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-batch-a", bus.State.revision, "definition-token", "batch-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-batch-b", bus.State.revision, "definition-token", "batch-b"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-batch-existing", bus.State.revision, "definition-token", "batch-existing"));
            Execute(bus, new M4PlacePieceCommand("m4-batch-place-a", bus.State.revision, "batch-a", 1, 1));
            Execute(bus, new M4PlacePieceCommand("m4-batch-place-b", bus.State.revision, "batch-b", 2, 1));
            Execute(bus, new M4PlacePieceCommand("m4-batch-place-existing", bus.State.revision, "batch-existing", 4, 3));
            var snapshot = bus.State.DeepClone();
            var revisionBefore = bus.State.revision;
            var command = new M4MovePiecesCommand(
                "m4-batch-move",
                revisionBefore,
                new[]
                {
                    new M4PieceMoveMutation("batch-a", 4, 3),
                    new M4PieceMoveMutation("batch-b", 4, 3)
                });

            var receipt = bus.Execute(command);
            Assert.That(receipt.accepted, Is.True, receipt.message);
            Assert.That(bus.State.revision, Is.EqualTo(revisionBefore + 1));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "batch-a").location.stackOrder, Is.EqualTo(1));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "batch-b").location.stackOrder, Is.EqualTo(2));
            Assert.That(M4PieceStateValidator.TryValidate(bus.State, out var diagnostic), Is.True, diagnostic);
            var acceptedState = bus.State.DeepClone();

            Assert.That(bus.Undo(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "batch-a").location.x, Is.EqualTo(1));
            Assert.That(bus.Redo(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "batch-b").location.x, Is.EqualTo(4));

            var envelope = M2CommandEnvelopeCodec.Encode(command);
            var decoded = M2CommandEnvelopeCodec.Decode(
                JsonUtility.FromJson<M1CommandEnvelope>(JsonUtility.ToJson(envelope, false)));
            Assert.That(decoded, Is.TypeOf<M4MovePiecesCommand>());
            var journal = new M2JournalStore(root, "m4-batch-stream");
            journal.Append(M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt), receipt.message, acceptedState);
            Assert.That(journal.TryReplay(snapshot, 0, out var replay), Is.True);
            Assert.That(replay.complete, Is.True, replay.diagnostic);
            Assert.That(M2CanonicalStateHasher.Compute(replay.state), Is.EqualTo(M2CanonicalStateHasher.Compute(acceptedState)));
        }

        [Test]
        public void MultiPieceMoveRejectsAnInvalidDestinationWithoutPartialMutation()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-invalid-a", bus.State.revision, "definition-token", "invalid-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-invalid-b", bus.State.revision, "definition-token", "invalid-b"));
            Execute(bus, new M4PlacePieceCommand("m4-invalid-place-a", bus.State.revision, "invalid-a", 1, 1));
            Execute(bus, new M4PlacePieceCommand("m4-invalid-place-b", bus.State.revision, "invalid-b", 2, 1));
            var hashBefore = M2CanonicalStateHasher.Compute(bus.State);
            var revisionBefore = bus.State.revision;

            Assert.Throws<InvalidOperationException>(() => bus.Execute(new M4MovePiecesCommand(
                "m4-invalid-move",
                bus.State.revision,
                new[]
                {
                    new M4PieceMoveMutation("invalid-a", 4, 3),
                    new M4PieceMoveMutation("invalid-b", -1, 3)
                })));

            Assert.That(bus.State.revision, Is.EqualTo(revisionBefore));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "invalid-a").location.x, Is.EqualTo(1));
            Assert.That(M2CanonicalStateHasher.Compute(bus.State), Is.EqualTo(hashBefore));
        }

        [Test]
        public void MultiPiecePresentationAndDeletionAreAtomicAndUndoable()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-present-a", bus.State.revision, "definition-token", "present-a"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-present-b", bus.State.revision, "definition-token", "present-b"));
            Execute(bus, new M4SetPiecePresentationsCommand(
                "m4-present-batch",
                bus.State.revision,
                new[]
                {
                    new M4PiecePresentationMutation("present-a", 90, true, false),
                    new M4PiecePresentationMutation("present-b", 180, false, false)
                }));

            Assert.That(M4PieceQueries.FindInstance(bus.State, "present-a").rotation, Is.EqualTo(90));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "present-b").visible, Is.False);
            Assert.That(bus.Undo(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "present-a").visible, Is.True);
            Assert.That(bus.Redo(), Is.True);

            var delete = new M4DeletePiecesCommand(
                "m4-delete-batch",
                bus.State.revision,
                new[] { "present-a", "present-b" });
            var envelope = M2CommandEnvelopeCodec.Encode(delete);
            Assert.That(M2CommandEnvelopeCodec.Decode(envelope), Is.TypeOf<M4DeletePiecesCommand>());
            Execute(bus, delete);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "present-a"), Is.Null);
            Assert.That(bus.Undo(), Is.True);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "present-b"), Is.Not.Null);
        }

        [Test]
        public void DeletingRelationshipTargetRequiresSelectingDependentsToo()
        {
            var bus = CreateBusWithDefinition();
            Execute(bus, new M4CreatePieceInstanceCommand("m4-delete-host", bus.State.revision, "definition-token", "host"));
            Execute(bus, new M4CreatePieceInstanceCommand("m4-delete-child", bus.State.revision, "definition-token", "child"));
            Execute(bus, new M4MovePieceToContainerCommand("m4-delete-container", bus.State.revision, "child", "host"));
            var revisionBefore = bus.State.revision;

            Assert.Throws<InvalidOperationException>(() => bus.Execute(new M4DeletePiecesCommand(
                "m4-delete-host-only", bus.State.revision, new[] { "host" })));
            Assert.That(bus.State.revision, Is.EqualTo(revisionBefore));
            Assert.That(M4PieceQueries.FindInstance(bus.State, "host"), Is.Not.Null);
            Assert.That(M4PieceQueries.FindInstance(bus.State, "child").location.kind, Is.EqualTo(M1PieceLocationKind.InContainer));

            Execute(bus, new M4DeletePiecesCommand(
                "m4-delete-together", bus.State.revision, new[] { "host", "child" }));
            Assert.That(bus.State.pieceInstances, Is.Empty);
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
