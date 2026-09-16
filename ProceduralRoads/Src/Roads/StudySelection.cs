using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Which places are candidates for a road at all, before any quota is applied.
///
/// Three controls, kept apart because they answer different questions:
///
/// * <b>Required</b>: the place must be selected. It beats the island's count,
///   so three required places on an island whose quota is two gives three, and
///   the run says why.
/// * <b>Frequency</b>: whether an optional place enters the pool this world at
///   all - dungeons half the time, say. The draw is deterministic from the
///   world seed and the place's own identity, so it is the same on every
///   generation of that world and changing one threshold does not reroll the
///   rest. A place that lost its draw is a different outcome from one that
///   lost the quota.
/// * <b>Priority</b>: which of the remaining candidates fill the quota. It
///   never changes a search's budget.
///
/// Identity is the place's name and its position rounded to a metre, never its
/// index in the location list: inserting or reordering locations, which any
/// content mod does, would otherwise reroll every destination in the world.
///
/// Study instrument (branch study/road-network-strategies). Not part of any PR.
/// </summary>
public static class StudySelection
{
    /// <summary>
    /// What a place is, for a rule that shares a quota across categories.
    ///
    /// One definition, here, because the study's reporting and the selection
    /// rule must agree: a category counted in the results that the selector
    /// never knew about would make a balanced run look unbalanced. The names
    /// are the generator's own priority-table entries, not a name pattern,
    /// except Mistlands, which the table itself matches by prefix.
    /// </summary>
    public static string Category(string name)
    {
        if (RoadNetworkGenerator.IsBossLocation(name)) return "boss";
        switch (name)
        {
            // What a place IS, never which biome it stands in. An earlier
            // version folded every "Mistlands_" name into one category, which
            // is a biome, not a kind.
            //
            // The Infested Mine entrances are dungeons. They were called
            // settlements here on 16 September 2026, on Tys's word, and he
            // reversed that later the same day after the 1.0.12 identity
            // review: the shell is a dvergr town, the content is a seeker
            // dungeon, and the dungeon is what a road is for. Both readings
            // were his; this is the one that stands.
            case "BearCave":
            case "Crypt2":
            case "Crypt3":
            case "Crypt4":
            case "Mistlands_DvergrTownEntrance1":
            case "Mistlands_DvergrTownEntrance2":
            case "MountainCave02":
            case "SunkenCrypt4":
            case "TrollCave02":
                return "dungeon";

            // Hildir's three quest sites. Not unique-selected - ordinary
            // placement, three each - so they carry none of the trader
            // problem.
            case "Hildir_cave":
            case "Hildir_crypt":
            case "Hildir_plainsfortress":
                return "quest-dungeon";

            // Places a player builds near, trades at or raids for supplies,
            // rather than a dungeon. The intact dvergr guard towers are here
            // and the ruined ones are not: occupied is the distinction, not
            // the prefab family.
            case "GoblinCamp2":
            case "GoblinCamp2_1":
            case "GoblinHut01":
            case "GoblinHut02":
            case "GoblinHut03":
            case "Mistlands_Excavation1":
            case "Mistlands_Excavation2":
            case "Mistlands_GuardTower1_new":
            case "Mistlands_GuardTower2_new":
            case "Mistlands_GuardTower3_new":
            case "Mistlands_Harbour1":
            case "Mistlands_Lighthouse1_new":
            case "StoneTower1":
            case "StoneTower3":
            case "WoodFarm1":
            case "WoodVillage1":
            case "WoodVillage2":
                return "settlement";

            // Standing stones and set-piece encounters: neither a building
            // to live by nor a ruin to pick over.
            case "CombatRuin01":
            case "StoneHenge1":
            case "StoneHenge2":
            case "StoneHenge3":
            case "StoneHenge4":
            case "StoneHenge5":
                return "encounter";
        }
        // Everything else the table makes eligible is a tower, ruin or
        // standing stone.
        return "ruin";
    }

    /// <summary>The categories a quota is shared across, bosses excluded: they are taken first and always.</summary>
    public static readonly string[] SharedCategories = { "dungeon", "quest-dungeon", "settlement", "ruin", "encounter" };

    /// <summary>Off by default: the shipped rules decide everything.</summary>
    public static bool Enabled;

    public static string PresetName = "shipped";

    /// <summary>Names that must be selected wherever they occur.</summary>
    public static readonly HashSet<string> Required = new();

    /// <summary>Odds that an optional place enters the pool, by name.</summary>
    public static readonly Dictionary<string, float> Frequency = new();

    /// <summary>Odds for a name with no entry of its own.</summary>
    public static float DefaultFrequency = 1f;

    /// <summary>Priorities that replace the built-in table, by name.</summary>
    public static readonly Dictionary<string, int> PriorityOverrides = new();

    public static void Reset()
    {
        Enabled = false;
        PresetName = "shipped";
        Required.Clear();
        Frequency.Clear();
        PriorityOverrides.Clear();
        DefaultFrequency = 1f;
    }

    /// <summary>A priority override for a name, if the preset sets one.</summary>
    public static bool TryGetPriority(string name, out int priority)
    {
        priority = 0;
        return Enabled && PriorityOverrides.TryGetValue(name, out priority);
    }

    /// <summary>
    /// The candidates a place list offers this world: everything required, plus
    /// the optional places that won their draw.
    /// </summary>
    public static List<(string name, Vector3 position, float radius)> Candidates(
        int worldSeed, List<(string name, Vector3 position, float radius)> places)
    {
        if (!Enabled)
            return places;

        List<(string name, Vector3 position, float radius)> pool = new();
        foreach ((string name, Vector3 position, float radius) place in places)
        {
            if (Required.Contains(place.name))
            {
                pool.Add(place);
                continue;
            }

            float odds = Frequency.TryGetValue(place.name, out float named) ? named : DefaultFrequency;
            if (odds >= 1f)
                pool.Add(place);
            else if (odds > 0f && Draw(worldSeed, place) < odds)
                pool.Add(place);
        }

        return pool;
    }

    /// <summary>Whether a place must be selected however small the quota.</summary>
    public static bool IsRequired(string name) => Enabled && Required.Contains(name);

    /// <summary>
    /// A number in [0,1) fixed by the world and the place, so the same world
    /// draws the same places every time and a change to one threshold leaves
    /// every other place where it was.
    /// </summary>
    private static float Draw(int worldSeed, (string name, Vector3 position, float radius) place)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in place.name)
                hash = (hash ^ c) * 16777619;
            hash = (hash ^ (uint)Mathf.RoundToInt(place.position.x)) * 16777619;
            hash = (hash ^ (uint)Mathf.RoundToInt(place.position.z)) * 16777619;
            hash = (hash ^ (uint)worldSeed) * 16777619;
            return (hash % 100000u) / 100000f;
        }
    }
}
