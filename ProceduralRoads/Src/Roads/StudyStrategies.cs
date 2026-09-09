using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Connection plans the study proposes, beside the two the mod and PR #16
/// already have. They all take the same island, the same selected places, the
/// same anchor, the same routing rules and the same per-attempt budget: only
/// the plan differs, so a difference in the result belongs to the plan.
///
/// Two of them plan on ROUTED cost - what the pathfinder actually charges to
/// get from one place to another - rather than on straight-line distance. That
/// is the point of them: a strait or a mountain makes two places far apart in
/// the only sense a road cares about, and straight-line planning cannot see it.
/// Routed costs are measured on the network as it stands when planning starts,
/// so a later road sharing a crossing is not credited; that approximation is
/// stated rather than hidden.
///
/// Study branch (study/road-network-strategies). Never part of a PR.
/// </summary>
public static partial class RoadNetworkGenerator
{
    /// <summary>
    /// Searches a plan ran to learn what a connection would cost, before
    /// deciding to build it. They are not attempts - nothing is built - but
    /// they are pathfinder work, and a plan that probes every pair is not
    /// cheaper than one that tries and fails just because its failures happen
    /// earlier. Reported so the comparison stays honest.
    /// </summary>
    public static int RoutingProbes { get; private set; }

    /// <summary>Probes that found no route at all.</summary>
    public static int RoutingProbesWithoutRoute { get; private set; }

    public static void ResetProbeCounters()
    {
        RoutingProbes = 0;
        RoutingProbesWithoutRoute = 0;
    }

    /// <summary>A place in a plan: the anchor is node 0.</summary>
    private readonly struct Node
    {
        public readonly string Name;
        public readonly Vector3 Position;
        public readonly float Radius;

        public Node(string name, Vector3 position, float radius)
        {
            Name = name;
            Position = position;
            Radius = radius;
        }
    }

    private static List<Node> PlanNodes(
        Vector3 startPos, float startRadius, string startName,
        List<(string name, Vector3 position, float radius)> locations)
    {
        List<Node> nodes = new() { new Node(startName, startPos, startRadius) };
        foreach ((string name, Vector3 position, float radius) location in locations)
            nodes.Add(new Node(location.name, location.position, location.radius));
        return nodes;
    }

    /// <summary>
    /// What the pathfinder charges to get between two places, without building
    /// anything: the length of the path it finds, or none when it finds no
    /// path. Used for planning; the road itself is built afterwards by the
    /// ordinary primitive, so a plan cannot smuggle in a route the generator
    /// would not have made.
    /// </summary>
    private static float? RoutedCost(Node from, Node to)
    {
        if (m_pathfinder == null)
            return null;

        Vector2 a = new(from.Position.x, from.Position.z);
        Vector2 b = new(to.Position.x, to.Position.z);
        if (Vector2.Distance(a, b) > MaxRoadLinkDistance)
            return null;

        RoutingProbes++;
        List<Vector2>? path = m_pathfinder.FindPath(a, b);
        if (path == null || path.Count < 2)
        {
            RoutingProbesWithoutRoute++;
            return null;
        }

        float length = 0f;
        for (int i = 1; i < path.Count; i++)
            length += Vector2.Distance(path[i - 1], path[i]);
        return length;
    }

    /// <summary>Routed cost between every pair worth trying, once.</summary>
    private static Dictionary<(int, int), float> RoutedCosts(List<Node> nodes)
    {
        Dictionary<(int, int), float> costs = new();
        for (int a = 0; a < nodes.Count; a++)
        {
            for (int b = a + 1; b < nodes.Count; b++)
            {
                float? cost = RoutedCost(nodes[a], nodes[b]);
                if (cost.HasValue)
                    costs[(a, b)] = cost.Value;
            }
        }

        return costs;
    }

    private static float? Cost(Dictionary<(int, int), float> costs, int a, int b) =>
        costs.TryGetValue(a < b ? (a, b) : (b, a), out float cost) ? cost : null;

    private static bool Build(Node from, Node to) =>
        GenerateRoad(from.Position, from.Radius, to.Position, to.Radius, RoadWidth,
            $"{from.Name} -> {to.Name}");

    /// <summary>
    /// A minimum spanning tree on routed cost instead of straight-line
    /// distance. The cheapest network that still reaches everything reachable:
    /// where a strait separates two places, the tree goes the way the road
    /// would actually go, or leaves them in separate components honestly.
    /// </summary>
    private static void GenerateRoutedMstRoads(
        Vector3 startPos, float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        List<Node> nodes = PlanNodes(startPos, startRadius, startName, locations);
        if (nodes.Count < 2)
            return;

        Dictionary<(int, int), float> costs = RoutedCosts(nodes);
        HashSet<int> inTree = new() { 0 };
        HashSet<int> remaining = new(Enumerable.Range(1, nodes.Count - 1));

        while (remaining.Count > 0)
        {
            int bestFrom = -1, bestTo = -1;
            float bestCost = float.MaxValue;
            foreach (int from in inTree)
            {
                foreach (int to in remaining)
                {
                    float? cost = Cost(costs, from, to);
                    if (cost.HasValue && cost.Value < bestCost)
                    {
                        bestCost = cost.Value;
                        bestFrom = from;
                        bestTo = to;
                    }
                }
            }

            if (bestFrom < 0)
            {
                // Nothing left is routable from the tree. Start a new component
                // at the most important place still waiting rather than stop.
                int next = remaining.OrderByDescending(i => GetLocationPriority(nodes[i].Name)).First();
                inTree.Add(next);
                remaining.Remove(next);
                Log.LogDebug($"Routed MST: no route to the rest; new component at {nodes[next].Name}");
                continue;
            }

            if (Build(nodes[bestFrom], nodes[bestTo]))
                inTree.Add(bestTo);
            else
                costs.Remove(bestFrom < bestTo ? (bestFrom, bestTo) : (bestTo, bestFrom));

            remaining.Remove(bestTo);
            if (!inTree.Contains(bestTo))
                Log.LogDebug($"Routed MST: {nodes[bestFrom].Name} -> {nodes[bestTo].Name} planned but not built");
        }
    }

    /// <summary>
    /// One long road between the two places furthest apart by routed cost, and
    /// everything else joined to whatever is nearest on the network so far -
    /// which may be a point along a road rather than its end, so the network
    /// grows junctions instead of a star.
    /// </summary>
    private static void GenerateTrunkAndSpurRoads(
        Vector3 startPos, float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        List<Node> nodes = PlanNodes(startPos, startRadius, startName, locations);
        if (nodes.Count < 2)
            return;

        Dictionary<(int, int), float> costs = RoutedCosts(nodes);
        if (costs.Count == 0)
        {
            Log.LogDebug("Trunk and spurs: nothing on this island is routable to anything else");
            return;
        }

        // The trunk is the dearest route that exists: the island's long axis as
        // a road sees it, not as the map sees it.
        (int a, int b) trunk = costs.OrderByDescending(entry => entry.Value).First().Key;
        HashSet<int> connected = new();
        if (Build(nodes[trunk.a], nodes[trunk.b]))
        {
            connected.Add(trunk.a);
            connected.Add(trunk.b);
        }
        else
        {
            connected.Add(trunk.a);
        }

        // Everything else joins the network where it is nearest to it, by
        // routed cost to any place already on it. The road it attaches to is
        // built by the ordinary primitive, which lands it on the existing
        // road's line where the two meet.
        List<int> waiting = Enumerable.Range(0, nodes.Count).Where(i => !connected.Contains(i)).ToList();
        waiting.Sort((x, y) => GetLocationPriority(nodes[y].Name).CompareTo(GetLocationPriority(nodes[x].Name)));

        foreach (int node in waiting)
        {
            int bestAnchor = -1;
            float bestCost = float.MaxValue;
            foreach (int onNetwork in connected)
            {
                float? cost = Cost(costs, onNetwork, node);
                if (cost.HasValue && cost.Value < bestCost)
                {
                    bestCost = cost.Value;
                    bestAnchor = onNetwork;
                }
            }

            if (bestAnchor < 0)
            {
                Log.LogDebug($"Trunk and spurs: {nodes[node].Name} is not routable to the network");
                continue;
            }

            if (Build(nodes[bestAnchor], nodes[node]))
                connected.Add(node);
        }
    }

    /// <summary>
    /// A hub with spokes: the anchor serves the places nearest it directly, and
    /// a cluster too far to serve that way gets a hub of its own, joined to the
    /// first. Short journeys from one important place, at the cost of going
    /// back through it.
    /// </summary>
    private static void GenerateHubAndSpokeRoads(
        Vector3 startPos, float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        List<Node> nodes = PlanNodes(startPos, startRadius, startName, locations);
        if (nodes.Count < 2)
            return;

        Dictionary<(int, int), float> costs = RoutedCosts(nodes);
        List<int> hubs = new() { 0 };
        HashSet<int> served = new() { 0 };

        // A place further from every hub than this is not a spoke; it wants a
        // hub of its own. Measured in routed metres, so a place across a strait
        // is far however near it looks.
        float spokeReach = RoadConstants.HubSpokeReachMetres;

        bool progress = true;
        while (progress)
        {
            progress = false;

            foreach (int node in Enumerable.Range(1, nodes.Count - 1))
            {
                if (served.Contains(node))
                    continue;

                int bestHub = -1;
                float bestCost = float.MaxValue;
                foreach (int hub in hubs)
                {
                    float? cost = Cost(costs, hub, node);
                    if (cost.HasValue && cost.Value <= spokeReach && cost.Value < bestCost)
                    {
                        bestCost = cost.Value;
                        bestHub = hub;
                    }
                }

                if (bestHub < 0)
                    continue;

                if (Build(nodes[bestHub], nodes[node]))
                    served.Add(node);
                else
                    served.Add(node);   // attempted and failed: it is not waiting on a hub
                progress = true;
            }

            // Anything still unserved is a cluster of its own: promote its most
            // important member to a hub, joined to the nearest existing hub if a
            // route exists.
            List<int> stranded = Enumerable.Range(1, nodes.Count - 1).Where(i => !served.Contains(i)).ToList();
            if (stranded.Count == 0)
                break;

            int promoted = stranded.OrderByDescending(i => GetLocationPriority(nodes[i].Name)).First();
            int nearestHub = -1;
            float nearestCost = float.MaxValue;
            foreach (int hub in hubs)
            {
                float? cost = Cost(costs, hub, promoted);
                if (cost.HasValue && cost.Value < nearestCost)
                {
                    nearestCost = cost.Value;
                    nearestHub = hub;
                }
            }

            if (nearestHub >= 0)
                Build(nodes[nearestHub], nodes[promoted]);

            hubs.Add(promoted);
            served.Add(promoted);
            progress = true;
            Log.LogDebug($"Hub and spoke: {nodes[promoted].Name} promoted to a hub");
        }
    }
}
