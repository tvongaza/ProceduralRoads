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

        for (int i = 1; i < path.Count; i++)
        {
            bool wet = SegmentTouchesRiverCore(path[i - 1], path[i], world);

            if (wet && runStart < 0)
                runStart = i - 1;

            if (!wet && runStart >= 0)
            {
                crossings.Add(BuildCrossing(path, runStart, i - 1, world));
                runStart = -1;
            }
        }

        // A path should never end inside a river, but guard anyway.
        if (runStart >= 0)
            crossings.Add(BuildCrossing(path, runStart, path.Count - 1, world));

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

    private static RoadCrossing BuildCrossing(List<Vector2> path, int fromIndex, int toIndex, WorldGenerator world)
    {
        Vector2 from = path[fromIndex];
        Vector2 to = path[toIndex];
        float width = Vector2.Distance(from, to);

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
}
