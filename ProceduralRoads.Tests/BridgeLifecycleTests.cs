using System;
using System.Collections.Generic;
using System.Linq;
using ProceduralRoads;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;

namespace ProceduralRoads.Tests;

// Bridge append and respawn lifecycles through the real StructurePlacement and StructureAppendQueue on the multi-zone
// river crossing ManualBridgeTests uses. Only the scene is doubled (Valheim.Testing.Doubles): instantiating a
// networked prefab creates its ZDO, and ZDO destruction is queued until the test processes it, as in the game.
// Expectations are the queue's own debt taken before any placement, compared by piece key and multiplicity.
public sealed class BridgeLifecycleTests : IDisposable
{
    private static readonly int AppendKey = "ProceduralRoads_AppendPiece".GetStableHashCode();

    private readonly ValheimWorldScope _world = new ValheimWorldScope().WithWorld(new BridgeTests.WideRiverWorld()).WithZdos().WithZoneSystem().WithScene().AsServer();

    public BridgeLifecycleTests()
    {
        RoadNetworkGenerator.Reset(); RoadTerrainModifier.ResetForWorld();
        RoadNetworkGenerator.MarkLocationsReady(); RoadSiteProtection.Set(Array.Empty<RoadSiteProtection.Footprint>());
        RoadGrade.Configured = .35f; RoadNetworkGenerator.RoadWidth = 4;
        var island = new Island { Id = 1, CellSize = 8, WorldOffset = 0, Min = new Vector2(-216, -120), Max = new Vector2(216, 120) };
        for (int x = -27; x <= 27; x++) for (int z = -15; z <= 15; z++) island.Cells.Add(new Vector2Int(x, z));
        ManualRoads.Commit(ManualRoads.Prepare(new[] { new Vector2(-160, 0), new Vector2(160, 0) }, false, new[] { island }));
    }
    public void Dispose() { RoadNetworkGenerator.Reset(); _world.Dispose(); }

    // What the queue owes, by piece key, before anything is placed.
    private static Dictionary<string, int> Owed() => StructureAppendQueue.Zones.SelectMany(z => StructureAppendQueue.ForZone(z)!)
        .GroupBy(StructureAppendQueue.Key).ToDictionary(g => g.Key, g => g.Count());
    private static List<ZDO> Live() => ZDOMan.instance!.Zdos.Where(z => !ZDOMan.instance.DestroyQueue.Contains(z)).ToList();
    private static Dictionary<string, int> Emitted() => Live().Select(z => z.GetString(AppendKey)).Where(k => k.Length > 0)
        .GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
    private static void RegisterPrefabs(string? except = null)
    {
        foreach (var piece in StructureAppendQueue.Zones.SelectMany(z => StructureAppendQueue.ForZone(z)!))
            if (piece.Prefab != except && ZNetScene.instance!.GetPrefab(piece.Prefab) == null) ZNetScene.instance.AddPrefab(piece.Prefab);
    }
    // One zone (at most 64 pieces) per call, as the game's retry does; bounded so a stall cannot hang the test.
    private static int Drain(int limit)
    {
        int calls = 0;
        for (; calls < limit && StructureAppendQueue.Count > 0; calls++) StructurePlacement.ApplyPendingAppends();
        return calls;
    }
    // A world save and restart through the game's own hooks: OnPrepareSave saves in full after an edit, and only the
    // bridge zones and append queue for a loaded network; the load marks the network loaded, as the lifecycle's
    // decision does (without the mark, later saves of the queue are skipped).
    private static void Reload()
    {
        RoadLifecycleManager.OnPrepareSave(); RoadNetworkGenerator.Reset();
        Assert.True(RoadNetworkGenerator.TryLoadGlobalRoadData()); RoadNetworkGenerator.MarkRoadsLoadedFromZDO();
    }

    /// <summary>
    /// Pieces (bridges, staircases) never go into a zone explored before the network (NetworkCutoff): neither the
    /// append queue nor a zone spawn puts them there, and the zone is not marked spawned, while every other zone of
    /// the same structure gets its pieces.
    /// </summary>
    [Fact] public void AZoneExploredBeforeTheNetworkGetsNoPieces()
    {
        var zones = StructureAppendQueue.Zones.ToList(); RegisterPrefabs();
        Assert.True(zones.Count >= 2, "the fixture must append in more than one zone");
        Vector2s kept = zones[0];
        NetworkCutoff.Forget();
        try
        {
            NetworkCutoff.SnapshotWorld(new[] { kept });
            NetworkCutoff.UseNewNetwork();
            Drain(10 * zones.Count);
            Assert.Equal(0, StructurePlacement.OnZoneSpawned(kept, ZoneSystem.SpawnMode.Full));
            Assert.Equal(0, StructurePlacement.SpawnGhostInZone(kept));
            Assert.DoesNotContain(Live(), z => z.GetString(AppendKey).Length > 0 && z.GetSector() == kept);
            Assert.False(StructurePlans.IsSpawned(kept));
            Assert.Contains(Live(), z => z.GetString(AppendKey).Length > 0 && z.GetSector() != kept);
        }
        finally { NetworkCutoff.Forget(); }
    }

    /// <summary>
    /// A generated bridge that would put any piece in a zone explored before the network is built nowhere: no zone of
    /// it gets a piece, live or ghost, and none is marked spawned, so no half bridge stands. On a world with nothing
    /// explored the same bridge gets every planned piece.
    /// </summary>
    [Fact] public void AGeneratedBridgeReachingAnExploredZoneIsBuiltNowhere()
    {
        List<Vector2s> zones = GeneratedPlanZones();
        foreach (Vector2s zone in zones) _world.RegisterHeightmap(zone);   // loaded too, for SpawnInLoadedZones
        NetworkCutoff.Forget();
        try
        {
            NetworkCutoff.SnapshotWorld(new[] { zones[zones.Count - 1] });
            NetworkCutoff.UseNewNetwork();
            foreach (Vector2s zone in zones)
            {
                Assert.Equal(0, StructurePlacement.OnZoneSpawned(zone, ZoneSystem.SpawnMode.Full));
                Assert.Equal(0, StructurePlacement.SpawnGhostInZone(zone));
                Assert.False(StructurePlans.IsSpawned(zone));
            }
            Assert.Equal(0, StructurePlacement.SpawnInLoadedZones());
            Assert.DoesNotContain(Live(), z => z.GetInt(StructurePlans.MarkerHash) == 1);

            NetworkCutoff.Forget();
            NetworkCutoff.SnapshotWorld(Array.Empty<Vector2s>());
            NetworkCutoff.UseNewNetwork();
            Assert.Equal(StructurePlans.TotalPlannedPieces, zones.Sum(z => StructurePlacement.OnZoneSpawned(z, ZoneSystem.SpawnMode.Full)));
        }
        finally { NetworkCutoff.Forget(); }
    }

    // The fixture's crossing as a generated plan, not an append, with its prefabs registered.
    private static List<Vector2s> GeneratedPlanZones()
    {
        StructureAppendQueue.Reset();
        List<Vector2s> zones = StructurePlans.PlannedZones;
        Assert.True(zones.Count >= 2, "the fixture's bridge must span more than one zone");
        foreach (var piece in zones.SelectMany(z => StructurePlans.PlanFor(z)!))
            if (ZNetScene.instance!.GetPrefab(piece.Prefab) == null) ZNetScene.instance.AddPrefab(piece.Prefab);
        return zones;
    }

    /// <summary>
    /// road_generate or road_regen_island in a running session: the new network leaves every zone generated so far
    /// as it is, so the respawn that follows destroys none of the bridge pieces standing there and builds no second
    /// copy over them, though the zones are loaded.
    /// </summary>
    [Fact] public void ARespawnKeepsThePiecesInExploredZones()
    {
        List<Vector2s> zones = GeneratedPlanZones();
        foreach (Vector2s zone in zones) _world.RegisterHeightmap(zone);   // loaded, so the respawn can rebuild
        NetworkCutoff.Forget();
        try
        {
            NetworkCutoff.SnapshotWorld(Array.Empty<Vector2s>());
            NetworkCutoff.UseNewNetwork();
            foreach (Vector2s zone in zones) StructurePlacement.OnZoneSpawned(zone, ZoneSystem.SpawnMode.Full);
            var standing = Live().Where(z => z.GetInt(StructurePlans.MarkerHash) == 1).ToList();
            Assert.Equal(StructurePlans.TotalPlannedPieces, standing.Count);

            NetworkCutoff.Forget();
            NetworkCutoff.SnapshotWorld(zones);   // every zone of the bridge explored before the next network
            NetworkCutoff.UseNewNetwork();
            StructurePlacement.RespawnFromPlans();

            Assert.All(standing, z => Assert.DoesNotContain(z, ZDOMan.instance!.DestroyQueue));
            Assert.Equal(standing.Count, Live().Count(z => z.GetInt(StructurePlans.MarkerHash) == 1));

            // Control: with nothing explored the same respawn destroys the pieces and builds the plan again.
            NetworkCutoff.Forget();
            NetworkCutoff.SnapshotWorld(Array.Empty<Vector2s>());
            NetworkCutoff.UseNewNetwork();
            StructurePlacement.RespawnFromPlans();
            Assert.All(standing, z => Assert.Contains(z, ZDOMan.instance!.DestroyQueue));
            Assert.Equal(StructurePlans.TotalPlannedPieces, Live().Count(z => z.GetInt(StructurePlans.MarkerHash) == 1));
        }
        finally { NetworkCutoff.Forget(); }
    }

    [Fact] public void AMultiZoneAppendResumesAfterAReloadAndEmitsEachOwedPieceOnce()
    {
        var owed = Owed(); var zones = StructureAppendQueue.Zones.ToList();
        Assert.True(zones.Count >= 2, "the fixture must append in more than one zone");
        RegisterPrefabs();
        StructurePlacement.ApplyPendingAppends();
        int first = Emitted().Values.Sum();
        Assert.InRange(first, 1, 64); Assert.True(StructureAppendQueue.Count > 0, "a reload must interrupt unfinished work");
        Reload();
        Assert.True(Drain(10 * owed.Count) < 10 * owed.Count, "the queue must drain");
        Assert.Equal(owed.OrderBy(k => k.Key), Emitted().OrderBy(k => k.Key));
        foreach (var zone in zones) Assert.True(StructurePlans.IsSpawned(zone));
        Reload(); Assert.Equal(0, StructureAppendQueue.Count);
    }
    [Fact] public void ALostQueueSaveDoesNotDuplicatePiecesAlreadyEmitted()
    {
        var owed = Owed(); RegisterPrefabs();
        var lastSaved = StructureAppendQueue.Serialize();
        StructurePlacement.ApplyPendingAppends();
        Assert.NotEmpty(Emitted());
        // The acknowledgements never reached a save: the queue comes back owing pieces that already stand.
        StructureAppendQueue.Load(lastSaved);
        Drain(10 * owed.Count);
        Assert.Equal(0, StructureAppendQueue.Count);
        Assert.Equal(owed.OrderBy(k => k.Key), Emitted().OrderBy(k => k.Key));
    }
    [Fact] public void AFullRespawnMidAppendLeavesEveryPlannedPieceOnceAndStrandsNothing()
    {
        var zones = StructureAppendQueue.Zones.ToList(); RegisterPrefabs();
        foreach (var zone in zones) ZoneSystem.instance!.Generated.Add(zone);
        var planned = zones.ToDictionary(z => z, StructurePlans.PlannedPieceCount);
        StructurePlacement.ApplyPendingAppends();
        int partial = Emitted().Values.Sum(); Assert.True(partial > 0);
        var (destroyed, respawnedZones) = StructurePlacement.RespawnFromPlans();
        Assert.Equal(partial, destroyed); Assert.Equal(zones.Count, respawnedZones);
        Assert.Equal(partial, ZDOMan.instance!.ProcessDestroyed());
        Assert.Equal(0, StructureAppendQueue.Count); Assert.Empty(Emitted());
        foreach (var zone in zones)
            Assert.Equal(planned[zone], Live().Count(z => z.GetInt(StructurePlans.MarkerHash) == 1 && z.GetSector() == zone));
        // A second respawn replaces, never adds.
        StructurePlacement.RespawnFromPlans(); ZDOMan.instance.ProcessDestroyed();
        Assert.Equal(planned.Values.Sum(), Live().Count(z => z.GetInt(StructurePlans.MarkerHash) == 1));
    }
    [Fact] public void AMissingPrefabStallsOnlyTheZonesThatNeedItAndResumesWhenItAppears()
    {
        var owed = Owed();
        string missing = StructureAppendQueue.ForZone(StructureAppendQueue.Zones.First())![0].Prefab;
        var needing = StructureAppendQueue.Zones.Where(z => StructureAppendQueue.ForZone(z)!.Any(p => p.Prefab == missing)).ToHashSet();
        RegisterPrefabs(except: missing);
        Drain(10 * owed.Count);
        Assert.Equal(needing.OrderBy(z => z.x).ThenBy(z => z.y), StructureAppendQueue.Zones.OrderBy(z => z.x).ThenBy(z => z.y));
        ZNetScene.instance!.AddPrefab(missing);
        Drain(10 * owed.Count);
        Assert.Equal(0, StructureAppendQueue.Count);
        Assert.Equal(owed.OrderBy(k => k.Key), Emitted().OrderBy(k => k.Key));
    }
    [Fact] public void RepeatedOrUnknownAcknowledgementsChangeNothing()
    {
        var zone = StructureAppendQueue.Zones.First(z => StructureAppendQueue.ForZone(z)!.Count > 1);
        string key = StructureAppendQueue.Key(StructureAppendQueue.ForZone(zone)![0]);
        StructureAppendQueue.Acknowledge(zone, key); int left = StructureAppendQueue.ForZone(zone)!.Count;
        StructureAppendQueue.Acknowledge(zone, key); StructureAppendQueue.Acknowledge(zone, "not-a-piece");
        StructureAppendQueue.Acknowledge(new Vector2s(999, 999), key);
        Assert.Equal(left, StructureAppendQueue.ForZone(zone)!.Count);
        StructureAppendQueue.Complete(zone); StructureAppendQueue.Acknowledge(zone, key);
        Assert.Null(StructureAppendQueue.ForZone(zone)); Assert.True(StructurePlans.IsSpawned(zone));
    }
    // (d) An append into zones neither generated nor loaded: the server places the pieces as ZDOs, the restart keeps
    // what was done, and the zones' own generation later neither repeats nor loses a piece.
    [Fact] public void AppendsIntoUngeneratedZonesSurviveARestartAndAreNotRepeatedWhenTheZonesGenerate()
    {
        var owed = Owed(); var zones = StructureAppendQueue.Zones.ToList(); RegisterPrefabs();
        Assert.All(zones, z => Assert.False(ZoneSystem.instance!.IsZoneGenerated(z)));
        Assert.Empty(Heightmap.GetAllHeightmaps());
        Assert.True(Drain(10 * owed.Count) < 10 * owed.Count, "the queue must drain");
        Assert.Equal(owed.OrderBy(k => k.Key), Emitted().OrderBy(k => k.Key));
        Reload();
        foreach (var zone in zones)
        {
            ZoneSystem.instance!.Generated.Add(zone);
            Assert.Equal(0, StructurePlacement.OnZoneSpawned(zone, ZoneSystem.SpawnMode.Full));
        }
        Assert.Equal(owed.OrderBy(k => k.Key), Emitted().OrderBy(k => k.Key));
        Assert.Equal(owed.Values.Sum(), Live().Count(z => z.GetInt(StructurePlans.MarkerHash) == 1));
    }
    [Fact] public void PlacementDoesNothingOffTheServer()
    {
        var owed = Owed(); RegisterPrefabs(); ZNet.instance.Server = false;
        Drain(10);
        Assert.Empty(Emitted()); Assert.Equal(owed.Count, StructureAppendQueue.Zones.Sum(z => StructureAppendQueue.ForZone(z)!.Count));
    }
}
