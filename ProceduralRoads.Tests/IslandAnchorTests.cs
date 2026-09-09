using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Where an island's network is rooted. Islands are found on a 128 m grid and
/// a cell counts as land on its base height, so the centre of a cell that
/// straddles the shore -- the point the shipped anchor uses -- is often out at
/// sea. A search that starts in the sea settles one cell and stops.
/// </summary>
public class IslandAnchorTests
{
    /// <summary>
    /// One island on the 128 m grid, cells 0..count-1 on both axes with the
    /// world offset placing them over the synthetic world's dome.
    /// </summary>
    private static Island GridIsland(float cellSize = 128f, float worldOffset = 0f, int radiusCells = 5)
    {
        List<Vector2Int> cells = new();
        for (int x = -radiusCells; x <= radiusCells; x++)
            for (int z = -radiusCells; z <= radiusCells; z++)
                cells.Add(new Vector2Int(x, z));

        float extent = radiusCells * cellSize - worldOffset;
        return new Island
        {
            Id = 1,
            Cells = cells,
            CellSize = cellSize,
            WorldOffset = worldOffset,
            Center = new Vector2(0f, 0f),
            Min = new Vector2(-extent, -extent),
            Max = new Vector2(extent, extent),
            CellCount = cells.Count,
        };
    }

    [Fact]
    public void TheShippedEdgePointCanSitBelowTheWaterline()
    {
        // Not a wish: the premise of the fix. The synthetic island's dome
        // reaches sea level well inside 640 m, so the corner of a 5-cell
        // grid at 128 m is open water.
        SyntheticWorld world = new() { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        Island island = GridIsland();

        Vector2 edge = island.GetEdgePoint();
        Assert.True(world.GetHeight(edge.x, edge.y) < RoadConstants.SeaLevel,
            $"edge point ({edge.x},{edge.y}) is at {world.GetHeight(edge.x, edge.y)} m, expected below sea level");
    }

    [Fact]
    public void TheAnchorIsWalkedInlandUntilItIsAboveTheWaterline()
    {
        SyntheticWorld world = new() { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        Island island = GridIsland();

        Vector2 anchor = RoadNetworkGenerator.EdgePointOnLand(island);

        Assert.True(world.GetHeight(anchor.x, anchor.y) >= RoadConstants.SeaLevel,
            $"anchor ({anchor.x},{anchor.y}) is at {world.GetHeight(anchor.x, anchor.y)} m, expected dry ground");
    }

    [Fact]
    public void TheWalkStopsAtTheFirstDryGroundRatherThanTheCentre()
    {
        // Walking all the way in would put every island's road at its middle.
        // The anchor is meant to stay a coast anchor, only a dry one.
        SyntheticWorld world = new() { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        Island island = GridIsland();

        Vector2 edge = island.GetEdgePoint();
        Vector2 anchor = RoadNetworkGenerator.EdgePointOnLand(island);

        // The step before the anchor is still water, so this is the shore and
        // not somewhere further in. That is the whole claim: the walk stops as
        // soon as there is ground, and the anchor stays a coast anchor.
        Vector2 inward = (island.Center - edge).normalized;
        Vector2 before = new Vector2(anchor.x - inward.x * RoadPathfinder.CellSize,
                                     anchor.y - inward.y * RoadPathfinder.CellSize);
        Assert.True(world.GetHeight(before.x, before.y) < RoadConstants.SeaLevel,
            $"the point one cell short of the anchor is already dry ({world.GetHeight(before.x, before.y):F1} m), " +
            "so the walk overshot the shore");
        Assert.NotEqual(island.Center, anchor);
    }

    [Fact]
    public void AnAnchorAlreadyOnLandIsLeftWhereItIs()
    {
        SyntheticWorld world = new() { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        // A tight grid: every cell centre, corners included, is on the dome.
        Island island = GridIsland(cellSize: 64f, radiusCells: 3);

        Vector2 edge = island.GetEdgePoint();
        Assert.True(world.GetHeight(edge.x, edge.y) >= RoadConstants.SeaLevel, "premise: this edge point is dry");
        Assert.Equal(edge, RoadNetworkGenerator.EdgePointOnLand(island));
    }
}
