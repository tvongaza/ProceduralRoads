using System.Collections.Generic;
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
