using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Night plan 2026-09-03 task 1g: a route generated after another one
/// pays a fraction of the normal cost on cells that already carry road,
/// and on a crossing where a road already crosses, so it merges into the
/// earlier route and shares its bridge instead of building a second one a
/// few cells away (RoadTestMac2 c1/c2 and c0/c3 doubled sites).
/// </summary>
public class RoadReuseTests
{
    private static RoadPathfinder Setup(SyntheticWorld world, float discount)
    {
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        var pathfinder = new RoadPathfinder(world) { RoadReuseDiscount = discount };
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, pathfinder);
        return pathfinder;
    }

    /// <summary>Hub west of the river, two destinations east of it, 240 m
    /// apart along the bank: with the discount the second road follows the
    /// first to its bridge; without it each road bridges on its own line.</summary>
    private static List<RoadCrossing> TwoRoadsFromAHub(float discount)
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        Setup(world, discount);
        try
        {
            Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-200f, 0f), 0f, new Vector2(320f, -120f), 0f, 4f, "Hub -> A"));
            Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-200f, 0f), 0f, new Vector2(320f, 120f), 0f, 4f, "Hub -> B"));
            return new List<RoadCrossing>(RoadNetworkGenerator.GetRoadCrossings());
        }
        finally
        {
            typeof(RoadNetworkGenerator).GetMethod("Reset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null!;
        }
    }

    [Fact]
    public void SecondRoadSharesTheFirstRoadsBridge()
    {
        var crossings = TwoRoadsFromAHub(RoadConstants.DefaultRoadReuseDiscount);
        Assert.Equal(2, crossings.Count); // each route records its own crossing...
        float apart = Vector2.Distance(crossings[0].Center, crossings[1].Center);
        Assert.True(apart <= BridgeLayout.SharedSiteRadius,
            $"crossings {apart:F1} m apart: the second road did not reuse the first bridge");
        Assert.Single(BridgeLayout.DistinctSites(crossings)); // ...but they are one site, one bridge
    }

    [Fact]
    public void WithoutTheDiscountEachRoadBridgesOnItsOwn()
    {
        var crossings = TwoRoadsFromAHub(1f);
        Assert.Equal(2, crossings.Count);
        float apart = Vector2.Distance(crossings[0].Center, crossings[1].Center);
        Assert.True(apart > BridgeLayout.SharedSiteRadius * 3f, $"crossings only {apart:F1} m apart without the discount");
    }
}
