using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Writes one generation run to disk so it can be measured, mapped and
/// compared without loading the world again: the road centrelines, the river
/// crossings, and a manifest naming what produced them.
///
/// The manifest carries the settings as the plugin holds them AFTER BepInEx
/// has parsed and clamped the file, which is the only honest record of what
/// the run actually used.
///
/// Study instrument (branch study/road-network-strategies). Not part of any PR.
/// </summary>
public static class RoadStudyExport
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    public static string DefaultDirectory => Paths.ConfigPath;

    public static string RoutesPath(string dir) => Path.Combine(dir, "ProceduralRoads.routes.csv");
    public static string CrossingsPath(string dir) => Path.Combine(dir, "ProceduralRoads.crossings.csv");
    public static string ManifestPath(string dir) => Path.Combine(dir, "ProceduralRoads.manifest.json");

    /// <summary>Writes all three files. Returns the paths written, or null on failure.</summary>
    public static List<string>? WriteAll(string dir, string scope)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(RoutesPath(dir), RoadRouteRecorder.ToCsv());
            File.WriteAllText(CrossingsPath(dir), CrossingsCsv(RoadNetworkGenerator.GetRoadCrossings()));
            File.WriteAllText(ManifestPath(dir), Manifest(scope));
            return new List<string> { RoutesPath(dir), CrossingsPath(dir), ManifestPath(dir) };
        }
        catch (IOException e)
        {
            Log.LogWarning($"[STUDY] Could not write export files: {e.Message}");
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            Log.LogWarning($"[STUDY] Could not write export files: {e.Message}");
            return null;
        }
    }

    public static string CrossingsCsv(IReadOnlyList<RoadCrossing> crossings)
    {
        StringBuilder sb = new();
        sb.Append("index,kind,style,center_x,center_z,from_bank_x,from_bank_z,to_bank_x,to_bank_z,")
          .Append("width,water_level,riverbed_height,depth,fairway_width\n");
        for (int i = 0; i < crossings.Count; i++)
        {
            RoadCrossing c = crossings[i];
            sb.Append(i).Append(',')
              .Append(c.Kind).Append(',')
              .Append(c.Style).Append(',')
              .Append(c.Center.x.ToString("F1")).Append(',')
              .Append(c.Center.y.ToString("F1")).Append(',')
              .Append(c.FromBank.x.ToString("F1")).Append(',')
              .Append(c.FromBank.y.ToString("F1")).Append(',')
              .Append(c.ToBank.x.ToString("F1")).Append(',')
              .Append(c.ToBank.y.ToString("F1")).Append(',')
              .Append(c.Width.ToString("F1")).Append(',')
              .Append(c.WaterLevel.ToString("F2")).Append(',')
              .Append(c.RiverbedHeight.ToString("F2")).Append(',')
              .Append((c.WaterLevel - c.RiverbedHeight).ToString("F2")).Append(',')
              .Append(c.FairwayWidth.ToString("F1")).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// What produced this run. Settings are read back from the plugin's config
    /// entries, so a value BepInEx clamped is reported as the clamped value.
    /// </summary>
    public static string Manifest(string scope)
    {
        IReadOnlyList<RoadRoute> routes = RoadRouteRecorder.Routes;
        IReadOnlyList<RoadCrossing> crossings = RoadNetworkGenerator.GetRoadCrossings();

        float totalLength = 0f;
        int pointCount = 0;
        foreach (RoadRoute route in routes)
        {
            totalLength += route.Length;
            pointCount += route.Points.Count;
        }

        int fords = 0, bridges = 0;
        foreach (RoadCrossing c in crossings)
        {
            if (c.Kind == CrossingKind.Bridge) bridges++;
            else fords++;
        }

        StringBuilder sb = new();
        sb.Append("{\n");
        sb.Append($"  \"modVersion\": \"{ProceduralRoadsPlugin.ModVersion}\",\n");
        sb.Append($"  \"gameVersion\": \"{Escape(GameVersion())}\",\n");
        sb.Append($"  \"worldName\": \"{Escape(WorldName())}\",\n");
        sb.Append($"  \"worldSeed\": {WorldGenerator.instance?.GetSeed() ?? 0},\n");
        sb.Append($"  \"scope\": \"{Escape(scope)}\",\n");
        sb.Append($"  \"generatedUtc\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",\n");
        sb.Append("  \"config\": {\n");
        sb.Append($"    \"RoadWidth\": {ProceduralRoadsPlugin.RoadWidth.Value},\n");
        sb.Append($"    \"IslandRoadPercentage\": {ProceduralRoadsPlugin.IslandRoadPercentage.Value},\n");
        sb.Append($"    \"MaxLocationsPerIsland\": {ProceduralRoadsPlugin.MaxLocationsPerIsland.Value},\n");
        sb.Append($"    \"PathfindingMaxIterations\": {ProceduralRoadsPlugin.PathfindingMaxIterations.Value},\n");
        sb.Append($"    \"CustomLocations\": \"{Escape(ProceduralRoadsPlugin.CustomLocations.Value)}\",\n");
        sb.Append($"    \"FordsEnabled\": {Bool(ProceduralRoadsPlugin.FordsEnabled.Value)},\n");
        sb.Append($"    \"FordWadeWeight\": {ProceduralRoadsPlugin.FordWadeWeight.Value},\n");
        sb.Append($"    \"FordRaiseWeight\": {ProceduralRoadsPlugin.FordRaiseWeight.Value},\n");
        sb.Append($"    \"FordSpanWeight\": {ProceduralRoadsPlugin.FordSpanWeight.Value},\n");
        sb.Append($"    \"BridgesEnabled\": {Bool(ProceduralRoadsPlugin.BridgesEnabled.Value)},\n");
        sb.Append($"    \"BridgeCostFixed\": {ProceduralRoadsPlugin.BridgeCostFixed.Value},\n");
        sb.Append($"    \"BridgeCostPerMeter\": {ProceduralRoadsPlugin.BridgeCostPerMeter.Value}\n");
        sb.Append("  },\n");
        sb.Append("  \"result\": {\n");
        sb.Append($"    \"routeCount\": {routes.Count},\n");
        sb.Append($"    \"pointCount\": {pointCount},\n");
        sb.Append($"    \"totalLengthMeters\": {totalLength:F0},\n");
        sb.Append($"    \"crossingCount\": {crossings.Count},\n");
        sb.Append($"    \"fordCount\": {fords},\n");
        sb.Append($"    \"bridgeCount\": {bridges},\n");
        sb.Append($"    \"roadNetworkVersion\": {RoadSpatialGrid.RoadNetworkVersion},\n");
        sb.Append($"    \"totalRoadPoints\": {RoadSpatialGrid.TotalRoadPoints}\n");
        sb.Append("  }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string GameVersion()
    {
        try { return global::Version.GetVersionString(); }
        catch (Exception) { return "unknown"; }
    }

    private static string WorldName()
    {
        try { return ZNet.instance?.GetWorldName() ?? ""; }
        catch (Exception) { return ""; }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
