using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The production lock (PROCEDURALROADS_LOCK_NETWORK, RoadNetworkLock): a
/// live server's saved road network is never regenerated or re-applied, even
/// by a later build that hashes it differently, lays bridges out differently
/// or cannot read its save.
///
/// Every locked path has a test here, and most have the unlocked control
/// beside it: a refusal proves nothing if the same setup would not have
/// written anyway. The flag is process-wide static state, so every test puts
/// it back (Dispose), whatever happened.
/// </summary>
public class RoadNetworkLockTests : IDisposable
{
    private sealed class FlatWorld : WorldGenerator
    {
        public override float GetHeight(float x, float z) => 40f;
        public override void GetRiverWeight(float x, float z, out float weight, out float width) { weight = 0; width = 0; }
    }

    private static readonly Vector2s Zone = new(0, 0);
    private const int Foreign = 12345;

    private static readonly int GlobalDataHash = "ProceduralRoads_GlobalData".GetStableHashCode();
    private static readonly int BridgeZonesHash = "ProceduralRoads_BridgeZones".GetStableHashCode();
    private static readonly int ClearedZonesHash = "ProceduralRoads_ClearedZones".GetStableHashCode();
    private static readonly int AppendBridgesHash = "ProceduralRoads_AppendBridges".GetStableHashCode();

    private readonly WorldGenerator m_world = new FlatWorld();
    private readonly List<string>? m_previousCapture;
    private readonly List<string> m_log = new();
    private ZoneSystem m_zones = null!;

    public RoadNetworkLockTests()
    {
        RoadNetworkLock.Enabled = false;
        m_previousCapture = ManualLogSource.Captured;
        ManualLogSource.Captured = m_log;
        SetWorldDataLoaded(false);
        RoadNetworkGenerator.Reset();
        RoadTerrainModifier.ResetDebugCounters();
        WorldGenerator.instance = m_world;
        ZDOMan.instance = new ZDOMan();
        m_zones = WorldWithAPlace();
        ZoneSystem.instance = m_zones;
        Heightmap.Registered = null;
        RoadGrade.Configured = .35f;
        RoadNetworkGenerator.RoadWidth = 4;
        RoadSiteProtection.Set(Array.Empty<RoadSiteProtection.Footprint>());
    }

    public void Dispose()
    {
        RoadNetworkLock.Enabled = false;
        RoadNetworkLock.ResetSession();
        ManualLogSource.Captured = m_previousCapture;
        SetWorldDataLoaded(false);
        RoadNetworkGenerator.Reset();
        RoadTerrainModifier.ResetDebugCounters();
        WorldGenerator.instance = null;
        ZDOMan.instance = null;
        ZoneSystem.instance = null;
        Heightmap.Registered = null;
    }

    // ---- fixtures ----

    private static void SetWorldDataLoaded(bool value) =>
        typeof(RoadLifecycleManager)
            .GetField("m_worldDataLoaded", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, value);

    private static ZoneSystem WorldWithAPlace()
    {
        ZoneSystem zones = new();
        zones.Locations.Add(new ZoneSystem.LocationInstance
        {
            m_position = new Vector3(0f, 0f, 400f),
            m_location = new ZoneSystem.ZoneLocation
            {
                m_prefab = new ZoneSystem.ZoneLocation.PrefabEntry { Name = "StartTemple" },
                m_exteriorRadius = 16f,
            },
        });
        return zones;
    }

    private RoadSpatialGrid.PlannedPath Line(float z, float start = -40, float end = 40)
    {
        var plan = RoadSpatialGrid.PlanRoadPath(new List<Vector2> { new(start, z), new(end, z) }, 4, m_world);
        Assert.NotNull(plan);
        return plan!;
    }

    private static Island Group()
    {
        var island = new Island { Id = 1, CellSize = 8, WorldOffset = 0, Min = new Vector2(-512, -512), Max = new Vector2(512, 512) };
        for (int x = -64; x <= 64; x++)
            for (int z = -64; z <= 64; z++)
                island.Cells.Add(new Vector2Int(x, z));
        return island;
    }

    /// <summary>
    /// A previous session's network, saved through the real save path, and the
    /// session then started over with that world on "disk": its ZDOs stay in
    /// ZDOMan, everything in memory is gone. Returns the saved version.
    /// </summary>
    private int SaveANetwork(IReadOnlyCollection<Vector2s>? bridgeZones = null)
    {
        RoadSpatialGrid.Commit(Line(0f));
        RoadSpatialGrid.FinalizeRoadNetwork();
        int version = RoadSpatialGrid.RoadNetworkVersion;
        RoadNetworkPersistence.EnsureMetadataInstance();
        RoadNetworkPersistence.SaveGlobalRoadData(new List<(Vector2, string)> { (new Vector2(-40, 0), "saved") },
            new List<RoadCrossing>(), bridgeZones ?? new HashSet<Vector2s>());
        StartOver();
        return version;
    }

    private static void StartOver()
    {
        SetWorldDataLoaded(false);
        RoadNetworkPersistence.Reset();
        RoadNetworkGenerator.Reset();
        RoadTerrainModifier.ResetDebugCounters();
    }

    private static ZDO Metadata() =>
        ZDOMan.instance!.Zdos.Single(z => z.GetPrefab() == RoadNetworkPersistence.MetadataPrefabName.GetStableHashCode());

    /// <summary>The game's order: the mod subscribes, locations are ready, the world's data arrives.</summary>
    private void Decide()
    {
        RoadLifecycleManager.OnZoneSystemStart(m_zones);
        m_zones.LocationsGenerated = true;
        RoadLifecycleManager.OnWorldDataLoaded();
    }

    private static byte[] BridgeRecord(int format, int layout, params Vector2s[] zones)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(format);
        if (format == 2) writer.Write(layout);
        writer.Write(zones.Length);
        foreach (var zone in zones) { writer.Write((int)zone.x); writer.Write((int)zone.y); }
        return ms.ToArray();
    }

    private static byte[] ClearedRecord(int version, params Vector2s[] zones)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(1);
        writer.Write(version);
        writer.Write(zones.Length);
        foreach (var zone in zones) { writer.Write((int)zone.x); writer.Write((int)zone.y); }
        return ms.ToArray();
    }

    private static Heightmap ZoneWithCompiler(int stamp = 0, bool autoRebuild = true)
    {
        Heightmap hm = Heightmap.CreateForZone(Zone, 64);
        hm.AutoRebuild = autoRebuild;
        Heightmap.Registered = hm;
        if (stamp != 0)
            hm.m_terrainComp!.m_nview.GetZDO().Set(RoadTerrainModifier.AppliedVersionHash, stamp);
        return hm;
    }

    private static int Stamp(TerrainComp tc) => tc.m_nview.GetZDO().GetInt(RoadTerrainModifier.AppliedVersionHash, 0);

    private int Lines(string fragment) => m_log.Count(l => l.Contains(fragment));

    /// <summary>A saved network loaded with the lock on.</summary>
    private int LoadLocked()
    {
        int version = SaveANetwork();
        RoadNetworkLock.Enabled = true;
        Decide();
        Assert.True(RoadNetworkGenerator.RoadsLoadedFromZDO, "the saved network did not load");
        Assert.Equal(version, RoadSpatialGrid.RoadNetworkVersion);
        Assert.NotEqual(Foreign, version);
        return version;
    }

    // ---- the switch ----

    [Fact]
    public void TheSwitchIsReadFromProceduralRoadsLockNetworkAndIsOffByDefault()
    {
        const string variable = "PROCEDURALROADS_LOCK_NETWORK";
        string? before = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            Assert.False(DebugSwitches.Flag("LOCK_NETWORK", false));
            Environment.SetEnvironmentVariable(variable, "1");
            Assert.True(DebugSwitches.Flag("LOCK_NETWORK", false));
        }
        finally { Environment.SetEnvironmentVariable(variable, before); }
    }

    // ---- load ----

    [Fact]
    public void ALockedWorldLoadsItsSavedNetworkAndSaysSo()
    {
        int version = LoadLocked();

        Assert.False(RoadNetworkGenerator.RoadsGenerated);
        Assert.False(RoadNetworkLock.RoadsDisabled);
        string line = Assert.Single(m_log, l => l.StartsWith("[LOCK] road network locked:"));
        Assert.Contains($"version={version}", line);
        Assert.Contains($"points={RoadSpatialGrid.TotalRoadPoints}", line);
        Assert.Contains("bridgesFrozen=False", line);
        Assert.Equal($"[LOCK] selftest PASS network={version} points={RoadSpatialGrid.TotalRoadPoints} bridges=0",
            Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest")));

        // Later triggers do not decide, or report, again.
        RoadLifecycleManager.OnPlayerSpawn(Vector3.zero);
        RoadLifecycleManager.OnWorldDataLoaded();
        Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest"));
    }

    [Fact]
    public void TheSelftestCountsTheBridgesOfTheLoadedNetwork()
    {
        RoadSpatialGrid.Commit(Line(0f));
        RoadSpatialGrid.FinalizeRoadNetwork();
        RoadNetworkPersistence.EnsureMetadataInstance();
        var crossings = new List<RoadCrossing>
        {
            RoadCrossing.Between(new Vector2(0, 100), new Vector2(40, 100), 20f, new Vector2(20, 100), 10f, CrossingKind.Bridge, FordStyle.None),
            RoadCrossing.Between(new Vector2(0, 200), new Vector2(10, 200), 28f, new Vector2(5, 200), 4f, CrossingKind.Ford, FordStyle.Wade),
        };
        RoadNetworkPersistence.SaveGlobalRoadData(new List<(Vector2, string)>(), crossings, new HashSet<Vector2s>());
        StartOver();
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.EndsWith(" bridges=1", Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest PASS")));
        Assert.Contains("crossings=2", Assert.Single(m_log, l => l.StartsWith("[LOCK] road network locked:")));
    }

    [Fact]
    public void UnlockedNoSelftestLineIsLogged()
    {
        SaveANetwork();
        Decide();
        Assert.True(RoadNetworkGenerator.RoadsLoadedFromZDO);
        Assert.Equal(0, Lines("[LOCK]"));
    }

    [Fact]
    public void ALockedWorldWithNoSavedNetworkGeneratesNothingAndStaysWithoutRoads()
    {
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.False(RoadNetworkGenerator.RoadsGenerated, "a network was generated in place of the locked one");
        Assert.False(RoadSpatialGrid.IsInitialized);
        Assert.True(RoadNetworkLock.RoadsDisabled);
        string line = Assert.Single(m_log, l => l.StartsWith("[LOCK] refused road generation"));
        Assert.Contains("no ProceduralRoads_Metadata object", line);
        Assert.Equal("[LOCK] selftest FAIL no ProceduralRoads_Metadata object in the world",
            Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest")));
        Assert.Null(ZDOMan.instance!.Zdos.FirstOrDefault(z =>
            z.GetPrefab() == RoadNetworkPersistence.MetadataPrefabName.GetStableHashCode()));

        // Decided for the session: a later trigger does not try again, even
        // if a network has turned up since.
        SaveANetworkWithoutStartingOver();
        RoadLifecycleManager.OnPlayerSpawn(Vector3.zero);
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.False(RoadSpatialGrid.IsInitialized);
        Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest"));
    }

    private void SaveANetworkWithoutStartingOver()
    {
        RoadSpatialGrid.Commit(Line(0f));
        RoadSpatialGrid.FinalizeRoadNetwork();
        RoadNetworkPersistence.EnsureMetadataInstance();
        RoadNetworkPersistence.SaveGlobalRoadData(new List<(Vector2, string)>(), new List<RoadCrossing>(), new HashSet<Vector2s>());
        RoadSpatialGrid.Clear();
    }

    [Fact]
    public void MetadataWithoutRoadDataIsAFailedLoadNotAFreshWorld()
    {
        RoadNetworkPersistence.EnsureMetadataInstance();
        StartOver();
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.True(RoadNetworkLock.RoadsDisabled);
        Assert.Contains("holds no road data", Assert.Single(m_log, l => l.StartsWith("[LOCK] refused road generation")));
        Assert.Equal("[LOCK] selftest FAIL the road metadata holds no road data",
            Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest")));
    }

    [Fact]
    public void AnUnreadableSaveIsLeftAloneAndNothingIsGenerated()
    {
        SaveANetwork();
        byte[] unreadable = { 99, 0, 0, 0, 1, 2, 3 };
        Metadata().Set(GlobalDataHash, unreadable);
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.Contains("could not be read", Assert.Single(m_log, l => l.StartsWith("[LOCK] refused road generation")));
        Assert.StartsWith("[LOCK] selftest FAIL the saved road data (7 bytes) could not be read",
            Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest")));

        // Nothing is saved over it on the next world save.
        RoadLifecycleManager.OnPrepareSave();
        Assert.Equal(unreadable, Metadata().GetByteArray(GlobalDataHash));
    }

    [Fact]
    public void ALoadThatThrowsPartwayLeavesNoHalfNetworkBehind()
    {
        // The road points load, then the pending-bridge record throws. The
        // grid already holds the network at that moment: without clearing it
        // the manual-road retry (which looks only at the grid) would write it.
        SaveANetwork();
        Metadata().Set(AppendBridgesHash, new byte[] { 99, 0, 0, 0 });
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.True(RoadNetworkLock.RoadsDisabled);
        Assert.Contains("threw InvalidDataException", Assert.Single(m_log, l => l.StartsWith("[LOCK] refused road generation")));
        Assert.StartsWith("[LOCK] selftest FAIL the load threw InvalidDataException",
            Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest")));
        Assert.False(RoadNetworkGenerator.RoadsAvailable);
        Assert.False(RoadSpatialGrid.IsInitialized);
        Assert.Equal(0, RoadSpatialGrid.TotalRoadPoints);
        Assert.Equal(0, RoadSpatialGrid.AppendCount);
        Assert.Empty(RoadNetworkGenerator.GetRoadStartPoints());

        Heightmap hm = ZoneWithCompiler();
        RoadTerrainModifier.OnTerrainCompilerReady(hm.m_terrainComp!);
        RoadTerrainModifier.ApplyAdditionsToLoadedZones();
        Assert.Equal(0, hm.m_terrainComp!.SaveCount);
    }

    [Fact]
    public void WithRoadsDisabledNoTerrainIsWrittenEvenIfPointsAppear()
    {
        RoadNetworkLock.Enabled = true;
        Decide();
        Assert.True(RoadNetworkLock.RoadsDisabled);
        Assert.True(RoadNetworkLock.RefusesTerrain(Zone, 0, "test"));

        // Whatever put them there, the writer itself refuses.
        RoadSpatialGrid.Commit(Line(0f));
        RoadSpatialGrid.FinalizeRoadNetwork();
        Heightmap hm = ZoneWithCompiler();
        RoadTerrainModifier.OnTerrainCompilerReady(hm.m_terrainComp!);
        RoadTerrainModifier.OnZoneSpawned(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone));
        Assert.Equal(0, RoadTerrainModifier.SweepUnstamped());
        Assert.Equal(0, hm.m_terrainComp!.SaveCount);
        Assert.Equal(0, Stamp(hm.m_terrainComp));
    }

    // ---- regeneration ----

    [Fact]
    public void ForcedGenerationOverALockedNetworkIsRefused()
    {
        int version = LoadLocked();
        byte[]? before = RoadSpatialGrid.SerializeAllRoadPoints();

        RoadNetworkGenerator.GenerateRoads(force: true);

        Assert.False(RoadNetworkGenerator.RoadsGenerated);
        Assert.True(RoadNetworkGenerator.RoadsLoadedFromZDO);
        Assert.Equal(version, RoadSpatialGrid.RoadNetworkVersion);
        Assert.Equal(before, RoadSpatialGrid.SerializeAllRoadPoints());
        Assert.Contains("force=True", Assert.Single(m_log, l => l.StartsWith("[LOCK] refused road generation (GenerateRoads")));
    }

    [Fact]
    public void GenerationWithNoNetworkIsRefusedUnderTheLock()
    {
        RoadNetworkLock.Enabled = true;
        RoadNetworkGenerator.GenerateRoads();
        Assert.False(RoadNetworkGenerator.RoadsGenerated);
        Assert.False(RoadNetworkGenerator.GenerateRoadsOnLoad());
        Assert.False(RoadSpatialGrid.IsInitialized);
        Assert.Equal(1, Lines("[LOCK] refused road generation (GenerateRoads"));
        Assert.Equal(1, Lines("[LOCK] refused road generation on load"));
    }

    [Fact]
    public void IslandRegenerationIsRefusedUnderTheLock()
    {
        int version = LoadLocked();
        Assert.False(RoadNetworkGenerator.RegenerateIslandAt(Vector3.zero, out string summary));
        Assert.StartsWith("[LOCK] refused island regeneration", summary);
        Assert.True(RoadNetworkGenerator.RoadsLoadedFromZDO);
        Assert.Equal(version, RoadSpatialGrid.RoadNetworkVersion);
    }

    [Fact]
    public void TheRegenerationCommandsAreRefusedOnlyUnderTheLock()
    {
        // road_generate, road_regen_island and road_bridges respawn print this
        // and return before doing anything.
        foreach (string command in new[] { "road_generate", "road_regen_island", "road_bridges respawn" })
            Assert.Null(RoadNetworkLock.RefuseRegeneration(command));
        Assert.Equal(0, Lines("[LOCK]"));

        RoadNetworkLock.Enabled = true;
        foreach (string command in new[] { "road_generate", "road_regen_island", "road_bridges respawn" })
        {
            string? refused = RoadNetworkLock.RefuseRegeneration(command);
            Assert.NotNull(refused);
            Assert.StartsWith($"[LOCK] refused {command}:", refused);
            Assert.Equal(1, Lines($"[LOCK] refused {command}:"));
        }
    }

    // ---- manual additions ----

    [Fact]
    public void AManualRoadIsStillAppendedUnderTheLockAndKeepsTheNetwork()
    {
        int version = LoadLocked();
        int points = RoadSpatialGrid.TotalRoadPoints;
        var before = RoadSpatialGrid.GetRoadPointsInZone(Zone).Select(p => (p.p, p.h)).ToList();

        var plan = ManualRoads.Prepare(new[] { new Vector2(-40, 80), new Vector2(40, 80) }, false, new[] { Group() });
        ManualRoads.Commit(plan);

        Assert.Equal(1, RoadSpatialGrid.AppendCount);
        Assert.True(RoadSpatialGrid.IsAppendAncestor(version), "the loaded network is not the append's ancestor");
        Assert.True(RoadSpatialGrid.TotalRoadPoints > points);
        var after = RoadSpatialGrid.GetRoadPointsInZone(Zone).Select(p => (p.p, p.h)).ToList();
        foreach (var point in before)
            Assert.Contains(point, after);
        Assert.Equal(0, Lines("[LOCK] refused"));
    }

    [Fact]
    public void AManualRoadIsRefusedWhenTheLockedNetworkDidNotLoad()
    {
        RoadNetworkLock.Enabled = true;
        Decide();
        var refused = Assert.Throws<InvalidOperationException>(() =>
            ManualRoads.Prepare(new[] { new Vector2(-40, 80), new Vector2(40, 80) }, false, new[] { Group() }));
        Assert.StartsWith("[LOCK] refused manual road", refused.Message);
        Assert.False(RoadSpatialGrid.IsInitialized);
        Assert.False(ManualRoads.Busy);
    }

    [Fact]
    public void AManualRoadPlannedBeforeRoadsWereDisabledIsNotCommitted()
    {
        RoadNetworkLock.Enabled = true;
        RoadNetworkGenerator.MarkLocationsReady();
        var plan = ManualRoads.Prepare(new[] { new Vector2(-40, 80), new Vector2(40, 80) }, false, new[] { Group() });
        RoadNetworkLock.DisableRoads("test");
        var refused = Assert.Throws<InvalidOperationException>(() => ManualRoads.Commit(plan));
        Assert.StartsWith("[LOCK] refused manual road", refused.Message);
        Assert.False(RoadSpatialGrid.IsInitialized);
        Assert.False(RoadNetworkGenerator.RoadsGenerated);
    }

    // ---- terrain ----

    [Fact]
    public void AZoneCarryingAnotherNetworkIsNeverWrittenUnderTheLock()
    {
        LoadLocked();
        Heightmap hm = ZoneWithCompiler(stamp: Foreign);
        TerrainComp tc = hm.m_terrainComp!;
        tc.m_nview.GetZDO().SetOwner(0);   // released: unlocked, this would be claimed

        RoadTerrainModifier.OnTerrainCompilerReady(tc);
        RoadTerrainModifier.OnTerrainCompilerReady(tc);
        RoadTerrainModifier.OnZoneSpawned(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone));

        Assert.Equal(0, tc.SaveCount);
        Assert.Equal(Foreign, Stamp(tc));
        Assert.Equal(0L, tc.m_nview.GetZDO().GetOwner());
        Assert.All(tc.m_levelDelta, d => Assert.Equal(0f, d));
        Assert.Equal(1, Lines($"[LOCK] refused terrain write to zone {Zone}"));
        Assert.True(RoadNetworkLock.IsRefusedZone(Zone));
        Assert.Equal(0, hm.PokeCount);   // not even queued for a rebuild

        // Owned by us, the zone-spawn path reaches the queue itself: refused there too.
        tc.m_nview.GetZDO().SetOwner(ZDOMan.instance!.m_sessionID);
        RoadTerrainModifier.OnZoneSpawned(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone));
        RoadTerrainModifier.ApplyRoadTerrainMods(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone));
        Assert.Equal(0, hm.PokeCount);
        Assert.Equal(0, tc.SaveCount);
        Assert.Equal(Foreign, Stamp(tc));
    }

    [Fact]
    public void UnlockedTheSameZoneIsWrittenOverThatIsWhatTheLockPrevents()
    {
        SaveANetwork();
        Decide();
        Heightmap hm = ZoneWithCompiler(stamp: Foreign);

        RoadTerrainModifier.OnTerrainCompilerReady(hm.m_terrainComp!);

        Assert.Equal(1, hm.m_terrainComp!.SaveCount);
        Assert.Equal(RoadSpatialGrid.RoadNetworkVersion, Stamp(hm.m_terrainComp));
        Assert.Equal(0, Lines("[LOCK]"));
    }

    [Fact]
    public void AZoneNeverVisitedStillGetsItsRoadsUnderTheLock()
    {
        int version = LoadLocked();
        Heightmap hm = ZoneWithCompiler();

        RoadTerrainModifier.OnTerrainCompilerReady(hm.m_terrainComp!);

        Assert.Equal(1, hm.m_terrainComp!.SaveCount);
        Assert.Equal(version, Stamp(hm.m_terrainComp));
        Assert.Equal(0, Lines("[LOCK] refused"));
    }

    [Fact]
    public void AZoneCarryingAnAppendAncestorGetsOnlyTheNewRoadUnderTheLock()
    {
        int version = LoadLocked();
        Heightmap hm = ZoneWithCompiler();
        TerrainComp tc = hm.m_terrainComp!;
        RoadTerrainModifier.OnTerrainCompilerReady(tc);
        Assert.Equal(version, Stamp(tc));
        float onOldRoad = tc.m_levelDelta[32 * 65 + 32];

        RoadSpatialGrid.CommitAppend(new[] { Line(16f) }, version);
        Assert.True(RoadSpatialGrid.IsAppendAncestor(version));
        Assert.False(RoadNetworkLock.IsForeignStamp(version));

        RoadTerrainModifier.OnTerrainCompilerReady(tc);

        Assert.Equal(2, tc.SaveCount);
        Assert.Equal(RoadSpatialGrid.RoadNetworkVersion, Stamp(tc));
        Assert.Equal(onOldRoad, tc.m_levelDelta[32 * 65 + 32]);
        Assert.Equal(0, Lines("[LOCK] refused"));
    }

    [Fact]
    public void AForcedReapplicationIsDemotedUnderTheLock()
    {
        LoadLocked();
        Heightmap hm = ZoneWithCompiler();
        TerrainComp tc = hm.m_terrainComp!;
        RoadTerrainModifier.OnTerrainCompilerReady(tc);
        Assert.Equal(1, tc.SaveCount);

        RoadTerrainModifier.ApplyRoadTerrainModsWithContext(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone), hm, tc);
        RoadTerrainModifier.ApplyRoadTerrainMods(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone), force: true);

        Assert.Equal(1, tc.SaveCount);
        Assert.Equal(1, Lines($"[LOCK] refused forced re-application of road terrain in zone {Zone}"));
    }

    [Fact]
    public void UnlockedAForcedReapplicationWritesAgain()
    {
        SaveANetwork();
        Decide();
        Heightmap hm = ZoneWithCompiler();
        TerrainComp tc = hm.m_terrainComp!;
        RoadTerrainModifier.OnTerrainCompilerReady(tc);

        RoadTerrainModifier.ApplyRoadTerrainModsWithContext(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone), hm, tc);

        Assert.Equal(2, tc.SaveCount);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void AForcedWriteWaitingForASavedCompilerIsDemotedUnderTheLock(bool locked, int writes)
    {
        // ApplyToLoadedZones remembers a zone whose saved compiler is not alive
        // yet, and forces the write when it comes alive.
        SaveANetwork();
        RoadNetworkLock.Enabled = locked;
        Decide();
        int version = RoadSpatialGrid.RoadNetworkVersion;
        Heightmap hm = Heightmap.CreateForZone(Zone, 64, withCompiler: false);
        Heightmap.Registered = hm;
        ZDOMan.instance!.CreateNewZDO(ZoneSystem.GetZonePos(Zone), TerrainComp.PrefabName.GetStableHashCode());

        Assert.Equal(1, RoadTerrainModifier.ApplyToLoadedZones());
        hm.m_terrainComp = new TerrainComp(hm, 64);
        hm.m_terrainComp.m_nview.GetZDO().Set(RoadTerrainModifier.AppliedVersionHash, version);
        hm.m_terrainComp.m_nview.GetZDO().SetOwner(0);
        RoadTerrainModifier.OnTerrainCompilerReady(hm.m_terrainComp);

        Assert.Equal(writes, hm.m_terrainComp.SaveCount);
        // Locked, a compiler it is not going to write is not claimed either.
        Assert.Equal(locked ? 0L : ZDOMan.instance.m_sessionID, hm.m_terrainComp.m_nview.GetZDO().GetOwner());
    }

    [Fact]
    public void AForcedWriteQueuedBeforeTheLockIsDemotedAtTheLastDoor()
    {
        SaveANetwork();
        Decide();
        Heightmap hm = ZoneWithCompiler(autoRebuild: false);
        TerrainComp tc = hm.m_terrainComp!;
        RoadTerrainModifier.OnTerrainCompilerReady(tc);
        hm.RebuildTerrain();
        Assert.Equal(1, tc.SaveCount);

        RoadTerrainModifier.ApplyRoadTerrainModsWithContext(Zone, RoadSpatialGrid.GetRoadPointsInZone(Zone), hm, tc);
        RoadNetworkLock.Enabled = true;
        hm.RebuildTerrain();

        Assert.Equal(1, tc.SaveCount);
        Assert.Equal(1, Lines($"[LOCK] refused forced re-application of road terrain in zone {Zone}"));
    }

    [Fact]
    public void TheSweepNeitherClaimsNorWritesAZoneTheLockRefuses()
    {
        LoadLocked();
        Heightmap hm = ZoneWithCompiler(stamp: Foreign);
        hm.m_terrainComp!.m_nview.GetZDO().SetOwner(0);

        Assert.Equal(0, RoadTerrainModifier.SweepUnstamped());
        Assert.Equal(0, RoadTerrainModifier.SweepUnstamped());

        Assert.Equal(0, hm.m_terrainComp.SaveCount);
        Assert.Equal(0L, hm.m_terrainComp.m_nview.GetZDO().GetOwner());
        Assert.Equal(1, Lines("[LOCK] refused terrain write"));
    }

    [Fact]
    public void UnlockedTheSweepClaimsAndWritesThatZone()
    {
        SaveANetwork();
        Decide();
        Heightmap hm = ZoneWithCompiler(stamp: Foreign);
        hm.m_terrainComp!.m_nview.GetZDO().SetOwner(0);

        Assert.Equal(1, RoadTerrainModifier.SweepUnstamped());
        Assert.Equal(1, hm.m_terrainComp.SaveCount);
    }

    [Fact]
    public void TheLastDoorBeforeTheCompilerRefusesAWriteQueuedBeforeTheStampChanged()
    {
        LoadLocked();
        Heightmap hm = ZoneWithCompiler(autoRebuild: false);
        TerrainComp tc = hm.m_terrainComp!;
        RoadTerrainModifier.OnTerrainCompilerReady(tc);
        Assert.Equal(1, RoadTerrainModifier.PendingWriteCount);

        tc.m_nview.GetZDO().Set(RoadTerrainModifier.AppliedVersionHash, Foreign);
        hm.RebuildTerrain();

        Assert.Equal(RoadTerrainModifier.WriteOutcome.RefusedByLock, RoadTerrainModifier.LastWriteOutcome);
        Assert.Equal(0, RoadTerrainModifier.PendingWriteCount);
        Assert.Equal(0, tc.SaveCount);
        Assert.Equal(Foreign, Stamp(tc));
    }

    [Fact]
    public void AForeignStampIsNeitherZeroTheNetworkNorAnAncestor()
    {
        int version = LoadLocked();
        Assert.False(RoadNetworkLock.IsForeignStamp(0));
        Assert.False(RoadNetworkLock.IsForeignStamp(version));
        Assert.True(RoadNetworkLock.IsForeignStamp(Foreign));
        RoadSpatialGrid.CommitAppend(new[] { Line(16f) }, version);
        Assert.False(RoadNetworkLock.IsForeignStamp(version));
        Assert.False(RoadNetworkLock.IsForeignStamp(RoadSpatialGrid.RoadNetworkVersion));

        // Unlocked, no stamp is ever refused.
        RoadNetworkLock.Enabled = false;
        Assert.False(RoadNetworkLock.RefusesTerrain(Zone, Foreign, "test"));
        Assert.False(RoadNetworkLock.IsRefusedZone(Zone));
    }

    // ---- the server bake ----

    [Fact]
    public void TheBakeRefusesAForeignCompilerWhoeverOwnsIt()
    {
        const int version = 77;
        var foreign = new ServerBakePlanner.Compiler(Foreign, 0, false, foreignStamp: true);
        var foreignActive = new ServerBakePlanner.Compiler(Foreign, 7, ownerActiveHere: true, foreignStamp: true);
        var fresh = new ServerBakePlanner.Compiler(0, 0, false);
        var current = new ServerBakePlanner.Compiler(version, 0, false);
        var ancestor = new ServerBakePlanner.Compiler(version - 1, 0, false);

        foreach (bool loadedHere in new[] { false, true })
        {
            Assert.Equal(ServerBakePlanner.Action.RefusedByLock,
                ServerBakePlanner.Decide(version, true, loadedHere, new[] { foreign }, 1, locked: true));
            Assert.Equal(ServerBakePlanner.Action.RefusedByLock,
                ServerBakePlanner.Decide(version, true, loadedHere, new[] { foreignActive }, 1, locked: true));
        }
        // Unlocked, the same compiler is written (or waited for).
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            ServerBakePlanner.Decide(version, true, false, new[] { foreign }, 1));
        Assert.Equal(ServerBakePlanner.Action.WriteLiveCompiler,
            ServerBakePlanner.Decide(version, true, true, new[] { foreign }, 1));
        Assert.Equal(ServerBakePlanner.Action.WaitForOwner,
            ServerBakePlanner.Decide(version, true, false, new[] { foreignActive }, 1));
        // Locked, what the lock allows is decided as before.
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            ServerBakePlanner.Decide(version, true, false, new[] { fresh }, 1, locked: true));
        Assert.Equal(ServerBakePlanner.Action.WriteSavedCompiler,
            ServerBakePlanner.Decide(version, true, false, new[] { ancestor }, 1, locked: true));
        Assert.Equal(ServerBakePlanner.Action.AlreadyCurrent,
            ServerBakePlanner.Decide(version, true, false, new[] { current }, 1, locked: true));
        Assert.Equal(ServerBakePlanner.Action.CreateCompiler,
            ServerBakePlanner.Decide(version, true, false, Array.Empty<ServerBakePlanner.Compiler>(), 1, locked: true));
        // Refused is an answer: nothing for the repair queue to retry.
        Assert.Equal(ServerBakePlanner.RepairOutcome.Resolved,
            ServerBakePlanner.ResolveRepair(ServerBakePlanner.Action.RefusedByLock, ServerBakePlanner.WriteReport.NotAttempted));
    }

    // ---- bridges ----

    [Fact]
    public void AnOlderBridgeLayoutIsNeitherDestroyedNorReplacedUnderTheLock()
    {
        SaveANetwork();
        byte[] record = BridgeRecord(2, BridgeLayout.LayoutVersion - 1, new Vector2s(0, 0), new Vector2s(1, 0));
        Metadata().Set(BridgeZonesHash, record);
        int cleared = BridgePlacement.ClearedByTests;
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.True(RoadNetworkGenerator.RoadsLoadedFromZDO);
        Assert.Equal(cleared, BridgePlacement.ClearedByTests);
        Assert.True(RoadNetworkLock.BridgesFrozen);
        Assert.False(RoadNetworkLock.MaySpawnPlannedBridges);
        Assert.Contains("layout 3", Assert.Single(m_log, l => l.StartsWith("[LOCK] refused bridge layout replacement")));
        Assert.Contains("bridgesFrozen=True", Assert.Single(m_log, l => l.StartsWith("[LOCK] road network locked:")));

        // The record is not relabelled with this build's layout, by either save.
        RoadLifecycleManager.OnPrepareSave();
        RoadNetworkPersistence.SaveGlobalRoadData(new List<(Vector2, string)>(), new List<RoadCrossing>(),
            new HashSet<Vector2s> { new(5, 5) });
        Assert.Equal(record, Metadata().GetByteArray(BridgeZonesHash));
    }

    [Fact]
    public void UnlockedAnOlderBridgeLayoutIsReplaced()
    {
        SaveANetwork();
        Metadata().Set(BridgeZonesHash, BridgeRecord(2, BridgeLayout.LayoutVersion - 1, new Vector2s(0, 0)));
        int cleared = BridgePlacement.ClearedByTests;
        Decide();

        Assert.Equal(cleared + 1, BridgePlacement.ClearedByTests);
        Assert.False(RoadNetworkLock.BridgesFrozen);
        Assert.True(RoadNetworkLock.MaySpawnPlannedBridges);
        Assert.Equal(1, Lines("[BRIDGES] replaced an older bridge layout"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnreadableBridgeRecordFreezesBridgesOnlyUnderTheLock(bool locked)
    {
        SaveANetwork();
        Metadata().Set(BridgeZonesHash, BridgeRecord(99, 0));
        int cleared = BridgePlacement.ClearedByTests;
        RoadNetworkLock.Enabled = locked;
        Decide();

        Assert.True(RoadNetworkPersistence.BridgeRecordUnreadable);
        Assert.Equal(locked, RoadNetworkLock.BridgesFrozen);
        Assert.Equal(!locked, RoadNetworkLock.MaySpawnPlannedBridges);
        Assert.Equal(cleared, BridgePlacement.ClearedByTests);
        Assert.Equal(locked ? 1 : 0, Lines("[LOCK] refused bridge layout replacement: the saved bridge record cannot be read"));
    }

    [Fact]
    public void TheCurrentBridgeLayoutLoadsAsBeforeUnderTheLock()
    {
        SaveANetwork(new HashSet<Vector2s> { new(0, 0), new(1, 0) });
        RoadNetworkLock.Enabled = true;
        Decide();

        Assert.False(RoadNetworkLock.BridgesFrozen);
        Assert.True(RoadNetworkLock.MaySpawnPlannedBridges);
        Assert.True(BridgePlans.IsSpawned(new Vector2s(1, 0)));
        Assert.Contains("bridgeZones=2", Assert.Single(m_log, l => l.StartsWith("[LOCK] road network locked:")));
    }

    [Fact]
    public void NoBridgePieceIsDestroyedOrSpawnedFromThePlansWhenRoadsAreDisabled()
    {
        Assert.True(RoadNetworkLock.MayDestroyBridges("test"));
        RoadNetworkLock.Enabled = true;
        Assert.False(RoadNetworkLock.MayDestroyBridges("bridge piece removal"));
        Assert.Equal(1, Lines("[LOCK] refused bridge piece removal"));
        Assert.True(RoadNetworkLock.MaySpawnPlannedBridges);
        Decide();
        Assert.True(RoadNetworkLock.RoadsDisabled);
        Assert.False(RoadNetworkLock.MaySpawnPlannedBridges);
    }

    // ---- vegetation ----

    [Fact]
    public void AVegetationRecordOfAnotherVersionIsKeptUnderTheLock()
    {
        SaveANetwork();
        Metadata().Set(ClearedZonesHash, ClearedRecord(777, new Vector2s(0, 0), new Vector2s(1, 0)));
        RoadNetworkLock.Enabled = true;
        Decide();

        int version = RoadSpatialGrid.RoadNetworkVersion;
        Assert.True(VegetationClearing.IsCleared(new Vector2s(0, 0), version));
        Assert.True(VegetationClearing.IsCleared(new Vector2s(1, 0), version));
        Assert.False(VegetationClearing.IsCleared(new Vector2s(2, 0), version));
        Assert.Contains("network 777", Assert.Single(m_log, l => l.StartsWith("[LOCK] refused to clear vegetation again")));
    }

    [Fact]
    public void UnlockedAVegetationRecordOfAnotherVersionIsDropped()
    {
        SaveANetwork();
        Metadata().Set(ClearedZonesHash, ClearedRecord(777, new Vector2s(0, 0)));
        Decide();

        Assert.False(VegetationClearing.IsCleared(new Vector2s(0, 0), RoadSpatialGrid.RoadNetworkVersion));
        Assert.Equal(0, VegetationClearing.Version);
    }

    [Fact]
    public void AMatchingVegetationRecordLoadsTheSameEitherWay()
    {
        foreach (bool keep in new[] { false, true })
        {
            VegetationClearing.Load(55, new[] { Zone }, 55, keep);
            Assert.Equal(55, VegetationClearing.Version);
            Assert.True(VegetationClearing.IsCleared(Zone, 55));
            VegetationClearing.Load(55, new[] { Zone }, 0, keep);
            Assert.Equal(0, VegetationClearing.Version);
            VegetationClearing.Load(0, new[] { Zone }, 55, keep);
            Assert.Equal(0, VegetationClearing.Version);
        }
        VegetationClearing.Load(54, new[] { Zone }, 55, keepMismatched: true);
        Assert.True(VegetationClearing.IsCleared(Zone, 55));
        VegetationClearing.Load(54, new[] { Zone }, 55);
        Assert.False(VegetationClearing.IsCleared(Zone, 55));
        VegetationClearing.Reset();
    }

    // ---- isolation ----

    [Fact]
    public void AWorldUnloadForgetsTheSessionButNotTheSwitch()
    {
        RoadNetworkLock.Enabled = true;
        Decide();
        Assert.True(RoadNetworkLock.RoadsDisabled);

        RoadLifecycleManager.OnZoneSystemDestroy(m_zones);

        Assert.False(RoadNetworkLock.RoadsDisabled);
        Assert.Null(RoadNetworkLock.DisabledReason);
        Assert.True(RoadNetworkLock.Enabled);

        // The next world in the same process is a new session: one verdict of its own.
        Decide();
        Assert.Equal(2, Lines("[LOCK] selftest FAIL"));
    }

    [Fact]
    public void AReasonWithLineBreaksIsLoggedOnOneLine()
    {
        RoadNetworkLock.Enabled = true;
        RoadNetworkLock.DisableRoads("first\r\nsecond\nthird");
        Assert.Equal("[LOCK] selftest FAIL first second third", Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest")));
        RoadNetworkLock.DisableRoads("again");
        Assert.Single(m_log, l => l.StartsWith("[LOCK] selftest"));
    }
}
