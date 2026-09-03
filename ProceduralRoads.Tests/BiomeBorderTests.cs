using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// At biome borders the raw generator height (one biome's height at the
/// point) and the rendered height (bilinear blend of the corner biomes'
/// heights, what Heightmap builds and what the road grid already uses)
/// differ by metres. Live witness 2026-09-02: RoadTestAuto1 at 200000
/// iterations, route Crypt4 -> Crypt4, 103 m of terrain-following road
/// whose rendered height falls to 22 m under a 30 m waterline, invisible
/// to a validator that reads raw heights and built by a pathfinder that
/// reads raw heights. Everything that judges water must use the blend.
/// </summary>
public class BiomeBorderTests
{
    /// <summary>Meadows plateau (33) west of x = 0, ocean floor (20) east,
    /// and a deep lake inside the meadow (22) for |y| &lt; 20 west of x = -14,
    /// leaving a strip along the border as the only way north-south. Raw
    /// height is the point's own biome height (the strip reads 33); the blend
    /// near the border dips to ~28 at x = -6, deeper than knee-deep.</summary>
    private sealed class BorderWorld : WorldGenerator
    {
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            wx < 0f ? Heightmap.Biome.Meadows : Heightmap.Biome.Ocean;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wy) > 300f || wx < -300f || wx > 100f) return 20f;
            if (wx < -14f && wx > -300f && Mathf.Abs(wy) < 20f) return 22f; // lake
            return GetBiome(wx, wy) == Heightmap.Biome.Meadows ? 33f : 20f;
        }
        public override float GetBiomeHeight(Heightmap.Biome biome, float wx, float wy, out Color mask)
        {
            mask = default;
            if (Mathf.Abs(wy) > 300f || wx < -300f || wx > 100f) return 20f;
            if (biome == Heightmap.Biome.Meadows && wx < -14f && Mathf.Abs(wy) < 20f) return 22f;
            return biome == Heightmap.Biome.Meadows ? 33f : 20f;
        }
    }

    [Fact]
    public void BlendedHeightDipsInsideTheMeadowNearTheBorder()
    {
        var world = new BorderWorld();
        Assert.Equal(33f, world.GetHeight(-6f, 0f));
        float blended = BiomeBlendedHeight.GetBlendedHeight(-6f, 0f, world);
        Assert.True(blended < RoadConstants.SeaLevel - RoadConstants.FordWadeDepth,
            $"Expected the rendered ground at x=-6 to be deeper than knee-deep, got {blended:F2}");
        Assert.True(BiomeBlendedHeight.GetBlendedHeight(-40f, 100f, world) > 32f);
    }

    [Fact]
    public void ValidatorSeesRenderedWaterNotRawHeight()
    {
        var world = new BorderWorld();
        var waypoints = new List<Vector2>();
        for (float y = -60f; y <= 60f; y += 4f) waypoints.Add(new Vector2(-6f, y));
        var route = RoadRoute.FromWaypoints(0, "Along the border", 4f, waypoints, world);

        var report = RoadNetworkValidator.Validate(new[] { route }, world, new List<RoadCrossing>());
        Assert.Contains(report.Violations, v => v.StartsWith("dry-land"));
    }

    [Fact]
    public void PathfinderDoesNotSqueezeThroughRenderedWaterAtTheBorder()
    {
        // North to south the only dry-LOOKING way past the lake is the strip
        // along the border, which reads 33 raw and ~28 rendered. A router on
        // raw heights takes it; one on rendered heights finds no path.
        var world = new BorderWorld();
        var path = new RoadPathfinder(world).FindPath(new Vector2(-40f, -120f), new Vector2(-40f, 120f));
        if (path != null)
            foreach (var p in path)
            {
                float blended = BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);
                Assert.True(blended >= RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance - 0.01f,
                    $"Path point {p} sits on rendered ground {blended:F2}, under the waterline clearance");
            }
    }
}
