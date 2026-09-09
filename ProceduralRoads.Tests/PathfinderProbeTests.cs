using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The pathfinder probe (study instrument): it sees the decision points
/// inside the search — what was settled, how near the search came, and why
/// each blocked move stayed blocked — and it changes nothing about what the
/// search decides.
/// </summary>
public class PathfinderProbeTests
{
    /// <summary>A river too deep to ford, with land all around it.</summary>
    private sealed class RiverWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 400f || Mathf.Abs(wy) > 400f) return 20f;
            return Mathf.Abs(wx - 100f) < 30f ? 24f : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx - 100f) / 60f);
            width = weight > 0f ? 120f : 0f;
        }
    }

    private static readonly (Vector2 start, Vector2 end)[] Cases =
    {
        (new Vector2(-300f, -100f), new Vector2(200f, 150f)),   // long dry crossing of an island
        (new Vector2(-40f, 0f), new Vector2(40f, 40f)),         // short hop
        (new Vector2(-200f, 0f), new Vector2(300f, 0f)),        // across the river
        (new Vector2(-200f, 0f), new Vector2(-2000f, 0f)),      // off the island: unreachable
    };

    private static IEnumerable<(WorldGenerator world, bool crossings)> Configurations()
    {
        yield return (new SyntheticWorld { HasRiver = false, HasMountain = false }, false);
        yield return (new SyntheticWorld(), false);
        yield return (new SyntheticWorld(), true);
        yield return (new RiverWorld(), false);
        yield return (new RiverWorld(), true);
    }

    [Fact]
    public void TheProbeIsNullUnlessAStudyRunInstallsIt()
    {
        Assert.Null(RoadPathfinder.Probe);
    }

    [Fact]
    public void InstallingAProbeChangesNoRouteAndNoIterationCount()
    {
        int probedAttempts = 0;
        foreach ((WorldGenerator world, bool crossings) in Configurations())
        {
            foreach ((Vector2 start, Vector2 end) in Cases)
            {
                RoadPathfinder Fresh() =>
                    new RoadPathfinder(world) { Fords = crossings, Bridges = crossings };

                RoadPathfinder bare = Fresh();
                List<Vector2>? without = bare.FindPath(start, end);

                PathfinderTrace trace = new();
                RoadPathfinder watched = Fresh();
                List<Vector2>? with;
                RoadPathfinder.Probe = trace;
                try { with = watched.FindPath(start, end); }
                finally { RoadPathfinder.Probe = null; }

                Assert.Equal(without == null, with == null);
                if (without != null)
                {
                    Assert.Equal(without.Count, with!.Count);
                    for (int i = 0; i < without.Count; i++)
                    {
                        Assert.Equal(without[i].x, with[i].x, 4);
                        Assert.Equal(without[i].y, with[i].y, 4);
                    }
                }

                Assert.Equal(bare.LastIterations, watched.LastIterations);
                Assert.Equal(bare.LastOutcome, watched.LastOutcome);
                Assert.Equal(bare.LastOutcome, trace.Outcome);
                Assert.Equal(bare.LastIterations, trace.Iterations);

                // A pass here means nothing unless the probe actually ran.
                if (trace.UniqueExpandedCells > 0)
                    probedAttempts++;
            }
        }

        Assert.Equal(Configurations().Count() * Cases.Length, probedAttempts);
    }

    [Fact]
    public void ADeepRiverIsReportedAsAFrontierThatStoppedAtItsBank()
    {
        var world = new RiverWorld();
        PathfinderTrace trace = new();
        RoadPathfinder.Probe = trace;
        List<Vector2>? path;
        try
        {
            // Fords on, but this river is 6 m deep and 60 m across: too deep
            // to wade, and bridges are off.
            path = new RoadPathfinder(world) { Fords = true, Bridges = false }
                .FindPath(new Vector2(-200f, 0f), new Vector2(300f, 0f));
        }
        finally { RoadPathfinder.Probe = null; }

        Assert.Null(path);
        Assert.Equal("no reachable path", trace.Outcome);
        Assert.False(trace.Found);

        // The search filled the near shore and stopped: it settled cells, it
        // met blocked moves, and the crossing it looked for was refused
        // because a ford may not have it.
        Assert.True(trace.UniqueExpandedCells > 100, $"only {trace.UniqueExpandedCells} cells settled");
        Assert.True(trace.BlockedMoves > 0);
        Assert.Equal(0, trace.CrossingsAccepted);
        // A ford's scan reaches MaxRiverCrossingCells; this river is wider
        // than that, so the scan runs out of cells before any far bank —
        // which is a different finding from "a bank was found and refused",
        // and the probe distinguishes them.
        Assert.Contains(CrossingRejection.NoBankFound, trace.Rejections.Keys);
        Assert.DoesNotContain(CrossingRejection.BankDelta, trace.Rejections.Keys);

        // It never reached the far bank, and it says how near it came.
        Assert.True(trace.ClosestApproach > 100f,
            $"expected the frontier to stop short of the river, closest {trace.ClosestApproach:F0} m");
        Assert.True(trace.MaxX * RoadPathfinder.CellSize < 100f,
            "no settled cell should lie beyond the near bank");
        Assert.Contains("no reachable path", trace.Summary());
    }

    [Fact]
    public void ABridgedRiverIsReportedAsACrossingTaken()
    {
        var world = new RiverWorld();
        PathfinderTrace trace = new();
        RoadPathfinder.Probe = trace;
        List<Vector2>? path;
        try
        {
            path = new RoadPathfinder(world) { Fords = true, Bridges = true }
                .FindPath(new Vector2(-200f, 0f), new Vector2(300f, 0f));
        }
        finally { RoadPathfinder.Probe = null; }

        Assert.NotNull(path);
        Assert.Equal("found", trace.Outcome);
        // Crossing moves the search took, not bridges built: a crossing edge
        // is offered from many cells along the bank.
        Assert.True(trace.CrossingsAccepted > 0, "the road crossed the river, so a crossing was taken");
        Assert.True(trace.ClosestApproach <= RoadPathfinder.CellSize,
            $"the search reached the destination cell, closest {trace.ClosestApproach:F0} m");
    }

    [Fact]
    public void EveryRejectionCauseNamesTheRuleThatRefusedTheCrossing()
    {
        // A knight move never carries a crossing: the scan walks whole cells.
        var world = new RiverWorld();
        PathfinderTrace trace = new();
        RoadPathfinder.Probe = trace;
        try
        {
            new RoadPathfinder(world) { Fords = true, Bridges = true }
                .FindPath(new Vector2(-200f, 0f), new Vector2(300f, 0f));
        }
        finally { RoadPathfinder.Probe = null; }

        Assert.Contains(CrossingRejection.KnightMove, trace.Rejections.Keys);
        Assert.All(trace.Rejections.Values, count => Assert.True(count > 0));

        // With crossings off the refusal is that, and only that.
        PathfinderTrace off = new();
        RoadPathfinder.Probe = off;
        try
        {
            new RoadPathfinder(world) { Fords = false, Bridges = false }
                .FindPath(new Vector2(-200f, 0f), new Vector2(300f, 0f));
        }
        finally { RoadPathfinder.Probe = null; }

        Assert.Equal(new[] { CrossingRejection.CrossingsDisabled }, off.Rejections.Keys.ToArray());
    }
}
