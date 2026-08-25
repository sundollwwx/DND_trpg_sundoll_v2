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
            var sample = M7PerformanceProbe.Measure(() => { var value = 1 + 1; }, 5);
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
    }
}
