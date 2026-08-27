using System.Collections;
using NUnit.Framework;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Sundoll.Tests.PlayMode
{
    public sealed class M7WorkbenchPerformanceTests
    {
        [UnityTest]
        public IEnumerator M7PieceProjectionMeasures1000VisiblePiecesAndSteadyStateAllocations()
        {
            var previousTargetFrameRate = UnityEngine.Application.targetFrameRate;
            var bus = M1VerticalSlice.CreateDemoBus();
            bus.State.map.width = 256;
            bus.State.map.height = 256;
            var library = new M4PieceLibraryFacade(bus);
            var definition = library.CreateDefinition(
                "m7-performance-definition",
                "M7 性能棋子",
                "Performance",
                new[] { "m7", "performance" });
            Assert.That(definition.accepted, Is.True, definition.message);

            for (var index = 0; index < 1000; index++)
            {
                var instanceId = "m7-performance-piece-" + index;
                var created = library.CreateInstance("m7-performance-definition", instanceId);
                Assert.That(created.accepted, Is.True, created.message);
                var placed = library.Place(instanceId, index % 256, (index / 256) % 256);
                Assert.That(placed.accepted, Is.True, placed.message);
            }

            GameObject projectionObject = null;
            try
            {
                UnityEngine.Application.targetFrameRate = 60;
                Screen.SetResolution(2560, 1440, FullScreenMode.Windowed);
                projectionObject = new GameObject("M7WorkbenchPerformanceProjection");
                var projection = projectionObject.AddComponent<M4WorkbenchPieceProjection>();
                projection.Bind(bus);
                Assert.That(projection.Views.Count, Is.EqualTo(1000));

                yield return null;
                projection.RefreshAll();
                yield return null;

                var refresh = M7PerformanceProbe.Measure(() => projection.RefreshAll(), 10);
                var allocations = M7PerformanceProbe.MeasureAllocations(() => projection.RefreshAll(), 10);
                TestContext.WriteLine(
                    "M7 1000-piece projection | target=2560x1440 actual=" + Screen.width + "x" + Screen.height +
                    " targetFps=" + UnityEngine.Application.targetFrameRate +
                    " batchMode=" + UnityEngine.Application.isBatchMode +
                    " refresh p95=" + refresh.p95Milliseconds.ToString("0.000") +
                    "ms max=" + refresh.maxMilliseconds.ToString("0.000") +
                    "ms; allocations p95=" + allocations.p95Bytes +
                    "B max=" + allocations.maxBytes + "B");
            }
            finally
            {
                UnityEngine.Application.targetFrameRate = previousTargetFrameRate;
                if (projectionObject != null)
                {
                    Object.Destroy(projectionObject);
                }
            }

            yield return null;
        }
    }
}
