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

    /// <summary>
    /// Dumps snap points (children tagged "snappoint") and combined collider
    /// bounds for a prefab, in prefab-local space. The numbers feed the
    /// PieceGeometry constants used by the snap-chained layout solvers; this
    /// probe stays as the tool that re-verifies them after game updates.
    /// </summary>
    public static void ProbeSnapPoints(string prefabName, System.Action<string> addOutput)
    {
        if (ZNetScene.instance == null)
        {
            addOutput("ERROR: ZNetScene is not ready");
            return;
        }

        UnityEngine.GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            addOutput($"[SNAP] prefab={prefabName} MISSING");
            return;
        }

        int snapIndex = 0;
        foreach (UnityEngine.Transform child in prefab.GetComponentsInChildren<UnityEngine.Transform>(true))
        {
            if (!child.CompareTag("snappoint"))
                continue;
            UnityEngine.Vector3 local = prefab.transform.InverseTransformPoint(child.position);
            addOutput($"[SNAP] prefab={prefabName} snap={snapIndex} name={child.name} local=({local.x:F3},{local.y:F3},{local.z:F3})");
            snapIndex++;
        }

        UnityEngine.Collider[] colliders = prefab.GetComponentsInChildren<UnityEngine.Collider>(true);
        if (colliders.Length > 0)
        {
            UnityEngine.Bounds bounds = colliders[0].bounds;
            foreach (UnityEngine.Collider collider in colliders)
                bounds.Encapsulate(collider.bounds);
            UnityEngine.Vector3 min = prefab.transform.InverseTransformPoint(bounds.min);
            UnityEngine.Vector3 max = prefab.transform.InverseTransformPoint(bounds.max);
            addOutput($"[SNAP] prefab={prefabName} snaps={snapIndex} colliders={colliders.Length} boundsMin=({min.x:F3},{min.y:F3},{min.z:F3}) boundsMax=({max.x:F3},{max.y:F3},{max.z:F3})");
        }
        else
        {
            addOutput($"[SNAP] prefab={prefabName} snaps={snapIndex} colliders=0");
        }
    }
}
