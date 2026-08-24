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

            Assert.That(session.PendingTransactions, Is.EqualTo(0));
            Assert.That(session.Validate().valid, Is.True);
            Assert.That(session.ActiveRevisionId, Is.Not.Null.And.Not.Empty);
        }
    }
}
