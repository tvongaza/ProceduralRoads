using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

public partial class SitesFirstTests
{
    /// <summary>A placed place is reached only by network an approach could climb from, as every other place: road beside
    /// its landing 30 m under it (MaxApproachClimb is 20 m) does not reach it, road 5 m under it does. Without the guard
    /// network far under a landing counted the place reached and its stub was dropped.</summary>
    [Fact]
    public void APlacedPlaceIsNotReachedFromFarUnderItsLanding()
    {
        PlacedSite cave = Cave();
        PlacedSites.Ready(cave);
        Vector2 landing = cave.Shape.Landing;
        float ground = cave.Ground(landing.x, landing.y);
        bool ReachedFrom(float under)
        {
            RoadSpatialGrid.Clear();
            var points = new List<Vector2> { landing + new Vector2(-3f, 4f), landing + new Vector2(0f, 4f), landing + new Vector2(3f, 4f) };
            RoadSpatialGrid.Commit(new RoadSpatialGrid.PlannedPath(points, points.ConvertAll(_ => ground - under), points.ConvertAll(_ => default(RoadPointDebugInfo)), 4f, 6f, false));
            return RoadNetworkGenerator.LandingReached(cave);
        }
        Assert.False(ReachedFrom(30f));
        Assert.True(ReachedFrom(5f));
        RoadSpatialGrid.Clear();
    }
}
