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

    public static void EnsureMetadataInstance() { }
    public static void Reset() { }
    public static void SaveGlobalRoadData(List<(Vector2 position, string label)> roadStartPoints) { }
    public static bool TryLoadGlobalRoadData(List<(Vector2 position, string label)> roadStartPoints) => false;

    // Post-warp-71/route-export signatures (routes parameter is object-typed
    // via generics so the shim compiles both before and after the merge).
    public static void SaveGlobalRoadData<TRoute>(
        List<(Vector2 position, string label)> roadStartPoints, List<TRoute> routes) { }

    public static bool TryLoadGlobalRoadData<TRoute>(
        List<(Vector2 position, string label)> roadStartPoints, List<TRoute> routes) => false;

    public static void SaveGlobalRoadData<TRoute, TCrossing>(
        List<(Vector2 position, string label)> roadStartPoints,
        List<TRoute> routes, List<TCrossing> crossings) { }

    public static bool TryLoadGlobalRoadData<TRoute, TCrossing>(
        List<(Vector2 position, string label)> roadStartPoints,
        List<TRoute> routes, List<TCrossing> crossings) => false;

    public static void SaveGlobalRoadData<TRoute, TCrossing, TStair>(
        List<(Vector2 position, string label)> roadStartPoints,
        List<TRoute> routes, List<TCrossing> crossings,
        List<TStair> stairRuns, IReadOnlyCollection<Vector2i> spawnedZones) { }

    public static bool TryLoadGlobalRoadData<TRoute, TCrossing, TStair>(
        List<(Vector2 position, string label)> roadStartPoints,
        List<TRoute> routes, List<TCrossing> crossings,
        List<TStair> stairRuns, HashSet<Vector2i> spawnedZones) => false;
}

/// <summary>
/// Shim for RuinPlacement (real one instantiates prefabs via ZNetScene).
/// Only the members RoadNetworkGenerator touches.
/// </summary>
public static class RuinPlacement
{
    private static readonly HashSet<Vector2i> s_zones = new();
    public static IReadOnlyCollection<Vector2i> SpawnedZones => s_zones;
    public static void Reset() => s_zones.Clear();
    public static void MarkZonesSpawned(IEnumerable<Vector2i> zones)
    {
        foreach (Vector2i z in zones) s_zones.Add(z);
    }
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
