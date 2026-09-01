using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace ProceduralRoads;

/// <summary>
/// Game-side wrapper around RoadNetworkValidator: runs the checks against
/// the live WorldGenerator, logs a grep-friendly summary, and writes a JSON
/// report plus a routes CSV so results can be inspected without loading the
/// game again (or the world at all).
/// </summary>
public static class RoadValidationRunner
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    public static string ReportPath => Path.Combine(Paths.ConfigPath, "ProceduralRoads.selftest.json");
    public static string RoutesCsvPath => Path.Combine(Paths.ConfigPath, "ProceduralRoads.routes.csv");

    /// <summary>Runs after generation when the DebugValidation config is on.</summary>
    public static void MaybeRunAfterGeneration()
    {
        if (ProceduralRoadsPlugin.DebugValidation == null || !ProceduralRoadsPlugin.DebugValidation.Value)
            return;
        Run();
    }

    public static RoadNetworkValidator.Report? Run()
    {
        if (WorldGenerator.instance == null)
        {
            Log.LogWarning("[SELFTEST] WorldGenerator unavailable; cannot validate");
            return null;
        }

        var routes = RoadNetworkGenerator.GetRoadRoutes();
        System.DateTime validateStart = System.DateTime.Now;
        var report = RoadNetworkValidator.Validate(routes, WorldGenerator.instance,
            RoadNetworkGenerator.GetStairRuns());
        Log.LogInfo(
            $"[TIMING] validator ms={(System.DateTime.Now - validateStart).TotalMilliseconds:F0} " +
            $"pathfinderIterations={RoadPathfinder.TotalIterations} terrainSamples={RoadPathfinder.TotalTerrainSamples}");

        Log.LogInfo(
            $"[SELFTEST] {(report.Passed ? "PASS" : "FAIL")}: {report.RouteCount} routes, " +
            $"{report.TotalLengthMeters:F0}m total, {report.NetworkComponents} network component(s), " +
            $"{report.FordCount} ford(s), {RoadNetworkGenerator.GetRoadCrossings().Count} crossing(s), " +
            $"{RoadNetworkGenerator.GetStairRuns().Count} stair run(s), " +
            $"hash {report.PointsHash}, {report.Violations.Count} violation(s)");

        foreach (string violation in report.Violations)
            Log.LogWarning($"[SELFTEST] VIOLATION {violation}");

        try
        {
            File.WriteAllText(ReportPath, RoadNetworkValidator.ToJson(report));
            File.WriteAllText(RoutesCsvPath, RoadNetworkValidator.ToRoutesCsv(routes));
            Log.LogInfo($"[SELFTEST] Report: {ReportPath}");
            Log.LogInfo($"[SELFTEST] Routes CSV: {RoutesCsvPath}");
        }
        catch (IOException e)
        {
            Log.LogWarning($"[SELFTEST] Could not write report files: {e.Message}");
        }

        return report;
    }
}
