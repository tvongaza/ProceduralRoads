using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The route recorder (study instrument): it keeps the centreline of every
/// generated road as the spatial grid received it, tags the stretches a
/// crossing produced, and changes nothing about what generation paints.
/// </summary>
public class RouteRecorderTests
{
    private static RoadPathfinder Pathfinder(WorldGenerator world, bool crossings) =>
        new RoadPathfinder(world) { Fords = crossings, Bridges = crossings };

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private static void TearDownGeneration()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f);
        WorldGenerator.instance = null;
    }

    /// <summary>A knee-deep gully a road wades: the ford case.</summary>
    private sealed class GullyWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 100f || Mathf.Abs(wy) > 100f) return 20f;
            return Mathf.Abs(wx) < 12f ? 29.5f : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 24f);
            width = weight > 0f ? 48f : 0f;
        }
    }

    [Fact]
    public void EveryRecordedPointIsARoadPointTheGridHolds()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(Pathfinder(world, false));
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-300f, -100f), 0f, new Vector2(200f, 150f), 0f, 4f, "Dry"));

            RoadRoute route = Assert.Single(RoadRouteRecorder.Routes);
            Assert.Equal("Dry", route.Label);
            Assert.Equal(4f, route.Width);
            Assert.True(route.Points.Count > 2, "A road across the island is more than two points");
            Assert.True(route.Length > 500f, $"Expected a long road, measured {route.Length:F0} m");

            // The grid is the thing that was painted: every recorded point must
            // be one of its road points, at the same height. This is what makes
            // the export a record of the road rather than a re-derivation of it.
            foreach (Vector3 point in route.Points)
            {
                List<RoadSpatialGrid.RoadPoint> near =
                    RoadSpatialGrid.GetRoadPointsNearPosition(point, 0.5f);
                Assert.True(
                    near.Any(p => Vector2.Distance(p.p, new Vector2(point.x, point.z)) < 0.01f
                                  && Mathf.Abs(p.h - point.y) < 0.001f),
                    $"No stored road point at ({point.x:F1},{point.z:F1}) height {point.y:F2}");
            }

            // And the record is complete: the grid holds no more road points
            // than the route accounted for.
            Assert.Equal(route.Points.Count, RoadSpatialGrid.TotalRoadPoints);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void RecordingChangesNothingThatIsPainted()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        try
        {
            byte[]? Generate(bool record)
            {
                RoadNetworkGenerator.Reset();
                SetPathfinder(Pathfinder(world, false));
                if (record)
                    RoadRouteRecorder.Begin("Recorded", 4f);
                RoadSpatialGrid.AddRoadPath(
                    new List<Vector2> { new(-300f, -100f), new(0f, 0f), new(200f, 150f) },
                    4f, world);
                if (record)
                    RoadRouteRecorder.End();
                return RoadSpatialGrid.SerializeAllRoadPoints();
            }

            byte[]? recorded = Generate(true);
            Assert.NotNull(recorded);
            Assert.Single(RoadRouteRecorder.Routes);

            byte[]? plain = Generate(false);
            Assert.Empty(RoadRouteRecorder.Routes);
            Assert.Equal(recorded, plain);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void AWadedFordIsRecordedAsItsOwnStretch()
    {
        var world = new GullyWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        RoadCrossingDetector.SetFordStyleWeights(wade: 1f, raise: 0f, span: 0f);
        SetPathfinder(Pathfinder(world, true));
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-80f, 0f), 0f, new Vector2(80f, 0f), 0f, 4f, "ford"));
            RoadCrossing crossing = Assert.Single(RoadNetworkGenerator.GetRoadCrossings());
            Assert.Equal(CrossingKind.Ford, crossing.Kind);
            Assert.Equal(FordStyle.Wade, crossing.Style);

            RoadRoute route = Assert.Single(RoadRouteRecorder.Routes);
            RoadRouteSegment wade = Assert.Single(
                route.Segments.Where(s => s.Kind == RoadSegmentKind.Wade));

            // The waded stretch is the water, and it follows the riverbed
            // rather than standing on a leveled road.
            for (int i = 0; i < wade.Count; i++)
            {
                Vector3 p = route.Points[wade.StartIndex + i];
                Assert.True(Mathf.Abs(p.x) <= 24f, $"Waded point at x={p.x:F1} is not in the gully");
            }
            Assert.Contains(route.Segments, s => s.Kind == RoadSegmentKind.Road);
            Assert.All(route.Segments, s => Assert.NotEqual(RoadSegmentKind.Span, s.Kind));
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void ABridgeIsRecordedAsAnUnpaintedSpanBetweenItsBanks()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        // No spanned fords: then an unpainted span in the route can only be a
        // bridge, which is what this test is about.
        RoadCrossingDetector.SetFordStyleWeights(wade: 1f, raise: 1f, span: 0f);
        SetPathfinder(Pathfinder(world, true));
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-300f, 0f), 0f, new Vector2(400f, 0f), 0f, 4f, "Cross river"));
            RoadCrossing crossing = Assert.Single(RoadNetworkGenerator.GetRoadCrossings());
            Assert.Equal(CrossingKind.Bridge, crossing.Kind);

            RoadRoute route = Assert.Single(RoadRouteRecorder.Routes);
            RoadRouteSegment span = Assert.Single(
                route.Segments.Where(s => s.Kind == RoadSegmentKind.Span));
            Assert.Equal(2, span.Count);

            Vector3 from = route.Points[span.StartIndex];
            Vector3 to = route.Points[span.StartIndex + 1];
            Assert.True(Vector2.Distance(new Vector2(from.x, from.z), crossing.FromBank) < 0.01f);
            Assert.True(Vector2.Distance(new Vector2(to.x, to.z), crossing.ToBank) < 0.01f);

            // Nothing is painted between the banks: the span is a gap in the
            // road, and the route says so instead of hiding it.
            RoadSpatialGrid.GetRoadWeight(crossing.FairwayCenter.x, crossing.FairwayCenter.y,
                out float weight, out _);
            Assert.Equal(0f, weight);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void TheCsvHasOneRowPerCentrelinePointInPathOrder()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(Pathfinder(world, false));
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-300f, -100f), 0f, new Vector2(200f, 150f), 0f, 4f, "Dry"));
            RoadRoute route = Assert.Single(RoadRouteRecorder.Routes);

            string[] lines = RoadRouteRecorder.ToCsv()
                .Split('\n').Where(l => l.Length > 0).ToArray();
            Assert.Equal("route_index,label,width,segment_index,kind,point_index,x,y,z", lines[0]);
            Assert.Equal(route.Points.Count, lines.Length - 1);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split(',');
                Assert.Equal("0", cells[0]);
                Assert.Equal("\"Dry\"", cells[1]);
                Assert.Equal((i - 1).ToString(), cells[5]);
            }
        }
        finally { TearDownGeneration(); }
    }
}
