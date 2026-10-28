using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;
using static ProceduralRoads.Tests.StructureEndTests;

namespace ProceduralRoads.Tests;

/// <summary>
/// One owner for "a stretch of road replaced
/// by a structure" (StructureEnds), judged on the road as BUILT. Each case seen on a test world is reproduced on a small valley.
/// </summary>
public sealed class StructureStretchTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();

    public StructureStretchTests()
    {
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
    }

    public void Dispose()
    {
        RoadNetworkGenerator.PieceCommitted = null;
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
        _scope.Dispose();
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, pathfinder);

    private void Use(WorldGenerator world, List<RoadSpatialGrid.PlannedPath> roads)
    {
        _scope.WithWorld(world);
        SetPathfinder(new RoadPathfinder(world) { Stairs = true, Viaducts = true, TerrainVarianceThreshold = 1000f, Meander = 0f });
        RoadNetworkGenerator.PieceCommitted = (plan, _) => roads.Add(plan);
    }

    /// <summary>A road laid before, straight from <paramref name="a"/> to <paramref name="b"/> at the heights
    /// <paramref name="height"/> gives, every 2 m (committed as the network so far).</summary>
    internal static RoadSpatialGrid.PlannedPath LayRoad(Vector2 a, Vector2 b, Func<Vector2, float> height)
    {
        var points = new List<Vector2>();
        var heights = new List<float>();
        int n = Mathf.CeilToInt(Vector2.Distance(a, b) / 2f);
        for (int i = 0; i <= n; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, i / (float)n);
            points.Add(p);
            heights.Add(height(p));
        }
        var plan = new RoadSpatialGrid.PlannedPath(points, heights, new List<RoadPointDebugInfo>(), 4f, Vector2.Distance(a, b), false);
        RoadSpatialGrid.Commit(plan);
        return plan;
    }

    // ---------------------------------------------------------------- the floor

    /// <summary>
    /// A viaduct on a test world (16 m, centre 5288, 4640) with roads part way under it: about 9 m
    /// over its floor, under the 12 m MinViaductPier, and built. The valley here is 14 m deep on the generator's ground (a
    /// 14 m pier: over the floor), but a road laid before runs along its bottom on 4 m of fill, so the finished floor stands
    /// 10 m under the deck. No viaduct stands less than the floor over the finished ground.
    /// </summary>
    [Fact]
    public void NoViaductStandsUnderTheFloorOverTheFinishedGround()
    {
        var world = new RimValley { Depth = 14f };
        var roads = new List<RoadSpatialGrid.PlannedPath>();
        Use(world, roads);
        float floor = world.Top - world.Depth;
        LayRoad(new Vector2(0f, -150f), new Vector2(0f, 150f), _ => floor + 4f);
        // Without the fill the viaduct is over the floor: the case is the fill alone.
        RoadCrossing probe = DvergrViaduct.ValleyCrossing(new Vector2(-world.Rim - 1f, 0f), new Vector2(world.Rim + 1f, 0f), world, out string why)!;
        Assert.True(probe != null, why);
        Assert.True(DvergrViaduct.TallestPier(probe!, world) >= DvergrViaduct.MinViaductPier, "the valley alone is deep enough for a viaduct");

        RoadNetworkGenerator.GenerateRoad(new Vector3(-110f, 0f, 0f), 0f, new Vector3(110f, 0f, 0f), 0f, 4f, "across");
        foreach (RoadCrossing c in RoadNetworkGenerator.GetRoadCrossings().Where(c => c.ValleyJump))
        {
            float tallest = DvergrViaduct.TallestPierOnFinishedGround(c, world, null);
            Assert.True(tallest >= DvergrViaduct.MinViaductPier - 1e-3f,
                $"a viaduct {c.FromBank} -> {c.ToBank} stands {tallest:F1} m over the finished floor, under the {DvergrViaduct.MinViaductPier:F0} m floor");
        }
    }
}
