using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Manages road generation lifecycle: initialization, loading, and cleanup.
/// </summary>
public static class RoadLifecycleManager
{
    /// <summary>
    /// Called when ZoneSystem starts. Initializes road generator and subscribes to events.
    /// </summary>
    public static void OnZoneSystemStart(ZoneSystem zoneSystem)
    {
        RoadNetworkGenerator.Initialize();
        zoneSystem.GenerateLocationsCompleted += OnLocationsGenerated;
        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug("Subscribed to GenerateLocationsCompleted event");
    }

    /// <summary>
    /// Called when ZoneSystem is destroyed. Cleans up road data and unsubscribes from events.
    /// </summary>
    public static void OnZoneSystemDestroy(ZoneSystem zoneSystem)
    {
        zoneSystem.GenerateLocationsCompleted -= OnLocationsGenerated;
        RoadNetworkGenerator.Reset();
        RoadClearAreaManager.ClearCache();
        RoadVegetationCleaner.Reset();
        RoadTerrainModifier.ResetDebugCounters();

        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug("Road data cleared on world unload");
    }

    /// <summary>
    /// Called when location generation completes. Triggers road loading or generation.
    /// </summary>
    private static void OnLocationsGenerated()
    {
        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug("Location generation complete...");
        RoadNetworkGenerator.MarkLocationsReady();
        RoadClearAreaManager.ClearCache();
        RoadVegetationCleaner.Reset();

        if (!TryStartRoadGeneration())
        {
            // Dedicated servers can fire GenerateLocationsCompleted before the
            // location list is populated, and no player ever spawns headless to
            // retry — so flag it and let the plugin's Update loop poll us.
            m_pendingDeferredGeneration = true;
        }
    }

    private static bool m_pendingDeferredGeneration;

    /// <summary>True while generation is waiting for world state to be ready.</summary>
    public static bool PendingDeferredGeneration => m_pendingDeferredGeneration;

    /// <summary>Polled by the plugin while generation is deferred.</summary>
    public static void RetryDeferredGeneration()
    {
        if (!m_pendingDeferredGeneration)
            return;
        if (TryStartRoadGeneration())
            m_pendingDeferredGeneration = false;
    }

    private static bool TryStartRoadGeneration()
    {
        bool hasWorldGen = WorldGenerator.instance != null;
        bool hasLocations = ZoneSystem.instance?.GetLocationList()?.Count > 0;

        if (hasWorldGen && hasLocations)
        {
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug(
                $"WorldGenerator and locations available ({ZoneSystem.instance!.GetLocationList()!.Count} locations)...");
            
            bool forceRegen = ProceduralRoadsPlugin.ForceRegenerate != null && ProceduralRoadsPlugin.ForceRegenerate.Value;
            if (!forceRegen && RoadNetworkGenerator.TryLoadGlobalRoadData())
            {
                RoadNetworkGenerator.MarkRoadsLoadedFromZDO();
                ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug("Loaded roads from global persistence");
            }
            else
            {
                ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug(forceRegen
                    ? "ForceRegenerate set, regenerating roads..."
                    : "No persisted roads found, generating...");
                RoadNetworkGenerator.GenerateRoads(force: forceRegen);
            }

            RoadValidationRunner.MaybeRunAfterGeneration();

            // RespawnAllZones logs the "[RUINS] respawn total" aggregate itself.
            if (ProceduralRoadsPlugin.SpawnRuinsHeadless != null && ProceduralRoadsPlugin.SpawnRuinsHeadless.Value)
                RuinPlacement.RespawnAllZones();

            return true;
        }
        else
        {
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug(
                $"Deferring road generation (WorldGen={hasWorldGen}, Locations={hasLocations})...");
        }

        return false;
    }

    /// <summary>
    /// Called when player spawns. Enables deferred road loading for existing worlds.
    /// </summary>
    public static void OnPlayerSpawn(Vector3 spawnPoint)
    {
        if (!RoadNetworkGenerator.IsLocationsReady || RoadNetworkGenerator.RoadsAvailable)
            return;

        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug(
            $"Player spawning at {spawnPoint}, attempting to load global road data...");

        bool forceRegenOnSpawn = ProceduralRoadsPlugin.ForceRegenerate != null && ProceduralRoadsPlugin.ForceRegenerate.Value;
        if (!forceRegenOnSpawn && RoadNetworkGenerator.TryLoadGlobalRoadData())
        {
            RoadNetworkGenerator.MarkRoadsLoadedFromZDO();
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug("Roads loaded from global persistence");
        }
        else
        {
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug("No global road data, generating...");
            RoadNetworkGenerator.GenerateRoads();
        }

        RoadValidationRunner.MaybeRunAfterGeneration();
    }

    /// <summary>
    /// Called before world save. Persists global road data.
    /// </summary>
    public static void OnPrepareSave()
    {
        if (RoadNetworkGenerator.RoadsGenerated)
        {
            RoadNetworkGenerator.SaveGlobalRoadData();
        }
    }
}
