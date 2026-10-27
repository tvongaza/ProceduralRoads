using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// Staircases on generated roads: the pathfinder buys a step too steep for road as a stair
/// step, and the generator lays the staircase and cuts the road at it.
/// </summary>
public class RoadStairTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();
    /// <summary>Flat ground at 40 m west of x = 0 and at 40 + Rise east of
    /// x = Rise / Grade, joined by a face of <see cref="Grade"/>: too steep
    /// for road even across it (a knight's move along it climbs Grade / 2.24).
    /// Sea beyond |x|, |y| = 200.</summary>
    private sealed class ScarpWorld : WorldGenerator
    {
        public float Grade = 1.2f;
        public float Rise = 24f;
        public Heightmap.Biome Biome = Heightmap.Biome.Mountain;

        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            return 40f + Mathf.Clamp(wx * Grade, 0f, Rise);
        }

        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Biome;
        public override int GetSeed() => 77;
    }

    /// <summary>A ridge (or a gully) along z: a 32 m flat top (or floor), faces
    /// of 1.2 rising 24 m either side, flats beyond, sea past 200 m. A crest
    /// narrower than a pathfinding cell would be stepped over as level ground.</summary>
    private sealed class RidgeWorld : WorldGenerator
    {
        public bool Gully;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            float bump = Mathf.Clamp(24f - (Mathf.Abs(wx) - 16f) * 1.2f, 0f, 24f);
            return Gully ? 64f - bump : 40f + bump;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    private readonly ScarpWorld _world = new();

    public RoadStairTests()
    {
        _scope.WithWorld(_world);
        RoadNetworkGenerator.Reset();
    }

    public void Dispose()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        _scope.Dispose();
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private RoadPathfinder Finder(bool stairs) =>
        new(_world) { Stairs = stairs, TerrainVarianceThreshold = 1000f, Meander = 0f };

    [Fact]
    public void AFaceTooSteepForRoadIsClimbedOnStairSteps()
    {
        Assert.Null(Finder(stairs: false).FindPath(new Vector2(-80f, 0f), new Vector2(100f, 0f)));

        RoadPathfinder finder = Finder(stairs: true);
        List<Vector2>? path = finder.FindPath(new Vector2(-80f, 0f), new Vector2(100f, 0f));
        Assert.NotNull(path);
        Assert.NotEmpty(finder.LastStairSteps);
        foreach (var (from, to) in finder.LastStairSteps)
        {
            float slope = Mathf.Abs(_world.GetHeight(to.x, to.y) - _world.GetHeight(from.x, from.y)) / Vector2.Distance(from, to);
            Assert.InRange(slope, RoadPathfinder.StairMinGrade, RoadPathfinder.StairMaxGrade);
        }
        // Every stair step is a step of the path.
        foreach (var (from, to) in finder.LastStairSteps)
            Assert.Contains(Enumerable.Range(1, path!.Count - 1), i => path[i - 1] == from && path[i] == to);
    }

    /// <summary>A face of 0.6 from 40 m to 64 m: too steep for road straight up, so a road
    /// switches back across it; steep enough for stairs.</summary>
    private sealed class SwitchbackFaceWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            return 40f + Mathf.Clamp(wx * 0.6f, 0f, 24f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    /// <summary>East along z = 0, a last search step in from the north-west, then east up a face of 0.45: the
    /// road as a player sees it runs the staircase's way, though its last 8 m step is 45 degrees off it.</summary>
    private (List<Vector2> path, List<(Vector2, Vector2)> steps) EastWithAJogIn()
    {
        // A face a staircase climbs straight up: steeper than road, gentler than a flight (0.5).
        _world.Grade = 0.45f;
        var path = new List<Vector2>();
        for (int x = -48; x <= -16; x += 8) path.Add(new Vector2(x, 0f));
        path.Add(new Vector2(-8f, 8f));
        for (int x = 0; x <= 32; x += 8) path.Add(new Vector2(x, 0f));
        var steps = new List<(Vector2, Vector2)> { (new Vector2(0f, 0f), new Vector2(8f, 0f)), (new Vector2(8f, 0f), new Vector2(16f, 0f)), (new Vector2(16f, 0f), new Vector2(24f, 0f)) };
        return (path, steps);
    }

    /// <summary>A face too steep for road, then 40 m of slope a road can climb:
    /// the staircase takes the face and the road the slope. Offered wherever it
    /// was cheaper, the staircase carried on up the slope too, and roads lost
    /// their traverses and switchbacks.</summary>
    private sealed class FaceThenSlopeWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            return 40f + Mathf.Clamp(wx * 1.2f, 0f, 24f) + Mathf.Clamp((wx - 20f) * 0.44f, 0f, 17.6f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Meadows;
        public override int GetSeed() => 77;
    }

    [Fact]
    public void StairsOnlyWhereTheRoadCannotClimb()
    {
        var world = new FaceThenSlopeWorld();
        _scope.WithWorld(world);
        var finder = new RoadPathfinder(world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f };

        Assert.NotNull(finder.FindPath(new Vector2(-80f, 0f), new Vector2(120f, 0f)));

        Assert.NotEmpty(finder.LastStairSteps);
        // A staircase starts only where road cannot climb, and carries on only over
        // ground steeper than StairMinGrade: never onto the gentle ground beyond.
        float roadLimit = RoadGrade.Configured + RoadConstants.SearchGradeMargin;
        float Slope((Vector2 from, Vector2 to) s) => Mathf.Abs(world.GetHeight(s.to.x, s.to.y) - world.GetHeight(s.from.x, s.from.y)) / Vector2.Distance(s.from, s.to);
        Assert.True(Slope(finder.LastStairSteps[0]) > roadLimit, $"the staircase starts on {Slope(finder.LastStairSteps[0]):F2}");
        foreach (var step in finder.LastStairSteps)
            Assert.True(Slope(step) >= RoadPathfinder.StairMinGrade, $"a stair step of {Slope(step):F2} at {step.from} -> {step.to}");
    }

    [Fact]
    public void WithoutStairsTheRoadIsNotBuiltAndNothingIsLaid()
    {
        SetPathfinder(Finder(stairs: false));

        Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 0f), 0f, new Vector2(100f, 0f), 0f, 4f, "scarp"));
        Assert.Empty(StairStructures.Runs);
    }

    /// <summary>A road built after a staircase goes round it, never under or through
    /// it.</summary>
    [Fact]
    public void ALaterRoadGoesRoundAStaircase()
    {
        var flat = new ScarpWorld { Grade = 0f, Biome = Heightmap.Biome.Meadows };
        _scope.WithWorld(flat);
        // A staircase standing across the straight line, north to south at x = 0.
        var run = new StairRun { From = new Vector2(0f, -40f), To = new Vector2(0f, 40f), Width = 2f };
        for (float z = -40f; z <= 40f; z += 2f)
            run.Pieces.Add(new StructurePiece { Kind = StructurePieceKind.StairStep, Prefab = "stone_stair", Position = new Vector3(0f, 40f, z) });
        StairStructures.Add(run);
        StairStructures.MarkStanding(run);

        var finder = new RoadPathfinder(flat) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f };
        List<Vector2>? path = finder.FindPath(new Vector2(-60f, 0f), new Vector2(60f, 0f));

        Assert.NotNull(path);
        Assert.DoesNotContain(path!, p => Mathf.Abs(p.x) < 4f && Mathf.Abs(p.y) < 36f);
    }

    /// <summary>Flat at 40 m north of z = 0, rising south at <see cref="Grade"/> to 40 + <see cref="Rise"/>; sea past 200 m.</summary>
    private sealed class BehindWorld : WorldGenerator
    {
        public float Grade = 1.6f, Rise = 12f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            return 40f + Mathf.Clamp(-wy * Grade, 0f, Rise);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    /// <summary>A flat world: a road built north-south, then one east-west across it. The second would cross the first at
    /// grade and is not built (roads that cross at grade are disallowed even if that rejects a POI); one that
    /// ends on the first (a T) is.</summary>
    private sealed class FlatWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f ? 0f : 40f;
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Meadows;
        public override int GetSeed() => 77;
    }

}
