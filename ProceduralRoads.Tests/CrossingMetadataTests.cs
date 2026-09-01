using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for RoadCrossing metadata (PR 3): crossings detected on finished
/// paths with a sailable-fairway profile, and rivers no longer painted —
/// the road grid stops at one bank and resumes at the other.
/// </summary>
public class CrossingMetadataTests
{
    [Fact]
    public void DetectsSingleCrossingWithFairway()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));
        Assert.NotNull(path);

        var crossings = RoadCrossingDetector.Detect(path!, world);

        var crossing = Assert.Single(crossings);

        // Banks are dry ground on opposite sides of the channel.
        Assert.True(world.GetHeight(crossing.FromBank.x, crossing.FromBank.y)
                    >= RoadConstants.ShallowWaterHeight, "FromBank is not dry");
        Assert.True(world.GetHeight(crossing.ToBank.x, crossing.ToBank.y)
                    >= RoadConstants.ShallowWaterHeight, "ToBank is not dry");
        Assert.True(crossing.Width > 8f && crossing.Width < 80f,
            $"Implausible crossing width {crossing.Width:F0}m");

        // The riverbed profile found genuinely deep water, and the fairway
        // (the sailing keep-clear zone) sits in the channel with real width.
        Assert.True(crossing.RiverbedHeight < RoadConstants.DeepWaterHeight,
            $"Riverbed {crossing.RiverbedHeight:F1} not below deep-water threshold");
        Assert.True(crossing.FairwayWidth > 0f, "Expected a sailable fairway");
        world.GetRiverWeight(crossing.FairwayCenter.x, crossing.FairwayCenter.y, out float w, out _);
        Assert.True(w > 0f, "Fairway center is not in the river");

        Assert.Equal(RoadConstants.SeaLevel, crossing.WaterLevel);
        Assert.True(Mathf.Abs(crossing.Direction.magnitude - 1f) < 0.01f, "Direction not normalized");
    }

    [Fact]
    public void NoCrossingsOnDryPath()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-300f, -100f), new Vector2(200f, 150f));
        Assert.NotNull(path);

        Assert.Empty(RoadCrossingDetector.Detect(path!, world));
    }

    [Fact]
    public void GenerateRoadRecordsCrossingAndStopsPaintingAtBanks()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        typeof(RoadNetworkGenerator)
            .GetMethod("Reset", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);
        typeof(RoadNetworkGenerator)
            .GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new RoadPathfinder(world));

        try
        {
            bool ok = RoadNetworkGenerator.GenerateRoad(
                new Vector2(-300f, 0f), 0f, new Vector2(400f, 0f), 0f, 4f, "Cross river");
            Assert.True(ok);

            var crossing = Assert.Single(RoadNetworkGenerator.GetRoadCrossings());

            // The route still spans the river (actors can follow it)...
            var route = Assert.Single(RoadNetworkGenerator.GetRoadRoutes());
            Assert.True(route.Points.Count > 10);

            // ...but no road terrain lands in the river: the fairway center
            // has zero road weight, while both banks have road points nearby.
            RoadSpatialGrid.GetRoadWeight(
                crossing.FairwayCenter.x, crossing.FairwayCenter.y, out float wetWeight, out _);
            Assert.Equal(0f, wetWeight);

            var nearFrom = RoadSpatialGrid.GetRoadPointsNearPosition(
                new Vector3(crossing.FromBank.x, 0, crossing.FromBank.y), 12f);
            var nearTo = RoadSpatialGrid.GetRoadPointsNearPosition(
                new Vector3(crossing.ToBank.x, 0, crossing.ToBank.y), 12f);
            Assert.True(nearFrom.Count > 0, "No road points at FromBank");
            Assert.True(nearTo.Count > 0, "No road points at ToBank");
        }
        finally
        {
            typeof(RoadNetworkGenerator)
                .GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, null);
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null;
        }
    }

    [Fact]
    public void DetectionIsDeterministic()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);
        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));
        Assert.NotNull(path);

        var a = RoadCrossingDetector.Detect(path!, world);
        var b = RoadCrossingDetector.Detect(new List<Vector2>(path!), world);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].FromBank, b[i].FromBank);
            Assert.Equal(a[i].ToBank, b[i].ToBank);
            Assert.Equal(a[i].FairwayCenter, b[i].FairwayCenter);
            Assert.Equal(a[i].FairwayWidth, b[i].FairwayWidth);
        }
    }
}
