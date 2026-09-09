using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Two crossings on one road can overlap: a bridge's banks walk outward to the
/// bank tops, and a swamp bridge's banks walk on to dry ground, so a crossing's
/// span can reach past the start of the next one. Painting has to survive that.
/// </summary>
public class CrossingOverlapTests
{
    /// <summary>
    /// Two river channels with a low spit between them, both flanked by high
    /// ground: the first crossing's bank top walk climbs past the spit and into
    /// the second channel, so the two crossings overlap on the path.
    /// </summary>
    private sealed class TwinChannelWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 300f || Mathf.Abs(wy) > 300f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax < 20f) return 30.6f;          // the low spit between the channels
            if (ax < 60f) return 25f;            // the two channels, 4 m deep
            if (ax < 66f) return 33f;            // the water's edge
            return 44f;                          // the cliff the deck springs from,
                                                 // inside HighBankReach of the bank
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            float ax = Mathf.Abs(wx);
            weight = ax >= 20f && ax < 60f ? 1f : 0f;
            width = weight > 0f ? 80f : 0f;
        }
    }

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

    /// <summary>Paints a path whose crossings overlap, straight through the
    /// generator's own painting step.</summary>
    private static void Paint(List<Vector2> path, List<RoadCrossing> crossings)
    {
        MethodInfo add = typeof(RoadNetworkGenerator).GetMethod(
            "AddRoadPathWithCrossings", BindingFlags.NonPublic | BindingFlags.Static)!;
        add.Invoke(null, new object[] { path, crossings, 4f });
    }

    [Fact]
    public void OverlappingCrossingsAreStillPainted()
    {
        // A bridge's banks walk outward to the bank tops, and a swamp bridge's
        // walk on to dry ground, so one crossing's span on the path can reach
        // past the start of the next one. The painting step used to assume the
        // spans were disjoint and in order, and threw when they were not - a
        // crash in road generation, reached in a real world with bridges on.
        var world = new TwinChannelWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        try
        {
            List<Vector2> path = new();
            for (float x = -160f; x <= 160f; x += 8f)
                path.Add(new Vector2(x, 0f));

            // Two bridges whose spans overlap on the path, as walked banks do.
            RoadCrossing first = RoadCrossing.Between(
                new Vector2(-72f, 0f), new Vector2(8f, 0f), 25f, new Vector2(-32f, 0f), 60f,
                CrossingKind.Bridge);
            first.FromIndex = 11;
            first.ToIndex = 21;
            RoadCrossing second = RoadCrossing.Between(
                new Vector2(-8f, 0f), new Vector2(72f, 0f), 25f, new Vector2(32f, 0f), 60f,
                CrossingKind.Bridge);
            second.FromIndex = 19;   // inside the first crossing's span
            second.ToIndex = 29;

            Paint(path, new List<RoadCrossing> { first, second });

            // The road exists on the far approaches...
            foreach (float x in new[] { -140f, 140f })
                Assert.True(RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(x, 0f, 0f), 8f).Count > 0,
                    $"no road painted at x={x}");

            // ...and nothing was paved over either channel.
            foreach (float x in new[] { -40f, 40f })
            {
                RoadSpatialGrid.GetRoadWeight(x, 0f, out float weight, out _);
                Assert.Equal(0f, weight);
            }
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void ACrossingSwallowedByTheOneBeforeItIsNotPaintedTwice()
    {
        var world = new TwinChannelWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        try
        {
            List<Vector2> path = new();
            for (float x = -160f; x <= 160f; x += 8f)
                path.Add(new Vector2(x, 0f));

            RoadCrossing wide = RoadCrossing.Between(
                new Vector2(-72f, 0f), new Vector2(72f, 0f), 25f, new Vector2(0f, 0f), 130f,
                CrossingKind.Bridge);
            wide.FromIndex = 11;
            wide.ToIndex = 29;
            RoadCrossing inside = RoadCrossing.Between(
                new Vector2(-8f, 0f), new Vector2(8f, 0f), 25f, new Vector2(0f, 0f), 12f,
                CrossingKind.Bridge);
            inside.FromIndex = 19;
            inside.ToIndex = 21;

            Paint(path, new List<RoadCrossing> { wide, inside });

            foreach (float x in new[] { -140f, 140f })
                Assert.True(RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(x, 0f, 0f), 8f).Count > 0,
                    $"no road painted at x={x}");
            RoadSpatialGrid.GetRoadWeight(0f, 0f, out float weight, out _);
            Assert.Equal(0f, weight);
        }
        finally { TearDownGeneration(); }
    }
}
