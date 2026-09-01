using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// A* pathfinding for road generation with 16-direction movement, slope-based cost, and river avoidance.
/// </summary>
public class RoadPathfinder
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;
    
    public const float CellSize = RoadConstants.PathfindingCellSize;
    public static int MaxIterations = RoadConstants.PathfindingMaxIterations;

    private const float ImpassableCost = float.PositiveInfinity;

    /// <summary>Cumulative A* iterations across all searches (profiling).</summary>
    public static long TotalIterations;

    /// <summary>Cumulative world-terrain samples (profiling; interior sampling multiplies these).</summary>
    public static long TotalTerrainSamples;

    /// <summary>
    /// EXPERIMENT (off by default): before running A*, flood-fill the TARGET's
    /// passable pocket (same move rules, capped). Unreachable targets usually
    /// sit in small pockets behind rivers/cliffs: the fill exhausts quickly
    /// without containing the start -> fail in O(pocket) instead of burning
    /// the full A* cap, and the pocket is memoized so later edges into it
    /// fail O(1). A capped fill means "large component, unknown" and falls
    /// through to normal A*, so reachable searches are never affected.
    /// </summary>
    public static bool UseReachabilityPrepass = false;
    public static int PrepassFillCap = 3000;

    public float SlopeMultiplier = RoadConstants.DefaultSlopeMultiplier;
    public float RiverPenalty = RoadConstants.DefaultRiverPenalty;
    public float SwampShallowWaterPenalty = RoadConstants.DefaultSwampShallowWaterPenalty;
    public float MountainSteepSlopePenalty = RoadConstants.DefaultMountainSteepSlopePenalty;
    public float SteepSlopePenalty = RoadConstants.DefaultSteepSlopePenalty;
    public float SteepSlopeThreshold = RoadConstants.DefaultSteepSlopeThreshold;
    public float TerrainVariancePenalty = RoadConstants.DefaultTerrainVariancePenalty;
    public float TerrainVarianceThreshold = RoadConstants.DefaultTerrainVarianceThreshold;
    public float BaseCost = RoadConstants.DefaultBaseCost;

    private static readonly Vector2Int[] Directions = new Vector2Int[]
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1),
        new Vector2Int(2, 1), new Vector2Int(2, -1), new Vector2Int(-2, 1), new Vector2Int(-2, -1),
        new Vector2Int(1, 2), new Vector2Int(-1, 2), new Vector2Int(1, -2), new Vector2Int(-1, -2),
    };

    private static readonly float[] DirectionCosts;

    static RoadPathfinder()
    {
        DirectionCosts = new float[Directions.Length];
        for (int i = 0; i < Directions.Length; i++)
            DirectionCosts[i] = Mathf.Sqrt(Directions[i].x * Directions[i].x + Directions[i].y * Directions[i].y);
    }

    private WorldGenerator m_worldGen;

    // Per-cell terrain cache: terrain is a pure function of position, and A*
    // re-touches the same cells from up to 16 directions across every search
    // on an island. Caching the variance probe alone replaces 9 height
    // queries with a lookup. Values are identical to uncached — results
    // cannot change, only speed.
    private struct CellSample
    {
        public float Height;
        public float RiverWeight;
        public float Variance;
        public Heightmap.Biome Biome;
    }

    private readonly Dictionary<Vector2i, CellSample> m_cellCache = new();

    public RoadPathfinder(WorldGenerator worldGen)
    {
        m_worldGen = worldGen;
    }

    private CellSample GetCellSample(Vector2i grid)
    {
        if (m_cellCache.TryGetValue(grid, out CellSample cached))
            return cached;

        Vector2 world = GridToWorld(grid);
        TotalTerrainSamples += 2 + RoadConstants.TerrainVarianceSampleCount;
        CellSample sample = new()
        {
            Height = m_worldGen.GetHeight(world.x, world.y),
            Biome = m_worldGen.GetBiome(world.x, world.y),
            Variance = GetTerrainVariance(world),
        };
        m_worldGen.GetRiverWeight(world.x, world.y, out sample.RiverWeight, out _);
        m_cellCache[grid] = sample;
        return sample;
    }

    public List<Vector2>? FindPath(Vector2 start, Vector2 end)
    {
        Vector2i startGrid = WorldToGrid(start);
        Vector2i endGrid = WorldToGrid(end);

        if (startGrid == endGrid)
            return new List<Vector2> { start, end };

        SortedSet<(float priority, Vector2i pos)> openSet = new SortedSet<(float, Vector2i)>(
            Comparer<(float priority, Vector2i pos)>.Create((a, b) =>
            {
                int cmp = a.priority.CompareTo(b.priority);
                if (cmp != 0) return cmp;
                cmp = a.pos.x.CompareTo(b.pos.x);
                if (cmp != 0) return cmp;
                return a.pos.y.CompareTo(b.pos.y);
            }));

        Dictionary<Vector2i, float> gCosts = new Dictionary<Vector2i, float>();
        Dictionary<Vector2i, Vector2i> cameFrom = new Dictionary<Vector2i, Vector2i>();
        HashSet<Vector2i> closedSet = new HashSet<Vector2i>();

        if (UseReachabilityPrepass && !PrepassReachable(startGrid, endGrid))
        {
            Log.LogWarning(
                $"Pathfinding failed: unreachable (prepass) distance {Vector2.Distance(start, end):F0}m");
            return null;
        }

        openSet.Add((Heuristic(startGrid, endGrid), startGrid));
        gCosts[startGrid] = 0;

        int iterations = 0;

        while (openSet.Count > 0 && iterations < MaxIterations)
        {
            iterations++;
            TotalIterations++;

            var current = openSet.Min;
            openSet.Remove(current);
            Vector2i currentPos = current.pos;

            if (currentPos == endGrid)
                return ReconstructPath(cameFrom, currentPos, start, end);

            closedSet.Add(currentPos);

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2i neighborPos = new Vector2i(currentPos.x + Directions[i].x, currentPos.y + Directions[i].y);

                float moveCost = GetMoveCost(currentPos, neighborPos, i);

                if (float.IsPositiveInfinity(moveCost))
                {
                    // Only river cores can be jumped (a short ford). Other
                    // blockers (deep water, non-swamp shallows) end the move.
                    if (!IsRiverBlocked(neighborPos))
                        continue;

                    if (!TryGetShortRiverCrossing(currentPos, Directions[i],
                            out Vector2i fordLanding, out float fordCost))
                        continue;

                    neighborPos = fordLanding;
                    moveCost = fordCost;
                }

                if (closedSet.Contains(neighborPos))
                    continue;

                float tentativeG = gCosts[currentPos] + moveCost;

                bool known = gCosts.TryGetValue(neighborPos, out float existingG);
                if (!known || tentativeG < existingG)
                {
                    float h = Heuristic(neighborPos, endGrid);
                    if (known)
                        openSet.Remove((existingG + h, neighborPos));

                    cameFrom[neighborPos] = currentPos;
                    gCosts[neighborPos] = tentativeG;
                    openSet.Add((tentativeG + h, neighborPos));
                }
            }
        }

        string reason = openSet.Count == 0 ? "no reachable path" : "max iterations reached";
        Log.LogWarning($"Pathfinding failed: {reason} after {iterations} iterations");
        return null;
    }

    private Vector2i WorldToGrid(Vector2 world)
    {
        return new Vector2i(Mathf.RoundToInt(world.x / CellSize), Mathf.RoundToInt(world.y / CellSize));
    }

    private Vector2 GridToWorld(Vector2i grid)
    {
        return new Vector2(grid.x * CellSize, grid.y * CellSize);
    }

    private float Heuristic(Vector2i from, Vector2i to)
    {
        float dx = (to.x - from.x) * CellSize;
        float dy = (to.y - from.y) * CellSize;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private float GetTerrainVariance(Vector2 pos)
    {
        float centerHeight = m_worldGen.GetHeight(pos.x, pos.y);
        float minHeight = centerHeight;
        float maxHeight = centerHeight;
        
        for (int i = 0; i < RoadConstants.TerrainVarianceSampleCount; i++)
        {
            float angle = i * Mathf.PI * 2f / RoadConstants.TerrainVarianceSampleCount;
            float h = m_worldGen.GetHeight(
                pos.x + Mathf.Cos(angle) * RoadConstants.TerrainVarianceSampleRadius,
                pos.y + Mathf.Sin(angle) * RoadConstants.TerrainVarianceSampleRadius);
            minHeight = Mathf.Min(minHeight, h);
            maxHeight = Mathf.Max(maxHeight, h);
        }
        
        return maxHeight - minHeight;
    }

    private float GetMoveCost(Vector2i from, Vector2i to, int directionIndex)
    {
        Vector2 fromWorld = GridToWorld(from);
        Vector2 toWorld = GridToWorld(to);

        float dist = DirectionCosts[directionIndex] * CellSize;
        CellSample fromCell = GetCellSample(from);
        CellSample toCell = GetCellSample(to);
        float h1 = fromCell.Height;
        float h2 = toCell.Height;
        float slope = Mathf.Abs(h2 - h1) / dist;

        float riverWeight = toCell.RiverWeight;
        if (riverWeight > RoadConstants.RiverImpassableThreshold)
            return ImpassableCost;

        // Along-path grades above the traversable cap are unroadable; A* is
        // forced to zigzag/contour steep faces, which produces switchbacks.
        if (slope > RoadConstants.MaxTraversableGrade)
            return ImpassableCost;

        Heightmap.Biome biome = toCell.Biome;

        // Additive model: every passable hazard adds cost instead of
        // replacing it, so A* weighs slopes, rough ground, and water
        // together rather than seeing only the first hazard checked.
        // Grade cost is per-meter and quadratic in grade relative to the
        // comfort threshold, so long gentle contours beat short steep climbs.
        float gradeRatio = slope / RoadConstants.GradeComfortThreshold;
        float cost = BaseCost * dist + dist * SlopeMultiplier * gradeRatio * gradeRatio;

        float waterCost = GetWaterCost(h2, biome);
        if (float.IsPositiveInfinity(waterCost))
            return ImpassableCost;
        cost += waterCost;

        // Splined roads travel the ground BETWEEN cell centers, so sample the
        // move's interior too — otherwise narrow dips or river edges between
        // two dry cells put the finished road underwater.
        int interiorSamples = Mathf.Max(1, Mathf.RoundToInt(dist / RoadConstants.MoveInteriorSampleSpacing) - 1);
        for (int s = 1; s <= interiorSamples; s++)
        {
            float t = (float)s / (interiorSamples + 1);
            float ix = fromWorld.x + (toWorld.x - fromWorld.x) * t;
            float iy = fromWorld.y + (toWorld.y - fromWorld.y) * t;

            TotalTerrainSamples += 3;
            m_worldGen.GetRiverWeight(ix, iy, out float interiorRiver, out _);
            if (interiorRiver > RoadConstants.RiverImpassableThreshold)
                return ImpassableCost;

            float interiorCost = GetWaterCost(
                m_worldGen.GetHeight(ix, iy),
                m_worldGen.GetBiome(ix, iy));
            if (float.IsPositiveInfinity(interiorCost))
                return ImpassableCost;
            cost += interiorCost;
        }

        if (slope > SteepSlopeThreshold)
            cost += SteepSlopePenalty;

        if (biome == Heightmap.Biome.Mountain && slope > RoadConstants.MountainSlopeThreshold)
            cost += MountainSteepSlopePenalty;

        if (toCell.Variance > TerrainVarianceThreshold)
        {
            // Swamps are uniformly lumpy; halve the penalty so they don't
            // become de-facto blockers.
            cost += biome == Heightmap.Biome.Swamp
                ? TerrainVariancePenalty * 0.5f
                : TerrainVariancePenalty;
        }

        if (riverWeight > 0f)
            cost += RiverPenalty * riverWeight;

        return cost;
    }

    /// <summary>
    /// Water cost for one terrain sample: impassable below deep water or
    /// (outside swamps) below the waterline clearance margin; wadeable at a
    /// penalty in swamp shallows.
    /// </summary>
    private float GetWaterCost(float height, Heightmap.Biome biome)
    {
        if (height < RoadConstants.DeepWaterHeight)
            return ImpassableCost;

        if (biome == Heightmap.Biome.Swamp)
            return height < RoadConstants.ShallowWaterHeight ? SwampShallowWaterPenalty : 0f;

        if (height < RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance)
            return ImpassableCost;

        return 0f;
    }

    private readonly List<HashSet<Vector2i>> m_pockets = new();
    private readonly Dictionary<Vector2i, int> m_pocketOf = new();

    /// <summary>
    /// True unless the target provably sits in a small passable pocket that
    /// does not contain the start. Fills with the same edge rules as A*
    /// (including fords), capped; memoizes completed pockets.
    /// </summary>
    private bool PrepassReachable(Vector2i startGrid, Vector2i endGrid)
    {
        if (m_pocketOf.TryGetValue(endGrid, out int knownPocket))
            return m_pockets[knownPocket].Contains(startGrid);

        HashSet<Vector2i> visited = new() { endGrid };
        Queue<Vector2i> frontier = new();
        frontier.Enqueue(endGrid);
        bool foundStart = false;

        while (frontier.Count > 0)
        {
            if (visited.Count > PrepassFillCap)
                return true; // large component — unknown, let A* decide

            Vector2i current = frontier.Dequeue();
            if (current == startGrid)
                foundStart = true;

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2i neighbor = new(current.x + Directions[i].x, current.y + Directions[i].y);
                float moveCost = GetMoveCost(current, neighbor, i);

                if (float.IsPositiveInfinity(moveCost))
                {
                    if (!IsRiverBlocked(neighbor))
                        continue;
                    if (!TryGetShortRiverCrossing(current, Directions[i], out Vector2i landing, out _))
                        continue;
                    neighbor = landing;
                }

                if (visited.Add(neighbor))
                    frontier.Enqueue(neighbor);
            }
        }

        // Fill completed under the cap: this is a closed pocket. Memoize it.
        int pocketId = m_pockets.Count;
        m_pockets.Add(visited);
        foreach (Vector2i cell in visited)
            m_pocketOf[cell] = pocketId;

        return foundStart;
    }

    private bool IsRiverBlocked(Vector2i grid)
    {
        return GetCellSample(grid).RiverWeight > RoadConstants.RiverImpassableThreshold;
    }

    private bool IsValidFordLanding(Vector2i grid)
    {
        Vector2 world = GridToWorld(grid);

        m_worldGen.GetRiverWeight(world.x, world.y, out float riverWeight, out _);
        if (riverWeight > RoadConstants.RiverImpassableThreshold)
            return false;

        return m_worldGen.GetHeight(world.x, world.y)
            >= RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
    }

    /// <summary>
    /// Looks for dry ground on the far side of a river core along one
    /// direction. Succeeds only when the jump stays within the ford cap
    /// measured in world meters, so long diagonal directions don't stretch it.
    /// </summary>
    private bool TryGetShortRiverCrossing(
        Vector2i from, Vector2Int direction, out Vector2i landing, out float crossingCost)
    {
        landing = from;
        crossingCost = 0f;

        float maxFordDistance = RoadConstants.MaxRiverCrossingCells * CellSize;

        for (int step = 1; step <= RoadConstants.MaxRiverCrossingCells; step++)
        {
            Vector2i check = new Vector2i(from.x + direction.x * step, from.y + direction.y * step);

            if (IsRiverBlocked(check))
                continue;

            if (!IsValidFordLanding(check))
                return false;

            float distance = Vector2.Distance(GridToWorld(from), GridToWorld(check));
            if (distance > maxFordDistance)
                return false;

            landing = check;
            crossingCost = BaseCost * distance + RoadConstants.RiverCrossingPenalty + distance * 10f;
            return true;
        }

        return false;
    }

    private List<Vector2> ReconstructPath(Dictionary<Vector2i, Vector2i> cameFrom, Vector2i current, Vector2 start, Vector2 end)
    {
        List<Vector2> path = new List<Vector2> { end };

        while (cameFrom.ContainsKey(current))
        {
            path.Add(GridToWorld(current));
            current = cameFrom[current];
        }

        path.Add(start);
        path.Reverse();
        return path;
    }
}
