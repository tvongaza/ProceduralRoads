using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Which policy package decides a world's road network.</summary>
public enum RoadNetworkStrategy
{
    /// <summary>What the mod ships: largest islands by percentage, priority
    /// truncation, an edge-cell anchor off the starter island, and Chain or
    /// MST by island parity. The study's control.</summary>
    Shipped,

    /// <summary>The author's proposal in PR #16 (feature/warp-71), ported here
    /// so it can be measured on the same base as everything else: islands
    /// balanced over three world rings, endpoints filtered to places a road
    /// can actually reach, a highest-priority anchor instead of a coast cell,
    /// endpoints snapped to pathable ground, and one tree grown outward from
    /// the anchor with retries and a failed-attempt cap.</summary>
    Reachable,
}

/// <summary>
/// The author's PR #16 network policy, ported onto this base.
///
/// It is here as a candidate to measure, not as a change to the mod: the
/// shipped strategy is untouched and stays the control, and nothing below runs
/// unless <see cref="RoadNetworkGenerator.Strategy"/> selects it. The method
/// names follow the PR so the harness that already knows it
/// (StrategySupport, ReachableRoadsTests) drives this code unchanged.
///
/// Not ported: PR #16's per-name registered priorities. This base gives every
/// registered location the same custom priority the PR's default gives them,
/// so for a study that registers no per-name priority the two agree.
///
/// Study branch (study/road-network-strategies). Never part of a PR.
/// </summary>
public static partial class RoadNetworkGenerator
{
    private const float WorldRadius = 10000f;

    /// <summary>PR #16: an edge longer than this is never attempted.</summary>
    private const float MaxRoadLinkDistance = 2200f;

    /// <summary>PR #16: how many failed edges one island may spend.</summary>
    private const int MaxFailedEdgeAttemptsPerIsland = 24;

    private static RoadNetworkStrategy m_strategy = RoadNetworkStrategy.Shipped;

    /// <summary>
    /// Which policy package this run uses. Shipped unless a study run says
    /// otherwise; generation never changes it by itself. Setting it sets every
    /// factor in <see cref="StudyFactors"/>, which a run may then override one
    /// at a time.
    /// </summary>
    public static RoadNetworkStrategy Strategy
    {
        get => m_strategy;
        set
        {
            m_strategy = value;
            StudyFactors.Apply(value);
        }
    }

    private sealed class IslandCandidate
    {
        public Island Island = null!;
        public List<(string name, Vector3 position, float radius)> Locations = new();
        public bool IsStarterIsland;
        public int Ring;
    }

    private static List<IslandCandidate> BuildIslandCandidates(
        List<Island> islands,
        List<(string name, Vector3 position, float radius)> allLocations,
        Vector3 spawnPoint)
    {
        List<IslandCandidate> candidates = new();
        foreach (Island island in islands)
        {
            List<(string name, Vector3 position, float radius)> islandLocations =
                GetLocationsOnIsland(island, allLocations);
            if (islandLocations.Count == 0)
                continue;

            candidates.Add(new IslandCandidate
            {
                Island = island,
                Locations = islandLocations,
                IsStarterIsland = island.ContainsPoint(spawnPoint),
                Ring = GetIslandRing(island),
            });
        }

        return candidates;
    }

    /// <summary>Inner, middle or outer third of the world by distance from its centre.</summary>
    private static int GetIslandRing(Island island)
    {
        float normalized = Mathf.Clamp01(island.Center.magnitude / WorldRadius);
        if (normalized < 0.33f) return 0;
        if (normalized < 0.66f) return 1;
        return 2;
    }

    /// <summary>
    /// Largest first inside each ring, then one ring at a time in turn, so the
    /// outer world's big landmasses cannot take the whole quota.
    /// </summary>
    private static List<IslandCandidate> SelectBalancedIslands(List<IslandCandidate> candidates, int percentage)
    {
        if (candidates.Count == 0)
            return new List<IslandCandidate>();

        int targetCount = Mathf.Max(1, Mathf.RoundToInt(candidates.Count * percentage / 100f));
        if (targetCount >= candidates.Count)
            return candidates.OrderByDescending(candidate => candidate.Island.ApproxArea).ToList();

        List<IslandCandidate> selected = new();
        HashSet<int> selectedIslandIds = new();

        IslandCandidate? starterIsland = candidates.FirstOrDefault(candidate => candidate.IsStarterIsland);
        if (starterIsland != null)
        {
            selected.Add(starterIsland);
            selectedIslandIds.Add(starterIsland.Island.Id);
        }

        List<List<IslandCandidate>> rings = new();
        for (int ring = 0; ring < 3; ring++)
        {
            rings.Add(candidates
                .Where(candidate => candidate.Ring == ring && !selectedIslandIds.Contains(candidate.Island.Id))
                .OrderByDescending(candidate => candidate.Island.ApproxArea)
                .ToList());
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

    /// <summary>
    /// PR #16's location quota: the highest-priority place first, then each
    /// further place by priority less a penalty that GROWS with its distance
    /// from the nearest place already chosen. It is a compactness bias, not a
    /// spread: among equal priorities the nearer candidate wins. The penalty
    /// reaches 110 at MaxRoadLinkDistance while one priority step is worth
    /// 100, so distance can outrank a one-step priority difference.
    /// Worth reporting to the author as measured behaviour.
    /// </summary>
    private static List<(string name, Vector3 position, float radius)> SelectLocationsPriorityThenNearest(
        List<(string name, Vector3 position, float radius)> candidates, int maxCount)
    {
        if (candidates.Count <= maxCount)
            return candidates;

        List<(string name, Vector3 position, float radius)> remaining = candidates
            .OrderByDescending(location => GetLocationPriority(location.name))
            .ToList();
        List<(string name, Vector3 position, float radius)> selected = new() { remaining[0] };
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

    /// <summary>
    /// The same quota as PR #16's, with the distance term reversed: each
    /// further place is taken as far as it can be from those already chosen.
    /// It answers whether the arrangement of destinations matters at all,
    /// holding their number fixed.
    /// </summary>
    private static List<(string name, Vector3 position, float radius)> SelectLocationsPriorityThenFarthest(
        List<(string name, Vector3 position, float radius)> candidates, int maxCount)
    {
        if (candidates.Count <= maxCount)
            return candidates;

        List<(string name, Vector3 position, float radius)> remaining = candidates
            .OrderByDescending(location => GetLocationPriority(location.name))
            .ToList();
        List<(string name, Vector3 position, float radius)> selected = new() { remaining[0] };
        remaining.RemoveAt(0);

        while (selected.Count < maxCount && remaining.Count > 0)
        {
            int bestIndex = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                (string name, Vector3 position, float radius) candidate = remaining[i];
                float nearest = selected.Min(location => Vector3.Distance(location.position, candidate.position));
                float score = GetLocationPriority(candidate.name) * 100f
                              + Mathf.Min(nearest, MaxRoadLinkDistance) * 0.05f;
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

    /// <summary>
    /// A draw from the island's places that ignores priority entirely: the
    /// control the other two quotas are measured against.
    /// </summary>
    private static List<(string name, Vector3 position, float radius)> SelectLocationsAtRandom(
        List<(string name, Vector3 position, float radius)> candidates, int maxCount)
    {
        if (candidates.Count <= maxCount)
            return candidates;

        int seed = WorldGenerator.instance?.GetSeed() ?? 0;
        return candidates
            .OrderBy(c => DrawOrder(seed, c))
            .Take(maxCount)
            .ToList();
    }

    /// <summary>A number fixed by the world and the place, so the draw is the
    /// same every time that world is generated.</summary>
    private static uint DrawOrder(int seed, (string name, Vector3 position, float radius) place)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in place.name)
                hash = (hash ^ c) * 16777619;
            hash = (hash ^ (uint)Mathf.RoundToInt(place.position.x)) * 16777619;
            hash = (hash ^ (uint)Mathf.RoundToInt(place.position.z)) * 16777619;
            return (hash ^ (uint)seed) * 16777619;
        }
    }

    /// <summary>The island's highest-priority place, nearest its centre on a
    /// tie: a destination rather than a coast cell.</summary>
    private static (string name, Vector3 position, float radius) SelectIslandAnchor(
        Island island,
        List<(string name, Vector3 position, float radius)> locations)
    {
        Vector3 islandCenter = new(island.Center.x, 0f, island.Center.y);
        return locations
            .OrderByDescending(location => GetLocationPriority(location.name))
            .ThenBy(location => Vector3.Distance(location.position, islandCenter))
            .First();
    }

    private static bool SameLocation(
        (string name, Vector3 position, float radius) a,
        (string name, Vector3 position, float radius) b) =>
        a.name == b.name && Vector3.SqrMagnitude(a.position - b.position) < 1f;

    private static string GetEdgeKey(int a, int b) => a < b ? $"{a}:{b}" : $"{b}:{a}";

    /// <summary>
    /// Nothing left is within reach of the tree: start a second component at
    /// the highest-priority place still waiting, rather than stopping.
    /// </summary>
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

    /// <summary>
    /// PR #16's connection plan: grow one tree outward from the anchor, each
    /// step taking the cheapest edge from anything connected to anything left
    /// (distance less a priority bonus, never longer than
    /// MaxRoadLinkDistance). A failed edge is remembered and not tried again,
    /// and the island stops after MaxFailedEdgeAttemptsPerIsland attempts.
    /// </summary>
    private static void GenerateReachableRoads(
        Vector3 startPos,
        float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        if (locations.Count == 0)
            return;

        List<(string name, Vector3 position, float radius)> nodes = new()
        {
            (startName, startPos, startRadius),
        };
        nodes.AddRange(locations);

        HashSet<int> connected = new() { 0 };
        HashSet<int> remaining = new(Enumerable.Range(1, nodes.Count - 1));
        HashSet<string> failedEdges = new();
        int maxAttempts = Mathf.Min(nodes.Count * nodes.Count, MaxFailedEdgeAttemptsPerIsland);
        int attempts = 0;

        while (remaining.Count > 0 && attempts < maxAttempts)
        {
            int bestFrom = -1, bestTo = -1;
            float bestScore = float.MaxValue;

            foreach (int fromIndex in connected)
            {
                foreach (int toIndex in remaining)
                {
                    if (failedEdges.Contains(GetEdgeKey(fromIndex, toIndex)))
                        continue;

                    float distance = Vector3.Distance(nodes[fromIndex].position, nodes[toIndex].position);
                    if (distance > MaxRoadLinkDistance)
                        continue;

                    float score = distance - GetLocationPriority(nodes[toIndex].name) * 20f;
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
            if (GenerateRoad(from.position, from.radius, to.position, to.radius, RoadWidth,
                    $"{from.name} -> {to.name}"))
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
            Log.LogDebug($"Skipped {failedEdges.Count} unreachable road edge attempt(s)");

        if (remaining.Count > 0)
        {
            string skipped = string.Join(", ", remaining.Select(index => nodes[index].name).Distinct().Take(8));
            Log.LogDebug($"Skipped {remaining.Count} endpoint(s) after reaching edge attempt cap: {skipped}");
        }
    }

    /// <summary>PR #16's per-island entry: anchor on a place, not a coast cell.</summary>
    private static void GenerateReachableIslandRoads(
        Island island,
        List<(string name, Vector3 position, float radius)> islandLocations,
        Vector3? overrideStart = null,
        float overrideStartRadius = 0f)
    {
        if (islandLocations.Count == 0)
            return;

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
            roadLocations = islandLocations.Where(location => !SameLocation(location, anchor)).ToList();

            if (roadLocations.Count == 0)
            {
                Log.LogDebug($"Island {island.Id}: skipped single-location island anchored at {anchor.name}");
                return;
            }
        }

        Log.LogDebug($"Island {island.Id}: {islandLocations.Count} locations, strategy=Reachable, anchor={startName}");
        GenerateReachableRoads(startPos, startRadius, roadLocations, startName);
    }

    /// <summary>
    /// The nearest point a road could stand on, searched outward in rings.
    /// Returns the centre unchanged when nothing pathable is near, so the
    /// caller still sees the attempt fail rather than silently move.
    /// </summary>
    private static Vector2 GetNearestPathablePoint(Vector2 center, float radius)
    {
        if (IsPathablePoint(center))
            return center;

        float searchRadius = Mathf.Max(radius + RoadConstants.PathfindingCellSize * 2f,
            RoadConstants.PathfindingCellSize * 2f);
        float bestDistance = float.MaxValue;
        Vector2 bestPoint = center;
        bool found = false;

        for (float currentRadius = RoadConstants.PathfindingCellSize;
             currentRadius <= searchRadius;
             currentRadius += RoadConstants.PathfindingCellSize)
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

    private static bool HasNearbyPathablePoint(Vector2 center, float radius) =>
        IsPathablePoint(GetNearestPathablePoint(center, radius));

    /// <summary>Dry ground outside the river band.</summary>
    private static bool IsPathablePoint(Vector2 point)
    {
        if (WorldGenerator.instance == null)
            return true;

        if (WorldGenerator.instance.GetHeight(point.x, point.y) < RoadConstants.ShallowWaterHeight)
            return false;

        WorldGenerator.instance.GetRiverWeight(point.x, point.y, out float riverWeight, out _);
        return riverWeight <= RoadConstants.RiverImpassableThreshold;
    }
}
