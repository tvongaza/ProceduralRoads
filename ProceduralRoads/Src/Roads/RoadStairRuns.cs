using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>A staircase on a road: path points <see cref="FromIndex"/> to
/// <see cref="ToIndex"/> are climbed on it. The road before it stops at
/// <see cref="FromPoint"/>, standing at <see cref="FromHeight"/>; the road after
/// it starts at <see cref="ToPoint"/> and <see cref="ToHeight"/>.</summary>
public sealed class RoadStairSpan
{
    public int FromIndex;
    public int ToIndex;
    public Vector2 FromPoint;
    public Vector2 ToPoint;
    public float FromHeight;
    public float ToHeight;
    public StairKit Kit = StairKit.Stone;
    public StairPlan Plan = new();
    /// <summary>An earlier road laid this staircase: this road walks it and lays nothing.</summary>
    public bool Existing;
}

/// <summary>
/// The staircases of a generated road. The pathfinder buys a step too steep for road as a stair step
/// and keeps the steps it bought; once the path has been trimmed and fitted to
/// its ends, the stretches of it that lie along those steps are found here,
/// joined where only a little road would lie between them, and each is laid
/// out by <see cref="StairLayout"/> in its biome's kit. The road is cut at them
/// as at a bridge: nothing is levelled or painted on a staircase. Pure logic.
/// </summary>
public static partial class RoadStairRuns
{
    /// <summary>
    /// Two staircases with less road than this between them are one: the
    /// stretch between is laid by the stair layout too, as floors and
    /// stairs hugging the ground (stone blocks rather than earthworks
    /// between two stair runs that are close together; 20 m is the working value).
    /// </summary>
    public const float StoneGap = 20f;
    /// <summary>A path point this close to a stair step lies on it (the path's
    /// ends are moved a little by trimming; its middle points are cell centres).</summary>
    public const float OnStep = 1f;
    /// <summary>Stairs on generated roads are this wide (2 m works well and is a bit more flexible).</summary>
    public const float Width = 2f;
    /// <summary>
    /// Where the ground between two staircases going the same way is steeper
    /// than this share of the road's grade cap, the road between them could
    /// not be built, and they are one staircase however far apart, up to
    /// <see cref="SteepGapReach"/>: 217 roads on one world were refused for
    /// "ends too far apart for the cap" between two staircases.
    /// </summary>
    public const float SteepGapShare = 0.8f;
    public const float SteepGapReach = 80f;
    /// <summary>A generated road's staircase gives up after this many pieces of
    /// search per leg: of 9184 layouts on one world, the 19 that failed took 60 s
    /// at the interactive budget, the rest about 50 ms each. Doubled from 25000
    /// with the other two: a third of the layouts
    /// spent their budget and Hildir's cave stopped 45 m short; offline on RoadSeedA-E, 2x reached
    /// 1382 places against 1378 with 127 staircases against 112, for about a fifth more generation
    /// time; 4x added 2 staircases and nothing reached.</summary>
    public const int GenerationExpansions = 50000;
    /// <summary>And after this many in all: following the road's cells leg by leg, a
    /// staircase that could not be laid spent its budget on every leg and backtrack
    /// (387 failures took 1959 s of thread time on RoadSeedA). 40000
    /// left a ridge's second staircase, aimed at the first, unlaid. Doubled with
    /// <see cref="GenerationExpansions"/>.</summary>
    public const int GenerationTotalExpansions = 160000;
    /// <summary>A generated staircase's search gives up on a leg after this many
    /// pieces without getting any nearer the goal. On RoadSeedA (offline):
    /// none 116 s and 206 staircases, 4000 42.5 s and 206, 2000
    /// 30 s and 196; a world's first load in game waits on it. Doubled with
    /// <see cref="GenerationExpansions"/>.</summary>
    public const int GenerationStall = 4000;
    /// <summary>Floors a generated staircase ends on at its top, for the road to meet.</summary>
    public const int TopLandings = 1;

    /// <summary>Layouts already made this generation, by ends and kit: a road's
    /// staircases are laid again on every retry and replan, and by every later
    /// road over the same face (9184 layouts for 1272 staircases kept).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int, int, int, string, int, int), StairPlan> s_plans = new();
    /// <summary>The longest stretch a generated road's staircase is laid out over: the longest laid offline on
    /// RoadSeedA-E's layouts was 208 m (RoadSeedD). Settable for tests.</summary>
    internal static float MaxStairSpan = 250f;
    /// <summary>The share of the allowance a road's staircase layout gets where another follows if it fails
    /// (held to the road's headings, then free in its cells; the last, free anywhere, gets it all): a half.
    /// Offline it reached the same places as the whole allowance (RoadSeedA-E 1383); the slowest island's staircases 34 -> 19 s. A quarter failed a square
    /// staircase off its road's edge that a half lays. Settable for tests.</summary>
    internal static int HeldShare = 2;
    /// <summary>The total allowance of the last layout made on this thread (tests).</summary>
    [ThreadStatic] internal static int LastAllowance;

    /// <summary>
    /// The game's terrain height on a 1 m lattice, read bilinearly between lattice
    /// points as the game draws its terrain mesh (1 m between vertices). Each
    /// worker thread keeps its own tiles of <see cref="TileSize"/> metres, plain
    /// arrays filled as they are first read: a layout asks the ground millions of
    /// times, and sampled afresh by each, the world generator's blended noise made
    /// one island take 401 s in game; shared through a concurrent
    /// dictionary, the lookups themselves were the cost (slower still).
    /// </summary>
    private const int TileSize = 32;
    [ThreadStatic] private static Dictionary<(int, int), float[]>? t_tiles;
    [ThreadStatic] private static int t_generation;
    [ThreadStatic] private static WorldGenerator? t_world;
    [ThreadStatic] private static (int, int) t_lastKey;
    [ThreadStatic] private static float[]? t_last;
    private static int s_generation;

    internal static float GroundAt(float x, float z, WorldGenerator world)
    {
        if (t_tiles == null || t_generation != s_generation || !ReferenceEquals(t_world, world))
        {
            t_tiles = new Dictionary<(int, int), float[]>();
            t_generation = s_generation;
            t_world = world;
            t_last = null;
        }
        int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z);
        float fx = x - x0, fz = z - z0;
        return Mathf.Lerp(Mathf.Lerp(Lattice(x0, z0, world), Lattice(x0 + 1, z0, world), fx),
                          Mathf.Lerp(Lattice(x0, z0 + 1, world), Lattice(x0 + 1, z0 + 1, world), fx), fz);
    }

    private static float Lattice(int i, int k, WorldGenerator world)
    {
        int ti = i >= 0 ? i / TileSize : (i + 1) / TileSize - 1, tk = k >= 0 ? k / TileSize : (k + 1) / TileSize - 1;
        var key = (ti, tk);
        float[]? tile = t_last != null && t_lastKey == key ? t_last : null;
        if (tile == null)
        {
            if (!t_tiles!.TryGetValue(key, out tile))
            {
                tile = new float[TileSize * TileSize];
                for (int n = 0; n < tile.Length; n++) tile[n] = float.NaN;
                t_tiles[key] = tile;
            }
            t_last = tile;
            t_lastKey = key;
        }
        int at = (k - tk * TileSize) * TileSize + (i - ti * TileSize);
        float h = tile[at];
        if (float.IsNaN(h)) tile[at] = h = BiomeBlendedHeight.GetBlendedHeight(i, k, world);
        return h;
    }

    public static void ResetCache()
    {
        System.Threading.Interlocked.Increment(ref s_generation);
        s_plans.Clear();
        s_laid.Clear();
        FeetBeside = FeetBesideRefused = FeetLowered = FeetFacedFreely = FootAprons = SpurFeet = 0;
    }

    /// <summary>
    /// The index ranges of <paramref name="path"/> to climb on stairs. A
    /// staircase only goes one way, so where the path turns from climbing to
    /// descending (over a ridge, through a gully) it is two staircases, and
    /// two are joined across a short gap only when they go the same way: a
    /// layout climbs from its lower end to its upper and cannot cross a crest.
    /// <paramref name="height"/> null: all one way.
    /// </summary>
    public static List<(int from, int to)> Find(IReadOnlyList<Vector2> path, IReadOnlyList<(Vector2 from, Vector2 to)> steps,
        Func<Vector2, float>? height = null)
    {
        var runs = new List<(int from, int to, int way)>();
        if (steps.Count == 0) return new List<(int, int)>();
        int Way(int a, int b) => height == null || height(path[b]) >= height(path[a]) ? 1 : -1;
        for (int i = 1; i < path.Count; i++)
        {
            if (!OnAStep(path[i - 1], path[i], steps)) continue;
            int way = Way(i - 1, i);
            if (runs.Count > 0 && runs[runs.Count - 1].to == i - 1 && runs[runs.Count - 1].way == way)
                runs[runs.Count - 1] = (runs[runs.Count - 1].from, i, way);
            else
                runs.Add((i - 1, i, way));
        }
        // Join runs with only a little road between them, going the same way, or
        // with road between them too steep to build.
        float steep = RoadGrade.Capped(RoadGrade.Configured) ? RoadGrade.Configured * SteepGapShare : float.PositiveInfinity;
        for (int r = runs.Count - 1; r > 0; r--)
        {
            if (runs[r - 1].way != runs[r].way) continue;
            float gap = 0f;
            for (int i = runs[r - 1].to + 1; i <= runs[r].from; i++) gap += Vector2.Distance(path[i - 1], path[i]);
            bool tooSteep = height != null && gap < SteepGapReach && gap > 0f
                && Mathf.Abs(height(path[runs[r].from]) - height(path[runs[r - 1].to])) / gap > steep;
            if (gap >= StoneGap && !tooSteep) continue;
            if (runs[r - 1].to < runs[r].from && Way(runs[r - 1].to, runs[r].from) != runs[r].way) continue;
            runs[r - 1] = (runs[r - 1].from, runs[r].to, runs[r].way);
            runs.RemoveAt(r);
        }
        return runs.ConvertAll(r => (r.from, r.to));
    }

    private static bool OnAStep(Vector2 a, Vector2 b, IReadOnlyList<(Vector2 from, Vector2 to)> steps)
    {
        foreach (var (from, to) in steps)
            if (DistanceToSegment(a, from, to) <= OnStep && DistanceToSegment(b, from, to) <= OnStep
                && Vector2.Distance(a, b) > 0.5f)
                return true;
        return false;
    }

    /// <summary>The path indices round the stair steps on path[from..to] steeper than <see cref="RoadPathfinder.StairMaxGrade"/>
    /// (first to last), or null where none is.</summary>
    internal static (int from, int to)? TooSteepFor(List<Vector2> path, int from, int to, IReadOnlyList<(Vector2 from, Vector2 to)> steps,
        Func<float, float, float> ground)
    {
        int first = -1, last = -1;
        for (int i = from + 1; i <= to; i++)
        {
            float run = Vector2.Distance(path[i - 1], path[i]);
            if (run < 0.5f || !OnAStep(path[i - 1], path[i], steps)) continue;
            if (Mathf.Abs(ground(path[i].x, path[i].y) - ground(path[i - 1].x, path[i - 1].y)) / run <= RoadPathfinder.StairMaxGrade + 0.01f) continue;
            if (first < 0) first = i - 1;
            last = i;
        }
        return first < 0 ? null : (first, last);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Ends of a run closer than this to an existing run's ends are that run.</summary>
    public const float SameRunReach = 2f;

    /// <summary>
    /// Every staircase of <paramref name="path"/> laid out, or null when one
    /// cannot be: the caller then builds the road without stairs, as before.
    /// Not laid either: a run that overlaps a river crossing, or whose layout
    /// (which finds its own way between the run's ends) passes through a
    /// place's footprint or water. A run an earlier road already laid between
    /// the same ends is walked, not laid again. The ground is the height the
    /// road itself is built to (the game's blended terrain), so the two meet.
    /// </summary>
    public static List<RoadStairSpan>? Lay(List<Vector2> path, IReadOnlyList<(Vector2 from, Vector2 to)> steps,
        IReadOnlyList<RoadCrossing> crossings, WorldGenerator world)
    {
        var spans = new List<RoadStairSpan>();
        float Ground(float x, float z) => GroundAt(x, z, world);
        List<(int from, int to)> stretches = Find(path, steps, p => Ground(p.x, p.y));
        for (int k = 0; k < stretches.Count; k++)
        {
            var (from, to) = stretches[k];
            LastFailedStretch = (from, to);
            foreach (RoadCrossing c in crossings)
                if (c.FromIndex < to && c.ToIndex > from) return null;
            Vector2 a = path[from], b = path[to];
            StairPlan plan = null!;
            int start = from;
            StairRun? existing = StairStructures.Between(a, b, SameRunReach);
            StairKit kit;
            var carrying = existing == null ? StairStructures.Carrying(a, b, OnStairReach) : null;
            if (carrying.HasValue && s_laid.TryGetValue(carrying.Value.run, out var carried))
            {
                StructurePiece footPiece = carrying.Value.nearA, topPiece = carrying.Value.nearB;
                // Both ends lie on a staircase an earlier road laid, a few metres from its
                // own ends: the road walks that one. Laid again, the copy ran into it and
                // took the road's stairs with it (433 layouts on RoadSeedA).
                spans.Add(new RoadStairSpan
                {
                    FromIndex = from, ToIndex = to, Existing = true, Kit = carried.kit, Plan = carried.plan,
                    FromPoint = new Vector2(footPiece.Position.x, footPiece.Position.z), FromHeight = Surface(footPiece),
                    ToPoint = new Vector2(topPiece.Position.x, topPiece.Position.z), ToHeight = Surface(topPiece),
                });
                continue;
            }
            if (existing != null)
            {
                // Walked as the earlier road laid it: the same plan, so the same heights to meet.
                if (!s_laid.TryGetValue(existing, out (StairPlan plan, StairKit kit) laid)) return null;
                bool same = Vector2.Distance(existing.From, a) <= SameRunReach;
                a = same ? existing.From : existing.To;
                b = same ? existing.To : existing.From;
                (plan, kit) = laid;
            }
            else
            {
                kit = StairKit.ForLine(path.GetRange(from, to - from + 1), (x, z) => world.GetBiome(x, z), world.GetSeed());
                // The steps it climbs are within its kit's grade: a kit that cannot hold a flight out on
                // cantilevers (wood) is not laid over a step the search bought steeper than RoadPathfinder.StairMaxGrade by
                // another biome's limit (a Mountain face clipped by a Meadows climb). Those steps' cells are closed to stairs
                // and the road searched again, rather than the layout spending its allowance and failing.
                if (kit.Cantilever == null && TooSteepFor(path, from, to, steps, Ground) is (int, int) steep)
                {
                    LastFailedStretch = steep;
                    return null;
                }
                // The road's own headings at the two ends: the staircase lines up with them
                // (within reason), and failing that is laid as it falls; either way none of
                // it may lie on the road it joins.
                float arriving = StairLayout.Bearing(from > 0 ? path[from - 1] : a, from > 0 ? a : path[from + 1]);
                float leaving = StairLayout.Bearing(to + 1 < path.Count ? b : path[to - 1], to + 1 < path.Count ? path[to + 1] : b);
                // One staircase over both stretches, in place of the one before and this.
                bool Merge()
                {
                    if (spans.Count == 0 || spans[spans.Count - 1].Existing) return false;
                    RoadStairSpan merged = spans[spans.Count - 1];
                    spans.RemoveAt(spans.Count - 1);
                    start = merged.FromIndex;
                    LastFailedStretch = (start, to);
                    a = path[start];
                    kit = merged.Kit;
                    var line = new List<Vector2>(path.GetRange(start, to - start + 1)) { b };
                    float mergedArriving = StairLayout.Bearing(start > 0 ? path[start - 1] : a, start > 0 ? a : path[start + 1]);
                    float? mergedFacing = FootFacing(path, start, to, Ground);
                    plan = Plan(a, b, kit, Ground, mergedArriving, leaving, line, retried: true, facing: mergedFacing);
                    if (!Usable(plan, path, start, to, world, spans)) plan = Plan(a, b, kit, Ground, null, null, line, retried: mergedFacing.HasValue, facing: mergedFacing);
                    if (mergedFacing.HasValue && !Usable(plan, path, start, to, world, spans))
                    {
                        System.Threading.Interlocked.Increment(ref FeetFacedFreely);
                        plan = Plan(a, b, kit, Ground, null, null, line);
                    }
                    return Usable(plan, path, start, to, world, spans);
                }
                // The road between the staircase before it and this one too steep to
                // build, judged from where the earlier one really ended (up to 3.5 m and
                // half a metre off its path point): one staircase over both. 149 roads
                // on RoadSeedA lost their stairs there.
                if (spans.Count > 0 && !spans[spans.Count - 1].Existing && RoadGrade.Capped(RoadGrade.Configured))
                {
                    RoadStairSpan prev = spans[spans.Count - 1];
                    float gap = Vector2.Distance(prev.ToPoint, path[Mathf.Min(prev.ToIndex, from)]);
                    for (int i = prev.ToIndex + 1; i <= from; i++) gap += Vector2.Distance(path[i - 1], path[i]);
                    if (gap < SteepGapReach && Mathf.Abs(Ground(a.x, a.y) - prev.ToHeight) > RoadGrade.Configured * SteepGapShare * Mathf.Max(gap, 1f))
                    {
                        if (!Merge()) return null;
                        goto Laid;
                    }
                }
                var corridor = new List<Vector2> { a };
                corridor.AddRange(path.GetRange(start, to - start + 1));
                corridor.Add(b);
                // Every layout sets off within 90 degrees of the way the road comes to its foot (StairLayout.FacingSteps), or
                // back beside the road for at most StairLayout.MostSetOffBack pieces first, and only as a last resort any way:
                // set off freely, the foot at Hildir_cave 1664,768 climbed back alongside the road that had arrived heading
                // the other way.
                float? facing = FootFacing(path, start, to, Ground);
                int tops = TopLandings;
                plan = Plan(a, b, kit, Ground, arriving, leaving, corridor, topLandings: tops, retried: true, facing: facing);
                if (!Usable(plan, path, start, to, world, spans)) plan = Plan(a, b, kit, Ground, null, null, corridor, topLandings: tops, retried: true, facing: facing);
                // Its own way between the ends: kept to the road's cells, a layout
                // that stalls, or comes back onto its road or another staircase,
                // cost the whole road its stairs (540 roads on RoadSeedA at 35/25, though 4777 of 5900 layouts were laid).
                if (!Usable(plan, path, start, to, world, spans)) plan = Plan(a, b, kit, Ground, null, null, null, topLandings: tops, retried: facing.HasValue, facing: facing);
                if (facing.HasValue && !Usable(plan, path, start, to, world, spans))
                {
                    System.Threading.Interlocked.Increment(ref FeetFacedFreely);
                    plan = Plan(a, b, kit, Ground, null, null, null, topLandings: tops);
                }
                // Into the staircase before it, or onto the road between: one
                // staircase over both stretches instead. On a steep road with
                // short switchback legs, runs a leg apart piled into each other.
                if (!Usable(plan, path, start, to, world, spans) && !Merge()) return null;
                else if (start == from)
                {
                    // Held to the road's last heading, a flight whose own way lay well off it swung
                    // round on its bottom steps (a weird jog; it should start aligned with the road): it sets off on its own heading instead,
                    // and BesideItsRoad puts it beside the road where that turns off it.
                    if (!plan.Reversed && SwingsAtItsFoot(plan, a))
                    {
                        StairPlan own = Plan(a, b, kit, Ground, null, leaving, corridor, topLandings: tops, facing: facing);
                        if (Usable(own, path, start, to, world, spans)) plan = own;
                    }
                    plan = BesideItsRoad(plan, path, start, to, kit, Ground, corridor, world, spans);
                }
            }
            Laid:
            // The plan climbs from the lower end, exactly, to its own end near the higher.
            Vector2 end = new Vector2(plan.End.x, plan.End.z);
            float endHeight = plan.End.y;
            // Its foot over a road that cannot climb to it within the cap: stairs on down toward the road.
            Vector2 footAt = plan.Reversed ? b : a;
            float footHeight = Ground(footAt.x, footAt.y);
            int beyond = plan.Reversed ? to + 1 : start - 1;
            if (existing == null && beyond >= 0 && beyond < path.Count)
                lock (plan)
                {
                    Vector3? lowered = plan.FootLowered > 0 ? plan.Foot : null;
                    if (lowered == null)
                    {
                        Vector2 backAt = Back(path, plan.Reversed ? to : start, plan.Reversed ? 1 : -1, FootBackReach);
                        lowered = StairLayout.LowerFoot(plan, Ground, Width, Ground(backAt.x, backAt.y), Vector2.Distance(backAt, footAt),
                            RoadGrade.Capped(RoadGrade.Configured) ? RoadGrade.Configured : 1f);
                        if (lowered != null) System.Threading.Interlocked.Increment(ref FeetLowered);
                    }
                    if (lowered is Vector3 f) { footAt = new Vector2(f.x, f.z); footHeight = f.y; }
                }
            // Its foot against a bank: a small level apron cut there for the road to meet (outside any place's footprint:
            // the commit skips cuts inside one). Once per plan (a plan is shared between the roads that walk it).
            if (existing == null)
                lock (plan)
                    if (plan.FootApron == 0 && StairLayout.CutFootApron(plan, Ground, Width) > 0)
                        System.Threading.Interlocked.Increment(ref FootAprons);
            var span = plan.Reversed
                ? new RoadStairSpan { FromIndex = start, ToIndex = to, FromPoint = end, FromHeight = endHeight, ToPoint = footAt, ToHeight = footHeight, Kit = kit, Plan = plan }
                : new RoadStairSpan { FromIndex = start, ToIndex = to, FromPoint = footAt, FromHeight = footHeight, ToPoint = end, ToHeight = endHeight, Kit = kit, Plan = plan };
            span.Existing = existing != null;
            // Columns at its ends over the road's cut are carried on down below it once the network is built, on the ground
            // the terrain writer will leave (BuriedPieces.CarryColumns), not on a guess at the road's cut here.
            spans.Add(span);
        }
        return spans;
    }

    /// <summary>Plans laid on the network this generation, by run, so a later road walking
    /// a run meets it at the heights it was laid at.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<StairRun, (StairPlan plan, StairKit kit)> s_laid = new();

    /// <summary>Record the plan a run was laid from (called as the run joins the network).</summary>
    public static void Laid(StairRun run, StairPlan plan, StairKit kit) => s_laid[run] = (plan, kit);

    private static StairPlan Plan(Vector2 a, Vector2 b, StairKit kit, Func<float, float, float> ground, float? arriving, float? leaving, List<Vector2>? corridor,
        float maxBuried = StairLayout.MaxBuriedDepth, int topLandings = TopLandings, float? footHeight = null, StairContinuation? after = null,
        bool retried = false, bool relayPads = true, float? facing = null)
    {
        int Key(float? h) => h.HasValue ? Mathf.RoundToInt(StairLayout.SnapHeading(h.Value) / StairLayout.HeadingStep) : -1;
        int Row(int h)
        {
            if (!after.HasValue) return h;
            unchecked
            {
                StairContinuation row = after.Value;
                return ((h * 31 + Mathf.RoundToInt(row.Edge.x * 10f)) * 31 + Mathf.RoundToInt(row.Edge.y * 10f)) * 31
                    + Mathf.RoundToInt(row.Height * 100f) * 17 + Key(row.Heading) * 3 + (int)row.Kind;
            }
        }
        int Line()
        {
            if (corridor == null) return Row(-1 - Mathf.RoundToInt(maxBuried * 100f) - 1000 * topLandings);
            unchecked
            {
                int h = corridor.Count;
                foreach (Vector2 p in corridor) h = h * 31 + Mathf.RoundToInt(p.x) * 7919 + Mathf.RoundToInt(p.y);
                return Row(footHeight.HasValue ? h * 31 + Mathf.RoundToInt(footHeight.Value * 100f) : h);
            }
        }
        var key = (Mathf.RoundToInt(a.x * 10f), Mathf.RoundToInt(a.y * 10f), Mathf.RoundToInt(b.x * 10f), Mathf.RoundToInt(b.y * 10f), kit.Name,
            Key(arriving) * 64 + Key(leaving) + (relayPads ? 0 : 1 << 20) + ((Key(facing) + 1) << 22), Line());
        if (s_plans.TryGetValue(key, out StairPlan? cached)) return cached;
        // A stretch longer than any staircase laid is not laid out: none over 133 m was on the
        // layouts offline (208 m on RoadSeedD), and the 19 over 150 m that were tried (to 878 m, the road's
        // steps bought one by one along a whole slope) all failed, spending their allowances: 50 s of the
        // 83 s of failed layouts on that world.
        float span = Vector2.Distance(a, b);
        if (span > MaxStairSpan)
            return s_plans[key] = new StairPlan { Reason = FormattableString.Invariant($"{span:F0} m, longer than a staircase is laid ({MaxStairSpan:F0} m)") };
        // A layout another follows if it fails (retried: held to the road's headings, then free in the road's
        // cells, then free anywhere between the ends) is tried on a share of the allowance; the last gets all
        // of it. Failing held layouts spent the full allowance
        // first, the largest part of the slowest island's staircases (191 s in game).
        int share = retried ? HeldShare : 1;
        LastAllowance = GenerationTotalExpansions / share;
        StairPlan plan = StairLayout.Build(a, b, ground, Width, kit, GenerationExpansions / share, arriving, leaving, corridor, GenerationTotalExpansions / share,
            GenerationStall / share, topLandings, sunkFoot: true, maxBuried: maxBuried, footHeight: footHeight, after: after, relayPads: relayPads, facing: facing);
        return s_plans[key] = plan;
    }

    /// <summary>Metres from either end of a staircase where it may meet its road.</summary>
    public const float JunctionReach = 6f;
    /// <summary>How far from the end the piece that joins the road (and the sunk step under it) stands: only it may lie on
    /// a road's surface.</summary>
    public const float JoiningStep = 3.2f;

    /// <summary>Near a junction, a piece this close to the road's level there meets the road; higher, it is over it.</summary>
    public const float JunctionHeight = 2f;
    /// <summary>A walked piece's centre closer than this to the road's centreline lies on the road.</summary>
    public const float OnRoad = 3f;

    private static bool Usable(StairPlan plan, List<Vector2> path, int from, int to, WorldGenerator world, List<RoadStairSpan> before)
    {
        Refusal why = !plan.Reached ? Refusal.NotReached : !Clear(plan, world) ? Refusal.WaterOrPlace
            : OverRoad(plan, path, from, to, (x, z) => GroundAt(x, z, world)) ? Refusal.OnItsRoad : OverStairs(plan, before) ? Refusal.OnAnotherStaircase
            : BesideARoad(plan, path, from, to, (x, z) => GroundAt(x, z, world)) ? Refusal.BesideARoad : Refusal.None;
        if (why != Refusal.None) LastRefusal = why;
        return why == Refusal.None;
    }

    /// <summary>Settable for tests: false lets a staircase run beside a road at its height.</summary>
    internal static bool NoParallelStairs = true;
    /// <summary>A walked piece this close across to a road, and within <see cref="ParallelHeight"/> of its height, runs
    /// beside it; <see cref="ParallelPieces"/> in a row make a staircase redundant there. The owner's (<see cref="RoadBeside"/>).</summary>
    public const float ParallelReach = RoadBeside.Reach;
    public const float ParallelHeight = RoadBeside.Height;
    public const int ParallelPieces = RoadBeside.MostStairPieces;

    /// <summary>
    /// Whether a staircase over path[from..to] runs beside a road at that road's height for <see cref="ParallelPieces"/> or
    /// more walked pieces in a row, away from its own ends (at -5853,5918 a staircase ran parallel to a road, mostly flat pieces:
    /// better to connect further up the road, or avoid running parallel even if it costs POIs). Its own
    /// road outside the stretch (at the ground's height) and the network's roads count.
    /// </summary>
    internal static bool BesideARoad(StairPlan plan, List<Vector2> path, int from, int to, Func<float, float, float> ground)
    {
        if (!NoParallelStairs) return false;
        Vector2 a = path[from], b = path[to];
        int run = 0;
        foreach (StairPiece piece in plan.Pieces)
        {
            if (piece.Kind != StairPieceKind.Stair && piece.Kind != StairPieceKind.Landing) continue;
            var p = new Vector2(piece.Position.x, piece.Position.z);
            float top = piece.Position.y + (piece.Kind == StairPieceKind.Stair ? StairLayout.PieceRise : 0.5f);
            if (Vector2.Distance(p, a) <= ParallelReach || Vector2.Distance(p, b) <= ParallelReach) { run = 0; continue; }
            bool beside = false;
            for (int i = 1; i < path.Count && !beside; i++)
            {
                if (i > from && i <= to) continue;
                if (DistanceToSegment(p, path[i - 1], path[i]) >= ParallelReach) continue;
                Vector2 q = Vector2.Lerp(path[i - 1], path[i], 0.5f);
                beside = RoadBeside.AtItsHeight(ground(q.x, q.y), top);
            }
            if (!beside) beside = RoadBeside.NearAtItsHeight(p, top);
            run = beside ? run + 1 : 0;
            if (run >= ParallelPieces) return true;
        }
        return false;
    }

    /// <summary>Why a staircase's layout could not be laid (<see cref="Usable"/>).</summary>
    internal enum Refusal { None, NotReached, WaterOrPlace, OnItsRoad, OnAnotherStaircase, BesideARoad }

    /// <summary>The last layout refusal on this thread.</summary>
    [ThreadStatic] private static Refusal t_lastRefusal;
    internal static Refusal LastRefusal { get => t_lastRefusal; private set => t_lastRefusal = value; }

    /// <summary>The path indices of the stretch whose staircase the last failed <see cref="Lay"/>
    /// on this thread could not lay (a merge's whole stretch where it was merging).</summary>
    [ThreadStatic] internal static (int from, int to) LastFailedStretch;

    /// <summary>A road end this close to a walked piece of a laid staircase is on it.</summary>
    public const float OnStairReach = 4f;

    /// <summary>The walking surface at a walked piece's centre.</summary>
    private static float Surface(StructurePiece piece)
    {
        if (piece.Kind == StructurePieceKind.StairStep) return piece.Position.y + StairLayout.PieceRise / 2f;
        StairKit kit = StairKit.Named(piece.Prefab.Split('_')[0]) ?? StairKit.Stone;
        return piece.Position.y + kit.FloorTop;
    }

    /// <summary>Walked pieces closer than this across, and this close in height, are one inside the other.</summary>
    public const float StairOverlap = 1.8f;

    /// <summary>Whether a walked piece lies inside a walked piece of this road's
    /// earlier staircases or of one already in the network.</summary>
    private static bool OverStairs(StairPlan plan, List<RoadStairSpan> before)
    {
        var others = new List<Vector3>();
        foreach (RoadStairSpan span in before)
            foreach (StairPiece p in span.Plan.Pieces)
                if (p.Kind != StairPieceKind.Support) others.Add(p.Position);
        // The laid staircases near any of its pieces, read once: asked piece by piece, every
        // candidate layout on every island worker took the shared lock once a piece.
        var walked = new List<Vector2>();
        foreach (StairPiece piece in plan.Pieces)
            if (piece.Kind != StairPieceKind.Support) walked.Add(new Vector2(piece.Position.x, piece.Position.z));
        List<StructurePiece> laid = StairStructures.PiecesNearAny(walked, StairOverlap);
        foreach (StairPiece piece in plan.Pieces)
        {
            if (piece.Kind == StairPieceKind.Support) continue;
            var at = new Vector2(piece.Position.x, piece.Position.z);
            foreach (Vector3 o in others)
                if (Vector2.Distance(at, new Vector2(o.x, o.z)) < StairOverlap && Mathf.Abs(o.y - piece.Position.y) < 2f)
                    return true;
            foreach (StructurePiece o in laid)
                if (o.Kind != StructurePieceKind.StairSupport && Mathf.Abs(o.Position.y - piece.Position.y) < 2f
                    && Vector2.Distance(at, new Vector2(o.Position.x, o.Position.z)) < StairOverlap)
                    return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a walked piece lies on the road the staircase joins, away from
    /// the two junctions (a staircase must not go over the road it starts from).
    /// </summary>
    internal static bool OverRoad(StairPlan plan, List<Vector2> path, int from, int to, Func<float, float, float> ground)
    {
        Vector2 a = path[from], b = path[to];
        float ha = ground(a.x, a.y), hb = ground(b.x, b.y);
        foreach (StairPiece piece in plan.Pieces)
        {
            if (piece.Kind == StairPieceKind.Support) continue;
            var p = new Vector2(piece.Position.x, piece.Position.z);
            // Near a junction, only pieces at the road's level there meet it: a flight
            // that comes back over the junction metres up went straight over the road it
            // started from (cantilevers are not supported there, so avoid it).
            // Its top walking surface: a stair rises a step from its base.
            float top = piece.Position.y + (piece.Kind == StairPieceKind.Stair ? StairLayout.PieceRise : 0.5f);
            // Only the step that joins the road (and the sunk step under it) may stand on it: further up a foot that turned
            // across the road walled it off (stairs must not fully block the road).
            // At its foot (the low end) only; its top meets the road on its landing as before.
            float joinsA = !plan.Reversed ? JoiningStep : JunctionReach,
                joinsB = plan.Reversed ? JoiningStep : JunctionReach;
            // A foot that set off back (StairLayout.MostSetOffBack) runs beside the road, never on it: only its sunk step,
            // under the road's level, stands there (at 1948.1,691.7 a stair set off 180 degrees opposite the direction of road
            // travel blocked the actual road to the stair start).
            bool backA = plan.SetOffBack > 0 && !plan.Reversed, backB = plan.SetOffBack > 0 && plan.Reversed;
            if ((Vector2.Distance(p, a) <= joinsA && (backA ? top <= ha + SunkStepAbove : Mathf.Abs(top - ha) <= JunctionHeight))
                || (Vector2.Distance(p, b) <= joinsB && (backB ? top <= hb + SunkStepAbove : Mathf.Abs(top - hb) <= JunctionHeight))) continue;
            for (int i = 1; i < path.Count; i++)
            {
                if (i > from && i <= to) continue; // the stretch the staircase replaces
                if (DistanceToSegment(p, path[i - 1], path[i]) < OnRoad) return true;
            }
            // Nor on a road already in the network: checked against its own road
            // only, a staircase was laid across another (broken stairs, overlapping roads).
            if (RoadSpatialGrid.IsInitialized && RoadSpatialGrid.TryGetRoadWithin(p, OnRoad, out _)) return true;
        }
        return false;
    }

    /// <summary>No walked piece in water or a place's footprint, and no step between two of them through one;
    /// <paramref name="site"/>: the centre of a place whose own footprint it may cross.</summary>
    private static bool Clear(StairPlan plan, WorldGenerator world, Vector2? site = null) => Blocked(plan, world, site) == "";

    /// <summary>What a walked piece of the plan stands in, if anything: "water at (x, z)" or
    /// "another place at (x, z)" (the refusal says what it hit, and where).</summary>
    /// <param name="ground">The ground the plan is laid on, when known (a placed approach's real
    /// ground): water is judged by it, not by the world generator's height, which at a shore can be
    /// metres lower (TrollCave02 at -1793,6330: refused for "water" 12 m from the cave where
    /// the real ground stood at 31.7, above the sea).</param>
    internal static string Blocked(StairPlan plan, WorldGenerator world, Vector2? site = null, Func<float, float, float>? ground = null)
    {
        Vector2? last = null;
        foreach (StairPiece piece in plan.Pieces)
        {
            if (piece.Kind != StairPieceKind.Stair && piece.Kind != StairPieceKind.Landing) continue;
            var p = new Vector2(piece.Position.x, piece.Position.z);
            if ((ground != null ? ground(p.x, p.y) : world.GetHeight(p.x, p.y)) < RoadConstants.ShallowWaterHeight)
                return FormattableString.Invariant($"water at ({p.x:F1}, {p.y:F1})");
            if (RoadSiteProtection.BlocksSegment(last ?? p, p, 1f, null, site))
                return FormattableString.Invariant($"another place at ({p.x:F1}, {p.y:F1})");
            last = p;
        }
        return "";
    }
}
