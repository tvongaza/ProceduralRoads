using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// One river crossing on a generated road (bridges prototype): where the road
/// leaves each bank, the river profile between the banks, and the fairway,
/// the deepest contiguous stretch, which a bridge leaves open so boats can
/// still sail through.
/// </summary>
public sealed class RoadCrossing
{
    /// <summary>Path vertices bracketing the water: the crossing is the path
    /// segment [FromIndex, ToIndex]. Not persisted; only the generation pass
    /// that paints the land on either side needs them.</summary>
    public int FromIndex;
    public int ToIndex;

    /// <summary>Last road ground on each side: where the deck starts and ends.</summary>
    public Vector2 FromBank;
    public Vector2 ToBank;

    /// <summary>Midpoint between the banks.</summary>
    public Vector2 Center;

    /// <summary>Normalized FromBank -> ToBank.</summary>
    public Vector2 Direction;

    /// <summary>Bank-to-bank distance in metres.</summary>
    public float Width;

    public float WaterLevel = RoadConstants.SeaLevel;

    /// <summary>Lowest terrain height between the banks.</summary>
    public float RiverbedHeight;

    /// <summary>Centre of the deepest contiguous stretch (depth at least
    /// <see cref="RoadCrossingDetector.FairwayMinDepth"/>), or the single
    /// deepest sample when no stretch qualifies.</summary>
    public Vector2 FairwayCenter;

    /// <summary>Length of that stretch in metres; 0 when the river is too shallow to sail.</summary>
    public float FairwayWidth;

    /// <summary>A crossing between two banks with its derived fields filled in.</summary>
    public static RoadCrossing Between(Vector2 fromBank, Vector2 toBank, float riverbedHeight,
        Vector2 fairwayCenter, float fairwayWidth)
    {
        Vector2 direction = toBank - fromBank;
        direction.Normalize();
        return new RoadCrossing
        {
            FromBank = fromBank,
            ToBank = toBank,
            Center = (fromBank + toBank) * 0.5f,
            Direction = direction,
            Width = Vector2.Distance(fromBank, toBank),
            RiverbedHeight = riverbedHeight,
            FairwayCenter = fairwayCenter,
            FairwayWidth = fairwayWidth,
        };
    }

    /// <summary>Signed distance of a point along the crossing line, from FromBank toward ToBank.</summary>
    public float Along(Vector2 p) => Vector2.Dot(p - FromBank, Direction);
}

/// <summary>
/// Finds the river crossings on a finished path. With bridges on, the
/// pathfinder jumps a river in one straight segment from dry cell to dry
/// cell; this walks each such segment to the water's edge on both sides and
/// profiles the river between them. Pure logic: the same code runs in the
/// game and in the headless test harness.
/// </summary>
public static class RoadCrossingDetector
{
    /// <summary>Minimum water depth that counts as sailable fairway.</summary>
    public const float FairwayMinDepth = 1.2f;

    private const float SampleSpacing = 2f;
    private const float ShoreStep = 0.5f;

    public static List<RoadCrossing> Detect(List<Vector2> path, WorldGenerator world)
    {
        List<RoadCrossing> crossings = new();
        if (path == null || path.Count < 2 || world == null)
            return crossings;

        for (int i = 1; i < path.Count; i++)
        {
            if (!SegmentCrossesRiver(path[i - 1], path[i], world))
                continue;
            RoadCrossing? crossing = Build(path[i - 1], path[i], world);
            if (crossing == null)
                continue;
            crossing.FromIndex = i - 1;
            crossing.ToIndex = i;
            crossings.Add(crossing);
        }
        return crossings;
    }

    /// <summary>Ground a road may stand on, so also where a deck may end.</summary>
    public static bool IsRoadGround(Vector2 p, WorldGenerator world) =>
        BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world) >= RoadConstants.ShallowWaterHeight;

    /// <summary>A segment with river water under it somewhere: the jump the
    /// pathfinder made. A splined road dipping into a puddle between two dry
    /// cells is not a crossing.</summary>
    private static bool SegmentCrossesRiver(Vector2 a, Vector2 b, WorldGenerator world)
    {
        float length = Vector2.Distance(a, b);
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
        for (int s = 0; s <= samples; s++)
        {
            float t = (float)s / samples;
            float x = a.x + (b.x - a.x) * t;
            float y = a.y + (b.y - a.y) * t;
            world.GetRiverWeight(x, y, out float weight, out _);
            if (weight > RoadConstants.RiverImpassableThreshold
                && BiomeBlendedHeight.GetBlendedHeight(x, y, world) < RoadConstants.SeaLevel)
                return true;
        }
        return false;
    }

    private static RoadCrossing? Build(Vector2 a, Vector2 b, WorldGenerator world)
    {
        // The deck spans the water, not the dry approaches: each bank is the
        // last road ground along the jump before the water, so the painted
        // road runs down to the abutment and the deck lies on the road.
        Vector2 from = Shore(a, b, world);
        Vector2 to = Shore(b, a, world);
        float width = Vector2.Distance(from, to);
        if (width < 1f)
            return null;

        // Profile the river along the bank-to-bank line at about 1 m spacing.
        int samples = Mathf.Max(2, Mathf.CeilToInt(width));
        float riverbed = float.MaxValue;
        Vector2 deepestPoint = from;
        int bestRunStart = -1, bestRunLength = 0, runStart = -1;
        for (int s = 0; s <= samples; s++)
        {
            float t = (float)s / samples;
            Vector2 p = new(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t);
            float h = BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);
            if (h < riverbed)
            {
                riverbed = h;
                deepestPoint = p;
            }

            bool sailable = h <= RoadConstants.SeaLevel - FairwayMinDepth;
            if (sailable && runStart < 0)
                runStart = s;
            if ((!sailable || s == samples) && runStart >= 0)
            {
                int runEnd = sailable ? s : s - 1;
                int runLength = runEnd - runStart + 1;
                if (runLength > bestRunLength)
                {
                    bestRunLength = runLength;
                    bestRunStart = runStart;
                }
                runStart = -1;
            }
        }

        // A crossing needs water under it; a dry river valley is ordinary road.
        if (riverbed >= RoadConstants.SeaLevel)
            return null;

        Vector2 fairwayCenter = deepestPoint;
        float fairwayWidth = 0f;
        if (bestRunLength > 0)
        {
            float t = (bestRunStart + (bestRunLength - 1) / 2f) / samples;
            fairwayCenter = new Vector2(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t);
            fairwayWidth = bestRunLength * (width / samples);
        }

        return RoadCrossing.Between(from, to, riverbed, fairwayCenter, fairwayWidth);
    }

    /// <summary>
    /// The water's edge walking from <paramref name="start"/> toward
    /// <paramref name="end"/>: the last point that stands on road ground
    /// before the ground turns wet and stays wet. A wet start is its own
    /// bank; a walk that never meets water keeps the start as its bank.
    /// </summary>
    private static Vector2 Shore(Vector2 start, Vector2 end, WorldGenerator world)
    {
        float length = Vector2.Distance(start, end);
        if (length < 0.01f || !IsRoadGround(start, world))
            return start;

        Vector2 dir = (end - start) * (1f / length);
        Vector2 last = start;
        for (float d = ShoreStep; d <= length; d += ShoreStep)
        {
            Vector2 p = start + dir * d;
            if (IsRoadGround(p, world))
            {
                last = p;
                continue;
            }
            // A 1-2 m pothole on the approach is not the shore: the ground
            // must still be wet 1 m and 2 m further on.
            bool wetAhead = !IsRoadGround(start + dir * Mathf.Min(length, d + 1f), world)
                && !IsRoadGround(start + dir * Mathf.Min(length, d + 2f), world);
            if (wetAhead)
                return last;
        }
        return start;
    }
}
