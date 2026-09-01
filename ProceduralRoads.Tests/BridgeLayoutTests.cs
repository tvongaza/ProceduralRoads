using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for the ruined-bridge layout solver: support-safety (nothing
/// floats), the sailing fairway stays clear, ruin is deterministic, and the
/// plan reads as a bridge (abutments, piers, a collapsed span).
/// </summary>
public class BridgeLayoutTests
{
    private static (RoadCrossing crossing, SyntheticWorld world) SolveSetup()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);
        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));
        Assert.NotNull(path);
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path!, world));
        return (crossing, world);
    }

    [Fact]
    public void PlanIsDeterministic()
    {
        var (crossing, world) = SolveSetup();

        var a = BridgeLayout.Solve(crossing, world, 12345, BridgeStyle.MeadowsWood);
        var b = BridgeLayout.Solve(crossing, world, 12345, BridgeStyle.MeadowsWood);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Prefab, b[i].Prefab);
            Assert.Equal(a[i].Position, b[i].Position);
            Assert.Equal(a[i].HealthFraction, b[i].HealthFraction);
        }

        // A different seed ruins differently.
        var c = BridgeLayout.Solve(crossing, world, 99999, BridgeStyle.MeadowsWood);
        Assert.True(c.Count != a.Count ||
            c.Where((p, i) => p.Position != a[i].Position).Any(),
            "Different seeds should produce different ruins");
    }

    [Fact]
    public void FairwayContainsNoPieces()
    {
        var (crossing, world) = SolveSetup();
        Assert.True(crossing.FairwayWidth > 0f, "Test river should be sailable");

        var plan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);
        Assert.NotEmpty(plan);

        Vector2 dir = crossing.Direction;
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - crossing.FromBank, dir);
        float half = crossing.FairwayWidth * 0.5f;

        foreach (var piece in plan)
        {
            if (piece.Kind == BridgePieceKind.Deck)
                continue; // deck is above the water; checked separately below

            Vector2 p2 = new(piece.Position.x, piece.Position.z);
            float along = Vector2.Dot(p2 - crossing.FromBank, dir);
            Assert.True(Mathf.Abs(along - fairwayMid) > half,
                $"{piece.Kind} at along={along:F1} inside fairway [{fairwayMid - half:F1},{fairwayMid + half:F1}]");
        }

        // The deck over the fairway is collapsed: no deck piece spans it.
        foreach (var deck in plan.Where(p => p.Kind == BridgePieceKind.Deck))
        {
            Vector2 p2 = new(deck.Position.x, deck.Position.z);
            float along = Vector2.Dot(p2 - crossing.FromBank, dir);
            Assert.True(Mathf.Abs(along - fairwayMid) > half - 1f,
                $"Deck at along={along:F1} spans the fairway — bridge must be broken over it");
        }
    }

    [Fact]
    public void NothingFloats()
    {
        var (crossing, world) = SolveSetup();
        var plan = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood);

        // Every piling column starts at the ground.
        var columns = plan.Where(p => p.Kind == BridgePieceKind.Piling)
            .GroupBy(p => new Vector2(p.Position.x, p.Position.z));
        foreach (var column in columns)
        {
            float ground = world.GetHeight(column.Key.x, column.Key.y);
            float lowest = column.Min(p => p.Position.y);
            Assert.True(lowest <= ground + 0.1f,
                $"Column at {column.Key} starts {lowest - ground:F1}m above ground");

            // And the column is continuous: no vertical gaps between segments.
            var heights = column.Select(p => p.Position.y).OrderBy(h => h).ToList();
            for (int i = 1; i < heights.Count; i++)
                Assert.True(heights[i] - heights[i - 1] <= BridgeStyle.MeadowsWood.PilingSegment + 0.01f,
                    $"Gap in column at {column.Key}");
        }

        // Every deck piece has surviving pilings under both ends.
        var pilingXZ = plan.Where(p => p.Kind == BridgePieceKind.Piling)
            .Select(p => new Vector2(p.Position.x, p.Position.z)).Distinct().ToList();
        float span = BridgeStyle.MeadowsWood.DeckSpan;
        foreach (var deck in plan.Where(p => p.Kind == BridgePieceKind.Deck))
        {
            Vector2 p2 = new(deck.Position.x, deck.Position.z);
            int nearby = pilingXZ.Count(c => Vector2.Distance(c, p2) <= span * 0.75f);

            // Terrain itself is valid support: a station whose ground reaches
            // deck height carries the deck without a piling column.
            Vector2 dir = crossing.Direction;
            int grounded = 0;
            foreach (float side in new[] { -0.5f, 0.5f })
            {
                Vector2 end = p2 + dir * (span * side);
                if (world.GetHeight(end.x, end.y) >= deck.Position.y - 0.5f)
                    grounded++;
            }

            Assert.True(nearby + grounded >= 2,
                $"Deck at {p2} has {nearby} pier(s) + {grounded} grounded end(s), needs 2");
        }
    }

    [Fact]
    public void PlanReadsAsARuinedBridge()
    {
        var (crossing, world) = SolveSetup();
        var plan = BridgeLayout.Solve(crossing, world, 3, BridgeStyle.MeadowsWood);

        Assert.Equal(2, plan.Count(p => p.Kind == BridgePieceKind.Abutment));
        Assert.True(plan.Count(p => p.Kind == BridgePieceKind.Piling) >= 2, "Expected surviving piers");

        // Ruin means the deck is incomplete: fewer deck pieces than stations.
        int stations = Mathf.FloorToInt(crossing.Width / BridgeStyle.MeadowsWood.DeckSpan);
        int decks = plan.Count(p => p.Kind == BridgePieceKind.Deck);
        Assert.True(decks < stations, $"Deck complete ({decks}/{stations}) — not a ruin");

        // Damage states: every piece carries partial health for WearNTear.
        Assert.All(plan, p => Assert.True(p.HealthFraction is > 0.1f and < 0.95f,
            $"{p.Kind} health {p.HealthFraction:F2} outside ruin range"));

        // Abutments sit sunk below the bank surface (road laps onto them).
        foreach (var ab in plan.Where(p => p.Kind == BridgePieceKind.Abutment))
        {
            float ground = world.GetHeight(ab.Position.x, ab.Position.z);
            Assert.True(ab.Position.y < ground, "Abutment should be sunk into the bank");
        }

        // Debris is tilted, not standing.
        foreach (var d in plan.Where(p => p.Kind == BridgePieceKind.Debris))
            Assert.True(d.PitchDegrees > 30f, "Debris should be toppled");
    }

    [Fact]
    public void RendersBridgeFootprint()
    {
        var (crossing, world) = SolveSetup();
        var plan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);

        var paths = new List<(List<Vector2>, byte, byte, byte)>();
        var markers = new List<(Vector2, byte, byte, byte)>();

        // Fairway band drawn along the crossing line.
        Vector2 dir = crossing.Direction;
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - crossing.FromBank, dir);
        float half = crossing.FairwayWidth * 0.5f;
        paths.Add((new List<Vector2>
        {
            crossing.FromBank + dir * (fairwayMid - half),
            crossing.FromBank + dir * (fairwayMid + half),
        }, 60, 90, 255));

        foreach (var piece in plan)
        {
            Vector2 p2 = new(piece.Position.x, piece.Position.z);
            (byte r, byte g, byte b) c = piece.Kind switch
            {
                BridgePieceKind.Piling => ((byte)230, (byte)180, (byte)60),
                BridgePieceKind.Deck => ((byte)250, (byte)240, (byte)120),
                BridgePieceKind.Abutment => ((byte)255, (byte)255, (byte)255),
                _ => ((byte)200, (byte)80, (byte)80),
            };
            markers.Add((p2, c.r, c.g, c.b));
        }

        string output = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(BridgeLayoutTests).Assembly.Location)!,
            "debug-bridge.bmp");
        WorldRenderer.RenderCentered(world, paths, markers, output,
            crossing.Center, 60f, 0.25f);
        Assert.True(System.IO.File.Exists(output));
    }
}
