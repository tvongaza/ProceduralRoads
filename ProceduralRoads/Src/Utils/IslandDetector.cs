using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

public class Island
{
    public int Id { get; set; }
    public Vector2 Center { get; set; }
    public Vector2 Min { get; set; }
    public Vector2 Max { get; set; }
    public int CellCount { get; set; }
    public float ApproxArea { get; set; }
    public List<Vector2Int> Cells { get; set; } = new();
    public float CellSize { get; set; }
    public float WorldOffset { get; set; }
    public List<Vector3> CoastalLandings { get; set; } = new();
    
    private HashSet<Vector2Int>? _cellSet;
    
    public bool ContainsPoint(float worldX, float worldZ)
    {
        if (worldX < Min.x || worldX > Max.x || worldZ < Min.y || worldZ > Max.y)
            return false;
        
        _cellSet ??= new HashSet<Vector2Int>(Cells);
        
        int cellX = Mathf.RoundToInt((worldX + WorldOffset) / CellSize);
        int cellZ = Mathf.RoundToInt((worldZ + WorldOffset) / CellSize);
        return _cellSet.Contains(new Vector2Int(cellX, cellZ));
    }
    
    public bool ContainsPoint(Vector3 worldPos) => ContainsPoint(worldPos.x, worldPos.z);
    
    public Vector2 GetEdgePoint()
    {
        if (Cells.Count == 0) return Center;
        
        float minDistToEdge = float.MaxValue;
        Vector2Int edgeCell = Cells[0];
        
        foreach (var cell in Cells)
        {
            Vector2 worldPos = CellToWorld(cell);
            float distToMinX = Mathf.Abs(worldPos.x - Min.x);
            float distToMaxX = Mathf.Abs(worldPos.x - Max.x);
            float distToMinY = Mathf.Abs(worldPos.y - Min.y);
            float distToMaxY = Mathf.Abs(worldPos.y - Max.y);
            float minDist = Mathf.Min(distToMinX, distToMaxX, distToMinY, distToMaxY);
            
            if (minDist < minDistToEdge)
            {
                minDistToEdge = minDist;
                edgeCell = cell;
            }
        }
        
        return CellToWorld(edgeCell);
    }
    
    internal IEnumerable<bool> PrepareMembership(RoadPreparation preparation)
    {
        if(_cellSet!=null) yield break;
        var copied=new HashSet<Vector2Int>();
        foreach(var cell in Cells) { copied.Add(cell); if(preparation.Checkpoint()) yield return true; }
        _cellSet=copied;
    }

    public Vector2 CellToWorld(Vector2Int cell) => IslandDetector.CellToWorld(cell, CellSize, WorldOffset);
}

public static class IslandDetector
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;
    
    private const float WaterThreshold = 0.05f;
    private const float DefaultCellSize = 128f;
    private const int MinIslandCells = 10;
    
    public static Vector2 CellToWorld(Vector2Int cell, float cellSize, float worldOffset)
    {
        return new Vector2(
            cell.x * cellSize - worldOffset,
            cell.y * cellSize - worldOffset
        );
    }
    
    /// <summary>The grouping used by road generation, also exposed by road_islands without arguments.</summary>
    public static List<Island> DetectRoadIslands()
    {
        List<Island>? result=null;
        using var preparation=new RoadPreparation(p=>DetectRoadIslandsSteps(WorldGenerator.instance,RoadNetworkGenerator.NetworkOptions,p,value=>result=value));
        preparation.Drain(); return result!;
    }
    internal static IEnumerable<bool> DetectRoadIslandsSteps(WorldGenerator world,RoadNetworkOptions options,
        RoadPreparation preparation,System.Action<List<Island>> complete)
    {
        if(!options.WalkableIslands)
        {
            foreach(var step in DetectSteps(world,preparation,complete)) yield return step;
            yield break;
        }
        var clock=System.Diagnostics.Stopwatch.StartNew();
        List<Island>? islands=null;
        foreach(var step in RoadIslandDetector.DetectSteps(world,preparation,value=>islands=value,options:options)) yield return step;
        Log.LogInfo($"Road island detection: {islands!.Count} groups at {RoadIslandDetector.SampleSpacing} m in {clock.Elapsed.TotalSeconds:F1}s");
        complete(islands);
    }

    public static List<Island> DetectIslands(float cellSize = DefaultCellSize, int minCells = MinIslandCells)
    {
        List<Island>? result=null;
        using var preparation=new RoadPreparation(p=>DetectSteps(WorldGenerator.instance,p,value=>result=value,cellSize,minCells));
        preparation.Drain(); return result!;
    }
    internal static IEnumerable<bool> DetectSteps(WorldGenerator wg,RoadPreparation preparation,System.Action<List<Island>> complete,
        float cellSize=DefaultCellSize,int minCells=MinIslandCells)
    {
        if (wg == null)
        {
            Log.LogWarning("IslandDetector: WorldGenerator not available");
            complete(new List<Island>()); yield break;
        }
        
        float worldRadius = 10000f;
        float worldSize = worldRadius * 2;
        int gridSize = Mathf.CeilToInt(worldSize / cellSize);
        float worldOffset = worldRadius;
        
        Log.LogDebug($"IslandDetector: Scanning {gridSize}x{gridSize} grid (cellSize={cellSize}m)");
        
        preparation.BeginStage("Mapping legacy islands",gridSize*gridSize,"cells visited"); yield return true;
        bool[,] isLand = new bool[gridSize, gridSize];
        int landCells = 0;
        
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                float worldX = x * cellSize - worldOffset;
                float worldY = y * cellSize - worldOffset;
                
                float distFromCenter = Mathf.Sqrt(worldX * worldX + worldY * worldY);
                if (distFromCenter > worldRadius)
                {
                    isLand[x, y] = false;
                    if(preparation.Checkpoint()) yield return true;
                    continue;
                }
                
                float baseHeight = wg.GetBaseHeight(worldX, worldY, false);
                isLand[x, y] = baseHeight >= WaterThreshold;
                if (isLand[x, y]) landCells++;
                if(preparation.Checkpoint()) yield return true;
            }
        }
        
        Log.LogDebug($"IslandDetector: Found {landCells} land cells out of {gridSize * gridSize} total");
        
        preparation.BeginStage("Grouping legacy islands",unit:"visits"); yield return true;
        bool[,] visited = new bool[gridSize, gridSize];
        var islands = new List<Island>();
        int islandId = 0;
        
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                if(preparation.Checkpoint()) yield return true;
                if (isLand[x, y] && !visited[x, y])
                {
                    Island? island=null;
                    foreach(var step in FloodFillSteps(isLand,visited,x,y,gridSize,cellSize,worldOffset,preparation,value=>island=value)) yield return step;
                    if (island!.CellCount >= minCells)
                    {
                        island.Id = islandId++;
                        islands.Add(island);
                    }
                }
            }
        }
        
        preparation.BeginStage("Ordering legacy islands"); yield return true;
        islands.Sort((a, b) => b.CellCount.CompareTo(a.CellCount));
        
        Log.LogDebug($"IslandDetector: Detected {islands.Count} islands (min {minCells} cells)");
        
        complete(islands);
    }
    
    private static IEnumerable<bool> FloodFillSteps(bool[,] isLand, bool[,] visited, int startX, int startY,
        int gridSize, float cellSize, float worldOffset,RoadPreparation preparation,System.Action<Island> complete)
    {
        var island = new Island();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;
        
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        float sumX = 0, sumY = 0;
        
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };
        
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if(preparation.Checkpoint()) yield return true;
            island.Cells.Add(cell);
            island.CellCount++;
            
            Vector2 worldPos = CellToWorld(cell, cellSize, worldOffset);
            sumX += worldPos.x;
            sumY += worldPos.y;
            
            if (worldPos.x < minX) minX = worldPos.x;
            if (worldPos.x > maxX) maxX = worldPos.x;
            if (worldPos.y < minY) minY = worldPos.y;
            if (worldPos.y > maxY) maxY = worldPos.y;
            
            for (int i = 0; i < 4; i++)
            {
                int nx = cell.x + dx[i];
                int ny = cell.y + dy[i];
                
                if (nx >= 0 && nx < gridSize && ny >= 0 && ny < gridSize &&
                    isLand[nx, ny] && !visited[nx, ny])
                {
                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        
        Vector2 centroid = new Vector2(sumX / island.CellCount, sumY / island.CellCount);
        
        float minDistToCentroid = float.MaxValue;
        Vector2 closestCell = centroid;
        foreach (var cell in island.Cells)
        {
            if(preparation.Checkpoint()) yield return true;
            Vector2 cellWorld = CellToWorld(cell, cellSize, worldOffset);
            float dist = (cellWorld.x - centroid.x) * (cellWorld.x - centroid.x) + 
                         (cellWorld.y - centroid.y) * (cellWorld.y - centroid.y);
            if (dist < minDistToCentroid)
            {
                minDistToCentroid = dist;
                closestCell = cellWorld;
            }
        }
        
        island.Center = closestCell;
        island.Min = new Vector2(minX, minY);
        island.Max = new Vector2(maxX, maxY);
        island.ApproxArea = island.CellCount * cellSize * cellSize;
        island.CellSize = cellSize;
        island.WorldOffset = worldOffset;
        
        complete(island);
    }
    
    public static string GetIslandSummary(Island island)
    {
        float width = island.Max.x - island.Min.x;
        float height = island.Max.y - island.Min.y;
        return $"Island {island.Id}: center=({island.Center.x:F0},{island.Center.y:F0}) " +
               $"size={width:F0}x{height:F0}m area≈{island.ApproxArea/1000000:F2}km² cells={island.CellCount}";
    }
}
