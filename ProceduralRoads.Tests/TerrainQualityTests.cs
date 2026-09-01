using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for terrain-quality guarantees added after the first real-world
/// self-test run: roads keep their feet dry between pathfinder samples, and
/// steep faces produce contouring (switchback) paths instead of cliff climbs.
/// </summary>
public class TerrainQualityTests
{
    /// <summary>True once the waterline/grade quality pass exists.</summary>
    public static bool Available =>
        typeof(RoadConstants).GetField("WaterlineClearance", BindingFlags.Public | BindingFlags.Static) != null;

    [Fact]
    public void RouteThroughPuddleFieldStaysDry()
    {
        if (!Available) return;
        // Real-world finding: splined roads dipped below the waterline between
        // dry-sampled 8m cells (12 violations in world RoadTestAuto1).
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false, HasPuddleField = true };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-250f, 0f), new Vector2(150f, 0f));
        Assert.NotNull(path);

        var route = RoadRoute.FromWaypoints(0, "Across puddles", 4f, path!, world);
        var report = RoadNetworkValidator.Validate(new[] { route }, world);

        Assert.True(report.Passed, string.Join("; ", report.Violations));
    }

    [Fact]
    public void SteepFaceProducesContouringNotCliffClimb()
    {
        if (!Available) return;
        // Real-world finding: Bonemass -> Dragonqueen climbed 150-240% grades.
        // Along-path grade at pathfinder scale must stay within the
        // traversable cap, which forces zigzag/contour ascents on steep faces.
        var world = new SyntheticWorld
        {
            HasRiver = false,
            HasMountain = true,
            MountainHeight = 80f,
            MountainHalfWidth = 110f,
        };
        var pathfinder = new RoadPathfinder(world);

        var from = new Vector2(-450f, 0f);
        var to = new Vector2(-60f, 0f);
        var path = pathfinder.FindPath(from, to);
        Assert.NotNull(path);

        float maxGrade = 0f;
        float pathLength = 0f;
        for (int i = 1; i < path!.Count; i++)
        {
            float dist = Vector2.Distance(path[i - 1], path[i]);
            if (dist < 0.5f) continue;
            pathLength += dist;
            float h1 = world.GetHeight(path[i - 1].x, path[i - 1].y);
            float h2 = world.GetHeight(path[i].x, path[i].y);
            maxGrade = Mathf.Max(maxGrade, Mathf.Abs(h2 - h1) / dist);
        }

        Assert.True(maxGrade <= RoadConstants.MaxTraversableGrade + 0.05f,
            $"Max along-path grade {maxGrade:F2} exceeds traversable cap");

        // Contouring means real extra distance versus the straight line.
        float straight = Vector2.Distance(from, to);
        Assert.True(pathLength > straight * 1.1f,
            $"Path {pathLength:F0}m vs straight {straight:F0}m — expected contouring detour");
    }

    [Fact]
    public void GentleTerrainStaysDirect()
    {
        if (!Available) return;
        // The grade shaping must not distort flat-terrain roads.
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);

        var from = new Vector2(-300f, -100f);
        var to = new Vector2(200f, 150f);
        var path = pathfinder.FindPath(from, to);
        Assert.NotNull(path);

        float pathLength = 0f;
        for (int i = 1; i < path!.Count; i++)
            pathLength += Vector2.Distance(path[i - 1], path[i]);

        float straight = Vector2.Distance(from, to);
        Assert.True(pathLength < straight * 1.15f,
            $"Flat-terrain path {pathLength:F0}m vs straight {straight:F0}m — too much wandering");
    }
}
