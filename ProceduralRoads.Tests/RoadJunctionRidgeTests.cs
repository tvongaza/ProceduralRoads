using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valheim.Testing;
using Valheim.Testing.Doubles;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// A ridge or trench that runs through a junction is refused (the owner's rule of 18 Nov 2026): each road's ridge judge
/// (RoadNetworkGenerator.Ridges, RoadRidges.Find with RoadRidges.Joined) reads the stored stretch of the laid road it meets at
/// either end (RoadSpatialGrid.JoinedAt) with its own pieces, in travel order, through the one RoadRidges.Join. Offline, roads
/// were stored with a ridge or trench split at a junction, each part under the rule on its own road (8.4 m of a ridge meeting a
/// stored 1.7 m, 10.1 m together; trenches of 11.4 and 12.1 m; ridges of 8.3 and 5.5 m) while the stored network held one
/// stretch long enough to count. The live lengths and depths are laid here on flat ground, the laid road committed and recorded
/// as a generated road is, the new road judged as the one list judges it.
/// </summary>
[Collection("RoadStatics")]
public sealed class RoadJunctionRidgeTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();
    private const float Ground = 50f;
    /// <summary>The points' spacing, metres: fine enough that a stretch measures its live length to a tenth.</summary>
    private const float Step = 0.1f;

    private sealed class FlatWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Ground;
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Meadows;
        public override int GetSeed() => 78;
        public override void GetRiverWeight(float x, float z, out float weight, out float width) { weight = 0f; width = 0f; }
    }

    public RoadJunctionRidgeTests()
    {
        _scope.WithWorld(new FlatWorld());
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
        PlacedLocations.Reset();
    }

    public void Dispose()
    {
        RoadSpatialGrid.Clear();
        RoadNetworkGenerator.Reset();
        PlacedLocations.Reset();
        _scope.Dispose();
    }

    /// <summary>A piece from <paramref name="from"/> along <paramref name="dir"/> for <paramref name="length"/> m, a point every
    /// <see cref="Step"/>, at <paramref name="offset"/> m off the ground (+ a ridge, - a trench) where <paramref name="on"/> says
    /// (by distance along it), on the ground elsewhere.</summary>
    private static RoadSpatialGrid.PlannedPath Piece(Vector2 from, Vector2 dir, float length, Func<float, bool> on, float offset)
    {
        var points = new List<Vector2>();
        var heights = new List<float>();
        int n = Mathf.RoundToInt(length / Step);
        for (int k = 0; k <= n; k++)
        {
            float along = k * Step;
            points.Add(from + dir.normalized * along);
            heights.Add(on(along) ? Ground + offset : Ground);
        }
        return new RoadSpatialGrid.PlannedPath(points, heights, points.ConvertAll(_ => default(RoadPointDebugInfo)), RoadNetworkGenerator.RoadWidth,
            n * Step, false);
    }

    /// <summary>Stores <paramref name="piece"/> as a generated road is stored (its points in the grid, its line recorded as laid).</summary>
    private static void Lay(params RoadSpatialGrid.PlannedPath[] pieces)
    {
        RoadSpatialGrid.CommitRoad(pieces.ToList(), ownChecked: true);
        RoadSpatialGrid.RecordLaid(pieces);
    }

    private static readonly Vector2 East = new(1f, 0f), North = new(0f, 1f);

    /// <summary>
    /// A road laid first, ending at the junction <paramref name="j"/> with <paramref name="laidLength"/> m of it before the
    /// junction <paramref name="offset"/> m off the ground, and a road from the junction on along the same line with
    /// <paramref name="ownLength"/> m of it so; the new road's verdict, and its stretches, as the one list judges it.
    /// </summary>
    private static (string? refused, string reason, List<RoadRidges.Stretch> stretches) MeetAlongALine(Vector2 j, float laidLength, float ownLength, float offset,
        bool newRoadEndsAtTheJunction = false)
    {
        // The new road runs toward the junction (it ends there) or away from it (it starts there).
        Vector2 laidFrom = j - East * 60f;
        Lay(Piece(laidFrom, East, 60f, a => a >= 60f - laidLength - Step / 2f, offset));
        RoadSpatialGrid.PlannedPath own = newRoadEndsAtTheJunction
            ? Piece(j + East * 60f, -East, 60f, a => a >= 60f - ownLength - Step / 2f, offset)
            : Piece(j, East, 60f, a => a <= ownLength + Step / 2f, offset);
        var plans = new List<RoadSpatialGrid.PlannedPath> { own };
        string? refused = RoadNetworkGenerator.AlongARidge(plans, new List<RoadCrossing>(), out _, out string reason);
        return (refused, reason, RoadNetworkGenerator.Ridges(plans, new List<RoadCrossing>()));
    }

    /// <summary>The road on its own, as the judge read it before: no stretch long enough to count.</summary>
    private static void OnItsOwnNothingCounts(float ownLength, float offset)
    {
        var alone = Piece(new Vector2(5000f, 5000f), East, 60f, a => a < ownLength, offset);
        Assert.Empty(RoadRidges.Find(new[] { alone }, p => Ground, null, null, null));
    }

    /// <summary>RoadSeedA (-9501.4, -2422.6): a road's own ridge 8.4 m, 2.49 m over the ground, meeting a stored stretch at its
    /// junction, 10.1 m together: refused along a ridge.</summary>
    [Fact]
    public void ARidgeOf8Point4MetresMeetingAStoredStretchIsJudgedTogetherAndRefused()
    {
        OnItsOwnNothingCounts(8.4f, 2.49f);
        var (refused, reason, stretches) = MeetAlongALine(new Vector2(-9505.1f, -2414.5f), 10.1f - 8.4f, 8.4f, 2.49f, newRoadEndsAtTheJunction: true);
        Assert.True(refused != null, "the ridge through the junction is not refused");
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, reason);
        Assert.Equal(10.1f, Assert.Single(stretches).Length, 0.2f);
    }

    /// <summary>RoadSeedB (2185.8, -1274.4): a road ending in a trench of 11.4 m (2.08 m) on a road whose own trench of 12.1 m
    /// (2.55 m) starts at the junction: 23.5 m together, past RoadRidges.TrenchMinLength: refused in a trench.</summary>
    [Fact]
    public void TrenchesOf11Point4And12Point1MetresThroughAJunctionAreOneTrenchAndRefused()
    {
        OnItsOwnNothingCounts(11.4f, -2.08f);
        Vector2 j = new(2186.7f, -1274.7f);
        // The laid road leaves the junction east, its trench its first 12.1 m (a few metres of it run back to its own start).
        Lay(Piece(j - East * 4f, East, 64f, a => a >= 4f - Step / 2f && a <= 4f + 12.1f + Step / 2f, -2.55f));
        var own = Piece(j - East * 60f, East, 60f, a => a >= 60f - 11.4f - Step / 2f, -2.08f);
        var plans = new List<RoadSpatialGrid.PlannedPath> { own };
        string? refused = RoadNetworkGenerator.AlongARidge(plans, new List<RoadCrossing>(), out _, out string reason);
        Assert.True(refused != null, "the trench through the junction is not refused");
        Assert.Equal(RoadNetworkGenerator.TrenchRoadRefusal, reason);
        RoadRidges.Stretch st = Assert.Single(RoadNetworkGenerator.Ridges(plans, new List<RoadCrossing>()));
        Assert.Equal(11.4f + 12.1f, st.Length, 0.3f);
    }

    /// <summary>RoadSeedC (-127.1, 973.0): a road ending at (-120.9, 970.3) on its own ridge of 8.3 m (2.58 m), where a road laid
    /// before it starts along the same line with a ridge of 5.5 m (2.02 m): one ridge of 13.8 m, refused. As stored live, the
    /// junction is the end of the road's first piece, not of the road: it rides the laid road from there (the stretch kept
    /// off it, RoadRide.Own) and goes on in a second piece 35 m on, so the first piece's run is read with the laid stretch.</summary>
    [Fact]
    public void RidgesOf8Point3And5Point5MetresEndToEndAreOneRidgeAndRefused()
    {
        OnItsOwnNothingCounts(8.3f, 2.58f);
        Vector2 j = new(-120.9f, 970.3f);
        Lay(Piece(j, -East, 40f, a => a <= 5.5f + Step / 2f, 2.02f));
        var own = Piece(j + East * 60f, -East, 60f, a => a >= 60f - 8.3f - Step / 2f, 2.58f);
        var beyond = Piece(j - East * 35f + North * 20f, North, 6f, _ => false, 0f);
        var plans = new List<RoadSpatialGrid.PlannedPath> { own, beyond };
        Assert.NotNull(RoadNetworkGenerator.AlongARidge(plans, new List<RoadCrossing>(), out _, out string reason));
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, reason);
        Assert.Equal(8.3f + 5.5f, Assert.Single(RoadNetworkGenerator.Ridges(plans, new List<RoadCrossing>())).Length, 0.3f);
    }

    /// <summary>RoadSeedE (-2008, 3776): a new route joining a road at (-1993, 3783), a trench 3.49 m deep, 33 m together, each
    /// road's part under RoadRidges.TrenchMinLength (the split is not in the record: 16.5 m each here): refused in a trench.</summary>
    [Fact]
    public void ATrenchOf33MetresSplitAtAJunctionIsRefused()
    {
        OnItsOwnNothingCounts(16.5f, -3.49f);
        var (refused, reason, stretches) = MeetAlongALine(new Vector2(-1993f, 3783f), 16.5f, 16.5f, -3.49f);
        Assert.True(refused != null, "the trench through the junction is not refused");
        Assert.Equal(RoadNetworkGenerator.TrenchRoadRefusal, reason);
        Assert.Equal(33f, Assert.Single(stretches).Length, 0.3f);
    }

    /// <summary>Run61p (-9125.9, 1063.5): a ridge of 11 m across two roads meeting, each short on its own (the split is not in
    /// the record: 6 and 5 m here), the new road meeting the laid one in its middle (a T), the ridge running on along one side:
    /// that side's stretch is read, the other's not, and the ridge is refused.</summary>
    [Fact]
    public void ARidgeOf11MetresThroughATJunctionIsRefusedAlongTheSideItRunsOn()
    {
        OnItsOwnNothingCounts(6f, 2.2f);
        Vector2 j = new(-9125.9f, 1063.5f);
        // The laid road runs north through the junction, its ridge the 5 m north of it.
        Lay(Piece(j - North * 30f, North, 60f, a => a >= 30f - Step / 2f && a <= 35f + Step / 2f, 2.2f));
        // The new road comes from the west and ends at the junction, its last 6 m on the ridge, turning north onto it.
        var own = Piece(j - East * 40f, East, 40f, a => a >= 40f - 6f - Step / 2f, 2.2f);
        var plans = new List<RoadSpatialGrid.PlannedPath> { own };
        RoadRidges.Joined joined = RoadSpatialGrid.JoinedAt(plans);
        Assert.Empty(joined.AtStart);
        Assert.Equal(2, joined.AtEnd.Count);
        Assert.NotNull(RoadNetworkGenerator.AlongARidge(plans, new List<RoadCrossing>(), out _, out string reason));
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, reason);
        Assert.Equal(11f, Assert.Single(RoadNetworkGenerator.Ridges(plans, new List<RoadCrossing>())).Length, 0.3f);
    }

    /// <summary>RoadSeedA (-1774.9, 756.4): a road planned onto a laid road that rides it a few metres before its end (held to its
    /// surface there, a stretch it never stores: RoadRide.Own). Its junction is where it comes onto the laid road, as the network
    /// holds it: its own ridge of 7 m up to there and the laid road's 6 m on the far side of the mouth are one ridge of 13 m,
    /// refused. Read at the planned end, 6 m along the laid road, the ridden stretch between broke the ridge in two.</summary>
    [Fact]
    public void ARoadRidingTheLaidRoadToItsEndMeetsItWhereItComesOntoIt()
    {
        Vector2 j = new(-1774.1f, 756.1f);
        // The laid road runs east through the mouth, its ridge the 6 m west of it.
        Lay(Piece(j - East * 20f, East, 60f, a => a >= 14f - Step / 2f && a <= 20f + Step / 2f, 2.34f));
        // The new road comes from the south onto it (its last 7 m on the ridge) and rides it east for 6 m at its height.
        var comeOnto = Piece(j - North * 40f, North, 40f, a => a >= 33f - Step / 2f, 2.34f);
        var ride = Piece(j, East, 6f, _ => false, 0f);
        var points = new List<Vector2>(comeOnto.Points);
        var heights = new List<float>(comeOnto.Heights);
        points.AddRange(ride.Points.Skip(1));
        heights.AddRange(ride.Heights.Skip(1));
        var plans = new List<RoadSpatialGrid.PlannedPath> { new(points, heights, points.ConvertAll(_ => default(RoadPointDebugInfo)), RoadNetworkGenerator.RoadWidth, 46f, false) };
        Assert.Empty(RoadRidges.Find(plans, _ => Ground, null, null, RoadSpatialGrid.JoinedAt(plans)));
        Assert.NotNull(RoadNetworkGenerator.AlongARidge(plans, new List<RoadCrossing>(), out _, out string reason));
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, reason);
        Assert.Equal(13f, Assert.Single(RoadNetworkGenerator.Ridges(plans, new List<RoadCrossing>())).Length, 1f);
    }

    /// <summary>A ridge wholly of the laid road (the new road on the ground from the junction) is that road's, not the new road's;
    /// and a road with no junction is read as before.</summary>
    [Fact]
    public void ALaidRoadsOwnRidgeAtAJunctionIsNotTheNewRoads()
    {
        var (refused, _, stretches) = MeetAlongALine(new Vector2(100f, 100f), 15f, 0f, 2.5f);
        Assert.Null(refused);
        Assert.Empty(stretches);
        var far = Piece(new Vector2(400f, 400f), East, 60f, a => a < 8f, 2.5f);
        Assert.False(RoadSpatialGrid.JoinedAt(new[] { far }).Any);
    }

    /// <summary>The stretch is read at the laid road's heights as stored now (an append's raise included): a laid road raised off
    /// its ridge since it was recorded meets no ridge.</summary>
    [Fact]
    public void TheLaidStretchIsReadAtItsStoredHeights()
    {
        Vector2 j = new(300f, -300f);
        var laid = Piece(j - East * 60f, East, 60f, a => a > 60f - 5f, 2.5f);
        // Recorded with its ridge heights, stored on the ground (as if levelled since).
        RoadSpatialGrid.CommitRoad(new List<RoadSpatialGrid.PlannedPath> { Piece(j - East * 60f, East, 60f, _ => false, 0f) }, ownChecked: true);
        RoadSpatialGrid.RecordLaid(new[] { laid });
        var own = Piece(j, East, 60f, a => a < 8f, 2.5f);
        Assert.Null(RoadNetworkGenerator.AlongARidge(new List<RoadSpatialGrid.PlannedPath> { own }, new List<RoadCrossing>(), out _, out _));
    }

    /// <summary>One assembly on every path (rule 13): the final check (RoadRefusal), a way in as it is planned and every road judged
    /// by its plans alone (ShapeRefusal, BuiltRoadRefusal: a staircase's end floor, a manual road), the re-route and searched-round
    /// verdicts (Ridges) and the offline gate (RoadRidges.Find with RoadSpatialGrid.JoinedAt, only the roads laid before it read) all
    /// read the RoadSeedC ridge through its junction as one, refused at the same place.</summary>
    [Fact]
    public void EveryPathReadsTheRidgeThroughAJunctionAsOne()
    {
        Vector2 j = new(-120.9f, 970.3f);
        var laid = Piece(j, -East, 40f, a => a <= 5.5f + Step / 2f, 2.02f);
        Lay(laid);
        var own = Piece(j + East * 60f, -East, 60f, a => a >= 60f - 8.3f - Step / 2f, 2.58f);
        var plans = new List<RoadSpatialGrid.PlannedPath> { own };

        RoadNetworkGenerator.Refusal? final = RoadNetworkGenerator.RoadRefusal(new RoadNetworkGenerator.PlannedRoad { Plans = plans }, out _);
        RoadNetworkGenerator.Refusal? shaped = RoadNetworkGenerator.ShapeRefusal(plans, Array.Empty<RoadSpatialGrid.PlannedPath>(), new List<RoadCrossing>(), null,
            RoadNetworkGenerator.LinesAtEnds(plans, new List<RoadCrossing>(), Array.Empty<RoadStairSpan>()));
        string? built = RoadNetworkGenerator.BuiltRoadRefusal(plans, new List<RoadCrossing>(), out Vector2 builtAt);
        RoadRidges.Stretch judged = Assert.Single(RoadNetworkGenerator.Ridges(plans, new List<RoadCrossing>()));
        // The offline gate: the new road stored and recorded too, each road judged with every other met at its ends.
        int me = RoadSpatialGrid.RecordLaid(plans);
        RoadRidges.Stretch gate = Assert.Single(RoadRidges.Find(plans, _ => Ground, null, null,
            RoadSpatialGrid.JoinedAt(plans, before: me)));

        Assert.NotNull(final);
        Assert.NotNull(shaped);
        Assert.Equal(RoadNetworkGenerator.RidgeRoadRefusal, final!.Reason);
        Assert.Equal(final.Reason, shaped!.Reason);
        Assert.Equal(final.Said, shaped.Said);
        Assert.Equal(final.Said, built);
        Assert.True(Vector2.Distance(final.At, shaped.At) < 1e-3f && Vector2.Distance(final.At, builtAt) < 1e-3f);
        Assert.Equal(judged.Length, gate.Length, 3);
        Assert.Equal(judged.Points[0], gate.Points[0]);
    }
}
