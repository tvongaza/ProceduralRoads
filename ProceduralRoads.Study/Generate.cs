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
        // Fords and bridges can be asked for separately: they answer different
        // questions, a ford being a place a road wades and a bridge a place it
        // does not touch the water at all.
        bool fords = (Options.Value(args, "--fords") ?? (crossings ? "on" : "off")) == "on";
        bool bridges = (Options.Value(args, "--bridges") ?? (crossings ? "on" : "off")) == "on";
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

        // Four stages, timed apart. A single number for a run cannot be
        // compared across plans, because reading a quarter-gigabyte dump and
        // measuring a finished network are not what a planner costs - and the
        // study's own 21x runtime claim came from comparing numbers whose
        // scopes did not match.
        System.Diagnostics.Stopwatch stage = System.Diagnostics.Stopwatch.StartNew();
        CsvWorld world = new CsvWorld().LoadIslandGrid(gridPath);
        foreach (string path in terrainPaths)
            world.Load(path);
        WorldGenerator.instance = world;

        List<Program.Location> locations = Program.ReadLocations(locationsPath);
        TimeSpan loadElapsed = stage.Elapsed;
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
        ApplyFactorOverrides(args);
        RoadNetworkGenerator.RoadWidth = width;
        RoadNetworkGenerator.IslandRoadPercentage = islandPercentage;
        RoadNetworkGenerator.MaxLocationsPerIsland = maxLocations;
        RoadPathfinder.MaxIterations = iterations;
        RoadPathfinder.FordsEnabled = fords;
        RoadPathfinder.BridgesEnabled = bridges;
        RoadAttemptLog.Enabled = true;

        Console.WriteLine($"world:     {world.Describe()}");
        Console.WriteLine($"run:       strategy={strategy} fords={(fords ? "on" : "off")} bridges={(bridges ? "on" : "off")} " +
                          $"islands={islandPercentage}% iterations={iterations} maxLocations={maxLocations} width={width}");
        Console.WriteLine($"factors:   {StudyFactors.Describe()}");
        Console.WriteLine($"places:    {Presets.Describe()}");

        stage.Restart();
        try
        {
            RoadNetworkGenerator.GenerateRoads(force: true);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("outside every dump"))
        {
            Console.Error.WriteLine($"ABORTED: the run asked the world for a position the dumps do not cover.\n  {error.Message}");
            return 3;
        }
        TimeSpan elapsed = stage.Elapsed;

        IReadOnlyList<RoadRoute> routes = RoadRouteRecorder.Routes;
        IReadOnlyList<RoadAttempt> attempts = RoadAttemptLog.Attempts;
        IReadOnlyList<RoadCrossing> sites = RoadNetworkGenerator.GetRoadCrossings();

        stage.Restart();
        NetworkMetrics.Result metrics = NetworkMetrics.Measure(routes, locations, attempts);
        List<Island> islands = IslandDetector.DetectIslands();
        string placesCsv = PlaceOutcomes.ToCsv(locations, routes, attempts, islands);
        TimeSpan analysisElapsed = stage.Elapsed;

        stage.Restart();
        if (!Options.Has(args, "--no-routes"))
            File.WriteAllText(Path.Combine(outDir, $"{label}.routes.csv"), RoadRouteRecorder.ToCsv());
        File.WriteAllText(Path.Combine(outDir, $"{label}.attempts.csv"), RoadAttemptLog.ToCsv());
        File.WriteAllText(Path.Combine(outDir, $"{label}.selection.csv"), RoadSelectionLog.ToCsv());
        File.WriteAllText(Path.Combine(outDir, $"{label}.crossings.csv"), RoadCrossingCsv.ToCsv(sites));
        File.WriteAllText(Path.Combine(outDir, $"{label}.islands.csv"), RoadIslandLog.ToCsv());
        File.WriteAllText(Path.Combine(outDir, $"{label}.places.csv"), placesCsv);
        TimeSpan exportElapsed = stage.Elapsed;

        File.WriteAllText(Path.Combine(outDir, $"{label}.manifest.json"),
            Manifest(label, strategy, fords, bridges, islandPercentage, iterations, maxLocations, width,
                metrics, gridPath, terrainPaths, locationsPath, world, routes, attempts, sites,
                elapsed, loadElapsed, analysisElapsed, exportElapsed));

        Report(routes, attempts, sites, locations, elapsed, outDir, label);
        Console.WriteLine($"stages:    load {loadElapsed.TotalSeconds:F1} s, generate {elapsed.TotalSeconds:F1} s, " +
                          $"analyse {analysisElapsed.TotalSeconds:F1} s, export {exportElapsed.TotalSeconds:F1} s " +
                          $"({Build.Configuration} build)");
        return 0;
    }

    /// <summary>
    /// One factor at a time: a run may take a package and change exactly one of
    /// its rules, which is the only way to say what that rule is worth.
    /// </summary>
    private static void ApplyFactorOverrides(string[] args)
    {
        string? anchor = Options.Value(args, "--anchor");
        if (anchor != null)
            StudyFactors.Anchor = anchor switch
            {
                "edge" => AnchorMode.IslandEdgeCell,
                "poi" => AnchorMode.HighestPriorityLocation,
                "edge-on-land" => AnchorMode.IslandEdgeCellOnLand,
                _ => throw new ArgumentException(
                    $"--anchor must be edge, edge-on-land or poi, not '{anchor}'"),
            };

        string? islands = Options.Value(args, "--island-selection");
        if (islands != null)
            StudyFactors.Islands = islands switch
            {
                "largest" => IslandSelection.LargestFirst,
                "rings" => IslandSelection.RingBalanced,
                _ => throw new ArgumentException($"--island-selection must be largest or rings, not '{islands}'"),
            };

        string? quota = Options.Value(args, "--quota");
        if (quota != null)
            StudyFactors.Quota = quota switch
            {
                "truncate" => LocationQuota.PriorityTruncated,
                "nearest" => LocationQuota.PriorityThenNearest,
                "farthest" => LocationQuota.PriorityThenFarthest,
                "random" => LocationQuota.SeededRandom,
                _ => throw new ArgumentException(
                    $"--quota must be truncate, nearest, farthest or random, not '{quota}'"),
            };

        string? plan = Options.Value(args, "--plan");
        if (plan != null)
            StudyFactors.Plan = plan switch
            {
                "parity" => ConnectionPlan.ChainOrMstByParity,
                "tree" => ConnectionPlan.TreeWithRetries,
                "routed-mst" => ConnectionPlan.RoutedMst,
                "trunk" => ConnectionPlan.TrunkAndSpurs,
                "hub" => ConnectionPlan.HubAndSpoke,
                "grow" => ConnectionPlan.GrowFromNetwork,
                "reverse" => ConnectionPlan.ReverseToNetwork,
                _ => throw new ArgumentException(
                    $"--plan must be parity, tree, routed-mst, trunk, hub, grow or reverse, not '{plan}'"),
            };

        string? fallback = Options.Value(args, "--fallback");
        if (fallback != null)
            StudyFactors.Fallback = fallback switch
            {
                "none" => FailureFallback.None,
                "road" => FailureFallback.NearestRoad,
                "place" => FailureFallback.NearestConnectedPlace,
                "road-then-place" => FailureFallback.RoadThenPlace,
                _ => throw new ArgumentException(
                    $"--fallback must be none, road, place or road-then-place, not '{fallback}'"),
            };

        string preset = Options.Value(args, "--preset") ?? "shipped";
        if (!Presets.Apply(preset))
            throw new ArgumentException($"unknown preset '{preset}'");

        string? places = Options.Value(args, "--places");
        if (places != null)
        {
            if (places == "all")
                StudyFactors.Quantity = IslandQuota.EveryEligiblePlace;
            else if (places == "formula")
                StudyFactors.Quantity = IslandQuota.AreaFormula;
            else if (int.TryParse(places, out int count) && count > 0)
            {
                StudyFactors.Quantity = IslandQuota.FixedCount;
                StudyFactors.FixedPlaceCount = count;
            }
            else
                throw new ArgumentException($"--places must be all, formula or a number, not '{places}'");
        }

        string? neighbours = Options.Value(args, "--neighbours");
        if (neighbours != null)
            StudyFactors.RoutedPlanNeighbours = int.Parse(neighbours, CultureInfo.InvariantCulture);

        string? sharing = Options.Value(args, "--road-sharing");
        if (sharing != null)
            StudyFactors.ExistingRoadCostFraction = float.Parse(sharing, CultureInfo.InvariantCulture);

        string? reach = Options.Value(args, "--road-reach");
        if (reach != null)
            StudyFactors.ExistingRoadReach = float.Parse(reach, CultureInfo.InvariantCulture);

        string? filter = Options.Value(args, "--filter-endpoints");
        if (filter != null)
            StudyFactors.FilterUnreachableEndpoints = OnOff(filter, "--filter-endpoints");

        string? snap = Options.Value(args, "--snap-endpoints");
        if (snap != null)
            StudyFactors.SnapEndpointsToPathableGround = OnOff(snap, "--snap-endpoints");
    }

    private static bool OnOff(string value, string name) => value switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException($"{name} must be on or off, not '{value}'"),
    };

    private static void Report(IReadOnlyList<RoadRoute> routes, IReadOnlyList<RoadAttempt> attempts,
        IReadOnlyList<RoadCrossing> sites, List<Program.Location> places, TimeSpan elapsed,
        string outDir, string label)
    {
        NetworkMetrics.Result metrics = NetworkMetrics.Measure(routes, places, attempts);
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
        Console.WriteLine($"roads:     {routes.Count} built, {metrics.UniqueLengthMetres / 1000f:F1} km of distinct road " +
                          $"({metrics.SummedLengthMetres / 1000f:F1} km summed over routes)");
        Console.WriteLine($"connected: {metrics.PlannedConnections} places the generator planned a road to and built it");
        Console.WriteLine($"joins:     {metrics.TeeJunctions} road(s) end on another road's length (a tee), " +
                          $"{metrics.EndToEndJoins} end where another road ends; " +
                          $"{metrics.ParallelMetres / 1000f:F1} km running alongside another road");
        if (RoadNetworkGenerator.FallbacksToRoad + RoadNetworkGenerator.FallbacksToPlace > 0)
            Console.WriteLine($"fallback:  {RoadNetworkGenerator.FallbacksToRoad} recovered onto a road, " +
                              $"{RoadNetworkGenerator.FallbacksToPlace} onto a connected place");
        Console.WriteLine($"served:    {metrics.PlacesServed} places with a road end within reach, " +
                          $"in {metrics.Components} joined group(s), largest {metrics.LargestComponentRoutes} roads");
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
        if (RoadNetworkGenerator.RoutingProbes > 0)
            Console.WriteLine($"probes:    {RoadNetworkGenerator.RoutingProbes} searches run to price a connection before building it, " +
                              $"{RoadNetworkGenerator.RoutingProbesWithoutRoute} of them found no route");
        Console.WriteLine($"time:      {elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"wrote:     {Path.Combine(outDir, label)}.{{routes,attempts,crossings,selection}}.csv + manifest.json");
    }

    /// <summary>
    /// A short name for this exact configuration, stable across machines: the
    /// same settings on the same inputs always produce the same id, so a table
    /// can cite one and a reader can find the run that made it.
    /// </summary>
    private static string RunId(string label, RoadNetworkStrategy strategy, bool fords, bool bridges,
        int islandPercentage, int iterations, int maxLocations, float width)
    {
        string spec = string.Join("|", label, strategy, StudyFactors.Describe(), Presets.Describe(),
            fords, bridges, islandPercentage, iterations, maxLocations,
            width.ToString(CultureInfo.InvariantCulture));
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(spec));
        return Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }

    /// <summary>An input identified by its content, so a renamed or edited dump
    /// cannot pass for the one a run actually used.</summary>
    private static string FileDigest(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream))[..16].ToLowerInvariant();
        }
        catch (IOException)
        {
            return "unreadable";
        }
    }

    /// <summary>The commit the study code was built from, if it can be read.</summary>
    private static string GitDescribe()
    {
        // With the dirty marker, because the q4 family of runs recorded a
        // commit whose tree did not contain the code that produced them: the
        // run happened two minutes before the commit that added the metrics
        // it reported. A commit id that can be wrong is worse than no id.
        try
        {
            string head = Git("rev-parse --short HEAD");
            string dirty = Git("status --porcelain --untracked-files=no");
            if (head.Length == 0)
                return "unknown";
            return dirty.Length > 0 ? head + "-dirty" : head;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static string Git(string arguments)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo info = new("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(info);
            if (process == null)
                return "";
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return output;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string Manifest(string label, RoadNetworkStrategy strategy, bool fords, bool bridges,
        int islandPercentage, int iterations, int maxLocations, float width, NetworkMetrics.Result metrics,
        string gridPath, string[] terrainPaths, string locationsPath, CsvWorld world,
        IReadOnlyList<RoadRoute> routes, IReadOnlyList<RoadAttempt> attempts,
        IReadOnlyList<RoadCrossing> sites, TimeSpan elapsed,
        TimeSpan loadElapsed, TimeSpan analysisElapsed, TimeSpan exportElapsed)
    {
        string Json(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        // A run nobody can reproduce is not evidence. The manifest carries what
        // it takes to rebuild this exact run: the code it ran, the inputs by
        // content rather than by name, and a run id every table can cite.
        string runId = RunId(label, strategy, fords, bridges, islandPercentage, iterations, maxLocations, width);
        List<string> lines = new()
        {
            "{",
            $"  \"runId\": \"{runId}\",",
            $"  \"label\": \"{Json(label)}\",",
            $"  \"studyCommit\": \"{Json(GitDescribe())}\",",
            $"  \"generatedUtc\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",",
            // The build configuration belongs in the manifest because it is
            // worth a factor of six on this workload, and two runs that did
            // not record it were compared as if it were free.
            $"  \"buildConfiguration\": \"{Build.Configuration}\",",
            $"  \"runtime\": \"{Json(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)}\",",
            $"  \"platform\": \"{Json(System.Runtime.InteropServices.RuntimeInformation.OSDescription.Split('\n')[0])} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}\",",
            $"  \"processors\": {Environment.ProcessorCount},",
            "  \"terrain\": \"approx\",",
            $"  \"islandGrid\": \"{Json(Path.GetFileName(gridPath))}\",",
            $"  \"islandGridSha\": \"{FileDigest(gridPath)}\",",
            $"  \"locationsSha\": \"{FileDigest(locationsPath)}\",",
            $"  \"terrainSha\": [{string.Join(", ", terrainPaths.Select(t => $"\"{FileDigest(t)}\""))}],",
            $"  \"terrainDumps\": [{string.Join(", ", terrainPaths.Select(p => $"\"{Json(Path.GetFileName(p))}\""))}],",
            $"  \"locations\": \"{Json(Path.GetFileName(locationsPath))}\",",
            "  \"approximations\": [",
            string.Join(",\n", world.Approximations.Select(a => $"    \"{Json(a)}\"")),
            "  ],",
            "  \"config\": {",
            $"    \"Strategy\": \"{strategy}\",",
            $"    \"Factors\": \"{Json(StudyFactors.Describe())}\",",
            $"    \"Places\": \"{Json(Presets.Describe())}\",",
            $"    \"RoadWidth\": {width.ToString(CultureInfo.InvariantCulture)},",
            $"    \"IslandRoadPercentage\": {islandPercentage},",
            $"    \"MaxLocationsPerIsland\": {maxLocations},",
            $"    \"PathfindingMaxIterations\": {iterations},",
            $"    \"FordsEnabled\": {(fords ? "true" : "false")},",
            $"    \"BridgesEnabled\": {(bridges ? "true" : "false")}",
            "  },",
            "  \"result\": {",
            $"    \"routeCount\": {routes.Count},",
            $"    \"summedRouteLengthMeters\": {routes.Sum(r => r.Length):F0},",
            $"    \"uniqueRoadLengthMeters\": {metrics.UniqueLengthMetres:F0},",
            $"    \"plannedConnections\": {metrics.PlannedConnections},",
            $"    \"placesServed\": {metrics.PlacesServed},",
            $"    \"joinedGroups\": {metrics.Components},",
            $"    \"teeJunctions\": {metrics.TeeJunctions},",
            $"    \"endToEndJoins\": {metrics.EndToEndJoins},",
            $"    \"parallelRoadMeters\": {metrics.ParallelMetres:F0},",
            $"    \"fallbacksToRoad\": {RoadNetworkGenerator.FallbacksToRoad},",
            $"    \"fallbacksToPlace\": {RoadNetworkGenerator.FallbacksToPlace},",
            $"    \"attemptCount\": {attempts.Count},",
            $"    \"failedAttemptCount\": {attempts.Count(a => !a.Connected)},",
            $"    \"routingProbes\": {RoadNetworkGenerator.RoutingProbes},",
            $"    \"routingProbesWithoutRoute\": {RoadNetworkGenerator.RoutingProbesWithoutRoute},",
            $"    \"crossingCount\": {sites.Count},",
            $"    \"bridgeCount\": {sites.Count(c => c.Kind == CrossingKind.Bridge)},",
            $"    \"roadNetworkVersion\": {RoadSpatialGrid.RoadNetworkVersion},",
            // Rows, connections and searches are three different counts and
            // the study needs all three: one plan writes two rows for one
            // connection, and a priced plan runs searches that write no row
            // at all.
            $"    \"connectionCount\": {RoadAttemptLog.ConnectionCount},",
            $"    \"buildSearches\": {attempts.Count},",
            $"    \"planningSearches\": {RoadNetworkGenerator.RoutingProbes},",
            $"    \"totalSearches\": {attempts.Count + RoadNetworkGenerator.RoutingProbes},",
            $"    \"expandedCells\": {attempts.Sum(a => (long)a.UniqueExpandedCells)},",
            $"    \"islandsWithRoads\": {RoadIslandLog.Entries.Count(i => i.Roads > 0)},",
            $"    \"seconds\": {elapsed.TotalSeconds:F1},",
            $"    \"loadSeconds\": {loadElapsed.TotalSeconds:F1},",
            $"    \"generateSeconds\": {elapsed.TotalSeconds:F2},",
            $"    \"analysisSeconds\": {analysisElapsed.TotalSeconds:F2},",
            $"    \"exportSeconds\": {exportElapsed.TotalSeconds:F2}",
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

    public static bool Has(string[] args, string name)
    {
        foreach (string arg in args)
            if (arg == name)
                return true;
        return false;
    }
}
