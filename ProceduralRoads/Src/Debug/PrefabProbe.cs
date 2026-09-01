using System.Collections.Generic;
using BepInEx.Logging;

namespace ProceduralRoads;

/// <summary>
/// Verifies that the prefab names the ruin kits reference actually exist in
/// the running game (names cannot be checked at compile time — they live in
/// asset bundles). Logs a grep-able [PREFABS] report; runs once per world
/// when DebugValidation is on, so the headless validation loop answers the
/// question with no decompiler and no manual inspection.
/// </summary>
public static class PrefabProbe
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;
    private static bool s_probed;

    /// <summary>Every prefab any kit references, plus likely alternates to
    /// disambiguate naming (e.g. stone_stair vs stone_stairs).</summary>
    private static readonly string[] Candidates =
    {
        // Meadows wood kit
        "wood_pole2", "wood_pole", "wood_floor", "wood_stair", "wood_beam",
        // Stone kit
        "stone_stair", "stone_stairs", "stone_wall_1x1", "stone_wall_2x1",
        "stone_wall_4x2", "stone_floor_2x2", "stone_floor", "stone_arch", "stone_pile",
        // Mistlands (dvergr) later
        "blackmarble_stair", "dvergrprops_stairs", "blackmarble_1x1",
        // Candidates harvested from the shipped game data's localization
        // tokens (2026-09-01). Those tokens are NOT prefab names -- the
        // mapping between them is not consistent ($piece_woodstair vs the
        // wood_stair prefab) -- so these are guesses to be settled by this
        // probe at runtime, which is the only authoritative source. Added
        // because the Mistlands kit still has no confirmed step prefab:
        // "dvergrprops_stairs" is a miss, and StairLayout has no dvergr
        // kit as a result.
        "dvergr_spiralstair", "dvergr_spiralstair_right",
        "dvergrprops_stair", "dvergrprops_spiralstair",
        // Corner pieces. Worth knowing whether these exist because the
        // 2026-09-01 visual assessment found stairs breaking into sawtooth
        // gaps at heading changes; a real corner piece is a better answer
        // than filling the wedge with blocks.
        "blackmarble_stair_corner_left", "blackmarble_stair_corner_right",
        "stone_stair_corner", "wood_stair_corner",
        // Other stair families seen in the same token table, in case a kit
        // wants them: grausten (Ashlands) and ashwood.
        "grausten_stair", "ashwood_stair", "ashwoodstair",
    };

    public static void Reset() => s_probed = false;

    public static void ProbeOnce()
    {
        if (s_probed || ZNetScene.instance == null)
            return;
        s_probed = true;

        List<string> found = new(), missing = new();
        foreach (string name in Candidates)
        {
            if (ZNetScene.instance.GetPrefab(name) != null)
                found.Add(name);
            else
                missing.Add(name);
        }

        Log.LogInfo($"[PREFABS] found: {string.Join(", ", found)}");
        if (missing.Count > 0)
            Log.LogInfo($"[PREFABS] missing: {string.Join(", ", missing)}");
    }
}
