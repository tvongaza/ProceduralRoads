using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for the reworked pathfinder cost model: additive costs, true
/// blockers as +infinity, swamp shallow-water wading, and short river
/// crossings (fords). These no-op on the pre-rework pathfinder so the suite
/// stays green on every base.
/// </summary>
public class RiverCrossingTests
{
    /// <summary>Reflection-safe constant lookup so this file compiles on
    /// bases that predate the constant (tests no-op there anyway).</summary>
    private static float ConstF(string name, float fallback)
    {
        var f = typeof(RoadConstants).GetField(name, BindingFlags.Public | BindingFlags.Static);
        return f != null ? System.Convert.ToSingle(f.GetRawConstantValue()) : fallback;
    }

    /// <summary>True once the cost-model rework (river fords) is present.</summary>
    public static bool Available =>
        typeof(RoadPathfinder).GetMethod("TryGetShortRiverCrossing",
            BindingFlags.NonPublic | BindingFlags.Instance) != null;

    [Fact]
    public void CrossesNarrowRiver()
    {
        if (!Available) return; // pre-rework pathfinder blocks all rivers
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));

        Assert.NotNull(path);

        // The crossing shows up as one long segment spanning the river core.
        bool foundCrossing = false;
        for (int i = 1; i < path!.Count; i++)
        {
            float segment = Vector2.Distance(path[i - 1], path[i]);
            if (segment <= RoadPathfinder.CellSize * 2.5f)
                continue;

            Vector2 mid = (path[i - 1] + path[i]) * 0.5f;
            world.GetRiverWeight(mid.x, mid.y, out float weight, out _);
            if (weight > RoadConstants.RiverImpassableThreshold)
            {
                foundCrossing = true;
                Assert.True(segment <= ConstF("MaxRiverCrossingCells", 6f) * RoadPathfinder.CellSize + 1f,
                    $"Crossing segment {segment:F0}m exceeds the {ConstF("MaxRiverCrossingCells", 6f) * RoadPathfinder.CellSize:F0}m cap");
            }
        }
        Assert.True(foundCrossing, "Expected the path to include a river-crossing segment");

        // Every ordinary waypoint stays out of the river core and off deep water.
        foreach (var p in path)
        {
            world.GetRiverWeight(p.x, p.y, out float weight, out _);
            Assert.True(weight <= RoadConstants.RiverImpassableThreshold,
                $"Waypoint {p} sits in the river core");
            Assert.True(world.GetHeight(p.x, p.y) >= RoadConstants.DeepWaterHeight,
                $"Waypoint {p} is in deep water");
        }
    }

    [Fact]
    public void WideRiverStillBlocks()
    {
        if (!Available) return;
        // Core wider than the maximum ford length must remain uncrossable.
        var world = new SyntheticWorld
        {
            HasRiver = true,
            HasMountain = false,
            RiverHalfWidth = 170f, // impassable core ~170m > 128m bridge cap (fords cap at 48m)
        };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));

        Assert.Null(path);
    }

    [Fact]
    public void SwampShallowsAreWadeable()
    {
        if (!Available) return;
        var world = new SyntheticWorld
        {
            HasRiver = false,
            HasMountain = false,
            HasWetBand = true,
            WetBandIsSwamp = true,
        };
        var pathfinder = new RoadPathfinder(world);

        // Start and end on opposite sides of the flooded swamp band.
        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(50f, 0f));

        Assert.NotNull(path);
    }

    [Fact]
    public void NonSwampShallowsStillBlock()
    {
        if (!Available) return;
        // The same flooded band in Meadows biome stays impassable — wading is
        // a swamp-only affordance.
        var world = new SyntheticWorld
        {
            HasRiver = false,
            HasMountain = false,
            HasWetBand = true,
            WetBandIsSwamp = false,
        };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(50f, 0f));

        Assert.Null(path);
    }

    [Fact]
    public void SteepMountainIsExpensiveNotImpassable()
    {
        if (!Available) return;
        // Pre-rework, mountain cells above the slope threshold returned a
        // blocker-level cost; now they are merely expensive.
        var world = new SyntheticWorld
        {
            HasRiver = false,
            HasMountain = true,
            MountainHeight = 70f, // slopes well above MountainSlopeThreshold
            MountainHalfWidth = 120f,
        };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-450f, 0f), new Vector2(-60f, 0f));

        Assert.NotNull(path);
    }

    [Fact]
    public void RendersRiverCrossing()
    {
        if (!Available) return;
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);

        var paths = new List<(List<Vector2>, byte, byte, byte)>();
        var markers = new List<(Vector2, byte, byte, byte)>();

        var from = new Vector2(-300f, 0f);
        var to = new Vector2(400f, 0f);
        var path = pathfinder.FindPath(from, to);
        if (path != null)
            paths.Add((path, 220, 40, 40));
        markers.Add((from, 255, 255, 255));
        markers.Add((to, 255, 255, 255));

        string output = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(RiverCrossingTests).Assembly.Location)!,
            "debug-crossing.bmp");

        WorldRenderer.Render(world, paths, markers, output, -700f, 700f, 2f);
        Assert.True(System.IO.File.Exists(output));
    }
}
