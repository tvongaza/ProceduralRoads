using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralRoads.Tests;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// Road generation on real terrain, offline: a world dumped from the game, the
/// mod's own island detection and generator, and every decision written out.
///
///   roads-study islands &lt;base128.csv&gt; &lt;locations.csv&gt; [--out DIR]
///
/// Study branch tool. Not part of any PR.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: roads-study islands <base128.csv> <locations.csv> [--out DIR]\n       roads-study generate <island-grid.csv> <terrain.csv> <locations.csv> --out DIR [options]");
            return 2;
        }

        switch (args[0])
        {
            case "islands":
                return Islands(args);
            case "generate":
                return Generate.Run(args);
            case "compare":
                return Compare.Run(args);
            case "outcomes":
                return Outcomes.Run(args);
            default:
                Console.Error.WriteLine($"unknown command '{args[0]}'");
                return 2;
        }
    }

    private static int Islands(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: roads-study islands <base128.csv> <locations.csv> [--out DIR]");
            return 2;
        }

        string worldPath = args[1];
        string locationsPath = args[2];
        string outDir = Options.Value(args, "--out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);

        CsvWorld world = new CsvWorld().LoadIslandGrid(worldPath);
        WorldGenerator.instance = world;
        Console.WriteLine($"world:     {world.Describe()}");

        List<Location> locations = ReadLocations(locationsPath);
        Console.WriteLine($"locations: {locations.Count} placed");

        List<Island> islands = IslandDetector.DetectIslands();
        Console.WriteLine($"islands:   {islands.Count} detected (128 m grid, at least 10 cells)");

        // Every island gets a row, including the ones with nothing on them:
        // an island without a single eligible place is an outcome, not a gap.
        List<(Island island, List<Location> all, List<Location> eligible)> rows = new();
        HashSet<Location> assigned = new();
        foreach (Island island in islands)
        {
            List<Location> on = locations.Where(l => island.ContainsPoint(l.Position)).ToList();
            foreach (Location location in on)
                assigned.Add(location);
            rows.Add((island, on, on.Where(l => Eligible.IsRoadLocation(l.Name)).ToList()));
        }

        rows.Sort((a, b) => b.island.ApproxArea.CompareTo(a.island.ApproxArea));

        string islandsCsv = Path.Combine(outDir, "islands.csv");
        using (StreamWriter writer = new(islandsCsv))
        {
            writer.WriteLine("island_id,center_x,center_z,cells,area_km2,ring,distance_from_centre_m," +
                             "locations,eligible_locations,max_locations,bosses");
            foreach ((Island island, List<Location> all, List<Location> eligible) in rows)
            {
                writer.WriteLine(string.Join(",",
                    island.Id,
                    F(island.Center.x), F(island.Center.y),
                    island.CellCount,
                    (island.ApproxArea / 1_000_000f).ToString("F2", CultureInfo.InvariantCulture),
                    Ring(island),
                    F(island.Center.magnitude),
                    all.Count,
                    eligible.Count,
                    Eligible.MaxLocationsFor(island),
                    eligible.Count(l => Eligible.IsBoss(l.Name))));
            }
        }

        int withEligible = rows.Count(r => r.eligible.Count > 0);
        int withOne = rows.Count(r => r.eligible.Count == 1);
        int unassigned = locations.Count - assigned.Count;
        float landArea = islands.Sum(i => i.ApproxArea) / 1_000_000f;

        Console.WriteLine();
        Console.WriteLine($"land:      {landArea:F0} km² over {islands.Count} islands " +
                          $"(largest {rows[0].island.ApproxArea / 1_000_000f:F1} km², " +
                          $"median {rows[rows.Count / 2].island.ApproxArea / 1_000_000f:F1} km²)");
        Console.WriteLine($"eligible:  {rows.Sum(r => r.eligible.Count)} road locations on {withEligible} islands; " +
                          $"{islands.Count - withEligible} islands have none, {withOne} have exactly one " +
                          "(nothing to connect them to)");
        Console.WriteLine($"off-island: {unassigned} placed locations sit on no detected island " +
                          "(too small a landmass, or shore cells the 128 m grid calls water)");
        Console.WriteLine($"wrote:     {islandsCsv}");
        return 0;
    }

    private static int Ring(Island island)
    {
        float normalized = Mathf.Clamp01(island.Center.magnitude / 10000f);
        return normalized < 0.33f ? 0 : normalized < 0.66f ? 1 : 2;
    }

    private static string F(float value) => value.ToString("F0", CultureInfo.InvariantCulture);

    public sealed class Location
    {
        public string Name = "";
        public Vector3 Position;
        public float Radius;
    }

    public static List<Location> ReadLocations(string path)
    {
        List<Location> locations = new();
        using StreamReader reader = new(path);
        string? header = reader.ReadLine();
        if (header == null)
            throw new InvalidDataException($"{path}: empty");

        string[] columns = header.Split(',');
        int name = Array.IndexOf(columns, "name");
        int x = Array.IndexOf(columns, "x");
        int z = Array.IndexOf(columns, "z");
        int radius = Array.IndexOf(columns, "radius");
        if (name < 0 || x < 0 || z < 0 || radius < 0)
            throw new InvalidDataException($"{path}: needs name,x,z,radius columns");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            locations.Add(new Location
            {
                Name = cells[name],
                Position = new Vector3(Parse(cells[x]), 0f, Parse(cells[z])),
                Radius = Parse(cells[radius]),
            });
        }

        return locations;
    }

    private static float Parse(string cell) =>
        float.Parse(cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
