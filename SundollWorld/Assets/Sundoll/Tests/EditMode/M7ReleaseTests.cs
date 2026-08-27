using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;

namespace Sundoll.Tests.EditMode
{
    public sealed class M7ReleaseTests
    {
        [Test]
        public void Schema1MigratesToSchema2WithoutDroppingData()
        {
            var legacy = M1VerticalSlice.CreateDemoBus().State.DeepClone();
            legacy.schemaVersion = 1;
            legacy.map.objects = null;
            legacy.pieceAssets = null;
            legacy.pieceDefinitions = null;
            legacy.pieceInstances = null;
            legacy.m5Console = null;

            var result = M7MigrationRegistry.CreateDefault().Migrate(legacy, 2);
            Assert.That(result.migrated, Is.True);
            Assert.That(result.state.schemaVersion, Is.EqualTo(2));
            Assert.That(result.state.map.objects, Is.Not.Null);
            Assert.That(result.state.pieceInstances, Is.Not.Null);
            Assert.That(result.state.m5Console, Is.Not.Null);
        }

        [Test]
        public void FrozenSaveRoundTripsAndRejectsTampering()
        {
            var state = M1VerticalSlice.CreateDemoBus().State;
            var frozen = M7FrozenSave.Freeze(state);
            var valid = M7FrozenSave.Validate(frozen);
            Assert.That(valid.valid, Is.True, valid.diagnostic);

            frozen.stateJson = frozen.stateJson.Replace("Sundoll", "Tampered");
            var invalid = M7FrozenSave.Validate(frozen);
            Assert.That(invalid.valid, Is.False);
            Assert.That(invalid.diagnostic, Does.Contain("hash"));
        }

        [Test]
        public void PerformanceProbeReportsPercentilesAndPoolReusesObjects()
        {
            var sample = M7PerformanceProbe.Measure(() => GC.KeepAlive(1 + 1), 5);
            Assert.That(sample.sampleCount, Is.EqualTo(5));
            Assert.That(sample.p95Milliseconds, Is.GreaterThanOrEqualTo(sample.p50Milliseconds));

            var created = 0;
            var pool = new M7ReusablePool<object>(() => { created++; return new object(); });
            var first = pool.Rent();
            pool.Return(first);
            var second = pool.Rent();
            Assert.That(second, Is.SameAs(first));
            Assert.That(created, Is.EqualTo(1));
        }

        [Test]
        public void MacOsPerformanceBaselineRecordsM7Budgets()
        {
            var mutations = BuildFullMapMutations();
            var batch = M7PerformanceProbe.Measure(() =>
            {
                var bus = M1VerticalSlice.CreateDemoBus();
                bus.State.map.width = 256;
                bus.State.map.height = 256;
                bus.State.map.cells.Clear();
                var receipt = new M3MapEditorFacade(bus).PaintCells(mutations);
                if (!receipt.accepted)
                {
                    throw new InvalidOperationException(receipt.message);
                }
            }, 5);

            var filledState = BuildFilledState(mutations);
            var snapshot = M7PerformanceProbe.Measure(() =>
            {
                var copy = filledState.DeepClone();
                if (copy.map == null || copy.map.cells.Count != mutations.Count)
                {
                    throw new InvalidOperationException("Snapshot did not preserve the full map.");
                }
            }, 5);

            var save = M7PerformanceProbe.Measure(() =>
            {
                var root = Path.Combine(Path.GetTempPath(), "Sundoll-M7-Perf-Save-" + Guid.NewGuid().ToString("N"));
                try
                {
                    new M2ProjectStore(root).Save(filledState, "m7-performance", 0);
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
            }, 5);

            var journalRoot = Path.Combine(Path.GetTempPath(), "Sundoll-M7-Perf-Journal-" + Guid.NewGuid().ToString("N"));
            var journal = new M2JournalStore(journalRoot, "performance", 250);
            var journalBus = M1VerticalSlice.CreateDemoBus();
            var journalSnapshot = journalBus.State.DeepClone();
            const int journalBatchCount = 10000;
            for (var index = 0; index < journalBatchCount; index++)
            {
                var command = new M5SetFogCommand(
                    "m7-performance-fog-" + index,
                    journalBus.State.revision,
                    journalBus.State.map.id,
                    index % journalBus.State.map.width,
                    (index / journalBus.State.map.width) % journalBus.State.map.height,
                    index % 2 == 0);
                var receipt = journalBus.Execute(command);
                if (!receipt.accepted)
                {
                    throw new InvalidOperationException(receipt.message);
                }

                journal.Append(
                    M2CommandEnvelopeCodec.CreateAcceptedBatch(receipt),
                    receipt.message,
                    journalBus.State);
            }

            var recovery = M7PerformanceProbe.Measure(() =>
            {
                if (!journal.TryReplay(journalSnapshot, 0, out var replay) ||
                    !replay.complete || replay.appliedCount != journalBatchCount)
                {
                    throw new InvalidOperationException("10,000 Journal batches did not fully recover.");
                }
            }, 3);

            TestContext.WriteLine(
                "M7 macOS baseline | batch 256x256 p50=" + batch.p50Milliseconds.ToString("0.000") +
                "ms p95=" + batch.p95Milliseconds.ToString("0.000") + "ms max=" + batch.maxMilliseconds.ToString("0.000") +
                "ms; snapshot p50=" + snapshot.p50Milliseconds.ToString("0.000") +
                "ms p95=" + snapshot.p95Milliseconds.ToString("0.000") + "ms max=" + snapshot.maxMilliseconds.ToString("0.000") +
                "ms; save p50=" + save.p50Milliseconds.ToString("0.000") +
                "ms p95=" + save.p95Milliseconds.ToString("0.000") + "ms max=" + save.maxMilliseconds.ToString("0.000") +
                "ms; journal 10000 p50=" + recovery.p50Milliseconds.ToString("0.000") +
                "ms p95=" + recovery.p95Milliseconds.ToString("0.000") + "ms max=" + recovery.maxMilliseconds.ToString("0.000") + "ms");

            if (Directory.Exists(journalRoot))
            {
                Directory.Delete(journalRoot, true);
            }
        }

        private static List<M3CellMutation> BuildFullMapMutations()
        {
            var mutations = new List<M3CellMutation>(256 * 256);
            for (var y = 0; y < 256; y++)
            {
                for (var x = 0; x < 256; x++)
                {
                    mutations.Add(new M3CellMutation(x, y, M3MapLayerIds.Terrain, "terrain-ground", false));
                }
            }

            return mutations;
        }

        private static M1WorldState BuildFilledState(List<M3CellMutation> mutations)
        {
            var bus = M1VerticalSlice.CreateDemoBus();
            bus.State.map.width = 256;
            bus.State.map.height = 256;
            bus.State.map.cells.Clear();
            var receipt = new M3MapEditorFacade(bus).PaintCells(mutations);
            Assert.That(receipt.accepted, Is.True, receipt.message);
            return bus.State.DeepClone();
        }
    }
}
