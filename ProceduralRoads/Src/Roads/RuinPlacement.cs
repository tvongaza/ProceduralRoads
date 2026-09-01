using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Spawns the solved ruin plans (bridges at crossings, staircases on stair
/// runs) into the world as persistent ZDOs when their zones first generate.
/// Vanilla prefabs only, so unmodded clients see everything. Pieces carry
/// WearNTear health fractions for the built-in damage visuals. Zones are
/// spawned exactly once (the set is persisted); plans are recomputed
/// deterministically from the world seed, never stored.
/// </summary>
public static class RuinPlacement
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    private static Dictionary<Vector2i, List<BridgePiece>>? m_plansByZone;
    private static readonly HashSet<Vector2i> m_spawnedZones = new();
    private static readonly HashSet<string> m_warnedPrefabs = new();

    /// <summary>ZDO marker identifying pieces this mod spawned, so debug
    /// tooling can find and remove them (vanilla clients ignore unknown ZDO
    /// vars, so the marker is crossplay-safe).</summary>
    public static readonly int RuinMarkerHash = "pr_ruin".GetStableHashCode();

    public static IReadOnlyCollection<Vector2i> SpawnedZones => m_spawnedZones;

    /// <summary>Forget which zones have spawned (their pieces should already
    /// be destroyed) so plans can respawn from the current layout code.</summary>
    public static void ClearSpawnedZones() => m_spawnedZones.Clear();

    /// <summary>Recompute plans and spawn every planned zone immediately in
    /// ghost mode (ZDOs only; ZNetScene instantiates the nearby ones on its
    /// own). Debug workflow: iterate layout code against one fixture world
    /// without recreating it or re-visiting zones.</summary>
    public static int RespawnAllZones()
    {
        m_plansByZone = null; // recompute from the current road network
        EnsurePlans();
        if (m_plansByZone == null)
            return 0;

        int zones = 0;
        foreach (Vector2i zone in new List<Vector2i>(m_plansByZone.Keys))
        {
            if (m_spawnedZones.Contains(zone))
                continue;
            SpawnRuinsInZone(zone, ZoneSystem.SpawnMode.Ghost);
            zones++;
        }
        return zones;
    }

    public static void Reset()
    {
        m_plansByZone = null;
        m_spawnedZones.Clear();
        m_warnedPrefabs.Clear();
    }

    public static void MarkZonesSpawned(IEnumerable<Vector2i> zones)
    {
        foreach (Vector2i zone in zones)
            m_spawnedZones.Add(zone);
    }

    /// <summary>Total planned pieces (for the selftest report — plans exist
    /// even when no zones have spawned yet, e.g. on a headless server).</summary>
    public static int GetPlannedPieceCount()
    {
        EnsurePlans();
        if (m_plansByZone == null)
            return 0;
        int total = 0;
        foreach (var kv in m_plansByZone) total += kv.Value.Count;
        return total;
    }

    private static void EnsurePlans()
    {
        if (m_plansByZone != null || WorldGenerator.instance == null)
            return;

        m_plansByZone = new Dictionary<Vector2i, List<BridgePiece>>();
        int seed = WorldGenerator.instance.GetSeed();

        foreach (RoadCrossing crossing in RoadNetworkGenerator.GetRoadCrossings())
            Bucket(BridgeLayout.Solve(crossing, WorldGenerator.instance, seed, BridgeStyleFor(crossing.Biome)));

        foreach (StairRun run in RoadNetworkGenerator.GetStairRuns())
            Bucket(StairLayout.Solve(run, WorldGenerator.instance, seed, StairLayout.StyleFor(run.Biome)));

        int total = 0;
        foreach (var kv in m_plansByZone) total += kv.Value.Count;
        Log.LogInfo($"[RUINS] planned {total} pieces across {m_plansByZone.Count} zones");
    }

    private static BridgeStyle BridgeStyleFor(Heightmap.Biome biome)
    {
        // Progression-aligned kits; Mistlands gets black marble later.
        return biome switch
        {
            Heightmap.Biome.Mountain or Heightmap.Biome.Plains or Heightmap.Biome.Mistlands
                => BridgeStyle.MountainStone,
            _ => BridgeStyle.MeadowsWood,
        };
    }

    private static void Bucket(List<BridgePiece> pieces)
    {
        foreach (BridgePiece piece in pieces)
        {
            Vector2i zone = ZoneSystem.GetZone(piece.Position);
            if (!m_plansByZone!.TryGetValue(zone, out List<BridgePiece>? list))
            {
                list = new List<BridgePiece>();
                m_plansByZone[zone] = list;
            }
            list.Add(piece);
        }
    }

    /// <summary>
    /// Called from the SpawnZone postfix. Spawns this zone's ruin pieces as
    /// persistent ZDOs, once ever per zone. Ghost mode wraps instantiation
    /// in ghost-init like vanilla generation does.
    /// </summary>
    public static void SpawnRuinsInZone(Vector2i zoneID, ZoneSystem.SpawnMode mode)
    {
        if (mode == ZoneSystem.SpawnMode.Client)
            return;
        if (m_spawnedZones.Contains(zoneID))
            return;

        EnsurePlans();
        if (m_plansByZone == null || !m_plansByZone.TryGetValue(zoneID, out List<BridgePiece>? pieces))
            return;
        if (ZNetScene.instance == null)
            return;

        m_spawnedZones.Add(zoneID);

        bool ghost = mode == ZoneSystem.SpawnMode.Ghost;
        int spawned = 0;

        foreach (BridgePiece piece in pieces)
        {
            GameObject? prefab = ZNetScene.instance.GetPrefab(piece.Prefab);
            if (prefab == null)
            {
                if (m_warnedPrefabs.Add(piece.Prefab))
                    Log.LogWarning($"[RUINS] prefab not found: {piece.Prefab}");
                continue;
            }

            if (ghost)
                ZNetView.StartGhostInit();

            Quaternion rotation = Quaternion.Euler(piece.PitchDegrees, piece.YawDegrees, piece.RollDegrees);
            GameObject go = Object.Instantiate(prefab, piece.Position, rotation);

            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.GetZDO() != null)
                nview.GetZDO().Set(RuinMarkerHash, 1);

            WearNTear wearNTear = go.GetComponent<WearNTear>();
            if (wearNTear != null && wearNTear.m_nview != null && wearNTear.m_nview.GetZDO() != null)
                wearNTear.m_nview.GetZDO().Set("health", wearNTear.m_health * piece.HealthFraction);

            if (ghost)
            {
                // Plain Destroy: ghost-init views keep their ZDO (the whole
                // point of ghost generation); ZNetView.Destroy would drop it.
                Object.Destroy(go);
                ZNetView.FinishGhostInit();
            }

            spawned++;
        }

        if (spawned > 0)
            Log.LogInfo($"[RUINS] zone {zoneID}: spawned {spawned} ruin pieces");
    }

    /// <summary>
    /// Clear-areas so vegetation does not spawn through decks and stairs
    /// (ruin zones often carry no painted road points of their own).
    /// </summary>
    public static List<ZoneSystem.ClearArea> GetClearAreas(Vector2i zoneID)
    {
        List<ZoneSystem.ClearArea> areas = new();
        EnsurePlans();
        if (m_plansByZone == null || !m_plansByZone.TryGetValue(zoneID, out List<BridgePiece>? pieces))
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
