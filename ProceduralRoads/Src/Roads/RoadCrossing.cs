using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Metadata for one river crossing (ford) on a generated road: where the
/// road leaves each bank, the river profile between them, and the fairway —
/// the deepest contiguous span, which future bridge ruins must keep clear
/// of piers and debris so boats can still sail through.
/// </summary>
/// <summary>Bridge: deep or sailable water, pieces span it. Ford: knee-deep
/// water the road goes through in one of the FordStyle ways.</summary>
public enum CrossingKind { Bridge, Ford }

/// <summary>How a ford treats the shallows (Tys, 2026-09-02): WADE paints
/// the ground and leaves it at its height (the road goes through the
/// water), RAISE levels the road up through the shallows, SPAN builds a
/// short low bridge with steps at each end. Chosen per site for variety.</summary>
public enum FordStyle { None, Wade, Raise, Span }

public sealed class RoadCrossing
{
    public CrossingKind Kind = CrossingKind.Bridge;
    public FordStyle Style = FordStyle.None;

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
        int lineFrom = -1; // the wet run's own start point (the jump endpoint)
        int lastEnd = -1;  // ToIndex of the previous crossing (banks never overlap)

        // A bank is the nearest path point that stands above the waterline
        // clearance, not merely the last point outside the river core:
        // splined centerlines dip through marshy shelves below the waterline
        // on their way into the channel, and an abutment placed there sits in
        // the water (live witness: banks at 29.0/29.4 vs water 30.0).
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        bool AboveWater(int index) => BiomeBlendedHeight.GetBlendedHeight(path[index].x, path[index].y, world) >= minBank;

        for (int i = 1; i < path.Count; i++)
        {
            bool wet = SegmentTouchesRiverCore(path[i - 1], path[i], world);

            if (wet && runStart < 0)
            {
                runStart = i - 1;
                lineFrom = runStart; // the jump's own endpoint: the crossing LINE follows it
                while (runStart > lastEnd + 1 && !AboveWater(runStart))
                    runStart--;      // painting exclusion may reach further back over wet shelves
            }

            if (!wet && runStart >= 0)
            {
                int end = i - 1;
                int lineTo = end;
                while (end < path.Count - 1 && !AboveWater(end))
                    end++;
                RoadCrossing? crossing = BuildCrossing(path, runStart, end, lineFrom, lineTo, world);
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
            RoadCrossing? crossing = BuildCrossing(path, runStart, path.Count - 1, lineFrom, path.Count - 1, world);
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
    private static RoadCrossing? BuildCrossing(List<Vector2> path, int fromIndex, int toIndex,
        int lineFromIndex, int lineToIndex, WorldGenerator world)
    {
        // The crossing LINE (deck, banks, profile) follows the wet run's own
        // endpoints — the jump segment — never the indices extended outward
        // over wet shelves for painting; otherwise the deck is planned along a
        // line that cuts across the road (fixture witness: 6-12 m off).
        Vector2 dryFrom = path[lineFromIndex];
        Vector2 dryTo = path[lineToIndex];

        // The deck spans the WATER, not the dry approaches: the path's last
        // dry cells can sit 8-10 m up the bank (a 36 m "crossing" over a
        // 15 m pond, decks running into hillsides). Each bank is the last
        // point along the ford line that can legally carry road, so the
        // painted road runs down the approach and stops at the abutment.
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        Vector2 from = Bank(dryFrom, dryTo, world, minBank);
        Vector2 to = Bank(dryTo, dryFrom, world, minBank);
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

        // Knee-deep and unsailable: a FORD, in one of three styles chosen
        // per site so roads vary. Wading only where the water is ankle deep;
        // a span only where there is room for a deck.
        bool ford = fairwayWidth <= 0f && riverbed >= RoadConstants.SeaLevel - RoadConstants.FordWadeDepth;
        FordStyle style = FordStyle.None;
        if (ford)
        {
            float depth = RoadConstants.SeaLevel - riverbed;
            List<FordStyle> eligible = new() { FordStyle.Raise };
            if (depth <= RoadConstants.FordWadeMaxDepth) eligible.Add(FordStyle.Wade);
            if (width >= RoadConstants.FordSpanMinWidth) eligible.Add(FordStyle.Span);
            style = PickFordStyle(eligible, SiteHash(center));
        }

        return new RoadCrossing
        {
            Kind = ford ? CrossingKind.Ford : CrossingKind.Bridge,
            Style = style,
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

    /// <summary>Player-facing lever (config "Fords/WadeWeight", "RaiseWeight",
    /// "SpanWeight"): relative odds of each ford style among the styles a
    /// site allows. 0 removes a style; when every allowed style is 0 the site
    /// raises the road (always allowed). Set at config read.</summary>
    public static float ConfiguredWadeWeight = RoadConstants.DefaultFordStyleWeight;
    public static float ConfiguredRaiseWeight = RoadConstants.DefaultFordStyleWeight;
    public static float ConfiguredSpanWeight = RoadConstants.DefaultFordStyleWeight;

    public static void SetFordStyleWeights(float wade, float raise, float span)
    {
        ConfiguredWadeWeight = Mathf.Max(0f, wade);
        ConfiguredRaiseWeight = Mathf.Max(0f, raise);
        ConfiguredSpanWeight = Mathf.Max(0f, span);
    }

    private static float WeightOf(FordStyle style) => style switch
    {
        FordStyle.Wade => ConfiguredWadeWeight,
        FordStyle.Raise => ConfiguredRaiseWeight,
        FordStyle.Span => ConfiguredSpanWeight,
        _ => 0f,
    };

    /// <summary>Weighted pick driven by the site hash, so a world regenerates
    /// with the same fords. Equal weights reduce to the plain modulo pick,
    /// so default worlds keep the styles they had before the lever existed.</summary>
    internal static FordStyle PickFordStyle(List<FordStyle> eligible, int hash)
    {
        float total = 0f, first = -1f;
        bool equal = true;
        foreach (FordStyle s in eligible)
        {
            float w = Mathf.Max(0f, WeightOf(s));
            total += w;
            if (first < 0f) first = w;
            else if (Mathf.Abs(w - first) > 1e-6f) equal = false;
        }
        if (total <= 0f)
            return FordStyle.Raise;
        if (equal)
            return eligible[hash % eligible.Count];

        float r = (hash & 0xFFFF) / 65536f * total;
        foreach (FordStyle s in eligible)
        {
            float w = Mathf.Max(0f, WeightOf(s));
            if (w <= 0f) continue;
            if (r < w) return s;
            r -= w;
        }
        return FordStyle.Raise; // float tail
    }

    /// <summary>Deterministic per-site hash so a ford keeps its style across
    /// loads and worlds regenerate identically.</summary>
    private static int SiteHash(Vector2 center)
    {
        unchecked
        {
            int x = Mathf.RoundToInt(center.x), y = Mathf.RoundToInt(center.y);
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (int)((h ^ (h >> 16)) & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// The bank on one side of the crossing line: if the run endpoint stands
    /// on legal road ground, walk inward to the water's edge; if it is itself
    /// wet (a marsh point on an ordinary path, a swamp shelf before a jump),
    /// walk OUTWARD along the same line to the first legal point. Either way
    /// the bank lies on the crossing line, so the deck follows the road.
    /// </summary>
    private static Vector2 Bank(Vector2 endpoint, Vector2 other, WorldGenerator world, float minBank)
    {
        if (BiomeBlendedHeight.GetBlendedHeight(endpoint.x, endpoint.y, world) >= minBank)
            return Shore(endpoint, other, world, minBank);

        Vector2 outward = (endpoint - other).normalized;
        for (float d = 0.5f; d <= 60f; d += 0.5f)
        {
            Vector2 p = endpoint + outward * d;
            if (BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world) >= minBank)
                return p;
        }
        return endpoint;
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
            if (BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world) < minBank)
            {
                // A 1-2 m pothole on the approach is not the shore: keep
                // walking if the ground recovers just beyond it.
                Vector2 p1 = dry + dir * Mathf.Min(d + 1f, length);
                Vector2 p2 = dry + dir * Mathf.Min(d + 2f, length);
                if (BiomeBlendedHeight.GetBlendedHeight(p1.x, p1.y, world) < minBank && BiomeBlendedHeight.GetBlendedHeight(p2.x, p2.y, world) < minBank)
                    break;
                continue;
            }
            last = p;
        }
        return last;
    }
}
