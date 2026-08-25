using System;
using System.Collections.Generic;

namespace Sundoll.Core
{
    public static class M7ReleaseContract
    {
        public const int SaveFormatVersion = 1;
        public const int WorldSchemaVersion = 2;
        public const int MigrationRegistryVersion = 1;
        public const string MinimumUnityVersion = "6000.3.22f1";
    }

    public sealed class M7MigrationResult
    {
        public bool migrated;
        public int fromSchemaVersion;
        public int toSchemaVersion;
        public M1WorldState state;
        public string diagnostic;
    }

    public interface IM7WorldMigration
    {
        int FromSchemaVersion { get; }
        int ToSchemaVersion { get; }
        M1WorldState Apply(M1WorldState source);
    }

    public sealed class M7Schema1To2Migration : IM7WorldMigration
    {
        public int FromSchemaVersion => 1;
        public int ToSchemaVersion => 2;

        public M1WorldState Apply(M1WorldState source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var migrated = source.DeepClone();
            migrated.EnsureSchema2Defaults();
            migrated.schemaVersion = 2;
            if (migrated.project != null)
            {
                migrated.project.schemaVersion = 2;
            }

            return migrated;
        }
    }

    public sealed class M7MigrationRegistry
    {
        private readonly Dictionary<int, IM7WorldMigration> migrations = new Dictionary<int, IM7WorldMigration>();

        public int Count => migrations.Count;

        public void Register(IM7WorldMigration migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (migration.ToSchemaVersion <= migration.FromSchemaVersion)
            {
                throw new ArgumentException("M7 migration must increase the schema version.", nameof(migration));
            }

            migrations[migration.FromSchemaVersion] = migration;
        }

        public M7MigrationResult Migrate(M1WorldState source, int targetSchemaVersion)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (targetSchemaVersion < source.schemaVersion)
            {
                return new M7MigrationResult
                {
                    migrated = false,
                    fromSchemaVersion = source.schemaVersion,
                    toSchemaVersion = source.schemaVersion,
                    state = source.DeepClone(),
                    diagnostic = "Downgrade is not supported."
                };
            }

            var current = source.DeepClone();
            var from = current.schemaVersion;
            while (current.schemaVersion < targetSchemaVersion)
            {
                if (!migrations.TryGetValue(current.schemaVersion, out var migration))
                {
                    return new M7MigrationResult
                    {
                        migrated = false,
                        fromSchemaVersion = from,
                        toSchemaVersion = current.schemaVersion,
                        state = current,
                        diagnostic = "No migration registered from schema " + current.schemaVersion + "."
                    };
                }

                current = migration.Apply(current);
                if (current == null || current.schemaVersion != migration.ToSchemaVersion)
                {
                    throw new InvalidOperationException("M7 migration returned an invalid schema version.");
                }
            }

            return new M7MigrationResult
            {
                migrated = from != current.schemaVersion,
                fromSchemaVersion = from,
                toSchemaVersion = current.schemaVersion,
                state = current,
                diagnostic = string.Empty
            };
        }

        public static M7MigrationRegistry CreateDefault()
        {
            var registry = new M7MigrationRegistry();
            registry.Register(new M7Schema1To2Migration());
            return registry;
        }
    }

    public sealed class M7PerformanceSample
    {
        public int sampleCount;
        public double p50Milliseconds;
        public double p95Milliseconds;
        public double maxMilliseconds;
    }

    public static class M7PerformanceProbe
    {
        public static M7PerformanceSample Measure(Action action, int sampleCount = 20)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (sampleCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            }

            var samples = new List<double>(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var start = DateTime.UtcNow;
                action();
                samples.Add((DateTime.UtcNow - start).TotalMilliseconds);
            }

            samples.Sort();
            return new M7PerformanceSample
            {
                sampleCount = samples.Count,
                p50Milliseconds = Percentile(samples, 0.50),
                p95Milliseconds = Percentile(samples, 0.95),
                maxMilliseconds = samples[samples.Count - 1]
            };
        }

        private static double Percentile(List<double> sorted, double percentile)
        {
            var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
            index = Math.Max(0, Math.Min(sorted.Count - 1, index));
            return sorted[index];
        }
    }
}
