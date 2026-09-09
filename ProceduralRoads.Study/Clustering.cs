using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralRoads.Tests;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// Whether roads cluster at an edge, and which edge.
///
/// The issue describes two different things as "the edge": the shoreline of an
/// island, and the outer parts of the world. They are measured apart here -
/// distance to the nearest water, and distance from the world's centre - for
/// every road point and every place.
///
/// The measurement that matters is not where roads are but whether they are
/// anywhere unusual. Most land in these worlds is near a shore, so roads near
/// shores prove nothing on their own. Every distribution below is therefore
/// reported against the land itself: what the same measurement gives for
/// ground chosen uniformly over the world's land.
/// </summary>
internal static class Clustering
{
    public static int Run(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine(
                "usage: roads-study clustering <island-grid.csv> <terrain.csv> <routes.csv> <outcomes.csv> [--out FILE]");
            return 2;
        }

        CsvWorld world = new CsvWorld().LoadIslandGrid(args[1]);
        world.Load(args[2]);
        WorldGenerator.instance = world;

        Console.WriteLine("building the distance-to-open-water map (a walkable swamp counts as land)...");
        ShoreDistance shore = ShoreDistance.Build(args[2]);
        Console.WriteLine($"land: {shore.LandCells} cells of {shore.Total} ({100f * shore.LandCells / shore.Total:F1}%), " +
                          $"furthest from any shore {shore.MaxDistance:F0} m");

        List<Compare.Route> routes = Compare.ReadRoutes(args[3]);
        List<Vector2> roadPoints = routes.SelectMany(r => r.Points).ToList();

        List<float> roadShore = roadPoints.Select(p => shore.At(p.x, p.y)).Where(d => d >= 0f).ToList();
        List<float> roadCentre = roadPoints.Select(p => p.magnitude).ToList();
        (List<float> landShore, List<float> landCentre) = shore.LandSample();

        Console.WriteLine();
        Console.WriteLine("distance to the nearest open water (metres)");
        Report("road points", roadShore);
        Report("land itself", landShore);

        Console.WriteLine();
        Console.WriteLine("distance from the centre of the world (metres)");
        Report("road points", roadCentre);
        Report("land itself", landCentre);

        // Roads hug shorelines. Before calling that a routing bias, ask which
        // ground they are on: swamp lies in coastal lowland and the pathfinder
        // wades it cheaply, so a road that prefers swamp will look like a road
        // that prefers coasts without any coastal preference at all.
        Console.WriteLine();
        Console.WriteLine("distance to the nearest open water, by the ground under it (metres)");
        Dictionary<string, List<float>> roadByBiome = new();
        foreach (Vector2 point in roadPoints)
        {
            if (!world.Covers(point.x, point.y))
                continue;
            float distance = shore.At(point.x, point.y);
            if (distance < 0f)
                continue;
            string biome = world.GetBiome(point.x, point.y).ToString();
            if (!roadByBiome.TryGetValue(biome, out List<float>? list))
                roadByBiome[biome] = list = new List<float>();
            list.Add(distance);
        }

        Dictionary<string, List<float>> landByBiome = shore.LandSampleByBiome(world);
        foreach (KeyValuePair<string, List<float>> entry in roadByBiome.OrderByDescending(e => e.Value.Count))
        {
            if (entry.Value.Count < 200)
                continue;
            Report($"road in {entry.Key}", entry.Value);
            if (landByBiome.TryGetValue(entry.Key, out List<float>? land) && land.Count >= 50)
                Report($"  {entry.Key} itself", land);
        }

        // Where the roads are, against where the land is. This is the bias that
        // reads at a glance on a map: a biome carrying far more road than its
        // share of the ground.
        Console.WriteLine();
        Console.WriteLine("share of road, against share of land");
        int roadTotal = roadByBiome.Values.Sum(v => v.Count);
        int landTotal = landByBiome.Values.Sum(v => v.Count);
        foreach (KeyValuePair<string, List<float>> entry in roadByBiome.OrderByDescending(e => e.Value.Count))
        {
            landByBiome.TryGetValue(entry.Key, out List<float>? land);
            float roadShare = 100f * entry.Value.Count / roadTotal;
            float landShare = land != null ? 100f * land.Count / landTotal : 0f;
            string ratio = landShare > 0.05f ? $"{roadShare / landShare,5:F1}x" : "    -";
            Console.WriteLine($"   {entry.Key,-12} road {roadShare,5:F1}%   land {landShare,5:F1}%   {ratio}");
        }

        // Places, split by whether a road reached them: the same two measures,
        // so "roads go to the coast" can be told from "the places are coastal".
        List<Place> places = ReadOutcomes(args[4]);
        List<Place> eligible = places.Where(p => p.Eligible).ToList();
        List<Place> connected = eligible.Where(p => p.Connected).ToList();
        List<Place> unconnected = eligible.Where(p => !p.Connected).ToList();

        Console.WriteLine();
        Console.WriteLine("eligible places, distance to the nearest open water (metres)");
        Report("connected", connected.Select(p => shore.At(p.X, p.Z)).Where(d => d >= 0f).ToList());
        Report("not connected", unconnected.Select(p => shore.At(p.X, p.Z)).Where(d => d >= 0f).ToList());

        Console.WriteLine();
        Console.WriteLine("eligible places, distance from the centre of the world (metres)");
        Report("connected", connected.Select(p => new Vector2(p.X, p.Z).magnitude).ToList());
        Report("not connected", unconnected.Select(p => new Vector2(p.X, p.Z).magnitude).ToList());

        Console.WriteLine();
        Console.WriteLine("connected share by ring of the world");
        foreach (int ring in new[] { 0, 1, 2 })
        {
            List<Place> inRing = eligible.Where(p => Ring(p) == ring).ToList();
            if (inRing.Count == 0)
                continue;
            Console.WriteLine($"   ring {ring} ({(ring == 0 ? "inner" : ring == 1 ? "middle" : "outer")}): " +
                              $"{inRing.Count(p => p.Connected)} of {inRing.Count} " +
                              $"({100f * inRing.Count(p => p.Connected) / inRing.Count:F1}%)");
        }

        Console.WriteLine();
        Console.WriteLine("connected share by biome of the place");
        foreach (IGrouping<string, Place> group in eligible.GroupBy(p => Biome(world, p))
                     .OrderByDescending(g => g.Count()))
        {
            if (group.Count() < 20)
                continue;
            Console.WriteLine($"   {group.Key,-12} {group.Count(p => p.Connected),4} of {group.Count(),5} " +
                              $"({100f * group.Count(p => p.Connected) / group.Count():F1}%)");
        }

        string? outPath = Options.Value(args, "--out");
        if (outPath != null)
        {
            using StreamWriter writer = new(outPath);
            writer.WriteLine("what,count,p10,median,p90,mean");
            Write(writer, "road_points_shore", roadShore);
            Write(writer, "land_shore", landShore);
            Write(writer, "road_points_centre", roadCentre);
            Write(writer, "land_centre", landCentre);
            Console.WriteLine($"\nwrote: {outPath}");
        }

        return 0;
    }

    private static int Ring(Place place)
    {
        float normalized = Mathf.Clamp01(new Vector2(place.X, place.Z).magnitude / 10000f);
        return normalized < 0.33f ? 0 : normalized < 0.66f ? 1 : 2;
    }

    private static string Biome(CsvWorld world, Place place) =>
        world.Covers(place.X, place.Z) ? world.GetBiome(place.X, place.Z).ToString() : "unknown";

    private static void Report(string label, List<float> values)
    {
        if (values.Count == 0)
        {
            Console.WriteLine($"   {label,-14} no samples");
            return;
        }

        values.Sort();
        Console.WriteLine($"   {label,-14} n={values.Count,7}  " +
                          $"p10 {values[(int)(values.Count * 0.1f)],6:F0}  " +
                          $"median {values[values.Count / 2],6:F0}  " +
                          $"p90 {values[(int)(values.Count * 0.9f)],6:F0}  " +
                          $"mean {values.Average(),6:F0}");
    }

    private static void Write(StreamWriter writer, string what, List<float> values)
    {
        if (values.Count == 0)
            return;
        values.Sort();
        writer.WriteLine(string.Join(",", what, values.Count,
            values[(int)(values.Count * 0.1f)].ToString("F1", CultureInfo.InvariantCulture),
            values[values.Count / 2].ToString("F1", CultureInfo.InvariantCulture),
            values[(int)(values.Count * 0.9f)].ToString("F1", CultureInfo.InvariantCulture),
            values.Average().ToString("F1", CultureInfo.InvariantCulture)));
    }

    internal sealed class Place
    {
        public float X, Z;
        public bool Eligible, Connected;
    }

    private static List<Place> ReadOutcomes(string path)
    {
        List<Place> places = new();
        foreach (string[] cells in Csv.Rows(path, out string[] columns))
        {
            places.Add(new Place
            {
                X = Csv.Number(cells[Array.IndexOf(columns, "x")]),
                Z = Csv.Number(cells[Array.IndexOf(columns, "z")]),
                Eligible = cells[Array.IndexOf(columns, "eligible")].Trim() == "true",
                Connected = cells[Array.IndexOf(columns, "connected")].Trim() == "true",
            });
        }

        return places;
    }
}

/// <summary>
/// How far every cell of the world is from the nearest open water, by a
/// two-pass chamfer over the dumped grid. Exact enough at 8 m spacing: the
/// diagonal step is weighted, so the answer is within a few per cent of a
/// straight-line distance and costs two sweeps instead of a search per cell.
///
/// What counts as water decides the whole measurement. The first version of
/// this called everything below the road floor water, which put all of a swamp
/// in the sea: swamp sits below that line by nature, so every swamp road came
/// out "eight metres from the shore" and the study nearly reported roads
/// hugging coasts when what they were doing was crossing swamps. Water here is
/// open water: below sea level and not swamp, or swamp deeper than a road may
/// wade. A swamp a player can walk is land.
/// </summary>
internal sealed class ShoreDistance
{
    private const float Straight = 1f;
    private const float Diagonal = 1.41421356f;

    private float[] m_distance = Array.Empty<float>();
    private bool[] m_land = Array.Empty<bool>();
    private float m_x0, m_z0, m_step;
    private int m_nx, m_nz;

    public int LandCells { get; private set; }
    public int Total => m_nx * m_nz;
    public float MaxDistance { get; private set; }

    public static ShoreDistance Build(string terrainPath)
    {
        ShoreDistance shore = new();
        (float x0, float z0, float step, int nx, int nz, bool[] land) = ReadLand(terrainPath);
        shore.m_x0 = x0;
        shore.m_z0 = z0;
        shore.m_step = step;
        shore.m_nx = nx;
        shore.m_nz = nz;
        shore.m_land = land;
        shore.LandCells = land.Count(l => l);

        float[] distance = new float[nx * nz];
        for (int i = 0; i < distance.Length; i++)
            distance[i] = land[i] ? float.MaxValue / 4f : 0f;

        // Forward sweep, then backward: each cell takes the cheapest way to any
        // water cell it can reach through its neighbours.
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                if (distance[i] == 0f) continue;
                if (x > 0) distance[i] = Mathf.Min(distance[i], distance[i - 1] + Straight);
                if (z > 0) distance[i] = Mathf.Min(distance[i], distance[i - nx] + Straight);
                if (x > 0 && z > 0) distance[i] = Mathf.Min(distance[i], distance[i - nx - 1] + Diagonal);
                if (x < nx - 1 && z > 0) distance[i] = Mathf.Min(distance[i], distance[i - nx + 1] + Diagonal);
            }
        }

        for (int z = nz - 1; z >= 0; z--)
        {
            for (int x = nx - 1; x >= 0; x--)
            {
                int i = z * nx + x;
                if (distance[i] == 0f) continue;
                if (x < nx - 1) distance[i] = Mathf.Min(distance[i], distance[i + 1] + Straight);
                if (z < nz - 1) distance[i] = Mathf.Min(distance[i], distance[i + nx] + Straight);
                if (x < nx - 1 && z < nz - 1) distance[i] = Mathf.Min(distance[i], distance[i + nx + 1] + Diagonal);
                if (x > 0 && z < nz - 1) distance[i] = Mathf.Min(distance[i], distance[i + nx - 1] + Diagonal);
            }
        }

        for (int i = 0; i < distance.Length; i++)
        {
            distance[i] *= step;
            if (land[i] && distance[i] > shore.MaxDistance)
                shore.MaxDistance = distance[i];
        }

        shore.m_distance = distance;
        return shore;
    }

    /// <summary>Distance to the nearest water, or -1 outside the dump.</summary>
    public float At(float x, float z)
    {
        int ix = Mathf.RoundToInt((x - m_x0) / m_step);
        int iz = Mathf.RoundToInt((z - m_z0) / m_step);
        if (ix < 0 || iz < 0 || ix >= m_nx || iz >= m_nz)
            return -1f;
        return m_distance[iz * m_nx + ix];
    }

    /// <summary>
    /// The same two measures for the land itself, every hundredth cell: the
    /// null a road distribution has to be judged against.
    /// </summary>
    public (List<float> shore, List<float> centre) LandSample()
    {
        List<float> shore = new(), centre = new();
        for (int i = 0; i < m_land.Length; i += 100)
        {
            if (!m_land[i]) continue;
            int ix = i % m_nx, iz = i / m_nx;
            float x = m_x0 + ix * m_step, z = m_z0 + iz * m_step;
            shore.Add(m_distance[i]);
            centre.Add(new Vector2(x, z).magnitude);
        }

        return (shore, centre);
    }

    /// <summary>The land null, split by biome: what distance-to-shore looks
    /// like for each kind of ground, so a road's preference for a biome is not
    /// mistaken for a preference for coasts.</summary>
    public Dictionary<string, List<float>> LandSampleByBiome(CsvWorld world)
    {
        Dictionary<string, List<float>> byBiome = new();
        for (int i = 0; i < m_land.Length; i += 100)
        {
            if (!m_land[i]) continue;
            int ix = i % m_nx, iz = i / m_nx;
            float x = m_x0 + ix * m_step, z = m_z0 + iz * m_step;
            if (!world.Covers(x, z)) continue;
            string biome = world.GetBiome(x, z).ToString();
            if (!byBiome.TryGetValue(biome, out List<float>? list))
                byBiome[biome] = list = new List<float>();
            list.Add(m_distance[i]);
        }

        return byBiome;
    }

    private static (float, float, float, int, int, bool[]) ReadLand(string path)
    {
        CsvWorld probe = new CsvWorld().Load(path);
        string description = probe.Describe();

        // The grid's geometry is in the description; read the file again for the
        // mask rather than holding two copies of every column.
        using StreamReader reader = new(path);
        string header = reader.ReadLine()!;
        string[] columns = header.Split(',');
        int cx = Array.IndexOf(columns, "x"), cz = Array.IndexOf(columns, "z");
        int ch = Array.IndexOf(columns, "height");

        int cb = Array.IndexOf(columns, "biome");
        List<(float x, float z, float h, string biome)> samples = new();
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        float secondX = float.MaxValue;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            float x = float.Parse(cells[cx], CultureInfo.InvariantCulture);
            float z = float.Parse(cells[cz], CultureInfo.InvariantCulture);
            float h = float.Parse(cells[ch], CultureInfo.InvariantCulture);
            samples.Add((x, z, h, cells[cb].Trim()));
            if (x < minX) { secondX = minX; minX = x; }
            else if (x > minX && x < secondX) secondX = x;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (z > maxZ) maxZ = z;
        }

        float step = secondX - minX;
        int nx = Mathf.RoundToInt((maxX - minX) / step) + 1;
        int nz = Mathf.RoundToInt((maxZ - minZ) / step) + 1;
        bool[] land = new bool[nx * nz];
        foreach ((float x, float z, float h, string biome) in samples)
        {
            int ix = Mathf.RoundToInt((x - minX) / step);
            int iz = Mathf.RoundToInt((z - minZ) / step);
            bool swamp = biome == "Swamp";
            bool openWater = swamp
                ? h < RoadConstants.DeepWaterHeight
                : h < RoadConstants.SeaLevel;
            land[iz * nx + ix] = !openWater;
        }

        return (minX, minZ, step, nx, nz, land);
    }
}
