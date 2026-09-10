using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Regressions for three counting mistakes the 9 September review of the
/// strategies study found in the study's own instruments, not in the mod.
/// Each of them made a table say something that was not true, and none of
/// them would have been caught by a test of road generation.
///
/// Study instrument (branch study/road-network-strategies). Not part of any PR.
/// </summary>
public class StudyAccountingTests
{
    /// <summary>Two islands with open sea between them: a road can be built on
    /// either, and never from one to the other.</summary>
    private sealed class TwoIslandsWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            float west = Vector2.Distance(new Vector2(wx, wy), new Vector2(-400f, 0f));
            float east = Vector2.Distance(new Vector2(wx, wy), new Vector2(400f, 0f));
            return Mathf.Min(west, east) < 250f ? 34f : 20f;
        }

        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private static void Setup(WorldGenerator world)
    {
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(new RoadPathfinder(world));
    }

    private static void TearDown()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        StudyFactors.Fallback = FailureFallback.None;
        RoadAttemptLog.Enabled = true;
        RoadPathfinder.Probe = null;
        WorldGenerator.instance = null;
    }

    /// <summary>
    /// A plan may need two searches for one decision - the reverse plan runs a
    /// destination-free search and then builds along what it found - and the
    /// study reported its row count as its connection count, which made one
    /// plan look like it attempted 157 connections where it made 116.
    /// </summary>
    [Fact]
    public void RowsSharingAConnectionAreOneConnection()
    {
        var world = new TwoIslandsWorld();
        Setup(world);
        try
        {
            RoadAttemptLog.NextRow("leg");
            RoadNetworkGenerator.GenerateRoad(new Vector2(-500f, 0f), 0f, new Vector2(-300f, 100f), 0f, 4f, "a");

            int shared = RoadAttemptLog.OpenConnection("branch");
            RoadAttemptLog.NextRow("branch-search", shared);
            RoadNetworkGenerator.GenerateRoad(new Vector2(-500f, 0f), 0f, new Vector2(-300f, -100f), 0f, 4f, "b");
            RoadAttemptLog.NextRow("branch-build", shared);
            RoadNetworkGenerator.GenerateRoad(new Vector2(-300f, -100f), 0f, new Vector2(-400f, 120f), 0f, 4f, "c");

            Assert.Equal(3, RoadAttemptLog.Attempts.Count);
            Assert.Equal(2, RoadAttemptLog.Attempts.Select(a => a.ConnectionId).Distinct().Count());
            Assert.Equal(2, RoadAttemptLog.ConnectionCount);
            Assert.Equal(new[] { "leg", "branch-search", "branch-build" },
                RoadAttemptLog.Attempts.Select(a => a.Role).ToArray());
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>
    /// The nearest-connected-place fallback used to pick the place the failed
    /// leg started from, which is the search that had just failed. On the
    /// issue seed it did that 52 times in 70, at identical iteration counts,
    /// and the sweep built on it reported that no fallback recovers anything.
    /// </summary>
    [Fact]
    public void TheFallbackDoesNotRerunTheSearchThatJustFailed()
    {
        var world = new TwoIslandsWorld();
        Setup(world);
        StudyFactors.Fallback = FailureFallback.NearestConnectedPlace;
        try
        {
            var west = new Vector3(-400f, 0f, 0f);
            var alsoWest = new Vector3(-350f, 0f, 60f);
            var east = new Vector3(400f, 0f, 0f);

            RoadNetworkGenerator.GenerateRoad(new Vector2(west.x, west.z), 0f,
                new Vector2(alsoWest.x, alsoWest.z), 0f, 4f, "west -> alsoWest");
            int before = RoadAttemptLog.Attempts.Count;

            // The leg west -> east fails. The nearest place on the network to
            // east is west itself, and that is the one candidate the fallback
            // must refuse.
            Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(west.x, west.z), 0f,
                new Vector2(east.x, east.z), 0f, 4f, "west -> east"));
            RoadNetworkGenerator.TryFallbackConnection(east, 0f, "east",
                new[] { (west, 0f, "west"), (alsoWest, 0f, "alsoWest") }, west);

            var after = RoadAttemptLog.Attempts.Skip(before).ToList();
            Assert.DoesNotContain(after.Skip(1), a =>
                Vector2.Distance(a.Start, new Vector2(west.x, west.z)) < 1f
                && Vector2.Distance(a.End, new Vector2(east.x, east.z)) < 1f);
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>
    /// A road built FOR a place is trimmed to that place's exterior radius, so
    /// its last point is ON the circle - which makes "a road end within the
    /// place's radius" a float comparison against the number the point was
    /// constructed to equal. Eight places on the issue seed fell the wrong
    /// side of it. This pins the geometry that makes the serving test a knife
    /// edge, so a future change to trimming shows up here rather than as an
    /// unexplained drift in the served count.
    /// </summary>
    [Fact]
    public void ARoadBuiltToAPlaceEndsExactlyOnItsApproachCircle()
    {
        var world = new TwoIslandsWorld();
        Setup(world);
        try
        {
            var centre = new Vector2(-330f, 90f);
            const float radius = 32f;
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-470f, -60f), 0f, centre, radius, 4f, "west -> walled"));

            RoadRoute route = RoadRouteRecorder.Routes.Last();
            Vector3 last = route.Points[route.Points.Count - 1];
            float gap = Vector2.Distance(new Vector2(last.x, last.z), centre);

            Assert.InRange(gap, radius - 0.05f, radius + 0.05f);
        }
        finally
        {
            TearDown();
        }
    }
}
