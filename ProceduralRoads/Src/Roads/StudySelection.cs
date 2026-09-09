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
