using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralRoads.Tests;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// Where a finished network's centreline actually lies: every road point
/// looked up in the terrain the pathfinder walked, and grouped by what the
/// ground under it is.
///
/// A road over water is legal in three ways - a bridge deck spans it, a ford
/// wades it, a swamp road stands in the shallows by design - and illegal in a
/// fourth. Counting them apart is the only way to tell a picture's artefact
/// from a real fault.
/// </summary>
internal static class Audit
{
    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: roads-study audit <island-grid.csv> <terrain.csv> <routes.csv> [--out FILE]");
            return 2;
        }

        CsvWorld world = new CsvWorld().LoadIslandGrid(args[1]);
        world.Load(args[2]);
        WorldGenerator.instance = world;

        List<Row> points = ReadRoutePoints(args[3]);
        Console.WriteLine($"routes:    {points.Select(p => p.RouteIndex).Distinct().Count()}, {points.Count} centreline points");
        Console.WriteLine($"terrain:   {world.Describe()}");

        Dictionary<string, int> byKind = new();
        Dictionary<string, int> wetByKind = new();
        Dictionary<Heightmap.Biome, int> wetBiomes = new();
        List<Row> unexplained = new();

        foreach (Row point in points)
        {
            Bump(byKind, point.Kind);

            float height = world.GetHeight(point.X, point.Z);
            Heightmap.Biome biome = world.GetBiome(point.X, point.Z);
            world.GetRiverWeight(point.X, point.Z, out float river, out _);

            // "Wet" is the road floor, not the shoreline: a road point below
            // this is standing in water however shallow.
            bool wet = height < RoadConstants.ShallowWaterHeight;
            if (!wet)
                continue;

            Bump(wetByKind, point.Kind);
            wetBiomes.TryGetValue(biome, out int count);
            wetBiomes[biome] = count + 1;

            bool spanned = point.Kind == "Span";                       // a bridge deck, nothing painted
            bool forded = point.Kind is "Wade" or "Raise";              // a ford, by design
            bool swampShallows = biome == Heightmap.Biome.Swamp
                                 && height >= RoadConstants.DeepWaterHeight;   // wading, by design
            bool inRiverCore = river > RoadConstants.RiverImpassableThreshold;

            if (!spanned && !forded && !swampShallows && !inRiverCore)
            {
                point.Height = height;
                point.Biome = biome;
                unexplained.Add(point);
            }
        }

        Console.WriteLine();
        Console.WriteLine("points by stretch:   " + string.Join(", ", byKind.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        Console.WriteLine("of those, in water:  " + (wetByKind.Count > 0
            ? string.Join(", ", wetByKind.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))
            : "none"));
        Console.WriteLine("water by biome:      " + (wetBiomes.Count > 0
            ? string.Join(", ", wetBiomes.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))
            : "none"));
        Console.WriteLine();
        Console.WriteLine($"unexplained:         {unexplained.Count} point(s) in water that no bridge, ford, swamp or river accounts for");

        foreach (IGrouping<int, Row> group in unexplained.GroupBy(p => p.RouteIndex).OrderByDescending(g => g.Count()).Take(8))
        {
            Row worst = group.OrderBy(p => p.Height).First();
            Console.WriteLine($"   route {group.Key} \"{worst.Label}\": {group.Count()} points, " +
                              $"deepest {worst.Height:F1} m at ({worst.X:F0},{worst.Z:F0}) in {worst.Biome}");
        }

        string? outPath = Options.Value(args, "--out");
        if (outPath != null)
        {
            using StreamWriter writer = new(outPath);
            writer.WriteLine("route_index,label,kind,x,z,height,biome");
            foreach (Row point in unexplained)
                writer.WriteLine($"{point.RouteIndex},\"{point.Label}\",{point.Kind}," +
                                 $"{point.X.ToString("F1", CultureInfo.InvariantCulture)}," +
                                 $"{point.Z.ToString("F1", CultureInfo.InvariantCulture)}," +
                                 $"{point.Height.ToString("F2", CultureInfo.InvariantCulture)},{point.Biome}");
            Console.WriteLine($"wrote:               {outPath}");
        }

        return 0;
    }

    private static void Bump(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    internal sealed class Row
    {
        public int RouteIndex;
        public string Label = "";
        public string Kind = "Road";
        public float X, Z;
        public float Height;
        public Heightmap.Biome Biome;
    }

    private static List<Row> ReadRoutePoints(string path)
    {
        List<Row> rows = new();
        foreach (string[] cells in Csv.Rows(path, out string[] columns))
        {
            int index = Array.IndexOf(columns, "route_index");
            int label = Array.IndexOf(columns, "label");
            int kind = Array.IndexOf(columns, "kind");
            int x = Array.IndexOf(columns, "x");
            int z = Array.IndexOf(columns, "z");
            rows.Add(new Row
            {
                RouteIndex = (int)Csv.Number(cells[index]),
                Label = cells[label].Trim('"'),
                Kind = kind >= 0 ? cells[kind].Trim() : "Road",
                X = Csv.Number(cells[x]),
                Z = Csv.Number(cells[z]),
            });
        }

        return rows;
    }
}
