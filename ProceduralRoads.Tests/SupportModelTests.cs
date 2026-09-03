using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// A support model for ruin plans (Tys, 2 Sep 2026): the ruin state is
/// decided by the solver, never by the game, so nothing may be left for
/// Valheim's support system to knock down on zone load (that would be the
/// player arriving to a crash, a sound, and dropped materials). Every
/// planned piece must therefore be grounded or connected, through touching
/// pieces, to one that is — checked here, before any piece reaches the game.
///
/// The model is deliberately stricter than vanilla: it knows nothing of
/// wood's horizontal reach and demands actual contact, so a plan that
/// passes here stands in the game; the in-game census is the final word.
/// </summary>
public class SupportModelTests
{
    // ---- piece extents: vertical interval relative to Position.y, and a
    // horizontal reach from the origin, per kind and kit ----

    private static (float bottom, float top, float reach) Extent(BridgePiece p, BridgeStyle style)
    {
        float half = style.PilingSegment * 0.5f;
        float deckThickness = style.DeckTopOffset > 0f ? style.DeckTopOffset * 2f : 0.1f; // stone slab vs wood plate
        return p.Kind switch
        {
            BridgePieceKind.Piling => (-half, half, style.PilingAcross ? 1f : 0.25f),
            BridgePieceKind.Beam => (-0.15f, 0.15f, 1f),
            BridgePieceKind.Deck => (style.DeckTopOffset - deckThickness, style.DeckTopOffset, 1f),
            BridgePieceKind.Abutment => (style.DeckTopOffset - deckThickness, style.DeckTopOffset, 1f),
            BridgePieceKind.Stair => (0f, 1f, 1f),      // step: 2 m run, 1 m rise, origin at the foot
            BridgePieceKind.Debris => (-0.5f, 0.5f, 1f),
            BridgePieceKind.Arch => (-0.5f, 0.5f, 1f),
            _ => (-0.5f, 0.5f, 1f),
        };
    }

    private const float GroundTolerance = 0.15f;
    private const float ContactTolerance = 0.3f;

    /// <summary>Indices of pieces the model cannot support: neither buried
    /// nor connected through touching pieces to one that is.</summary>
    private static List<int> Floaters(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world)
    {
        int n = plan.Count;
        var ext = plan.Select(p => Extent(p, style)).ToList();
        var supported = new bool[n];
        var queue = new Queue<int>();

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = plan[i].Position;
            float ground = BiomeBlendedHeight.GetBlendedHeight(pos.x, pos.z, world);
            if (pos.y + ext[i].bottom <= ground + GroundTolerance)
            {
                supported[i] = true;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            for (int b = 0; b < n; b++)
            {
                if (supported[b] || !Touch(plan[a], ext[a], plan[b], ext[b]))
                    continue;
                supported[b] = true;
                queue.Enqueue(b);
            }
        }

        return Enumerable.Range(0, n).Where(i => !supported[i]).ToList();
    }

    /// <summary>Two pieces touch when their vertical intervals overlap (or
    /// meet within tolerance) and their origins lie within reach of each
    /// other horizontally — resting, hanging, side-snapped or interpenetrating
    /// all count, as they do for vanilla colliders.</summary>
    private static bool Touch(BridgePiece a, (float bottom, float top, float reach) ea,
                              BridgePiece b, (float bottom, float top, float reach) eb)
    {
        float dx = a.Position.x - b.Position.x, dz = a.Position.z - b.Position.z;
        if (dx * dx + dz * dz > (ea.reach + eb.reach) * (ea.reach + eb.reach))
            return false;
        float aBottom = a.Position.y + ea.bottom, aTop = a.Position.y + ea.top;
        float bBottom = b.Position.y + eb.bottom, bTop = b.Position.y + eb.top;
        return aBottom <= bTop + ContactTolerance && aTop >= bBottom - ContactTolerance;
    }

    private static string Describe(List<BridgePiece> plan, List<int> floaters, WorldGenerator world) =>
        string.Join("; ", floaters.Take(6).Select(i =>
            $"{plan[i].Kind} {plan[i].Prefab} at ({plan[i].Position.x:F1},{plan[i].Position.y:F1},{plan[i].Position.z:F1}) ground {BiomeBlendedHeight.GetBlendedHeight(plan[i].Position.x, plan[i].Position.z, world):F1}"));

    private static void AssertGrounded(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world, string site)
    {
        Assert.NotEmpty(plan);
        var floaters = Floaters(plan, style, world);
        Assert.True(floaters.Count == 0, $"{site}: {floaters.Count} of {plan.Count} pieces float: {Describe(plan, floaters, world)}");
    }

    // ---- worlds ----

    /// <summary>Sailable river: flat 4 m deep bed for |x| &lt; 35, banks
    /// rising over 10 m to 32 (+EastRise on the east bank).</summary>
    private sealed class WideSteppedWorld : WorldGenerator
    {
        public float EastRise;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 220f || Mathf.Abs(wy) > 120f) return 20f;
            float bank = wx < 0f ? 32f : 32f + EastRise;
            float ax = Mathf.Abs(wx);
            if (ax <= 35f) return 26f;
            if (ax >= 45f) return bank;
            return Mathf.Lerp(26f, bank, (ax - 35f) / 10f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 80f);
            width = weight > 0f ? 160f : 0f;
        }
    }

    private sealed class GullyWorld : WorldGenerator
    {
        public float Bed = 29.5f;
        public float HalfWidth = 8f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 100f || Mathf.Abs(wy) > 100f) return 20f;
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

    private static RoadCrossing Bridge(WorldGenerator world)
    {
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);
        var c = Assert.Single(RoadCrossingDetector.Detect(path!, world));
        Assert.Equal(CrossingKind.Bridge, c.Kind);
        return c;
    }

    // ---- the checks ----

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 0.85f)]
    [InlineData(0f, 1f)]
    [InlineData(1.5f, 0.85f)]   // graded deck: banks differ by 1.5 m
    [InlineData(2.4f, 1f)]      // just inside MaxBridgeBankDelta
    public void WoodBridge_EveryPieceIsGroundedOrConnected_AcrossSeedsAndPersistence(float eastRise, float persistence)
    {
        var world = new WideSteppedWorld { EastRise = eastRise };
        var crossing = Bridge(world);
        var style = BridgeStyle.MeadowsWood.WithPierPersistence(persistence);
        for (int seed = 1; seed <= 25; seed++)
            AssertGrounded(BridgeLayout.Solve(crossing, world, seed, style), style, world, $"wood rise {eastRise} persistence {persistence} seed {seed}");
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 0.85f)]
    [InlineData(1.5f, 0.85f)]
    public void StoneBridge_EveryPieceIsGroundedOrConnected(float eastRise, float persistence)
    {
        var world = new WideSteppedWorld { EastRise = eastRise };
        var crossing = Bridge(world);
        var style = BridgeStyle.MountainStone.WithPierPersistence(persistence);
        for (int seed = 1; seed <= 25; seed++)
            AssertGrounded(BridgeLayout.Solve(crossing, world, seed, style), style, world, $"stone rise {eastRise} persistence {persistence} seed {seed}");
    }

    [Theory]
    [InlineData(29.5f, 6f)]
    [InlineData(29.3f, 8f)]
    [InlineData(29.5f, 12f)]
    public void FordSpan_DeckPostsAndStepsAreGroundedOrConnected(float bed, float halfWidth)
    {
        var world = new GullyWorld { Bed = bed, HalfWidth = halfWidth };
        var path = new List<Vector2> { new(-32f, 0f), new(-24f, 0f), new(-16f, 0f), new(16f, 0f), new(24f, 0f), new(32f, 0f) };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));
        Assert.Equal(CrossingKind.Ford, crossing.Kind);
        crossing.Style = FordStyle.Span;
        foreach (var style in new[] { BridgeStyle.MeadowsWood, BridgeStyle.MountainStone })
            for (int seed = 1; seed <= 15; seed++)
                AssertGrounded(BridgeLayout.Solve(crossing, world, seed, style), style, world, $"span bed {bed} half {halfWidth} {style.PilingPrefab} seed {seed}");
    }

    [Fact]
    public void TheModelItselfCatchesAFloater()
    {
        // A deck plate hanging in the air with nothing under it must be reported,
        // or a passing suite means nothing.
        var world = new WideSteppedWorld();
        var crossing = Bridge(world);
        var plan = BridgeLayout.Solve(crossing, world, 3, BridgeStyle.MeadowsWood);
        Assert.Empty(Floaters(plan, BridgeStyle.MeadowsWood, world));
        plan.Add(new BridgePiece { Kind = BridgePieceKind.Deck, Prefab = "wood_floor", Position = new Vector3(0f, 45f, 60f) });
        Assert.Single(Floaters(plan, BridgeStyle.MeadowsWood, world));
    }
}
