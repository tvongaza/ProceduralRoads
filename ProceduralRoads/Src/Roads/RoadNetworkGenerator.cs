using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Where a road ends when the point on its location's radius
/// circle turns out to be wet (Tys, end of 2 Sep 2026).</summary>
public enum WetTerminusMode
{
    /// <summary>End at the last dry point short of the circle.</summary>
    Trim,
    /// <summary>End at the nearest dry point on the circle, so the road still
    /// reaches the location.</summary>
    Reroute,
    /// <summary>No road to a location whose approach is wet.</summary>
    Drop,
}

/// <summary>
/// Orchestrates road network generation after POI locations are known.
/// </summary>
public static class RoadNetworkGenerator
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;
    private const float WorldRadius = 10000f;
    
    private static readonly HashSet<string> BossLocationNames = new HashSet<string>
    {
        "Eikthyrnir",
        "GDKing",
        "Bonemass",
        "Dragonqueen",
        "GoblinKing",
        "SeekerQueen",
    };

    private static readonly Dictionary<string, int> LocationPriorities = new()
    {
        { "Eikthyrnir", 100 },
        { "GDKing", 100 },
        { "Bonemass", 100 },
        { "Dragonqueen", 100 },
        { "GoblinKing", 100 },
        { "SeekerQueen", 100 },
        
        { "Crypt4", 80 },
        { "SunkenCrypt4", 80 },
        { "MountainCave02", 80 },
        { "TrollCave02", 40 },
        { "Crypt3", 75 },
        
        { "Mistlands_DvergrTownEntrance1", 75 },
        { "Mistlands_DvergrTownEntrance2", 75 },
        { "Mistlands_Harbour1", 70 },
        
        { "WoodVillage1", 60 },
        { "WoodFarm1", 55 },
        
        { "Mistlands_GuardTower1_new", 50 },
        { "Mistlands_GuardTower2_new", 50 },
        { "Mistlands_GuardTower3_new", 50 },
        { "Mistlands_Lighthouse1_new", 50 },
        { "Mistlands_Excavation1", 45 },
        { "Mistlands_Excavation2", 45 },
        { "Mistlands_Excavation3", 45 },
        
        { "StoneTower1", 40 },
        { "StoneTower3", 40 },
        
        { "Mistlands_GuardTower1_ruined_new", 30 },
        { "Mistlands_GuardTower3_ruined_new", 30 },
        { "StoneTowerRuins03", 30 },
        { "StoneTowerRuins04", 30 },
        { "StoneTowerRuins05", 30 },
        { "StoneTowerRuins07", 30 },
        { "StoneTowerRuins08", 30 },
        { "StoneTowerRuins09", 30 },
        { "StoneTowerRuins10", 30 },
        { "StoneHenge1", 25 },
        { "StoneHenge2", 25 },
        { "StoneHenge3", 25 },
        { "SwampHut5", 25 },
        { "SwampRuin1", 25 },
        { "SwampRuin2", 25 },
    };
    
    private const int DefaultPriority = 20;
    private const int CustomLocationPriority = 80;
    private const int MinLocationsPerIsland = 2;
    public static int MaxLocationsPerIsland = 12;
    private const float AreaPerLocation = 2_000_000f;
    private const float MaxRoadLinkDistance = 2200f;
    private const int MaxFailedEdgeAttemptsPerIsland = 24;

    /// <summary>
    /// Location names registered via API or config for road generation.
    /// </summary>
    private static readonly Dictionary<string, int> RegisteredLocationPriorities = new Dictionary<string, int>(StringComparer.Ordinal);

    #region Location Registration API

    /// <summary>
    /// Register a location name for road generation.
    /// Call this from other mods to include custom locations in the road network.
    /// </summary>
    public static void RegisterLocation(string locationName)
    {
        RegisterLocation(locationName, CustomLocationPriority);
    }

    /// <summary>
    /// Register a location name with an explicit road endpoint priority.
    /// Higher priority locations are preferred when an island has too many endpoints.
    /// </summary>
    public static void RegisterLocation(string locationName, int priority)
    {
        if (string.IsNullOrWhiteSpace(locationName))
            return;
        
        string trimmed = locationName.Trim();
        int clampedPriority = Mathf.Clamp(priority, 0, 100);
        bool isNew = !RegisteredLocationPriorities.ContainsKey(trimmed);
        RegisteredLocationPriorities[trimmed] = clampedPriority;
        if (isNew)
        {
            Log.LogDebug($"Registered location for roads: {trimmed} (priority {clampedPriority})");
        }
        else
        {
            Log.LogDebug($"Updated road location priority: {trimmed} (priority {clampedPriority})");
        }
    }

    /// <summary>
    /// Unregister a location name from road generation.
    /// </summary>
    public static void UnregisterLocation(string locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName))
            return;
        
        string trimmed = locationName.Trim();
        if (RegisteredLocationPriorities.Remove(trimmed))
        {
            Log.LogDebug($"Unregistered location from roads: {trimmed}");
        }
    }

    /// <summary>
    /// Get all currently registered location names.
    /// </summary>
    public static IReadOnlyCollection<string> GetRegisteredLocations()
    {
        return RegisteredLocationPriorities.Keys.ToList();
    }

    /// <summary>
    /// Get all currently registered location names and road endpoint priorities.
    /// </summary>
    public static IReadOnlyDictionary<string, int> GetRegisteredLocationPriorities()
    {
        return new Dictionary<string, int>(RegisteredLocationPriorities);
    }

    #endregion

    public static float RoadWidth = 4f;
    public static int IslandRoadPercentage = 50;

    /// <summary>Roads stop at a location's exterior radius, which for a
    /// crypt is 25 m of swamp around a small mound: the road ends far from
    /// the door (Tys, 2 Sep 2026). Compact locations get a tighter approach
    /// radius so the road runs up to the entrance.</summary>
    public const float CryptApproachRadius = 8f;
    public static float ApproachRadius(string prefabName, float exteriorRadius) =>
        prefabName.StartsWith("SunkenCrypt", StringComparison.Ordinal) || prefabName.StartsWith("Crypt", StringComparison.Ordinal)
            ? Mathf.Min(exteriorRadius, CryptApproachRadius)
            : exteriorRadius;

    /// <summary>Two locations whose circles are closer than this are one
    /// place for road purposes: they count as connected without a road,
    /// instead of a few metres of paint between them (the crypt-to-crypt
    /// stubs of RoadTestMac2, 2-27 m long).</summary>
    public const float MinUsefulRoadLength = 30f;

    private static readonly List<(string name, Vector3 position, float radius)> m_roadLocations = new();
    /// <summary>Locations the current generation connected (debug rings);
    /// empty after a load from ZDO, which stores routes only.</summary>
    public static IReadOnlyList<(string name, Vector3 position, float radius)> GetRoadLocations() => m_roadLocations;
    /// <summary>Stair runs (steep sections become staircases) are their own
    /// line of work on branch pc/stairs; the bridge work keeps them off so
    /// stairs cannot bend routes or pieces around crossings (Tys, 2 Sep 2026).
    /// Config "Stairs/Enabled".</summary>
    public static bool StairsEnabled = false;

    /// <summary>Player-facing lever (config "Roads/WetTerminus"): what to do
    /// with a route whose end on its location's radius circle is in water.</summary>
    public static WetTerminusMode WetTerminus = WetTerminusMode.Reroute;

    private static bool m_roadsGenerated = false;
    private static bool m_locationsReady = false;
    private static bool m_roadsLoadedFromZDO = false;
    private static RoadPathfinder? m_pathfinder;
    private static int m_roadsGeneratedCount = 0;
    private static List<(Vector2 position, string label)> m_roadStartPoints = new();
    private static List<RoadRoute> m_roadRoutes = new List<RoadRoute>();
    private static List<RoadCrossing> m_roadCrossings = new List<RoadCrossing>();
    private static List<StairRun> m_stairRuns = new List<StairRun>();

    public static bool RoadsGenerated => m_roadsGenerated;
    public static bool IsLocationsReady => m_locationsReady;
    public static bool RoadsLoadedFromZDO => m_roadsLoadedFromZDO;
    public static bool RoadsAvailable => m_roadsGenerated || m_roadsLoadedFromZDO;

    /// <summary>
    /// Get the start points of all generated roads for visualization.
    /// </summary>
    public static IReadOnlyList<(Vector2 position, string label)> GetRoadStartPoints() => m_roadStartPoints;

    /// <summary>
    /// Get ordered centerlines for generated roads.
    /// </summary>
    public static IReadOnlyList<RoadRoute> GetRoadRoutes() => m_roadRoutes;

    public static IReadOnlyList<RoadCrossing> GetRoadCrossings() => m_roadCrossings;

    public static IReadOnlyList<StairRun> GetStairRuns() => m_stairRuns;

    public static string GetRoadRouteLabel(int routeIndex)
    {
        if (routeIndex < 0 || routeIndex >= m_roadRoutes.Count)
        {
            return "";
        }

        return m_roadRoutes[routeIndex].Label;
    }

    public static List<Vector3> GetRoadRouteWaypoints(int routeIndex, float spacing, bool reverse)
    {
        if (routeIndex < 0 || routeIndex >= m_roadRoutes.Count)
        {
            return new List<Vector3>();
        }

        return m_roadRoutes[routeIndex].Resample(spacing, reverse);
    }

    public static int FindNearestRoadRouteIndex(Vector3 position, float radius)
    {
        float radiusSquared = radius * radius;
        float bestDistanceSquared = float.MaxValue;
        int bestIndex = -1;
        Vector2 position2D = new Vector2(position.x, position.z);

        for (int routeIndex = 0; routeIndex < m_roadRoutes.Count; routeIndex++)
        {
            RoadRoute route = m_roadRoutes[routeIndex];
            for (int pointIndex = 0; pointIndex < route.Points.Count; pointIndex++)
            {
                Vector3 point = route.Points[pointIndex];
                Vector2 point2D = new Vector2(point.x, point.z);
                float distanceSquared = (point2D - position2D).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestIndex = routeIndex;
                }
            }
        }

        if (bestIndex < 0 || bestDistanceSquared > radiusSquared)
        {
            return -1;
        }

        return bestIndex;
    }

    public static void Initialize() => Reset();

    /// <summary>
    /// Called when location generation is complete. Does not trigger road generation.
    /// </summary>
    public static void MarkLocationsReady()
    {
        m_locationsReady = true;
        Log.LogDebug("Locations marked ready for road generation");
    }

    /// <summary>
    /// Called when roads have been loaded from ZDO persistence (existing world).
    /// </summary>
    public static void MarkRoadsLoadedFromZDO()
    {
        m_roadsLoadedFromZDO = true;
        Log.LogDebug("Roads marked as loaded from ZDO persistence");
    }

    /// <summary>
    /// Main entry point for road generation. Calls various generation methods.
    /// </summary>
    /// <param name="force">If true, regenerate roads even if already generated (for existing worlds)</param>
    public static void GenerateRoads(bool force = false)
    {
        if (m_roadsGenerated && !force)
        {
            Log.LogDebug("Roads already generated, skipping");
            return;
        }
        
        if (force && (m_roadsGenerated || m_roadsLoadedFromZDO || RoadSpatialGrid.IsInitialized))
        {
            Log.LogDebug("Force regenerating roads...");
            Reset();
        }

        if (WorldGenerator.instance == null)
        {
            Log.LogWarning("WorldGenerator not available, cannot generate roads");
            return;
        }

        if (ZoneSystem.instance == null)
        {
            Log.LogWarning("ZoneSystem not available, cannot generate roads");
            return;
        }

        Log.LogDebug("Starting road network generation...");

        // Merge config-defined custom locations into registered set
        var configLocations = ProceduralRoadsPlugin.GetConfigLocationNames();
        foreach (string locName in configLocations)
        {
            if (!RegisteredLocationPriorities.ContainsKey(locName))
            {
                RegisterLocation(locName, CustomLocationPriority);
                Log.LogDebug($"Added config location: {locName}");
            }
        }

        DateTime startTime = DateTime.Now;
        m_pathfinder = new RoadPathfinder(WorldGenerator.instance);
        m_roadsGeneratedCount = 0;

        var locations = GatherLocationData();
        if (locations == null)
            return;

        List<Island> islands = IslandDetector.DetectIslands();
        List<IslandCandidate> islandCandidates = BuildIslandCandidates(islands, locations.Value.AllLocations, locations.Value.SpawnPoint);
        List<IslandCandidate> selectedIslands = SelectBalancedIslands(islandCandidates, IslandRoadPercentage);

        Log.LogDebug(
            $"Islands: {islands.Count} total, {islandCandidates.Count} eligible, {selectedIslands.Count} selected ({IslandRoadPercentage}%)");

        foreach (IslandCandidate candidate in selectedIslands)
        {
            Island island = candidate.Island;
            List<(string name, Vector3 position, float radius)> islandLocations = candidate.Locations;
            int maxLocs = GetMaxLocationsForIsland(candidate.Island);
            List<(string name, Vector3 position, float radius)> selected = SelectLocations(islandLocations, maxLocs);
            m_roadLocations.AddRange(selected);
            
            Log.LogDebug(
                $"Island {island.Id}: {islandLocations.Count} candidates -> {selected.Count} selected " +
                $"(max {maxLocs}, area {island.ApproxArea/1_000_000:F1}km², ring {candidate.Ring})");
            
            DateTime islandStart = DateTime.Now;
            long iterationsBefore = RoadPathfinder.TotalIterations;
            int roadsBefore = m_roadsGeneratedCount;

            if (candidate.IsStarterIsland)
            {
                GenerateIslandRoads(island, selected,
                    locations.Value.SpawnPoint, locations.Value.SpawnRadius);
            }
            else
            {
                GenerateIslandRoads(island, selected);
            }

            Log.LogInfo(
                $"[TIMING] island={island.Id} ms={(DateTime.Now - islandStart).TotalMilliseconds:F0} " +
                $"roads={m_roadsGeneratedCount - roadsBefore}/{selected.Count} " +
                $"iterations={RoadPathfinder.TotalIterations - iterationsBefore} " +
                $"area={island.ApproxArea / 1_000_000f:F1}km2 ring={candidate.Ring}");
        }

        TimeSpan elapsed = DateTime.Now - startTime;
        LogGenerationStats(m_roadsGeneratedCount, elapsed);

        RoadSpatialGrid.FinalizeRoadNetwork();
        
        m_roadsGenerated = true;
        m_pathfinder = null;
        
        RoadNetworkPersistence.EnsureMetadataInstance();
    }

    #region Core Road Generation Primitive

    /// <summary>
    /// Core primitive: Generates a single road between two points.
    /// Handles pathfinding, radius trimming, and adding to the spatial grid.
    /// </summary>
    /// <param name="startCenter">Center of the start location</param>
    /// <param name="startRadius">Exterior radius of start location (road starts at edge)</param>
    /// <param name="endCenter">Center of the end location</param>
    /// <param name="endRadius">Exterior radius of end location (road ends at edge)</param>
    /// <param name="width">Width of the road</param>
    /// <param name="label">Optional label for logging</param>
    /// <returns>True if road was successfully generated</returns>
    public static bool GenerateRoad(
        Vector2 startCenter, float startRadius,
        Vector2 endCenter, float endRadius,
        float width, string? label = null)
    {
        if (m_pathfinder == null)
        {
            Log.LogWarning("GenerateRoad called without active pathfinder");
            return false;
        }

        Vector2 pathStart = GetNearestPathablePoint(startCenter, startRadius);
        Vector2 pathEnd = GetNearestPathablePoint(endCenter, endRadius);
        List<Vector2>? path = m_pathfinder.FindPath(pathStart, pathEnd);

        UnityEngine.Canvas.ForceUpdateCanvases();

        if (path == null || path.Count < 2)
        {
            if (label != null)
                Log.LogWarning($"Could not find path: {label}");
            return false;
        }

        path = TrimPathToRadii(path, startCenter, startRadius, endCenter, endRadius);

        if (path == null || path.Count < 2)
        {
            if (label != null)
                Log.LogWarning($"Path too short after trimming: {label}");
            return false;
        }

        // A route whose wet ends were trimmed down to a few metres of dry
        // hummock (swamp crypt to swamp crypt) is paint, not a road.
        float trimmedLength = 0f;
        for (int i = 1; i < path.Count; i++)
            trimmedLength += Vector2.Distance(path[i - 1], path[i]);
        if (trimmedLength < MinUsefulRoadLength)
        {
            Log.LogDebug($"Route dropped: {trimmedLength:F0} m left after trimming wet ends ({label})");
            return false;
        }

        // Rivers are crossed but never paved (crossings become bridge ruins)
        // and steep sections become staircases with untouched ground: paint
        // and level only the ordinary road spans between those exclusions.
        List<RoadCrossing> crossings = RoadCrossingDetector.Detect(path, WorldGenerator.instance);
        List<StairRun> stairRuns = StairsEnabled
            ? StairRunDetector.Detect(path, WorldGenerator.instance)
            : new List<StairRun>();

        // A stair run that ends at a crossing's dry point descends the rest of
        // the way to the abutment at the water's edge, so stairs meet the deck
        // the same way painted road does.
        foreach (StairRun run in stairRuns)
        {
            foreach (RoadCrossing crossing in crossings)
            {
                if (run.ToIndex == crossing.FromIndex && Vector2.Distance(run.ToPos, crossing.FromBank) > 0.5f)
                {
                    run.Points.Add(crossing.FromBank);
                    run.ToPos = crossing.FromBank;
                }
                if (run.FromIndex == crossing.ToIndex && Vector2.Distance(run.FromPos, crossing.ToBank) > 0.5f)
                {
                    run.Points.Insert(0, crossing.ToBank);
                    run.FromPos = crossing.ToBank;
                }
            }
        }

        // Crossings carry their abutment points (the water's edge): the land
        // segment before a crossing runs on to FromBank and the one after it
        // starts at ToBank, so painted road meets the deck — no bridge to
        // nowhere, no deck starting up a dry hillside.
        List<(int from, int to, Vector2? lead, Vector2? resume)> exclusions = new();
        foreach (RoadCrossing crossing in crossings)
        {
            // Raised fords are ordinary leveled road; wading fords are painted
            // separately at terrain height; bridges and spans exclude painting.
            if (crossing.Kind == CrossingKind.Ford && crossing.Style == FordStyle.Raise)
                continue;
            exclusions.Add((crossing.FromIndex, crossing.ToIndex, crossing.FromBank, crossing.ToBank));
            if (crossing.Kind == CrossingKind.Ford && crossing.Style == FordStyle.Wade)
                RoadSpatialGrid.AddRoadPath(new List<Vector2> { crossing.FromBank, crossing.ToBank }, width, WorldGenerator.instance, followTerrain: true);
        }
        foreach (StairRun stairRun in stairRuns)
            exclusions.Add((stairRun.FromIndex, stairRun.ToIndex, null, null));
        exclusions.Sort((x, y) => x.from.CompareTo(y.from));

        if (exclusions.Count == 0)
        {
            RoadSpatialGrid.AddRoadPath(path, width, WorldGenerator.instance);
        }
        else
        {
            int cursor = 0;
            Vector2? resumeAt = null;
            foreach ((int from, int to, Vector2? lead, Vector2? resume) in exclusions)
            {
                if (from > cursor)
                {
                    List<Vector2> landSegment = path.GetRange(cursor, from - cursor + 1);
                    if (resumeAt.HasValue && Vector2.Distance(resumeAt.Value, landSegment[0]) > 0.5f)
                        landSegment.Insert(0, resumeAt.Value);
                    if (lead.HasValue && Vector2.Distance(lead.Value, landSegment[landSegment.Count - 1]) > 0.5f)
                        landSegment.Add(lead.Value);
                    if (landSegment.Count >= 2)
                        RoadSpatialGrid.AddRoadPath(landSegment, width, WorldGenerator.instance);
                }
                if (to >= cursor)
                    resumeAt = resume;
                cursor = Mathf.Max(cursor, to);
            }

            List<Vector2> tail = path.GetRange(cursor, path.Count - cursor);
            if (resumeAt.HasValue && Vector2.Distance(resumeAt.Value, tail[0]) > 0.5f)
                tail.Insert(0, resumeAt.Value);
            if (tail.Count >= 2)
                RoadSpatialGrid.AddRoadPath(tail, width, WorldGenerator.instance);
        }

        m_roadsGeneratedCount++;

        if (path.Count > 0)
        {
            string pinLabel = label ?? $"Road {m_roadsGeneratedCount}";
            m_roadStartPoints.Add((path[0], pinLabel));
            RoadRoute route = RoadRoute.FromWaypoints(m_roadRoutes.Count, pinLabel, width, path, WorldGenerator.instance);
            m_roadRoutes.Add(route);

            foreach (RoadCrossing crossing in crossings)
            {
                crossing.RouteIndex = route.Index;
                m_roadCrossings.Add(crossing);
            }

            foreach (StairRun stairRun in stairRuns)
            {
                stairRun.RouteIndex = route.Index;
                m_stairRuns.Add(stairRun);
            }
        }

        if (label != null)
            Log.LogDebug($"Generated road: {label} ({path.Count} waypoints)");

        return true;
    }

    /// <summary>
    /// Convenience overload using Vector3 positions (extracts X/Z as Vector2).
    /// </summary>
    public static bool GenerateRoad(
        Vector3 startPos, float startRadius,
        Vector3 endPos, float endRadius,
        float width, string? label = null)
    {
        return GenerateRoad(
            new Vector2(startPos.x, startPos.z), startRadius,
            new Vector2(endPos.x, endPos.z), endRadius,
            width, label);
    }

    #endregion

    #region Location Data

    public struct LocationData
    {
        public Vector3 SpawnPoint;
        public float SpawnRadius;
        public List<(string name, Vector3 position, float radius)> BossLocations;
        public List<(string name, Vector3 position, float radius)> AllLocations;
    }

    private sealed class IslandCandidate
    {
        public Island Island = null!;
        public List<(string name, Vector3 position, float radius)> Locations = new();
        public bool IsStarterIsland;
        public int Ring;
    }

    private static LocationData? GatherLocationData()
    {
        var locationInstances = ZoneSystem.instance.GetLocationList();
        if (locationInstances == null || locationInstances.Count == 0)
        {
            Log.LogWarning("No location instances found");
            return null;
        }

        Vector3? spawnPoint = null;
        float spawnRadius = 0f;
        var bossLocations = new List<(string name, Vector3 position, float radius)>();
        var allLocations = new List<(string name, Vector3 position, float radius)>();

        foreach (var loc in locationInstances)
        {
            string prefabName = loc.m_location.m_prefab.Name;
            float exteriorRadius = ApproachRadius(prefabName, loc.m_location.m_exteriorRadius);

            allLocations.Add((prefabName, loc.m_position, exteriorRadius));

            if (prefabName == "StartTemple")
            {
                spawnPoint = loc.m_position;
                spawnRadius = exteriorRadius;
            }
            else if (BossLocationNames.Contains(prefabName))
            {
                bossLocations.Add((prefabName, loc.m_position, exteriorRadius));
            }
        }

        if (!spawnPoint.HasValue)
        {
            Log.LogWarning("Could not find spawn point (StartTemple)");
            spawnPoint = Vector3.zero;
        }

        Log.LogDebug(
            $"Found spawn at {spawnPoint.Value}, {bossLocations.Count} boss locations, {allLocations.Count} total locations");

        return new LocationData
        {
            SpawnPoint = spawnPoint.Value,
            SpawnRadius = spawnRadius,
            BossLocations = bossLocations,
            AllLocations = allLocations
        };
    }

    private static List<(string name, Vector3 position, float radius)> GetLocationsOnIsland(
        Island island, List<(string name, Vector3 position, float radius)> allLocations)
    {
        var result = new List<(string name, Vector3 position, float radius)>();
        foreach (var loc in allLocations)
        {
            if (!island.ContainsPoint(loc.position) || !IsRoadLocation(loc.name))
                continue;

            Vector2 position = new Vector2(loc.position.x, loc.position.z);
            if (!HasNearbyPathablePoint(position, loc.radius))
                continue;

            bool duplicate = result.Any(existing =>
                existing.name == loc.name &&
                Vector3.SqrMagnitude(existing.position - loc.position) < RoadConstants.PathfindingCellSize * RoadConstants.PathfindingCellSize);
            if (duplicate)
                continue;

            result.Add(loc);
        }
        return result;
    }

    private static bool IsRoadLocation(string locationName)
    {
        return BossLocationNames.Contains(locationName) ||
               LocationPriorities.ContainsKey(locationName) ||
               RegisteredLocationPriorities.ContainsKey(locationName);
    }

    private static List<IslandCandidate> BuildIslandCandidates(
        List<Island> islands,
        List<(string name, Vector3 position, float radius)> allLocations,
        Vector3 spawnPoint)
    {
        List<IslandCandidate> candidates = new List<IslandCandidate>();
        foreach (Island island in islands)
        {
            List<(string name, Vector3 position, float radius)> islandLocations = GetLocationsOnIsland(island, allLocations);
            if (islandLocations.Count == 0)
                continue;

            candidates.Add(new IslandCandidate
            {
                Island = island,
                Locations = islandLocations,
                IsStarterIsland = island.ContainsPoint(spawnPoint),
                Ring = GetIslandRing(island)
            });
        }

        return candidates;
    }

    private static int GetIslandRing(Island island)
    {
        float distanceFromCenter = island.Center.magnitude;
        float normalizedDistance = Mathf.Clamp01(distanceFromCenter / WorldRadius);
        if (normalizedDistance < 0.33f)
            return 0;
        if (normalizedDistance < 0.66f)
            return 1;
        return 2;
    }

    private static List<IslandCandidate> SelectBalancedIslands(List<IslandCandidate> candidates, int percentage)
    {
        if (candidates.Count == 0)
            return new List<IslandCandidate>();

        // 0 means no roads at all — the pristine-fixture flow depends on it
        // (world-fixture.sh: create with roads off, snapshot, regenerate on
        // every run). Any positive percentage still gets at least one island.
        if (percentage <= 0)
            return new List<IslandCandidate>();

        int targetCount = Mathf.Max(1, Mathf.RoundToInt(candidates.Count * percentage / 100f));
        if (targetCount >= candidates.Count)
            return candidates.OrderByDescending(candidate => candidate.Island.ApproxArea).ToList();

        List<IslandCandidate> selected = new List<IslandCandidate>();
        HashSet<int> selectedIslandIds = new HashSet<int>();

        IslandCandidate? starterIsland = candidates.FirstOrDefault(candidate => candidate.IsStarterIsland);
        if (starterIsland != null)
        {
            selected.Add(starterIsland);
            selectedIslandIds.Add(starterIsland.Island.Id);
        }

        List<List<IslandCandidate>> rings = new List<List<IslandCandidate>>();
        for (int ring = 0; ring < 3; ring++)
        {
            List<IslandCandidate> ringCandidates = candidates
                .Where(candidate => candidate.Ring == ring && !selectedIslandIds.Contains(candidate.Island.Id))
                .OrderByDescending(candidate => candidate.Island.ApproxArea)
                .ToList();
            rings.Add(ringCandidates);
        }

        int[] ringIndexes = new int[rings.Count];
        while (selected.Count < targetCount)
        {
            bool addedAny = false;
            for (int ring = 0; ring < rings.Count && selected.Count < targetCount; ring++)
            {
                List<IslandCandidate> ringCandidates = rings[ring];
                if (ringIndexes[ring] >= ringCandidates.Count)
                    continue;

                IslandCandidate candidate = ringCandidates[ringIndexes[ring]++];
                selected.Add(candidate);
                selectedIslandIds.Add(candidate.Island.Id);
                addedAny = true;
            }

            if (!addedAny)
                break;
        }

        return selected;
    }

    private static int GetMaxLocationsForIsland(Island island)
    {
        int scaled = MinLocationsPerIsland + (int)(island.ApproxArea / AreaPerLocation);
        return Mathf.Clamp(scaled, MinLocationsPerIsland, MaxLocationsPerIsland);
    }

    private static List<(string name, Vector3 position, float radius)> SelectLocations(
        List<(string name, Vector3 position, float radius)> candidates, int maxCount)
    {
        if (candidates.Count <= maxCount)
            return candidates;
        
        List<(string name, Vector3 position, float radius)> selected = new List<(string name, Vector3 position, float radius)>();
        List<(string name, Vector3 position, float radius)> remaining = candidates
            .OrderByDescending(location => GetLocationPriority(location.name))
            .ToList();

        selected.Add(remaining[0]);
        remaining.RemoveAt(0);

        while (selected.Count < maxCount && remaining.Count > 0)
        {
            int bestIndex = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                (string name, Vector3 position, float radius) candidate = remaining[i];
                float minDistanceToSelected = selected.Min(location => Vector3.Distance(location.position, candidate.position));
                float distancePenalty = Mathf.Min(minDistanceToSelected, MaxRoadLinkDistance) * 0.05f;
                float score = GetLocationPriority(candidate.name) * 100f - distancePenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            selected.Add(remaining[bestIndex]);
            remaining.RemoveAt(bestIndex);
        }

        return selected;
    }

    private static int GetLocationPriority(string locationName)
    {
        if (LocationPriorities.TryGetValue(locationName, out int priority))
            return priority;

        if (RegisteredLocationPriorities.TryGetValue(locationName, out int registeredPriority))
            return registeredPriority;

        return DefaultPriority;
    }

    #endregion

    #region Island Road Strategies

    private static void GenerateIslandRoads(
        Island island,
        List<(string name, Vector3 position, float radius)> islandLocations,
        Vector3? overrideStart = null,
        float overrideStartRadius = 0f)
    {
        if (islandLocations.Count == 0) return;
        
        Vector3 startPos;
        float startRadius;
        string startName;
        List<(string name, Vector3 position, float radius)> roadLocations = islandLocations;
        if (overrideStart.HasValue)
        {
            startPos = overrideStart.Value;
            startRadius = overrideStartRadius;
            startName = "Start";
        }
        else
        {
            (string name, Vector3 position, float radius) anchor = SelectIslandAnchor(island, islandLocations);
            startPos = anchor.position;
            startRadius = anchor.radius;
            startName = anchor.name;
            roadLocations = islandLocations
                .Where(location => !SameLocation(location, anchor))
                .ToList();

            if (roadLocations.Count == 0)
            {
                Log.LogDebug($"Island {island.Id}: skipped single-location island anchored at {anchor.name}");
                return;
            }
        }

        Log.LogDebug(
            $"Island {island.Id}: {islandLocations.Count} locations, strategy=ReachableMST, anchor={startName}");

        GenerateReachableRoads(startPos, startRadius, roadLocations, startName);
    }

    private static void GenerateReachableRoads(
        Vector3 startPos,
        float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        if (locations.Count == 0)
            return;

        List<(string name, Vector3 position, float radius)> nodes = new List<(string name, Vector3 position, float radius)>
        {
            (startName, startPos, startRadius)
        };
        nodes.AddRange(locations);

        HashSet<int> connected = new HashSet<int> { 0 };
        HashSet<int> remaining = new HashSet<int>(Enumerable.Range(1, nodes.Count - 1));
        HashSet<string> failedEdges = new HashSet<string>();
        int maxAttempts = Mathf.Min(nodes.Count * nodes.Count, MaxFailedEdgeAttemptsPerIsland);
        int attempts = 0;

        while (remaining.Count > 0 && attempts < maxAttempts)
        {
            int bestFrom = -1;
            int bestTo = -1;
            float bestScore = float.MaxValue;

            foreach (int fromIndex in connected)
            {
                foreach (int toIndex in remaining)
                {
                    string edgeKey = GetEdgeKey(fromIndex, toIndex);
                    if (failedEdges.Contains(edgeKey))
                        continue;

                    float distance = Vector3.Distance(nodes[fromIndex].position, nodes[toIndex].position);
                    if (distance > MaxRoadLinkDistance)
                        continue;

                    float priorityBonus = GetLocationPriority(nodes[toIndex].name) * 20f;
                    float score = distance - priorityBonus;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestFrom = fromIndex;
                        bestTo = toIndex;
                    }
                }
            }

            if (bestFrom < 0 || bestTo < 0)
            {
                PromoteNextComponentAnchor(nodes, connected, remaining);
                continue;
            }

            attempts++;
            (string name, Vector3 position, float radius) from = nodes[bestFrom];
            (string name, Vector3 position, float radius) to = nodes[bestTo];

            // Circles a short walk apart are one place: connected, no road.
            float edgeToEdge = Vector3.Distance(from.position, to.position) - from.radius - to.radius;
            if (edgeToEdge < MinUsefulRoadLength)
            {
                Log.LogDebug($"Adjacent locations, no road: {from.name} -> {to.name} ({edgeToEdge:F0} m between circles)");
                connected.Add(bestTo);
                remaining.Remove(bestTo);
                continue;
            }

            bool generated = GenerateRoad(from.position, from.radius, to.position, to.radius, RoadWidth,
                $"{from.name} -> {to.name}");

            if (generated)
            {
                connected.Add(bestTo);
                remaining.Remove(bestTo);
            }
            else
            {
                failedEdges.Add(GetEdgeKey(bestFrom, bestTo));
            }
        }

        if (failedEdges.Count > 0)
        {
            Log.LogDebug($"Skipped {failedEdges.Count} unreachable road edge attempt(s)");
        }

        if (remaining.Count > 0)
        {
            string skipped = string.Join(", ", remaining.Select(index => nodes[index].name).Distinct().Take(8));
            Log.LogDebug($"Skipped {remaining.Count} endpoint(s) after reaching edge attempt cap: {skipped}");
        }
    }

    private static void PromoteNextComponentAnchor(
        List<(string name, Vector3 position, float radius)> nodes,
        HashSet<int> connected,
        HashSet<int> remaining)
    {
        if (remaining.Count == 0)
            return;

        int nextAnchor = remaining
            .OrderByDescending(index => GetLocationPriority(nodes[index].name))
            .First();

        connected.Add(nextAnchor);
        remaining.Remove(nextAnchor);
        Log.LogDebug($"Started disconnected road component at {nodes[nextAnchor].name}");
    }

    private static string GetEdgeKey(int a, int b)
    {
        return a < b ? $"{a}:{b}" : $"{b}:{a}";
    }

    #endregion

    #region Utility Methods

    private static (string name, Vector3 position, float radius) SelectIslandAnchor(
        Island island,
        List<(string name, Vector3 position, float radius)> locations)
    {
        Vector3 islandCenter = new Vector3(island.Center.x, 0f, island.Center.y);
        return locations
            .OrderByDescending(location => GetLocationPriority(location.name))
            .ThenBy(location => Vector3.Distance(location.position, islandCenter))
            .First();
    }

    private static bool SameLocation(
        (string name, Vector3 position, float radius) a,
        (string name, Vector3 position, float radius) b)
    {
        return a.name == b.name &&
               Vector3.SqrMagnitude(a.position - b.position) < 1f;
    }

    private static Vector2 GetNearestPathablePoint(Vector2 center, float radius)
    {
        if (IsPathablePoint(center))
            return center;

        float searchRadius = Mathf.Max(radius + RoadConstants.PathfindingCellSize * 2f, RoadConstants.PathfindingCellSize * 2f);
        float bestDistance = float.MaxValue;
        Vector2 bestPoint = center;
        bool found = false;

        for (float currentRadius = RoadConstants.PathfindingCellSize; currentRadius <= searchRadius; currentRadius += RoadConstants.PathfindingCellSize)
        {
            int sampleCount = Mathf.Max(12, Mathf.CeilToInt(currentRadius / 8f));
            for (int i = 0; i < sampleCount; i++)
            {
                float angle = i * Mathf.PI * 2f / sampleCount;
                Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * currentRadius;
                if (!IsPathablePoint(candidate))
                    continue;

                float distance = (candidate - center).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPoint = candidate;
                    found = true;
                }
            }

            if (found)
                return bestPoint;
        }

        return center;
    }

    private static bool HasNearbyPathablePoint(Vector2 center, float radius)
    {
        return IsPathablePoint(GetNearestPathablePoint(center, radius));
    }

    private static bool IsPathablePoint(Vector2 point)
    {
        if (WorldGenerator.instance == null)
            return true;

        // Same floor as crossing banks and road points: the waterline plus
        // clearance. Endpoints between 30.5 and 31.25 used to be accepted and
        // produced the recurring below-waterline route ends.
        float height = BiomeBlendedHeight.GetBlendedHeight(point.x, point.y, WorldGenerator.instance);
        if (height < RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance)
            return false;

        WorldGenerator.instance.GetRiverWeight(point.x, point.y, out float riverWeight, out _);
        return riverWeight <= RoadConstants.RiverImpassableThreshold;
    }

    /// <summary>
    /// Debug helper: clears the road network and regenerates roads for ONLY
    /// the island containing worldPos. Locations are already persisted in the
    /// world save, so on a reloaded world this gives a seconds-long feedback
    /// loop for generation changes instead of a full-world pass.
    /// </summary>
    public static bool RegenerateIslandAt(Vector3 worldPos, out string summary)
    {
        if (WorldGenerator.instance == null || ZoneSystem.instance == null)
        {
            summary = "World not ready";
            return false;
        }

        var locations = GatherLocationData();
        if (locations == null)
        {
            summary = "No location data available";
            return false;
        }

        var islands = IslandDetector.DetectIslands();
        Island? island = islands.FirstOrDefault(i => i.ContainsPoint(worldPos));
        if (island == null)
        {
            summary = $"No island at ({worldPos.x:F0},{worldPos.z:F0})";
            return false;
        }

        var islandLocations = GetLocationsOnIsland(island, locations.Value.AllLocations);
        if (islandLocations.Count == 0)
        {
            summary = $"Island {island.Id} has no road-eligible locations";
            return false;
        }

        var selected = SelectLocations(islandLocations, GetMaxLocationsForIsland(island));

        DateTime startTime = DateTime.Now;
        bool locationsWereReady = m_locationsReady;
        Reset();
        m_locationsReady = locationsWereReady;
        m_pathfinder = new RoadPathfinder(WorldGenerator.instance);
        m_roadsGeneratedCount = 0;

        if (island.ContainsPoint(locations.Value.SpawnPoint))
            GenerateIslandRoads(island, selected, locations.Value.SpawnPoint, locations.Value.SpawnRadius);
        else
            GenerateIslandRoads(island, selected);

        RoadSpatialGrid.FinalizeRoadNetwork();
        m_roadsGenerated = true;
        m_pathfinder = null;

        TimeSpan elapsed = DateTime.Now - startTime;
        summary =
            $"Island {island.Id} ({island.ApproxArea / 1_000_000f:F1}km²): " +
            $"{selected.Count} locations, {m_roadsGeneratedCount} roads, " +
            $"{RoadSpatialGrid.TotalRoadLength:F0}m in {elapsed.TotalSeconds:F1}s";
        return true;
    }

    public static void Reset()
    {
        m_roadsGenerated = false;
        m_locationsReady = false;
        m_roadsLoadedFromZDO = false;
        m_pathfinder = null;
        m_roadsGeneratedCount = 0;
        m_roadStartPoints.Clear();
        m_roadRoutes.Clear();
        m_roadCrossings.Clear();
        m_stairRuns.Clear();
        m_roadLocations.Clear();
        RuinPlacement.Reset();
        RoadNetworkPersistence.Reset();
        RoadSpatialGrid.Clear();
    }

    private static void LogGenerationStats(int roadsGenerated, TimeSpan elapsed)
    {
        var log = Log;

        log.LogDebug("=== Road Generation Summary ===");
        log.LogDebug($"  Roads generated: {roadsGenerated}");
        log.LogDebug($"  Total road points: {RoadSpatialGrid.TotalRoadPoints}");
        log.LogDebug($"  Total road length: {RoadSpatialGrid.TotalRoadLength:F0}m");
        log.LogDebug($"  Grid cells with roads: {RoadSpatialGrid.GridCellsWithRoads}");

        if (roadsGenerated > 0)
        {
            log.LogDebug($"  Avg points/road: {RoadSpatialGrid.TotalRoadPoints / (float)roadsGenerated:F0}");
            log.LogDebug($"  Avg length/road: {RoadSpatialGrid.TotalRoadLength / roadsGenerated:F0}m");
        }

        log.LogDebug($"  Generation time: {elapsed.TotalSeconds:F2}s");
        log.LogDebug($"  Road width: {RoadWidth}m");
        log.LogDebug("===============================");
    }

    /// <summary>
    /// Trims a path so it stops at the exterior radius of both endpoints.
    /// </summary>
    private static List<Vector2>? TrimPathToRadii(List<Vector2> path, Vector2 startCenter, float startRadius, Vector2 endCenter, float endRadius)
    {
        if (path == null || path.Count < 2)
            return null;

        int startIndex = 0;
        float startRadiusSq = startRadius * startRadius;
        for (int i = 0; i < path.Count; i++)
        {
            if ((path[i] - startCenter).sqrMagnitude > startRadiusSq)
            {
                startIndex = i;
                break;
            }
        }

        int endIndex = path.Count - 1;
        float endRadiusSq = endRadius * endRadius;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            if ((path[i] - endCenter).sqrMagnitude > endRadiusSq)
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex <= startIndex)
            return null;

        var trimmedPath = new List<Vector2>();

        if (startIndex > 0 && startIndex < path.Count)
        {
            Vector2 edgePoint = CalculateRadiusIntersection(path[startIndex], startCenter, startRadius);
            trimmedPath.Add(edgePoint);
        }

        for (int i = startIndex; i <= endIndex; i++)
        {
            trimmedPath.Add(path[i]);
        }

        if (endIndex < path.Count - 1 && endIndex >= 0)
        {
            Vector2 edgePoint = CalculateRadiusIntersection(path[endIndex], endCenter, endRadius);
            trimmedPath.Add(edgePoint);
        }

        // The radius-edge point is interpolated on the location's circle and can
        // land in water; endpoints obey the same floor as banks and road points.
        // What happens then is the WetTerminus lever: Trim ends short at the
        // last dry point, Reroute walks the circle to the nearest dry point so
        // the road still reaches the location, Drop refuses the route.
        if (WorldGenerator.instance != null)
        {
            Vector2 startEdge = trimmedPath[0];
            Vector2 endEdge = trimmedPath[trimmedPath.Count - 1];
            bool startWet = !AboveWaterlineFloor(startEdge);
            bool endWet = !AboveWaterlineFloor(endEdge);

            if ((startWet || endWet) && WetTerminus == WetTerminusMode.Drop)
            {
                Log.LogDebug("Route dropped: its end on the location circle is in water (WetTerminus = Drop)");
                return null;
            }

            while (trimmedPath.Count > 2 && !AboveWaterlineFloor(trimmedPath[0]))
                trimmedPath.RemoveAt(0);
            while (trimmedPath.Count > 2 && !AboveWaterlineFloor(trimmedPath[trimmedPath.Count - 1]))
                trimmedPath.RemoveAt(trimmedPath.Count - 1);

            if (WetTerminus == WetTerminusMode.Reroute)
            {
                if (startWet && DryLegToCircle(startCenter, startRadius, startEdge, trimmedPath[0]) is List<Vector2> s)
                {
                    s.Reverse();
                    trimmedPath.InsertRange(0, s);
                }
                if (endWet && DryLegToCircle(endCenter, endRadius, endEdge, trimmedPath[trimmedPath.Count - 1]) is List<Vector2> e)
                    trimmedPath.AddRange(e);
            }
        }

        return trimmedPath.Count >= 2 ? trimmedPath : null;
    }

    /// <summary>
    /// Reroute terminus: the dry point on a location's radius circle nearest
    /// the wet edge point that the route's last dry point (the anchor, at
    /// most a cell outside the circle) can reach over dry ground. The water
    /// that made the edge point wet usually lies BETWEEN the anchor and the
    /// circle, so a straight leg would cross it: the leg is found by a
    /// flood fill over a 2 m grid of dry cells outside the circle, then
    /// string-pulled to the few corners that matter. Candidates farther than
    /// one radius from the wet point are not considered (the road would
    /// wrap half the location to reach them; such a site is left to Trim).
    /// Returns the leg's points after the anchor, ending at the terminus, or
    /// null when nothing qualifies.
    /// </summary>
    private static List<Vector2>? DryLegToCircle(Vector2 center, float radius, Vector2 wetEdge, Vector2 anchor)
    {
        if (radius < 1f || WorldGenerator.instance == null)
            return null;

        int samples = Mathf.Max(36, Mathf.CeilToInt(2f * Mathf.PI * radius)); // ~1 m of arc
        float maxDistSq = radius * radius;
        List<(float distSq, Vector2 point)> candidates = new();
        for (int i = 0; i < samples; i++)
        {
            float angle = i * Mathf.PI * 2f / samples;
            Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            float distSq = (p - wetEdge).sqrMagnitude;
            if (distSq <= maxDistSq && IsPathablePoint(p))
                candidates.Add((distSq, p));
        }
        if (candidates.Count == 0)
            return null;
        candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

        // Cheap first: a straight leg.
        foreach ((float _, Vector2 p) in candidates)
        {
            if (LegClear(anchor, p, center, radius))
                return Densify(anchor, new List<Vector2> { p });
        }

        // Flood fill from the anchor over dry cells in the ring just outside
        // the circle; every cell remembers how it was reached.
        const float step = 2f;
        float reach = radius + RoadConstants.PathfindingCellSize * 3f;
        Dictionary<(int x, int y), (int x, int y)> parent = new();
        Queue<(int x, int y)> open = new();
        (int x, int y) origin = (0, 0);
        parent[origin] = origin;
        open.Enqueue(origin);
        Vector2 Cell((int x, int y) c) => anchor + new Vector2(c.x * step, c.y * step);
        bool Passable(Vector2 p) => (p - center).sqrMagnitude <= reach * reach
                                     && (p - center).magnitude >= radius - 0.5f
                                     && IsPathablePoint(p);
        while (open.Count > 0 && parent.Count < 20000)
        {
            (int x, int y) c = open.Dequeue();
            Vector2 from = Cell(c);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                (int x, int y) n = (c.x + dx, c.y + dy);
                if (parent.ContainsKey(n)) continue;
                Vector2 to = Cell(n);
                if (!Passable(to) || !LegClear(from, to, center, radius)) continue;
                parent[n] = c;
                open.Enqueue(n);
            }
        }

        foreach ((float _, Vector2 t) in candidates)
        {
            // The reachable cell nearest the candidate whose final hop is clear.
            (int x, int y) near = (Mathf.RoundToInt((t.x - anchor.x) / step), Mathf.RoundToInt((t.y - anchor.y) / step));
            (int x, int y)? best = null;
            float bestDist = float.MaxValue;
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                (int x, int y) c = (near.x + dx, near.y + dy);
                if (!parent.ContainsKey(c)) continue;
                float d = (Cell(c) - t).sqrMagnitude;
                if (d < bestDist && LegClear(Cell(c), t, center, radius))
                {
                    bestDist = d;
                    best = c;
                }
            }
            if (best == null) continue;

            List<Vector2> leg = new();
            for ((int x, int y) c = best.Value; c != origin; c = parent[c])
                leg.Add(Cell(c));
            leg.Reverse();
            leg.Add(t);
            return Densify(anchor, StringPull(anchor, leg, center, radius));
        }
        return null;
    }

    /// <summary>Greedy string-pulling: from each kept point, skip ahead to the
    /// farthest leg point still reachable by a clear straight segment.</summary>
    private static List<Vector2> StringPull(Vector2 anchor, List<Vector2> leg, Vector2 center, float radius)
    {
        List<Vector2> pulled = new();
        Vector2 from = anchor;
        int i = 0;
        while (i < leg.Count)
        {
            int far = i;
            for (int j = leg.Count - 1; j > i; j--)
            {
                if (LegClear(from, leg[j], center, radius)) { far = j; break; }
            }
            pulled.Add(leg[far]);
            from = leg[far];
            i = far + 1;
        }
        return pulled;
    }

    /// <summary>The route centerline is a Catmull-Rom spline through its
    /// waypoints, and a sharp corner overshoots the polyline; waypoints every
    /// LegWaypointSpacing keep the curve within the clearance LegClear
    /// demands, so no resampled point lands in the water the leg skirted.</summary>
    private const float LegWaypointSpacing = 2f;
    private const float LegWaterClearance = 1f;

    private static List<Vector2> Densify(Vector2 anchor, List<Vector2> leg)
    {
        List<Vector2> dense = new();
        Vector2 from = anchor;
        foreach (Vector2 to in leg)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / LegWaypointSpacing));
            for (int i = 1; i <= steps; i++)
                dense.Add(Vector2.Lerp(from, to, (float)i / steps));
            from = to;
        }
        return dense;
    }

    private static bool AboveWaterlineFloor(Vector2 p) =>
        BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, WorldGenerator.instance)
            >= RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;

    /// <summary>Every metre of a reroute leg segment, both ends included,
    /// above the floor (finer than the route's own spline spacing, so no
    /// resampled point can land in a sliver the check skipped) and outside
    /// the location's circle (the leg skirts the location, never crosses it).</summary>
    private static bool LegClear(Vector2 a, Vector2 b, Vector2 center, float radius)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
        float keepOut = (radius - 0.5f) * (radius - 0.5f);
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
            if ((p - center).sqrMagnitude < keepOut)
                return false;
            if (!AboveWaterlineFloor(p))
                return false;
            // Dry LegWaterClearance to each side as well, so the spline's
            // overshoot at the corners has dry ground to overshoot into. Not
            // demanded of the segment's start: the anchor is a path point
            // that may legitimately sit at the water's edge.
            if (i > 0 && (!AboveWaterlineFloor(new Vector2(p.x + LegWaterClearance, p.y))
                || !AboveWaterlineFloor(new Vector2(p.x - LegWaterClearance, p.y))
                || !AboveWaterlineFloor(new Vector2(p.x, p.y + LegWaterClearance))
                || !AboveWaterlineFloor(new Vector2(p.x, p.y - LegWaterClearance))))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Calculates the point on the radius circle in the direction from center to the given point.
    /// </summary>
    private static Vector2 CalculateRadiusIntersection(Vector2 outsidePoint, Vector2 center, float radius)
    {
        Vector2 direction = (outsidePoint - center).normalized;
        return center + direction * radius;
    }

    #endregion

    #region Persistence

    /// <summary>
    /// Unique prefab name for our metadata ZDO. Must not conflict with any game prefabs.
    /// This is public so Plugin.cs can register the prefab with Jotunn.
    /// </summary>
    public const string MetadataPrefabName = RoadNetworkPersistence.MetadataPrefabName;

    /// <summary>
    /// Save the entire road network to a dedicated ZDO for persistence across world reloads.
    /// Call this on world save.
    /// </summary>
    public static void SaveGlobalRoadData()
    {
        if (!m_roadsGenerated)
        {
            Log.LogDebug("[SAVE] No roads generated, skipping global save");
            return;
        }

        RoadNetworkPersistence.SaveGlobalRoadData(m_roadStartPoints, m_roadRoutes, m_roadCrossings, m_stairRuns, RuinPlacement.SpawnedZones);
    }

    /// <summary>
    /// Try to load the entire road network from persisted ZDO.
    /// Call this on world load before road generation would trigger.
    /// </summary>
    /// <returns>True if road data was found and loaded</returns>
    public static bool TryLoadGlobalRoadData()
    {
        var spawnedRuinZones = new HashSet<Vector2i>();
        bool loaded = RoadNetworkPersistence.TryLoadGlobalRoadData(
            m_roadStartPoints, m_roadRoutes, m_roadCrossings, m_stairRuns, spawnedRuinZones);
        if (loaded)
            RuinPlacement.MarkZonesSpawned(spawnedRuinZones);
        return loaded;
    }

    #endregion
}
