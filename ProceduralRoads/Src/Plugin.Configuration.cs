using BepInEx.Configuration;

namespace ProceduralRoads
{
    public partial class ProceduralRoadsPlugin
    {
        public static ConfigEntry<float> RoadWidth = null!;
        public static ConfigEntry<Heightmap.Biome> ExcludedRoadBiomes = null!;
        public static ConfigEntry<bool> AttemptRoadsToHildirQuestLocations = null!;
        public static ConfigEntry<int> IslandRoadPercentage = null!;
        public static ConfigEntry<int> PathfindingMaxIterations = null!;
        public static ConfigEntry<int> MaxLocationsPerIsland = null!;
        public static ConfigEntry<bool> LowMemoryGeneration = null!;

        internal static void BindLowMemoryGeneration(ConfigFile config)
        {
            LowMemoryGeneration = config.Bind("Roads", "LowMemoryGeneration", GenerationMemory.DefaultLow,
                "Generate the roads keeping less in memory, for a server short of it: slower, the same roads. Off, generation " +
                "keeps work it may need again (staircase search pieces, terrain it has read, layouts laid ahead on idle " +
                "cores) to finish sooner. Changes apply to the next generation; a change during a generation waits for it to end.");
            LowMemoryGeneration.SettingChanged += (_, _) => ApplyConfiguration();
        }

        private static bool m_configurationPending;

        private static void ApplyConfiguration()
        {
            if (RoadLifecycleManager.IsGenerating) { m_configurationPending = true; return; }
            m_configurationPending = false;
            RoadNetworkGenerator.RoadWidth = RoadWidth.Value;
            RoadNetworkGenerator.IslandRoadPercentage = IslandRoadPercentage.Value;
            // A validation switch, not a setting: see DebugSwitches.
            RoadNetworkGenerator.GenerateOnLoad = DebugSwitches.Flag("GENERATE_ROADS_ON_LOAD", true);
            // How many islands may be built at once. Unset is what the mod
            // does on its own -- one fewer than the machine reports -- and 1
            // is the fully serial path. A validation switch for measuring
            // worker counts against each other, not a setting: the right
            // number is a property of the machine, not of the player's taste.
            RoadParallel.Configured = DebugSwitches.Count("ISLAND_WORKERS", 0, 0, 256);
            RoadNetworkGenerator.MaxLocationsPerIsland = MaxLocationsPerIsland.Value;
            RoadNetworkGenerator.NetworkOptions = new RoadNetworkOptions
            {
                ExcludedBiomes = ExcludedRoadBiomes.Value,
                HildirQuestLocations = AttemptRoadsToHildirQuestLocations.Value,
            };
            RoadPathfinder.MaxIterations = PathfindingMaxIterations.Value;
            GenerationMemory.Low = LowMemoryGeneration.Value;
            // CustomLocations is parsed at generation time to preserve API registrations
        }

        internal static void ApplyPendingConfiguration()
        {
            if (m_configurationPending && !RoadLifecycleManager.IsGenerating) ApplyConfiguration();
        }
    }
}
