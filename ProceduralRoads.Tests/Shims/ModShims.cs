// Stand-ins for mod classes we deliberately do NOT compile into the harness
// (they drag in ZDO/Harmony/MonoBehaviour dependencies). Signatures mirror
// only the members RoadNetworkGenerator / RoadSpatialGrid actually call.

using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Shim for RoadNetworkPersistence (real one lives in ZDO land).</summary>
public static class RoadNetworkPersistence
{
    public const string MetadataPrefabName = "ProceduralRoads_Metadata";

    /// <summary>Test control: whether this world has a road network saved in
    /// it.</summary>
    public static bool SavedNetworkExists;

    /// <summary>
    /// Test control: whether that network can be FOUND yet.
    ///
    /// This is the whole point. The real search reads the world's ZDOs, so
    /// before ZDOMan has loaded them it returns nothing even though the
    /// network is sitting in the save file. A shim that answered "yes, it
    /// exists" regardless of timing could not tell a fixed mod from a broken
    /// one, because the broken one's mistake is looking too early and being
    /// told no.
    /// </summary>
    public static bool SavedNetworkVisible;

    /// <summary>Test observation: how many times a load was attempted, so a
    /// test can tell "looked and found nothing" from "never looked".</summary>
    public static int LoadAttempts;

    public static void ResetForTest()
    {
        SavedNetworkExists = false;
        SavedNetworkVisible = false;
        LoadAttempts = 0;
    }

    public static void EnsureMetadataInstance() { }
    public static void Reset() { }
    public static void SaveGlobalRoadData(List<(Vector2 position, string label)> roadStartPoints) { }

    public static bool TryLoadGlobalRoadData(List<(Vector2 position, string label)> roadStartPoints)
    {
        LoadAttempts++;
        return SavedNetworkExists && SavedNetworkVisible;
    }

    // Post-warp-71/route-export signatures (routes parameter is object-typed
    // via generics so the shim compiles both before and after the merge).
    public static void SaveGlobalRoadData<TRoute>(
        List<(Vector2 position, string label)> roadStartPoints, List<TRoute> routes) { }

    public static bool TryLoadGlobalRoadData<TRoute>(
        List<(Vector2 position, string label)> roadStartPoints, List<TRoute> routes) => false;
}

/// <summary>
/// Shim for the debug-info struct from Src/Debug/RoadPointDebugMarker.cs
/// (that file also contains a MonoBehaviour, so it is not compiled here).
/// Field list mirrors the real struct.
/// </summary>
public struct RoadPointDebugInfo
{
    public int PointIndex;
    public int TotalPoints;
    public float OriginalHeight;
    public float SmoothedHeight;
    public int WindowStart;
    public int WindowEnd;
    public int ActualWindowSize;
    public float[] WindowHeights;
}


/// <summary>
/// Shim for RoadClearAreaManager: the real one caches per zone and needs
/// ZoneSystem.ClearArea, which the lifecycle tests do not exercise.
/// </summary>
public static class RoadClearAreaManager
{
    public static int CacheClears;
    public static void ClearCache() => CacheClears++;
}

/// <summary>Shim for the one RoadTerrainModifier member the lifecycle calls.</summary>
public static class RoadTerrainModifier
{
    public static void ResetDebugCounters() { }
}
