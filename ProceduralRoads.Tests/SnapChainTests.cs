using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for the snap-chained composition grammar (the 2026-09 rework):
/// stair pieces mate at their snap edges instead of drifting apart, hard
/// turns happen on landings, support and pier columns are grounded by
/// burial, the bridge deck grades between the banks instead of running
/// level at the higher one, and wood stations are post-pair assemblies.
///
/// Piece dimensions mirror road_snap_probe output: stairs are 2m-run/1m-rise
/// with bottom-edge snaps at (±1,0,+1) and top-edge snaps at (±1,1,-1);
/// wood_pole2 is 2m tall; stone walls are 1m tall.
/// </summary>
public class SnapChainTests
{
    /// <summary>Constant-grade ramp climbing toward +y (north).</summary>
    private sealed class RampWorld : WorldGenerator
    {
        public float Grade = 0.5f;
        public override float GetHeight(float wx, float wy) => wy * Grade;
    }

    /// <summary>Ramp with a gully whose walls drop faster than a stair chain
    /// can descend (3.5m over 2.5m against the climb): the chain must span
    /// part of it on support columns.</summary>
    private sealed class GullyWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            float t = Mathf.Abs(wy - 20f);
            return t < 2.5f ? -3.5f * (1f - t / 2.5f) + wy * 0.45f : wy * 0.45f;
        }
    }

    private static StairRun MakeRun(params Vector2[] points)
    {
        var run = new StairRun { FromPos = points[0], ToPos = points[points.Length - 1] };
        run.Points.AddRange(points);
        float len = 0f;
        for (int i = 1; i < points.Length; i++)
            len += Vector2.Distance(points[i - 1], points[i]);
        run.Length = len;
        run.MaxGrade = 0.6f;
        return run;
    }

    [Fact]
    public void StraightChainStepsMateAtSnapEdges()
    {
        var world = new RampWorld { Grade = 0.5f };
        var run = MakeRun(new Vector2(0f, 0f), new Vector2(0f, 40f));
        var pieces = StairLayout.Solve(run, world, 1234, StairStyle.MeadowsWood);

        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep)
            .OrderBy(p => p.Position.z).ToList();
        Assert.True(steps.Count >= 5, $"Expected a long chain, got {steps.Count} steps");

        var style = StairStyle.MeadowsWood;
        for (int i = 1; i < steps.Count; i++)
        {
            var a = steps[i - 1];
            var b = steps[i];
            float dz = b.Position.z - a.Position.z;
            if (dz > style.StepRun + 0.01f)
                continue; // ruin gap — chain broken on purpose

            // On a straight run every joint mates: one run forward, one rise up.
            Assert.Equal(style.StepRun, dz, 2);
            Assert.Equal(style.StepRise, b.Position.y - a.Position.y, 2);
            Assert.Equal(a.YawDegrees, b.YawDegrees, 1);
            Assert.Equal(a.Position.x, b.Position.x, 2);
        }
    }

    [Fact]
    public void SteepTerrainStacksStepsAtHalfRun()
    {
        var world = new RampWorld { Grade = 1.0f };
        var run = MakeRun(new Vector2(0f, 0f), new Vector2(0f, 30f));
        var pieces = StairLayout.Solve(run, world, 7, StairStyle.MountainStone);

        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep)
            .OrderBy(p => p.Position.z).ToList();
        Assert.True(steps.Count >= 5);

        // Grade 1.0 cannot be climbed at 2m/rise; the chain must stack at
        // half-run spacing (the vanilla steep-stair pattern) at least once,
        // and consecutive live steps never rise more than one piece rise.
        var style = StairStyle.MountainStone;
        bool sawStacked = false;
        for (int i = 1; i < steps.Count; i++)
        {
            float dz = steps[i].Position.z - steps[i - 1].Position.z;
            float dy = steps[i].Position.y - steps[i - 1].Position.y;
            if (dz <= style.StepRun * 0.5f + 0.01f)
                sawStacked = true;
            // Ruin gaps make spacing vary; the invariant is the chain's max
            // grade of one rise per half-run (never steeper than stacked).
            if (dz <= style.StepRun + 0.01f)
                Assert.True(dy <= dz + 0.01f, $"Step {i} rises {dy:F2} over {dz:F2}m");
        }
        Assert.True(sawStacked, "Steep ramp never produced stacked steps");
    }

    [Fact]
    public void HardTurnHappensOnALanding()
    {
        var world = new RampWorld { Grade = 0.45f };
        // Switchback: climb north, hairpin, climb north-east.
        var run = MakeRun(
            new Vector2(0f, 0f), new Vector2(0f, 16f),
            new Vector2(6f, 18f), new Vector2(14f, 26f));
        var pieces = StairLayout.Solve(run, world, 99, StairStyle.MeadowsWood);

        Assert.Contains(pieces, p => p.Kind == BridgePieceKind.Landing);

        // Wherever two live steps are adjacent in the chain, their headings
        // differ by at most the per-joint pivot allowance.
        var style = StairStyle.MeadowsWood;
        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep).ToList();
        for (int i = 1; i < steps.Count; i++)
        {
            float dist = Vector2.Distance(
                new Vector2(steps[i].Position.x, steps[i].Position.z),
                new Vector2(steps[i - 1].Position.x, steps[i - 1].Position.z));
            if (dist > style.StepRun + 0.01f)
                continue; // gap or landing between them

            float dyaw = Mathf.Abs(Mathf.DeltaAngle(steps[i - 1].YawDegrees, steps[i].YawDegrees));
            if (dyaw > 90f) dyaw = Mathf.Abs(dyaw - 180f); // descending pieces face backward
            Assert.True(dyaw <= style.TurnPerJointDegrees + 0.5f,
                $"Adjacent steps turn {dyaw:F1}° without a landing");
        }
    }

    [Fact]
    public void SupportColumnsAreGroundedByBurial()
    {
        var world = new GullyWorld();
        var run = MakeRun(new Vector2(0f, 0f), new Vector2(0f, 40f));
        var pieces = StairLayout.Solve(run, world, 5, StairStyle.MeadowsWood);

        var supports = pieces.Where(p => p.Kind == BridgePieceKind.StairSupport).ToList();
        Assert.NotEmpty(supports); // the gully forces supported spans

        foreach (var column in supports.GroupBy(p => new Vector2(p.Position.x, p.Position.z)))
        {
            float ground = world.GetHeight(column.Key.x, column.Key.y);
            Assert.True(column.Min(p => p.Position.y) <= ground,
                $"Support column at {column.Key} does not reach into the ground");

            var ys = column.Select(p => p.Position.y).OrderBy(y => y).ToList();
            for (int i = 1; i < ys.Count; i++)
                Assert.True(ys[i] - ys[i - 1] <= StairStyle.MeadowsWood.SupportSegment + 0.01f,
                    $"Air gap inside support column at {column.Key}");
        }
    }

    // ---- bridge assemblies ----

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

        float bankLow = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
        float bankHigh = world.GetHeight(crossing.ToBank.x, crossing.ToBank.y);
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
