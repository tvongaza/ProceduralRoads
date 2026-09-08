using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Spawns the bridge plans (bridges prototype) into the world as persistent
/// ZDOs of vanilla prefabs, so unmodded clients see them too. Plans are
/// recomputed deterministically from the recorded crossings and the world
/// seed, never stored; a zone gets its pieces once, when it first comes
/// alive with the network in place, and the pieces carry a ZDO marker so a
/// zone that already has them is left alone and debug tooling can find them.
/// </summary>
public static class BridgePlacement
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    /// <summary>ZDO marker on every piece this mod spawned. Vanilla clients
    /// ignore unknown ZDO variables, so the marker is crossplay-safe.</summary>
    public static readonly int MarkerHash = "ProceduralRoads_Bridge".GetStableHashCode();

    private static Dictionary<Vector2i, List<BridgePiece>>? s_plansByZone;
    private static readonly HashSet<Vector2i> s_zonesWithPieces = new();
    private static readonly HashSet<string> s_warnedPrefabs = new();

    /// <summary>Forget the plans and what has been spawned. Called with every network reset.</summary>
    public static void Reset()
    {
        s_plansByZone = null;
        s_zonesWithPieces.Clear();
        s_warnedPrefabs.Clear();
    }

    /// <summary>Pieces planned for one zone (0 when the zone has no bridge).</summary>
    public static int PlannedPieceCount(Vector2i zone)
    {
        EnsurePlans();
        return s_plansByZone != null && s_plansByZone.TryGetValue(zone, out List<BridgePiece>? list) ? list.Count : 0;
    }

    public static int TotalPlannedPieces
    {
        get
        {
            EnsurePlans();
            if (s_plansByZone == null)
                return 0;
            int total = 0;
            foreach (var kv in s_plansByZone)
                total += kv.Value.Count;
            return total;
        }
    }

    public static int PlannedZoneCount
    {
        get
        {
            EnsurePlans();
            return s_plansByZone?.Count ?? 0;
        }
    }

    private static void EnsurePlans()
    {
        if (s_plansByZone != null || WorldGenerator.instance == null)
            return;

        s_plansByZone = new Dictionary<Vector2i, List<BridgePiece>>();
        int seed = WorldGenerator.instance.GetSeed();
        foreach (RoadCrossing crossing in BridgeLayout.DistinctSites(RoadNetworkGenerator.GetRoadCrossings()))
        {
            foreach (BridgePiece piece in BridgeLayout.Solve(crossing, WorldGenerator.instance, seed))
            {
                Vector2i zone = ZoneSystem.GetZone(piece.Position);
                if (!s_plansByZone.TryGetValue(zone, out List<BridgePiece>? list))
                {
                    list = new List<BridgePiece>();
                    s_plansByZone[zone] = list;
                }
                list.Add(piece);
            }
        }

        int total = 0;
        foreach (var kv in s_plansByZone)
            total += kv.Value.Count;
        if (total > 0)
            Log.LogInfo($"[BRIDGES] planned {total} pieces across {s_plansByZone.Count} zones");
    }

    /// <summary>
    /// Called from the SpawnZone postfix in every mode. A zone with a plan and
    /// no pieces yet gets them now: as ZDOs only under ghost generation, the
    /// way the game generates its own zones, otherwise as live objects.
    /// </summary>
    public static int OnZoneSpawned(Vector2i zoneID, ZoneSystem.SpawnMode mode) =>
        SpawnInZone(zoneID, mode == ZoneSystem.SpawnMode.Ghost);

    /// <summary>
    /// Spawn the plans into every zone that is already loaded: zones generated
    /// before the network existed (around the login position, or on a world
    /// the mod was added to) never spawn again while they stay loaded, so
    /// they get their pieces here. Returns how many zones got pieces.
    /// </summary>
    public static int SpawnInLoadedZones()
    {
        var heightmaps = Heightmap.GetAllHeightmaps();
        if (heightmaps == null)
            return 0;
        int zones = 0;
        foreach (Heightmap heightmap in heightmaps)
        {
            if (heightmap == null)
                continue;
            if (SpawnInZone(ZoneSystem.GetZone(heightmap.transform.position), ghost: false) > 0)
                zones++;
        }
        return zones;
    }

    private static int SpawnInZone(Vector2i zoneID, bool ghost)
    {
        if (ZNetScene.instance == null || ZDOMan.instance == null)
            return 0;
        EnsurePlans();
        if (s_plansByZone == null || !s_plansByZone.TryGetValue(zoneID, out List<BridgePiece>? pieces))
            return 0;
        if (HasPieces(zoneID))
            return 0;

        s_zonesWithPieces.Add(zoneID);
        int spawned = SpawnPieces(pieces, ghost);
        if (spawned > 0)
            Log.LogInfo($"[BRIDGES] zone {zoneID}: spawned {spawned} bridge pieces");
        return spawned;
    }

    /// <summary>Whether the zone's saved objects already include our pieces.</summary>
    private static bool HasPieces(Vector2i zoneID)
    {
        if (s_zonesWithPieces.Contains(zoneID))
            return true;
        List<ZDO> zdos = new();
        ZDOMan.instance.FindObjects(zoneID, zdos);
        foreach (ZDO zdo in zdos)
        {
            if (zdo.GetInt(MarkerHash) == 1)
            {
                s_zonesWithPieces.Add(zoneID);
                return true;
            }
        }
        return false;
    }

    private static int SpawnPieces(List<BridgePiece> pieces, bool ghost)
    {
        int spawned = 0;
        foreach (BridgePiece piece in pieces)
        {
            GameObject? prefab = ZNetScene.instance.GetPrefab(piece.Prefab);
            if (prefab == null)
            {
                if (s_warnedPrefabs.Add(piece.Prefab))
                    Log.LogWarning($"[BRIDGES] prefab not found: {piece.Prefab}");
                continue;
            }

            if (ghost)
                ZNetView.StartGhostInit();

            Quaternion rotation = Quaternion.Euler(piece.PitchDegrees, piece.YawDegrees, 0f);
            GameObject go = Object.Instantiate(prefab, piece.Position, rotation);

            ZNetView nview = go.GetComponent<ZNetView>();
            ZDO? zdo = nview != null ? nview.GetZDO() : null;
            if (zdo != null)
            {
                zdo.Set(MarkerHash, 1);
                WearNTear wearNTear = go.GetComponent<WearNTear>();
                if (wearNTear != null)
                    zdo.Set("health", wearNTear.m_health * piece.HealthFraction);
            }

            if (ghost)
            {
                // Plain Destroy: a ghost-init view keeps its ZDO (the point of
                // ghost generation); ZNetView.Destroy would drop it.
                Object.Destroy(go);
                ZNetView.FinishGhostInit();
            }
            spawned++;
        }
        return spawned;
    }

    /// <summary>
    /// Destroy every piece this mod spawned, loaded or not, and forget which
    /// zones had them, so the current plans can be spawned afresh. For the
    /// console commands that rebuild the network in a running world.
    /// </summary>
    public static int ClearSpawnedPieces()
    {
        s_plansByZone = null;
        s_zonesWithPieces.Clear();
        if (ZDOMan.instance == null || ZNetScene.instance == null)
            return 0;

        List<ZDO> tagged = new();
        foreach (var kv in ZDOMan.instance.m_objectsByID)
        {
            if (kv.Value.GetInt(MarkerHash) == 1)
                tagged.Add(kv.Value);
        }

        int destroyed = 0;
        foreach (ZDO zdo in tagged)
        {
            ZNetView instance = ZNetScene.instance.FindInstance(zdo);
            if (instance != null)
            {
                ZNetScene.instance.Destroy(instance.gameObject);
            }
            else
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
            }
            destroyed++;
        }
        if (destroyed > 0)
            Log.LogInfo($"[BRIDGES] destroyed {destroyed} bridge pieces");
        return destroyed;
    }

    /// <summary>
    /// Clear-areas around the plan so vegetation does not spawn through the
    /// deck (a bridge zone often carries no painted road points of its own).
    /// </summary>
    public static List<ZoneSystem.ClearArea> GetClearAreas(Vector2i zoneID)
    {
        List<ZoneSystem.ClearArea> areas = new();
        EnsurePlans();
        if (s_plansByZone == null || !s_plansByZone.TryGetValue(zoneID, out List<BridgePiece>? pieces))
            return areas;

        Vector3? last = null;
        foreach (BridgePiece piece in pieces)
        {
            if (last.HasValue && Vector3.Distance(last.Value, piece.Position) < 2f)
                continue;
            areas.Add(new ZoneSystem.ClearArea(piece.Position, 2.5f));
            last = piece.Position;
        }
        return areas;
    }
}
