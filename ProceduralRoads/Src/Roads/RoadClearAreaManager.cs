using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Manages vegetation clear areas for roads.
/// </summary>
public static class RoadClearAreaManager
{
    private static readonly Dictionary<Vector2s, List<ZoneSystem.ClearArea>> s_roadClearAreasCache =
        new Dictionary<Vector2s, List<ZoneSystem.ClearArea>>();

    public static void ClearCache()
    {
        s_roadClearAreasCache.Clear();
    }

    /// <summary>
    /// Gets or creates road clear areas for a zone. Used to prevent vegetation spawning on roads.
    /// </summary>
    public static List<ZoneSystem.ClearArea> GetOrCreateClearAreas(Vector2s zoneID)
    {
        if (!s_roadClearAreasCache.TryGetValue(zoneID, out List<ZoneSystem.ClearArea> roadClearAreas))
        {
            roadClearAreas = CreateRoadClearAreas(zoneID);
            s_roadClearAreasCache[zoneID] = roadClearAreas;
        }

        return roadClearAreas;
    }

    /// <summary>The clear areas of the road points in a zone added by appends after the first <paramref name="sinceAppend"/>
    /// (a zone cleared for the older roads owes only the newer ones'). Not cached.</summary>
    public static List<ZoneSystem.ClearArea> ClearAreasSince(Vector2s zoneID, int sinceAppend) => CreateRoadClearAreas(zoneID, sinceAppend);

    private static List<ZoneSystem.ClearArea> CreateRoadClearAreas(Vector2s zoneID, int sinceAppend = -1)
    {
        List<ZoneSystem.ClearArea> clearAreas = new List<ZoneSystem.ClearArea>();
        List<RoadSpatialGrid.RoadPoint> roadPoints = RoadSpatialGrid.GetRoadPointsInZone(zoneID);
        if (sinceAppend >= 0) roadPoints = roadPoints.FindAll(p => p.addition > sinceAppend);

        if (roadPoints.Count == 0)
            return clearAreas;

        HashSet<Vector2i> processedCells = new HashSet<Vector2i>();

        foreach (RoadSpatialGrid.RoadPoint roadPoint in roadPoints)
        {
            Vector2i cell = new Vector2i(
                Mathf.RoundToInt(roadPoint.p.x / RoadConstants.VegetationClearSampleInterval),
                Mathf.RoundToInt(roadPoint.p.y / RoadConstants.VegetationClearSampleInterval));

            if (processedCells.Contains(cell))
                continue;
            processedCells.Add(cell);

            Vector3 center = new Vector3(
                cell.x * RoadConstants.VegetationClearSampleInterval,
                0f,
                cell.y * RoadConstants.VegetationClearSampleInterval);
            
            float radius = roadPoint.w * RoadConstants.VegetationClearMultiplier;
            clearAreas.Add(new ZoneSystem.ClearArea(center, radius));
        }

        return clearAreas;
    }
}
