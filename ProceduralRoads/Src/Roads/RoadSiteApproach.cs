using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// A bounded local alternative to carving straight into the side of a site.
/// Tries seven arrival directions and three bends over the last 64 metres.
/// Scores the actual smoothed, grade-limited profile across the road's width;
/// keeps the old route unless a candidate materially reduces earthworks.
/// This is not entrance detection: candidates stay on the same side of the
/// site, outside its exterior radius, and never introduce a water crossing.
/// </summary>
public static class RoadSiteApproach
{
    public const float Reach = 64f;

    public static List<Vector2> Improve(List<Vector2> path, Vector2 centre, float radius,
        float width, WorldGenerator world, bool atStart,
        Func<Vector2, float?> target, Func<Vector2, float> ground)
    {
        if (radius <= 0f || radius > Reach || path.Count < 3) return path;
        var oriented = new List<Vector2>(path);
        if (atStart) oriented.Reverse();
        int last = oriented.Count - 1, join = last;
        float oldLength = 0;
        while (join > 0 && oldLength < Reach)
        {
            oldLength += Vector2.Distance(oriented[join], oriented[join - 1]);
            join--;
        }
        // Leave enough of the old route to preserve its other approach.
        if (join < 2 || oldLength > Reach * 1.5f) return path;
        Vector2 oldEnd = oriented[last], anchor = oriented[join];
        float arrivalRadius = Vector2.Distance(oldEnd, centre);
        if (arrivalRadius < radius - 0.1f || arrivalRadius > radius + 16f) return path;
        var oldTail = oriented.GetRange(join, oriented.Count - join);
        // Per-decision cache only; release it when this approach is decided.
        var samples = new Dictionary<Vector2, float>();
        float Ground(Vector2 p)
        {
            if (!samples.TryGetValue(p, out float h)) samples[p] = h = ground(p);
            return h;
        }
        float anchorHeight = Ground(anchor);
        float Score(List<Vector2> tail, out float worst)
        {
            worst = 0f;
            // Trial profiles do not contribute to the network's grade report.
            float previousGrade = RoadGrade.SteepestPlanned;
            RoadSpatialGrid.PlannedPath? plan;
            try { plan = RoadSpatialGrid.PlanRoadPath(tail, width, world, anchorHeight, target(tail[tail.Count - 1])); }
            finally { RoadGrade.SteepestPlanned = previousGrade; }
            if (plan == null) return float.PositiveInfinity;
            double sum = 0;
            for (int i = 0; i < plan.Points.Count; i++)
            {
                Vector2 point = plan.Points[i];
                if (float.IsNaN(plan.Heights[i]) || float.IsInfinity(plan.Heights[i])) return float.PositiveInfinity;
                // Inspect the spline, not just the control points. Its turn
                // must not cut through the location or sneak across water.
                if (Vector2.Distance(point, centre) < radius - 0.1f) return float.PositiveInfinity;
                world.GetRiverWeight(point.x, point.y, out float river, out _);
                if (river > RoadConstants.RiverImpassableThreshold || Ground(point) < RoadConstants.ShallowWaterHeight)
                    return float.PositiveInfinity;
                Vector2 direction = plan.Points[Math.Min(i + 1, plan.Points.Count - 1)]
                    - plan.Points[Math.Max(i - 1, 0)];
                Vector2 side = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
                for (int j = -1; j <= 1; j++)
                {
                    float h = Ground(point + side * j);
                    if (float.IsNaN(h) || float.IsInfinity(h)) return float.PositiveInfinity;
                    float cut = Mathf.Abs(plan.Heights[i] - h);
                    worst = Mathf.Max(worst, cut);
                    sum += cut;
                }
            }
            return (float)(sum / (plan.Points.Count * 3)) + worst * 0.5f + plan.TotalLength * 0.015f;
        }
        float oldScore = Score(oldTail, out float oldWorst);
        if (float.IsInfinity(oldScore) || oldWorst < 2f) return path;
        float bestScore = oldScore;
        List<Vector2>? best = null;
        Vector2 radial = oldEnd - centre;
        for (int turn = -3; turn <= 3; turn++)
        {
            float angle = turn * Mathf.PI / 8f;
            Vector2 end = centre + new Vector2(radial.x * Mathf.Cos(angle) - radial.y * Mathf.Sin(angle),
                radial.x * Mathf.Sin(angle) + radial.y * Mathf.Cos(angle));
            Vector2 line = end - anchor;
            Vector2 side = new Vector2(-line.y, line.x).normalized;
            for (int bend = -1; bend <= 1; bend++)
            {
                Vector2 middle = (anchor + end) * 0.5f + side * (bend * 16f);
                var candidate = new List<Vector2>();
                float length = 0;
                // Quadratic arc avoids an abrupt dogleg at a single corner.
                for (int step = 0; step <= 8; step++)
                {
                    float t = step / 8f;
                    Vector2 point = anchor * ((1 - t) * (1 - t)) + middle * (2 * t * (1 - t)) + end * (t * t);
                    if (candidate.Count > 0) length += Vector2.Distance(candidate[candidate.Count - 1], point);
                    candidate.Add(point);
                }
                if (length > oldLength * 1.5f) continue;
                float score = Score(candidate, out float worst);
                if (worst > oldWorst - 0.5f || score >= bestScore || score > oldScore * 0.8f) continue;
                bestScore = score;
                best = candidate;
            }
        }
        if (best == null) return path;
        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug(
            $"Site approach {centre}: end {oldEnd} -> {best[best.Count - 1]}, earthwork score {oldScore:F2} -> {bestScore:F2}");
        var result = oriented.GetRange(0, join);
        result.AddRange(best);
        if (atStart) result.Reverse();
        return result;
    }
}
