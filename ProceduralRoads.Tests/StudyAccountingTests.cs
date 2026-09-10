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

}
