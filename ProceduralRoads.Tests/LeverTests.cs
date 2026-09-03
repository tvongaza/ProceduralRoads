using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tys's four crossing decisions (end of 2 Sep 2026), each a config lever
/// with a product default: piers outlive the deck, ford style weights, what
/// a wet terminus does, and the softened bridge cost. Config binding lives
/// in Plugin.cs (not compiled here); these tests drive the statics it sets.
/// </summary>
public class LeverTests
{
    // ---------------------------------------------------------------
    // Decision 1: ruin rule — piers outlive the deck (default 0.85)
    // ---------------------------------------------------------------

    /// <summary>80 m flat-bottomed sailable river, level banks.</summary>
    private sealed class WideChannelWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 220f || Mathf.Abs(wy) > 120f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax <= 35f) return 26f;
            if (ax >= 45f) return 32f;
            return Mathf.Lerp(26f, 32f, (ax - 35f) / 10f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 80f);
            width = weight > 0f ? 160f : 0f;
        }
    }

    private static string Describe(List<BridgePiece> plan) =>
        string.Join("|", plan.Select(p => $"{p.Kind}:{p.Prefab}:{p.Position.x:F2},{p.Position.y:F2},{p.Position.z:F2}"));

    [Fact]
    public void KitsCarryTheConfiguredPierPersistence_DefaultIsPiersOutliveTheDeck()
    {
        Assert.Equal(0.85f, RoadConstants.DefaultPierPersistence);
        float saved = BridgeLayout.ConfiguredPierPersistence;
        try
        {
            BridgeLayout.ConfiguredPierPersistence = RoadConstants.DefaultPierPersistence;
            BridgeStyle wood = BridgeLayout.StyleFor(Heightmap.Biome.Meadows);
            BridgeStyle stone = BridgeLayout.StyleFor(Heightmap.Biome.Mountain);
            Assert.Equal(RoadConstants.DefaultPierPersistence, wood.PierPersistence);
            Assert.Equal(RoadConstants.DefaultPierPersistence, stone.PierPersistence);
            Assert.Equal(BridgeStyle.MeadowsWood.PilingPrefab, wood.PilingPrefab);
            Assert.Equal(BridgeStyle.MountainStone.PilingPrefab, stone.PilingPrefab);
            // The templates stay at 0, so the lever alone decides.
            Assert.Equal(0f, BridgeStyle.MeadowsWood.PierPersistence);
            Assert.Equal(0f, BridgeStyle.MountainStone.PierPersistence);

            // Out-of-range values are clamped rather than trusted.
            BridgeLayout.ConfiguredPierPersistence = 3f;
            Assert.Equal(1f, BridgeLayout.StyleFor(Heightmap.Biome.Meadows).PierPersistence);
        }
        finally { BridgeLayout.ConfiguredPierPersistence = saved; }
    }

    [Fact]
    public void DefaultPlanKeepsMorePiers_AndZeroReproducesTheOldCoinFlip()
    {
        var world = new WideChannelWorld();
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path!, world));
        Assert.Equal(CrossingKind.Bridge, crossing.Kind);

        float saved = BridgeLayout.ConfiguredPierPersistence;
        try
        {
            BridgeLayout.ConfiguredPierPersistence = RoadConstants.DefaultPierPersistence;
            var withPiers = BridgeLayout.Solve(crossing, world, 42, BridgeLayout.StyleFor(crossing.Biome));

            BridgeLayout.ConfiguredPierPersistence = 0f;
            var coinFlip = BridgeLayout.Solve(crossing, world, 42, BridgeLayout.StyleFor(crossing.Biome));
            var beforeTheLever = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);

            Assert.Equal(Describe(beforeTheLever), Describe(coinFlip));
            int pilingsWith = withPiers.Count(p => p.Kind == BridgePieceKind.Piling);
            int pilingsFlip = coinFlip.Count(p => p.Kind == BridgePieceKind.Piling);
            Assert.True(pilingsWith > pilingsFlip,
                $"Persistent piers should leave more pilings standing ({pilingsWith}) than the coin flip ({pilingsFlip})");
        }
        finally { BridgeLayout.ConfiguredPierPersistence = saved; }
    }

    // ---------------------------------------------------------------
    // Decision 2: ford style weights (default 1/1/1 among allowed styles)
    // ---------------------------------------------------------------

    /// <summary>Flat 33 plateau with a gully of configurable bed height and
    /// width around x = 0.</summary>
    private sealed class GullyWorld : WorldGenerator
    {
        public float Bed = 29.5f;
        public float HalfWidth = 6f;
        public override float GetHeight(float wx, float wy)
        {
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

    private static Dictionary<FordStyle, int> StyleCensus(WorldGenerator world)
    {
        var census = new Dictionary<FordStyle, int>();
        for (float y = 0f; y < 4000f; y += 40f)
        {
            var c = Assert.Single(RoadCrossingDetector.Detect(Path(y), world));
            Assert.Equal(CrossingKind.Ford, c.Kind);
            census[c.Style] = census.TryGetValue(c.Style, out int n) ? n + 1 : 1;
        }
        return census;
    }

    [Fact]
    public void FordStyleWeightsSteerTheMix_AndOnlyAmongStylesTheSiteAllows()
    {
        var world = new GullyWorld { Bed = 29.5f, HalfWidth = 6f }; // wade, raise and span all allowed
        try
        {
            // (0,1,0): every ford raises.
            RoadCrossingDetector.SetFordStyleWeights(0f, 1f, 0f);
            var raiseOnly = StyleCensus(world);
            Assert.Equal(FordStyle.Raise, Assert.Single(raiseOnly).Key);

            // All zero: nothing is disabled into nonsense; the site raises.
            RoadCrossingDetector.SetFordStyleWeights(0f, 0f, 0f);
            Assert.Equal(FordStyle.Raise, Assert.Single(StyleCensus(world)).Key);

            // Skewed: span becomes the majority, the others survive.
            RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 10f);
            var skewed = StyleCensus(world);
            int Count(FordStyle s) => skewed.TryGetValue(s, out int n) ? n : 0; // net48 has no GetValueOrDefault
            Assert.True(Count(FordStyle.Span) > Count(FordStyle.Wade) + Count(FordStyle.Raise),
                $"span {Count(FordStyle.Span)} should dominate wade {Count(FordStyle.Wade)} + raise {Count(FordStyle.Raise)}");
            Assert.True(Count(FordStyle.Wade) > 0 && Count(FordStyle.Raise) > 0);

            // Weights redistribute among ALLOWED styles only: 0.7 m of water is
            // too deep to wade, so wade-only weights still raise the road.
            RoadCrossingDetector.SetFordStyleWeights(1f, 0f, 0f);
            var tooDeep = new GullyWorld { Bed = 29.3f, HalfWidth = 6f };
            Assert.Equal(FordStyle.Raise, Assert.Single(StyleCensus(tooDeep)).Key);

            // Defaults keep the variety; the same site keeps its style.
            RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f);
            var defaults = StyleCensus(world);
            Assert.Equal(3, defaults.Count);
            Assert.Equal(StyleCensus(world), defaults);
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); }
    }

    [Fact]
    public void EqualWeightsPickExactlyAsBeforeTheLever()
    {
        // The modulo pick is what shipped worlds already have; equal weights
        // must not move a single ford.
        var eligible = new List<FordStyle> { FordStyle.Raise, FordStyle.Wade, FordStyle.Span };
        RoadCrossingDetector.SetFordStyleWeights(2f, 2f, 2f);
        try
        {
            for (int hash = 0; hash < 300; hash += 7)
                Assert.Equal(eligible[hash % eligible.Count], RoadCrossingDetector.PickFordStyle(eligible, hash));
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); }
    }

    // ---------------------------------------------------------------
    // Decision 3: wet terminus — Trim | Reroute (default) | Drop
    // ---------------------------------------------------------------

    /// <summary>Flat 33 plateau. A pond (height 29) bites the north of a
    /// location circle at the origin, or, when Flooded, drowns the whole
    /// circle.</summary>
    private sealed class PondAtTheGateWorld : WorldGenerator
    {
        public bool Flooded = false;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 300f || Mathf.Abs(wy) > 300f) return 20f;
            if (Flooded) return wx * wx + wy * wy < 30f * 30f ? 29f : 33f;
            return wy > 5f && Mathf.Abs(wx) < 22f ? 29f : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
    }

    private const float Floor = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;

    private static List<Vector2>? Trim(List<Vector2> path, Vector2 endCenter, float endRadius) =>
        (List<Vector2>?)typeof(RoadNetworkGenerator)
            .GetMethod("TrimPathToRadii", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { path, new Vector2(-1000f, 0f), 0f, endCenter, endRadius });

    /// <summary>A road from the west whose last cell point outside the circle
    /// sits north-west of the location; the path runs on to the center as
    /// pathfinder paths do, and the interpolated point on the circle lands in
    /// the pond.</summary>
    private static List<Vector2> ApproachFromTheNorthWest() =>
        new() { new(-100f, 10f), new(-60f, 10f), new(-40f, 10f), new(-24f, 10f), new(-8f, 2f) };

    [Fact]
    public void WetTerminus_Reroute_EndsOnTheDryArcOfTheCircle()
    {
        var world = new PondAtTheGateWorld();
        WorldGenerator.instance = world;
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            Vector2 center = new(0f, 0f);
            const float radius = 20f;
            // Sanity: the naive circle point is wet.
            Vector2 naive = center + new Vector2(-24f, 10f).normalized * radius;
            Assert.True(world.GetHeight(naive.x, naive.y) < Floor);

            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Reroute;
            var path = Trim(ApproachFromTheNorthWest(), center, radius);
            Assert.NotNull(path);
            Vector2 terminus = path![path.Count - 1];
            Assert.True(path.Count >= 5, "the four cell points plus a leg to the terminus");
            Assert.Equal(new Vector2(-24f, 10f), path[3]); // the anchor: last dry cell point
            Assert.InRange(Vector2.Distance(terminus, center), radius - 0.5f, radius + 0.5f); // on the circle
            Assert.True(terminus.y <= 5f, $"terminus {terminus} is on the wet arc");
            Assert.True(world.GetHeight(terminus.x, terminus.y) >= Floor);
            Assert.True(terminus.x < 0f, $"terminus {terminus} should stay on the approach side");
            // The leg stays dry at every metre (the route's spline is resampled
            // coarser than that, so no road point can be wet) and outside the circle.
            for (int s = 3; s < path.Count - 1; s++)
            {
                int metres = Mathf.CeilToInt(Vector2.Distance(path[s], path[s + 1]));
                for (int i = 0; i <= metres; i++)
                {
                    Vector2 p = Vector2.Lerp(path[s], path[s + 1], (float)i / metres);
                    Assert.True(world.GetHeight(p.x, p.y) >= Floor, $"leg point {p} is wet");
                    Assert.True(Vector2.Distance(p, center) >= radius - 0.5f, $"leg point {p} enters the location");
                }
            }
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; WorldGenerator.instance = null; }
    }

    [Fact]
    public void WetTerminus_Trim_EndsShortAtTheLastDryPoint()
    {
        var world = new PondAtTheGateWorld();
        WorldGenerator.instance = world;
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Trim;
            var path = Trim(ApproachFromTheNorthWest(), new Vector2(0f, 0f), 20f);
            Assert.NotNull(path);
            Assert.Equal(4, path!.Count);
            Assert.Equal(new Vector2(-24f, 10f), path[path.Count - 1]);
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; WorldGenerator.instance = null; }
    }

    [Fact]
    public void WetTerminus_Drop_RefusesTheRoute()
    {
        var world = new PondAtTheGateWorld();
        WorldGenerator.instance = world;
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Drop;
            Assert.Null(Trim(ApproachFromTheNorthWest(), new Vector2(0f, 0f), 20f));

            // A dry approach is unaffected by the setting.
            var dry = Trim(new List<Vector2> { new(-100f, -10f), new(-60f, -10f), new(-24f, -10f), new(-8f, -2f) }, new Vector2(0f, 0f), 20f);
            Assert.NotNull(dry);
            Assert.Equal(4, dry!.Count); // three cell points plus the (dry) circle point
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; WorldGenerator.instance = null; }
    }

    [Fact]
    public void WetTerminus_Reroute_FallsBackToTrimWhenTheWholeCircleIsWet()
    {
        var world = new PondAtTheGateWorld { Flooded = true };
        WorldGenerator.instance = world;
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Reroute;
            var path = Trim(new List<Vector2> { new(-100f, 0f), new(-60f, 0f), new(-40f, 0f), new(-8f, 0f) }, new Vector2(0f, 0f), 20f);
            Assert.NotNull(path);
            Assert.Equal(3, path!.Count);
            Assert.Equal(new Vector2(-40f, 0f), path[path.Count - 1]);
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; WorldGenerator.instance = null; }
    }

    [Fact]
    public void DefaultWetTerminusIsReroute()
    {
        Assert.Equal(WetTerminusMode.Reroute, RoadNetworkGenerator.WetTerminus);
    }

    // The count fix that ships regardless of the setting: routes that end at
    // the same named location are one network even when their dry ends sit
    // apart on the circle.

    private static RoadRoute Route(SyntheticWorld world, int index, string label, Vector2 from, Vector2 to) =>
        RoadRoute.FromWaypoints(index, label, 4f, new List<Vector2> { from, to }, world);

    [Fact]
    public void RoutesEndingOnOneLocationCircleAreOneComponent_SamePrefabFarAwayIsNot()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        // Two roads reach Runestone_Boars on opposite sides of its circle:
        // 50 m apart, beyond the endpoint join radius.
        Assert.True(50f > RoadNetworkValidator.EndpointJoinRadius);
        var a = Route(world, 0, "Eikthyrnir -> Runestone_Boars", new(-200f, 0f), new(-25f, 0f));
        var b = Route(world, 1, "Runestone_Boars -> WoodVillage1", new(25f, 0f), new(200f, 0f));
        Assert.Equal(1, RoadNetworkValidator.Validate(new[] { a, b }, world).NetworkComponents);

        // The same prefab name 400 m away is another location.
        var c = Route(world, 2, "Runestone_Boars -> TrollCave02", new(300f, -300f), new(400f, -300f));
        Assert.Equal(2, RoadNetworkValidator.Validate(new[] { a, b, c }, world).NetworkComponents);

        // Labels without location names still join only by proximity.
        var d = Route(world, 3, "Road 4", new(25f, 60f), new(200f, 60f));
        Assert.Equal(3, RoadNetworkValidator.Validate(new[] { a, b, c, d }, world).NetworkComponents);
    }

    // ---------------------------------------------------------------
    // Decision 4: bridge cost 30000 + 300/m (~2 km rough break-even)
    // ---------------------------------------------------------------

    [Fact]
    public void BridgeCostDefaultsAreSoftened_AndTheLastResortScenarioStillDetours()
    {
        Assert.Equal(30000f, RoadConstants.BridgeCrossingPenalty);
        Assert.Equal(300f, RoadConstants.BridgeCostPerMeter);
        Assert.Equal(RoadConstants.BridgeCrossingPenalty, RoadPathfinder.ConfiguredBridgeCostFixed);
        Assert.Equal(RoadConstants.BridgeCostPerMeter, RoadPathfinder.ConfiguredBridgeCostPerMeter);

        // The exhibit world: a 96 m bridge against a ~1.4 km rough detour.
        // Old flat 20000 bridged it; the softened default still goes around.
        var world = new DecisionExhibitTests.RiverWithAnEndWorld();
        var from = new Vector2(-160f, 0f); var to = new Vector2(160f, 0f);
        Assert.False(Bridged(new RoadPathfinder(world), world, from, to), "default cost should still detour here");
        Assert.True(Bridged(new RoadPathfinder(world) { BridgeCrossingPenalty = 20000f, BridgeCostPerMeter = 0f }, world, from, to),
            "the old flat 20000 bridged here");
    }

    private static bool Bridged(RoadPathfinder pf, WorldGenerator world, Vector2 from, Vector2 to)
    {
        var path = pf.FindPath(from, to);
        Assert.NotNull(path);
        for (int i = 1; i < path!.Count; i++)
        {
            Vector2 mid = (path[i - 1] + path[i]) * 0.5f;
            world.GetRiverWeight(mid.x, mid.y, out float w, out _);
            if (w > RoadConstants.RiverImpassableThreshold && Vector2.Distance(path[i - 1], path[i]) > RoadPathfinder.CellSize * 1.5f)
                return true;
        }
        return false;
    }
}
