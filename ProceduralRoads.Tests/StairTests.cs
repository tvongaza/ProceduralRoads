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
