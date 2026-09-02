using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Metadata for one river crossing (ford) on a generated road: where the
/// road leaves each bank, the river profile between them, and the fairway —
/// the deepest contiguous span, which future bridge ruins must keep clear
/// of piers and debris so boats can still sail through.
/// </summary>
public sealed class RoadCrossing
{
    /// <summary>Index of the route this crossing belongs to.</summary>
    public int RouteIndex;

    /// <summary>Path indices of the bank points (last dry point each side).</summary>
    public int FromIndex;
    public int ToIndex;

    public Vector2 FromBank;
    public Vector2 ToBank;

    /// <summary>Midpoint between the banks.</summary>
    public Vector2 Center;

    /// <summary>Normalized FromBank -> ToBank.</summary>
    public Vector2 Direction;

    /// <summary>Bank-to-bank distance in meters.</summary>
    public float Width;

    public float WaterLevel;

    /// <summary>Lowest terrain height found between the banks.</summary>
    public float RiverbedHeight;

    /// <summary>
    /// Center of the deepest contiguous stretch (depth >= FairwayMinDepth).
    /// Falls back to the single deepest sample when no stretch qualifies.
    /// </summary>
    public Vector2 FairwayCenter;

    /// <summary>Length of that stretch in meters; 0 when the river is too shallow to sail.</summary>
    public float FairwayWidth;

    public Heightmap.Biome Biome;
}

/// <summary>
/// Detects river crossings on a finished path by finding maximal runs of
/// segments whose interiors pass through impassable river core, then
/// profiles the river between the enclosing dry bank points. Pure logic —
/// runs identically in-game and in the headless test harness.
/// </summary>
public static class RoadCrossingDetector
{
    /// <summary>Minimum water depth that counts as sailable fairway.</summary>
    public const float FairwayMinDepth = 1.2f;

    private const float SampleSpacing = 2f;

    public static List<RoadCrossing> Detect(List<Vector2> path, WorldGenerator world)
    {
        List<RoadCrossing> crossings = new();
        if (path == null || path.Count < 2 || world == null)
            return crossings;

        int runStart = -1; // index of the dry point before the first wet segment
        int lastEnd = -1;  // ToIndex of the previous crossing (banks never overlap)

        // A bank is the nearest path point that stands above the waterline
        // clearance, not merely the last point outside the river core:
        // splined centerlines dip through marshy shelves below the waterline
        // on their way into the channel, and an abutment placed there sits in
        // the water (live witness: banks at 29.0/29.4 vs water 30.0).
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        bool AboveWater(int index) => world.GetHeight(path[index].x, path[index].y) >= minBank;

        for (int i = 1; i < path.Count; i++)
        {
            bool wet = SegmentTouchesRiverCore(path[i - 1], path[i], world);

            if (wet && runStart < 0)
            {
                runStart = i - 1;
                while (runStart > lastEnd + 1 && !AboveWater(runStart))
                    runStart--;
            }

            if (!wet && runStart >= 0)
            {
                int end = i - 1;
                while (end < path.Count - 1 && !AboveWater(end))
                    end++;
                RoadCrossing? crossing = BuildCrossing(path, runStart, end, world);
                if (crossing != null)
                    crossings.Add(crossing);
                lastEnd = end;
                runStart = -1;
                if (end > i)
                    i = end; // resume scanning past the extended far bank
            }
        }

        // A path should never end inside a river, but guard anyway.
        if (runStart >= 0)
        {
            RoadCrossing? crossing = BuildCrossing(path, runStart, path.Count - 1, world);
            if (crossing != null)
                crossings.Add(crossing);
        }

        return crossings;
    }

    private static bool SegmentTouchesRiverCore(Vector2 a, Vector2 b, WorldGenerator world)
    {
        float length = Vector2.Distance(a, b);
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
        for (int s = 0; s <= samples; s++)
        {
            float t = (float)s / samples;
            float x = a.x + (b.x - a.x) * t;
            float y = a.y + (b.y - a.y) * t;
            world.GetRiverWeight(x, y, out float weight, out _);
            if (weight > RoadConstants.RiverImpassableThreshold)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the crossing between two dry path points, or returns null when
    /// the channel is a knee-deep, unsailable gully that a leveled road can
    /// simply go through (no bridge, no painting exclusion).
    /// </summary>
    private static RoadCrossing? BuildCrossing(List<Vector2> path, int fromIndex, int toIndex, WorldGenerator world)
    {
        Vector2 dryFrom = path[fromIndex];
        Vector2 dryTo = path[toIndex];

        // The deck spans the WATER, not the dry approaches: the path's last
        // dry cells can sit 8-10 m up the bank (a 36 m "crossing" over a
        // 15 m pond, decks running into hillsides). Each bank is the last
        // point along the ford line that can legally carry road, so the
        // painted road runs down the approach and stops at the abutment.
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        Vector2 from = Shore(dryFrom, dryTo, world, minBank);
        Vector2 to = Shore(dryTo, dryFrom, world, minBank);
        float width = Vector2.Distance(from, to);
        if (width < 0.5f)
            return null;

        Vector2 direction = to - from;
        direction.Normalize();

        // Profile the river along the bank-to-bank line at 1m spacing.
        int samples = Mathf.Max(2, Mathf.CeilToInt(width));
        float riverbed = float.MaxValue;
        Vector2 deepestPoint = from;

        int bestRunStart = -1, bestRunLength = 0;
        int runStart = -1;

        for (int s = 0; s <= samples; s++)
        {
            float t = (float)s / samples;
            Vector2 p = new(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t);
            float h = world.GetHeight(p.x, p.y);

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

        float step = width / samples;
        Vector2 fairwayCenter = deepestPoint;
        float fairwayWidth = 0f;
        if (bestRunLength > 0)
        {
            float midSample = bestRunStart + (bestRunLength - 1) / 2f;
            float t = midSample / samples;
            fairwayCenter = new Vector2(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t);
            fairwayWidth = bestRunLength * step;
        }

        // Knee-deep and unsailable: a road goes through as a leveled ford.
        if (fairwayWidth <= 0f && riverbed >= RoadConstants.SeaLevel - RoadConstants.FordWadeDepth)
            return null;

        Vector2 center = (from + to) * 0.5f;

        return new RoadCrossing
        {
            FromIndex = fromIndex,
            ToIndex = toIndex,
            FromBank = from,
            ToBank = to,
            Center = center,
            Direction = direction,
            Width = width,
            WaterLevel = RoadConstants.SeaLevel,
            RiverbedHeight = riverbed,
            FairwayCenter = fairwayCenter,
            FairwayWidth = fairwayWidth,
            Biome = world.GetBiome(center.x, center.y),
        };
    }

    /// <summary>Walks from a dry point toward the water and returns the last
    /// point whose ground can legally carry road (>= minBank).</summary>
    private static Vector2 Shore(Vector2 dry, Vector2 wet, WorldGenerator world, float minBank)
    {
        float length = Vector2.Distance(dry, wet);
        if (length < 0.01f)
            return dry;
        Vector2 dir = (wet - dry).normalized;

        Vector2 last = dry;
        for (float d = 0.5f; d < length; d += 0.5f)
        {
            Vector2 p = dry + dir * d;
            if (world.GetHeight(p.x, p.y) < minBank)
            {
                // A 1-2 m pothole on the approach is not the shore: keep
                // walking if the ground recovers just beyond it.
                Vector2 p1 = dry + dir * Mathf.Min(d + 1f, length);
                Vector2 p2 = dry + dir * Mathf.Min(d + 2f, length);
                if (world.GetHeight(p1.x, p1.y) < minBank && world.GetHeight(p2.x, p2.y) < minBank)
                    break;
                continue;
            }
            last = p;
        }
        return last;
    }
}
