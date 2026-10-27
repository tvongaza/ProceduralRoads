using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;
namespace ProceduralRoads.Tests;
public class SiteApproachTurnTests
{
    private readonly ITestOutputHelper _out;
    public SiteApproachTurnTests(ITestOutputHelper output) => _out = output;

    private static readonly IReadOnlyList<LevelOp> CaveFloor = new[] { new LevelOp(-0.06f, -4.75f, -4.09f, 4f, false, 7f, 3f) };

    /// <summary>Level ground at 50 before a frost cave facing +x (yaw 90), with its floor levelled as the
    /// game leaves it, and a band across the way in at x 10-26, flat for 4 m in the middle with sides at
    /// 1:2: a ridge (+) or a valley (-) of <paramref name="bump"/> m.</summary>
    private static System.Func<float, float, float> Ground(float bump) => (x, z) =>
    {
        float h = 50f + bump * Mathf.Clamp01(1f - (Mathf.Abs(x - 18f) - 2f) / 6f);
        LevelOp op = CaveFloor[0];
        Vector2 floor = LocationLevelling.OffsetAt(op, 90f);
        float d = Vector2.Distance(new Vector2(x, z), floor);
        float level = 50f + op.Height;
        if (d <= op.Radius) h = level;
        if (d <= op.SmoothRadius) h = Mathf.Lerp(h, level, 1f - Mathf.Pow(d / op.SmoothRadius, op.SmoothPower));
        return h;
    };

    [Fact]
    public void TheHighestOrLowestPointBetweenIsFoundAwayFromTheEnds()
    {
        var ridge = RoadStairRuns.Turn(new Vector2(30f, 0f), new Vector2(6f, 0f), Ground(3f));
        Assert.True(ridge.HasValue && ridge.Value.crest);
        Assert.InRange(ridge!.Value.at.x, 17f, 19f);
        var valley = RoadStairRuns.Turn(new Vector2(30f, 0f), new Vector2(6f, 0f), Ground(-3f));
        Assert.True(valley.HasValue && !valley.Value.crest);
        Assert.InRange(valley!.Value.at.x, 17f, 19f);
        // Under a rise's worth either way: one staircase, or none needed.
        Assert.Null(RoadStairRuns.Turn(new Vector2(30f, 0f), new Vector2(6f, 0f), Ground(0.5f)));
    }

}

public class SiteApproachToppingFloorTests
{

    [Fact]
    public void ApproachStairsStopWhereTheGroundToTheLandingIsWalkable()
    {
        // Road end at x 30, 10 m higher; a steep bank from x 30 to 20, then gentle ground (10%) to the landing at x 0.
        float Ground(float x, float z) => x >= 20f ? 50f + (x - 20f) : 50f + 0.1f * (x - 20f);
        Vector2 flats = RoadStairRuns.FlatsFrom(new Vector2(30f, 0f), new Vector2(0f, 0f), Ground);
        Assert.InRange(flats.x, 19f, 21f);
        // Steep right up to the landing: the landing stays the goal.
        Assert.Equal(new Vector2(0f, 0f), RoadStairRuns.FlatsFrom(new Vector2(30f, 0f), new Vector2(0f, 0f), (x, z) => 50f + 0.6f * x));
        // Gentle all the way: the walk ends at the road (nothing to lay).
        Assert.True(Vector2.Distance(RoadStairRuns.FlatsFrom(new Vector2(30f, 0f), new Vector2(0f, 0f), (x, z) => 50f + 0.2f * x), new Vector2(30f, 0f)) < 1.01f);
    }

    /// <summary>A climb to a shelf whose ground goes on rising past the aim (a knoll, 1 m a metre for 4 m, then
    /// flat): finishing anywhere within reach of the aim, the staircase stopped under the knoll, the ground 2.5 m on
    /// more than a rise and a step-up over its top (seen at 3522,-5773); held
    /// to finish where the ground ahead is within that, it climbs on up.</summary>

}
