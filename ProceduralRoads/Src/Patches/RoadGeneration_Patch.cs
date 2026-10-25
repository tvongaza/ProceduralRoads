using HarmonyLib;

namespace ProceduralRoads;

/// <summary>Keep the local host on its existing loading screen while the owned initial network is built.</summary>
[HarmonyPatch]
internal static class RoadGeneration_Patch
{
    [HarmonyPatch(typeof(Game), "UpdateRespawn")]
    [HarmonyPrefix]
    private static bool BeforeRespawn() => !RoadLifecycleManager.InitialGenerationActive;

    // Shutdown destroys ZNetScene before ZoneSystem.OnDestroy. Drain while the world's managed terrain and caches still exist.
    [HarmonyPatch(typeof(Game), "Shutdown")]
    [HarmonyPrefix]
    private static void BeforeShutdown() => RoadLifecycleManager.CancelAndDrain();

    [HarmonyPatch(typeof(Hud), "UpdateProgressIndicator")]
    [HarmonyPrefix]
    private static bool UpdateProgress(Hud __instance, ref bool ___m_progressIndicatorShown)
    {
        var progress = RoadLifecycleManager.Progress;
        if (!RoadLifecycleManager.InitialGenerationActive) return true;
        var indicator = __instance.m_loadingIndicator;
        if (indicator == null) return true;
        // Vanilla owns this field too. Leaving it true lets its next ordinary update hide/restore the indicator on release.
        ___m_progressIndicatorShown = true;
        indicator.SetShowProgress(true);
        indicator.SetProgress(progress.Total == 0 ? 0f : (float)progress.Completed / progress.Total);
        indicator.SetText(progress.Text, false);
        return false;
    }
}
