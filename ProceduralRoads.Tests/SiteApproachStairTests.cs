using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// A staircase from a road's end at a place up to the place's flat ground
/// (a frost cave's apron).
/// </summary>
public class SiteApproachStairTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();
    /// <summary>A place's flat apron at 52 m out to 12 m from the origin, a face of
    /// 1.2 below it down to 40 m at 22 m out, flat ground at 40 m beyond, sea past 200 m.</summary>
    private sealed class ApronWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            float r = new Vector2(wx, wy).magnitude;
            return 40f + Mathf.Clamp((22f - r) * 1.2f, 0f, 12f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    private const float LevelRadius = 12f;
    private const float Footprint = 20f;
    private static readonly IReadOnlyList<LevelOp> Apron = new[] { new LevelOp(0f, 0f, 0f, LevelRadius, false) };
    private readonly ApronWorld _world = new();

    public SiteApproachStairTests()
    {
        _scope.WithWorld(_world);
        RoadNetworkGenerator.Reset();
        RoadSiteProtection.Set(new[] { new RoadSiteProtection.Footprint(new Vector2(0f, 0f), Footprint) });
    }

    public void Dispose()
    {
        SetPathfinder(null);
        LocationLevelling.Source = null;
        RoadSiteProtection.Reset();
        RoadNetworkGenerator.Reset();
        _scope.Dispose();
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private RoadStairSpan? Lay(Vector2 end, IReadOnlyList<LevelOp>? ops, out string why)
    {
        var path = new List<Vector2> { end + new Vector2(-30f, 0f), end };
        return RoadStairRuns.LaySiteApproach(end, _world.GetHeight(end.x, end.y), StairLayout.Bearing(path[0], end),
            new Vector2(0f, 0f), 52f, ops, path, 1, new List<RoadStairSpan>(), _world, out why);
    }

    [Fact]
    public void ARoadEndBelowAPlacesApronGetsAStaircaseUpToIt()
    {
        RoadStairSpan? span = Lay(new Vector2(-24f, 0f), Apron, out string why);

        Assert.True(span != null, why);
        StairPlan plan = span!.Plan;
        Assert.False(plan.Reversed);
        Assert.Equal(new Vector2(-24f, 0f), span.FromPoint);
        // It ends on the apron, at the apron's height, just inside its edge.
        Assert.InRange(plan.End.y, 52f - StairLayout.ReachHeight, 52f + StairLayout.ReachHeight);
        float reached = new Vector2(plan.End.x, plan.End.z).magnitude;
        // Just inside its edge, or on it: arriving within reach of its aim a metre in, a top whose turn's filler
        // would have been buried (StairLayout.WedgeBuried) took another way and ended 5 cm past the edge.
        Assert.InRange(reached, LevelRadius - RoadStairRuns.ApronReach, LevelRadius + 0.1f);
        // Nothing walked far onto the apron.
        foreach (StairPiece piece in plan.Pieces.Where(p => p.Kind != StairPieceKind.Support))
            Assert.True(new Vector2(piece.Position.x, piece.Position.z).magnitude >= LevelRadius - RoadStairRuns.ApronReach,
                $"{piece.Kind} at {piece.Position}");
        Assert.True(plan.MaxCut - StairLayout.CutBelowSurface <= RoadStairRuns.SiteMaxCover, $"cut {plan.MaxCut:F2} m");
    }

    [Fact]
    public void ARoadEndAboveTheApronGetsAStaircaseDownToIt()
    {
        // The same place, sunk into the ground: the road arrives above it.
        var sunk = new[] { new LevelOp(0f, 0f, -12f, LevelRadius, false) };
        var world = new SunkWorld();
        _scope.WithWorld(world);
        var end = new Vector2(-24f, 0f);
        var path = new List<Vector2> { end + new Vector2(-30f, 0f), end };
        RoadStairSpan? span = RoadStairRuns.LaySiteApproach(end, world.GetHeight(end.x, end.y), StairLayout.Bearing(path[0], end),
            new Vector2(0f, 0f), 52f, sunk, path, 1, new List<RoadStairSpan>(), world, out string why);

        Assert.True(span != null, why);
        // Laid from the pit floor up: its top, with the landing the road meets, at the road's end.
        Assert.True(span!.Plan.Reversed);
        Assert.InRange(Vector2.Distance(span.FromPoint, end), 0f, StairLayout.ReachRadius + StairLayout.PieceRun);
        Assert.InRange(span.FromHeight, 52f - StairLayout.ReachHeight, 52f + StairLayout.ReachHeight);
        Assert.Equal(40f, span.ToHeight, 2);
        float bottom = span.Plan.Pieces.Where(p => p.Kind == StairPieceKind.Stair).Min(p => p.Position.y);
        Assert.InRange(bottom, 40f - 2f * StairLayout.PieceRise, 40f);
    }

    /// <summary>52 m flat, falling at 1.2 inside 22 m to a pit floor of 40 m; the place's centre stands at 52.</summary>
    private sealed class SunkWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            float r = new Vector2(wx, wy).magnitude;
            return 52f - Mathf.Clamp((22f - r) * 1.2f, 0f, 12f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    [Fact]
    public void NoStaircaseWithoutAKnownApronOrWhereTheRoadIsAlreadyLevelWithIt()
    {
        Assert.Null(Lay(new Vector2(-24f, 0f), null, out string why));
        Assert.Equal("no levelling known", why);
        // Two levels: no single apron to aim for.
        Assert.Null(Lay(new Vector2(-24f, 0f), new[] { Apron[0], new LevelOp(0f, 0f, 3f, 6f, false) }, out why));
        Assert.Equal("no single flat level", why);
        // An apron at the road's own height.
        Assert.Null(Lay(new Vector2(-24f, 0f), new[] { new LevelOp(0f, 0f, -12f, LevelRadius, false) }, out why));
        Assert.Equal("level enough", why);
        Assert.Null(Lay(new Vector2(-70f, 0f), Apron, out why));
        Assert.Equal("too far", why);
    }

    [Fact]
    public void ItMayCrossItsOwnPlacesFootprintButNoOther()
    {
        // Another place's footprint over the face the staircase must climb.
        RoadSiteProtection.Set(new[]
        {
            new RoadSiteProtection.Footprint(new Vector2(0f, 0f), Footprint),
            new RoadSiteProtection.Footprint(new Vector2(-17f, 0f), 4f),
            new RoadSiteProtection.Footprint(new Vector2(-17f, 6f), 4f),
            new RoadSiteProtection.Footprint(new Vector2(-17f, -6f), 4f),
            new RoadSiteProtection.Footprint(new Vector2(-12f, 12f), 5f),
            new RoadSiteProtection.Footprint(new Vector2(-12f, -12f), 5f),
        });
        Assert.Null(Lay(new Vector2(-24f, 0f), Apron, out string why));
        Assert.StartsWith("another place at (", why);
    }

    [Fact]
    public void PlacedGroundIsLevelledInsideAndBlendedOutToTheSmoothingRadius()
    {
        var ops = new[] { new LevelOp(0f, 0f, 2f, 10f, false, smoothRadius: 20f, smoothPower: 3f) };
        Assert.Equal(52f, LocationLevelling.PlacedGroundAt(new Vector2(5f, 0f), new Vector2(0f, 0f), 50f, ops, 30f));
        // Halfway out the ground has moved 1 - 0.5^3 of the way to the level.
        Assert.Equal(Mathf.Lerp(30f, 52f, 0.875f), LocationLevelling.PlacedGroundAt(new Vector2(0f, 10f), new Vector2(0f, 0f), 50f,
            new[] { new LevelOp(0f, 0f, 2f, 5f, false, 20f, 3f) }, 30f), 3);
        Assert.Equal(30f, LocationLevelling.PlacedGroundAt(new Vector2(25f, 0f), new Vector2(0f, 0f), 50f, ops, 30f));
        // An operation off the centre is left out: where it lies depends on the rotation.
        Assert.Equal(30f, LocationLevelling.PlacedGroundAt(new Vector2(5f, 0f), new Vector2(0f, 0f), 50f, new[] { new LevelOp(4f, 0f, 2f, 10f, false, 20f, 3f) }, 30f));
    }

    [Fact]
    public void AGeneratedRoadToThePlaceClimbsTheLastStretchOnStairs()
    {
        LocationLevelling.Source = c => c.magnitude < 0.5f ? Apron : null;
        SetPathfinder(new RoadPathfinder(_world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-120f, 0f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the apron"));

        StairRun run = Assert.Single(StairStructures.Runs);
        float top = run.Pieces.Where(p => p.Kind != StructurePieceKind.StairSupport).Max(p => p.Position.y);
        Assert.InRange(top, 51f, 52.5f);
        // It starts where the road stops: road points within a couple of metres of its foot, at its height.
        var foot = new Vector3(run.From.x, 0f, run.From.y);
        Assert.Contains(RoadSpatialGrid.GetRoadPointsNearPosition(foot, 2f), p => p.IsRoad && Mathf.Abs(p.h - 40f) < 0.6f);
    }

    [Fact]
    public void WithoutStairsAGeneratedRoadToThePlaceStopsAsBefore()
    {
        LocationLevelling.Source = c => c.magnitude < 0.5f ? Apron : null;
        SetPathfinder(new RoadPathfinder(_world) { Stairs = false, TerrainVarianceThreshold = 1000f, Meander = 0f });

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-120f, 0f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the apron"));

        Assert.Empty(StairStructures.Runs);
    }
    /// <summary>An approach that carries on from the top of the staircase its road ends on
    /// touches that staircase there, as staircases meeting head-on may: given the meeting
    /// point it is not refused "on another staircase" (a frost cave 13 m
    /// above a 38 m staircase's top got none). Without one, it still is.</summary>
    [Fact]
    public void AnApproachCarriesOnFromTheTopOfTheStaircaseItsRoadEndsOn()
    {
        // 100 m east of this class's place at the origin, clear of its footprint.
        float Slope(float x, float z) => 40f + 0.5f * z;
        Vector2 foot = new(100f, 0f);
        StairPlan below = StairLayout.Build(foot, foot + new Vector2(0f, 20f), Slope);
        Assert.True(below.Reached, below.Reason);
        Vector2 top = new(below.End.x, below.End.z);
        var laid = new RoadStairSpan { FromIndex = 0, ToIndex = 1, FromPoint = foot, FromHeight = Slope(foot.x, foot.y), ToPoint = top, ToHeight = below.End.y, Plan = below, Kit = StairKit.Stone };
        // In game the road ended 1.7 m back over the top row (at 3698.8,3293.0): the approach sets off from there.
        Vector2 roadEnd = top - StairLayout.Forward(below.EndHeading) * 1.7f;
        StairPlan on = StairLayout.Build(roadEnd, roadEnd + new Vector2(0f, 16f), Slope, arriving: below.EndHeading);
        Assert.True(on.Reached, on.Reason);
        var road = new List<Vector2> { foot + new Vector2(0f, -40f), foot + new Vector2(0f, -20f), roadEnd };
        var centre = roadEnd + new Vector2(0f, 60f);

        Assert.Equal("on another staircase", RoadStairRuns.SiteConflict(on, centre, road, road.Count - 1, Slope, _world, new[] { laid }));
        Assert.Equal("", RoadStairRuns.SiteConflict(on, centre, road, road.Count - 1, Slope, _world, new[] { laid }, meet: roadEnd));
    }

    /// <summary>The top row of a laid staircase is read back off its pieces: its far edge, the
    /// heading it walks and its walking height, whether it ends on a stair or on a floor carried
    /// on from the top; a road end at its foot is no top.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ALaidStaircasesTopRowIsFoundAtItsEnd(int topLandings)
    {
        float Slope(float x, float z) => 40f + 0.5f * z;
        Vector2 foot = new(100f, 0f);
        StairPlan plan = StairLayout.Build(foot, foot + new Vector2(0f, 20f), Slope, topLandings: topLandings);
        Assert.True(plan.Reached, plan.Reason);
        StairRun run = StairRun.FromPlan(foot, new Vector2(plan.End.x, plan.End.z), RoadStairRuns.Width, plan);
        var end = new Vector2(plan.End.x, plan.End.z);

        StairContinuation? top = RoadStairRuns.TopAt(end, plan.End.y, run.Pieces);

        Assert.True(top.HasValue);
        Assert.True(Vector2.Distance(top!.Value.Edge, end) < 0.05f, $"edge {top.Value.Edge}, end {end}");
        Assert.Equal(plan.End.y, top.Value.Height, 3);
        Assert.Equal(plan.EndHeading, top.Value.Heading, 3);
        Assert.Equal(topLandings > 0 ? StairPieceKind.Landing : StairPieceKind.Stair, top.Value.Kind);
        Assert.Null(RoadStairRuns.TopAt(foot, Slope(foot.x, foot.y), run.Pieces));
    }

}
