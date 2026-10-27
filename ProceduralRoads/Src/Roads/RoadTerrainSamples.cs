using System;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Fixed-size terrain memo, held for the life of one pathfinder rather than
/// one search. Collisions replace a sample, never approximate it.
///
/// Only immutable world-generator facts belong here. Road sharing, crossing
/// decisions and configurable move costs do NOT: they change as roads are
/// committed, and a memo that outlives a search must not hold anything that
/// can go out of date within one.
///
/// The facts it does hold are true for as long as the generator it was built
/// for is the generator being asked, and a pathfinder is built at the start of
/// a generation and discarded at the end of it. Reset() remains the explicit
/// invalidation for a caller that wants to point one at a different world.
/// </summary>
internal sealed class RoadTerrainSamples
{
    // 1.75 MiB of value data per pathfinder, independent of explored area.
    // Chosen on the station, not from the shape of the cache: at 16,384 a
    // whole-world generation evicted 23.0 M slots and made 204.6 M generator
    // calls; here it evicts 8.3 M and makes 79.7 M, and the run falls from
    // 261-271 s to 227 s for 20 MB more peak working set across 15 workers.
    // 262,144 was measured too and is deliberately not taken: 214 s, only
    // 5.7% better and close to the 10 s spread between two runs of the same
    // build, for another 102 MB.
    internal const int Capacity = 65536;
    private struct Sample
    {
        public Vector2i Position;
        public byte Flags;
        public float Height, River, Variance;
        public Heightmap.Biome Biome;
    }
    private readonly Sample[] samples = new Sample[Capacity];
    private readonly WorldGenerator world;
    public RoadTerrainSamples(WorldGenerator world) { this.world = world; }
    /// <summary>Forget every fact, this memo's and the generation's shared ones.</summary>
    public void Reset()
    {
        Array.Clear(samples, 0, samples.Length);
        SharedTerrain.Clear();
    }

    // How the memo is actually behaving, which a wall-clock second cannot say.
    // A memo belongs to one pathfinder and a pathfinder to one thread, so
    // these are plain fields: one add, on a line the caller already owns, and
    // no interlocking in the hot path. Hits and misses are counted per FACT
    // asked for, not per slot touched; a replacement is an occupied slot
    // evicted by a different position, which is what says the memo is too
    // small for the way it is being asked.
    internal long Hits, Misses, Replacements, TerrainCalls;
    /// <summary>Stopwatch ticks spent inside the world generator for this memo: the
    /// share of a search that is the game's terrain (and its river cache's lock),
    /// which in game cannot be told from the search's own work any other way.
    /// Counted only with PROCEDURALROADS_TIMINGS (<see cref="SearchStats.Enabled"/>).</summary>
    internal long TerrainTicks;
    private static long Now => SearchStats.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

    // Whole-generation totals. A memo is folded in when its owner is done,
    // so a pathfinder discarded mid-generation does not take its numbers with
    // it. Interlocked because several island workers fold at once.
    internal static long TotalHits, TotalMisses, TotalReplacements, TotalTerrainCalls, TotalTerrainTicks;

    internal static void ResetTotals()
    {
        SharedTerrain.Clear();
        System.Threading.Interlocked.Exchange(ref TotalHits, 0);
        System.Threading.Interlocked.Exchange(ref TotalMisses, 0);
        System.Threading.Interlocked.Exchange(ref TotalReplacements, 0);
        System.Threading.Interlocked.Exchange(ref TotalTerrainCalls, 0);
        System.Threading.Interlocked.Exchange(ref TotalTerrainTicks, 0);
    }

    internal void FoldIntoTotals()
    {
        System.Threading.Interlocked.Add(ref TotalHits, Hits);
        System.Threading.Interlocked.Add(ref TotalMisses, Misses);
        System.Threading.Interlocked.Add(ref TotalReplacements, Replacements);
        System.Threading.Interlocked.Add(ref TotalTerrainCalls, TerrainCalls);
        System.Threading.Interlocked.Add(ref TotalTerrainTicks, TerrainTicks);
        Hits = Misses = Replacements = TerrainCalls = TerrainTicks = 0;
    }

    private ref Sample At(Vector2i position)
    {
        int slot = unchecked((position.x * 73856093) ^ (position.y * 83492791)) & (Capacity - 1);
        ref Sample sample = ref samples[slot];
        if (sample.Position != position)
        {
            if (sample.Flags != 0) Replacements++;
            sample = default;
            sample.Position = position;
        }
        return ref sample;
    }
    private static Vector2 World(Vector2i p) => new(p.x * RoadPathfinder.CellSize, p.y * RoadPathfinder.CellSize);

    public float Height(Vector2i position)
    {
        ref Sample sample = ref At(position);
        if ((sample.Flags & 1) == 0)
        {
            Misses++;
            if (!SharedTerrain.TryHeight(world, position, out sample.Height))
            {
                TerrainCalls++;
                var p = World(position);
                long began = Now;
                sample.Height = world.GetHeight(p.x, p.y);
                TerrainTicks += Now - began;
                SharedTerrain.PutHeight(world, position, sample.Height);
            }
            sample.Flags |= 1;
        }
        else Hits++;
        return sample.Height;
    }
    public float River(Vector2i position)
    {
        ref Sample sample = ref At(position);
        if ((sample.Flags & 2) == 0)
        {
            Misses++;
            if (!SharedTerrain.TryRiver(world, position, out sample.River))
            {
                TerrainCalls++;
                var p = World(position);
                long began = Now;
                world.GetRiverWeight(p.x, p.y, out sample.River, out _);
                TerrainTicks += Now - began;
                SharedTerrain.PutRiver(world, position, sample.River);
            }
            sample.Flags |= 2;
        }
        else Hits++;
        return sample.River;
    }
    public Heightmap.Biome Biome(Vector2i position)
    {
        ref Sample sample = ref At(position);
        if ((sample.Flags & 4) == 0)
        {
            Misses++;
            if (!SharedTerrain.TryBiome(world, position, out sample.Biome))
            {
                TerrainCalls++;
                var p = World(position);
                long began = Now;
                sample.Biome = world.GetBiome(p.x, p.y);
                TerrainTicks += Now - began;
                SharedTerrain.PutBiome(world, position, sample.Biome);
            }
            sample.Flags |= 4;
        }
        else Hits++;
        return sample.Biome;
    }
    /// <summary>
    /// Measure roughness about the best QUADRATIC surface through the ring and
    /// the centre, so slope and curvature both read as smooth and only broken
    /// ground reads rough. The raw height spread on a 16 m ring is ~32 x slope,
    /// so every smooth sidehill past ~0.16 read as rough and paid the variance
    /// penalty on every step, even one running level along the contour; a
    /// plane fit fixes slopes but not troughs, since a ring on a valley floor
    /// crosses both walls. Roads avoided valley floors, the natural way up a
    /// mountain. Off keeps the raw spread. Settable for tests.
    /// </summary>
    internal static bool DefaultQuadVariance = true;
    internal bool QuadVariance = DefaultQuadVariance;

    /// <summary>
    /// Spread of the residuals left by the least-squares fit of
    /// h = a + bx + cy + dx^2 + exy + fy^2 to the centre and the ring samples
    /// (nine points, six unknowns). A smooth trough, ridge, bowl or slope fits
    /// exactly; a step, spike or broken ground does not.
    /// </summary>
    internal static float QuadraticRoughness(float centre, float[] ring, float radius)
    {
        int n = ring.Length + 1;
        var rows = new double[n][];
        var h = new double[n];
        rows[0] = new double[] { 1, 0, 0, 0, 0, 0 }; h[0] = centre;
        for (int i = 0; i < ring.Length; i++)
        {
            double angle = i * Math.PI * 2.0 / ring.Length;
            double x = Math.Cos(angle), y = Math.Sin(angle);   // in units of the radius
            rows[i + 1] = new double[] { 1, x, y, x * x, x * y, y * y };
            h[i + 1] = ring[i];
        }
        // Normal equations A^T A c = A^T h, solved by Gaussian elimination.
        var m = new double[6, 7];
        for (int r = 0; r < n; r++)
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 6; j++) m[i, j] += rows[r][i] * rows[r][j];
                m[i, 6] += rows[r][i] * h[r];
            }
        for (int col = 0; col < 6; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 6; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-9) continue;   // x^2 and y^2 are dependent with the offset on a ring alone; the centre breaks that
            for (int j = 0; j < 7; j++) { double t = m[col, j]; m[col, j] = m[pivot, j]; m[pivot, j] = t; }
            for (int r = 0; r < 6; r++)
            {
                if (r == col) continue;
                double f = m[r, col] / m[col, col];
                for (int j = col; j < 7; j++) m[r, j] -= f * m[col, j];
            }
        }
        var c = new double[6];
        for (int i = 0; i < 6; i++) c[i] = Math.Abs(m[i, i]) < 1e-9 ? 0 : m[i, 6] / m[i, i];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int r = 0; r < n; r++)
        {
            double fit = 0; for (int i = 0; i < 6; i++) fit += c[i] * rows[r][i];
            double res = h[r] - fit;
            lo = Math.Min(lo, res); hi = Math.Max(hi, res);
        }
        return (float)(hi - lo);
    }

    public float Variance(Vector2i position)
    {
        float centerHeight = Height(position);
        ref Sample sample = ref At(position);
        // Shared only as the default roughness measure computes it.
        bool share = QuadVariance == DefaultQuadVariance;
        if ((sample.Flags & 8) == 0 && share && SharedTerrain.TryVariance(world, position, out float shared))
        {
            Misses++;
            sample.Variance = shared;
            sample.Flags |= 8;
        }
        else if ((sample.Flags & 8) == 0)
        {
            Misses++; TerrainCalls += RoadConstants.TerrainVarianceSampleCount;
            var p = World(position);
            int n = RoadConstants.TerrainVarianceSampleCount;
            float radius = RoadConstants.TerrainVarianceSampleRadius;
            float min = centerHeight, max = centerHeight;
            float[] ring = new float[n];
            long began = Now;
            for (int i = 0; i < n; i++)
            {
                float angle = i * Mathf.PI * 2f / n;
                float h = world.GetHeight(p.x + Mathf.Cos(angle) * radius, p.y + Mathf.Sin(angle) * radius);
                ring[i] = h;
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            TerrainTicks += Now - began;
            if (QuadVariance)
                sample.Variance = QuadraticRoughness(centerHeight, ring, radius);
            else
                sample.Variance = max - min;
            sample.Flags |= 8;
            if (share) SharedTerrain.PutVariance(world, position, sample.Variance);
        }
        else Hits++;
        return sample.Variance;
    }
}

/// <summary>
/// The terrain facts every pathfinder of a generation shares: one lazily filled
/// grid of the 8 m search cells, in 64-cell tiles, behind each thread's own
/// memo. Each thread kept its own 65,536-slot memo, so up to fifteen workers
/// pricing and building over the same mountains each read the same terrain
/// from the world generator, and evicted and read it again; in game, where a
/// read is the generator's noise, route searches took about 2.6 s each against
/// 0.15 s offline, and a world's first load with stairs took 15 minutes.
/// A value is written before the bit that says it is
/// there, so a reader sees a whole value or none and reads the world itself;
/// two threads that do so write the same value.
/// </summary>
internal static class SharedTerrain
{
    private const int TileCells = 64;
    private sealed class Tile
    {
        public readonly float[] Height = new float[TileCells * TileCells];
        public readonly float[] River = new float[TileCells * TileCells];
        public readonly float[] Variance = new float[TileCells * TileCells];
        public readonly int[] Biome = new int[TileCells * TileCells];
        public readonly int[] Flags = new int[TileCells * TileCells];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Tile> s_tiles = new();
    private static WorldGenerator? s_world;
    private static readonly object s_gate = new();

    /// <summary>Forget everything: a new generation, or another world.</summary>
    internal static void Clear()
    {
        lock (s_gate)
        {
            s_tiles.Clear();
            s_world = null;
        }
    }

    private static Tile? Find(WorldGenerator world, Vector2i cell, bool create, out int index)
    {
        int tx = cell.x >= 0 ? cell.x / TileCells : (cell.x + 1) / TileCells - 1;
        int tz = cell.y >= 0 ? cell.y / TileCells : (cell.y + 1) / TileCells - 1;
        index = (cell.y - tz * TileCells) * TileCells + (cell.x - tx * TileCells);
        if (!ReferenceEquals(s_world, world))
        {
            lock (s_gate)
            {
                if (!ReferenceEquals(s_world, world)) { s_tiles.Clear(); s_world = world; }
            }
        }
        long key = ((long)tx << 32) ^ (uint)tz;
        if (s_tiles.TryGetValue(key, out Tile? tile)) return tile;
        return create ? s_tiles.GetOrAdd(key, _ => new Tile()) : null;
    }

    private static bool Has(Tile? tile, int index, int bit) =>
        tile != null && (System.Threading.Volatile.Read(ref tile.Flags[index]) & bit) != 0;

    private static void Mark(Tile tile, int index, int bit)
    {
        int seen = System.Threading.Volatile.Read(ref tile.Flags[index]);
        while ((seen & bit) == 0)
        {
            int was = System.Threading.Interlocked.CompareExchange(ref tile.Flags[index], seen | bit, seen);
            if (was == seen) return;
            seen = was;
        }
    }

    // Each Try reads the fact's bit ONCE. Read twice (once for the value, once for the answer),
    // another worker's Put between the two made it answer true with the empty value: a height
    // of 0 (deep sea), no river or the default biome, kept by that thread's memo for the rest of
    // the generation.
    internal static bool TryHeight(WorldGenerator world, Vector2i cell, out float value)
    {
        Tile? tile = Find(world, cell, false, out int i);
        bool has = Has(tile, i, 1);
        value = has ? tile!.Height[i] : 0f;
        return has;
    }
    internal static void PutHeight(WorldGenerator world, Vector2i cell, float value)
    {
        Tile tile = Find(world, cell, true, out int i)!;
        System.Threading.Volatile.Write(ref tile.Height[i], value);
        Mark(tile, i, 1);
    }
    internal static bool TryRiver(WorldGenerator world, Vector2i cell, out float value)
    {
        Tile? tile = Find(world, cell, false, out int i);
        bool has = Has(tile, i, 2);
        value = has ? tile!.River[i] : 0f;
        return has;
    }
    internal static void PutRiver(WorldGenerator world, Vector2i cell, float value)
    {
        Tile tile = Find(world, cell, true, out int i)!;
        System.Threading.Volatile.Write(ref tile.River[i], value);
        Mark(tile, i, 2);
    }
    internal static bool TryBiome(WorldGenerator world, Vector2i cell, out Heightmap.Biome value)
    {
        Tile? tile = Find(world, cell, false, out int i);
        bool has = Has(tile, i, 4);
        value = has ? (Heightmap.Biome)tile!.Biome[i] : default;
        return has;
    }
    internal static void PutBiome(WorldGenerator world, Vector2i cell, Heightmap.Biome value)
    {
        Tile tile = Find(world, cell, true, out int i)!;
        System.Threading.Volatile.Write(ref tile.Biome[i], (int)value);
        Mark(tile, i, 4);
    }
    internal static bool TryVariance(WorldGenerator world, Vector2i cell, out float value)
    {
        Tile? tile = Find(world, cell, false, out int i);
        bool has = Has(tile, i, 8);
        value = has ? tile!.Variance[i] : 0f;
        return has;
    }
    internal static void PutVariance(WorldGenerator world, Vector2i cell, float value)
    {
        Tile tile = Find(world, cell, true, out int i)!;
        System.Threading.Volatile.Write(ref tile.Variance[i], value);
        Mark(tile, i, 8);
    }
}
