using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ProceduralRoads.Tests;

/// <summary>
/// A world read back from a dump of the real one, so road generation can run
/// offline on real terrain.
///
/// What it reproduces exactly, and what it does not, decides what a run on it
/// may claim:
///
/// * At a sample position it returns the sampled value, unchanged. A dump
///   aligned to the 8 m pathfinding grid is therefore exact for every move
///   cost, and a dump on the 128 m base-height grid is exact for island
///   detection.
/// * Off a sample position it interpolates bilinearly between the four
///   surrounding samples. The pathfinder's terrain-variance ring and every
///   crossing-depth judgement sample off-grid, so those are APPROXIMATE.
/// * Asked for a biome's height field at a point in another biome - what
///   biome blending needs - it can only answer with the height that was
///   dumped. Blending across a biome edge is therefore APPROXIMATE.
/// * Outside the dumped area it throws. A world model that guesses beyond its
///   data would turn a missing dump into a quiet wrong answer.
///
/// Finer tiles win: load the 128 m map of a world and an 8 m window over one
/// island, and queries inside the window are answered from it.
/// </summary>
public sealed class CsvWorld : WorldGenerator
{
    /// <summary>One dumped grid: evenly spaced samples over a rectangle.</summary>
    private sealed class Layer
    {
        public string Source = "";
        public float Step;
        public float X0, Z0;
        public int Nx, Nz;
        public float[] Height = Array.Empty<float>();
        public float[] River = Array.Empty<float>();
        public float[] RiverWidth = Array.Empty<float>();
        public float[] BaseHeight = Array.Empty<float>();
        public Heightmap.Biome[] Biome = Array.Empty<Heightmap.Biome>();
        public bool HasBaseHeight;
        public bool HasRiverWidth;

        public float X1 => X0 + (Nx - 1) * Step;
        public float Z1 => Z0 + (Nz - 1) * Step;

        public bool Contains(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;

        public int Index(int ix, int iz) => iz * Nx + ix;
    }

    private readonly List<Layer> m_layers = new();
    private readonly int m_seed;

    public CsvWorld(int seed = 0) => m_seed = seed;

    /// <summary>What this world cannot reproduce from its dumps. Belongs in the
    /// manifest of any run that uses it.</summary>
    public IReadOnlyList<string> Approximations
    {
        get
        {
            List<string> notes = new()
            {
                "off-grid queries are bilinear between samples (terrain variance ring, crossing depth)",
                "biome blending uses the dumped height for every biome",
            };
            foreach (Layer layer in m_layers)
            {
                // File name only: these notes are copied into run manifests
                // and study documents, which carry no local paths.
                string name = Path.GetFileName(layer.Source);
                if (!layer.HasRiverWidth)
                    notes.Add($"{name}: no river_width column, width reported as 0");
                if (!layer.HasBaseHeight)
                    notes.Add($"{name}: no base_height column, island detection unavailable");
            }
            return notes;
        }
    }

    public string Describe()
    {
        List<string> parts = new();
        foreach (Layer layer in m_layers)
            parts.Add($"{Path.GetFileName(layer.Source)} step {layer.Step:F0} m, " +
                      $"{layer.Nx}x{layer.Nz} samples, [{layer.X0:F0},{layer.Z0:F0}..{layer.X1:F0},{layer.Z1:F0}]");
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Adds a dump. Columns: x, z, height, biome, river, and optionally
    /// base_height and river_width. Samples must lie on one even grid.
    /// </summary>
    public CsvWorld Load(string path)
    {
        using StreamReader reader = new(path);
        string? header = reader.ReadLine()
            ?? throw new InvalidDataException($"{path}: empty dump");
        string[] columns = header.Split(',');

        int Column(string name)
        {
            for (int i = 0; i < columns.Length; i++)
                if (columns[i].Trim() == name) return i;
            return -1;
        }

        int cx = Column("x"), cz = Column("z"), ch = Column("height");
        int cb = Column("biome"), cr = Column("river");
        int cbh = Column("base_height"), crw = Column("river_width");
        if (cx < 0 || cz < 0 || ch < 0 || cb < 0 || cr < 0)
            throw new InvalidDataException($"{path}: needs at least x,z,height,biome,river columns, has '{header}'");

        SortedSet<float> xs = new(), zs = new();
        Dictionary<(float, float), (float h, Heightmap.Biome b, float r, float rw, float bh)> samples = new();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            float x = Parse(cells[cx]), z = Parse(cells[cz]);
            xs.Add(x);
            zs.Add(z);
            samples[(x, z)] = (
                Parse(cells[ch]),
                ParseBiome(cells[cb], path),
                Parse(cells[cr]),
                crw >= 0 ? Parse(cells[crw]) : 0f,
                cbh >= 0 ? Parse(cells[cbh]) : 0f);
        }

        if (xs.Count < 2 || zs.Count < 2)
            throw new InvalidDataException($"{path}: a grid needs at least two rows and two columns");

        Layer layer = new()
        {
            Source = path,
            Nx = xs.Count,
            Nz = zs.Count,
            HasBaseHeight = cbh >= 0,
            HasRiverWidth = crw >= 0,
        };

        float[] xv = new float[xs.Count], zv = new float[zs.Count];
        xs.CopyTo(xv);
        zs.CopyTo(zv);
        layer.X0 = xv[0];
        layer.Z0 = zv[0];
        layer.Step = xv[1] - xv[0];

        EvenlySpaced(xv, layer.Step, path, "x");
        EvenlySpaced(zv, layer.Step, path, "z");

        int n = layer.Nx * layer.Nz;
        layer.Height = new float[n];
        layer.River = new float[n];
        layer.RiverWidth = new float[n];
        layer.BaseHeight = new float[n];
        layer.Biome = new Heightmap.Biome[n];

        for (int iz = 0; iz < layer.Nz; iz++)
        {
            for (int ix = 0; ix < layer.Nx; ix++)
            {
                if (!samples.TryGetValue((xv[ix], zv[iz]), out var sample))
                    throw new InvalidDataException($"{path}: no sample at ({xv[ix]},{zv[iz]}); the dump is not a full grid");
                int i = layer.Index(ix, iz);
                layer.Height[i] = sample.h;
                layer.Biome[i] = sample.b;
                layer.River[i] = sample.r;
                layer.RiverWidth[i] = sample.rw;
                layer.BaseHeight[i] = sample.bh;
            }
        }

        m_layers.Add(layer);
        // Finest first: a window over an island answers before the world map.
        m_layers.Sort((a, b) => a.Step.CompareTo(b.Step));
        return this;
    }

    private static void EvenlySpaced(float[] values, float step, string path, string axis)
    {
        for (int i = 1; i < values.Length; i++)
        {
            float delta = values[i] - values[i - 1];
            if (Mathf.Abs(delta - step) > step * 0.001f)
                throw new InvalidDataException(
                    $"{path}: {axis} samples are not evenly spaced ({values[i - 1]} to {values[i]} is {delta}, expected {step})");
        }
    }

    private static float Parse(string cell) =>
        float.Parse(cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Heightmap.Biome ParseBiome(string cell, string path)
    {
        string name = cell.Trim();
        if (Enum.TryParse(name, out Heightmap.Biome biome))
            return biome;
        throw new InvalidDataException($"{path}: unknown biome '{name}'");
    }

    private Layer LayerAt(float x, float z)
    {
        foreach (Layer layer in m_layers)
            if (layer.Contains(x, z))
                return layer;
        throw new InvalidOperationException(
            $"({x:F1},{z:F1}) is outside every dump loaded ({Describe()}). " +
            "A world read from a dump does not guess beyond its data.");
    }

    /// <summary>Whether a point can be answered at all.</summary>
    public bool Covers(float x, float z)
    {
        foreach (Layer layer in m_layers)
            if (layer.Contains(x, z))
                return true;
        return false;
    }

    /// <summary>
    /// Bilinear between the four surrounding samples; exactly the sample when
    /// the point is one.
    /// </summary>
    private float Sample(Layer layer, float[] values, float x, float z)
    {
        float fx = (x - layer.X0) / layer.Step;
        float fz = (z - layer.Z0) / layer.Step;
        int ix = Mathf.Clamp((int)Mathf.Floor(fx), 0, layer.Nx - 1);
        int iz = Mathf.Clamp((int)Mathf.Floor(fz), 0, layer.Nz - 1);
        int ix1 = Mathf.Min(ix + 1, layer.Nx - 1);
        int iz1 = Mathf.Min(iz + 1, layer.Nz - 1);
        float tx = fx - ix, tz = fz - iz;

        float v00 = values[layer.Index(ix, iz)];
        float v10 = values[layer.Index(ix1, iz)];
        float v01 = values[layer.Index(ix, iz1)];
        float v11 = values[layer.Index(ix1, iz1)];
        return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), tz);
    }

    public override float GetHeight(float wx, float wy)
    {
        Layer layer = LayerAt(wx, wy);
        return Sample(layer, layer.Height, wx, wy);
    }

    /// <summary>The biome of the nearest sample: a biome is a label, not a
    /// number, and averaging two of them would invent a third.</summary>
    public override Heightmap.Biome GetBiome(float wx, float wy)
    {
        Layer layer = LayerAt(wx, wy);
        int ix = Mathf.Clamp(Mathf.RoundToInt((wx - layer.X0) / layer.Step), 0, layer.Nx - 1);
        int iz = Mathf.Clamp(Mathf.RoundToInt((wy - layer.Z0) / layer.Step), 0, layer.Nz - 1);
        return layer.Biome[layer.Index(ix, iz)];
    }

    public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
    {
        Layer layer = LayerAt(wx, wy);
        weight = Sample(layer, layer.River, wx, wy);
        width = layer.HasRiverWidth ? Sample(layer, layer.RiverWidth, wx, wy) : 0f;
    }

    public override float GetBaseHeight(float wx, float wy, bool menuTerrain)
    {
        Layer layer = LayerAt(wx, wy);
        if (!layer.HasBaseHeight)
            throw new InvalidOperationException(
                $"{Path.GetFileName(layer.Source)} has no base_height column, so island detection cannot run on it. " +
                "Dump the 128 m base grid with the column and load it.");
        return Sample(layer, layer.BaseHeight, wx, wy);
    }

    /// <summary>
    /// The dump holds the height the world actually had at each point, not one
    /// height field per biome, so a biome's own field is answered with that.
    /// Blending across a biome edge is approximate here.
    /// </summary>
    public override float GetBiomeHeight(Heightmap.Biome biome, float wx, float wy, out Color mask)
    {
        mask = default;
        return GetHeight(wx, wy);
    }

    public override int GetSeed() => m_seed;
}
