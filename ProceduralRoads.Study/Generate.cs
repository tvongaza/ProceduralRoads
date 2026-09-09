using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ProceduralRoads.Tests;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// A whole road network generated offline on dumped terrain: the mod's own
/// generator, its own island detection, its own pathfinder, with every route,
/// every attempt and every crossing written out.
///
/// The terrain is a dump, so the run is labelled terrain=approx: exact where a
/// query lands on a sample, bilinear between samples. It compares strategies
/// honestly, because every strategy sees the same terrain. It does not settle
/// why one particular road failed in the game.
/// </summary>
internal static class Generate
{
    public static int Run(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "usage: roads-study generate <island-grid.csv> <terrain.csv[,terrain.csv...]> <locations.csv> --out DIR\n" +
                "         [--strategy shipped|reachable] [--crossings on|off] [--islands PCT]\n" +
                "         [--iterations N] [--max-locations N] [--width M] [--label NAME]");
            return 2;
        }

        string gridPath = args[1];
        string[] terrainPaths = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
        string locationsPath = args[3];
        string outDir = Options.Value(args, "--out") ?? Directory.GetCurrentDirectory();
        string strategyName = Options.Value(args, "--strategy") ?? "shipped";
        bool crossings = (Options.Value(args, "--crossings") ?? "on") == "on";
        int islandPercentage = int.Parse(Options.Value(args, "--islands") ?? "100", CultureInfo.InvariantCulture);
        int iterations = int.Parse(Options.Value(args, "--iterations") ?? "100000", CultureInfo.InvariantCulture);
        int maxLocations = int.Parse(Options.Value(args, "--max-locations") ?? "12", CultureInfo.InvariantCulture);
        float width = float.Parse(Options.Value(args, "--width") ?? "4", CultureInfo.InvariantCulture);
        string label = Options.Value(args, "--label") ?? strategyName;

        RoadNetworkStrategy strategy = strategyName switch
        {
            "shipped" => RoadNetworkStrategy.Shipped,
            "reachable" => RoadNetworkStrategy.Reachable,
            _ => throw new ArgumentException($"unknown strategy '{strategyName}' (shipped|reachable)"),
        };

        Directory.CreateDirectory(outDir);

        CsvWorld world = new CsvWorld().LoadIslandGrid(gridPath);
        foreach (string path in terrainPaths)
            world.Load(path);
        WorldGenerator.instance = world;

        List<Program.Location> locations = Program.ReadLocations(locationsPath);
        ZoneSystem zones = new();
        foreach (Program.Location location in locations)
        {
            zones.Locations.Add(new ZoneSystem.LocationInstance
            {
                m_position = location.Position,
                m_location = new ZoneSystem.ZoneLocation
                {
                    m_prefab = new ZoneSystem.ZoneLocation.PrefabEntry { Name = location.Name },
                    m_exteriorRadius = location.Radius,
                },
            });
        }
        ZoneSystem.instance = zones;

        // Everything a network depends on, set here and recorded in the
        // manifest: a run nobody can reproduce is not evidence.
        RoadNetworkGenerator.Reset();
        RoadNetworkGenerator.Strategy = strategy;
        RoadNetworkGenerator.RoadWidth = width;
        RoadNetworkGenerator.IslandRoadPercentage = islandPercentage;
        RoadNetworkGenerator.MaxLocationsPerIsland = maxLocations;
        RoadPathfinder.MaxIterations = iterations;
        RoadPathfinder.FordsEnabled = crossings;
        RoadPathfinder.BridgesEnabled = crossings;
        RoadAttemptLog.Enabled = true;

        Console.WriteLine($"world:     {world.Describe()}");
        Console.WriteLine($"run:       strategy={strategy} crossings={(crossings ? "on" : "off")} " +
                          $"islands={islandPercentage}% iterations={iterations} maxLocations={maxLocations} width={width}");

        DateTime started = DateTime.UtcNow;
        try
        {
            RoadNetworkGenerator.GenerateRoads(force: true);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("outside every dump"))
        {
            Console.Error.WriteLine($"ABORTED: the run asked the world for a position the dumps do not cover.\n  {error.Message}");
            return 3;
        }
        TimeSpan elapsed = DateTime.UtcNow - started;

        IReadOnlyList<RoadRoute> routes = RoadRouteRecorder.Routes;
        IReadOnlyList<RoadAttempt> attempts = RoadAttemptLog.Attempts;
        IReadOnlyList<RoadCrossing> sites = RoadNetworkGenerator.GetRoadCrossings();

        File.WriteAllText(Path.Combine(outDir, $"{label}.routes.csv"), RoadRouteRecorder.ToCsv());
        File.WriteAllText(Path.Combine(outDir, $"{label}.attempts.csv"), RoadAttemptLog.ToCsv());
        File.WriteAllText(Path.Combine(outDir, $"{label}.crossings.csv"), RoadCrossingCsv.ToCsv(sites));
        File.WriteAllText(Path.Combine(outDir, $"{label}.manifest.json"),
            Manifest(label, strategy, crossings, islandPercentage, iterations, maxLocations, width,
                gridPath, terrainPaths, locationsPath, world, routes, attempts, sites, elapsed));

        Report(routes, attempts, sites, elapsed, outDir, label);
        return 0;
    }

    private static void Report(IReadOnlyList<RoadRoute> routes, IReadOnlyList<RoadAttempt> attempts,
        IReadOnlyList<RoadCrossing> sites, TimeSpan elapsed, string outDir, string label)
    {
        int failed = attempts.Count(a => !a.Connected);
        Dictionary<string, int> outcomes = new();
        foreach (RoadAttempt attempt in attempts.Where(a => !a.Connected))
        {
            outcomes.TryGetValue(attempt.Outcome, out int count);
            outcomes[attempt.Outcome] = count + 1;
        }

        Dictionary<CrossingRejection, int> rejections = new();
        foreach (RoadAttempt attempt in attempts)
        {
            foreach (KeyValuePair<CrossingRejection, int> entry in attempt.Rejections)
            {
                rejections.TryGetValue(entry.Key, out int count);
                rejections[entry.Key] = count + entry.Value;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"roads:     {routes.Count} built, {routes.Sum(r => r.Length) / 1000f:F1} km");
        Console.WriteLine($"attempts:  {attempts.Count}, {failed} failed" +
                          (outcomes.Count > 0
                              ? " (" + string.Join(", ", outcomes.OrderByDescending(o => o.Value).Select(o => $"{o.Key} {o.Value}")) + ")"
                              : ""));
        Console.WriteLine($"crossings: {sites.Count} " +
                          $"({sites.Count(c => c.Kind == CrossingKind.Bridge)} bridges, " +
                          $"{sites.Count(c => c.Kind == CrossingKind.Ford)} fords)");
        if (rejections.Count > 0)
            Console.WriteLine("refused:   " + string.Join(", ",
                rejections.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {r.Value}")));
        Console.WriteLine($"time:      {elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"wrote:     {Path.Combine(outDir, label)}.{{routes,attempts,crossings}}.csv + manifest.json");
    }

    private static string Manifest(string label, RoadNetworkStrategy strategy, bool crossings,
        int islandPercentage, int iterations, int maxLocations, float width,
        string gridPath, string[] terrainPaths, string locationsPath, CsvWorld world,
        IReadOnlyList<RoadRoute> routes, IReadOnlyList<RoadAttempt> attempts,
        IReadOnlyList<RoadCrossing> sites, TimeSpan elapsed)
    {
        string Json(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        List<string> lines = new()
        {
            "{",
            $"  \"label\": \"{Json(label)}\",",
            $"  \"generatedUtc\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",",
            "  \"terrain\": \"approx\",",
            $"  \"islandGrid\": \"{Json(Path.GetFileName(gridPath))}\",",
            $"  \"terrainDumps\": [{string.Join(", ", terrainPaths.Select(p => $"\"{Json(Path.GetFileName(p))}\""))}],",
            $"  \"locations\": \"{Json(Path.GetFileName(locationsPath))}\",",
            "  \"approximations\": [",
            string.Join(",\n", world.Approximations.Select(a => $"    \"{Json(a)}\"")),
            "  ],",
            "  \"config\": {",
            $"    \"Strategy\": \"{strategy}\",",
            $"    \"RoadWidth\": {width.ToString(CultureInfo.InvariantCulture)},",
            $"    \"IslandRoadPercentage\": {islandPercentage},",
            $"    \"MaxLocationsPerIsland\": {maxLocations},",
            $"    \"PathfindingMaxIterations\": {iterations},",
            $"    \"FordsEnabled\": {(crossings ? "true" : "false")},",
            $"    \"BridgesEnabled\": {(crossings ? "true" : "false")}",
            "  },",
            "  \"result\": {",
            $"    \"routeCount\": {routes.Count},",
            $"    \"totalLengthMeters\": {routes.Sum(r => r.Length):F0},",
            $"    \"attemptCount\": {attempts.Count},",
            $"    \"failedAttemptCount\": {attempts.Count(a => !a.Connected)},",
            $"    \"crossingCount\": {sites.Count},",
            $"    \"bridgeCount\": {sites.Count(c => c.Kind == CrossingKind.Bridge)},",
            $"    \"roadNetworkVersion\": {RoadSpatialGrid.RoadNetworkVersion},",
            $"    \"seconds\": {elapsed.TotalSeconds:F1}",
            "  }",
            "}",
        };
        return string.Join("\n", lines) + "\n";
    }
}

internal static class Options
{
    public static string? Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }
}
