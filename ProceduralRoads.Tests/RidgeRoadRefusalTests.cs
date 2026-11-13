using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Valheim.Testing;
using Valheim.Testing.Doubles;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// A road along a ridge (RoadRidges.Find: the ground lower on both sides by more than RoadRidges.Depth over
/// RoadRidges.MinLength or more) is refused by the one list (RoadNetworkGenerator.AlongARidge, in RoadRefusal) where the
/// ridge re-route finds no way off it, on every path that asks the list: generation's final check, an approach's trial
/// (RoadNetworkGenerator.TrialBuilds), a staircase's end floor (BuiltRoadRefusal) and a marked manual road (rule 13). A
/// crest shorter than RoadRidges.MinLength is not a ridge and is built on every path.
/// </summary>
[Collection("RoadStatics")]
public sealed class RidgeRoadRefusalTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();
    private readonly IDisposable _grade = GradeCap.At(0.35f);
    private readonly float _cap = RoadNetworkGenerator.EarthworkCap;
    private readonly int _retries = RoadNetworkGenerator.RidgeRetries;

    public RidgeRoadRefusalTests()
    {
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
        NetworkCutoff.Forget();
    }

    public void Dispose()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.EarthworkCap = _cap;
        RoadNetworkGenerator.RidgeRetries = _retries;
        RoadSpatialGrid.Clear();
        RoadNetworkGenerator.Reset();
        NetworkCutoff.Forget();
        _grade.Dispose();
        _scope.Dispose();
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, pathfinder);

    private static Island Everywhere()
    {
        var i = new Island { Id = 1, CellSize = 8, WorldOffset = 0, Min = new Vector2(-280, -280), Max = new Vector2(280, 280) };
        for (int x = -35; x <= 35; x++) for (int z = -35; z <= 35; z++) i.Cells.Add(new Vector2Int(x, z));
        return i;
    }

    /// <summary>
    /// Two plateaus in the sea joined by a crest along x = 0 from z = -<see cref="Half"/> to z = <see cref="Half"/>, climbing
    /// at 19% (the south plateau at 60 m, the north one as high as the crest's top): its flanks fall 1 in 1, so the ground
    /// 7 m out on each side stands 7 m under its top, and beyond 7.5 m out they drop into water 8 m deep. No way round it
    /// and no shelf on its flanks: the road from one plateau to the other rides it.
    /// </summary>
    private sealed class Crest : WorldGenerator
    {
        public readonly float Half;
        /// <summary>How far under the crest's top its flank stands this far out (the default: 1 in 1), and how far out it
        /// runs before the water.</summary>
        private readonly Func<float, float> _flank;
        private readonly float _out;
        public Crest(float half, Func<float, float>? flank = null, float flankOut = 7.5f) { Half = half; _flank = flank ?? (x => x); _out = flankOut; }
        private float Top(float z) => 60f + 0.19f * Mathf.Clamp(z + Half, 0f, 2f * Half);
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            bool plateau = Mathf.Abs(wy) >= Half && Mathf.Abs(wy) <= 120f && Mathf.Abs(wx) <= 60f;
            if (plateau) return Top(wy);
            if (Mathf.Abs(wy) < Half && Mathf.Abs(wx) <= _out) return Top(wy) - _flank(Mathf.Abs(wx));
            return 22f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float x, float z, out float weight, out float width) { weight = 0f; width = 0f; }
        public override int GetSeed() => 79;
    }

    private static readonly Vector2 South = new(0f, -80f), North = new(0f, 80f);

    private WorldGenerator Begin(float half, Func<float, float>? flank = null, float flankOut = 7.5f)
    {
        var world = new Crest(half, flank, flankOut);
        _scope.WithWorld(world).WithZdos().WithZoneSystem().WithScene().AsServer();
        RoadNetworkGenerator.MarkLocationsReady();
        RoadNetworkGenerator.EarthworkCap = RoadNetworkGenerator.DefaultEarthworkCap;
        RoadNetworkGenerator.RidgeRetries = 2;
        return world;
    }

    private static float Ground(WorldGenerator world, Vector2 p) => BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);

    /// <summary>The four paths' verdicts on the road from the south plateau to the north one: generation's (null where it
    /// built it, else its refusal), an approach trial's of the straight road along the crest (TrialBuilds: whether the road
    /// with it is built), a straight piece planned as a staircase's end floor plans its road and judged by the list
    /// with no more context (BuiltRoadRefusal), and a marked manual road's along the crest (null where it was planned, else its message);
    /// and the ridges of the end floor's piece.</summary>
    private (string? generation, bool trial, string? endFloor, string? manual, List<RoadRidges.Stretch> ridges) Verdicts(WorldGenerator world)
    {
        var finder = new RoadPathfinder(world) { TerrainVarianceThreshold = 1000f, Meander = 0f, Fords = false, Bridges = false, Stairs = false };
        SetPathfinder(finder);
        bool generated = RoadNetworkGenerator.GenerateRoad(South, 0f, North, 0f, 4f, "over the crest");
        string? generation = generated ? null : RoadSpatialGrid.LastRefusal ?? "refused";
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
        RoadNetworkGenerator.MarkLocationsReady();
        var cells = new List<Vector2>();
        for (float z = South.y; z <= North.y + 1e-3f; z += RoadPathfinder.CellSize) cells.Add(new Vector2(0f, z));
        bool trial = RoadNetworkGenerator.TrialBuilds(finder, cells, South, 0f, North, 0f, false, false, false, false, RoadNetworkGenerator.RoadWidth, 0f, 0f);
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
        RoadNetworkGenerator.MarkLocationsReady();

        var line = new List<Vector2>();
        for (float z = South.y; z <= North.y + 1e-3f; z += 2f) line.Add(new Vector2(0f, z));
        RoadSpatialGrid.PlannedPath? piece = RoadSpatialGrid.PlanRoadPath(line, RoadNetworkGenerator.RoadWidth, world);
        Assert.True(piece != null, "the straight piece along the crest was not planned: " + RoadSpatialGrid.LastRefusal);
        string? endFloor = RoadNetworkGenerator.BuiltRoadRefusal(new List<RoadSpatialGrid.PlannedPath> { piece! }, new List<RoadCrossing>(), out _);
        var ridges = RoadRidges.Find(new[] { piece! }, p => Ground(world, p)).FindAll(st => st.Kind == RoadRidges.Kind.Ridge);

        // The marked road searched as generation's road was (RoadNetworkGenerator.NewFinder, every search's: no staircase,
        // ford or bridge bought), so it rides the crest and its refusal is the list's, not its search's.
        string? manual = null;
        RoadNetworkGenerator.ConfigureFinders(f => { f.TerrainVarianceThreshold = 1000f; f.Meander = 0f; f.Fords = false; f.Bridges = false; f.Stairs = false; });
        try { ManualRoads.Prepare(new[] { South, new Vector2(0f, -40f), new Vector2(0f, 0f), new Vector2(0f, 40f), North }, false, new[] { Everywhere() }); }
        catch (Exception e) { manual = e.Message; }
        finally { RoadNetworkGenerator.ConfigureFinders(null); }
        return (generation, trial, endFloor, manual, ridges);
    }

    /// <summary>
    /// A crest 50 m long, climbing at 19%, the ground 7 m out on each side 7 m under it (the shape of the ridge a road rode
    /// off a staircase's top for 39.9 m, up to 7.14 m over the ground on either side, at 19%): the road along it is a ridge
    /// of more than 30 m, and with no way off it every path refuses it for the ridge. On the parent generation built it (the
    /// ridge re-route kept its plan where no way round was a third shorter) and the end floor and the marked road passed the
    /// list.
    /// </summary>
    [Fact]
    public void ARoadAlongALongRidgeIsRefusedOnEveryPath()
    {
        var (generation, trial, endFloor, manual, ridges) = Verdicts(Begin(half: 25f));
        var ridge = Assert.Single(ridges);
        Assert.True(ridge.Length >= 30f, FormattableString.Invariant($"a ridge of {ridge.Length:F0} m"));
        Assert.InRange(ridge.Worst, 6f, 8f);

        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, generation);
        Assert.False(trial, "the approach trial built the road along the ridge");
        Assert.True(endFloor != null && endFloor.StartsWith(RoadNetworkGenerator.RidgeRoadRefusal, StringComparison.Ordinal), endFloor ?? "the end floor's road passed");
        Assert.True(manual != null && manual.Contains(RoadNetworkGenerator.RidgeRoadRefusal), manual ?? "the marked road was planned");
    }

    /// <summary>
    /// A crest 8 m long of the same shape: the ground under the road falls away on both sides over less than
    /// RoadRidges.MinLength of it, a blip, not a ridge. Every path builds it.
    /// </summary>
    [Fact]
    public void AShortCrestIsBuiltOnEveryPath()
    {
        var (generation, trial, endFloor, manual, ridges) = Verdicts(Begin(half: 4f));
        Assert.Empty(ridges);
        Assert.True(generation == null, generation);
        Assert.True(trial, "the approach trial refused the short crest");
        Assert.True(endFloor == null, endFloor);
        Assert.True(manual == null, manual);
    }

    /// <summary>
    /// The ridge a road rode in game on the owner's walk (the road 60.1 m where he stood, the ground 8.2 m under it 8 m out
    /// and 9.8 m under it 12 m out, either side), 40 m long, climbing at 19%: a flank falling 1 in 1 to 8 m out, then 0.45
    /// in 1 to 12 m out, then the water. Generation, an approach trial and an end floor refuse the road along it for the
    /// ridge, and so does a marked road along it, searched as generation's is (with staircases its search bought a staircase
    /// off the crest that could not be laid, and it was refused for that before the list was asked). Only a crest 7 m deep at
    /// 7 m out, ending in water, was pinned.
    /// </summary>
    [Fact]
    public void ARoadAlongTheLongSteepRidgeOfTheWalkIsRefusedOnEveryPath()
    {
        static float Flank(float x) => x <= 8f ? x : 8f + (x - 8f) * 0.45f;
        var (generation, trial, endFloor, manual, ridges) = Verdicts(Begin(half: 20f, Flank, flankOut: 12.5f));
        var ridge = Assert.Single(ridges);
        Assert.True(ridge.Length >= RoadRidges.MinLength, FormattableString.Invariant($"a ridge of {ridge.Length:F0} m"));
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, generation);
        Assert.False(trial, "the approach trial built the road along the ridge");
        Assert.True(endFloor != null && endFloor.StartsWith(RoadNetworkGenerator.RidgeRoadRefusal, StringComparison.Ordinal), endFloor ?? "the end floor's road passed");
        Assert.True(manual != null && manual.Contains(RoadNetworkGenerator.RidgeRoadRefusal), manual ?? "the marked road was planned");
    }

    /// <summary>
    /// The low ridge a road rode in game for more than 30 m, climbing: the natural ground 7 m out on each side 2.6 to 3.2 m
    /// under the road (read across it every 2 m), a causeway a player cannot step off. Its flank falls 2.5 m over 7 m, then
    /// the water: the road as planned along it stands 2.6 to 3.4 m over the ground on both sides at worst. Under a 4 m depth
    /// it was no ridge and every path built it. Every path refuses it for the ridge.
    /// </summary>
    [Fact]
    public void ARoadAlongTheLowLongRidgeOfTheWalkIsRefusedOnEveryPath()
    {
        static float Flank(float x) => x * 2.5f / 7f;
        var (generation, trial, endFloor, manual, ridges) = Verdicts(Begin(half: 20f, Flank));
        var ridge = Assert.Single(ridges);
        Assert.True(ridge.Length >= RoadRidges.MinLength, FormattableString.Invariant($"a ridge of {ridge.Length:F0} m"));
        Assert.InRange(ridge.Worst, 2.6f, 3.4f);
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, generation);
        Assert.False(trial, "the approach trial built the road along the ridge");
        Assert.True(endFloor != null && endFloor.StartsWith(RoadNetworkGenerator.RidgeRoadRefusal, StringComparison.Ordinal), endFloor ?? "the end floor's road passed");
        Assert.True(manual != null && manual.Contains(RoadNetworkGenerator.RidgeRoadRefusal), manual ?? "the marked road was planned");
    }

    /// <summary>
    /// A road in a trench the length of the crest's: a cut 40 m long, climbing at 19%, its walls 3.5 m over the road 7 m
    /// out on each side, then the water. A trench left after the re-route is refused like a ridge, on every path (it was
    /// searched round and then built anyway).
    /// </summary>
    [Fact]
    public void ARoadInALongTrenchIsRefusedOnEveryPath()
    {
        static float Wall(float x) => -x * 0.5f;
        var (generation, trial, endFloor, manual, _) = Verdicts(Begin(half: 20f, Wall));
        Assert.Equal(RoadNetworkGenerator.TrenchRoadRefusal, generation);
        Assert.False(trial, "the approach trial built the road in the trench");
        Assert.True(endFloor != null && endFloor.StartsWith(RoadNetworkGenerator.TrenchRoadRefusal, StringComparison.Ordinal), endFloor ?? "the end floor's road passed");
        Assert.True(manual != null && manual.Contains(RoadNetworkGenerator.TrenchRoadRefusal), manual ?? "the marked road was planned");
    }

    /// <summary>A ridge is judged with no earthwork cap set too: the verdict is the road's shape, not the cap's re-route.</summary>
    [Fact]
    public void ARoadAlongALongRidgeIsRefusedWithNoEarthworkCap()
    {
        var world = Begin(half: 25f);
        RoadNetworkGenerator.EarthworkCap = 0f;
        var line = new List<Vector2>();
        for (float z = South.y; z <= North.y + 1e-3f; z += 2f) line.Add(new Vector2(0f, z));
        RoadSpatialGrid.PlannedPath? piece = RoadSpatialGrid.PlanRoadPath(line, RoadNetworkGenerator.RoadWidth, world);
        Assert.True(piece != null, "the straight piece along the crest was not planned: " + RoadSpatialGrid.LastRefusal);
        string? refused = RoadNetworkGenerator.BuiltRoadRefusal(new List<RoadSpatialGrid.PlannedPath> { piece! }, new List<RoadCrossing>(), out _);
        Assert.True(refused != null && refused.StartsWith(RoadNetworkGenerator.RidgeRoadRefusal, StringComparison.Ordinal), refused ?? "built with no cap");
    }
}
