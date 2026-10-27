using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// An approach refused in play, laid again on the ground the game wrote for it
/// (<see cref="ApproachGroundFile"/>). Hildir_cave at (-2304, 1536): its last-resort road
/// ended on a 100 m staircase 45 m under the cave, and the approach from its top was refused at
/// every landing, "no nearer the goal in 8000 steps of search".
/// </summary>
public class ApproachReplayTests
{
    /// <summary>Hildir_cave's levelling (road_sites_dump); the file names the six parts this placement kept.</summary>
    private static readonly LevelOp[] HildirCave =
    {
        new(-0.06f, -4.75f, -4.09f, 4f, false, 7f, 3f), new(0f, 2.25f, -2f, 0f, false, 8f, 3f), new(0.09f, 9.31f, -1f, 0f, false, 8f, 3f),
        new(3.5f, 5.75f, -1f, 0f, false, 4f, 3f), new(-4.31f, 5.89f, -1f, 0f, false, 4f, 3f), new(-0.22f, 7.98f, -1f, 0f, false, 4f, 3f),
        new(-10.23f, 6.62f, -1f, 0f, false, 10f, 3f), new(-15.93f, 2.69f, 0f, 0f, false, 10f, 3f), new(7.91f, 5.3f, -1f, 0f, false, 10f, 3f),
        new(14.89f, 1.74f, 0f, 0f, false, 10f, 3f),
    };

    /// <summary>The file predates the root being written: 296.45 is the root at which the replay picks the
    /// landings the game logged at the shelf's front (-2302,1542 at 294.5 kept, -2302,1541 at 294.4 not).</summary>
    private static ApproachGroundFile HildirCaveGround() => ApproachGroundFile.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "approach-hildircave.csv")),
        HildirCave, root: 296.45f, rockRadius: 15f, keepSmallRocks: true, footprintRadius: 26.155f);

    [Fact]
    public void TheGroundFileIsTheRecordedGround()
    {
        ApproachGroundFile file = HildirCaveGround();
        Assert.Equal(6, file.Ops.Count);
        Assert.Null(file.Top);
        // cli_ground_height, from the staircase's top up to the landing.
        foreach (var (x, z, game) in new[] { (-2309f, 1566f, 252.5f), (-2308f, 1562f, 262.7f), (-2307f, 1558f, 267.7f), (-2306f, 1554f, 273.3f),
                     (-2305f, 1550f, 285.5f), (-2304f, 1546f, 293.6f), (-2302f, 1542f, 294.5f), (-2316f, 1560f, 266.2f), (-2296f, 1560f, 266.6f) })
            Assert.InRange(file.Ground(x, z), game - 0.06f, game + 0.06f);
    }

    /// <summary>The old in-play allowance (stall 8000) stopped under the shelf in game and in this replay
    /// until cantilevered turns and strip columns carried down to the ground: held out
    /// from the face through its turns, the approach now climbs onto it within that allowance.</summary>
    [Fact]
    public void TheOldInPlayAllowanceClimbsOntoTheShelfWithCantileveredTurns()
    {
        RoadStairSpan? span = HildirCaveGround().Replay(200000, 8000, RoadStairRuns.GenerationExpansions, out string why);
        Assert.True(span != null, why);
    }

    [Fact]
    public void TheInPlayAllowanceClimbsHildirsFaceOntoItsLanding()
    {
        ApproachGroundFile file = HildirCaveGround();
        RoadStairSpan? span = file.Replay(RoadStairRuns.SiteTotalExpansions, RoadStairRuns.SiteStall, new SiteApproachRequest().Leg, out string why,
            new SiteApproachRequest().Allowance);
        Assert.True(span != null, why);
        // 44 m to a level landing in front, 43 to the joined edge where the shelf meets the slope.
        Assert.True(span!.Plan.Rise >= 43f, $"rose {span.Plan.Rise}");
        Vector2 top = new(span.Plan.End.x, span.Plan.End.z);
        Assert.Contains(file.Landings(), l => Vector2.Distance(l.at, top) <= RoadStairRuns.LedgeReach);
        Assert.True(file.Steps <= RoadStairRuns.SiteAllowance);
    }

    [Fact]
    public void AnApproachSpendsOneAllowanceOverEveryLandingItTries()
    {
        // Each landing's layout alone may search 200000 steps; together they stop at the approach's allowance.
        // (Held out from the face on cantilevers, the approach lays within 50000 now: 5000 does not.)
        ApproachGroundFile file = HildirCaveGround();
        RoadStairSpan? span = file.Replay(200000, 1000000, 200000, out string why, allowance: 5000);
        Assert.Null(span);
        Assert.InRange(file.Steps, 5000, 5000 + 200);
        Assert.Contains("search allowance (5000 steps) spent", why);
    }

    /// <summary>The file's rocks are boxes (their real tops are not written): the three great ice blocks' boxes
    /// cover the slope west of the shelf, at (-2317, 293, 1537), open ground.</summary>
    private static ApproachGroundFile HildirCaveGroundWithoutIceBoxes()
    {
        ApproachGroundFile file = HildirCaveGround();
        file.Rocks.RemoveAll(k => k.Name == "ice_rock1");
        return file;
    }

    [Fact]
    public void AnApproachAimsFirstWhereTheShelfJoinsTheSlope()
    {
        // If the landing is a big overhang that part of it cannot be reached, but coming up where the
        // landing connects to the mountain it likely can be. The shelf's front overhangs the face (it falls
        // 2 to 2.6 in 1 within 8 m); on its west the slope meets it.
        var aims = HildirCaveGroundWithoutIceBoxes().Aims();
        Assert.Equal("joined edge", aims[0].name);
        Assert.InRange(aims[0].at.x, -2320f, -2309f);
        Assert.InRange(aims[0].at.y, 1530f, 1539f);
        Assert.All(aims.GetRange(1, aims.Count - 1), a => Assert.Equal("level landing", a.name));
    }

    [Fact]
    public void AimedAtTheJoinedEdgeARoadsStaircaseAllowanceClimbsOntoTheShelf()
    {
        ApproachGroundFile file = HildirCaveGroundWithoutIceBoxes();
        int total = RoadNetworkGenerator.EntryTotalExpansions, stall = RoadNetworkGenerator.EntryStall;
        // The landing entry built with the roads: a road staircase's allowance (stall 4000), no approach allowance.
        RoadStairSpan? joined = file.Replay(total, stall, RoadStairRuns.GenerationExpansions, out string why);
        Assert.True(joined != null, why);
        Assert.True(joined!.Plan.Rise >= 42f, $"rose {joined.Plan.Rise}");
        // 22019 steps with StairLayout.FootOffRoadWeight: a start along the road searched a little longer.
        Assert.True(file.Steps <= 25000, $"{file.Steps} steps");
        // Aimed at the landings in front only, the same allowance stalls under the overhang.
        Assert.Null(file.Replay(total, stall, RoadStairRuns.GenerationExpansions, out _, joinedAims: false));
    }

    [Fact]
    public void TheGroundHeaderCarriesWhatTheReplayNeeds()
    {
        ApproachGroundFile old = HildirCaveGround();
        string extras = ApproachGroundHeader.Extras(296.45f, 15f, true, 26.155f, old.Ops,
            new StairContinuation(new Vector2(-2308.344f, 1571.656f), 202.5f, 250.589f, StairPieceKind.Stair));
        string[] lines = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "approach-hildircave.csv")).Split('\n');
        lines[0] = lines[0].TrimEnd() + "," + extras;
        ApproachGroundFile file = ApproachGroundFile.Parse(string.Join("\n", lines));
        Assert.Equal(296.45f, file.Root);
        Assert.Equal(15f, file.RockRadius);
        Assert.True(file.KeepSmallRocks);
        Assert.Equal(26.155f, file.FootprintRadius);
        Assert.Equal(old.Ops.Select(o => (o.OffsetX, o.OffsetZ, o.Height, o.Radius, o.Square, o.SmoothRadius, o.SmoothPower)),
            file.Ops.Select(o => (o.OffsetX, o.OffsetZ, o.Height, o.Radius, o.Square, o.SmoothRadius, o.SmoothPower)));
        Assert.True(file.Top.HasValue);
        Assert.Equal(202.5f, file.Top!.Value.Heading);
        Assert.Equal(250.589f, file.Top.Value.Height);
        Assert.Equal(StairPieceKind.Stair, file.Top.Value.Kind);
    }

    /// <summary>
    /// The wedge a turn opens is filled level with the piece before it and walked; an approach makes no
    /// earthwork in its place's footprint, so the ground may not cover a filler deeper than it may a piece
    /// (StairLayout.WedgeBuried). Without the rule, with cantilevers held out from 1:1 faces, a turn's filler
    /// on the way up Hildir's cave's face lay 0.6 m under the slope in the footprint and the approach to
    /// the joined edge was refused for earthwork; at the entry allowance it is not laid without the rule.
    /// </summary>
    [Fact]
    public void AnApproachUpHildirsFaceIsLaidOnlyWithItsFillersOutOfTheGround()
    {
        ApproachGroundFile file = HildirCaveGroundWithoutIceBoxes();
        int total = RoadNetworkGenerator.EntryTotalExpansions, stall = RoadNetworkGenerator.EntryStall;
        Assert.NotNull(file.Replay(total, stall, RoadStairRuns.GenerationExpansions, out _));
        StairLayout.RulesOff = SteepRules.WedgeBurial;
        try { Assert.Null(file.Replay(total, stall, RoadStairRuns.GenerationExpansions, out _)); }
        finally { StairLayout.RulesOff = SteepRules.None; }
    }
}
