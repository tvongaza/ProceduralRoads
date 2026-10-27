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

    /// <summary>Cells opened to stairs (the generator opens them round switchback turns too close
    /// to fit) are climbed on stairs where road would switch back; elsewhere the road keeps its
    /// switchbacks (swap to stairs for tight switchbacks, then back to roads).</summary>
    [Fact]
    public void CellsOpenedToStairsAreClimbedOnStairsWhereRoadWouldSwitchBack()
    {
        var world = new SwitchbackFaceWorld();
        _scope.WithWorld(world);
        var finder = new RoadPathfinder(world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f };
        Assert.NotNull(finder.FindPath(new Vector2(-60f, 0f), new Vector2(100f, 0f)));
        Assert.Empty(finder.LastStairSteps);

        finder.OpenToStairs(new[] { new Vector2(20f, 0f) }, 3);
        Assert.NotNull(finder.FindPath(new Vector2(-60f, 0f), new Vector2(100f, 0f)));

        Assert.NotEmpty(finder.LastStairSteps);
        foreach (var (from, to) in finder.LastStairSteps)
            Assert.True(Mathf.Abs(from.x - 20f) <= 4 * RoadPathfinder.CellSize && Mathf.Abs(to.x - 20f) <= 4 * RoadPathfinder.CellSize, $"stair step {from}->{to} outside the opened cells");
        finder.ClearNoStairs();
        Assert.Equal(0, finder.OpenStairCells);
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

    /// <summary>A stretch whose staircase could not be laid is closed to stairs and the road
    /// searched again: the staircase moves off it, stairs still open elsewhere. Closing the whole
    /// face leaves what a search without stairs finds (none here). Cleared, the first route is back.</summary>
    [Fact]
    public void A_stretch_closed_to_stairs_moves_the_staircase_off_it()
    {
        RoadPathfinder finder = Finder(stairs: true);
        Vector2 from = new(-80f, 0f), to = new(100f, 0f);
        List<Vector2> first = finder.FindPath(from, to)!;
        var firstSteps = finder.LastStairSteps.ToList();
        Assert.NotEmpty(firstSteps);

        finder.CloseToStairs(firstSteps.SelectMany(s => new[] { s.from, s.to }), RoadNetworkGenerator.StairStretchReachCells);
        Assert.NotNull(finder.FindPath(from, to));
        Assert.NotEmpty(finder.LastStairSteps);
        float Off(Vector2 p) => firstSteps.Min(s => Mathf.Min(Vector2.Distance(p, s.from), Vector2.Distance(p, s.to)));
        foreach (var (a, b) in finder.LastStairSteps)
            Assert.True(Off(a) > RoadPathfinder.CellSize && Off(b) > RoadPathfinder.CellSize,
                $"stair step {a}->{b} is in a closed cell");

        var face = new List<Vector2>();
        for (float y = -200f; y <= 200f; y += RoadPathfinder.CellSize)
            for (float x = -8f; x <= 32f; x += RoadPathfinder.CellSize) face.Add(new Vector2(x, y));
        finder.CloseToStairs(face, 0);
        Assert.Null(finder.FindPath(from, to));

        finder.ClearNoStairs();
        Assert.Equal(first, finder.FindPath(from, to));
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
    public void TheRoadStopsAtTheStaircaseAndCarriesOnFromItsTop()
    {
        SetPathfinder(Finder(stairs: true));

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 0f), 0f, new Vector2(100f, 0f), 0f, 4f, "scarp"));

        StairRun run = Assert.Single(StairStructures.Runs);
        Assert.All(run.Pieces, p => Assert.StartsWith("stone_", p.Prefab)); // a Mountain staircase
        Assert.Contains(run.Pieces, p => p.Kind == StructurePieceKind.StairStep);
        // Road on the flats both sides, none levelled on the face (cuts are not road).
        Assert.NotEmpty(RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(-40f, 0f, 0f), 12f).Where(p => p.IsRoad));
        Assert.NotEmpty(RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(60f, 0f, 0f), 12f).Where(p => p.IsRoad));
        float faceTop = _world.Rise / _world.Grade;
        foreach (StructurePiece step in run.Pieces.Where(p => p.Kind == StructurePieceKind.StairStep))
            Assert.DoesNotContain(RoadSpatialGrid.GetRoadPointsNearPosition(step.Position, 0.5f), p => p.IsRoad && p.p.x > 2f && p.p.x < faceTop - 2f);
        // No walked piece lies on the road away from the two junctions (the stairs must not go over the road they start from).
        foreach (StructurePiece piece in run.Pieces.Where(p => p.Kind != StructurePieceKind.StairSupport))
        {
            var at = new Vector2(piece.Position.x, piece.Position.z);
            if (Vector2.Distance(at, run.From) <= RoadStairRuns.JunctionReach + 2f || Vector2.Distance(at, run.To) <= RoadStairRuns.JunctionReach + 2f) continue;
            Assert.DoesNotContain(RoadSpatialGrid.GetRoadPointsNearPosition(piece.Position, 1.5f), p => p.IsRoad);
        }
        // The road meets the staircase level at both ends. The foot is the first step's base: the
        // lowest stair is sunk a rise below it, or, for a staircase set beside its road, stands on
        // the road's height itself (BesideItsRoad lays it from the road's height).
        float lowest = run.Pieces.Where(p => p.Kind == StructurePieceKind.StairStep).Min(p => p.Position.y);
        Assert.Contains(RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(run.From.x, 0f, run.From.y), 3f),
            p => p.IsRoad && (Mathf.Abs(p.h - (lowest + StairLayout.PieceRise)) < 0.6f || Mathf.Abs(p.h - lowest) < 0.6f));
    }

    [Fact]
    public void WithoutStairsTheRoadIsNotBuiltAndNothingIsLaid()
    {
        SetPathfinder(Finder(stairs: false));

        Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 0f), 0f, new Vector2(100f, 0f), 0f, 4f, "scarp"));
        Assert.Empty(StairStructures.Runs);
    }

    [Fact]
    public void AStaircaseIsBuiltOfItsBiomesPieces()
    {
        _world.Biome = Heightmap.Biome.Mistlands;
        SetPathfinder(Finder(stairs: true));

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 0f), 0f, new Vector2(100f, 0f), 0f, 4f, "scarp"));

        Assert.All(Assert.Single(StairStructures.Runs).Pieces, p => Assert.StartsWith("blackmarble_", p.Prefab));
    }

    /// <summary>Over a ridge or through a gully the road climbs one staircase and
    /// descends another (stairs can go downhill too): a
    /// layout climbs from its lower end, so one staircase cannot cross a crest,
    /// and joined into one the road was refused.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARidgeOrAGullyIsTwoStaircases(bool gully)
    {
        var world = new RidgeWorld { Gully = gully };
        _scope.WithWorld(world);
        SetPathfinder(new RoadPathfinder(world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 0f), 0f, new Vector2(80f, 0f), 0f, 4f, gully ? "gully" : "ridge"));

        Assert.Equal(2, StairStructures.Runs.Count);
        // One climbs the west face, one the east; close over the top, the second is
        // laid to meet the first, with floors between.
        Assert.Contains(StairStructures.Runs, r => r.Pieces.Any(p => p.Position.x < -20f) && r.Pieces.All(p => p.Position.x < 1f));
        Assert.Contains(StairStructures.Runs, r => r.Pieces.Any(p => p.Position.x > 20f));
        // Their tops meet, not one inside the other.
        var walked0 = StairStructures.Runs[0].Pieces.Where(p => p.Kind != StructurePieceKind.StairSupport).ToList();
        var walked1 = StairStructures.Runs[1].Pieces.Where(p => p.Kind != StructurePieceKind.StairSupport).ToList();
        foreach (var p in walked0)
            Assert.DoesNotContain(walked1, q => Vector2.Distance(new Vector2(p.Position.x, p.Position.z), new Vector2(q.Position.x, q.Position.z)) < RoadStairRuns.StairOverlap
                && Mathf.Abs(p.Position.y - q.Position.y) < 2f);
    }

    /// <summary>A second road over the same face walks the staircase the first
    /// one laid: two laid side by side spawned every piece twice.</summary>
    [Fact]
    public void ASecondRoadOverTheSameFaceWalksTheFirstOnesStaircase()
    {
        SetPathfinder(Finder(stairs: true));

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 0f), 0f, new Vector2(100f, 0f), 0f, 4f, "first"));
        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 6f), 0f, new Vector2(100f, 6f), 0f, 4f, "second"));

        Assert.Single(StairStructures.Runs);
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

    /// <summary>Two staircases with less than 20 m of road between them are one;
    /// further apart they stay two, with road between.</summary>
    [Theory]
    [InlineData(16f, 1)]
    [InlineData(24f, 2)]
    public void StaircasesCloseTogetherAreOne(float between, int runs)
    {
        var path = new List<Vector2> { new(0, 0), new(8, 0), new(16, 0), new(16 + between, 0), new(24 + between, 0), new(40 + between, 0) };
        var steps = new List<(Vector2, Vector2)> { (path[1], path[2]), (path[3], path[4]) };

        var found = RoadStairRuns.Find(path, steps);

        Assert.Equal(runs, found.Count);
        Assert.Equal(1, found[0].from);
        Assert.Equal(4, found[found.Count - 1].to);
    }

    /// <summary>Two staircases further apart than the stone gap stay two, unless
    /// the ground between them is too steep for the road to be built there:
    /// then they are one (217 roads on one world were refused between two).</summary>
    [Theory]
    [InlineData(0.1f, 2)]
    [InlineData(0.4f, 1)]
    public void StaircasesWithRoadTooSteepBetweenThemAreOne(float gapGrade, int runs)
    {
        // Steps at 0.6 either side of a 30 m stretch climbing at gapGrade.
        var path = new List<Vector2> { new(0, 0), new(8, 0), new(16, 0), new(46, 0), new(54, 0), new(70, 0) };
        float H(Vector2 p) => p.x <= 8f ? 0f
            : p.x <= 16f ? (p.x - 8f) * 0.6f
            : p.x <= 46f ? 4.8f + (p.x - 16f) * gapGrade
            : p.x <= 54f ? 4.8f + 30f * gapGrade + (p.x - 46f) * 0.6f
            : 4.8f + 30f * gapGrade + 4.8f;
        var steps = new List<(Vector2, Vector2)> { (path[1], path[2]), (path[3], path[4]) };

        Assert.Equal(runs, RoadStairRuns.Find(path, steps, H).Count);
    }

    /// <summary>The path's ends are moved a little after the search (trimmed to
    /// the places' edges); a step shortened that way still reads as stairs.</summary>
    [Fact]
    public void AStepTrimmedShortIsStillAStairStep()
    {
        var path = new List<Vector2> { new(0, 0), new(8, 0), new(13, 0.4f) };
        var found = RoadStairRuns.Find(path, new List<(Vector2, Vector2)> { (new Vector2(8, 0), new Vector2(16, 0)) });

        Assert.Equal((1, 2), Assert.Single(found));
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
