using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralRoads.Tests;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// One row per island per run: what the island offered, what the run chose,
/// what it built there, and what it left unconnected.
///
/// An island is the unit a player experiences, and it is where the study's
/// averages hide the most: a world total of ninety roads says nothing about
/// whether the big island got a network and the small ones got a stub each.
/// Every island gets a row, including the ones that got nothing.
/// </summary>
internal static class PerIsland
{
    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "usage: roads-study per-island <island-grid.csv> <locations.csv> <run-prefix> [--out FILE]\n" +
                "       run-prefix is the path a generate run wrote, without .routes.csv");
            return 2;
        }

        string gridPath = args[1];
        string locationsPath = args[2];
        string prefix = args[3];
        string outPath = Options.Value(args, "--out") ?? prefix + ".per-island.csv";

        CsvWorld world = new CsvWorld().LoadIslandGrid(gridPath);
        WorldGenerator.instance = world;
        List<Program.Location> places = Program.ReadLocations(locationsPath);
        List<Island> islands = IslandDetector.DetectIslands();

        List<Compare.Route> routes = Compare.ReadRoutes(prefix + ".routes.csv");
        List<Attempt> attempts = ReadAttempts(prefix + ".attempts.csv");
        Dictionary<int, (int offered, int selected)> selection = ReadSelection(prefix + ".selection.csv");

        using StreamWriter writer = new(outPath);
        writer.WriteLine("island_id,area_km2,ring,distance_from_centre_m,places,eligible_places," +
                         "offered,selected,roads,length_m,longest_road_m,served,networks," +
                         "attempts,failed,unreachable,budget_spent");

        int islandsWithRoads = 0;
        foreach (Island island in islands.OrderByDescending(i => i.ApproxArea))
        {
            List<Program.Location> onIsland = places.Where(p => island.ContainsPoint(p.Position)).ToList();
            List<Compare.Route> here = routes.Where(r => Inside(island, r)).ToList();
            List<Attempt> tried = attempts.Where(a => island.ContainsPoint(To3(a.Start))
                                                      || island.ContainsPoint(To3(a.End))).ToList();
            selection.TryGetValue(island.Id, out (int offered, int selected) chose);

            NetworkMetrics.Result metrics = NetworkMetrics.Measure(
                here.Select(ToRoute).ToList(), onIsland);

            if (here.Count > 0)
                islandsWithRoads++;

            writer.WriteLine(string.Join(",",
                island.Id,
                (island.ApproxArea / 1_000_000f).ToString("F2", CultureInfo.InvariantCulture),
                Ring(island),
                island.Center.magnitude.ToString("F0", CultureInfo.InvariantCulture),
                onIsland.Count,
                onIsland.Count(p => Eligible.IsRoadLocation(p.Name)),
                chose.offered,
                chose.selected,
                here.Count,
                here.Sum(r => r.Length).ToString("F0", CultureInfo.InvariantCulture),
                (here.Count > 0 ? here.Max(r => r.Length) : 0f).ToString("F0", CultureInfo.InvariantCulture),
                metrics.PlacesServed,
                metrics.Components,
                tried.Count,
                tried.Count(a => !a.Connected),
                tried.Count(a => a.Outcome == "no reachable path"),
                tried.Count(a => a.Outcome == "max iterations reached")));
        }

        Console.WriteLine($"islands:   {islands.Count}, {islandsWithRoads} with at least one road");
        Console.WriteLine($"roads:     {routes.Count} on the world, {routes.Sum(r => r.Length) / 1000f:F1} km");
        Console.WriteLine($"wrote:     {outPath}");
        return 0;
    }

    private static Vector3 To3(Vector2 p) => new(p.x, 0f, p.y);

    private static bool Inside(Island island, Compare.Route route) =>
        island.ContainsPoint(To3(route.Start)) || island.ContainsPoint(To3(route.End));

    /// <summary>The metrics want the mod's route type; the comparison reader
    /// produces its own. Only the points matter here.</summary>
    private static RoadRoute ToRoute(Compare.Route route) =>
        new(0, route.Label, 4f,
            route.Points.Select(p => new Vector3(p.x, 0f, p.y)).ToList(),
            new List<RoadRouteSegment>());

    private static int Ring(Island island)
    {
        float normalized = Mathf.Clamp01(island.Center.magnitude / 10000f);
        return normalized < 0.33f ? 0 : normalized < 0.66f ? 1 : 2;
    }

    internal sealed class Attempt
    {
        public Vector2 Start, End;
        public bool Connected;
        public string Outcome = "";
    }

    private static List<Attempt> ReadAttempts(string path)
    {
        List<Attempt> attempts = new();
        foreach (string[] cells in Csv.Rows(path, out string[] columns))
        {
            attempts.Add(new Attempt
            {
                Start = new Vector2(Csv.Number(cells[Array.IndexOf(columns, "start_x")]),
                                    Csv.Number(cells[Array.IndexOf(columns, "start_z")])),
                End = new Vector2(Csv.Number(cells[Array.IndexOf(columns, "end_x")]),
                                  Csv.Number(cells[Array.IndexOf(columns, "end_z")])),
                Connected = cells[Array.IndexOf(columns, "connected")].Trim() == "true",
                Outcome = cells[Array.IndexOf(columns, "outcome")].Trim('"'),
            });
        }

        return attempts;
    }

    private static Dictionary<int, (int offered, int selected)> ReadSelection(string path)
    {
        Dictionary<int, (int offered, int selected)> counts = new();
        if (!File.Exists(path))
            return counts;

        foreach (string[] cells in Csv.Rows(path, out string[] columns))
        {
            int id = (int)Csv.Number(cells[Array.IndexOf(columns, "island_id")]);
            bool selected = cells[Array.IndexOf(columns, "selected")].Trim() == "true";
            counts.TryGetValue(id, out (int offered, int selected) row);
            counts[id] = (row.offered + 1, row.selected + (selected ? 1 : 0));
        }

        return counts;
    }
}
