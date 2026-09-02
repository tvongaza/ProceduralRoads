using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tys (2026-09-02): shallow-water crossings should come in styles for
/// diversity — WADE (paint the ground through the shallows, do not raise
/// it), RAISE (level the road up through the shallows), SPAN (a short
/// bridge over them, with steps at each end where the deck sits above the
/// road). Chosen per site, deterministically, with variety.
/// </summary>
public class FordStyleTests
{
    /// <summary>Flat 33 plateau with a gully of configurable bed height and
    /// width around x = 0; river core |x| &lt; HalfWidth/2.</summary>
    private sealed class GullyWorld : WorldGenerator
    {
        public float Bed = 29.5f;
        public float HalfWidth = 6f;
        public override float GetHeight(float wx, float wy)
        {
            // Bounded by deep water so a pathfinding search stays finite.
            if (Mathf.Abs(wx) > 100f || Mathf.Abs(wy) > 4100f) return 20f;
            return Mathf.Abs(wx) < HalfWidth ? Bed : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / (HalfWidth * 2f));
            width = weight > 0f ? HalfWidth * 4f : 0f;
        }
    }

    private static List<Vector2> Path(float y) =>
        new() { new(-32f, y), new(-24f, y), new(-16f, y), new(16f, y), new(24f, y), new(32f, y) };

    [Fact]
    public void ShallowGullyBecomesAFordCrossingWithAStyle()
    {
        var world = new GullyWorld { Bed = 29.5f };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(Path(0f), world));
        Assert.Equal(CrossingKind.Ford, crossing.Kind);
        Assert.NotEqual(FordStyle.None, crossing.Style);

        // Deep water is still a bridge crossing, not a ford.
        var deep = new GullyWorld { Bed = 27f };
        var bridge = Assert.Single(RoadCrossingDetector.Detect(Path(0f), deep));
        Assert.Equal(CrossingKind.Bridge, bridge.Kind);
        Assert.Equal(FordStyle.None, bridge.Style);
    }

    [Fact]
    public void FordStylesVaryAcrossSitesAndAreDeterministic()
    {
        var world = new GullyWorld { Bed = 29.5f, HalfWidth = 6f };
        var styles = new HashSet<FordStyle>();
        for (float y = 0f; y < 4000f; y += 40f)
        {
            var a = Assert.Single(RoadCrossingDetector.Detect(Path(y), world));
            var b = Assert.Single(RoadCrossingDetector.Detect(Path(y), world));
            Assert.Equal(a.Style, b.Style); // same site, same style
            styles.Add(a.Style);
        }
        Assert.Contains(FordStyle.Wade, styles);
        Assert.Contains(FordStyle.Raise, styles);
        Assert.Contains(FordStyle.Span, styles);
    }

    [Fact]
    public void WadeIsOnlyOfferedWhereTheWaterIsAnkleDeep()
    {
        // Bed 29.3 = 0.7 m deep: too deep to wade through unraised.
        var world = new GullyWorld { Bed = 29.3f };
        for (float y = 0f; y < 4000f; y += 40f)
        {
            var c = Assert.Single(RoadCrossingDetector.Detect(Path(y), world));
            Assert.NotEqual(FordStyle.Wade, c.Style);
        }
    }

    [Fact]
    public void SpanFordHasALowDeckAndStepsAtBothEnds()
    {
        var world = new GullyWorld { Bed = 29.5f, HalfWidth = 8f };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(Path(0f), world));
        crossing.Style = FordStyle.Span;
        var plan = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood);

        Assert.NotEmpty(plan);
        var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck).ToList();
        Assert.NotEmpty(decks);
        foreach (var d in decks)
            Assert.True(d.Position.y >= crossing.WaterLevel + 1f - 0.01f, $"Deck at {d.Position.y:F2} is not clear of the water");
        Assert.True(plan.Count(p => p.Kind == BridgePieceKind.Stair) >= 2, "Expected steps at both ends");
        Assert.DoesNotContain(plan, p => p.Kind == BridgePieceKind.Arch);
    }

    [Fact]
    public void WadeAndRaiseFordsPlaceNoPieces()
    {
        var world = new GullyWorld { Bed = 29.5f };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(Path(0f), world));
        crossing.Style = FordStyle.Wade;
        Assert.Empty(BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood));
        crossing.Style = FordStyle.Raise;
        Assert.Empty(BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood));
    }

    private static void ResetGenerator(WorldGenerator world)
    {
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new RoadPathfinder(world));
    }

    private static void TearDown()
    {
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        RoadSpatialGrid.Clear();
        WorldGenerator.instance = null;
    }

    [Fact]
    public void WadeFordPaintsThroughTheShallowsWithoutRaisingThem()
    {
        // A bounded island with a shallow gully; force the Wade style so
        // the painted road inside the gully sits at the terrain height.
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false, HasWetBand = false };
        ResetGenerator(world);
        try
        {
            RoadCrossingDetector.SetFordStyleWeights(1f, 0f, 0f); // the lever: wade only
            var gully = new GullyWorld { Bed = 29.5f };
            WorldGenerator.instance = gully;
            typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new RoadPathfinder(gully));
            // Pathfinder cells straddle the gully; the ford lands on the far side.
            Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-64f, 0f), 0f, new Vector2(64f, 0f), 0f, 4f, "Wade"));
            var crossing = Assert.Single(RoadNetworkGenerator.GetRoadCrossings());
            Assert.Equal(FordStyle.Wade, crossing.Style);
            var inGully = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(0f, 0f, 0f), 3f);
            Assert.NotEmpty(inGully); // painted through
            foreach (var rp in inGully)
                Assert.True(Mathf.Abs(rp.h - gully.GetHeight(rp.p.x, rp.p.y)) < 0.05f,
                    $"Wade ford point at {rp.p} has road height {rp.h:F2} vs terrain {gully.GetHeight(rp.p.x, rp.p.y):F2}: it was raised");
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); TearDown(); }
    }
}
