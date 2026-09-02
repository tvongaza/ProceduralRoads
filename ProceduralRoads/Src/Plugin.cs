using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ProceduralRoads
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class ProceduralRoadsPlugin : BaseUnityPlugin
    {
        internal const string ModName = "ProceduralRoads";
        internal const string ModVersion = "1.4.3";
        internal const string Author = "warpalicious";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource ProceduralRoadsLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        // Location Manager variables
        public Texture2D tex = null!;

        // Use only if you need them
        //private Sprite mySprite = null!;
        //private SpriteRenderer sr = null!;

        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        // Configuration entries
        public static ConfigEntry<float> RoadWidth = null!;
        public static ConfigEntry<string> CustomLocations = null!;
        public static ConfigEntry<int> IslandRoadPercentage = null!;
        public static ConfigEntry<int> PathfindingMaxIterations = null!;
        public static ConfigEntry<int> MaxLocationsPerIsland = null!;
        public static ConfigEntry<float> BridgeCostFixed = null!;
        public static ConfigEntry<float> BridgeCostPerMeter = null!;
        public static ConfigEntry<bool> DebugValidation = null!;
        public static ConfigEntry<bool> ForceRegenerate = null!;
        public static ConfigEntry<bool> SpawnRuinsHeadless = null!;

        private float m_nextDeferredRetry;

        public void Update()
        {
            if (!RoadLifecycleManager.PendingDeferredGeneration)
                return;
            if (Time.unscaledTime < m_nextDeferredRetry)
                return;
            m_nextDeferredRetry = Time.unscaledTime + 2f;
            RoadLifecycleManager.RetryDeferredGeneration();
        }

        public void Awake()
        {
            // Register the metadata prefab with Jotunn FIRST - must happen before ZNetScene.Awake
            RegisterMetadataPrefab();
            
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;

            // Initialize configuration
            RoadWidth = Config.Bind("Roads", "RoadWidth", 4f,
                new ConfigDescription("Width of generated roads in meters",
                    new AcceptableValueRange<float>(2f, 10f)));

            IslandRoadPercentage = Config.Bind("Roads", "IslandRoadPercentage", 50,
                new ConfigDescription("Percentage of islands that will have roads generated (0-100). " +
                    "Eligible islands are selected across inner, middle, and outer world rings, with larger islands preferred inside each ring.",
                    new AcceptableValueRange<int>(0, 100)));

            PathfindingMaxIterations = Config.Bind("Roads", "PathfindingMaxIterations", 10000,
                new ConfigDescription("Maximum number of iterations for each road segment's pathfinding algorithm. " +
                    "Higher values will generate more roads but increase generation time. " +
                    "Lower values will speed up generation time but cause less roads to generate. " +
                    "Validation stations: keep this well above the deepest genuine no-path decision the log reports " +
                    "(\"no reachable path after N iterations\"); at a binding ceiling failed routes are budget artefacts, not results.",
                    new AcceptableValueRange<int>(1000, 2000000)));

            MaxLocationsPerIsland = Config.Bind("Roads", "MaxLocationsPerIsland", 12,
                new ConfigDescription("Maximum number of locations that can be connected by roads on a single island. " +
                    "Higher values allow more roads on large islands.",
                    new AcceptableValueRange<int>(2, 30)));

            BridgeCostFixed = Config.Bind("Roads", "BridgeCostFixed", RoadConstants.BridgeCrossingPenalty,
                new ConfigDescription("Pathfinding cost of building a bridge over a wide river, fixed part. " +
                    "For scale: easy ground costs about 1 per metre of road, broken ground about 25. " +
                    "Lower = more bridges, higher = roads go around instead. 20000 was the old value (bridge beats ~0.8 km of rough detour); " +
                    "50000 + 400/m (default) makes a bridge worth ~3.6 km of rough detour.",
                    new AcceptableValueRange<float>(0f, 300000f)));

            BridgeCostPerMeter = Config.Bind("Roads", "BridgeCostPerMeter", RoadConstants.BridgeCostPerMeter,
                new ConfigDescription("Pathfinding cost of a bridge per metre of span, on top of BridgeCostFixed. " +
                    "Makes long bridges dearer than short ones.",
                    new AcceptableValueRange<float>(0f, 5000f)));

            ForceRegenerate = Config.Bind("Debug", "ForceRegenerate", false,
                "Ignore roads persisted in the world and regenerate the network from " +
                "scratch on every load. For validation/testing against fixture worlds " +
                "with pre-placed locations; leave off for normal play.");

            DebugValidation = Config.Bind("Debug", "DebugValidation", false,
                "Run automatic road-network validation after generation and write " +
                "ProceduralRoads.selftest.json plus ProceduralRoads.routes.csv to the config folder. " +
                "Also available on demand via the road_selftest console command.");

            SpawnRuinsHeadless = Config.Bind("Debug", "SpawnRuinsHeadless", false,
                "Spawn every planned ruin piece as ZDOs immediately after road generation, " +
                "without waiting for zones to generate around a player. For headless validation: " +
                "a dedicated server has no console input and never spawns zones, so this is the " +
                "only way its census can compare spawned pieces against plans. Leave off for " +
                "normal play (zones spawn lazily as players explore).");

            CustomLocations = Config.Bind("Locations", "CustomLocations", "",
                "Comma-separated list of location names to include in road generation. " +
                "Use this for locations added by Expand World Data or other mods. " +
                "Example: Runestone_Boars,Runestone_Greydwarfs,MerchantCamp");

            // Apply config to road generator
            ApplyConfiguration();

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();

            Analytics.Init(Config, ModGUID, ModVersion);


            if (saveOnSet)
            {
                Config.SaveOnConfigSet = saveOnSet;
                Config.Save();
            }
        }

        /// <summary>
        /// Register the metadata prefab with Jotunn's PrefabManager.
        /// This creates an empty, invisible GameObject that will be used to store road data.
        /// Must be called before ZNetScene.Awake so the prefab is registered in time.
        /// </summary>
        private void RegisterMetadataPrefab()
        {
            // Create an empty GameObject - no mesh, no collider, completely invisible
            var prefab = new GameObject(RoadNetworkGenerator.MetadataPrefabName);
            
            // Add ZNetView for ZDO creation and networking
            var nview = prefab.AddComponent<ZNetView>();
            nview.m_persistent = true;
            
            // Wrap in CustomPrefab and register with Jotunn
            var customPrefab = new CustomPrefab(prefab, false);
            PrefabManager.Instance.AddPrefab(customPrefab);
            
            ProceduralRoadsLogger.LogDebug($"Registered metadata prefab: {RoadNetworkGenerator.MetadataPrefabName}");
        }

        private static void ApplyConfiguration()
        {
            RoadNetworkGenerator.RoadWidth = RoadWidth.Value;
            RoadNetworkGenerator.IslandRoadPercentage = IslandRoadPercentage.Value;
            RoadNetworkGenerator.MaxLocationsPerIsland = MaxLocationsPerIsland.Value;
            RoadPathfinder.MaxIterations = PathfindingMaxIterations.Value;
            RoadPathfinder.ConfiguredBridgeCostFixed = BridgeCostFixed.Value;
            RoadPathfinder.ConfiguredBridgeCostPerMeter = BridgeCostPerMeter.Value;
            // CustomLocations is parsed at generation time to preserve API registrations
        }

        /// <summary>
        /// Parse the CustomLocations config string into a list of location names.
        /// Called at generation time to merge with API-registered locations.
        /// </summary>
        public static HashSet<string> GetConfigLocationNames()
        {
            var result = new HashSet<string>();
            
            if (string.IsNullOrWhiteSpace(CustomLocations.Value))
                return result;

            string[] locationNames = CustomLocations.Value.Split(',');
            foreach (string name in locationNames)
            {
                string trimmed = name.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }
            
            return result;
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                ProceduralRoadsLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
                ApplyConfiguration();
            }
            catch
            {
                ProceduralRoadsLogger.LogError($"There was an issue loading your {ConfigFileName}");
                ProceduralRoadsLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


    }

    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) &&
                   shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) &&
                   shortcut.Modifiers.All(Input.GetKey);
        }
    }
}
