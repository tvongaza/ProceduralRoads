using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tys (2026-09-02): the 60-100 m rivers a ship sails down. Fords cap at
/// 48 m, so those rivers split the network. A BRIDGE jump crosses them at
/// a much higher penalty (only where no land route exists) and only at
/// near-level banks; the solver keeps piers and deck out of the fairway,
/// so the ruin reads as a bridge collapsed exactly where boats pass.
/// </summary>
public class WideRiverBridgeTests
{
    private static (Vector2 a, Vector2 b)? FindJump(List<Vector2> path, WorldGenerator world)
    {
        for (int i = 1; i < path.Count; i++)
        {
            if (Vector2.Distance(path[i - 1], path[i]) <= RoadPathfinder.CellSize * 1.5f) continue;
            Vector2 mid = (path[i - 1] + path[i]) * 0.5f;
            world.GetRiverWeight(mid.x, mid.y, out float w, out _);
            if (w > RoadConstants.RiverImpassableThreshold) return (path[i - 1], path[i]);
        }
        return null;
    }

    [Fact]
    public void SailableRiverIsBridgedWithTheFairwayLeftOpen()
    {
        var world = new WideSteppedWorld { EastRise = 0f };
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);

        var jump = FindJump(path!, world);
        Assert.True(jump.HasValue, "Expected a bridge jump across the wide river");
        float jumpLength = Vector2.Distance(jump!.Value.a, jump.Value.b);
        Assert.True(jumpLength > RoadConstants.MaxRiverCrossingCells * RoadPathfinder.CellSize,
            $"Jump {jumpLength:F0} m is within the ford cap; this should be a bridge");

        var crossing = Assert.Single(RoadCrossingDetector.Detect(path!, world));
        Assert.InRange(crossing.Width, 70f, 110f); // shores at |x| ≈ 43.75 (h = 31.25); diagonal jumps measure longer
        Assert.True(crossing.FairwayWidth >= 60f, "The whole 4 m deep bed is sailable");

        var plan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);
        Assert.NotEmpty(plan);

        // The navigation gap (not the whole deep bed) is kept clear of piers
        // and has no deck over it: a bridge collapsed exactly where boats pass.
        Vector2 dir = crossing.Direction;
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - crossing.FromBank, dir);
        float gapHalf = BridgeLayout.FairwayGapWidth * 0.5f;
        foreach (var piece in plan)
        {
            Vector2 p2 = new(piece.Position.x, piece.Position.z);
            float along = Vector2.Dot(p2 - crossing.FromBank, dir);
            float reach = piece.Kind == BridgePieceKind.Deck ? 1f : 0f; // deck plates are 2 m long
            Assert.True(Mathf.Abs(along - fairwayMid) - reach >= gapHalf - 0.01f,
                $"{piece.Kind} at along={along:F1} inside the {BridgeLayout.FairwayGapWidth:F0} m navigation gap around {fairwayMid:F1}");
        }

        // Piers march in from BOTH banks: a ruined bridge, not two abutments.
        int west = plan.Count(p => p.Kind == BridgePieceKind.Piling && p.Position.x < 0f);
        int east = plan.Count(p => p.Kind == BridgePieceKind.Piling && p.Position.x > 0f);
        Assert.True(west >= 4 && east >= 4, $"Piers west={west} east={east}");
    }

    [Fact]
    public void BridgeTakesTheShortestPerpendicularJump()
    {
        // Straight banks, endpoints on the same line: the 96 m perpendicular
        // jump is the shortest and must win over a 107 m knight-move jump.
        // (Before: the perpendicular scan hit a non-core cell that was still
        // under water on the gentle bank and gave up, leaving only oblique
        // directions whose sampling happened to land dry.)
        var world = new WideSteppedWorld { EastRise = 0f };
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);
        var jump = FindJump(path!, world);
        Assert.True(jump.HasValue);
        float dy = Mathf.Abs(jump!.Value.a.y - jump.Value.b.y);
        float dx = Mathf.Abs(jump.Value.a.x - jump.Value.b.x);
        Assert.True(dy <= dx * 0.15f, $"Jump is oblique: dx={dx:F0} dy={dy:F0}");
    }

    [Fact]
    public void WideRiverBeyondTheBridgeCapStillBlocks()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false, RiverHalfWidth = 170f }; // core 170 m > 128 m bridge cap
        Assert.Null(new RoadPathfinder(world).FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f)));
    }

    /// <summary>A ship-sailable river: flat 4 m deep bed for |x| &lt; 35,
    /// banks rising over 10 m to a plateau at 32 (+EastRise on the east).
    /// Water (below 30) is ~80 m wide; river core |x| &lt; 40; the first
    /// dry, non-core cells sit at |x| = 48 (96 m jump, beyond any ford).</summary>
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

    [Fact]
    public void BridgeRefusesMismatchedBanksButAcceptsLevelOnes()
    {
        Assert.NotNull(new RoadPathfinder(new WideSteppedWorld { EastRise = 1f })
            .FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f)));
        Assert.Null(new RoadPathfinder(new WideSteppedWorld { EastRise = 3.5f })
            .FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f)));
        Assert.True(RoadConstants.MaxBridgeBankDelta < RoadConstants.MaxFordBankDelta);
        Assert.True(RoadConstants.BridgeCrossingPenalty > RoadConstants.RiverCrossingPenalty);
    }

    [Fact]
    public void RendersWideRiverBridge()
    {
        var world = new WideSteppedWorld { EastRise = 0f };
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));
        var plan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);

        var paths = new List<(List<Vector2>, byte, byte, byte)> { (path, 220, 40, 40) };
        var markers = new List<(Vector2, byte, byte, byte)>();
        Vector2 dir = crossing.Direction;
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - crossing.FromBank, dir);
        float half = BridgeLayout.FairwayGapWidth * 0.5f;
        paths.Add((new List<Vector2> { crossing.FromBank + dir * (fairwayMid - half), crossing.FromBank + dir * (fairwayMid + half) }, 60, 90, 255));
        foreach (var piece in plan)
        {
            (byte r, byte g, byte b) c = piece.Kind switch
            {
                BridgePieceKind.Piling => ((byte)230, (byte)180, (byte)60),
                BridgePieceKind.Deck => ((byte)250, (byte)240, (byte)120),
                BridgePieceKind.Abutment => ((byte)255, (byte)255, (byte)255),
                _ => ((byte)200, (byte)80, (byte)80),
            };
            markers.Add((new Vector2(piece.Position.x, piece.Position.z), c.r, c.g, c.b));
        }
        string output = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(WideRiverBridgeTests).Assembly.Location)!, "debug-wide-bridge.bmp");
        WorldRenderer.RenderCentered(world, paths, markers, output, crossing.Center, 90f, 0.35f);
        Assert.True(System.IO.File.Exists(output));
    }
}
