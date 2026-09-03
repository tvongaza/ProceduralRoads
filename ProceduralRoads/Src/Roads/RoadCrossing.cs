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

    /// <summary>Signed distance of a point along the crossing line, from FromBank toward ToBank.</summary>
    public float Along(Vector2 p) => Vector2.Dot(p - FromBank, Direction);

    /// <summary>Unsigned distance of a point from the crossing line.</summary>
    public float Across(Vector2 p)
    {
        Vector2 rel = p - FromBank;
        return Mathf.Abs(rel.x * Direction.y - rel.y * Direction.x);
    }
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

    /// <summary>Vertices join a crossing only within this distance of its
    /// line (the jump segment): the deck is straight, so a road that bends
    /// along a wet shelf after landing is road, not deck. Inside the
    /// validator's 6 m corridor with margin for the chord's own tilt.</summary>
    public const float LineCorridor = 4f;

    /// <summary>At a path end a wet vertex may seek its bank this far out
    /// along the last segment's line; further is a route that ends in
    /// water, which the terminus rules should have prevented.</summary>
    public const float EndBankReach = 16f;

    public static List<RoadCrossing> Detect(List<Vector2> path, WorldGenerator world)
    {
        List<RoadCrossing> crossings = new();
        if (path == null || path.Count < 2 || world == null)
            return crossings;

        int runFirst = -1; // first vertex of the current core-touching run
        int lastEnd = -1;  // highest vertex any crossing reached (banks never overlap)

        for (int i = 1; i < path.Count; i++)
        {
            bool wet = SegmentTouchesRiverCore(path[i - 1], path[i], world);
            if (wet && runFirst < 0)
                runFirst = i - 1;
            if (runFirst < 0)
                continue;
            bool lastSegment = i == path.Count - 1;
            if (wet && !lastSegment)
                continue;

            int runLast = wet ? i : i - 1;
            int reached = CollectCrossings(path, runFirst, runLast, lastEnd + 1, path.Count - 1, world, crossings);
            lastEnd = Mathf.Max(lastEnd, reached);
            runFirst = -1;
            if (lastEnd > i)
                i = lastEnd; // resume scanning past the extended far bank
        }

        crossings.Sort((x, y) => x.FromIndex.CompareTo(y.FromIndex));
        return crossings;
    }

    /// <summary>
    /// Builds the crossings of one core-touching run of segments
    /// [first..last]. The crossing LINE is the run's longest segment over
    /// water — the jump — and a bank is sought along the path from the
    /// jump's ends: outward over wet vertices as long as they stay near
    /// the line (a shelf the road runs straight across is deck), never
    /// around a bend (a shelf the road turns along after landing is road:
    /// RoadTestMac2 route 49, whose deck ran 15 m south of the road because
    /// the run's chord followed the bend). The rest of the run may hold a
    /// further channel (a braided river with a dry bar the road walks), so
    /// the parts before and after the crossing are searched again.
    /// Returns the highest vertex index any crossing reached, or -1.
    /// </summary>
    private static int CollectCrossings(List<Vector2> path, int first, int last, int lo, int hi,
        WorldGenerator world, List<RoadCrossing> crossings)
    {
        int jump = -1;
        float jumpLength = 0f;
        for (int s = first; s < last; s++)
        {
            if (!SegmentHasWater(path[s], path[s + 1], world))
                continue;
            float length = Vector2.Distance(path[s], path[s + 1]);
            if (length > jumpLength)
            {
                jump = s;
                jumpLength = length;
            }
        }
        if (jump < 0)
            return -1; // dry ground inside the core band: ordinary road

        Vector2 a = path[jump], b = path[jump + 1];
        int start = jump, end = jump + 1;
        while (start > lo && !IsRoadGround(path[start], world) && NearLine(path[start - 1], a, b))
            start--;
        while (end < hi && !IsRoadGround(path[end], world) && NearLine(path[end + 1], a, b))
            end++;

        RoadCrossing? crossing = BuildCrossing(path, start, end, world);
        if (crossing != null)
            crossings.Add(crossing);

        int reached = end;
        if (start > first)
            reached = Mathf.Max(reached, CollectCrossings(path, first, start, lo, start, world, crossings));
        if (end < last)
            reached = Mathf.Max(reached, CollectCrossings(path, end, last, end, hi, world, crossings));
        return reached;
    }

    private static bool NearLine(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.01f)
            return true;
        Vector2 rel = p - a;
        float across = Mathf.Abs(rel.x * dir.y - rel.y * dir.x) / length;
        return across <= LineCorridor;
    }

    /// <summary>A segment with water under it somewhere: a candidate crossing.
    /// Damp ground above the waterline never makes a crossing on its own.</summary>
    private static bool SegmentHasWater(Vector2 a, Vector2 b, WorldGenerator world)
    {
        float length = Vector2.Distance(a, b);
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
        for (int s = 0; s <= samples; s++)
        {
            float t = (float)s / samples;
            float x = a.x + (b.x - a.x) * t;
            float y = a.y + (b.y - a.y) * t;
            if (BiomeBlendedHeight.GetBlendedHeight(x, y, world) < RoadConstants.SeaLevel)
                return true;
        }
        return false;
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
        // The deck spans the WATER, not the dry approaches: the path's last
        // dry cells can sit 8-10 m up the bank (a 36 m "crossing" over a
        // 15 m pond, decks running into hillsides). Each bank is the last
        // point ALONG THE PATH that can legally carry road, walking in from
        // the run's dry ends, so the painted road runs down the approach and
        // stops at the abutment — and the deck line, drawn bank to bank,
        // lies on the road. Walking along a chord between the run's ends
        // instead put the banks off the road wherever the run bent: a wet
        // run swallows the shelf before a jump and the bank-hugging road
        // after it (river core covers dry bank land), and the chord through
        // those ends tilted the deck 6 degrees off an 86 m bridge
        // (RoadTestMac2 65a68b3, "Eikthyrnir -> GDKing", 13 points 6-13 m off).
        (Vector2 from, int fromOrdinal) = ShoreAlongPath(path, fromIndex, toIndex, world);
        (Vector2 to, int toOrdinal) = ShoreAlongPath(path, toIndex, fromIndex, world);
        fromIndex += fromOrdinal;
        toIndex -= toOrdinal;

        // High bridge: when the road climbs a cliff on both sides of the
        // water, the deck springs from the bank tops instead of the water's
        // edge (RoadConstants.HighBankReach / HighBankRise). Both banks move
        // together, and only when the tops are level enough for one deck.
        {
            float fromH = BiomeBlendedHeight.GetBlendedHeight(from.x, from.y, world);
            float toH = BiomeBlendedHeight.GetBlendedHeight(to.x, to.y, world);
            (Vector2 topFrom, int topFromIndex, float topFromH) = BankTop(path, from, fromIndex, -1, world);
            (Vector2 topTo, int topToIndex, float topToH) = BankTop(path, to, toIndex, +1, world);
            if (topFromH >= fromH + RoadConstants.HighBankRise && topToH >= toH + RoadConstants.HighBankRise
                && Mathf.Abs(topFromH - topToH) <= RoadConstants.MaxBridgeBankDelta)
            {
                from = topFrom;
                to = topTo;
                fromIndex = topFromIndex;
                toIndex = topToIndex;
            }
        }
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

        // A crossing needs water under it: a dry core valley is ordinary road
        // (night plan 2026-09-03 task 1f, "wade ford" over a bed at 33.5).
        if (riverbed >= RoadConstants.SeaLevel)
            return null;

        Vector2 center = (from + to) * 0.5f;
        Heightmap.Biome biome = world.GetBiome(center.x, center.y);
        bool swamp = biome == Heightmap.Biome.Swamp;

        // Knee-deep and unsailable: a FORD, in one of three styles chosen
        // per site so roads vary. Wading only where the water is ankle deep;
        // a span only where there is room for a deck.
        // Swamps wade deeper (Tys, 2026-09-02, c6/c7): a swamp channel whose
        // bed stays at wading depth (DeepWaterHeight, what the pathfinder
        // already wades) is a ford in the same style mix, wading always
        // allowed — unless a stretch of it is sailable for at least a
        // boat's length, which keeps it a bridge (sailing is sacred; a
        // shorter dip is a pothole, not a fairway).
        bool ford = swamp
            ? fairwayWidth < RoadConstants.SwampFordMaxFairway && riverbed >= RoadConstants.DeepWaterHeight
            : fairwayWidth <= 0f && riverbed >= RoadConstants.SeaLevel - RoadConstants.FordWadeDepth;
        FordStyle style = FordStyle.None;
        if (ford)
        {
            float depth = RoadConstants.SeaLevel - riverbed;
            List<FordStyle> eligible = new() { FordStyle.Raise };
            if (swamp || depth <= RoadConstants.FordWadeMaxDepth) eligible.Add(FordStyle.Wade);
            if (width >= RoadConstants.FordSpanMinWidth) eligible.Add(FordStyle.Span);
            style = PickFordStyle(eligible, SiteHash(center));
        }
        else if (swamp)
        {
            // A BRIDGE starts and ends on land above the water (Tys, 3 Sep
            // 2026, after seeing abutments standing in 2 m of swamp water):
            // in a swamp the wade shelf is road-legal, so the banks found
            // above can sit under the waterline; walk each one outward
            // along the road until the ground clears the waterline
            // clearance, even if that makes the deck longer.
            float dryFloor = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
            bool fromWet = BiomeBlendedHeight.GetBlendedHeight(from.x, from.y, world) < dryFloor;
            bool toWet = BiomeBlendedHeight.GetBlendedHeight(to.x, to.y, world) < dryFloor;
            if (fromWet || toWet)
            {
                if (fromWet)
                    (from, fromIndex) = FirstOutward(path, from, fromIndex, -1, RoadConstants.SwampBridgeDryReach, world, dryFloor);
                if (toWet)
                    (to, toIndex) = FirstOutward(path, to, toIndex, +1, RoadConstants.SwampBridgeDryReach, world, dryFloor);
                width = Vector2.Distance(from, to);
                direction = to - from;
                direction.Normalize();
                center = (from + to) * 0.5f;
                // The profile keeps its riverbed and fairway: the added deck
                // is over the shelf, which is shallower than both.
            }
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
            Biome = biome,
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
    /// Walks the path from the vertex at <paramref name="start"/> toward the
    /// vertex at <paramref name="stop"/> and returns the last point whose
    /// ground can legally carry road (IsRoadGround), with the ordinal (0-based,
    /// counted from start along the walk) of the path vertex at or before it.
    /// A wet start vertex is its own bank (see below). A walk that reaches
    /// the far vertex without meeting water (the run is wadeable or dry all
    /// the way) keeps the start vertex as its bank.
    /// </summary>
    private static (Vector2 bank, int ordinal) ShoreAlongPath(List<Vector2> path, int start, int stop, WorldGenerator world)
    {
        int step = stop >= start ? 1 : -1;
        Vector2 first = path[start];
        bool Legal(Vector2 p) => IsRoadGround(p, world);

        if (!Legal(first))
        {
            // A wet run end is the bank itself (the run stopped at a bend or
            // at the previous crossing) — except at a path end, where the
            // bank is sought a short way out along the last segment's line.
            bool pathEnd = start == 0 || start == path.Count - 1;
            if (!pathEnd || start == stop)
                return (first, 0);
            Vector2 outward = (first - path[start + step]).normalized;
            for (float d = 0.5f; d <= EndBankReach; d += 0.5f)
            {
                Vector2 p = first + outward * d;
                if (Legal(p))
                    return (p, 0);
            }
            return (first, 0);
        }

        // Sample the polyline at 0.5 m; every vertex is a sample of its own,
        // so a bank that IS a vertex comes back exactly.
        List<(Vector2 p, int ordinal)> samples = new() { (first, 0) };
        int ordinal = 0;
        for (int i = start; i != stop; i += step, ordinal++)
        {
            Vector2 a = path[i], b = path[i + step];
            float length = Vector2.Distance(a, b);
            if (length < 0.01f)
                continue;
            Vector2 dir = (b - a) * (1f / length);
            for (float d = 0.5f; d < length; d += 0.5f)
                samples.Add((a + dir * d, ordinal));
            samples.Add((b, ordinal + 1));
        }

        (Vector2 p, int ordinal) last = samples[0];
        bool metWater = false;
        for (int k = 1; k < samples.Count; k++)
        {
            if (!Legal(samples[k].p))
            {
                // A 1-2 m pothole on the approach is not the shore: keep
                // walking if the ground recovers just beyond it.
                int k1 = Mathf.Min(k + 2, samples.Count - 1);
                int k2 = Mathf.Min(k + 4, samples.Count - 1);
                if (!Legal(samples[k1].p) && !Legal(samples[k2].p))
                {
                    metWater = true;
                    break;
                }
                continue;
            }
            last = samples[k];
        }
        return metWater ? last : (first, 0);
    }

    /// <summary>
    /// Walks outward from a bank along the path (step -1 toward the path
    /// start, +1 toward its end) and returns the first point whose ground is
    /// at least <paramref name="floor"/> high, with the path index that
    /// brackets it on the water side (FromIndex / ToIndex semantics). Gives
    /// the bank back unchanged when no such point lies within reach.
    /// </summary>
    private static (Vector2 bank, int index) FirstOutward(List<Vector2> path, Vector2 bank, int bankIndex, int step, float reach, WorldGenerator world, float floor)
    {
        Vector2 pos = bank;
        int next = bankIndex;
        float budget = reach;
        while (budget > 0f && next >= 0 && next < path.Count)
        {
            Vector2 target = path[next];
            float length = Vector2.Distance(pos, target);
            if (length > 0.01f)
            {
                Vector2 dir = (target - pos) * (1f / length);
                for (float d = 0.5f; d < length && d <= budget; d += 0.5f)
                {
                    Vector2 p = pos + dir * d;
                    if (BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world) >= floor)
                        return (p, next);
                }
                if (length <= budget && BiomeBlendedHeight.GetBlendedHeight(target.x, target.y, world) >= floor)
                    return (target, next);
                budget -= length;
            }
            pos = target;
            next += step;
        }
        return (bank, bankIndex);
    }

    /// <summary>
    /// The highest ground within HighBankReach of a bank, walking OUTWARD
    /// from it along the path (step -1 toward the path start from the from
    /// bank, +1 toward the path end from the to bank). Returns the nearest
    /// point where that height is reached, the path index that brackets it
    /// on the water side (FromIndex / ToIndex semantics), and its height.
    /// </summary>
    private static (Vector2 top, int index, float height) BankTop(List<Vector2> path, Vector2 bank, int bankIndex, int step, WorldGenerator world)
    {
        // From the from bank the first vertex outward is path[bankIndex]
        // (the bank lies on segment [bankIndex, bankIndex + 1]); from the to
        // bank it is path[bankIndex] as well (segment [bankIndex - 1, bankIndex]).
        List<(Vector2 p, int index)> samples = new() { (bank, bankIndex) };
        Vector2 pos = bank;
        int next = bankIndex;
        float budget = RoadConstants.HighBankReach;
        while (budget > 0f && next >= 0 && next < path.Count)
        {
            Vector2 target = path[next];
            float length = Vector2.Distance(pos, target);
            if (length > 0.01f)
            {
                Vector2 dir = (target - pos) * (1f / length);
                // Samples on this stretch bracket to the vertex on the water
                // side of it: `next` itself on the from side (the segment is
                // [next, next + 1]) and `next` on the to side (segment
                // [next - 1, next]) — the stretch's near vertex is the bracket
                // once we are past the first stretch, which starts at the bank.
                int bracket = next;
                for (float d = 0.5f; d < length && d <= budget; d += 0.5f)
                    samples.Add((pos + dir * d, bracket));
                if (length <= budget)
                    samples.Add((target, next));
                budget -= length;
            }
            pos = target;
            next += step;
        }

        float best = float.MinValue;
        foreach ((Vector2 p, int _) in samples)
            best = Mathf.Max(best, BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world));
        foreach ((Vector2 p, int index) in samples)
        {
            float h = BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);
            if (h >= best - 0.05f)
                return (p, index, h);
        }
        return (bank, bankIndex, BiomeBlendedHeight.GetBlendedHeight(bank.x, bank.y, world));
    }

    /// <summary>
    /// Ground a road may stand on, so also where a deck may end. Outside
    /// swamps: the waterline clearance, the pathfinder's rule for an
    /// ordinary move. In swamps the pathfinder already wades down to
    /// DeepWaterHeight, so everything shallower is road (a wading ford),
    /// not deck: the deck spans only the water the road could not wade,
    /// instead of a chord over 100 m of wadeable shelf (RoadTestMac2 c19,
    /// 171 m of "crossing" over a 111 m channel).
    /// </summary>
    internal static bool IsRoadGround(Vector2 p, WorldGenerator world)
    {
        float floor = world.GetBiome(p.x, p.y) == Heightmap.Biome.Swamp
            ? RoadConstants.DeepWaterHeight
            : RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        return BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world) >= floor;
    }
}
