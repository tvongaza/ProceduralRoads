using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tys's in-game calls of 2 Sep 2026 (RoadTestMac2 walk-through): roads
/// should run up to a crypt's door rather than stop 25 m out in the swamp,
/// and a long bridge's collapsed middle should scale with its span.
/// </summary>
public class ApproachAndGapTests
{
    [Theory]
    [InlineData("SunkenCrypt4", 25f, 8f)]
    [InlineData("Crypt3", 20f, 8f)]
    [InlineData("SunkenCrypt4", 6f, 6f)]      // never widened
    [InlineData("WoodVillage1", 40f, 40f)]     // everything else keeps its exterior radius
    [InlineData("Bonemass", 25f, 25f)]
    public void CryptsGetATightApproachRadius(string prefab, float exterior, float expected)
    {
        Assert.Equal(expected, RoadNetworkGenerator.ApproachRadius(prefab, exterior));
    }

    [Fact]
    public void MinUsefulRoadLengthIsLongerThanAnyCryptStubSeen()
    {
        // The RoadTestMac2 stubs were 2-27 m between circles.
        Assert.True(RoadNetworkGenerator.MinUsefulRoadLength >= 30f);
    }

    private static RoadCrossing Crossing(float width, float fairway) => new()
    {
        Kind = CrossingKind.Bridge,
        Width = width,
        FairwayWidth = fairway,
        FromBank = new Vector2(0f, 0f),
        ToBank = new Vector2(width, 0f),
        Direction = new Vector2(1f, 0f),
        Center = new Vector2(width / 2f, 0f),
        FairwayCenter = new Vector2(width / 2f, 0f),
        WaterLevel = RoadConstants.SeaLevel,
    };

    [Theory]
    [InlineData(40f, 30f, 20f)]     // short span: the 20 m floor
    [InlineData(82f, 60f, 24.6f)]   // 30% of the span once that exceeds the floor
    [InlineData(171f, 111f, 51.3f)] // the swamp bridge: a 51 m hole, not 20
    [InlineData(171f, 30f, 30f)]    // never wider than the fairway itself
    public void CollapsedMiddleScalesWithTheSpan(float width, float fairway, float expected)
    {
        Assert.Equal(expected, BridgeLayout.FairwayGap(Crossing(width, fairway)), 1);
    }

    /// <summary>Swamp: ankle-deep water everywhere (31.0, wadeable for the
    /// pathfinder but under the road floor), with one dry hummock at 33.</summary>
    private sealed class SwampWorld : WorldGenerator
    {
        public float HummockHalfWidth = 5f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 300f || Mathf.Abs(wy) > 300f) return 20f;
            return Mathf.Abs(wx) < HummockHalfWidth ? 33f : 31.0f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Swamp;
    }

    [Theory]
    [InlineData(5f, false)]    // 10 m of dry hummock in 80 m of shallows: paint, dropped
    [InlineData(25f, true)]    // 50 m of dry ground: a road
    public void ASwampRouteTrimmedToAHummockIsDropped(float hummockHalfWidth, bool expectRoad)
    {
        var world = new SwampWorld { HummockHalfWidth = hummockHalfWidth };
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator).GetMethod("Reset", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, new RoadPathfinder(world));
        try
        {
            bool built = RoadNetworkGenerator.GenerateRoad(new Vector2(-40f, 0f), 0f, new Vector2(40f, 0f), 0f, 4f, "Crypt -> Crypt");
            Assert.Equal(expectRoad, built);
            if (built)
                Assert.True(Assert.Single(RoadNetworkGenerator.GetRoadRoutes()).Length >= RoadNetworkGenerator.MinUsefulRoadLength);
            else
                Assert.Empty(RoadNetworkGenerator.GetRoadRoutes());
        }
        finally
        {
            typeof(RoadNetworkGenerator).GetField("m_pathfinder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, null);
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null;
        }
    }

    /// <summary>80 m sailable channel; level banks.</summary>
    private sealed class WideChannelWorld : WorldGenerator
    {
        public float HalfBed = 35f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 400f || Mathf.Abs(wy) > 120f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax <= HalfBed) return 26f;
            if (ax >= HalfBed + 10f) return 32f;
            return Mathf.Lerp(26f, 32f, (ax - HalfBed) / 10f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / (HalfBed * 2.3f));
            width = weight > 0f ? HalfBed * 4.6f : 0f;
        }
    }

    [Fact]
    public void ALongerSpanLeavesAWiderHoleInTheDeck()
    {
        // Same kit, no decay: the 80 m channel keeps a ~25 m hole, a 120 m
        // channel a ~36 m one, measured as the longest deck-free stretch.
        float Hole(float halfBed)
        {
            var world = new WideChannelWorld { HalfBed = halfBed };
            var path = new RoadPathfinder(world).FindPath(new Vector2(-halfBed - 90f, 0f), new Vector2(halfBed + 90f, 0f));
            Assert.NotNull(path);
            var c = Assert.Single(RoadCrossingDetector.Detect(path!, world));
            // No ruin decay, so the only hole in the deck is the fairway gap.
            var intact = BridgeStyle.MeadowsWood.WithPierPersistence(0f);
            intact.BankSurvival = 1f;
            intact.MidSurvival = 1f;
            var plan = BridgeLayout.Solve(c, world, 5, intact);
            var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck)
                .Select(p => Vector2.Dot(new Vector2(p.Position.x - c.FromBank.x, p.Position.z - c.FromBank.y), c.Direction))
                .OrderBy(a => a).ToList();
            float longest = 0f;
            for (int i = 1; i < decks.Count; i++) longest = Mathf.Max(longest, decks[i] - decks[i - 1]);
            return longest;
        }
        float small = Hole(35f), large = Hole(55f);
        Assert.True(large > small + 8f, $"hole did not grow with the span: {small:F1} m vs {large:F1} m");
    }
}
