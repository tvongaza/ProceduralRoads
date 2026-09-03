using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for the snap-chained bridge grammar (the 2026-09 rework): pier
/// columns are grounded by burial, the deck grades between the banks
/// instead of running level at the higher one, wood stations are post-pair
/// assemblies, stone abutments spring grounded arches and stone piers stack
/// without air gaps. The stair-chain tests live in StairTests.cs.
///
/// Piece dimensions mirror road_snap_probe output: wood_pole2 is 2m tall;
/// stone walls are 1m tall.
/// </summary>
public class SnapChainTests
{

    private sealed class AsymmetricRiverWorld : WorldGenerator
    {
        // Channel along x ∈ [-10, 10]: low bank west (y=31), high bank east (y=36).
        public override float GetHeight(float wx, float wy)
        {
            if (wx < -10f) return 31f;
            if (wx > 10f) return 36f;
            float t = (wx + 10f) / 20f;
            float banks = Mathf.Lerp(31f, 36f, t);
            float dip = 6f * (1f - Mathf.Abs(wx) / 10f);
            return banks - dip;
        }
    }

    private static RoadCrossing MakeCrossing()
    {
        var from = new Vector2(-10f, 0f);
        var to = new Vector2(10f, 0f);
        return new RoadCrossing
        {
            FromBank = from,
            ToBank = to,
            Center = (from + to) * 0.5f,
            Direction = (to - from).normalized,
            Width = 20f,
            WaterLevel = 30f,
            RiverbedHeight = 25f,
            FairwayCenter = new Vector2(0f, 0f),
            FairwayWidth = 4f,
        };
    }

    [Fact]
    public void DeckGradesBetweenBanksInsteadOfStilting()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MeadowsWood;
        var plan = BridgeLayout.Solve(crossing, world, 42, style);

        // A stepped site (task 1c) lifts both ends by the same rise; the
        // graded line is between the lifted ends.
        float rise = BridgeLayout.SteppedEndRise(crossing);
        float bankLow = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y) + rise;
        float bankHigh = world.GetHeight(crossing.ToBank.x, crossing.ToBank.y) + rise;
        var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck).ToList();
        Assert.NotEmpty(decks);

        foreach (var deck in decks)
        {
            float surface = deck.Position.y + style.DeckTopOffset;
            float t = (deck.Position.x - crossing.FromBank.x) / crossing.Width;
            float graded = Mathf.Max(Mathf.Lerp(bankLow, bankHigh, t),
                crossing.WaterLevel + style.DeckFreeboard);
            Assert.True(Mathf.Abs(surface - graded) < 0.35f,
                $"Deck at t={t:F2} surface {surface:F1} vs graded line {graded:F1} — stilted");
        }

        // The old bug: every deck at the higher bank's height. With a 5m bank
        // difference the graded deck line must actually vary.
        float spread = decks.Max(d => d.Position.y) - decks.Min(d => d.Position.y);
        Assert.True(spread > 1f, $"Deck line is flat (spread {spread:F2}m) over asymmetric banks");
    }

    [Fact]
    public void WoodStationsArePostPairsWithBeams()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MeadowsWood;
        var plan = BridgeLayout.Solve(crossing, world, 3, style);

        // Post columns sit off the centerline (paired), never on it.
        var columns = plan.Where(p => p.Kind == BridgePieceKind.Piling)
            .GroupBy(p => new Vector2(p.Position.x, p.Position.z)).ToList();
        Assert.NotEmpty(columns);

        var fullColumns = columns
            .Where(c => c.Count() > 1 || c.Any(p => p.Position.y > crossing.WaterLevel + 0.5f))
            .ToList();
        Assert.NotEmpty(fullColumns);
        foreach (var column in fullColumns)
            Assert.Equal(style.PostSideOffset, Mathf.Abs(column.Key.y), 2);

        // Each full station ties its pair with a crossbeam under the deck.
        var beams = plan.Where(p => p.Kind == BridgePieceKind.Beam).ToList();
        Assert.NotEmpty(beams);
        int pairedStations = fullColumns.Select(c => c.Key.x).Distinct().Count();
        Assert.True(beams.Count >= pairedStations / 2,
            $"{pairedStations} paired stations but only {beams.Count} beams");
    }

    [Fact]
    public void StoneAbutmentsSpringGroundedArches()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MountainStone;
        var plan = BridgeLayout.Solve(crossing, world, 42, style);

        var arches = plan.Where(p => p.Kind == BridgePieceKind.Arch).ToList();
        Assert.NotEmpty(arches); // both banks stand clear of the water here
        Assert.True(arches.Count <= 2, "At most one arch per bank");

        foreach (var arch in arches)
        {
            // Long axis parallel to the crossing direction.
            float yawRad = arch.YawDegrees * Mathf.PI / 180f;
            Vector2 axis = new(Mathf.Cos(yawRad), -Mathf.Sin(yawRad));
            float align = Mathf.Abs(Vector2.Dot(axis, crossing.Direction));
            Assert.True(align > 0.99f, $"Arch axis misaligned (|dot|={align:F3})");

            // The tall face (local +x end) is buried into the bank: its top
            // sits at or below grade at the face position, grounding the piece.
            Vector2 facePos = new Vector2(arch.Position.x, arch.Position.z) + axis * 1f;
            float faceGround = world.GetHeight(facePos.x, facePos.y);
            float archTop = arch.Position.y + 0.5f;
            Assert.True(archTop <= faceGround + 0.01f,
                $"Arch top {archTop:F2} above grade {faceGround:F2} at the bank face — not grounded");

            // And the tapered end reaches inward over lower ground, not into the bank.
            Vector2 tipPos = new Vector2(arch.Position.x, arch.Position.z) - axis * 1f;
            float tipGround = world.GetHeight(tipPos.x, tipPos.y);
            Assert.True(tipGround < faceGround + 0.01f,
                "Arch points into rising ground instead of out over the water");
        }

        // Wood kits have no arch prefab and must emit none.
        var woodPlan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);
        Assert.DoesNotContain(woodPlan, p => p.Kind == BridgePieceKind.Arch);
    }

    [Fact]
    public void StoneStationsStackWallsWithoutAirGaps()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MountainStone;
        var plan = BridgeLayout.Solve(crossing, world, 11, style);

        var columns = plan.Where(p => p.Kind == BridgePieceKind.Piling)
            .GroupBy(p => new Vector2(p.Position.x, p.Position.z)).ToList();
        Assert.NotEmpty(columns);

        foreach (var column in columns)
        {
            float ground = world.GetHeight(column.Key.x, column.Key.y);
            Assert.True(column.Min(p => p.Position.y) <= ground,
                $"Stone pier at {column.Key} not buried");
            var ys = column.Select(p => p.Position.y).OrderBy(y => y).ToList();
            for (int i = 1; i < ys.Count; i++)
                Assert.True(ys[i] - ys[i - 1] <= style.PilingSegment + 0.01f,
                    $"1m walls stacked with an air gap at {column.Key}");
        }
    }
}
