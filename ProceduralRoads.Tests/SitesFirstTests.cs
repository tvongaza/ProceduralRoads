using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// Places placed before their roads ("sites first"): the
/// load-time generation waits for them, and a road to one goes onto its real landing.
/// </summary>
public partial class SitesFirstTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();
    /// <summary>Level ground at 50 m out to 200 m, sea beyond.</summary>
    private sealed class LevelWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f ? 0f : 50f;
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    private const float Footprint = 20f;
    /// <summary>MountainCave02's levelling: a floor 4 m across, 4.75 m behind the centre and 4.09 m down, smoothed out to 7 m.</summary>
    private static readonly IReadOnlyList<LevelOp> CaveFloor = new[] { new LevelOp(-0.06f, -4.75f, -4.09f, 4f, false, 7f, 3f) };
    private readonly LevelWorld _world = new();

    public SitesFirstTests()
    {
        _scope.WithWorld(_world);
        RoadNetworkGenerator.Reset();
        PlacedSites.Reset();
        RoadSiteProtection.Set(new[] { new RoadSiteProtection.Footprint(new Vector2(0f, 0f), Footprint) });
    }

    public void Dispose()
    {
        SetPathfinder(null);
        PlacedSites.Enabled = true;
        PlacedSites.Reset();
        LocationLevelling.Source = null;
        RoadSiteProtection.Reset();
        RoadNetworkGenerator.SitesFirst = null;
        RoadNetworkGenerator.Reset();
        _scope.Dispose();
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    /// <summary>The level ground with a deep sea east of x 28 for 12 m either side of the cave's facing:
    /// its ring point on that side is dry, but just beyond it is water.</summary>
    private sealed class SeaOnTheLandingsSide : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) =>
            Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f || (wx > 28f && Mathf.Abs(wy) < 12f) ? 0f : 50f;
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    /// <summary>A frost cave at the origin turned to face +x (yaw 90), as placed on the level ground.</summary>
    private PlacedSite Cave()
    {
        var record = new PlacedSiteRecord
        {
            Prefab = "MountainCave02", Centre = new Vector2(0f, 0f), Yaw = 90f, Root = 50f, RockRadius = 0f,
            Ops = new List<LevelOp>(CaveFloor), X0 = -60, Z0 = -60, W = 121, H = 121,
        };
        Func<float, float, float> ground = PlacedSiteGround.Sampler(record.Ground((x, z) => _world.GetHeight(x, z)), record.X0, record.Z0, record.W, record.H);
        return new PlacedSite { Record = record, Ground = ground, Shape = PlacedSiteShape.Of(record.Centre, record.Yaw, CaveFloor[0], 0f, null, ground, false, record.Root) };
    }

    /// <summary>The ring point a road is searched to lies toward the place's landing, whichever way the
    /// road comes: searched to the centre, one came in from behind 80 m from the landing.</summary>
    [Fact]
    public void ARoadToAPlacedPlaceIsAimedAtTheRingNearestItsLanding()
    {
        PlacedSite cave = Cave();

        Vector2 aim = PlacedSites.Aim(cave, 24f, _ => true);

        Assert.InRange(aim.magnitude, 24f, 24f + PlacedSites.AimOut + 0.01f);
        Assert.True(aim.x > 23f, $"aimed at {aim}, the landing is at {cave.Shape.Landing}");
        // Not road ground there: the nearest round the ring either side.
        Vector2 aside = PlacedSites.Aim(cave, 24f, p => p.y > 5f);
        Assert.True(aside.y > 5f && aside.x > 0f, $"aimed at {aside}");
    }

    /// <summary>Where only the place's back is road ground, the road is aimed there, not into the sea its
    /// landing faces (at -6016,4736: water 6-14 m out to the N, NE, E and SE of its landing).</summary>
    [Fact]
    public void ARoadToAPlacedPlaceFacingTheSeaIsAimedRoundToItsBack()
    {
        PlacedSite cave = Cave();

        Vector2 aim = PlacedSites.Aim(cave, 24f, p => p.x < -10f);

        Assert.True(aim.x < -10f, $"aimed at {aim}");
    }

    /// <summary>A road ending 58 m from a placed place's landing is still carried on onto it (a ring point
    /// at the side of a Dvergr entrance is 41 m out, its landing 24 m in); an approach keeps its 40 m.</summary>
    [Fact]
    public void AWayOntoAPlacedPlacesLandingReachesFurtherThanAnApproach()
    {
        PlacedSite cave = Cave();
        Vector2 end = cave.Shape.Landing + new Vector2(0f, 58f);
        RoadSpatialGrid.PlannedPath? From(float reach, out string why)
        {
            var request = new SiteApproachRequest
            {
                RoadEnd = end, RoadHeight = 50f, Arriving = 180f, Centre = cave.Centre,
                RoadTail = new List<Vector2> { end + new Vector2(0f, 20f), end }, Reach = reach,
            };
            return RoadSiteEntry.Plan(request, cave.Shape, 4f, cave.Ground, _world, out why);
        }

        Assert.True(From(RoadNetworkGenerator.PlacedSiteReach, out string placed) != null, placed);
        Assert.True(From(RoadStairRuns.SiteReach, out string approach) == null && approach.Contains("too far"), approach);
    }

    /// <summary>A road to a placed place goes onto its landing as part of the road, and nothing is left to lay
    /// later; without the place placed it stops at the ring, metres short.</summary>
    [Fact]
    public void ARoadToAPlacedPlaceGoesOntoItsLanding()
    {
        foreach (bool placed in new[] { false, true })
        {
            RoadNetworkGenerator.Reset();
            RoadSpatialGrid.Clear();
            StairStructures.Reset();
            PlacedSites.Reset();
            PlacedSite cave = Cave();
            PlacedSites.Ready(cave);
            PlacedSites.Enabled = placed;
            LocationLevelling.Source = c => c.magnitude < 0.5f ? CaveFloor : null;
            SetPathfinder(new RoadPathfinder(_world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });

            Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(0f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the cave"));

            var walked = RoadSpatialGrid.GetRoadPointsNearPosition(Vector3.zero, 60f).Where(p => p.IsRoad).Select(p => p.p)
                .Concat(StairStructures.Runs.SelectMany(r => r.Pieces).Select(p => new Vector2(p.Position.x, p.Position.z))).ToList();
            float toLanding = walked.Min(p => Vector2.Distance(p, cave.Shape.Landing));
            string outcomes = "last refusal: " + (RoadSpatialGrid.LastRefusal ?? "none");
            if (placed)
            {
                // A road keeps its own reach (half its width and its blend) off the entrance's pit, so it
                // stops a little short of the shape's nominal landing, but inside the place's footprint.
                Assert.True(toLanding <= 6f, $"placed: nearest the landing {cave.Shape.Landing} is {toLanding:F1} m off; {outcomes}");
                Assert.Contains(walked, p => p.magnitude < Footprint);
                // Reached because it was carried on onto the landing, wherever on it that ended.
                Assert.True(PlacedSites.Landed(cave.Centre));
                Assert.True(RoadNetworkGenerator.AlreadyServedByNetwork(Vector3.zero, 10f));
            }
            else Assert.True(toLanding > 10f, $"not placed: nearest the landing is {toLanding:F1} m off");
        }
    }

    /// <summary>A regeneration forgets which places the old network landed on (its roads are gone), not the
    /// places placed: they are the world's.</summary>
    [Fact]
    public void ARegenerationForgetsWhichPlacesTheOldNetworkLandedOn()
    {
        PlacedSite cave = Cave();
        PlacedSites.Remember(cave.Record);
        PlacedSites.MarkLanded(cave.Centre);
        RoadNetworkGenerator.Reset();
        Assert.False(PlacedSites.Landed(cave.Centre));
        Assert.True(PlacedSites.TryGet(cave.Centre, out _));
    }

    /// <summary>Where nothing gets from the ring onto a placed place's landing, the road is refused, not left at
    /// the outskirts: the planner tries its other candidates.</summary>
    [Fact]
    public void ARoadThatCannotGetOntoAPlacedPlacesLandingIsRefused()
    {
        foreach (bool placed in new[] { false, true })
        {
            RoadNetworkGenerator.Reset();
            RoadSpatialGrid.Clear();
            StairStructures.Reset();
            PlacedSites.Reset();
            PlacedSite cave = Cave();
            // The placed ground a sheer wall 40 m high everywhere but the ring the road ends on.
            Func<float, float, float> wall = (x, z) => new Vector2(x, z).magnitude < 22f ? 90f : 50f;
            PlacedSites.Ready(new PlacedSite { Record = cave.Record, Ground = wall, Shape = PlacedSiteShape.Of(cave.Centre, 90f, CaveFloor[0], 0f, null, wall, false, 90f) });
            PlacedSites.Enabled = placed;
            LocationLevelling.Source = c => c.magnitude < 0.5f ? CaveFloor : null;
            SetPathfinder(new RoadPathfinder(_world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });

            bool built = RoadNetworkGenerator.GenerateRoad(new Vector2(0f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the cave");

            Assert.Equal(!placed, built);
            if (placed)
            {
                Assert.Equal("no way onto a placed place's landing", RoadSpatialGrid.LastRefusal);
                Assert.Empty(RoadSpatialGrid.GetRoadPointsNearPosition(Vector3.zero, 60f));
            }
        }
    }

    /// <summary>Where the landing's side cannot be reached from beyond the ring, the road still comes in as
    /// near that side as it can, further round the ring, not wherever a search to the place arrives (three Dvergr
    /// entrances on one test world found no route to their landing's side, and one then came in on
    /// the far side, too far from its landing).</summary>
    [Fact]
    public void ARoadThatCannotReachAPlacedPlacesLandingSideComesInFurtherRoundTheRing()
    {
        var sea = new SeaOnTheLandingsSide();
        _scope.WithWorld(sea);
        PlacedSite cave = Cave();
        Func<float, float, float> ground = PlacedSiteGround.Sampler(cave.Record.Ground((x, z) => sea.GetHeight(x, z)), cave.Record.X0, cave.Record.Z0, cave.Record.W, cave.Record.H);
        PlacedSites.Ready(new PlacedSite { Record = cave.Record, Ground = ground, Shape = PlacedSiteShape.Of(cave.Centre, 90f, CaveFloor[0], 0f, null, ground, false, cave.Record.Root) });
        LocationLevelling.Source = c => c.magnitude < 0.5f ? CaveFloor : null;
        SetPathfinder(new RoadPathfinder(sea) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });

        Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(-20f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the cave"));

        Vector2 nearest = RoadSpatialGrid.GetRoadPointsNearPosition(Vector3.zero, 60f).Where(p => p.IsRoad && p.p.magnitude > Footprint)
            .OrderBy(p => p.p.magnitude).First().p;
        float offLanding = Mathf.Abs(Mathf.Atan2(nearest.y, nearest.x) * Mathf.Rad2Deg);
        string outcomes = "last refusal: " + (RoadSpatialGrid.LastRefusal ?? "none");
        Assert.True(offLanding <= 60f, $"the road's nearest point outside the cave is {nearest}, {offLanding:F0} deg round from its landing's side; {outcomes}");
    }

    /// <summary>A staircase onto a placed place's landing that could not be laid is not laid out again from
    /// the same start, and a place gets no more than EntryStairsPerPlace of them: the slowest islands took
    /// 570 and 537 s, most of it laying the same failing staircases for each road tried to a place.</summary>
    [Fact]
    public void AFailedStaircaseOntoAPlacedPlacesLandingIsNotLaidOutAgain()
    {
        int perPlace = RoadNetworkGenerator.EntryStairsPerPlace;
        try
        {
            PlacedSite cave = Cave();
            Func<float, float, float> wall = (x, z) => new Vector2(x, z).magnitude < 22f ? 90f : 50f;
            PlacedSites.Ready(new PlacedSite { Record = cave.Record, Ground = wall, Shape = PlacedSiteShape.Of(cave.Centre, 90f, CaveFloor[0], 0f, null, wall, false, 90f) });
            LocationLevelling.Source = c => c.magnitude < 0.5f ? CaveFloor : null;
            SetPathfinder(new RoadPathfinder(_world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });
            RoadNetworkGenerator.EntryStairLayouts = 0;

            Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(0f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the cave"));
            int first = RoadNetworkGenerator.EntryStairLayouts;
            Assert.True(first > 0);
            Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(0f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the cave again"));
            Assert.Equal(first, RoadNetworkGenerator.EntryStairLayouts);

            RoadNetworkGenerator.EntryStairsPerPlace = 0;
            Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(120f, 60f), 0f, new Vector2(0f, 0f), 10f, 4f, "from elsewhere"));
            Assert.Equal(first, RoadNetworkGenerator.EntryStairLayouts);
        }
        finally { RoadNetworkGenerator.EntryStairsPerPlace = perPlace; }
    }

    /// <summary>The level ground with the cave on an islet: deep sea from 30 to 80 m out all round.</summary>
    private sealed class Islet : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            float r = new Vector2(wx, wy).magnitude;
            return Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f || (r > 30f && r < 80f) ? 0f : 50f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Mountain;
        public override int GetSeed() => 77;
    }

    /// <summary>A placed place no road gets to is searched round its ring once, not for every road tried to it:
    /// each search that finds nothing spends its whole budget (the slowest island in game, 149 s of build
    /// searches; on its layout 40 of that island's 115 were round rings).</summary>
    [Fact]
    public void APlacedPlaceNoRoadGetsToIsSearchedRoundItsRingOnce()
    {
        var islet = new Islet();
        _scope.WithWorld(islet);
        PlacedSites.Ready(Cave());
        LocationLevelling.Source = c => c.magnitude < 0.5f ? CaveFloor : null;
        SetPathfinder(new RoadPathfinder(islet) { Stairs = false, TerrainVarianceThreshold = 1000f, Meander = 0f });
        RoadNetworkGenerator.RingTurnSearches = 0;

        Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(0f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the islet"));
        int first = RoadNetworkGenerator.RingTurnSearches;
        Assert.True(first > 0);
        Assert.False(RoadNetworkGenerator.GenerateRoad(new Vector2(-120f, 0f), 0f, new Vector2(0f, 0f), 10f, 4f, "again"));
        Assert.Equal(first, RoadNetworkGenerator.RingTurnSearches);
    }

    /// <summary>A road from the north to a place facing east comes in on the east, its landing's side;
    /// without the place placed it stops on the north, the side it came in on.</summary>
    [Fact]
    public void ARoadToAPlacedPlaceComesInOnItsLandingsSide()
    {
        foreach (bool placed in new[] { false, true })
        {
            RoadNetworkGenerator.Reset();
            RoadSpatialGrid.Clear();
            PlacedSites.Reset();
            PlacedSites.Ready(Cave());
            PlacedSites.Enabled = placed;
            LocationLevelling.Source = c => c.magnitude < 0.5f ? CaveFloor : null;
            SetPathfinder(new RoadPathfinder(_world) { Stairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f });

            Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(0f, 120f), 0f, new Vector2(0f, 0f), 10f, 4f, "to the cave"));

            Vector2 nearest = RoadSpatialGrid.GetRoadPointsNearPosition(Vector3.zero, 60f).Where(p => p.IsRoad)
                .OrderBy(p => p.p.magnitude).First().p;
            if (placed) Assert.True(nearest.x > Mathf.Abs(nearest.y), $"placed: the road's nearest point to the cave is {nearest}");
            else Assert.True(nearest.y > Mathf.Abs(nearest.x), $"not placed: the road's nearest point to the cave is {nearest}");
        }
    }
}
