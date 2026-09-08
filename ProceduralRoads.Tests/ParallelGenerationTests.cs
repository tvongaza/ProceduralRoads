using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace ProceduralRoads.Tests;

/// <summary>
/// Parallel island generation must be indistinguishable from sequential
/// generation: same points in the same cells in the same order, same totals,
/// same road start points, same network version. Three islands, eight
/// locations, both strategies (island id parity picks MST or chain).
/// </summary>
public class ParallelGenerationTests
{
    private readonly ITestOutputHelper m_out;
    public ParallelGenerationTests(ITestOutputHelper output) { m_out = output; }

    private static SyntheticWorld SetUp()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        world.ExtraIslandCenters.Add(new Vector2(1500f, 0f));
        world.ExtraIslandCenters.Add(new Vector2(0f, 1500f));
        WorldGenerator.instance = world;
        var zones = new ZoneSystem();
        foreach (var (name, x, z, radius) in new[]
        {
            ("StartTemple", 0f, 0f, 25f), ("Eikthyrnir", 260f, 40f, 10f), ("Crypt4", -220f, 150f, 18f),
            ("Crypt4", 1500f, 90f, 18f), ("SunkenCrypt4", 1400f, -110f, 18f), ("Eikthyrnir", 1620f, 60f, 10f),
            ("Crypt4", 60f, 1540f, 18f), ("Crypt4", -90f, 1400f, 18f),
        })
        {
            zones.Locations.Add(new ZoneSystem.LocationInstance
            {
                m_location = new ZoneSystem.ZoneLocation { m_prefab = new ZoneSystem.ZoneLocation.PrefabEntry { Name = name }, m_exteriorRadius = radius },
                m_position = new Vector3(x, world.GetHeight(x, z), z),
            });
        }
        ZoneSystem.instance = zones;
        RoadNetworkGenerator.IslandRoadPercentage = 100;
        RoadNetworkGenerator.Reset();
        return world;
    }

    private static void TearDown()
    {
        RoadNetworkGenerator.IslandRoadPercentage = 50;
        RoadNetworkGenerator.ParallelGeneration = true;
        RoadNetworkGenerator.ParallelDegree = 0;
        RoadNetworkGenerator.Reset();
        ZoneSystem.instance = null;
        WorldGenerator.instance = null;
    }

    private sealed class Snapshot
    {
        public int Points; public float Length; public int Version;
        public List<(Vector2 position, string label)> Starts = new();
        public List<(Vector2i cell, RoadSpatialGrid.RoadPoint[] points)> Cells = new();
        public Snapshot(int points, float length, int version, List<(Vector2 position, string label)> starts, List<(Vector2i cell, RoadSpatialGrid.RoadPoint[] points)> cells)
        { Points = points; Length = length; Version = version; Starts = starts; Cells = cells; }
    }

    private static Snapshot Generate(bool parallel, out double seconds)
    {
        RoadNetworkGenerator.ParallelGeneration = parallel;
        RoadNetworkGenerator.Reset();
        var sw = Stopwatch.StartNew();
        RoadNetworkGenerator.GenerateRoads(force: true);
        seconds = sw.Elapsed.TotalSeconds;
        return new Snapshot(RoadSpatialGrid.TotalRoadPoints, RoadSpatialGrid.TotalRoadLength, RoadSpatialGrid.RoadNetworkVersion,
            new List<(Vector2, string)>(RoadNetworkGenerator.GetRoadStartPoints()), RoadSpatialGrid.SnapshotCells());
    }

    private static void AssertIdentical(Snapshot a, Snapshot b)
    {
        Assert.Equal(a.Points, b.Points);
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(a.Version, b.Version);
        Assert.Equal(a.Starts.Count, b.Starts.Count);
        for (int i = 0; i < a.Starts.Count; i++)
        {
            Assert.Equal(a.Starts[i].label, b.Starts[i].label);
            Assert.Equal(a.Starts[i].position, b.Starts[i].position);
        }
        Assert.Equal(a.Cells.Count, b.Cells.Count);
        for (int i = 0; i < a.Cells.Count; i++)
        {
            Assert.Equal(a.Cells[i].cell, b.Cells[i].cell);
            Assert.Equal(a.Cells[i].points.Length, b.Cells[i].points.Length);
            for (int j = 0; j < a.Cells[i].points.Length; j++)
            {
                Assert.Equal(a.Cells[i].points[j].p, b.Cells[i].points[j].p);
                Assert.Equal(a.Cells[i].points[j].h, b.Cells[i].points[j].h);
                Assert.Equal(a.Cells[i].points[j].w, b.Cells[i].points[j].w);
            }
        }
    }

    [Fact]
    public void ThreeIslandsAreDetectedAndAllGetRoads()
    {
        SetUp();
        try
        {
            Assert.Equal(3, IslandDetector.DetectIslands().Count);
            RoadNetworkGenerator.GenerateRoads(force: true);
            var starts = RoadNetworkGenerator.GetRoadStartPoints();
            int nearOrigin = 0, nearB = 0, nearC = 0;
            foreach (var (p, _) in starts)
            {
                if (p.magnitude < 700f) nearOrigin++;
                else if (Vector2.Distance(p, new Vector2(1500f, 0f)) < 450f) nearB++;
                else if (Vector2.Distance(p, new Vector2(0f, 1500f)) < 450f) nearC++;
            }
            Assert.True(nearOrigin > 0 && nearB > 0 && nearC > 0, $"roads per island: {nearOrigin}/{nearB}/{nearC}");
        }
        finally { TearDown(); }
    }

    [Fact]
    public void ParallelGenerationIsByteIdenticalToSequential()
    {
        SetUp();
        try
        {
            Snapshot sequential = Generate(parallel: false, out double tSeq);
            Assert.True(sequential.Points > 0);
            Snapshot parallel = Generate(parallel: true, out double tPar);
            m_out.WriteLine($"sequential {tSeq:F2}s, parallel {tPar:F2}s, {sequential.Points} points, {sequential.Starts.Count} roads");
            AssertIdentical(sequential, parallel);

            // and again with a single worker, which must also match
            RoadNetworkGenerator.ParallelDegree = 1;
            Snapshot single = Generate(parallel: true, out _);
            AssertIdentical(sequential, single);
        }
        finally { TearDown(); }
    }

    [Fact]
    public void SingleIslandRegenerationMatchesItsShareOfTheGlobalPass()
    {
        SetUp();
        try
        {
            RoadNetworkGenerator.GenerateRoads(force: true);
            int total = RoadSpatialGrid.TotalRoadPoints;
            var allStarts = new List<(Vector2 position, string label)>(RoadNetworkGenerator.GetRoadStartPoints());

            Assert.True(RoadNetworkGenerator.RegenerateIslandAt(new Vector3(1500f, 0f, 0f), out string summary), summary);
            var starts = RoadNetworkGenerator.GetRoadStartPoints();
            Assert.True(starts.Count > 0 && starts.Count < allStarts.Count, $"island B roads: {starts.Count} of {allStarts.Count}");
            foreach (var (p, _) in starts)
                Assert.True(Vector2.Distance(p, new Vector2(1500f, 0f)) < 450f, $"start {p} is not on island B");
            Assert.True(RoadSpatialGrid.TotalRoadPoints < total);
        }
        finally { TearDown(); }
    }
}
