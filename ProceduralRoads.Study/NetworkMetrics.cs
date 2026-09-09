using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// What a finished network is worth, measured the same way for every run.
///
/// Road count is not it. A policy that selects different places builds a
/// different number of roads between them, so counting roads rewards a policy
/// for choosing more destinations rather than for connecting them better.
/// These are the numbers a comparison can carry: how many places the network
/// actually reaches, and in how many separate pieces.
///
/// Definitions are fixed here and changed only on their own, never alongside a
/// change to generation - a metric that moves with the thing it measures can
/// never be attributed.
/// </summary>
internal static class NetworkMetrics
{
    /// <summary>A road end this close to a place serves it.</summary>
    public const float ServedRadius = 25f;

    /// <summary>Two road ends this close together are one junction.</summary>
    public const float JoinRadius = 24f;

    /// <summary>
    /// Two roads that end at the same place are one network even when their
    /// ends sit far apart on its approach circle, which they usually do: a
    /// road stops at the circle wherever it arrives, so two roads to one
    /// village can finish eighty metres apart on opposite sides of it.
    /// Counting those as separate networks made the study report an island as
    /// split when a player would walk straight from one road to the other.
    /// </summary>
    public const float PlaceJoinMargin = 8f;

    /// <summary>
    /// Four different things get called "connected", and they answer different
    /// questions, so they are kept apart.
    /// </summary>
    public sealed class Result
    {
        /// <summary>Places with a road end within reach. Geometry only: it does
        /// not say the road was built for that place, or that a player can walk
        /// from one to the other.</summary>
        public int PlacesServed;

        /// <summary>Places the generator planned a road to and built it. Taken
        /// from the attempt log by the place's own identity, not by distance,
        /// so a neighbour cannot inherit the outcome.</summary>
        public int PlannedConnections;

        /// <summary>Roads joined to each other geometrically. Two ends within
        /// the join radius count as one network even if a river or a cliff lies
        /// between them: this is a drawing-level measure, not a walkable
        /// one.</summary>
        public int Components;

        public int LargestComponentRoutes;

        /// <summary>Total centreline length of every route added up. A road
        /// shared by two routes is counted twice, so this punishes a strategy
        /// for sharing.</summary>
        public float SummedLengthMetres;

        /// <summary>Length of distinct road on the ground, counting a stretch
        /// used by two routes once. This is what a player would pace out.</summary>
        public float UniqueLengthMetres;
    }

    /// <summary>
    /// A place and an attempt endpoint are the same place when they are within
    /// this: the endpoint was passed to the generator from the place's own
    /// record, so this is an identity check with room for rounding, not a
    /// guess about which place a road end belongs to.
    /// </summary>
    public const float IdentityTolerance = 1.5f;

    public static Result Measure(IReadOnlyList<RoadRoute> routes, IReadOnlyList<Program.Location> places,
        IReadOnlyList<RoadAttempt>? attempts = null)
    {
        Result result = new();

        if (attempts != null)
        {
            foreach (Program.Location place in places)
            {
                Vector2 at = new(place.Position.x, place.Position.z);
                foreach (RoadAttempt attempt in attempts)
                {
                    if (!attempt.Connected)
                        continue;
                    if (Vector2.Distance(attempt.Start, at) <= IdentityTolerance
                        || Vector2.Distance(attempt.End, at) <= IdentityTolerance)
                    {
                        result.PlannedConnections++;
                        break;
                    }
                }
            }
        }

        foreach (Program.Location place in places)
        {
            float reach = Mathf.Max(ServedRadius, place.Radius);
            Vector2 at = new(place.Position.x, place.Position.z);
            foreach (RoadRoute route in routes)
            {
                if (route.Points.Count == 0)
                    continue;
                if (Near(route.Points[0], at, reach) || Near(route.Points[route.Points.Count - 1], at, reach))
                {
                    result.PlacesServed++;
                    break;
                }
            }
        }

        (result.Components, result.LargestComponentRoutes) = Components(routes, places);
        result.SummedLengthMetres = routes.Sum(r => r.Length);
        result.UniqueLengthMetres = UniqueLength(routes);
        return result;
    }

    /// <summary>
    /// Road on the ground, counting a stretch used by two routes once.
    ///
    /// Each segment is counted unless a road laid earlier already occupies the
    /// ground under its midpoint. Counting occupied cells instead was tried
    /// first and read HIGHER than the summed length, because a road crossing a
    /// cell diagonally occupies more cells per metre than one crossing it
    /// square: a unique length above the summed length is a contradiction, and
    /// it was the estimator, not the roads.
    /// </summary>
    public const float SharedRoadCellSize = 4f;

    private static float UniqueLength(IReadOnlyList<RoadRoute> routes)
    {
        HashSet<(int, int)> laid = new();
        float unique = 0f;

        foreach (RoadRoute route in routes)
        {
            List<(int, int)> mine = new();
            for (int i = 1; i < route.Points.Count; i++)
            {
                Vector3 a = route.Points[i - 1], b = route.Points[i];
                float dx = b.x - a.x, dz = b.z - a.z;
                float length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length <= 0f)
                    continue;

                (int, int) cell = (
                    Mathf.FloorToInt((a.x + b.x) * 0.5f / SharedRoadCellSize),
                    Mathf.FloorToInt((a.z + b.z) * 0.5f / SharedRoadCellSize));

                if (!laid.Contains(cell))
                    unique += length;
                mine.Add(cell);
            }

            foreach ((int, int) cell in mine)
                laid.Add(cell);
        }

        return unique;
    }

    private static bool Near(Vector3 point, Vector2 at, float radius)
    {
        float dx = point.x - at.x, dz = point.z - at.y;
        return dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>
    /// Union-find over route ends: two roads are one network when an end of one
    /// lies within the join radius of any point of the other, so a road meeting
    /// another halfway along counts as joined.
    /// </summary>
    private static (int components, int largest) Components(
        IReadOnlyList<RoadRoute> routes, IReadOnlyList<Program.Location> places)
    {
        int n = routes.Count;
        if (n == 0)
            return (0, 0);

        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (int a = 0; a < n; a++)
        {
            for (int b = a + 1; b < n; b++)
            {
                if (Touch(routes[a], routes[b]))
                    Union(a, b);
            }
        }

        // Roads that end at the same place are one network, however far apart
        // on its approach circle they finished.
        foreach (Program.Location place in places)
        {
            float reach = Mathf.Max(ServedRadius, place.Radius) + PlaceJoinMargin;
            Vector2 at = new(place.Position.x, place.Position.z);
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                if (routes[i].Points.Count == 0)
                    continue;
                if (!Near(routes[i].Points[0], at, reach)
                    && !Near(routes[i].Points[routes[i].Points.Count - 1], at, reach))
                    continue;

                if (first < 0) first = i;
                else Union(first, i);
            }
        }

        Dictionary<int, int> sizes = new();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            sizes.TryGetValue(root, out int size);
            sizes[root] = size + 1;
        }

        int largest = 0;
        foreach (int size in sizes.Values)
            if (size > largest) largest = size;
        return (sizes.Count, largest);
    }

    private static bool Touch(RoadRoute a, RoadRoute b)
    {
        if (a.Points.Count == 0 || b.Points.Count == 0)
            return false;

        return EndTouches(a, b) || EndTouches(b, a);
    }

    private static bool EndTouches(RoadRoute ends, RoadRoute along)
    {
        Vector3[] candidates = { ends.Points[0], ends.Points[ends.Points.Count - 1] };
        foreach (Vector3 end in candidates)
        {
            foreach (Vector3 point in along.Points)
            {
                float dx = end.x - point.x, dz = end.z - point.z;
                if (dx * dx + dz * dz <= JoinRadius * JoinRadius)
                    return true;
            }
        }

        return false;
    }
}
