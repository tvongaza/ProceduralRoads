using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Dumps the world as the road generator sees it — a height / biome / river
/// grid over the whole map and every placed location — so the road
/// distribution can be judged on paper (scripts/world-svg.py renders it).
/// Written to the config folder next to the selftest report:
/// ProceduralRoads.world.csv (x,z,height,biome,river) and
/// ProceduralRoads.locations.csv (name,x,z,radius).
/// </summary>
public static class WorldDump
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    public const float HalfExtent = 10000f;
    public const int DefaultStep = 50;

    public static string WorldCsvPath => Path.Combine(Paths.ConfigPath, "ProceduralRoads.world.csv");
    public static string LocationsCsvPath => Path.Combine(Paths.ConfigPath, "ProceduralRoads.locations.csv");

    /// <summary>Samples the grid and writes both files. Returns the sample count, or -1 without a world.</summary>
    public static int Write(int step = DefaultStep)
    {
        WorldGenerator world = WorldGenerator.instance;
        if (world == null)
        {
            Log.LogWarning("[WORLDDUMP] WorldGenerator unavailable");
            return -1;
        }
        step = Mathf.Max(5, step);

        System.DateTime start = System.DateTime.Now;
        int samples = 0;
        var inv = CultureInfo.InvariantCulture;
        using (var w = new StreamWriter(WorldCsvPath, false, new UTF8Encoding(false)))
        {
            w.WriteLine("x,z,height,biome,river");
            for (float z = -HalfExtent; z <= HalfExtent; z += step)
            {
                for (float x = -HalfExtent; x <= HalfExtent; x += step)
                {
                    float h = BiomeBlendedHeight.GetBlendedHeight(x, z, world);
                    Heightmap.Biome biome = world.GetBiome(x, z);
                    world.GetRiverWeight(x, z, out float river, out _);
                    w.Write(x.ToString("F0", inv)); w.Write(',');
                    w.Write(z.ToString("F0", inv)); w.Write(',');
                    w.Write(h.ToString("F1", inv)); w.Write(',');
                    w.Write(biome.ToString()); w.Write(',');
                    w.WriteLine(river.ToString("F2", inv));
                    samples++;
                }
            }
        }

        int locations = 0;
        using (var w = new StreamWriter(LocationsCsvPath, false, new UTF8Encoding(false)))
        {
            w.WriteLine("name,x,z,radius");
            var list = ZoneSystem.instance != null ? ZoneSystem.instance.GetLocationList() : null;
            if (list != null)
            {
                foreach (var loc in list)
                {
                    string name = loc.m_location.m_prefab.Name;
                    float radius = RoadNetworkGenerator.ApproachRadius(name, loc.m_location.m_exteriorRadius);
                    w.WriteLine($"{name},{loc.m_position.x.ToString("F1", inv)},{loc.m_position.z.ToString("F1", inv)},{radius.ToString("F1", inv)}");
                    locations++;
                }
            }
        }

        Log.LogInfo($"[WORLDDUMP] {samples} samples at {step} m, {locations} locations, " +
                    $"{(System.DateTime.Now - start).TotalMilliseconds:F0} ms -> {WorldCsvPath}");
        return samples;
    }
}
