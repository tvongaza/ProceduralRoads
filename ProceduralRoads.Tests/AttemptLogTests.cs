using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The attempt log (study instrument): generation drops a failed edge and
/// moves on, so without this a missing road leaves no trace. Every attempt is
/// kept — connected or not — with the search's own account of what stopped it.
/// </summary>
public class AttemptLogTests
{
    /// <summary>Two islands with open sea between them: a road can be built on
    /// either, and never from one to the other.</summary>
    private sealed class TwoIslandsWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            float west = Vector2.Distance(new Vector2(wx, wy), new Vector2(-400f, 0f));
            float east = Vector2.Distance(new Vector2(wx, wy), new Vector2(400f, 0f));
            float d = Mathf.Min(west, east);
            return d < 250f ? 34f : 20f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private static void TearDownGeneration()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        RoadAttemptLog.Enabled = true;
        RoadPathfinder.Probe = null;
        WorldGenerator.instance = null;
    }

    [Fact]
    public void AConnectedAndAFailedAttemptAreBothKeptWithTheirCause()
    {
        var world = new TwoIslandsWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(new RoadPathfinder(world));
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-500f, 0f), 0f, new Vector2(-300f, 100f), 0f, 4f, "West -> Inland"));
            Assert.False(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-400f, 0f), 0f, new Vector2(400f, 0f), 0f, 4f, "West -> East"));

            Assert.Equal(2, RoadAttemptLog.Attempts.Count);

            RoadAttempt connected = RoadAttemptLog.Attempts[0];
            Assert.True(connected.Connected);
            Assert.Equal("West -> Inland", connected.Label);
            Assert.Equal("found", connected.Outcome);
            Assert.True(connected.RouteLength > 100f, $"measured {connected.RouteLength:F0} m");
            Assert.True(connected.Iterations > 0);
            Assert.Equal(0f, connected.ClosestApproach);

            RoadAttempt failed = RoadAttemptLog.Attempts[1];
            Assert.False(failed.Connected);
            Assert.Equal("West -> East", failed.Label);
            Assert.Equal("no reachable path", failed.Outcome);
            Assert.Equal(0f, failed.RouteLength);
            Assert.Equal(800f, failed.DirectDistance, 1);

            // The failure is explained by where the search actually got to:
            // it filled its own island and stopped at the shore.
            Assert.True(failed.UniqueExpandedCells > 100, $"only {failed.UniqueExpandedCells} cells settled");
            Assert.True(failed.ClosestApproach > 200f,
                $"the frontier should stop an island away, closest {failed.ClosestApproach:F0} m");
            Assert.True(failed.SettledMax.x < 0f,
                $"no settled cell should reach the eastern island, max x {failed.SettledMax.x:F0}");
            Assert.Contains(CrossingRejection.CrossingsDisabled, failed.Rejections.Keys);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void TheCsvHasARowPerAttemptAndAColumnPerRejectionCause()
    {
        var world = new TwoIslandsWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(new RoadPathfinder(world));
        try
        {
            RoadNetworkGenerator.GenerateRoad(new Vector2(-500f, 0f), 0f, new Vector2(-300f, 100f), 0f, 4f, "ok");
            RoadNetworkGenerator.GenerateRoad(new Vector2(-400f, 0f), 0f, new Vector2(400f, 0f), 0f, 4f, "fail");

            string[] lines = RoadAttemptLog.ToCsv().Split('\n').Where(l => l.Length > 0).ToArray();
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("attempt_index,label,connected,outcome,", lines[0]);
            foreach (CrossingRejection cause in System.Enum.GetValues(typeof(CrossingRejection)))
                Assert.Contains($",reject_{cause}", lines[0]);
            Assert.Contains("\"ok\",true,\"found\"", lines[1]);
            Assert.Contains("\"fail\",false,\"no reachable path\"", lines[2]);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void RecordingCanBeSwitchedOffAndThenNothingIsWatched()
    {
        var world = new TwoIslandsWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(new RoadPathfinder(world));
        RoadAttemptLog.Enabled = false;
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-500f, 0f), 0f, new Vector2(-300f, 100f), 0f, 4f, "West -> Inland"));
            Assert.Empty(RoadAttemptLog.Attempts);
            Assert.Null(RoadPathfinder.Probe);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void AStudyRunsOwnProbeIsNotDisplacedByTheLog()
    {
        var world = new TwoIslandsWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(new RoadPathfinder(world));
        PathfinderTrace mine = new();
        RoadPathfinder.Probe = mine;
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-500f, 0f), 0f, new Vector2(-300f, 100f), 0f, 4f, "West -> Inland"));

            // The installed probe saw the search, and the log stood aside
            // rather than recording a half attempt or stealing the field.
            Assert.Same(mine, RoadPathfinder.Probe);
            Assert.True(mine.UniqueExpandedCells > 0);
            Assert.Empty(RoadAttemptLog.Attempts);
        }
        finally { TearDownGeneration(); }
    }
}
