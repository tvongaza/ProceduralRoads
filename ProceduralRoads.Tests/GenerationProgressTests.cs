using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;

namespace ProceduralRoads.Tests;

public sealed class GenerationProgressTests : IDisposable
{
    // The real generator sees two disjoint islands. Only worker terrain reads are gated; preparation is real and completes.
    private sealed class Islands : WorldGenerator
    {
        internal readonly ManualResetEventSlim Entered = new(false), Release = new(false), RightRelease = new(false);
        internal readonly int MainThread = Thread.CurrentThread.ManagedThreadId;
        internal bool Gate, Fail, Reverse, HoldCancellation;
        internal int ActiveReads;
        public override float GetHeight(float x, float z)
        {
            if (Gate && Thread.CurrentThread.ManagedThreadId != MainThread)
            {
                Interlocked.Increment(ref ActiveReads);
                try
                {
                    Entered.Set();
                    var gate = Reverse && x > 0 ? RightRelease : Release;
                    while (!gate.Wait(10)) if(!HoldCancellation) RoadLifecycleManager.ThrowIfCancellationRequested();
                    if (Fail) throw new InvalidOperationException("controlled terrain failure");
                }
                finally { Interlocked.Decrement(ref ActiveReads); }
            }
            return Math.Abs(z) < 400 && (Math.Abs(x + 1800) < 400 || Math.Abs(x - 1800) < 400) ? 45f : -50f;
        }
        public override Heightmap.Biome GetBiome(float x, float z, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) =>
            Math.Abs(z) < 400 && (Math.Abs(x + 1800) < 400 || Math.Abs(x - 1800) < 400) ? Heightmap.Biome.Meadows : Heightmap.Biome.Ocean;
        public override void GetRiverWeight(float x, float z, out float weight, out float width) { weight = width = 0; }
    }
    private readonly ValheimWorldScope scope = new();
    private readonly Islands world = new();
    private readonly RoadNetworkOptions oldOptions = RoadNetworkGenerator.NetworkOptions;
    private readonly int oldPercentage = RoadNetworkGenerator.IslandRoadPercentage, oldWorkers = RoadParallel.Configured;
    public GenerationProgressTests()
    {
        scope.WithWorld(world).WithZdos().WithZoneSystem().WithNetwork(server: true);
        RoadNetworkGenerator.Reset();
        RoadNetworkGenerator.GenerateOnLoad = true;
        RoadNetworkGenerator.NetworkOptions = new RoadNetworkOptions { CoastalLandings = false };
        RoadNetworkGenerator.IslandRoadPercentage = 100;
        RoadParallel.Configured = 2;
        foreach (var place in new[] { ("StartTemple", -1800f, 0f), ("Crypt4", -1600f, 150f), ("Eikthyrnir", 1750f, 0f), ("Crypt4", 1950f, 150f) })
            TestLocations.Add(ZoneSystem.instance!, new ZoneSystem.LocationInstance
            { m_position = new Vector3(place.Item2, 45, place.Item3), m_location = new ZoneSystem.ZoneLocation
                { m_prefab = new SoftReferenceableAssets.SoftReference<GameObject>(place.Item1), m_exteriorRadius = 12 } });
        RoadLifecycleManager.OnZoneSystemStart(ZoneSystem.instance!);
    }
    private static void Begin()
    {
        ZoneSystem.instance!.LocationsGenerated = true;
        RoadLifecycleManager.OnWorldDataLoaded();
    }
    private static void Finish()
    {
        Assert.True(SpinWait.SpinUntil(() => { RoadLifecycleManager.Tick(); return !RoadLifecycleManager.IsGenerating; }, 10000), "generation did not settle");
    }
    public void Dispose()
    {
        RoadLifecycleManager.BeforeWorkerCompletionCheck = null;
        RoadLifecycleManager.ReturnToMenuForTests = null;
        RoadLifecycleManager.MenuTransitionBlockedForTests = null;
        world.Release.Set(); world.RightRelease.Set();
        RoadLifecycleManager.OnZoneSystemDestroy(ZoneSystem.instance!);
        RoadNetworkGenerator.NetworkOptions = oldOptions;
        RoadNetworkGenerator.IslandRoadPercentage = oldPercentage;
        RoadParallel.Configured = oldWorkers;
        RoadNetworkGenerator.GenerateOnLoad = true;
        scope.Dispose();
        world.Release.Dispose(); world.RightRelease.Dispose(); world.Entered.Dispose();
    }
    [Fact]
    public void CancelRequestTicksWithoutBlockingWhileWorkerDrainsThenDispatchesMenuOnce()
    {
        world.Gate=world.HoldCancellation=true;
        Begin(); RoadLifecycleManager.Tick(); Assert.True(world.Entered.Wait(3000));
        int menu=0; RoadLifecycleManager.ReturnToMenuForTests=()=>menu++;
        long run=RoadLifecycleManager.Progress.RunId;
        Assert.False(RoadLifecycleManager.RequestCancelToMenu(run-1));
        Assert.True(RoadLifecycleManager.RequestCancelToMenu(run));
        Assert.True(RoadLifecycleManager.RequestCancelToMenu(run));
        Assert.Equal("Stopping",RoadLifecycleManager.Progress.Phase);
        try
        {
            var tick=Task.Run(RoadLifecycleManager.Tick);
            Assert.True(tick.Wait(1000),"cancellation Tick blocked on live work");
            Assert.True(RoadLifecycleManager.InitialGenerationActive);
            Assert.Equal(0,menu); Assert.True(world.ActiveReads>0); Assert.True(LocationLevelling.Sealed);
            RoadLifecycleManager.OnPrepareSave(); Assert.Empty(ZDOMan.instance!.Zdos);
        }
        finally { world.Release.Set(); }
        Assert.True(SpinWait.SpinUntil(()=> { RoadLifecycleManager.Tick(); return menu==1; },5000));
        Assert.Equal(0,world.ActiveReads); Assert.False(LocationLevelling.Sealed);
        Assert.True(RoadLifecycleManager.IsGenerating); Assert.True(RoadLifecycleManager.WorkersOwnWorld);
        Assert.Equal("ReturningToMenu",RoadLifecycleManager.Progress.Phase);
        Assert.Empty(RoadLifecycleManager.Progress.Islands); Assert.Equal(0,RoadSpatialGrid.TotalRoadPoints);
        for(int i=0;i<5;i++) RoadLifecycleManager.Tick(); Assert.Equal(1,menu);
        RoadLifecycleManager.OnPrepareSave(); Assert.Empty(ZDOMan.instance!.Zdos);
        RoadLifecycleManager.CancelAndDrain(); // Game.Shutdown prefix precedes vanilla's final save.
        RoadLifecycleManager.OnPrepareSave();
        Assert.False(RoadLifecycleManager.IsGenerating); Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.Empty(ZDOMan.instance!.Zdos);
        world.Gate=false; RoadLifecycleManager.OnZoneSystemDestroy(ZoneSystem.instance!);
        RoadLifecycleManager.OnZoneSystemStart(ZoneSystem.instance!); Begin(); Finish();
        Assert.Equal("Complete",RoadLifecycleManager.Progress.Phase);
    }
    [Fact]
    public void AcceptedCancelWinsOverAlreadyCompletedUnpublishedWorker()
    {
        Begin(); RoadLifecycleManager.Tick();
        var run=typeof(RoadLifecycleManager).GetField("m_generation",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var worker=(Task)run.GetType().GetField("Worker",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(run)!;
        Assert.True(SpinWait.SpinUntil(()=>worker.IsCompleted,5000));
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        int menus=0; RoadLifecycleManager.ReturnToMenuForTests=()=>menus++;
        Assert.True(RoadLifecycleManager.RequestCancelToMenu(RoadLifecycleManager.Progress.RunId));
        RoadLifecycleManager.Tick();
        Assert.Equal(1,menus); Assert.False(RoadNetworkGenerator.RoadsAvailable); Assert.Empty(ZDOMan.instance!.Zdos);
        Assert.Equal(0,RoadSpatialGrid.TotalRoadPoints);
    }
    [Fact]
    public void CancelBeforePreparationAndMenuFailureKeepGateUntilExplicitRetry()
    {
        Begin(); long run=RoadLifecycleManager.Progress.RunId;
        int menus=0; RoadLifecycleManager.ReturnToMenuForTests=()=> { menus++; throw new InvalidOperationException("controlled menu failure"); };
        Assert.True(RoadLifecycleManager.RequestCancelToMenu(run)); RoadLifecycleManager.Tick();
        Assert.Equal("MenuFailed",RoadLifecycleManager.Progress.Phase);
        Assert.Equal(1,menus); Assert.True(RoadLifecycleManager.InitialGenerationActive);
        for(int i=0;i<5;i++) RoadLifecycleManager.Tick(); Assert.Equal(1,menus);
        RoadLifecycleManager.ReturnToMenuForTests=()=>menus++;
        Assert.True(RoadLifecycleManager.RequestCancelToMenu(run)); Assert.Equal(1,menus);
        RoadLifecycleManager.MenuTransitionBlockedForTests=()=>true;
        Assert.False(RoadLifecycleManager.RetryReturnToMenu(run)); Assert.Equal(1,menus);
        RoadLifecycleManager.MenuTransitionBlockedForTests=()=>false;
        Assert.True(RoadLifecycleManager.RetryReturnToMenu(run)); Assert.Equal(2,menus);
        Assert.Equal("ReturningToMenu",RoadLifecycleManager.Progress.Phase);
    }
    [Fact]
    public void DialogAppearingDuringDrainDefersInitialMenuDispatchUntilExplicitRetry()
    {
        Begin(); int menus=0;
        RoadLifecycleManager.ReturnToMenuForTests=()=>menus++;
        RoadLifecycleManager.MenuTransitionBlockedForTests=()=>true;
        long run=RoadLifecycleManager.Progress.RunId;
        Assert.True(RoadLifecycleManager.RequestCancelToMenu(run)); RoadLifecycleManager.Tick();
        Assert.Equal(0,menus); Assert.True(RoadLifecycleManager.InitialGenerationActive);
        Assert.Equal("MenuFailed",RoadLifecycleManager.Progress.Phase);
        Assert.False(RoadLifecycleManager.RetryReturnToMenu(run));
        RoadLifecycleManager.MenuTransitionBlockedForTests=()=>false;
        RoadLifecycleManager.Tick(); Assert.Equal(0,menus);
        Assert.True(RoadLifecycleManager.RetryReturnToMenu(run)); Assert.Equal(1,menus);
    }
    [Fact]
    public void CompletedNetworkCannotBeCancelledToMenu()
    {
        Begin(); Finish();
        Assert.False(RoadLifecycleManager.RequestCancelToMenu(RoadLifecycleManager.Progress.RunId));
        Assert.True(RoadNetworkGenerator.RoadsAvailable);
    }
    [Fact]
    public void BeginClaimsOnceAndFramesContinueWhileWorkersAreGated()
    {
        world.Gate = true;
        Begin();
        Assert.Equal("Preparing", RoadLifecycleManager.Progress.Phase);
        long run = RoadLifecycleManager.Progress.RunId;
        RoadLifecycleManager.OnWorldDataLoaded();
        RoadLifecycleManager.OnZoneSystemStart(ZoneSystem.instance!);
        Assert.Equal(run, RoadLifecycleManager.Progress.RunId);
        RoadLifecycleManager.Tick();
        Assert.True(world.Entered.Wait(3000));
        for (int frame = 0; frame < 20; frame++) RoadLifecycleManager.Tick();
        Assert.Equal("Generating", RoadLifecycleManager.Progress.Phase);
        Assert.Equal(0, RoadLifecycleManager.Progress.Completed);
        Assert.Equal(2, RoadLifecycleManager.Progress.Total);
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        RoadLifecycleManager.OnPrepareSave();
        Assert.Empty(ZDOMan.instance!.Zdos);
        Assert.Throws<InvalidOperationException>(() => RoadNetworkGenerator.GenerateRoads());
        Assert.Throws<InvalidOperationException>(() => ManualRoads.Prepare(new[] { new Vector2(0, 0), new Vector2(8, 0) }, false));
        world.Release.Set(); Finish();
        Assert.Equal("Complete", RoadLifecycleManager.Progress.Phase);
        Assert.Equal(2, RoadLifecycleManager.Progress.Completed);
        Assert.True(RoadNetworkGenerator.RoadsAvailable);
        int version = RoadSpatialGrid.RoadNetworkVersion;
        RoadLifecycleManager.Tick();
        Assert.Equal(version, RoadSpatialGrid.RoadNetworkVersion);
    }
    [Fact]
    public void CancellationDrainsBeforeResetAndCannotPublishIntoTheNextRun()
    {
        world.Gate = true;
        Begin(); RoadLifecycleManager.Tick(); Assert.True(world.Entered.Wait(3000));
        long run = RoadLifecycleManager.Progress.RunId;
        RoadNetworkGenerator.Reset();
        Assert.Equal(0, world.ActiveReads);
        Assert.Equal("Cancelled", RoadLifecycleManager.Progress.Phase);
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.Equal(0, RoadSpatialGrid.TotalRoadPoints);
        Assert.False(LocationLevelling.Sealed);
        world.Gate = false;
        Begin(); Finish();
        Assert.True(RoadLifecycleManager.Progress.RunId > run);
        Assert.Equal("Complete", RoadLifecycleManager.Progress.Phase);
        Assert.Equal(2, RoadLifecycleManager.Progress.Completed);
    }
    [Fact]
    public void WorkerFailureDiscardsPartialStateAndPublishesNoMetadata()
    {
        world.Gate = world.Fail = true;
        world.Release.Set();
        Begin(); Finish();
        Assert.Equal("Failed", RoadLifecycleManager.Progress.Phase);
        Assert.Contains("controlled terrain failure", RoadLifecycleManager.Progress.Failure!);
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.Equal(0, RoadSpatialGrid.TotalRoadPoints);
        Assert.Empty(ZDOMan.instance!.Zdos);
        Assert.Equal(0, world.ActiveReads);
        long failedRun = RoadLifecycleManager.Progress.RunId;
        RoadLifecycleManager.OnPlayerSpawn(Vector3.zero);
        RoadLifecycleManager.OnWorldDataLoaded();
        Assert.Equal(failedRun, RoadLifecycleManager.Progress.RunId);
        Assert.False(RoadLifecycleManager.IsGenerating);
    }
    [Fact]
    public void AsyncTwoIslandNetworkMatchesSerialBytes()
    {
        RoadParallel.Configured = 1;
        RoadNetworkGenerator.GenerateRoads();
        byte[] serial = RoadSpatialGrid.SerializeAllRoadPoints()!;
        int version = RoadSpatialGrid.RoadNetworkVersion;
        var starts = RoadNetworkGenerator.GetRoadStartPoints().Select(p => p.ToString()).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var crossings = RoadNetworkPersistence.SerializeRoadCrossings(RoadNetworkGenerator.GetRoadCrossings());
        Assert.True(RoadSpatialGrid.TotalRoadPoints > 0);
        RoadNetworkGenerator.Reset();
        RoadParallel.Configured = 2;
        Begin(); Finish();
        Assert.Equal(CanonicalRecords(serial), CanonicalRecords(RoadSpatialGrid.SerializeAllRoadPoints()!));
        Assert.Equal(2, RoadLifecycleManager.Progress.Total);
        Assert.Equal(version, RoadSpatialGrid.RoadNetworkVersion);
        Assert.Equal(starts, RoadNetworkGenerator.GetRoadStartPoints().Select(p => p.ToString()).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Equal(crossings, RoadNetworkPersistence.SerializeRoadCrossings(RoadNetworkGenerator.GetRoadCrossings()));
    }
    private static string[] CanonicalRecords(byte[] bytes)
    {
        using var input = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(2, input.ReadInt32());
        int cells = input.ReadInt32();
        var records = new List<string>();
        for (int c = 0; c < cells; c++)
        {
            int x = input.ReadInt32(), z = input.ReadInt32(), points = input.ReadInt32();
            for (int p = 0; p < points; p++)
                records.Add(x + ":" + z + ":" + Convert.ToBase64String(input.ReadBytes(17)));
        }
        Assert.Equal(input.BaseStream.Length, input.BaseStream.Position);
        return records.OrderBy(r => r, StringComparer.Ordinal).ToArray();
    }
    [Fact]
    public void LastCompletionPublishedBetweenObservationAndTaskCheckIsConsumed()
    {
        world.Gate = true;
        Begin(); RoadLifecycleManager.Tick(); Assert.True(world.Entered.Wait(3000));
        var run = typeof(RoadLifecycleManager).GetField("m_generation", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var worker = (Task)run.GetType().GetField("Worker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(run)!;
        RoadLifecycleManager.BeforeWorkerCompletionCheck = () =>
        {
            // Exactly the former race: the first drain has observed no completions, then the workers publish and exit.
            world.Release.Set();
            Assert.True(SpinWait.SpinUntil(() => worker.IsCompleted, 5000));
        };
        RoadLifecycleManager.Tick();
        RoadLifecycleManager.BeforeWorkerCompletionCheck = null;
        Assert.Equal("Complete", RoadLifecycleManager.Progress.Phase);
        Assert.Equal(2, RoadLifecycleManager.Progress.Completed);
    }
    [Fact]
    public void ApplicationFailureRetainsTheFinalNetworkAndPriorWorldSideEffects()
    {
        RoadNetworkGenerator.GenerateRoads();
        var zones = RoadSpatialGrid.GetZonesWithRoadPoints().Take(2).ToArray();
        Assert.Equal(2, zones.Length);
        RoadNetworkGenerator.Reset();
        scope.RegisterHeightmap(zones[0], 64, withCompiler: false);
        var broken = scope.RegisterHeightmap(zones[1], 64);
        broken.m_terrainComp!.m_nview = null!; // fails after the first loaded zone has created its compiler
        Begin(); Finish();
        Assert.Equal("ApplicationFailed", RoadLifecycleManager.Progress.Phase);
        Assert.True(RoadNetworkGenerator.RoadsAvailable);
        Assert.True(RoadSpatialGrid.TotalRoadPoints > 0);
        Assert.True(ZDOMan.instance!.CountWithPrefab(TerrainComp.PrefabName) >= 2);
        int version = RoadSpatialGrid.RoadNetworkVersion;
        RoadLifecycleManager.OnPrepareSave();
        Assert.Equal(version, RoadSpatialGrid.RoadNetworkVersion);
    }
    [Fact]
    public void ReverseIslandCompletionCountsFinishedWorkAndSnapshotsStayImmutable()
    {
        world.Gate = world.Reverse = true;
        Begin(); RoadLifecycleManager.Tick();
        Assert.True(world.Entered.Wait(3000));
        var initial = RoadLifecycleManager.Progress;
        world.RightRelease.Set();
        Assert.True(SpinWait.SpinUntil(() => { RoadLifecycleManager.Tick(); return RoadLifecycleManager.Progress.Completed == 1; }, 5000));
        var middle = RoadLifecycleManager.Progress;
        Assert.Equal(0, initial.Completed);
        Assert.Equal(2, middle.Total);
        Assert.True(RoadLifecycleManager.InitialGenerationActive);
        for (int frame = 0; frame < 10; frame++) RoadLifecycleManager.Tick();
        Assert.Equal(1, RoadLifecycleManager.Progress.Completed);
        world.Release.Set(); Finish();
        Assert.Equal(1, middle.Completed);
        Assert.Equal(2, RoadLifecycleManager.Progress.Completed);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisabledAndNonHostLoadsNeverClaimARun(bool enabled, bool server)
    {
        RoadNetworkGenerator.GenerateOnLoad = enabled;
        ZNet.instance!.Server = server;
        Begin();
        Assert.False(RoadLifecycleManager.IsGenerating);
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
    }
    [Fact]
    public void ZeroSelectedIslandsCompletesAsAnEmptyNetwork()
    {
        RoadNetworkGenerator.IslandRoadPercentage = 0;
        Begin(); Finish();
        Assert.Equal("Complete", RoadLifecycleManager.Progress.Phase);
        Assert.Equal(0, RoadLifecycleManager.Progress.Total);
        Assert.True(RoadNetworkGenerator.RoadsAvailable);
        Assert.Equal(0, RoadSpatialGrid.TotalRoadPoints);
    }
}
