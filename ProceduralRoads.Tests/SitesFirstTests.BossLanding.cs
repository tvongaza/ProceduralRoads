using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

public partial class SitesFirstTests
{
    /// <summary>A placed boss entrance is counted reached as the planner joins it: only once a road or staircase was carried
    /// onto its own landing, not by road passing beside the landing. The count said reached where the planner
    /// went on building a road to it.</summary>
    [Fact]
    public void APlacedBossEntranceIsReachedOnlyOnceARoadIsCarriedOntoItsLanding()
    {
        PlacedSite cave = Cave();
        cave.Record.Prefab = "Mistlands_DvergrBossEntrance1";
        PlacedSites.Ready(cave);
        Vector2 landing = cave.Shape.Landing;
        float ground = cave.Ground(landing.x, landing.y);
        var points = new List<Vector2> { landing + new Vector2(-3f, 4f), landing + new Vector2(0f, 4f), landing + new Vector2(3f, 4f) };
        RoadSpatialGrid.Commit(new RoadSpatialGrid.PlannedPath(points, points.ConvertAll(_ => ground), points.ConvertAll(_ => default(RoadPointDebugInfo)), 4f, 6f, false));

        Assert.True(RoadNetworkGenerator.LandingReached(cave));
        Assert.False(RoadNetworkGenerator.AlreadyServedByNetwork(Vector3.zero, 10f));
        Assert.False(RoadNetworkGenerator.PlaceServed(cave.Record.Prefab, Vector3.zero, 10f));
        PlacedSites.MarkLanded(cave.Centre);
        Assert.True(RoadNetworkGenerator.AlreadyServedByNetwork(Vector3.zero, 10f));
        Assert.True(RoadNetworkGenerator.PlaceServed(cave.Record.Prefab, Vector3.zero, 10f));
        RoadSpatialGrid.Clear();
    }
}
