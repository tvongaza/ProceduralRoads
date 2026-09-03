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
    // The model itself lives in the mod (Src/Roads/SupportModel.cs) so the
    // blueprint weathering pass can use it in-game; the harness asserts with it.
    private static List<int> Floaters(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world) =>
        SupportModel.Floaters(plan, style, world);

    private static string Describe(List<BridgePiece> plan, List<int> floaters, WorldGenerator world) =>
        string.Join("; ", floaters.Take(6).Select(i =>
            $"{plan[i].Kind} {plan[i].Prefab} at ({plan[i].Position.x:F1},{plan[i].Position.y:F1},{plan[i].Position.z:F1}) ground {BiomeBlendedHeight.GetBlendedHeight(plan[i].Position.x, plan[i].Position.z, world):F1}"));

    /// <summary>The plan without the pieces the model cannot support. One
    /// pass is enough: support is a closure from the ground, so removing
    /// what lies outside it changes nothing inside it.</summary>
    internal static List<BridgePiece> DropUnsupported(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world) =>
        SupportModel.DropUnsupported(plan, style, world);

    internal static void AssertGrounded(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world, string site)
    {
        Assert.NotEmpty(plan);
        var floaters = Floaters(plan, style, world);
        Assert.True(floaters.Count == 0, $"{site}: {floaters.Count} of {plan.Count} pieces float: {Describe(plan, floaters, world)}");
    }

    // ---- worlds ----

    /// <summary>Sailable river: flat 4 m deep bed for |x| &lt; 35, banks
    /// rising over 10 m to 32 (+EastRise on the east bank).</summary>
    internal sealed class WideSteppedWorld : WorldGenerator
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

    /// <summary>Narrow channel (bed 26, |x| &lt; 8) between cliffs: the ground
    /// climbs from the water's edge to a 36 plateau at |x| = 16 — Tys's c4/c15,
    /// where the fairway gap ate the whole low deck.</summary>
    private sealed class CliffChannelWorld : WorldGenerator
    {
        public float Plateau = 36f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 100f || Mathf.Abs(wy) > 100f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax <= 8f) return 26f;
            if (ax >= 16f) return Plateau;
            return Mathf.Lerp(26f, Plateau, (ax - 8f) / 8f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 16f);
            width = weight > 0f ? 32f : 0f;
        }
    }

    private static readonly List<Vector2> CliffPath = new()
    {
        new(-40f, 0f), new(-32f, 0f), new(-24f, 0f), new(-16f, 0f), new(16f, 0f), new(24f, 0f), new(32f, 0f), new(40f, 0f),
    };

    [Fact]
    public void HighBridge_SpringsFromTheCliffTopsAndIsGrounded()
    {
        // Night plan 2026-09-03 task 1d: both bank tops within HighBankReach
        // stand >= HighBankRise above the water's edge, so the deck springs
        // from the tops (banks at ±16, height 36), abutments there, piers
        // down to the bed — and every piece is grounded or connected.
        var world = new CliffChannelWorld();
        var crossing = Assert.Single(RoadCrossingDetector.Detect(CliffPath, world));
        Assert.Equal(CrossingKind.Bridge, crossing.Kind);
        Assert.Equal(-16f, crossing.FromBank.x, 1);
        Assert.Equal(16f, crossing.ToBank.x, 1);
        Assert.Equal(3, crossing.FromIndex);
        Assert.Equal(4, crossing.ToIndex);

        foreach (var style in new[] { BridgeStyle.MeadowsWood, BridgeStyle.MountainStone.WithPierPersistence(0.85f) })
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var plan = BridgeLayout.Solve(crossing, world, seed, style);
                AssertGrounded(plan, style, world, $"high bridge {style.PilingPrefab} seed {seed}");
                // The deck runs at plateau height (plus any stepped-end rise), not at the water,
                // and the abutments sit on the tops.
                foreach (var deck in plan.Where(p => p.Kind == BridgePieceKind.Deck))
                    Assert.InRange(deck.Position.y + style.DeckTopOffset, 35.5f, 38f);
                foreach (var abutment in plan.Where(p => p.Kind == BridgePieceKind.Abutment))
                    Assert.InRange(Mathf.Abs(abutment.Position.x), 15.5f, 16.5f);
                if (seed == 1 && style.PilingPrefab == "wood_pole2")
                    SideViewExhibit.Write("high-bridge-side.svg", crossing, plan, world, style, "High bridge: deck springs from the cliff tops (task 1d)");
            }
        }
    }

    [Fact]
    public void GentleBanksKeepTheDeckAtTheWatersEdge()
    {
        // The same channel with banks rising only 1 m within reach: no high
        // bridge, banks stay where the ground can carry road.
        var world = new CliffChannelWorld { Plateau = 32f };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(CliffPath, world));
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        Assert.InRange(world.GetHeight(crossing.FromBank.x, 0f), minBank, minBank + 0.5f);
        Assert.InRange(crossing.Width, 28f, 32f); // shores at |x| = 15, where the 26-32 slope crosses 31.25
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
