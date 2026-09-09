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
    /// anything: the cost its search accumulated, or none when it finds no
    /// path. That is the number a plan should compare, not the route's length:
    /// a short route over a mountain is dear and a long one along a valley is
    /// cheap, and only the cost knows the difference.
    ///
    /// Used for planning; the road itself is built afterwards by the ordinary
    /// primitive, so a plan cannot smuggle in a route the generator would not
    /// have made.
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

        return m_pathfinder.LastPathCost;
    }

    /// <summary>
    /// Routed cost between every pair worth trying, once.
    ///
    /// Every pair means one pathfinding search per pair, which is fine for a
    /// dozen places and hopeless for four hundred. Each place therefore offers
    /// only its nearest neighbours as candidates - by straight-line distance,
    /// which is free - and the choice between those candidates is still made
    /// on what the pathfinder charges. A plan can only be as good as its
    /// candidate set, and this one says so.
    /// </summary>
    private static Dictionary<(int, int), float> RoutedCosts(List<Node> nodes)
    {
        Dictionary<(int, int), float> costs = new();
        int neighbours = Mathf.Max(1, StudyFactors.RoutedPlanNeighbours);

        HashSet<(int, int)> pairs = new();
        for (int a = 0; a < nodes.Count; a++)
        {
            IEnumerable<int> candidates = Enumerable.Range(0, nodes.Count)
                .Where(b => b != a)
                .OrderBy(b => Vector3.SqrMagnitude(nodes[a].Position - nodes[b].Position))
                .Take(neighbours);
            foreach (int b in candidates)
                pairs.Add(a < b ? (a, b) : (b, a));
        }

        foreach ((int a, int b) in pairs)
        {
            float? cost = RoutedCost(nodes[a], nodes[b]);
            if (cost.HasValue)
                costs[(a, b)] = cost.Value;
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

        // Everything else joins the ROAD, not the places on it: the spur starts
        // at the nearest point along a road already built, so the network grows
        // a junction there instead of another line back to a destination. That
        // is the whole point of a trunk, and joining place to place - which an
        // earlier version of this did - does not test it.
        List<int> waiting = Enumerable.Range(0, nodes.Count).Where(i => !connected.Contains(i)).ToList();
        waiting.Sort((x, y) => GetLocationPriority(nodes[y].Name).CompareTo(GetLocationPriority(nodes[x].Name)));

        foreach (int node in waiting)
        {
            Vector3? junction = NearestPointOnBuiltRoad(nodes[node].Position);
            if (junction.HasValue)
            {
                // A spur from a point on the road has no location at its start,
                // so it is named for the road it leaves.
                if (GenerateRoad(junction.Value, 0f, nodes[node].Position, nodes[node].Radius, RoadWidth,
                        $"road -> {nodes[node].Name}"))
                {
                    connected.Add(node);
                    continue;
                }
            }

            // Nothing built yet to join, or the spur failed: fall back to the
            // cheapest place already on the network.
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
    /// No tree at all. Every place in turn joins the network where the network
    /// is nearest to it - a point along a road, not the place at its end - so
    /// every connection after the first is a junction by construction.
    ///
    /// This is the plan the other four cannot be: a tree, however it is
    /// priced, only ever draws a line between two places, and two places near
    /// each other both reached from the same anchor give two lines side by
    /// side. Growing from the road gives a fork.
    /// </summary>
    private static void GenerateGrowFromNetworkRoads(
        Vector3 startPos, float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        List<Node> nodes = PlanNodes(startPos, startRadius, startName, locations);
        if (nodes.Count < 2)
            return;

        // The first road has no network to join, so it goes from the anchor to
        // the place it can reach most cheaply. After that the network exists.
        Dictionary<(int, int), float> costs = RoutedCosts(nodes);
        (int a, int b)? seed = SeedConnection(nodes, costs);
        if (!seed.HasValue)
        {
            Log.LogDebug("Grow from network: nothing on this island is routable to anything else");
            return;
        }
        HashSet<int> connected = new() { 0, seed.Value.a, seed.Value.b };
        Build(nodes[seed.Value.a], nodes[seed.Value.b]);
        List<int> waiting = Enumerable.Range(1, nodes.Count - 1).Where(i => !connected.Contains(i)).ToList();

        // Then, repeatedly, the place nearest the road as it now stands. The
        // order matters: taking the nearest each time keeps the network
        // growing outward instead of leaping across the island and back.
        while (waiting.Count > 0)
        {
            int next = -1;
            float nextDistance = float.MaxValue;
            Vector3? nextJunction = null;
            foreach (int node in waiting)
            {
                Vector3? junction = NearestPointOnBuiltRoad(nodes[node].Position);
                if (!junction.HasValue)
                    continue;
                float distance = Vector3.Distance(junction.Value, nodes[node].Position);
                if (distance < nextDistance)
                {
                    nextDistance = distance;
                    next = node;
                    nextJunction = junction;
                }
            }

            if (next < 0)
            {
                // No road is near enough to any of them to be worth joining.
                // Fall back to the cheapest place already on the network, so
                // the plan behaves like the others rather than giving up.
                int bestFrom = -1, bestTo = -1;
                float bestCost = float.MaxValue;
                foreach (int on in connected)
                    foreach (int node in waiting)
                    {
                        float? cost = Cost(costs, on, node);
                        if (cost.HasValue && cost.Value < bestCost)
                        { bestCost = cost.Value; bestFrom = on; bestTo = node; }
                    }
                if (bestTo < 0)
                    break;
                if (Build(nodes[bestFrom], nodes[bestTo]))
                    connected.Add(bestTo);
                waiting.Remove(bestTo);
                continue;
            }

            if (GenerateRoad(nextJunction!.Value, 0f, nodes[next].Position, nodes[next].Radius, RoadWidth,
                    $"road -> {nodes[next].Name}"))
                connected.Add(next);
            waiting.Remove(next);
        }
    }

    /// <summary>
    /// The first road, for a plan that needs a network before it can grow one.
    /// Normally the anchor to the place it reaches most cheaply - but the
    /// shipped anchor is a coast cell that is often in the sea and can reach
    /// nothing at all, and a plan that gave up there would report the anchor's
    /// fault as its own. When that happens the cheapest routable pair of
    /// places starts the network instead.
    /// </summary>
    private static (int a, int b)? SeedConnection(List<Node> nodes, Dictionary<(int, int), float> costs)
    {
        int best = -1;
        float bestCost = float.MaxValue;
        for (int i = 1; i < nodes.Count; i++)
        {
            float? cost = Cost(costs, 0, i);
            if (cost.HasValue && cost.Value < bestCost) { bestCost = cost.Value; best = i; }
        }
        if (best >= 0)
            return (0, best);

        (int, int)? cheapest = null;
        float cheapestCost = float.MaxValue;
        foreach (KeyValuePair<(int, int), float> entry in costs)
        {
            if (entry.Key.Item1 == 0 || entry.Key.Item2 == 0) continue;
            if (entry.Value < cheapestCost) { cheapestCost = entry.Value; cheapest = entry.Key; }
        }
        if (cheapest.HasValue)
            Log.LogDebug("Plan: the anchor can reach nothing; seeding from the cheapest pair of places");
        return cheapest;
    }

    /// <summary>
    /// Road points the reverse search can aim its heuristic at. Sampled, not
    /// complete: the heuristic only has to not overestimate, and a sample of
    /// the centreline cannot, because a nearer road point can only make the
    /// true distance shorter than the estimate, never longer.
    /// </summary>
    private static List<Vector2> NetworkHints()
    {
        List<Vector2> hints = new();
        foreach (RoadRoute route in RoadRouteRecorder.Routes)
            for (int i = 0; i < route.Points.Count; i += 8)
                hints.Add(new Vector2(route.Points[i].x, route.Points[i].z));
        return hints;
    }

    /// <summary>
    /// Turn the question round: instead of extending the network to a place,
    /// let the place find the network.
    ///
    /// Every other plan here picks a target first - another place, or the
    /// straight-line nearest point on a road - and then asks the pathfinder to
    /// reach it. That guess is made on straight-line distance, which is what a
    /// road cannot use. This plan asks the search itself: it runs outward from
    /// the place with no destination and stops at the first ground it settles
    /// that already carries road. Whatever it finds is the cheapest way onto
    /// the network from there, and it is a junction wherever it lands.
    /// </summary>
    private static void GenerateReverseToNetworkRoads(
        Vector3 startPos, float startRadius,
        List<(string name, Vector3 position, float radius)> locations,
        string startName)
    {
        List<Node> nodes = PlanNodes(startPos, startRadius, startName, locations);
        if (nodes.Count < 2 || m_pathfinder == null)
            return;

        // Something has to exist before anything can reach it. The first road
        // is the anchor to the place it can reach most cheaply, exactly as the
        // routed tree would start.
        Dictionary<(int, int), float> costs = RoutedCosts(nodes);
        (int a, int b)? seed = SeedConnection(nodes, costs);
        if (!seed.HasValue)
        {
            Log.LogDebug("Reverse to network: nothing on this island is routable to anything else");
            return;
        }
        HashSet<int> done = new() { 0, seed.Value.a, seed.Value.b };
        Build(nodes[seed.Value.a], nodes[seed.Value.b]);

        // Then every remaining place, most important first, reaches for it.
        List<int> waiting = Enumerable.Range(1, nodes.Count - 1).Where(i => !done.Contains(i)).ToList();
        waiting.Sort((x, y) => GetLocationPriority(nodes[y].Name).CompareTo(GetLocationPriority(nodes[x].Name)));

        foreach (int node in waiting)
        {
            Node place = nodes[node];
            PathfinderTrace? trace = RoadAttemptLog.Begin();
            Vector2 from = new(place.Position.x, place.Position.z);
            List<Vector2>? path = m_pathfinder.FindPathToNetwork(
                from, StudyFactors.ReverseSearchReach, NetworkHints());

            if (path == null || path.Count < 2)
            {
                RoadAttemptLog.Finish(trace, $"{place.Name} -> road", from, from,
                    connected: false, m_pathfinder.LastOutcome, 0f, 0);
                continue;
            }

            // The search found the ground; the ordinary primitive builds the
            // road, so a plan cannot smuggle in a route the generator would
            // not have made. It is asked for the point the search actually
            // reached, which is a point on a road, not a place.
            RoadAttemptLog.Finish(trace, $"{place.Name} -> road", from, path[path.Count - 1],
                connected: true, "found", 0f, 0);
            Vector3 junction = new(path[path.Count - 1].x, 0f, path[path.Count - 1].y);
            if (GenerateRoad(place.Position, place.Radius, junction, 0f, RoadWidth, $"{place.Name} -> road"))
                done.Add(node);
        }
    }

    /// <summary>
    /// A second chance at a connection the pathfinder could not build: the
    /// nearest point on a road already built, then the nearest place already
    /// on the network. Both are searches the plan did not intend to run, so
    /// they are counted where a reader can see them.
    ///
    /// The case for it is that a road runs where the ground allowed it, so it
    /// often reaches ground a particular place does not: a link that failed
    /// from A may succeed from a point on the road between A and B.
    /// </summary>
    public static bool TryFallbackConnection(Vector3 target, float targetRadius, string targetName,
        IEnumerable<(Vector3 position, float radius, string name)> onNetwork)
    {
        if (StudyFactors.Fallback == FailureFallback.None)
            return false;

        bool road = StudyFactors.Fallback is FailureFallback.NearestRoad or FailureFallback.RoadThenPlace;
        bool place = StudyFactors.Fallback is FailureFallback.NearestConnectedPlace or FailureFallback.RoadThenPlace;

        if (road)
        {
            Vector3? junction = NearestPointOnBuiltRoad(target);
            if (junction.HasValue
                && GenerateRoad(junction.Value, 0f, target, targetRadius, RoadWidth, $"road -> {targetName}"))
            {
                FallbacksToRoad++;
                return true;
            }
        }

        if (place)
        {
            (Vector3 position, float radius, string name)? nearest = null;
            float best = float.MaxValue;
            foreach ((Vector3 position, float radius, string name) candidate in onNetwork)
            {
                float distance = Vector3.Distance(candidate.position, target);
                if (distance < best) { best = distance; nearest = candidate; }
            }
            if (nearest.HasValue
                && GenerateRoad(nearest.Value.position, nearest.Value.radius, target, targetRadius, RoadWidth,
                    $"{nearest.Value.name} -> {targetName}"))
            {
                FallbacksToPlace++;
                return true;
            }
        }

        return false;
    }

    /// <summary>Connections recovered by each fallback, for the report.</summary>
    public static int FallbacksToRoad { get; private set; }
    public static int FallbacksToPlace { get; private set; }

    public static void ResetFallbackCounters() { FallbacksToRoad = 0; FallbacksToPlace = 0; }

    /// <summary>
    /// The nearest point on a road already built to a place, or none when no
    /// road is near enough to be worth joining. Straight-line nearest is the
    /// right question here: the spur's own search decides what the join
    /// actually costs, and a junction far off the road's line is not a junction.
    /// </summary>
    private static Vector3? NearestPointOnBuiltRoad(Vector3 place)
    {
        Vector3? best = null;
        float bestDistance = float.MaxValue;
        Vector2 at = new(place.x, place.z);

        foreach (RoadRoute route in RoadRouteRecorder.Routes)
        {
            // Every fourth point is plenty: the centreline is dense, and a
            // junction a metre either way is the same junction.
            for (int i = 0; i < route.Points.Count; i += 4)
            {
                Vector3 point = route.Points[i];
                float dx = point.x - at.x, dz = point.z - at.y;
                float distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                }
            }
        }

        if (best == null)
            return null;

        // A road further away than the link limit is not a network to join.
        return Mathf.Sqrt(bestDistance) <= MaxRoadLinkDistance ? best : null;
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
