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
