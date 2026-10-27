using System.Collections.Generic;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// A road that joins, shares or runs beside a road already laid takes that road's height
/// where the two would mix (RoadSpatialGrid.HoldToNetwork). Before, the branch kept its own
/// grade into the junction and a shared stretch its own heights: two surfaces on the same
/// ground, 3.1 m apart at points 1.8 m apart, double roads and weird junctions.
/// </summary>
[Collection("RoadStatics")]
public class JunctionHeightTests
{
    private sealed class Hill : WorldGenerator
    {
        public float Gz = 0.12f, Bump, Ridge;
        public override float GetHeight(float x, float z) => 60f + Gz * z
            + Bump * Mathf.Sin(x * 0.11f) * Mathf.Cos(z * 0.07f) + Ridge * (float)System.Math.Exp(-(z / 12f) * (z / 12f));
        public override Heightmap.Biome GetBiome(float x, float z, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float x, float z, out float weight, out float width) { weight = 0f; width = 0f; }
    }

    private static List<Vector2> Line(params float[] xy)
    {
        var w = new List<Vector2>();
        for (int i = 0; i < xy.Length; i += 2) w.Add(new Vector2(xy[i], xy[i + 1]));
        var dense = new List<Vector2> { w[0] };
        for (int i = 1; i < w.Count; i++)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(w[i - 1], w[i]) / 8f));
            for (int k = 1; k <= n; k++) dense.Add(Vector2.Lerp(w[i - 1], w[i], k / (float)n));
        }
        return dense;
    }

    /// <summary>The worst height difference between a branch point and the trunk point nearest it, among branch points on the trunk (within 2 m).</summary>
    private static float WorstOnTrunk(Hill world, List<Vector2> trunk, List<Vector2> branch, bool share = true)
    {
        float worst = 0f;
        PlainRoads.WithModDefaults(() =>
        {
            RoadWiggle.Amplitude = 0f;
            bool before = RoadSpatialGrid.ShareNetworkHeights;
            RoadSpatialGrid.ShareNetworkHeights = share;
            using var worldScope = new ValheimWorldScope().WithWorld(world);
            RoadSpatialGrid.Clear();
            try
            {
                var t = RoadSpatialGrid.PlanRoadPath(trunk, 4f, world);
                Assert.NotNull(t);
                RoadSpatialGrid.Commit(t!);
                var b = RoadSpatialGrid.PlanRoadPath(branch, 4f, world);
                Assert.NotNull(b);
                int on = 0;
                for (int i = 0; i < b!.Points.Count; i++)
                {
                    float best = float.MaxValue, th = 0f;
                    for (int j = 0; j < t!.Points.Count; j++)
                    {
                        float d = Vector2.Distance(b.Points[i], t.Points[j]);
                        if (d < best) { best = d; th = t.Heights[j]; }
                    }
                    if (best >= 2f) continue;
                    on++;
                    worst = Mathf.Max(worst, Mathf.Abs(b.Heights[i] - th));
                }
                Assert.True(on > 0, "the branch never reached the trunk");
            }
            finally
            {
                RoadSpatialGrid.ShareNetworkHeights = before;
                RoadSpatialGrid.Clear();
            }
        });
        return worst;
    }

    [Fact]
    public void ABranchMergingUpASteepTrunkMeetsItLevel()
    {
        var world = new Hill { Gz = 0.3f, Bump = 2f };
        List<Vector2> trunk = Line(0, -100, 0, 100), branch = Line(25, -70, 12, -35, 4, -12, 0, 0);
        Assert.InRange(WorstOnTrunk(world, trunk, branch), 0f, 0.3f);
        // The control: on its own grade into the junction it stood well off the trunk.
        Assert.True(WorstOnTrunk(world, trunk, branch, share: false) > 1f);
    }

    [Fact]
    public void AStretchSharedWithAnotherRoadHasItsHeights()
    {
        Assert.InRange(WorstOnTrunk(new Hill { Bump = 4f }, Line(0, -100, 0, 100), Line(60, -120, 30, -60, 6, -20, 0, 0, 0, 30, 0, 60)), 0f, 0.3f);
        Assert.InRange(WorstOnTrunk(new Hill { Gz = 0.1f, Bump = 6f }, Line(0, -100, 0, 100),
            Line(300, -200, 150, -120, 40, -60, 4, -30, 0, -20, 0, 0, 0, 20, 0, 40, 6, 50, 40, 70)), 0f, 0.3f);
    }

    [Fact]
    public void ABranchOverARidgeTakesTheTrunksCut()
    {
        Assert.InRange(WorstOnTrunk(new Hill { Gz = 0f, Ridge = 14f }, Line(0, -100, 0, 100),
            Line(60, -120, 30, -70, 6, -40, 0, -30, 0, 0, 0, 30, 0, 50)), 0f, 0.3f);
    }
}
