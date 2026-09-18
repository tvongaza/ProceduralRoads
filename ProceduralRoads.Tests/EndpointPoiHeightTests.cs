using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Where a road arrives when the place it is going to stands at a height of
/// its own. The ramp used to blend toward the natural terrain under the road's
/// last point, which on a cliff-mouth cave is the cliff face, not the cave.
/// </summary>
public class EndpointPoiHeightTests
{
    private sealed class Flat : WorldGenerator
    {
        public float Height = 60f;
        public override float GetHeight(float wx, float wy) => Height;
        public override Heightmap.Biome GetBiome(float wx, float wy) => Heightmap.Biome.Meadows;
    }

    private static List<Vector2> Path(float toX)
    {
        var path = new List<Vector2>();
        for (float x = 0f; x <= toX; x += 8f) path.Add(new Vector2(x, 0f));
        return path;
    }

    private static float HeightAt(float x)
    {
        var near = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(x, 0f, 0f), 1.0f);
        Assert.True(near.Count > 0, $"no road point at x={x}");
        float best = near[0].h, bestD = Mathf.Abs(near[0].p.x - x);
        foreach (var rp in near)
        {
            float d = Mathf.Abs(rp.p.x - x);
            if (d < bestD) { bestD = d; best = rp.h; }
        }
        return best;
    }

    [Fact]
    public void TheRoadEndsAtTheLocationsGroundNotTheTerrainUnderIt()
    {
        var world = new Flat();
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        try
        {
            // Ground is 60 everywhere; the location stands on 64. Four metres
            // over the forty-metre ramp is 10%, well inside the cap.
            using (GradeCap.At(0.25f))
                Assert.True(RoadSpatialGrid.AddRoadPath(Path(240f), 4f, world, endGround: 64f));

            Assert.Equal(64f, HeightAt(240f), 2);
        }
        finally { RoadSpatialGrid.Clear(); WorldGenerator.instance = null; }
    }

    [Fact]
    public void TheApproachIsGradualRatherThanAStepAtTheDoor()
    {
        var world = new Flat();
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        try
        {
            using (GradeCap.At(0.25f))
                Assert.True(RoadSpatialGrid.AddRoadPath(Path(240f), 4f, world, endGround: 64f));

            // Rising all the way in, never in one jump, and level again well
            // before the ramp's length is up.
            float atEnd = HeightAt(240f), tenIn = HeightAt(230f), thirtyIn = HeightAt(210f);
            Assert.True(atEnd > tenIn && tenIn > thirtyIn,
                $"not monotonic: {thirtyIn:F2} -> {tenIn:F2} -> {atEnd:F2}");
            Assert.True(Mathf.Abs(atEnd - tenIn) < 3f,
                $"{atEnd - tenIn:F2} m of the climb is in the last ten metres");
        }
        finally { RoadSpatialGrid.Clear(); WorldGenerator.instance = null; }
    }

    [Fact]
    public void InlandOfTheRampTheLocationHeightChangesNothing()
    {
        var world = new Flat();
        WorldGenerator.instance = world;
        try
        {
            RoadSpatialGrid.Clear();
            using (GradeCap.At(0.25f))
                Assert.True(RoadSpatialGrid.AddRoadPath(Path(240f), 4f, world, endGround: 64f));
            float withTarget = HeightAt(120f);

            RoadSpatialGrid.Clear();
            using (GradeCap.At(0.25f))
                Assert.True(RoadSpatialGrid.AddRoadPath(Path(240f), 4f, world));
            float without = HeightAt(120f);

            Assert.Equal(without, withTarget, 3);
            Assert.Equal(60f, without, 2);
        }
        finally { RoadSpatialGrid.Clear(); WorldGenerator.instance = null; }
    }

    [Fact]
    public void ALocationTooFarAboveItsApproachIsRefused()
    {
        var world = new Flat();
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        try
        {
            // The frost cave case in miniature: the road ends 200 m from its
            // start and the cave mouth stands 200 m higher. No profile inside
            // the cap joins those, so there is no road - which is the point.
            using (GradeCap.At(0.25f))
                Assert.False(RoadSpatialGrid.AddRoadPath(Path(200f), 4f, world, endGround: 260f));
            Assert.Equal(0, RoadSpatialGrid.TotalRoadPoints);
        }
        finally { RoadSpatialGrid.Clear(); WorldGenerator.instance = null; }
    }

    [Fact]
    public void BothEndsCanCarryTheirOwnLocationHeight()
    {
        var world = new Flat();
        WorldGenerator.instance = world;
        RoadSpatialGrid.Clear();
        try
        {
            using (GradeCap.At(0.25f))
                Assert.True(RoadSpatialGrid.AddRoadPath(Path(400f), 4f, world, startGround: 57f, endGround: 64f));

            Assert.Equal(57f, HeightAt(0f), 2);
            Assert.Equal(64f, HeightAt(400f), 2);
            Assert.Equal(60f, HeightAt(200f), 2);
        }
        finally { RoadSpatialGrid.Clear(); WorldGenerator.instance = null; }
    }

    [Fact]
    public void OnlyAFootprintAboveWaterOffersAHeight()
    {
        var world = new Flat();
        WorldGenerator.instance = world;
        try
        {
            // A real location: its ground is the height to meet.
            Assert.Equal(60f, RoadNetworkGenerator.LocationGround(new Vector2(0f, 0f), 32f)!.Value, 2);
            // An island edge point has no footprint, so the road keeps meeting
            // the natural terrain under its own last point, as before.
            Assert.Null(RoadNetworkGenerator.LocationGround(new Vector2(0f, 0f), 0f));
            // A centre under water is not somewhere a road can end; the
            // endpoint search has already moved the path off it.
            world.Height = 29f;
            Assert.Null(RoadNetworkGenerator.LocationGround(new Vector2(0f, 0f), 32f));
        }
        finally { WorldGenerator.instance = null; }
    }
}
