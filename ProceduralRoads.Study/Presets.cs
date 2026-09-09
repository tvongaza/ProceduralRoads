using System;
using System.Collections.Generic;
using System.Linq;

namespace ProceduralRoads.Study;

/// <summary>
/// The selection presets the study compares: three answers to "which places
/// should a road network serve at all", written as configuration a reader can
/// argue with rather than as code.
///
/// They are deliberately coarse. The point is not to propose one but to show
/// what the choice is worth, because on these worlds the choice of destinations
/// decides far more than the choice of route between them.
/// </summary>
internal static class Presets
{
    private static readonly string[] Bosses =
    {
        "Eikthyrnir", "GDKing", "Bonemass", "Dragonqueen", "GoblinKing",
        "Mistlands_DvergrBossEntrance",
    };

    private static readonly string[] Dungeons =
    {
        "Crypt2", "Crypt3", "Crypt4", "SunkenCrypt4", "MountainCave02", "TrollCave02",
        "Mistlands_DvergrTownEntrance1", "Mistlands_DvergrTownEntrance2",
    };

    /// <summary>Places a player builds near or trades at, rather than raids.</summary>
    private static readonly string[] Settlements =
    {
        "WoodVillage1", "WoodFarm1", "WoodHouse1", "StoneTower1", "StoneTower3",
        "Mistlands_Harbour1", "Mistlands_GuardTower1_new", "Mistlands_GuardTower2_new",
        "Mistlands_GuardTower3_new", "Vendor_BlackForest", "Hildir_camp",
        "Hildir_plainsfortress", "Hildir_cave", "Hildir_crypt", "Ruin1", "Ruin2",
        "StoneHouse3", "StoneHouse4", "Dolmen01", "Dolmen02", "Dolmen03",
    };

    public static bool Apply(string name)
    {
        StudySelection.Reset();
        switch (name)
        {
            case "shipped":
                return true;

            case "bosses":
                // Nothing but the five bosses and the spawn: the smallest
                // network anyone could call a network.
                StudySelection.Enabled = true;
                StudySelection.PresetName = name;
                foreach (string boss in Bosses)
                    StudySelection.Required.Add(boss);
                StudySelection.DefaultFrequency = 0f;
                return true;

            case "bosses-dungeons":
                // The bosses, and half the dungeons - enough to make a network
                // without wiring up every crypt in the world.
                StudySelection.Enabled = true;
                StudySelection.PresetName = name;
                foreach (string boss in Bosses)
                    StudySelection.Required.Add(boss);
                foreach (string dungeon in Dungeons)
                    StudySelection.Frequency[dungeon] = 0.5f;
                StudySelection.DefaultFrequency = 0f;
                return true;

            case "exploration":
                // Bosses required, dungeons thinned, and the places people
                // actually live raised to compete: today they sit at 25-60 and
                // never win a quota slot.
                StudySelection.Enabled = true;
                StudySelection.PresetName = name;
                foreach (string boss in Bosses)
                    StudySelection.Required.Add(boss);
                foreach (string dungeon in Dungeons)
                    StudySelection.Frequency[dungeon] = 0.25f;
                foreach (string settlement in Settlements)
                {
                    StudySelection.Frequency[settlement] = 1f;
                    StudySelection.PriorityOverrides[settlement] = 85;
                }
                StudySelection.DefaultFrequency = 0.1f;
                return true;

            default:
                Console.Error.WriteLine(
                    $"unknown preset '{name}' (shipped, bosses, bosses-dungeons, exploration)");
                return false;
        }
    }

    public static string Describe() =>
        StudySelection.Enabled
            ? $"{StudySelection.PresetName}: {StudySelection.Required.Count} required name(s), " +
              $"{StudySelection.Frequency.Count} with their own odds, " +
              $"{StudySelection.PriorityOverrides.Count} re-prioritised, " +
              $"everything else at {StudySelection.DefaultFrequency:P0}"
            : "shipped: the built-in priority table decides";
}
