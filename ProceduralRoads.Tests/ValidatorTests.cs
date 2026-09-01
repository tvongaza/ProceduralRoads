using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for RoadNetworkValidator — the same checks the in-game
/// road_selftest command runs, exercised here against synthetic networks so
/// the validator itself is trustworthy before it judges real worlds.
/// </summary>
public class ValidatorTests
{
    private static RoadRoute RouteFromPath(SyntheticWorld world, int index, string label, List<Vector2> waypoints) =>
        RoadRoute.FromWaypoints(index, label, 4f, waypoints, world);

    [Fact]
    public void RealFordingPathValidatesCleanly()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);

        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));
        Assert.NotNull(path);

        var route = RouteFromPath(world, 0, "Start -> FarSide", path!);
        var report = RoadNetworkValidator.Validate(new[] { route }, world);

        Assert.True(report.Passed, string.Join("; ", report.Violations));
        Assert.Equal(1, report.RouteCount);
        Assert.True(report.FordCount >= 1, "Expected the validator to count the river ford");
        Assert.Equal(1, report.NetworkComponents);
    }

    [Fact]
    public void RouteThroughOceanIsFlagged()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };

        // A straight line from the island out into open ocean.
        var route = RouteFromPath(world, 0, "Bad -> Ocean", new List<Vector2>
        {
            new(0f, 0f),
            new(0f, 900f),
        });

        var report = RoadNetworkValidator.Validate(new[] { route }, world);

        Assert.False(report.Passed);
        Assert.Contains(report.Violations, v => v.StartsWith("dry-land:"));
    }

    [Fact]
    public void ComponentCountReflectsConnectivity()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };

        var a = RouteFromPath(world, 0, "A", new List<Vector2> { new(-200f, 0f), new(0f, 0f) });
        var b = RouteFromPath(world, 1, "B", new List<Vector2> { new(0f, 0f), new(150f, 100f) });   // touches A's end
        var c = RouteFromPath(world, 2, "C", new List<Vector2> { new(-100f, -300f), new(100f, -300f) }); // isolated

        var report = RoadNetworkValidator.Validate(new[] { a, b, c }, world);

        Assert.Equal(2, report.NetworkComponents);
    }

    [Fact]
    public void ReportSerializesToJsonAndCsv()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        var route = RouteFromPath(world, 0, "Start -> \"Hub\"", new List<Vector2> { new(-100f, 0f), new(100f, 0f) });

        var report = RoadNetworkValidator.Validate(new[] { route }, world);
        string json = RoadNetworkValidator.ToJson(report);
        string csv = RoadNetworkValidator.ToRoutesCsv(new[] { route });

        Assert.Contains("\"passed\": true", json);
        Assert.Contains("\"pointsHash\"", json);
        Assert.StartsWith("route_index,label,point_index,x,y,z", csv);
        Assert.Contains("\\\"Hub\\\"", csv); // labels are escaped
    }

    [Fact]
    public void PointsHashIsDeterministic()
    {
        var world = new SyntheticWorld { HasRiver = true, HasMountain = false };
        var pathfinder = new RoadPathfinder(world);
        var path = pathfinder.FindPath(new Vector2(-300f, 0f), new Vector2(400f, 0f));
        Assert.NotNull(path);

        var r1 = RoadNetworkValidator.Validate(
            new[] { RouteFromPath(world, 0, "R", path!) }, world);
        var r2 = RoadNetworkValidator.Validate(
            new[] { RouteFromPath(world, 0, "R", path!) }, world);

        Assert.Equal(r1.PointsHash, r2.PointsHash);
    }
}
