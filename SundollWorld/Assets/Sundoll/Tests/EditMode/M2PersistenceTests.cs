using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;
using UnityEngine;

namespace Sundoll.Tests.EditMode
{
    public sealed class M2PersistenceTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "Sundoll-M2-" + Guid.NewGuid().ToString("N"));
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
        public void CanonicalHashSurvivesJsonRoundTripAndDetectsStateChange()
        {
            var state = M1VerticalSlice.CreateDemoBus().State;
            var json = JsonUtility.ToJson(state, false);
            var roundTrip = JsonUtility.FromJson<M1WorldState>(json);

            Assert.That(M2CanonicalStateHasher.Compute(roundTrip), Is.EqualTo(M2CanonicalStateHasher.Compute(state)));
            roundTrip.pieceInstance.location.x++;
            Assert.That(M2CanonicalStateHasher.Compute(roundTrip), Is.Not.EqualTo(M2CanonicalStateHasher.Compute(state)));
        }

        [Test]
        public void VersionedCommandEnvelopeRoundTripsM3CommandAndPayload()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var command = new M3PaintCellsCommand(
                "envelope-m3-paint",
                bus.State.revision,
                new[] { new M3CellMutation(4, 4, M3MapLayerIds.Wall, "wall-solid", false) });

            var envelope = M2CommandEnvelopeCodec.Encode(command);
            var roundTripEnvelope = JsonUtility.FromJson<M1CommandEnvelope>(JsonUtility.ToJson(envelope, false));
            var decoded = M2CommandEnvelopeCodec.Decode(roundTripEnvelope);
            var receipt = bus.Execute(decoded);

            Assert.That(roundTripEnvelope.formatVersion, Is.EqualTo(1));
            Assert.That(roundTripEnvelope.commandType, Is.EqualTo("M3.PaintCells"));
            Assert.That(roundTripEnvelope.payloadVersion, Is.EqualTo(1));
            Assert.That(receipt.accepted, Is.True);
            Assert.That(bus.State.map.cells.Exists(cell => cell.x == 4 && cell.y == 4 && cell.contentId == "wall-solid"), Is.True);
        }

        [Test]
        public void AcceptedOperationBatchCarriesEnvelopeAndWorldChangeSet()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var command = new M3PaintCellsCommand(
                "accepted-batch-m3-paint",
                bus.State.revision,
                new[] { new M3CellMutation(5, 5, M3MapLayerIds.Object, "object-marker", false) });
            var receipt = bus.Execute(command);

            var batch = M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt);
            var roundTrip = JsonUtility.FromJson<AcceptedOperationBatch>(JsonUtility.ToJson(batch, false));

            Assert.That(roundTrip.formatVersion, Is.EqualTo(1));
            Assert.That(roundTrip.commandEnvelope.commandType, Is.EqualTo("M3.PaintCells"));
            Assert.That(roundTrip.changeSet.formatVersion, Is.EqualTo(1));
            Assert.That(roundTrip.changeSet.MapCellDeltaCount, Is.EqualTo(1));
            Assert.That(roundTrip.revisionAfter, Is.EqualTo(roundTrip.revisionBefore + 1));
            Assert.That(M2CommandEnvelopeCodec.Decode(roundTrip.commandEnvelope).CommandId, Is.EqualTo(command.CommandId));
        }

        [Test]
        public void VersionedJournalReplaysCommandWithoutPersistingFullStateJson()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var snapshot = bus.State.DeepClone();
            var journal = new M2JournalStore(root, "stream-test");
            var receipt = bus.Execute(new M1MovePieceCommand("journal-v2-move", bus.State.revision, 6, 0));

            var sequence = journal.Append(
                M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt),
                receipt.message,
                bus.State);

            Assert.That(sequence, Is.EqualTo(1));
            var line = File.ReadAllText(Path.Combine(journal.StreamPath, "segment-000000.log"), new UTF8Encoding(false));
            // Unity JsonUtility may emit an empty compatibility field, but a v2
            // record must never contain the nested full-world JSON object.
            Assert.That(line, Does.Not.Contain("\\\"stateJson\\\":\\\"{"));
            Assert.That(line, Does.Not.Contain("schemaVersion"));

            Assert.That(journal.TryReplay(snapshot, 0, out var replay), Is.True);
            Assert.That(replay.complete, Is.True);
            Assert.That(replay.appliedCount, Is.EqualTo(1));
            Assert.That(replay.state.pieceInstance.location.x, Is.EqualTo(6));
            Assert.That(M2CanonicalStateHasher.Compute(replay.state), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
        }

        [Test]
        public void JournalReplaySupportsMixedLegacyAndVersionedBatches()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var snapshot = bus.State.DeepClone();
            var journal = new M2JournalStore(root, "stream-test");

            bus.Execute(new M1MovePieceCommand("journal-v1-move", bus.State.revision, 5, 0));
            journal.Append("journal-v1-move", "legacy move", bus.State);

            var receipt = bus.Execute(new M1MovePieceCommand("journal-v2-move", bus.State.revision, 6, 0));
            journal.Append(M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt), receipt.message, bus.State);

            Assert.That(journal.TryReplay(snapshot, 0, out var replay), Is.True);
            Assert.That(replay.complete, Is.True);
            Assert.That(replay.appliedCount, Is.EqualTo(2));
            Assert.That(replay.state.pieceInstance.location.x, Is.EqualTo(6));
            Assert.That(M2CanonicalStateHasher.Compute(replay.state), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
        }

        [Test]
        public void SaveSessionReplaysUnsavedVersionedCommandAfterSnapshot()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var session = M2SaveSession.Open(root, bus.State, new M2AutosavePolicy(25, 999f));
            var snapshotRevisionId = session.ActiveRevisionId;
            var editor = new M3MapEditorFacade(bus);
            var receipt = editor.PaintCell(6, 6, "wall-solid");

            Assert.That(receipt.accepted, Is.True);
            session.RecordAccepted(receipt, bus.State);
            Assert.That(session.ActiveRevisionId, Is.EqualTo(snapshotRevisionId));

            var reopened = M2SaveSession.Open(root, bus.State, new M2AutosavePolicy(25, 999f));

            Assert.That(reopened.State.map.cells.Exists(cell =>
                cell.x == 6 && cell.y == 6 && cell.contentId == "wall-solid"), Is.True);
            Assert.That(reopened.LastAction, Does.Contain("Journal"));
            Assert.That(M2CanonicalStateHasher.Compute(reopened.State), Is.EqualTo(M2CanonicalStateHasher.Compute(bus.State)));
        }

        [Test]
        public void VersionedJournalReplaysValidEntriesBeforeCorruptTail()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var snapshot = bus.State.DeepClone();
            var journal = new M2JournalStore(root, "stream-test", 3);
            var receipt = bus.Execute(new M1MovePieceCommand("journal-tail-move", bus.State.revision, 7, 0));
            journal.Append(M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt), receipt.message, bus.State);
            journal.AppendCorruptTail("{\"formatVersion\":2,\"payloadJson\":");

            Assert.That(journal.TryReplay(snapshot, 0, out var replay), Is.True);
            Assert.That(replay.complete, Is.True);
            Assert.That(replay.appliedCount, Is.EqualTo(1));
            Assert.That(replay.state.pieceInstance.location.x, Is.EqualTo(7));
        }

        [Test]
        public void ProjectStoreKeepsImmutableRevisionsAndValidHead()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var store = new M2ProjectStore(root);
            var first = store.Save(bus.State, "stream-test", 0);

            var move = bus.Execute(new M1MovePieceCommand("m2-move", bus.State.revision, 4, 0));
            Assert.That(move.accepted, Is.True);
            var second = store.Save(bus.State, "stream-test", 1);

            Assert.That(second.saveRevisionId, Is.Not.EqualTo(first.saveRevisionId));
            Assert.That(File.Exists(Path.Combine(root, "HEAD.json")), Is.True);
            Assert.That(Directory.Exists(first.revisionPath), Is.True);
            Assert.That(store.LoadActive().state.pieceInstance.location.x, Is.EqualTo(4));
            Assert.That(store.Validate().valid, Is.True);
        }

        [Test]
        public void FailedHeadCommitLeavesPreviousRevisionActive()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var stableStore = new M2ProjectStore(root);
            var first = stableStore.Save(bus.State, "stream-test", 0);
            var stableHeadJson = File.ReadAllText(stableStore.HeadPath, new UTF8Encoding(false));
            bus.Execute(new M1MovePieceCommand("m2-failed-save", bus.State.revision, 5, 0));

            var failingStore = new M2ProjectStore(root, point =>
            {
                if (point == M2SaveFaultPoint.BeforeHeadCommit)
                {
                    throw new IOException("Injected failure before HEAD commit.");
                }
            });
            Assert.Throws<IOException>(() => failingStore.Save(bus.State, "stream-test", 1));

            var loaded = stableStore.LoadActive();
            Assert.That(loaded.manifest.saveRevisionId, Is.EqualTo(first.saveRevisionId));
            Assert.That(loaded.state.pieceInstance.location.x, Is.EqualTo(1));
            Assert.That(File.ReadAllText(stableStore.HeadPath, new UTF8Encoding(false)), Is.EqualTo(stableHeadJson));
        }

        [Test]
        public void MissingHeadRecoversNewestValidImmutableRevisionWithoutRewritingHead()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var store = new M2ProjectStore(root);
            store.Save(bus.State, "stream-test", 0);
            bus.Execute(new M1MovePieceCommand("newest", bus.State.revision, 6, 0));
            var newest = store.Save(bus.State, "stream-test", 1);
            File.Delete(store.HeadPath);

            var recovered = store.LoadBestAvailable();

            Assert.That(recovered.source, Is.EqualTo("RevisionScan"));
            Assert.That(recovered.manifest.saveRevisionId, Is.EqualTo(newest.saveRevisionId));
            Assert.That(recovered.state.pieceInstance.location.x, Is.EqualTo(6));
            Assert.That(recovered.head.generation, Is.EqualTo(0));
            Assert.That(File.Exists(store.HeadPath), Is.False);
        }

        [Test]
        public void CorruptHeadRecoversNewestValidImmutableRevisionWithoutRewritingHead()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var store = new M2ProjectStore(root);
            store.Save(bus.State, "stream-test", 0);
            bus.Execute(new M1MovePieceCommand("newest", bus.State.revision, 7, 0));
            var newest = store.Save(bus.State, "stream-test", 1);
            const string corruptHead = "{\"formatVersion\":";
            File.WriteAllText(store.HeadPath, corruptHead, new UTF8Encoding(false));

            var recovered = store.LoadBestAvailable();

            Assert.That(recovered.source, Is.EqualTo("RevisionScan"));
            Assert.That(recovered.manifest.saveRevisionId, Is.EqualTo(newest.saveRevisionId));
            Assert.That(recovered.state.pieceInstance.location.x, Is.EqualTo(7));
            Assert.That(File.ReadAllText(store.HeadPath, new UTF8Encoding(false)), Is.EqualTo(corruptHead));
        }

        [Test]
        public void ExpectedGenerationConflictDoesNotWriteRevisionOrReplaceHead()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var store = new M2ProjectStore(root);
            var first = store.Save(bus.State, "stream-test", 0);
            bus.Execute(new M1MovePieceCommand("fresh-writer", bus.State.revision, 6, 0));
            var second = store.Save(bus.State, "stream-test", 1);
            var stableHeadJson = File.ReadAllText(store.HeadPath, new UTF8Encoding(false));
            var revisionCount = Directory.GetDirectories(store.RevisionsPath).Length;
            bus.Execute(new M1MovePieceCommand("stale-writer", bus.State.revision, 8, 0));

            var conflict = Assert.Throws<M2GenerationConflictException>(() =>
                store.Save(bus.State, "stream-test", 2, first.generation));

            Assert.That(conflict.ExpectedGeneration, Is.EqualTo(first.generation));
            Assert.That(conflict.ActualGeneration, Is.EqualTo(second.generation));
            Assert.That(Directory.GetDirectories(store.RevisionsPath).Length, Is.EqualTo(revisionCount));
            Assert.That(File.ReadAllText(store.HeadPath, new UTF8Encoding(false)), Is.EqualTo(stableHeadJson));
            Assert.That(store.LoadActive().state.pieceInstance.location.x, Is.EqualTo(6));
        }

        [Test]
        public void JournalSegmentsRecoverLatestValidBatchAfterCorruptTail()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var journal = new M2JournalStore(root, "stream-test", 1);
            journal.Append("first", "first", bus.State);
            bus.Execute(new M1MovePieceCommand("second", bus.State.revision, 6, 0));
            journal.Append("second", "second", bus.State);
            journal.AppendCorruptTail("{\"formatVersion\":1,\"payloadJson\":");

            Assert.That(journal.TryLoadLatest(out var recovery), Is.True);
            Assert.That(recovery.batch.operationSequence, Is.EqualTo(2));
            Assert.That(recovery.state.pieceInstance.location.x, Is.EqualTo(6));
            Assert.That(Directory.GetFiles(Path.Combine(root, "journal", "stream-test"), "segment-*.log").Length, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void JournalSequenceAndSegmentsStayContinuousAcrossRolloverAndReopen()
        {
            var state = M1VerticalSlice.CreateDemoBus().State;
            var journal = new M2JournalStore(root, "stream-test", 2);

            for (var sequence = 1; sequence <= 5; sequence++)
            {
                Assert.That(journal.Append("command-" + sequence, "batch", state), Is.EqualTo(sequence));
            }

            Assert.That(journal.LastSequence, Is.EqualTo(5));
            Assert.That(Directory.GetFiles(journal.StreamPath, "segment-*.log").Length, Is.EqualTo(3));

            var reopened = new M2JournalStore(root, "stream-test", 2);
            Assert.That(reopened.LastSequence, Is.EqualTo(5));
            Assert.That(reopened.Append("command-6", "batch", state), Is.EqualTo(6));
            Assert.That(reopened.TryLoadLatest(out var recovery), Is.True);
            Assert.That(recovery.batch.operationSequence, Is.EqualTo(6));
        }

        [Test]
        public void JournalAppendAfterUnterminatedCorruptTailStartsANewSegment()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var journal = new M2JournalStore(root, "stream-test", 3);
            journal.Append("first", "first", bus.State);
            journal.AppendCorruptTail("{\"formatVersion\":1,\"payloadJson\":");

            bus.Execute(new M1MovePieceCommand("second", bus.State.revision, 7, 0));
            var reopened = new M2JournalStore(root, "stream-test", 3);
            Assert.That(reopened.Append("second", "second", bus.State), Is.EqualTo(2));

            Assert.That(reopened.TryLoadLatest(out var recovery), Is.True);
            Assert.That(recovery.batch.operationSequence, Is.EqualTo(2));
            Assert.That(recovery.state.pieceInstance.location.x, Is.EqualTo(7));
            Assert.That(Directory.GetFiles(reopened.StreamPath, "segment-*.log").Length, Is.EqualTo(2));
        }

        [Test]
        public void ContentBlobsAreAddressedByHashAndVerifiedOnRead()
        {
            var blobs = new M2ContentBlobStore(root);
            var bytes = Encoding.UTF8.GetBytes("new M2 content");
            var asset = blobs.PutAsset(bytes, ".png", "image/png");
            var duplicate = blobs.PutAsset(bytes, "png", "image/png");
            var thumbnail = blobs.PutThumbnail(bytes, "webp", "image/webp");

            Assert.That(duplicate.sha256, Is.EqualTo(asset.sha256));
            Assert.That(asset.relativePath, Does.StartWith("assets/"));
            Assert.That(thumbnail.relativePath, Does.StartWith("thumbnails/"));
            Assert.That(blobs.Read(asset), Is.EqualTo(bytes));
            Assert.That(blobs.TryResolve(asset, out _), Is.True);
        }

        [Test]
        public void PackageRoundTripPreservesActiveRevision()
        {
            var state = M1VerticalSlice.CreateDemoBus().State;
            var store = new M2ProjectStore(root);
            store.Save(state, "stream-test", 0);
            var packagePath = Path.Combine(Path.GetTempPath(), "Sundoll-M2-" + Guid.NewGuid().ToString("N") + ".sundollpkg");
            var importedRoot = Path.Combine(Path.GetTempPath(), "Sundoll-M2-import-" + Guid.NewGuid().ToString("N"));

            try
            {
                store.ExportPackage(packagePath);
                M2PackageArchive.Import(packagePath, importedRoot);
                var imported = new M2ProjectStore(importedRoot).LoadActive();
                Assert.That(imported.state.pieceInstance.location.x, Is.EqualTo(state.pieceInstance.location.x));
                Assert.That(imported.manifest.canonicalStateHash, Is.EqualTo(M2CanonicalStateHasher.Compute(state)));
            }
            finally
            {
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }

                if (Directory.Exists(importedRoot))
                {
                    Directory.Delete(importedRoot, true);
                }
            }
        }

        [Test]
        public void PackageImportRejectsTraversalEntry()
        {
            var packagePath = Path.Combine(Path.GetTempPath(), "Sundoll-M2-malicious-" + Guid.NewGuid().ToString("N") + ".sundollpkg");
            var importedRoot = Path.Combine(Path.GetTempPath(), "Sundoll-M2-rejected-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stream = new FileStream(packagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open(), new UTF8Encoding(false)))
                {
                    writer.Write("unsafe");
                }

                Assert.Throws<InvalidDataException>(() => M2PackageArchive.Import(packagePath, importedRoot));
                Assert.That(Directory.Exists(importedRoot), Is.False);
            }
            finally
            {
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }

                if (Directory.Exists(importedRoot))
                {
                    Directory.Delete(importedRoot, true);
                }
            }
        }

        [Test]
        public void BackgroundSaveQueueCapturesSnapshotAndReportsSafe()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var store = new M2ProjectStore(root);
            var first = store.Save(bus.State, "stream-test", 0);
            var queue = new M2SaveQueue(store);

            try
            {
                bus.Execute(new M1MovePieceCommand("queue-first", bus.State.revision, 4, 0));
                var operation = queue.Enqueue(
                    bus.State,
                    "stream-test",
                    1,
                    first.generation,
                    "queue-first",
                    1);

                // The queued write must use the captured snapshot, not this later state.
                bus.Execute(new M1MovePieceCommand("queue-second", bus.State.revision, 5, 0));

                var result = operation.Wait();
                Assert.That(operation.Status, Is.EqualTo(M2SaveStatus.Safe));
                Assert.That(result.saveRevisionId, Is.Not.Null.And.Not.Empty);
                Assert.That(store.LoadActive().state.pieceInstance.location.x, Is.EqualTo(4));
            }
            finally
            {
                queue.Dispose();
            }
        }

        [Test]
        public void BackgroundSaveQueueReportsFailureAndKeepsPreviousHead()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var stableStore = new M2ProjectStore(root);
            var first = stableStore.Save(bus.State, "stream-test", 0);
            var failingStore = new M2ProjectStore(root, point =>
            {
                if (point == M2SaveFaultPoint.BeforeHeadCommit)
                {
                    throw new IOException("Injected background save failure.");
                }
            });
            var queue = new M2SaveQueue(failingStore);

            try
            {
                bus.Execute(new M1MovePieceCommand("queue-failure", bus.State.revision, 6, 0));
                var operation = queue.Enqueue(
                    bus.State,
                    "stream-test",
                    1,
                    first.generation,
                    "queue-failure");

                Assert.Throws<IOException>(() => operation.Wait());
                Assert.That(operation.Status, Is.EqualTo(M2SaveStatus.Failed));
                Assert.That(operation.Error.Message, Does.Contain("background save failure"));
                Assert.That(stableStore.LoadActive().state.pieceInstance.location.x, Is.EqualTo(1));
            }
            finally
            {
                queue.Dispose();
            }
        }

        [Test]
        public void SaveSessionTracksQueuedSaveAndChangesAfterCapturedSnapshot()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var session = M2SaveSession.Open(root, bus.State, new M2AutosavePolicy(25, 999f));

            try
            {
                var firstReceipt = bus.Execute(new M1MovePieceCommand("session-queue-first", bus.State.revision, 4, 0));
                session.RecordAccepted(firstReceipt, bus.State);
                var firstOperation = session.QueueSave("测试后台保存");

                var secondReceipt = bus.Execute(new M1MovePieceCommand("session-queue-second", bus.State.revision, 5, 0));
                session.RecordAccepted(secondReceipt, bus.State);

                firstOperation.Wait();
                session.RefreshSaveStatus();
                Assert.That(session.SaveStatus, Is.EqualTo(M2SaveStatus.Unsaved));
                Assert.That(session.PendingTransactions, Is.EqualTo(1));

                var secondOperation = session.QueueSave("测试第二次后台保存");
                secondOperation.Wait();
                session.RefreshSaveStatus();

                Assert.That(session.SaveStatus, Is.EqualTo(M2SaveStatus.Safe));
                Assert.That(session.PendingTransactions, Is.EqualTo(0));
                Assert.That(session.State.pieceInstance.location.x, Is.EqualTo(5));
                Assert.That(session.Validate().valid, Is.True);
            }
            finally
            {
                session.Dispose();
            }
        }

        [Test]
        public void SaveSessionAutosavesAtTransactionThreshold()
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            var session = M2SaveSession.Open(root, bus.State, new M2AutosavePolicy(2, 999f));
            bus.Execute(new M1MovePieceCommand("auto-1", bus.State.revision, 2, 0));
            session.RecordAccepted(new M1CommandReceipt
            {
                commandId = "auto-1",
                message = "auto-1",
                accepted = true
            }, bus.State);
            Assert.That(session.PendingTransactions, Is.EqualTo(1));

            bus.Execute(new M1MovePieceCommand("auto-2", bus.State.revision, 3, 0));
            session.RecordAccepted(new M1CommandReceipt
            {
                commandId = "auto-2",
                message = "auto-2",
                accepted = true
            }, bus.State);

            session.WaitForSave();
            Assert.That(session.PendingTransactions, Is.EqualTo(0));
            session.RefreshSaveStatus();
            Assert.That(session.SaveStatus, Is.EqualTo(M2SaveStatus.Safe));
            Assert.That(session.Validate().valid, Is.True);
            Assert.That(session.ActiveRevisionId, Is.Not.Null.And.Not.Empty);
            session.Dispose();
        }
    }
}
