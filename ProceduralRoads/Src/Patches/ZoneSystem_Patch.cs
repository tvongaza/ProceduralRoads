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
        public static void Prefix(Vector2i zoneID, List<ZoneSystem.ClearArea> clearAreas)
        {
            // Roads loaded from the save count too: a zone that first generates on a
            // reloaded world must get its paint, leveling, clearing and ruin pieces
            // (Tys, 3 Sep 2026: after a relaunch every zone had the road in the data
            // and nothing on the ground).
            if (!RoadNetworkGenerator.RoadsAvailable)
                return;

            List<ZoneSystem.ClearArea> roadClearAreas = RoadClearAreaManager.GetOrCreateClearAreas(zoneID);
            clearAreas.AddRange(roadClearAreas);
            clearAreas.AddRange(RuinPlacement.GetClearAreas(zoneID));
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnZone))]
    public static class ZoneSystem_SpawnZone_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZoneSystem __instance, Vector2i zoneID, ZoneSystem.SpawnMode mode, ref bool __result)
        {
            if (!__result || !RoadNetworkGenerator.RoadsAvailable)
                return;

            RuinPlacement.SpawnRuinsInZone(zoneID, mode);

            List<RoadSpatialGrid.RoadPoint> roadPoints = RoadSpatialGrid.GetRoadPointsInZone(zoneID);
            if (roadPoints.Count == 0)
                return;

            if (mode == ZoneSystem.SpawnMode.Client)
                RoadVegetationCleaner.RemoveOverlappingVegetation(zoneID, roadPoints);
            else
                RoadTerrainModifier.ApplyRoadTerrainMods(zoneID, roadPoints);
        }
    }

    /// <summary>Night plan 2026-09-03 task 1h: log every placed location with
    /// the rotation the game chose, so a rotation predicted from terrain
    /// (slope-rotated prefabs) can be checked against reality. Off unless
    /// [Debug] LogLocationSpawns is on: one line per location per zone.</summary>
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SpawnLocation))]
    public static class ZoneSystem_SpawnLocation_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ZoneSystem.ZoneLocation location, int seed, Vector3 pos, Quaternion rot, ZoneSystem.SpawnMode mode)
        {
            if (ProceduralRoadsPlugin.LogLocationSpawns == null || !ProceduralRoadsPlugin.LogLocationSpawns.Value)
                return;
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo(
                $"[LOCATION] {location.m_prefab.Name} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) yaw={rot.eulerAngles.y:F1} seed={seed} mode={mode} " +
                $"slopeRotation={location.m_slopeRotation} randomRotation={location.m_randomRotation}");
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
    public static class ZoneSystem_OnDestroy_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ZoneSystem __instance)
        {
            RoadLifecycleManager.OnZoneSystemDestroy(__instance);
        }
    }

}
