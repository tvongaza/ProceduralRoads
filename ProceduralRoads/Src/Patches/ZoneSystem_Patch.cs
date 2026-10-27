using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Harmony patches for ZoneSystem and related classes to integrate road generation.
/// This file contains thin wrappers that delegate to dedicated modules.
/// </summary>
public static class ZoneSystem_Patch
{
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZoneSystem __instance)
        {
            RoadLifecycleManager.OnZoneSystemStart(__instance);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PlaceVegetation))]
    public static class ZoneSystem_PlaceVegetation_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Vector2s zoneID, List<ZoneSystem.ClearArea> clearAreas)
        {
            if (!RoadNetworkGenerator.RoadsAvailable)
                return;

            List<ZoneSystem.ClearArea> roadClearAreas = RoadClearAreaManager.GetOrCreateClearAreas(zoneID);
            clearAreas.AddRange(roadClearAreas);
            List<ZoneSystem.ClearArea> bridgeClearAreas = StructurePlacement.GetClearAreas(zoneID);
            clearAreas.AddRange(bridgeClearAreas);

            // Generated with the road's clear areas: its vegetation matches the
            // network from the start, and the server never clears it later.
            if (roadClearAreas.Count > 0 || bridgeClearAreas.Count > 0)
                VegetationClearing.MarkCleared(zoneID, RoadSpatialGrid.RoadNetworkVersion);
        }

        /// <summary>
        /// A zone generated for a remote peer (ghost mode) is torn down as
        /// soon as it has been populated and only its ZDOs remain, so its road
        /// terrain has to go into a ZDO now, while its heightmap still exists.
        /// SpawnZone's postfix is too late: the zone root is gone by then.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Vector2s zoneID, Heightmap hmap, ZoneSystem.SpawnMode mode)
        {
            if (mode == ZoneSystem.SpawnMode.Ghost)
                ServerTerrainBake.OnGhostZoneGenerated(zoneID, hmap);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "Update")]
    public static class ZoneSystem_Update_Patch
    {
        // Location/zone placement mutates world state that the sealed island workers read. HUD and CLI still tick.
        [HarmonyPrefix]
        public static bool Prefix() => !RoadLifecycleManager.InitialGenerationActive;

        private static float s_nextReveal;

        [HarmonyPostfix]
        public static void Postfix(ZoneSystem __instance)
        {
            if (!RoadLifecycleManager.IsGenerating)
            {
                if (FreezeWatchdog.Enabled) FreezeWatchdog.Enter("terrain bake");
                ServerTerrainBake.Tick();
                if (FreezeWatchdog.Enabled) FreezeWatchdog.Leave();
            }
            if (FreezeWatchdog.Enabled) FreezeWatchdog.Enter("site icons");
            RevealSiteIcons(__instance);
            if (FreezeWatchdog.Enabled) FreezeWatchdog.Leave();
        }

        /// <summary>A place placed before its roads goes on the map once a player has come as near as the
        /// game would have placed it from (<see cref="HiddenSiteIcons"/>): checked twice a second, as the
        /// game's own zone creation for each player and the server's own position.</summary>
        private static void RevealSiteIcons(ZoneSystem zones)
        {
            if (HiddenSiteIcons.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer() || Time.time < s_nextReveal) return;
            s_nextReveal = Time.time + 0.5f;
            var references = new List<Vector3> { ZNet.instance.GetReferencePosition() };
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) references.Add(peer.GetRefPos());
            List<Vector3> shown = HiddenSiteIcons.Reveal(references, zones.m_simulationDistance.TotalSimulationDistance, zones.m_simulationDistance.IsClassic);
            if (shown.Count == 0) return;
            if (RoadNetworkGenerator.RoadsLog)
                foreach (Vector3 p in shown)
                    ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"[SITES] icon at {p} on the map: a player came near enough for the game to have placed it");
            zones.SendLocationIcons(0L);
        }
    }

    /// <summary>The server's map icons without the places placed before their roads that no player has come
    /// near yet (<see cref="HiddenSiteIcons"/>); the list the server sends every player is built here too.</summary>
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.GetLocationIcons))]
    public static class ZoneSystem_GetLocationIcons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Dictionary<Vector3, string> icons)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) HiddenSiteIcons.Filter(icons);
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnZone))]
    public static class ZoneSystem_SpawnZone_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZoneSystem __instance, Vector2s zoneID, ZoneSystem.SpawnMode mode, ref bool __result)
        {
            // Every zone that comes alive gets its road terrain, whether the
            // network was generated this session or loaded from the save, and
            // whether the zone spawns Full (first time) or Client (generated
            // earlier, as a ghost zone or in another session). Ghost spawns
            // are skipped: their zone root is destroyed on the spot, and a
            // terrain compiler made for it would only duplicate the one the
            // real spawn makes. A zone already stamped with the current network
            // version is left alone, so a reload does not overwrite the
            // player's terrain edits in the road. See RoadTerrainModifier.
            if (!__result || !RoadNetworkGenerator.RoadsAvailable)
                return;

            // Bridge pieces (prototype) go in under every mode: as ZDOs only
            // under ghost generation, the way the game places its own
            // objects, and a zone that already has them is left alone.
            if (FreezeWatchdog.Enabled) FreezeWatchdog.Enter("zone pieces");
            StructurePlacement.OnZoneSpawned(zoneID, mode);
            if (FreezeWatchdog.Enabled) FreezeWatchdog.Leave();

            if (mode == ZoneSystem.SpawnMode.Ghost)
                return;

            List<RoadSpatialGrid.RoadPoint> roadPoints = RoadSpatialGrid.GetRoadPointsInZone(zoneID);
            if (roadPoints.Count == 0) return;
            if (FreezeWatchdog.Enabled) FreezeWatchdog.Enter("zone terrain");
            RoadTerrainModifier.OnZoneSpawned(zoneID, roadPoints);
            if (FreezeWatchdog.Enabled) FreezeWatchdog.Leave();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
    public static class ZoneSystem_OnDestroy_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ZoneSystem __instance)
        {
            ServerTerrainBake.Reset();
            RoadLifecycleManager.OnZoneSystemDestroy(__instance);
        }
    }

}
