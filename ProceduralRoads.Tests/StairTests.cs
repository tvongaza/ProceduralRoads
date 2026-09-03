using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for stair runs: detection of steep sections, the staircase layout
/// solver (terrain-tracking, support-safe, deterministic ruin), and the
/// validator exempting stair-run grades.
/// </summary>
public class StairTests
{
    private static (List<Vector2> path, SyntheticWorld world) SteepSetup()
    {
        // Tall narrow dome: the direct approach forces sustained steep grades.
        var world = new SyntheticWorld
        {
            HasRiver = false,
            HasMountain = true,
            MountainHeight = 80f,
            MountainHalfWidth = 110f,
        };
        var pathfinder = new RoadPathfinder(world);
        var path = pathfinder.FindPath(new Vector2(-450f, 0f), new Vector2(-250f, 20f));
        Assert.NotNull(path);
        return (path!, world);
    }

    [Fact]
    public void DetectsRunsOnSteepPathAndNoneOnFlat()
    {
        var (path, world) = SteepSetup();
        var runs = StairRunDetector.Detect(path, world);
        Assert.NotEmpty(runs);
        foreach (var run in runs)
        {
            Assert.True(run.Length >= StairRunDetector.MinRunLength);
            Assert.True(run.MaxGrade >= StairRunDetector.StairMinGrade);
            Assert.True(run.Points.Count >= 2);
        }

        var flat = new SyntheticWorld { HasRiver = false, HasMountain = false };
        var flatPath = new RoadPathfinder(flat).FindPath(new Vector2(-200f, 0f), new Vector2(100f, 50f));
        Assert.NotNull(flatPath);
        Assert.Empty(StairRunDetector.Detect(flatPath!, flat));
    }

    [Fact]
    public void StaircaseTracksTerrainAndNothingFloats()
    {
        var (path, world) = SteepSetup();
        var run = StairRunDetector.Detect(path, world)[0];
        var pieces = StairLayout.Solve(run, world, 42, StairStyle.MountainStone);
        Assert.NotEmpty(pieces);

        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep).ToList();
        Assert.True(steps.Count >= 2, "Expected multiple steps");

        foreach (var step in steps)
        {
            float ground = world.GetHeight(step.Position.x, step.Position.z);
            float gap = step.Position.y - ground;

            // Steps may clip into cuts but never hover unsupported.
            Assert.True(gap < 6f, $"Step {gap:F1}m above ground — implausible");
            if (gap > StairStyle.MountainStone.MaxUndersideGap + 0.01f)
            {
                bool supported = pieces.Any(p => p.Kind == BridgePieceKind.StairSupport
                    && Mathf.Abs(p.Position.x - step.Position.x) < 0.1f
                    && Mathf.Abs(p.Position.z - step.Position.z) < 0.1f
                    && p.Position.y <= ground + 0.1f);
                Assert.True(supported, $"Floating step at {step.Position.x:F0},{step.Position.z:F0} (gap {gap:F1}m) has no ground support");
            }
        }

        // Consecutive surviving steps never exceed the chain's max grade of
        // one rise per half-run (the stacked steep-stair pattern), so nothing
        // teleports between levels.
        for (int i = 1; i < steps.Count; i++)
        {
            float rise = Mathf.Abs(steps[i].Position.y - steps[i - 1].Position.y);
            float dist = Vector2.Distance(
                new Vector2(steps[i].Position.x, steps[i].Position.z),
                new Vector2(steps[i - 1].Position.x, steps[i - 1].Position.z));
            if (dist < StairStyle.MountainStone.StepRun * 1.5f)
                Assert.True(rise <= dist + 0.01f,
                    $"Step {i} rises {rise:F2}m over {dist:F1}m");
        }
    }

    [Fact]
    public void SolverIsDeterministicAndRuins()
    {
        var (path, world) = SteepSetup();
        var run = StairRunDetector.Detect(path, world)[0];

        var a = StairLayout.Solve(run, world, 7, StairStyle.MeadowsWood);
        var b = StairLayout.Solve(run, world, 7, StairStyle.MeadowsWood);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i].Position, b[i].Position);

        Assert.All(a, p => Assert.True(p.HealthFraction is > 0.2f and < 0.95f));
    }

    [Fact]
    public void GenerateRoadRecordsStairRunsAndSkipsTerrainThere()
    {
        RoadNetworkGenerator.StairsEnabled = true; // stairs are off by default on the bridge branch

        var world = new SyntheticWorld
        {
            HasRiver = false,
            HasMountain = true,
            MountainHeight = 80f,
            MountainHalfWidth = 110f,
        };
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator)
            .GetMethod("Reset", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null);
        typeof(RoadNetworkGenerator)
            .GetField("m_pathfinder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, new RoadPathfinder(world));

        try
        {
            bool ok = RoadNetworkGenerator.GenerateRoad(
                new Vector2(-450f, 0f), 0f, new Vector2(-250f, 20f), 0f, 4f, "Up the mountain");
            Assert.True(ok);

            var runs = RoadNetworkGenerator.GetStairRuns();
            Assert.NotEmpty(runs);

            // No painted road terrain deep inside a stair run (the road
            // correctly paints right up to the run's endpoints, so sample
            // the interior of a run long enough to have one).
            var run = runs.OrderByDescending(r => r.Length).First();
            if (run.Length >= 16f)
            {
                Vector2 mid = run.Points[run.Points.Count / 2];
                RoadSpatialGrid.GetRoadWeight(mid.x, mid.y, out float w, out _);
                Assert.Equal(0f, w);
            }
        }
        finally
        { RoadNetworkGenerator.StairsEnabled = false;
            typeof(RoadNetworkGenerator)
                .GetField("m_pathfinder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, null);
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null;
        }
    }

    [Fact]
    public void ValidatorExemptsStairRunGrades()
    {
        var (path, world) = SteepSetup();
        var runs = StairRunDetector.Detect(path, world);
        Assert.NotEmpty(runs);

        var route = RoadRoute.FromWaypoints(0, "Steep", 4f, path, world);
        foreach (var r in runs) r.RouteIndex = 0;

        var without = RoadNetworkValidator.Validate(new[] { route }, world);
        var with_ = RoadNetworkValidator.Validate(new[] { route }, world, runs);

        int slopeWithout = without.Violations.Count(v => v.StartsWith("slope:"));
        int slopeWith = with_.Violations.Count(v => v.StartsWith("slope:"));
        Assert.True(slopeWith <= slopeWithout,
            $"Exemption increased slope violations ({slopeWithout} -> {slopeWith})");
    }
}

/// <summary>
/// Snap-chained stair grammar: stair pieces mate at their snap edges
/// instead of drifting apart, hard turns happen on landings, support
/// columns are grounded by burial. Stairs are 2m-run/1m-rise with
/// bottom-edge snaps at (±1,0,+1) and top-edge snaps at (±1,1,-1);
/// wood_pole2 is 2m tall.
/// </summary>
public class StairSnapChainTests
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
}
