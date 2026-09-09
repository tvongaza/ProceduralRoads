using System.Collections.Generic;
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

    public sealed class Result
    {
        public int PlacesServed;
        public int Components;
        public int LargestComponentRoutes;
    }

    public static Result Measure(IReadOnlyList<RoadRoute> routes, IReadOnlyList<Program.Location> places)
    {
        Result result = new();

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

        (result.Components, result.LargestComponentRoutes) = Components(routes);
        return result;
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
    private static (int components, int largest) Components(IReadOnlyList<RoadRoute> routes)
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

        for (int a = 0; a < n; a++)
        {
            for (int b = a + 1; b < n; b++)
            {
                if (!Touch(routes[a], routes[b]))
                    continue;
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
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
