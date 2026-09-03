using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Machine-checkable validation of a generated road network against the
/// world it was generated for. Pure logic: no BepInEx, no game singletons —
/// callers pass routes and a WorldGenerator, so the same checks run in-game
/// (via RoadValidationRunner) and in the headless test harness.
/// </summary>
public static class RoadNetworkValidator
{
    // Crossings may be fords (<= MaxRiverCrossingCells) or, over wide sailable
    // rivers, bridges (<= MaxBridgeCrossingCells); anything longer is a route
    // that wandered through river core on ordinary moves.
    private static readonly float FordLengthCap = RoadConstants.MaxBridgeCrossingCells * RoadPathfinder.CellSize;
    public const float SlopeSanityCap = 1.5f;
    public const float EndpointJoinRadius = 24f;
    private const int MaxViolationsPerCheck = 12;

    public sealed class Report
    {
        public int RouteCount;
        public float TotalLengthMeters;
        public int PointCount;
        public int NetworkComponents;
        public int FordCount;
        /// <summary>Centerline points under the water threshold, exempt or not.</summary>
        public int WetPoints;
        /// <summary>Wet points that no recorded crossing span or wading rule
        /// covers: the uncapped dry-land count. The listed violations stop at
        /// MaxViolationsPerCheck, so this is the number that moves.</summary>
        public int WetPointsOutsideSpans;
        public int OverLengthCrossings;
        public int OverGradePoints;
        public string PointsHash = "";
        public readonly List<string> Violations = new();
        public bool Passed => Violations.Count == 0;
    }

    public static Report Validate(IReadOnlyList<RoadRoute> routes, WorldGenerator world,
        IReadOnlyList<RoadCrossing>? crossings = null)
    {
        Report report = new();
        if (routes == null || world == null)
        {
            report.Violations.Add("validator: routes or world unavailable");
            return report;
        }

        uint hash = 2166136261;
        int dryLandViolations = 0, fordViolations = 0, slopeViolations = 0;
        int dryLandTotal = 0, fordTotal = 0, slopeTotal = 0;

        // A wet point is legal only inside a RECORDED crossing span (a deck
        // sits over water) — not merely inside river core, which would let a
        // spurious crossing hide its own underwater points. Callers without
        // crossing metadata fall back to the river-core rule.
        bool InRecordedCrossing(RoadRoute route, Vector3 p)
        {
            if (crossings == null)
                return false;
            foreach (RoadCrossing c in crossings)
            {
                if (c.RouteIndex != route.Index)
                    continue;
                Vector2 rel = new(p.x - c.FromBank.x, p.z - c.FromBank.y);
                float along = Vector2.Dot(rel, c.Direction);
                float across = Mathf.Abs(rel.x * c.Direction.y - rel.y * c.Direction.x);
                if (along >= -2f && along <= c.Width + 2f && across <= 6f)
                    return true;
            }
            return false;
        }

        foreach (RoadRoute route in routes)
        {
            report.RouteCount++;
            report.TotalLengthMeters += route.Length;
            report.PointCount += route.Points.Count;

            int fordRunStart = -1;

            for (int i = 0; i < route.Points.Count; i++)
            {
                Vector3 p = route.Points[i];
                hash = HashPoint(hash, p);

                float height = BiomeBlendedHeight.GetBlendedHeight(p.x, p.z, world);
                world.GetRiverWeight(p.x, p.z, out float riverWeight, out _);
                bool inRiverCore = riverWeight > RoadConstants.RiverImpassableThreshold;

                // Dry-land invariant: a wet point is legal only inside a
                // recorded crossing span (or, without crossing metadata, in
                // river core) or as wadeable swamp shallows.
                // Knee-deep water (bed within FordWadeDepth of the waterline) is a
                // leveled ford by design (6f1dc31): the road goes through it.
                bool kneeDeepFord = height >= RoadConstants.SeaLevel - RoadConstants.FordWadeDepth;
                bool exempt = kneeDeepFord || (crossings != null ? InRecordedCrossing(route, p) : inRiverCore);
                if (height < RoadConstants.ShallowWaterHeight - 0.25f)
                    report.WetPoints++;
                if (height < RoadConstants.ShallowWaterHeight - 0.25f && !exempt)
                {
                    bool swampWade = world.GetBiome(p.x, p.z) == Heightmap.Biome.Swamp
                                     && height >= RoadConstants.DeepWaterHeight;
                    if (!swampWade)
                    {
                        dryLandTotal++;
                        if (dryLandViolations++ < MaxViolationsPerCheck)
                            report.Violations.Add(
                                $"dry-land: {route.Label} point {i} ({p.x:F0},{p.z:F0}) height {height:F1}");
                    }
                }

                // Crossing-length invariant: consecutive points over WATER
                // (ground a road may not stand on: below the waterline
                // clearance, or in a swamp below wading depth — the same rule
                // the crossing detector uses for its banks) must span no more
                // than the bridge cap. Measured on water, not river core: a
                // core band can be a dry valley the road simply runs through,
                // and a swamp shelf the road wades is road, not crossing.
                bool overWater = !RoadCrossingDetector.IsRoadGround(new Vector2(p.x, p.z), world);
                if (overWater)
                {
                    if (fordRunStart < 0)
                        fordRunStart = i;
                }
                else if (fordRunStart >= 0)
                {
                    int startIdx = fordRunStart > 0 ? fordRunStart - 1 : fordRunStart;
                    float span = Vector3.Distance(route.Points[startIdx], route.Points[i]);
                    // A crossing is at least a cell of water; shorter runs are dips.
                    if (span >= RoadPathfinder.CellSize)
                        report.FordCount++;
                    if (span > FordLengthCap + 16f)
                        fordTotal++;
                    if (span > FordLengthCap + 16f && fordViolations++ < MaxViolationsPerCheck)
                        report.Violations.Add(
                            $"crossing-length: {route.Label} spans {span:F0}m of water (cap {FordLengthCap:F0}m)");
                    fordRunStart = -1;
                }

                // Slope sanity between consecutive centerline points.
                if (i > 0)
                {
                    Vector3 prev = route.Points[i - 1];
                    float dx = p.x - prev.x, dz = p.z - prev.z;
                    float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
                    if (horizontal > 0.01f)
                    {
                        float slope = Mathf.Abs(p.y - prev.y) / horizontal;
                        if (slope > SlopeSanityCap)
                            slopeTotal++;
                        if (slope > SlopeSanityCap && slopeViolations++ < MaxViolationsPerCheck)
                            report.Violations.Add(
                                $"slope: {route.Label} point {i} grade {slope:F2} over {horizontal:F1}m");
                    }
                }
            }

            // A ford run reaching the route's final point is a waterfront
            // terminus (e.g. a harbour road ending at the water) — by design.
        }

        report.PointsHash = hash.ToString("x8");
        report.NetworkComponents = CountComponents(routes);
        // The listed lines are capped for readability; the totals are what the
        // instrument measured — a display limit must never become a measurement
        // limit, so they are report fields too. "Wet points outside recorded
        // spans" is a count of what fell OUTSIDE every exemption, not of all
        // wet points (WetPoints has those): the two together say how much of
        // the water the decks cover.
        report.WetPointsOutsideSpans = dryLandTotal;
        report.OverLengthCrossings = fordTotal;
        report.OverGradePoints = slopeTotal;
        if (dryLandTotal > MaxViolationsPerCheck)
            report.Violations.Add($"dry-land: {dryLandTotal} wet points outside recorded spans ({MaxViolationsPerCheck} listed)");
        if (fordTotal > MaxViolationsPerCheck)
            report.Violations.Add($"crossing-length: {fordTotal} over-length crossings ({MaxViolationsPerCheck} listed)");
        if (slopeTotal > MaxViolationsPerCheck)
            report.Violations.Add($"slope: {slopeTotal} over-grade points ({MaxViolationsPerCheck} listed)");

        return report;
    }

    /// <summary>
    /// Two routes that end at the same named location are one network even
    /// when their ends sit apart on its radius circle (the Reroute terminus
    /// puts dry ends anywhere on the dry arc). Location names are prefab
    /// names and repeat across an island, so the two ends must also lie
    /// within this distance of each other: one location, not two instances
    /// of one prefab. Comfortably above twice any exterior radius.
    /// </summary>
    public const float LocationJoinRadius = 100f;

    /// <summary>
    /// Union-find over route endpoints: routes whose endpoints touch (within
    /// EndpointJoinRadius) or that end at the same named location (see
    /// LocationJoinRadius) belong to one network component.
    /// </summary>
    private static int CountComponents(IReadOnlyList<RoadRoute> routes)
    {
        int n = routes.Count;
        if (n == 0) return 0;

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
                if (RoutesTouch(routes[a], routes[b]) || RoutesShareLocation(routes[a], routes[b]))
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra != rb) parent[ra] = rb;
                }
            }
        }

        HashSet<int> roots = new();
        for (int i = 0; i < n; i++) roots.Add(Find(i));
        return roots.Count;
    }

    private static bool RoutesTouch(RoadRoute a, RoadRoute b)
    {
        if (a.Points.Count == 0 || b.Points.Count == 0) return false;

        Vector3[] endsA = { a.Points[0], a.Points[a.Points.Count - 1] };
        Vector3[] endsB = { b.Points[0], b.Points[b.Points.Count - 1] };

        foreach (Vector3 ea in endsA)
        {
            // An endpoint may join mid-route (T junction), so compare against
            // every point of the other route, not just its ends.
            foreach (Vector3 pb in b.Points)
            {
                float dx = ea.x - pb.x, dz = ea.z - pb.z;
                if (dx * dx + dz * dz <= EndpointJoinRadius * EndpointJoinRadius)
                    return true;
            }
        }

        foreach (Vector3 eb in endsB)
        {
            foreach (Vector3 pa in a.Points)
            {
                float dx = eb.x - pa.x, dz = eb.z - pa.z;
                if (dx * dx + dz * dz <= EndpointJoinRadius * EndpointJoinRadius)
                    return true;
            }
        }

        return false;
    }

    private static bool RoutesShareLocation(RoadRoute a, RoadRoute b)
    {
        if (a.Points.Count == 0 || b.Points.Count == 0) return false;
        (string? aStart, string? aEnd) = LocationNames(a.Label);
        (string? bStart, string? bEnd) = LocationNames(b.Label);
        if (aStart == null && aEnd == null) return false;

        Vector3 a0 = a.Points[0], a1 = a.Points[a.Points.Count - 1];
        Vector3 b0 = b.Points[0], b1 = b.Points[b.Points.Count - 1];
        return SameLocation(aStart, a0, bStart, b0) || SameLocation(aStart, a0, bEnd, b1)
            || SameLocation(aEnd, a1, bStart, b0) || SameLocation(aEnd, a1, bEnd, b1);
    }

    private static bool SameLocation(string? nameA, Vector3 endA, string? nameB, Vector3 endB)
    {
        if (string.IsNullOrEmpty(nameA) || nameA != nameB) return false;
        float dx = endA.x - endB.x, dz = endA.z - endB.z;
        return dx * dx + dz * dz <= LocationJoinRadius * LocationJoinRadius;
    }

    /// <summary>Generated routes are labelled "{from} -> {to}" with the
    /// location names at each end; any other label carries no names.</summary>
    private static (string? start, string? end) LocationNames(string? label)
    {
        if (string.IsNullOrEmpty(label)) return (null, null);
        int i = label!.IndexOf(" -> ", System.StringComparison.Ordinal);
        if (i < 0) return (null, null);
        string start = label.Substring(0, i).Trim();
        string end = label.Substring(i + 4).Trim();
        return (start.Length > 0 ? start : null, end.Length > 0 ? end : null);
    }

    private static uint HashPoint(uint hash, Vector3 p)
    {
        unchecked
        {
            hash = (hash ^ (uint)Mathf.RoundToInt(p.x * 10f)) * 16777619;
            hash = (hash ^ (uint)Mathf.RoundToInt(p.y * 10f)) * 16777619;
            hash = (hash ^ (uint)Mathf.RoundToInt(p.z * 10f)) * 16777619;
            return hash;
        }
    }

    public static string ToJson(Report report)
    {
        StringBuilder sb = new();
        sb.Append("{\n");
        sb.Append($"  \"passed\": {(report.Passed ? "true" : "false")},\n");
        sb.Append($"  \"routeCount\": {report.RouteCount},\n");
        sb.Append($"  \"totalLengthMeters\": {report.TotalLengthMeters:F0},\n");
        sb.Append($"  \"pointCount\": {report.PointCount},\n");
        sb.Append($"  \"networkComponents\": {report.NetworkComponents},\n");
        sb.Append($"  \"fordCount\": {report.FordCount},\n");
        sb.Append($"  \"wetPoints\": {report.WetPoints},\n");
        sb.Append($"  \"wetPointsOutsideSpans\": {report.WetPointsOutsideSpans},\n");
        sb.Append($"  \"overLengthCrossings\": {report.OverLengthCrossings},\n");
        sb.Append($"  \"overGradePoints\": {report.OverGradePoints},\n");
        sb.Append($"  \"pointsHash\": \"{report.PointsHash}\",\n");
        sb.Append("  \"violations\": [\n");
        for (int i = 0; i < report.Violations.Count; i++)
        {
            sb.Append("    \"").Append(Escape(report.Violations[i])).Append('"');
            sb.Append(i < report.Violations.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    public static string ToRoutesCsv(IReadOnlyList<RoadRoute> routes)
    {
        StringBuilder sb = new();
        sb.Append("route_index,label,point_index,x,y,z\n");
        foreach (RoadRoute route in routes)
        {
            for (int i = 0; i < route.Points.Count; i++)
            {
                Vector3 p = route.Points[i];
                sb.Append(route.Index).Append(',')
                  .Append('"').Append(Escape(route.Label)).Append('"').Append(',')
                  .Append(i).Append(',')
                  .Append(p.x.ToString("F1")).Append(',')
                  .Append(p.y.ToString("F1")).Append(',')
                  .Append(p.z.ToString("F1")).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
