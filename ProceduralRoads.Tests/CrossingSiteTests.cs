using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Crossing-site selection (directive 2026-09-02): bridges across banks of
/// drastically different heights do not work, so the pathfinder must seek
/// crossing points with near-level banks the way a natural road would, and
/// a crossing's recorded banks must stand above the waterline. Live witness:
/// RoadTestMac1 crossings 3 and 6 (banks 29.4 / 29.0 vs water 30.0, bank
/// deltas 4.3 / 5.3 m) — abutments in the stream, decks grading from water
/// level up the far bank.
/// </summary>
public class CrossingSiteTests
{
    /// <summary>
    /// Straight river along x = 0 (core |x| &lt; 5). West bank flat at 32.
    /// East bank level with the west for y &lt;= 0, ramping up to +5 m by
    /// y = 40 and staying there: a mismatched crossing everywhere north, a
    /// level crossing only in the south.
    /// </summary>
    private sealed class SteppedBankRiverWorld : WorldGenerator
    {
        public float EastRise = 5f;
        public float RiseStartY = 0f;
        public float RiseEndY = 40f;

        private float EastBank(float wy) =>
            32f + EastRise * Mathf.Clamp01((wy - RiseStartY) / (RiseEndY - RiseStartY));

        public override float GetHeight(float wx, float wy)
        {
            // Bounded by deep water so the search cannot wander an infinite
            // plain (the crossing penalty dwarfs the heuristic, so A* fills
            // a disc; the ocean rim keeps that disc finite).
            if (Mathf.Abs(wx) > 200f || wy < -80f || wy > 240f) return 20f;
            float bank = wx < 0f ? 32f : EastBank(wy);
            float ax = Mathf.Abs(wx);
            if (ax >= 6f) return bank;
            return Mathf.Lerp(26f, bank, ax / 6f);
        }

        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;

        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 10f);
            width = weight > 0f ? 20f : 0f;
        }
    }

    /// <summary>Finds the ford segment (long jump whose midpoint is river core).</summary>
    private static (Vector2 a, Vector2 b)? FindFord(List<Vector2> path, WorldGenerator world)
    {
        for (int i = 1; i < path.Count; i++)
        {
            if (Vector2.Distance(path[i - 1], path[i]) <= RoadPathfinder.CellSize * 1.5f)
                continue;
            Vector2 mid = (path[i - 1] + path[i]) * 0.5f;
            world.GetRiverWeight(mid.x, mid.y, out float w, out _);
            if (w > RoadConstants.RiverImpassableThreshold)
                return (path[i - 1], path[i]);
        }
        return null;
    }

    [Fact]
    public void FordSeeksLevelBanksInsteadOfCrossingStraightAcrossAStep()
    {
        var world = new SteppedBankRiverWorld();
        var pathfinder = new RoadPathfinder(world);

        // Start and end sit on the mismatched stretch; the straight line
        // crosses at y = 150 with a 5 m bank delta. The level crossing is
        // 150 m south — a detour a natural road takes.
        var path = pathfinder.FindPath(new Vector2(-160f, 150f), new Vector2(160f, 150f));
        Assert.NotNull(path);

        var ford = FindFord(path!, world);
        Assert.True(ford.HasValue, "Path has no river-crossing segment");

        float hA = world.GetHeight(ford!.Value.a.x, ford.Value.a.y);
        float hB = world.GetHeight(ford.Value.b.x, ford.Value.b.y);
        float delta = Mathf.Abs(hA - hB);
        float crossingY = (ford.Value.a.y + ford.Value.b.y) * 0.5f;

        Assert.True(delta <= 1.5f,
            $"Ford chosen with bank delta {delta:F1} m at y={crossingY:F0}; expected the level crossing (south of y=12)");
        Assert.True(crossingY < 12f,
            $"Ford at y={crossingY:F0} instead of the level stretch (y <= 0 is level, +5 m by y=40)");
    }

    [Fact]
    public void FordRejectsBankDeltaAboveTheMaximum()
    {
        // Every crossing is mismatched by 6 m (> MaxFordBankDelta): no ford
        // may be accepted, so the far bank is unreachable — the road should
        // not exist rather than stilt across.
        var world = new SteppedBankRiverWorld { EastRise = 6f, RiseStartY = -10000f, RiseEndY = -9999f };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));

        Assert.Null(path);
    }

    [Fact]
    public void FordStillAcceptedWithinTheMaximumButPaysForTheDelta()
    {
        // Uniform 3 m mismatch (< MaxFordBankDelta): crossable, and the ford
        // cost carries the delta penalty.
        var world = new SteppedBankRiverWorld { EastRise = 3f, RiseStartY = -10000f, RiseEndY = -9999f };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));

        Assert.NotNull(path);
        Assert.True(FindFord(path!, world).HasValue, "Expected a ford across the 3 m step");
        Assert.True(RoadConstants.FordBankDeltaPenalty > 0f);
        Assert.True(RoadConstants.MaxFordBankDelta >= 3f && RoadConstants.MaxFordBankDelta < 6f);
    }

    /// <summary>
    /// River core |x| &lt; 6 at riverbed height; a marshy shelf at 29.4
    /// (below the waterline) out to |x| = 14; dry land at 33 beyond. The
    /// recorded banks must be the first points that stand above the
    /// waterline clearance, not the last points outside the river core.
    /// </summary>
    private sealed class MarshyBankWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            float ax = Mathf.Abs(wx);
            if (ax <= 6f) return 26f;
            if (ax < 14f) return 29.4f;
            return 33f;
        }

        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 12f);
            width = weight > 0f ? 24f : 0f;
        }
    }

    [Fact]
    public void CrossingBanksStandAboveTheWaterline()
    {
        var world = new MarshyBankWorld();
        var path = new List<Vector2>();
        for (float x = -30f; x <= 30f; x += 2f)
            path.Add(new Vector2(x, 0f));

        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));

        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        float fromH = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
        float toH = world.GetHeight(crossing.ToBank.x, crossing.ToBank.y);
        Assert.True(fromH >= minBank, $"FromBank at height {fromH:F1} is below the waterline clearance {minBank:F2}");
        Assert.True(toH >= minBank, $"ToBank at height {toH:F1} is below the waterline clearance {minBank:F2}");

        // Banks are the nearest such points, not somewhere far up the path.
        Assert.Equal(-14f, crossing.FromBank.x, 1);
        Assert.Equal(14f, crossing.ToBank.x, 1);
        Assert.Equal(path[crossing.FromIndex], crossing.FromBank);
        Assert.Equal(path[crossing.ToIndex], crossing.ToBank);
    }
}

/// <summary>
/// Tys's wood-bridge feedback (2026-09-02): decks must span the WATER, not
/// the dry approaches between the path's last dry cells and the shore
/// (c4: 36 m crossing over a 15 m pond; c6: "bridge over land"; c0: deck
/// running into the hillside); the painted road must reach the abutments
/// (c0, c7: bridge to nowhere); knee-deep gullies get a road, not a bridge;
/// and a land route beats a bridge whenever one exists.
/// </summary>
public class CrossingExtentTests
{
    /// <summary>Dry plateau at 33 for |x| ≥ 12, banks sloping to a channel
    /// 26 deep at x = 0; the waterline (30) is crossed at |x| ≈ 6.9.</summary>
    private sealed class ApproachWorld : WorldGenerator
    {
        public float Bed = 26f;
        public override float GetHeight(float wx, float wy)
        {
            float ax = Mathf.Abs(wx);
            if (ax >= 12f) return 33f;
            return Mathf.Lerp(Bed, 33f, ax / 12f);
        }
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 8f); // core |x| < 4
            width = weight > 0f ? 16f : 0f;
        }
    }

    private static List<Vector2> StraightPath(float from, float to, float step)
    {
        var path = new List<Vector2>();
        for (float x = from; x <= to + 0.01f; x += step)
            path.Add(new Vector2(x, 0f));
        return path;
    }

    [Fact]
    public void CrossingSpansOnlyTheWater()
    {
        var world = new ApproachWorld();
        // Path cells 8 m apart: dry points at ±16, the ford jumps between them.
        var path = new List<Vector2> { new(-32f, 0f), new(-24f, 0f), new(-16f, 0f), new(16f, 0f), new(24f, 0f), new(32f, 0f) };

        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));

        // Banks sit at the water's edge — the last point that can legally
        // carry road (waterline + clearance) — not 7 m up the dry approach.
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        float fromH = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
        float toH = world.GetHeight(crossing.ToBank.x, crossing.ToBank.y);
        Assert.InRange(fromH, minBank, minBank + 0.4f);
        Assert.InRange(toH, minBank, minBank + 0.4f);
        Assert.InRange(crossing.Width, 16f, 20f); // shores at |x| ≈ 9, not the dry cells at ±16

        // The path indices still bracket the ford segment (painting resumes there).
        Assert.Equal(2, crossing.FromIndex);
        Assert.Equal(3, crossing.ToIndex);

        // And the solver puts nothing on dry ground beyond the shores.
        var plan = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood);
        Assert.NotEmpty(plan);
        foreach (var piece in plan)
            Assert.True(Mathf.Abs(piece.Position.x) <= crossing.Width * 0.5f + 1.5f,
                $"{piece.Kind} {piece.Prefab} at x={piece.Position.x:F1} is on the dry approach");
    }

    [Fact]
    public void ShallowGullyIsAFordNotACrossing()
    {
        // Riverbed 29.5: knee-deep. A road (leveled ford) goes through; no
        // bridge, no painting exclusion.
        var world = new ApproachWorld { Bed = 29.5f };
        var path = new List<Vector2> { new(-32f, 0f), new(-24f, 0f), new(-16f, 0f), new(16f, 0f), new(24f, 0f), new(32f, 0f) };

        Assert.Empty(RoadCrossingDetector.Detect(path, world));
        Assert.True(RoadConstants.FordWadeDepth > 0f && RoadConstants.FordWadeDepth <= 1.2f,
            "Wadeable depth must stay below the sailable fairway depth (1.2 m)");
    }

    [Fact]
    public void PaintedRoadReachesBothAbutments()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new RoadPathfinder(world));
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-300f, 0f), 0f, new Vector2(400f, 0f), 0f, 4f, "Cross river"));
            var crossing = Assert.Single(RoadNetworkGenerator.GetRoadCrossings());

            // No bridge to nowhere: painted road within 3 m of each abutment
            // (12 m was the old tolerance — a 12 m gap is the hillside at c0).
            var stairRuns = RoadNetworkGenerator.GetStairRuns();
            foreach (Vector2 bank in new[] { crossing.FromBank, crossing.ToBank })
            {
                bool road = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(bank.x, 0, bank.y), 3f).Count > 0;
                bool stairs = false;
                foreach (var run in stairRuns)
                    foreach (var rp in run.Points)
                        if (Vector2.Distance(rp, bank) <= 3f) stairs = true;
                Assert.True(road || stairs, $"Neither painted road nor stairs within 3 m of the abutment at {bank}");
            }

            // Still nothing painted in the channel.
            RoadSpatialGrid.GetRoadWeight(crossing.FairwayCenter.x, crossing.FairwayCenter.y, out float wet, out _);
            Assert.Equal(0f, wet);
        }
        finally
        {
            typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null;
        }
    }

    private sealed class KneeDeepGullyWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Mathf.Abs(wx) < 6f ? 29.4f : 33f;
    }

    [Fact]
    public void FordRoadSurfaceStaysAboveTheWater()
    {
        // A knee-deep gully is painted through as a ford (6f1dc31). The road
        // is leveled to the smoothed road height, so that height — not the
        // raw terrain — is what the ford surface becomes. Live check
        // 2026-09-02: 30.55 and 30.8 at two fords. Pin the guarantee: the
        // ford surface sits at least 0.5 m above the waterline.
        var world = new KneeDeepGullyWorld();
        RoadSpatialGrid.Clear();
        try
        {
            var path = new List<Vector2>();
            for (float x = -40f; x <= 40f; x += 4f) path.Add(new Vector2(x, 0f));
            RoadSpatialGrid.AddRoadPath(path, 4f, world);
            var near = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(0f, 0f, 0f), 4f);
            Assert.NotEmpty(near);
            foreach (var rp in near)
                Assert.True(rp.h >= RoadConstants.SeaLevel + 0.5f,
                    $"Ford surface at {rp.p} is only {rp.h - RoadConstants.SeaLevel:F2} m above the water");
        }
        finally { RoadSpatialGrid.Clear(); }
    }

    /// <summary>River along x = 0 with a dry land bridge (no river, plateau
    /// height) for y ∈ [60, 76]; bounded by deep water.</summary>
    private sealed class GappedRiverWorld : WorldGenerator
    {
        private static bool InGap(float wy) => wy >= 60f && wy <= 76f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || wy < -80f || wy > 200f) return 20f;
            if (InGap(wy)) return 32f;
            float ax = Mathf.Abs(wx);
            return ax >= 6f ? 32f : Mathf.Lerp(26f, 32f, ax / 6f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = InGap(wy) ? 0f : Mathf.Clamp01(1f - Mathf.Abs(wx) / 10f);
            width = weight > 0f ? 20f : 0f;
        }
    }

    [Fact]
    public void LandRouteBeatsBridgeWhenAGapExists()
    {
        // A* question (Tys, c4): the ford costs RiverCrossingPenalty on top of
        // distance, so a dry gap 68 m off the straight line must win.
        var world = new GappedRiverWorld();
        var path = new RoadPathfinder(world).FindPath(new Vector2(-120f, 0f), new Vector2(120f, 0f));
        Assert.NotNull(path);
        for (int i = 1; i < path!.Count; i++)
        {
            Vector2 mid = (path[i - 1] + path[i]) * 0.5f;
            world.GetRiverWeight(mid.x, mid.y, out float w, out _);
            Assert.True(w <= RoadConstants.RiverImpassableThreshold,
                $"Path fords the river at {mid} instead of using the land gap at y=60..76");
        }
    }
}
