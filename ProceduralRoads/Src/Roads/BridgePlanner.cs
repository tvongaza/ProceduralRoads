using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Which plan source a bridge crossing gets (config "Bridges/Kit").</summary>
public enum BridgeKit
{
    /// <summary>BridgeLayout's station solver (the default; the ruins the
    /// mod has always produced).</summary>
    Solver,
    /// <summary>The wood-bridge blueprint kit: 2 m plank spans on post pairs.</summary>
    Wood,
    /// <summary>The stone-arch kit: 4 m arch bays on double-wide piers.</summary>
    StoneArch,
    /// <summary>The hybrid kit: stone piers every 4 m under a 4 m wood deck.</summary>
    Hybrid,
    /// <summary>A kit per biome, the way the solver picks its style: stone
    /// arches where it would build stone, wood elsewhere.</summary>
    ByBiome,
}

/// <summary>
/// The blueprint kits: START / SPAN / END units per kit, shipped inside the
/// mod as embedded resources (blueprints/&lt;prefix&gt;-{start,span,end}.blueprint)
/// and overridable by files of the same names in <see cref="OverrideDirectory"/>
/// (BepInEx/config/ProceduralRoads/blueprints), so a player's own build —
/// saved with valheimCreative or PlanBuild — can replace a unit.
/// </summary>
public static class BridgeKits
{
    public static string? OverrideDirectory;

    private static readonly Dictionary<string, (RoadBlueprint start, RoadBlueprint span, RoadBlueprint end)> s_cache = new();

    public static string Prefix(BridgeKit kit) => kit switch
    {
        BridgeKit.Wood => "wood-bridge",
        BridgeKit.StoneArch => "stone-arch",
        BridgeKit.Hybrid => "hybrid",
        _ => throw new ArgumentException("not a kit: " + kit),
    };

    /// <summary>The style whose prefabs the kit is built from: what the
    /// support model measures pieces by.</summary>
    public static BridgeStyle StyleOf(BridgeKit kit) => kit switch
    {
        BridgeKit.Wood => BridgeStyle.MeadowsWood,
        BridgeKit.StoneArch => BridgeStyle.MountainStone,
        BridgeKit.Hybrid => BridgeStyle.HybridStoneWood,
        _ => throw new ArgumentException("not a kit: " + kit),
    };

    /// <summary>The kit a biome gets under ByBiome: stone arches where the
    /// solver builds stone, wood where it builds wood.</summary>
    public static BridgeKit ForBiome(Heightmap.Biome biome) =>
        BridgeLayout.StyleFor(biome).DeckPrefab == BridgeStyle.MountainStone.DeckPrefab ? BridgeKit.StoneArch : BridgeKit.Wood;

    public static (RoadBlueprint start, RoadBlueprint span, RoadBlueprint end) Load(BridgeKit kit)
    {
        string prefix = Prefix(kit);
        string key = (OverrideDirectory ?? "") + "|" + prefix;
        if (s_cache.TryGetValue(key, out var cached))
            return cached;
        var units = (Unit(prefix, "start"), Unit(prefix, "span"), Unit(prefix, "end"));
        s_cache[key] = units;
        return units;
    }

    public static void ClearCache() => s_cache.Clear();

    private static RoadBlueprint Unit(string prefix, string unit)
    {
        string file = prefix + "-" + unit + ".blueprint";
        if (!string.IsNullOrEmpty(OverrideDirectory))
        {
            string path = Path.Combine(OverrideDirectory, file);
            if (File.Exists(path))
            {
                RoadBlueprint bp = RoadBlueprint.Parse(File.ReadAllText(path));
                string sidecar = Path.Combine(OverrideDirectory, "blueprint-metadata.json");
                if (File.Exists(sidecar))
                    bp.LoadYOffset = RoadBlueprint.ReadLoadYOffset(File.ReadAllText(sidecar), file);
                return bp;
            }
        }
        using Stream stream = typeof(BridgeKits).Assembly.GetManifestResourceStream("blueprints/" + file)
            ?? throw new FileNotFoundException("embedded blueprint missing: " + file);
        using StreamReader reader = new(stream);
        return RoadBlueprint.Parse(reader.ReadToEnd());
    }
}

/// <summary>
/// One entry point for a crossing's plan: the solver, or a blueprint kit
/// composed, grounded and weathered — chosen by config, the solver by
/// default so worlds keep the ruins they had. Fords always go to the
/// solver (wade and raise are road; a span is its short footbridge), and
/// a Mistlands bridge gets nothing from either (Tys, 3 Sep 2026).
/// </summary>
public static class BridgePlanner
{
    /// <summary>Player-facing lever (config "Bridges/Kit"). Set at config read.</summary>
    public static BridgeKit ConfiguredKit = BridgeKit.Solver;

    public static List<BridgePiece> Plan(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Plan(crossing, world, worldSeed, ConfiguredKit);

    public static List<BridgePiece> Plan(RoadCrossing crossing, WorldGenerator world, int worldSeed, BridgeKit kit)
    {
        if (kit == BridgeKit.Solver || crossing.Kind != CrossingKind.Bridge)
            return BridgeLayout.Solve(crossing, world, worldSeed, BridgeLayout.StyleFor(crossing.Biome));
        if (BridgeLayout.TouchesMistlands(crossing, world))
            return new List<BridgePiece>();
        if (kit == BridgeKit.ByBiome)
            kit = BridgeKits.ForBiome(crossing.Biome);

        (RoadBlueprint start, RoadBlueprint span, RoadBlueprint end) = BridgeKits.Load(kit);
        if (crossing.Width < start.Length + end.Length)
            return new List<BridgePiece>();
        BridgeStyle style = BridgeKits.StyleOf(kit);
        List<BridgePiece> full = BlueprintComposer.GroundPosts(BlueprintComposer.Tile(crossing, world, style, start, span, end), world, style);
        return BlueprintComposer.Weather(full, crossing, style, world, worldSeed ^ BridgeLayout.StableSeed(crossing));
    }
}
