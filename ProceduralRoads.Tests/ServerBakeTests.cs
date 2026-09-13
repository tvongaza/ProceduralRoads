using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Road terrain written by the server for clients without the mod: which
/// zones, what to do with each, and the rule that a zone is stamped as
/// carrying its roads only once its compiler has saved them. The Unity side
/// (temporary terrain, ghost init) is ServerTerrainBake and is exercised in
/// game; everything it decides is decided here.
/// </summary>
public class ServerBakeTests
{
    private const int Version = 12345;
    private const long Me = 1;

    private static List<ServerBakePlanner.Compiler> Compilers(params ServerBakePlanner.Compiler[] compilers) =>
        new(compilers);

    private static ServerBakePlanner.Action Decide(bool generated, bool loadedHere,
        List<ServerBakePlanner.Compiler> compilers, int version = Version) =>
        ServerBakePlanner.Decide(version, generated, loadedHere, compilers, Me);

    [Fact]
    public void NoNetworkMeansNothingToWrite()
    {
        Assert.Equal(ServerBakePlanner.Action.NoNetwork, Decide(true, false, Compilers(), version: 0));
    }

    [Fact]
    public void AnUngeneratedZoneIsLeftToItsGeneration()
    {
        Assert.Equal(ServerBakePlanner.Action.LeaveToGeneration, Decide(false, false, Compilers()));
        Assert.Equal(ServerBakePlanner.Action.LeaveToGeneration,
            Decide(false, false, Compilers(new ServerBakePlanner.Compiler(0, 0, false))));
    }

    [Fact]
    public void AZoneLoadedHereIsLeftToTheLiveZoneHooks()
    {
        // Nothing saved yet: the zone-spawn hook makes its one compiler.
        Assert.Equal(ServerBakePlanner.Action.LeaveToLiveZone, Decide(true, true, Compilers()));
    }

    /// <summary>
    /// A loaded zone is not evidence its terrain was written. The live write is
    /// an Awake postfix that returns without writing when another peer owns the
    /// compiler, and no later ownership transfer makes Awake run again -- so a
    /// stale compiler in a loaded zone must stay pending, not be counted done.
    /// </summary>
    [Fact]
    public void AStaleCompilerInALoadedZoneIsNeverCountedAsDone()
    {
        // Owned by a peer standing there: wait for them, whether or not it is loaded here.
        Assert.Equal(ServerBakePlanner.Action.WaitForOwner,
            Decide(true, true, Compilers(new ServerBakePlanner.Compiler(0, 7, ownerActiveHere: true))));

        // Ours, or nobody's, and the zone is live: write through the live compiler
        // rather than raising a second one on a temporary terrain.
        Assert.Equal(ServerBakePlanner.Action.WriteLiveCompiler,
            Decide(true, true, Compilers(new ServerBakePlanner.Compiler(0, 0, false))));
        Assert.Equal(ServerBakePlanner.Action.WriteLiveCompiler,
            Decide(true, true, Compilers(new ServerBakePlanner.Compiler(Version - 1, Me, ownerActiveHere: true))));
        // Its owner has gone elsewhere: still ours to write, through the live one.
        Assert.Equal(ServerBakePlanner.Action.WriteLiveCompiler,
            Decide(true, true, Compilers(new ServerBakePlanner.Compiler(0, 7, ownerActiveHere: false))));

        // Already carrying this network: nothing to do, loaded or not.
        Assert.Equal(ServerBakePlanner.Action.AlreadyCurrent,
            Decide(true, true, Compilers(new ServerBakePlanner.Compiler(Version, 0, false))));
        // Two compilers are left alone in a loaded zone too.
        Assert.Equal(ServerBakePlanner.Action.DuplicateCompilers,
            Decide(true, true, Compilers(new ServerBakePlanner.Compiler(0, 0, false),
                new ServerBakePlanner.Compiler(0, 0, false))));
    }

    [Fact]
    public void AGeneratedZoneWithoutACompilerGetsItsOneCompiler()
    {
        Assert.Equal(ServerBakePlanner.Action.CreateCompiler, Decide(true, false, Compilers()));
    }

    [Fact]
    public void AStampedCompilerIsLeftAloneAndAnOldStampIsRewritten()
    {
        Assert.Equal(ServerBakePlanner.Action.AlreadyCurrent,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(Version, 0, false))));
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(Version - 1, 0, false))));
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(0, 0, false))));
    }

    [Fact]
    public void TwoSavedCompilersAreNeitherOfThemWritten()
    {
        Assert.Equal(ServerBakePlanner.Action.DuplicateCompilers,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(0, 0, false),
                new ServerBakePlanner.Compiler(0, 0, false))));
    }

    [Fact]
    public void APlayerInTheZoneKeepsTheirCompilerUntilTheyLeave()
    {
        // Owned by a connected peer standing in the zone: wait.
        Assert.Equal(ServerBakePlanner.Action.WaitForOwner,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(0, 7, ownerActiveHere: true))));
        // Their owner gone or elsewhere: write it.
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(0, 7, ownerActiveHere: false))));
        // Ours already: write it.
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(0, Me, ownerActiveHere: true))));
        // Already current is current whoever owns it.
        Assert.Equal(ServerBakePlanner.Action.AlreadyCurrent,
            Decide(true, false, Compilers(new ServerBakePlanner.Compiler(Version, 7, ownerActiveHere: true))));
    }

    [Fact]
    public void TheZonesWithRoadPointsAreExactlyTheZonesThatHaveThem()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        try
        {
            RoadSpatialGrid.Clear();
            // A 4 m diagonal road, and a 2 m road running along the border
            // between zone rows 0 and 1 (z = 32), so points sit at every kind
            // of zone edge.
            var diagonal = new List<Vector2>();
            for (float t = -150f; t <= 150f; t += 10f)
                diagonal.Add(new Vector2(t, t * 0.7f + 5f));
            RoadSpatialGrid.AddRoadPath(diagonal, 4f, world);
            var border = new List<Vector2>();
            for (float x = -100f; x <= 100f; x += 10f)
                border.Add(new Vector2(x, 32f));
            RoadSpatialGrid.AddRoadPath(border, 2f, world);
            RoadSpatialGrid.FinalizeRoadNetwork();

            HashSet<Vector2s> zones = RoadSpatialGrid.GetZonesWithRoadPoints();
            Assert.NotEmpty(zones);
            Assert.All(zones, z =>
            {
                Assert.InRange(z.x, -6, 6);
                Assert.InRange(z.y, -6, 6);
            });
            for (int y = -6; y <= 6; y++)
            {
                for (int x = -6; x <= 6; x++)
                {
                    var zone = new Vector2s(x, y);
                    Assert.Equal(RoadSpatialGrid.GetRoadPointsInZone(zone).Count > 0, zones.Contains(zone));
                }
            }
            // The border road lies in both rows.
            Assert.Contains(new Vector2s(0, 0), zones);
            Assert.Contains(new Vector2s(0, 1), zones);
        }
        finally
        {
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null;
        }
    }

    [Fact]
    public void AZoneWhoseCompilerWillNotSaveIsNotStamped()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        ZDOMan.instance = new ZDOMan();
        try
        {
            RoadSpatialGrid.Clear();
            var path = new List<Vector2>();
            for (float x = -40f; x <= 40f; x += 8f)
                path.Add(new Vector2(x, 0f));
            RoadSpatialGrid.AddRoadPath(path, 4f, world);
            RoadSpatialGrid.FinalizeRoadNetwork();
            var zone = new Vector2s(0, 0);
            List<RoadSpatialGrid.RoadPoint> points = RoadSpatialGrid.GetRoadPointsInZone(zone);
            Heightmap hm = Heightmap.CreateForZone(zone, 64);
            Heightmap.Registered = hm;
            TerrainComp tc = hm.m_terrainComp!;
            tc.m_nview.GetZDO().SetOwner(2); // another peer's

            // The explicit path writes the arrays whatever it is given; the
            // game refuses the save, and a stamp now would be a lie.
            RoadTerrainModifier.ApplyRoadTerrainModsWithContext(zone, points, hm, tc);
            Assert.Equal(0, tc.SaveCount);
            Assert.Null(tc.m_nview.GetZDO().GetByteArray(ZDOVars.s_TCData));
            Assert.False(RoadTerrainModifier.CarriesCurrentRoads(tc));

            // Once it is ours the same write saves, and only then is stamped.
            tc.m_nview.GetZDO().SetOwner(ZDOMan.instance.m_sessionID);
            RoadTerrainModifier.ApplyRoadTerrainModsWithContext(zone, points, hm, tc);
            Assert.Equal(1, tc.SaveCount);
            Assert.NotNull(tc.m_nview.GetZDO().GetByteArray(ZDOVars.s_TCData));
            Assert.True(RoadTerrainModifier.CarriesCurrentRoads(tc));
        }
        finally
        {
            Heightmap.Registered = null;
            RoadSpatialGrid.Clear();
            ZDOMan.instance = null;
            WorldGenerator.instance = null;
        }
    }

    /// <summary>
    /// A failed ghost write is the zone's only chance unless something keeps it:
    /// vanilla finishes generating it, so it never takes the new-zone path
    /// again, and the queue has already passed it by.
    /// </summary>
    [Fact]
    public void AZoneWhoseGhostWriteFailedIsWrittenFromTheQueueInstead()
    {
        var ledger = new GhostRepairLedger();
        var zone = new Vector2s(4, -9);
        ledger.Record(zone);
        Assert.True(ledger.Holds(zone));

        // Still generating: nothing to hand over yet, and it is not forgotten.
        Assert.Empty(ledger.TakeReady(_ => false));
        Assert.Equal(1, ledger.Count);

        // Generated: it goes to the queue, and stays watched until the write
        // actually lands -- being handed over is not the same as fixed.
        Assert.Equal(new[] { zone }, ledger.TakeReady(_ => true));
        Assert.True(ledger.Holds(zone));
        Assert.True(ledger.IsQueued(zone));

        // The queue wrote it: nothing left to watch.
        ledger.Succeeded(zone);
        Assert.False(ledger.Holds(zone));
        Assert.Empty(ledger.TakeReady(_ => true));
    }

    /// <summary>
    /// The repair budget belongs to failed WRITES, not to the clock. The timer
    /// polls once a second whatever the queue is doing, and a zone can sit in
    /// it through a backlog, a terrain build, or a ten-second wait for an owner
    /// to leave. Spending an attempt per poll burned the whole allowance
    /// without a single write being tried, and queued a fresh copy each time.
    /// </summary>
    [Fact]
    public void PollingSpendsNothingAndNeverQueuesTheZoneTwice()
    {
        var ledger = new GhostRepairLedger();
        var zone = new Vector2s(4, -9);
        ledger.Record(zone);

        int queued = 0;
        for (int poll = 1; poll <= 12; poll++)
            queued += ledger.TakeReady(_ => true).Count;

        Assert.Equal(1, queued);
        Assert.True(ledger.Holds(zone));
        Assert.Equal(0, ledger.FailedWrites(zone));
    }

    [Fact]
    public void OnlyAFailedWriteSpendsTheBudgetAndTheLastOneIsTerminal()
    {
        var ledger = new GhostRepairLedger();
        var zone = new Vector2s(1, 1);
        ledger.Record(zone);

        // Every round is a real write that failed, not a poll.
        for (int spent = 1; spent < GhostRepairLedger.MaxFailedWrites; spent++)
        {
            Assert.Equal(new[] { zone }, ledger.TakeReady(_ => true));
            Assert.False(ledger.FailedWrite(zone));
            Assert.Equal(spent, ledger.FailedWrites(zone));
            Assert.False(ledger.IsQueued(zone));
        }

        // The last failure is the one that gives up, and it says so.
        Assert.Equal(new[] { zone }, ledger.TakeReady(_ => true));
        Assert.True(ledger.FailedWrite(zone));
        Assert.False(ledger.Holds(zone));
        Assert.Empty(ledger.TakeReady(_ => true));
    }

    [Fact]
    public void AWriteThatLandsEndsTheWatchWhicheverPathWroteIt()
    {
        // The queue's temporary-terrain path: handed over, then written.
        var viaQueue = new GhostRepairLedger();
        var zone = new Vector2s(2, 2);
        viaQueue.Record(zone);
        viaQueue.TakeReady(_ => true);
        viaQueue.Succeeded(zone);
        Assert.Equal(0, viaQueue.Count);

        // The live-compiler path, and the nothing-to-write case: the zone is
        // finished without the ledger ever handing it anywhere.
        var viaLiveZone = new GhostRepairLedger();
        viaLiveZone.Record(zone);
        viaLiveZone.Succeeded(zone);
        Assert.Equal(0, viaLiveZone.Count);
    }

    [Theory]
    [InlineData(false, false, PeerAdmission.Verdict.WithoutMod, true)]
    [InlineData(false, true, PeerAdmission.Verdict.WithoutMod, true)]
    [InlineData(true, true, PeerAdmission.Verdict.SameVersion, true)]
    [InlineData(true, false, PeerAdmission.Verdict.VersionMismatch, false)]
    public void APeerWithoutTheModIsAdmittedAndAMismatchedOneIsNot(
        bool answered, bool matched, PeerAdmission.Verdict expected, bool admitted)
    {
        PeerAdmission.Verdict verdict = PeerAdmission.Decide(answered, matched);
        Assert.Equal(expected, verdict);
        Assert.Equal(admitted, PeerAdmission.Admits(verdict));
    }
}
