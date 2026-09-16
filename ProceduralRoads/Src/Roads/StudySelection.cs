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
            // is a biome, not a kind: it put a dvergr town entrance, a
            // harbour, a guard tower and a ruined guard tower in the same
            // bucket while the same four kinds elsewhere were kept apart. A
            // quota shared across categories then gave the whole Mistlands a
            // single share and, inside it, no say in which kind it got.
            //
            // The generator's own priority table is the evidence for most of
            // these: DvergrTownEntrance scores 75, exactly Crypt3's; the
            // ruined guard towers score 30, exactly StoneTowerRuins'.
            case "Crypt3":
            case "Crypt4":
            case "SunkenCrypt4":
            case "MountainCave02":
            case "TrollCave02":
                return "dungeon";

            case "WoodVillage1":
            case "WoodFarm1":
            case "SwampHut5":
            // Dvergr settlements, not dungeons. An earlier version read the
            // priority table as evidence of KIND - DvergrTownEntrance scores
            // 75, the same as Crypt3 - which it is not: priority ranks how
            // worth a road a place is, and says nothing about what stands
            // there. Corrected on the word of someone who has played it.
            case "Mistlands_DvergrTownEntrance1":
            case "Mistlands_DvergrTownEntrance2":
            case "Mistlands_Harbour1":
            // Inspected, not derived: a lighthouse and a dvergr excavation are
            // built, occupied sites rather than ruins, but the table prices
            // them with the towers (50 and 45). Called settlements here; the
            // call is worth revisiting with someone who has played Mistlands.
            case "Mistlands_Lighthouse1_new":
            case "Mistlands_Excavation1":
            case "Mistlands_Excavation2":
            case "Mistlands_Excavation3":
                return "settlement";
        }
        // Everything else the table makes eligible is a tower, ruin or
        // standing stone - including the Mistlands guard towers, intact (50,
        // as StoneTower) and ruined (30, as StoneTowerRuins).
        return "ruin";
    }

    /// <summary>The categories a quota is shared across, bosses excluded: they are taken first and always.</summary>
    public static readonly string[] SharedCategories = { "dungeon", "settlement", "ruin" };

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
