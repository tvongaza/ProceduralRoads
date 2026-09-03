using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Sweeps of the four crossing levers (Tys, 2 Sep 2026): each lever is
/// driven through its extremes and the values in between, and the response
/// is checked for direction, endpoints and invariants rather than for one
/// hand-picked value. LeverTests covers the defaults and the mechanics;
/// this file proves the knobs turn the way the config text promises.
/// </summary>
public class LeverSweepTests
{
    // ===============================================================
    // 1. Bridges/PierPersistence: 0 (coin flip) .. 1 (every pier stands)
    // ===============================================================

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

    private static RoadCrossing WideCrossing(WorldGenerator world)
    {
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);
        var c = Assert.Single(RoadCrossingDetector.Detect(path!, world));
        Assert.Equal(CrossingKind.Bridge, c.Kind);
        return c;
    }

    /// <summary>The wood kit ties every surviving station with exactly one
    /// beam, so beams count standing stations (stubs and debris do not).</summary>
    private static int Stations(List<BridgePiece> plan) => plan.Count(p => p.Kind == BridgePieceKind.Beam);

    [Fact]
    public void PierPersistence_SweepIsMonotone_AndOneKeepsEveryPierOutsideTheFairway()
    {
        var world = new WideChannelWorld();
        var crossing = WideCrossing(world);
        float[] levels = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        const int seeds = 40;

        var mean = new double[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            var style = BridgeStyle.MeadowsWood.WithPierPersistence(levels[i]);
            int total = 0;
            for (int seed = 1; seed <= seeds; seed++)
                total += Stations(BridgeLayout.Solve(crossing, world, seed, style));
            mean[i] = (double)total / seeds;
        }

        // Direction: more persistence, more standing stations (sampling noise
        // over 40 seeds is well under one station).
        for (int i = 1; i < levels.Length; i++)
            Assert.True(mean[i] >= mean[i - 1] - 0.5,
                $"persistence {levels[i]} left fewer stations ({mean[i]:F1}) than {levels[i - 1]} ({mean[i - 1]:F1})");
        Assert.True(mean[levels.Length - 1] - mean[0] >= 5.0,
            $"the lever barely moved the pier count: {mean[0]:F1} -> {mean[levels.Length - 1]:F1}");

        // Endpoint: at 1 the only stations missing are the fairway's — the
        // same set a kit with certain survival produces.
        var full = BridgeStyle.MeadowsWood.WithPierPersistence(0f);
        full.BankSurvival = 1f;
        full.MidSurvival = 1f;
        var certain = BridgeLayout.Solve(crossing, world, 7, full);
        var persistent = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood.WithPierPersistence(1f));
        Assert.Equal(Stations(certain), Stations(persistent));
        Assert.True(Stations(certain) > 0);
        Assert.Equal(StationAlongs(certain, crossing), StationAlongs(persistent, crossing));

        // Endpoint: at 0 the plan is the template's plan (the lever is absent).
        var zero = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood.WithPierPersistence(0f));
        var template = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood);
        Assert.Equal(template.Count, zero.Count);
        Assert.Equal(StationAlongs(template, crossing), StationAlongs(zero, crossing));
    }

    private static List<int> StationAlongs(List<BridgePiece> plan, RoadCrossing c) =>
        plan.Where(p => p.Kind == BridgePieceKind.Beam)
            .Select(p => Mathf.RoundToInt(Vector2.Dot(new Vector2(p.Position.x - c.FromBank.x, p.Position.z - c.FromBank.y), c.Direction)))
            .OrderBy(a => a).ToList();

    [Fact]
    public void PierPersistence_TheFairwayStaysClearAtEveryLevel()
    {
        // Sailing is sacred: no persistence value may put a pier in the gap.
        var world = new WideChannelWorld();
        var crossing = WideCrossing(world);
        Assert.True(crossing.FairwayWidth > 0f);
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - crossing.FromBank, crossing.Direction);
        float half = BridgeLayout.FairwayGap(crossing) * 0.5f + BridgeLayout.FairwayClearance;

        foreach (float level in new[] { 0f, 0.1f, 0.5f, 0.9f, 1f })
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var plan = BridgeLayout.Solve(crossing, world, seed, BridgeStyle.MeadowsWood.WithPierPersistence(level));
                foreach (var piece in plan.Where(p => p.Kind == BridgePieceKind.Piling || p.Kind == BridgePieceKind.Debris))
                {
                    float along = Vector2.Dot(new Vector2(piece.Position.x - crossing.FromBank.x, piece.Position.z - crossing.FromBank.y), crossing.Direction);
                    Assert.True(Mathf.Abs(along - fairwayMid) > half - 0.01f,
                        $"persistence {level} seed {seed}: {piece.Kind} at along={along:F1} sits in the fairway ({fairwayMid:F1} +- {half:F1})");
                }
            }
        }
    }

    // ===============================================================
    // 2. Fords/*Weight: 0 (never) .. dominant, and the shares in between
    // ===============================================================

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

    private const int Sites = 100;

    private static Dictionary<FordStyle, int> Census(WorldGenerator world)
    {
        var census = new Dictionary<FordStyle, int> { [FordStyle.Wade] = 0, [FordStyle.Raise] = 0, [FordStyle.Span] = 0 };
        for (int i = 0; i < Sites; i++)
        {
            var c = Assert.Single(RoadCrossingDetector.Detect(Path(i * 40f), world));
            Assert.Equal(CrossingKind.Ford, c.Kind);
            census[c.Style]++;
        }
        return census;
    }

    [Theory]
    [InlineData(FordStyle.Wade)]
    [InlineData(FordStyle.Raise)]
    [InlineData(FordStyle.Span)]
    public void FordWeight_SweepOfOneStyleAgainstTheOthersIsMonotone(FordStyle style)
    {
        var world = new GullyWorld { Bed = 29.5f, HalfWidth = 6f }; // every style allowed at every site
        float[] weights = { 0f, 0.25f, 0.5f, 1f, 2f, 5f, 20f, 100f };
        try
        {
            int previous = -1;
            var shares = new List<int>();
            foreach (float w in weights)
            {
                RoadCrossingDetector.SetFordStyleWeights(
                    style == FordStyle.Wade ? w : 1f,
                    style == FordStyle.Raise ? w : 1f,
                    style == FordStyle.Span ? w : 1f);
                int share = Census(world)[style];
                shares.Add(share);
                // The hash is a fixed pseudo-random draw per site, so the share
                // can wobble by a few sites between adjacent weights, never fall.
                Assert.True(share >= previous - 3,
                    $"{style} weight {w}: share {share}/{Sites} fell below the previous {previous}");
                previous = share;
            }
            string trace = string.Join(", ", weights.Zip(shares, (w, s) => $"{w}:{s}"));
            Assert.Equal(0, shares[0]);                         // 0 = never
            Assert.InRange(shares[3], 20, 46);                  // 1/1/1: about a third
            Assert.True(shares[shares.Count - 1] >= 90, trace); // 100:1:1 ~= 98% (may round to all 100 sites)
            // The expected share w / (w + 2) is tracked within sampling tolerance.
            for (int i = 0; i < weights.Length; i++)
            {
                double expected = weights[i] / (weights[i] + 2f) * Sites;
                Assert.InRange(shares[i], expected - 12, expected + 12);
            }
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); }
    }

    [Fact]
    public void FordWeight_MixedWeightsShareProportionally_AndAreStableAcrossCalls()
    {
        var world = new GullyWorld { Bed = 29.5f, HalfWidth = 6f };
        try
        {
            RoadCrossingDetector.SetFordStyleWeights(1f, 2f, 1f);
            var first = Census(world);
            Assert.InRange(first[FordStyle.Raise], 38, 62);   // 2/4 of the sites
            Assert.InRange(first[FordStyle.Wade], 13, 37);    // 1/4
            Assert.InRange(first[FordStyle.Span], 13, 37);    // 1/4
            Assert.Equal(first, Census(world));               // same hash, same fords
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); }
    }

    [Fact]
    public void FordWeight_IneligibleStylesNeverAppearAtAnyWeight()
    {
        try
        {
            // 0.7 m deep: wading is off the menu whatever its weight.
            RoadCrossingDetector.SetFordStyleWeights(100f, 1f, 1f);
            var deep = Census(new GullyWorld { Bed = 29.3f, HalfWidth = 6f });
            Assert.Equal(0, deep[FordStyle.Wade]);
            Assert.True(deep[FordStyle.Raise] > 0 && deep[FordStyle.Span] > 0);

            // Sweep the weight of an ineligible style: the mix of the others must not move.
            RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f);
            var baseline = Census(new GullyWorld { Bed = 29.3f, HalfWidth = 6f });
            foreach (float w in new[] { 0f, 5f, 100f })
            {
                RoadCrossingDetector.SetFordStyleWeights(w, 1f, 1f);
                Assert.Equal(baseline, Census(new GullyWorld { Bed = 29.3f, HalfWidth = 6f }));
            }
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); }
    }

    // ===============================================================
    // 3. Roads/WetTerminus across wet arcs from a sliver to the whole circle
    // ===============================================================

    /// <summary>Flat 33 plateau; a wet wedge (height 29) opens from the
    /// location's center toward the approach (from -x), spanning ± HalfAngle
    /// degrees around it, out to Radius + 4 m. HalfAngle 0 = dry, 180 = the
    /// whole circle wet.</summary>
    private sealed class WedgeWorld : WorldGenerator
    {
        public float Radius = 20f;
        public float HalfAngle = 30f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 400f || Mathf.Abs(wy) > 400f) return 20f;
            if (HalfAngle <= 0f) return 33f;
            float d = Mathf.Sqrt(wx * wx + wy * wy);
            if (d > Radius + 4f) return 33f;
            return OffsetFromWest(wx, wy) < HalfAngle ? 29f : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
    }

    /// <summary>Angular distance, in degrees, of the direction (x, y) from due west (the approach axis).</summary>
    private static float OffsetFromWest(float x, float y) =>
        180f - Mathf.Abs((float)Math.Atan2(y, x) * 180f / (float)Math.PI);

    private const float Floor = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;

    private static List<Vector2>? Trim(List<Vector2> path, float radius) =>
        (List<Vector2>?)typeof(RoadNetworkGenerator)
            .GetMethod("TrimPathToRadii", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { path, new Vector2(-1000f, 0f), 0f, new Vector2(0f, 0f), radius });

    private static List<Vector2> ApproachFromTheWest(float radius) =>
        new() { new(-200f, 0f), new(-100f, 0f), new(-(radius + 14f), 0f), new(-(radius + 6f), 0f), new(-radius * 0.4f, 0f) };

    [Theory]
    [InlineData(12f)]
    [InlineData(20f)]
    [InlineData(40f)]
    public void WetTerminus_SweepOfTheWetArc_RerouteHoldsItsInvariantsThenFallsBackToTrim(float radius)
    {
        float[] halfAngles = { 0f, 5f, 15f, 30f, 45f, 60f, 90f, 135f, 180f };
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            bool previousRerouted = true;
            var trace = new List<string>();
            foreach (float half in halfAngles)
            {
                var world = new WedgeWorld { Radius = radius, HalfAngle = half };
                WorldGenerator.instance = world;
                Vector2 wetEdge = new(-radius, 0f);
                bool edgeWet = world.GetHeight(wetEdge.x, wetEdge.y) < Floor;
                Assert.Equal(half > 0f, edgeWet);

                RoadNetworkGenerator.WetTerminus = WetTerminusMode.Trim;
                var trimmed = Trim(ApproachFromTheWest(radius), radius)!;
                RoadNetworkGenerator.WetTerminus = WetTerminusMode.Drop;
                var dropped = Trim(ApproachFromTheWest(radius), radius);
                RoadNetworkGenerator.WetTerminus = WetTerminusMode.Reroute;
                var rerouted = Trim(ApproachFromTheWest(radius), radius)!;

                if (!edgeWet)
                {
                    // Dry approach: every mode ends on the circle, unchanged.
                    Assert.NotNull(dropped);
                    Assert.Equal(trimmed, rerouted);
                    Assert.Equal(trimmed, dropped);
                    Assert.InRange(trimmed[trimmed.Count - 1].magnitude, radius - 0.5f, radius + 0.5f);
                    trace.Add($"{half}:dry");
                    continue;
                }

                Assert.Null(dropped);                                   // Drop refuses a wet approach
                Assert.Equal(new Vector2(-(radius + 6f), 0f), trimmed[trimmed.Count - 1]); // Trim ends at the last dry cell

                bool didReroute = rerouted.Count > trimmed.Count;
                if (didReroute)
                {
                    Vector2 t = rerouted[rerouted.Count - 1];
                    Assert.Equal(trimmed, rerouted.Take(trimmed.Count).ToList()); // the leg only extends the trimmed path
                    Assert.InRange(t.magnitude, radius - 0.5f, radius + 0.5f);       // on the circle
                    Assert.True(world.GetHeight(t.x, t.y) >= Floor, $"terminus {t} is wet");
                    Assert.True((t - wetEdge).magnitude <= radius + 0.5f, $"terminus {t} is more than a radius from the wet point");
                    for (int s = trimmed.Count - 1; s < rerouted.Count - 1; s++)
                    {
                        int metres = Mathf.CeilToInt((rerouted[s + 1] - rerouted[s]).magnitude);
                        for (int i = 0; i <= metres; i++)
                        {
                            Vector2 p = Vector2.Lerp(rerouted[s], rerouted[s + 1], (float)i / metres);
                            Assert.True(world.GetHeight(p.x, p.y) >= Floor, $"half {half}: leg point {p} is wet");
                            Assert.True(p.magnitude >= radius - 0.5f, $"half {half}: leg point {p} enters the location");
                        }
                    }
                    // The terminus sits just past the wet arc, never deep inside the dry side.
                    float angleOff = OffsetFromWest(t.x, t.y);
                    Assert.True(angleOff >= half - 0.5f, $"terminus at {angleOff:F1} deg is inside the {half} deg wet wedge");
                    trace.Add($"{half}:reroute@{angleOff:F0}deg");
                }
                else
                {
                    Assert.Equal(trimmed, rerouted);                   // honest fallback, nothing invented
                    trace.Add($"{half}:trim");
                }

                // Once the arc is too wide to reroute, wider arcs never reroute again.
                Assert.True(didReroute || !previousRerouted || half == halfAngles[1] || true);
                if (!didReroute) previousRerouted = false;
                else Assert.True(previousRerouted, $"reroute came back at {half} deg after failing at a narrower arc: {string.Join(" ", trace)}");
            }
            string summary = string.Join(" ", trace);
            Assert.Contains("5:reroute", summary);      // a sliver of water is walked around
            Assert.Contains("15:reroute", summary);
            Assert.Contains("180:trim", summary);       // a drowned circle is left alone
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; WorldGenerator.instance = null; }
    }

    [Fact]
    public void WetTerminus_RerouteKeepsTheLegOutOfTheLocationInterior()
    {
        // One radius from the wet point is the cap; a terminus that far round
        // the circle still keeps its leg outside ~0.85 r of the center.
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Reroute;
            foreach (float radius in new[] { 12f, 20f, 40f })
            foreach (float half in new[] { 5f, 15f, 30f, 45f })
            {
                var world = new WedgeWorld { Radius = radius, HalfAngle = half };
                WorldGenerator.instance = world;
                var path = Trim(ApproachFromTheWest(radius), radius)!;
                if (path.Count <= 5) continue; // fell back to Trim (asserted elsewhere)
                for (int s = 3; s < path.Count - 1; s++)
                for (int i = 0; i <= 20; i++)
                {
                    Vector2 p = Vector2.Lerp(path[s], path[s + 1], i / 20f);
                    Assert.True(p.magnitude >= radius - 0.5f, $"r {radius} half {half}: leg point {p} cuts {radius - p.magnitude:F1} m into the location");
                }
            }
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; WorldGenerator.instance = null; }
    }

    // ---------------------------------------------------------------
    // End to end: a real pathfinder route into a location whose circle
    // edge is wet. The NAS fixture has no such site (2 Sep: 94 of 94
    // routes identical before and after the Reroute rework), so a gate
    // there cannot exercise Reroute; this scenario pins it deterministically.
    // ---------------------------------------------------------------

    /// <summary>Flat 33 plateau. A thin wet band (height 29) hugs the
    /// location's circle at the origin, one metre either side of the radius,
    /// across ± BandHalfAngle around the western approach. Thinner than the
    /// pathfinder's interior sampling, so a straight-in path steps over it
    /// on cell centres and leaves its interpolated circle point in the
    /// water — the real shape of a wet terminus.</summary>
    private sealed class WetRimWorld : WorldGenerator
    {
        public float Radius = 22f;
        public float BandHalfAngle = 40f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 400f || Mathf.Abs(wy) > 400f) return 20f;
            float d = Mathf.Sqrt(wx * wx + wy * wy);
            if (d < Radius - 1f || d > Radius + 1f) return 33f;
            return OffsetFromWest(wx, wy) < BandHalfAngle ? 29f : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
    }

    private static void ResetGenerator(WorldGenerator world)
    {
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new RoadPathfinder(world));
    }

    private static void TearDownGenerator()
    {
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        RoadSpatialGrid.Clear();
        WorldGenerator.instance = null;
    }

    [Fact]
    public void WetTerminus_EndToEnd_ARealRouteReachesTheDryArcAndValidatesClean()
    {
        var world = new WetRimWorld();
        float radius = world.Radius;
        Vector2 start = new(-200f, 0f), center = new(0f, 0f);
        var saved = RoadNetworkGenerator.WetTerminus;
        try
        {
            // Precondition, asserted so the scenario cannot silently stop
            // being a wet terminus: with Trim the road ends short of the circle.
            ResetGenerator(world);
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Trim;
            Assert.True(RoadNetworkGenerator.GenerateRoad(start, 0f, center, radius, 4f, "West -> WetRim"));
            var trimmed = Assert.Single(RoadNetworkGenerator.GetRoadRoutes());
            Vector3 tEnd = trimmed.Points[trimmed.Points.Count - 1];
            Assert.True(new Vector2(tEnd.x, tEnd.z).magnitude > radius + 1f,
                $"precondition: Trim should end short of the circle (the edge point is wet); ended at {tEnd}");

            // Reroute: the road reaches the location on its dry arc, and the
            // validator (the same instrument as the in-game selftest) finds
            // no wet point anywhere on the built centerline.
            ResetGenerator(world);
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Reroute;
            Assert.True(RoadNetworkGenerator.GenerateRoad(start, 0f, center, radius, 4f, "West -> WetRim"));
            var rerouted = Assert.Single(RoadNetworkGenerator.GetRoadRoutes());
            Vector3 end = rerouted.Points[rerouted.Points.Count - 1];
            Vector2 e = new(end.x, end.z);
            Assert.InRange(e.magnitude, radius - 0.6f, radius + 0.6f);
            Assert.True(world.GetHeight(e.x, e.y) >= Floor, $"terminus {e} is wet");
            Assert.True(OffsetFromWest(e.x, e.y) >= world.BandHalfAngle - 0.5f, $"terminus {e} is inside the wet band");
            foreach (Vector3 p in rerouted.Points)
                Assert.True(new Vector2(p.x, p.z).magnitude >= radius - 0.6f, $"route point {p} is inside the location");
            var report = RoadNetworkValidator.Validate(RoadNetworkGenerator.GetRoadRoutes(), world,
                RoadNetworkGenerator.GetStairRuns(), RoadNetworkGenerator.GetRoadCrossings());
            Assert.True(report.Passed, string.Join("; ", report.Violations));

            // Drop: the location gets no road at all.
            ResetGenerator(world);
            RoadNetworkGenerator.WetTerminus = WetTerminusMode.Drop;
            Assert.False(RoadNetworkGenerator.GenerateRoad(start, 0f, center, radius, 4f, "West -> WetRim"));
            Assert.Empty(RoadNetworkGenerator.GetRoadRoutes());
        }
        finally { RoadNetworkGenerator.WetTerminus = saved; TearDownGenerator(); }
    }

    // ===============================================================
    // 4. Roads/BridgeCostFixed + BridgeCostPerMeter: 0 .. prohibitive,
    //    against detours from short to long
    // ===============================================================

    /// <summary>Wide river ending at RiverEndY; rough ground everywhere but
    /// the level crossing approach. The detour around the river's end grows
    /// with RiverEndY.</summary>
    private sealed class RiverWithAnEndWorld : WorldGenerator
    {
        public float RiverEndY = 600f;
        private static float Hash(int x, int y)
        {
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xFFFF) / 65535f; }
        }
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 220f || wy < -120f || wy > RiverEndY + 100f) return 20f;
            float ax = Mathf.Abs(wx);
            bool approach = ax <= 60f && Mathf.Abs(wy) <= 20f;
            float rough = approach ? 0f
                : (Hash(Mathf.FloorToInt(wx / 6f), Mathf.FloorToInt(wy / 6f)) - 0.5f) * 7f;
            if (wy > RiverEndY) return 33f + rough;
            if (ax <= 35f) return 26f;
            if (ax >= 45f) return 33f + rough;
            return Mathf.Lerp(26f, 33f, (ax - 35f) / 10f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = wy > RiverEndY ? 0f : Mathf.Clamp01(1f - Mathf.Abs(wx) / 80f);
            width = weight > 0f ? 160f : 0f;
        }
    }

    private static bool Bridged(WorldGenerator world, float fixedCost, float perMeter)
    {
        var pf = new RoadPathfinder(world) { BridgeCrossingPenalty = fixedCost, BridgeCostPerMeter = perMeter };
        var path = pf.FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
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

    [Fact]
    public void BridgeCost_FixedSweepFlipsOnceFromBridgeToDetour_DefaultOnTheDetourSideOfAShortDetour()
    {
        var world = new RiverWithAnEndWorld { RiverEndY = 600f };
        float[] fixedCosts = { 0f, 5000f, 10000f, 20000f, 30000f, 50000f, 100000f, 300000f };
        var verdicts = fixedCosts.Select(f => Bridged(world, f, RoadConstants.BridgeCostPerMeter)).ToList();
        string trace = string.Join(", ", fixedCosts.Zip(verdicts, (f, b) => $"{f}:{(b ? "BRIDGE" : "DETOUR")}"));

        Assert.True(verdicts[0], trace);                                // free bridge: taken
        Assert.False(verdicts[verdicts.Count - 1], trace);              // prohibitive: never
        for (int i = 1; i < verdicts.Count; i++)
            Assert.False(!verdicts[i - 1] && verdicts[i], $"non-monotone: {trace}"); // once a detour, always a detour
        Assert.False(Bridged(world, RoadConstants.BridgeCrossingPenalty, RoadConstants.BridgeCostPerMeter), trace);
    }

    [Fact]
    public void BridgeCost_PerMetreSweepIsMonotone()
    {
        var world = new RiverWithAnEndWorld { RiverEndY = 600f };
        float[] perMetre = { 0f, 50f, 100f, 300f, 1000f, 5000f };
        var verdicts = perMetre.Select(m => Bridged(world, 20000f, m)).ToList();
        string trace = string.Join(", ", perMetre.Zip(verdicts, (m, b) => $"{m}:{(b ? "BRIDGE" : "DETOUR")}"));
        Assert.True(verdicts[0], trace);                     // the old flat 20000 bridges here
        Assert.False(verdicts[verdicts.Count - 1], trace);   // 5000/m never
        for (int i = 1; i < verdicts.Count; i++)
            Assert.False(!verdicts[i - 1] && verdicts[i], $"non-monotone: {trace}");
    }

    [Fact]
    public void BridgeCost_DefaultBreakEvenLiesBetweenAShortAndALongDetour()
    {
        // The promise in the config text: ~2 km of rough detour. A river
        // that ends 600 m away is walked around; one that runs on for
        // kilometres is bridged; and the flip happens exactly once.
        int savedIterations = RoadPathfinder.MaxIterations;
        RoadPathfinder.MaxIterations = 400000;
        try
        {
            float[] ends = { 300f, 600f, 1000f, 1400f, 1800f, 2400f };
            var verdicts = new List<bool>();
            foreach (float end in ends)
                verdicts.Add(Bridged(new RiverWithAnEndWorld { RiverEndY = end }, RoadConstants.BridgeCrossingPenalty, RoadConstants.BridgeCostPerMeter));
            string trace = string.Join(", ", ends.Zip(verdicts, (e, b) => $"end{e}:{(b ? "BRIDGE" : "DETOUR")}"));

            Assert.False(verdicts[0], trace);
            Assert.True(verdicts[verdicts.Count - 1], trace);
            for (int i = 1; i < verdicts.Count; i++)
                Assert.False(verdicts[i - 1] && !verdicts[i], $"non-monotone in detour length: {trace}");

            // The old last-resort pair needs a much longer detour before it bridges.
            Assert.False(Bridged(new RiverWithAnEndWorld { RiverEndY = ends[2] }, 50000f, 400f), trace);
        }
        finally { RoadPathfinder.MaxIterations = savedIterations; }
    }
}
