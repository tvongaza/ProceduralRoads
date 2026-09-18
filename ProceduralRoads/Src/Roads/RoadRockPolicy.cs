namespace ProceduralRoads;

/// <summary>Known natural boulders only, never ore, fragments or arbitrary destructibles.</summary>
public static class RoadRockPolicy
{
    public const Heightmap.Biome SupportedBiomes = Heightmap.Biome.Mountain |
        Heightmap.Biome.Plains | Heightmap.Biome.BlackForest;

    // Shared rocks also occur outside these biomes. The instance biome and
    // enabled vegetation registration are checked separately before removal.
    public static bool IsNaturalBoulder(string name) => name == "rock1_mountain" ||
        name == "rock2_mountain" || name == "rock3_mountain" || name == "rock3_mountain_1" ||
        name == "rock2_heath" || name == "rock4_heath" || name == "rock4_forest" ||
        name == "rock4_coast" || name == "HeathRockPillar" ||
        name == "Rock_3" || name == "Rock_4" || name == "Rock_4_plains";

    public static bool CanClear(string name, bool registeredVegetation, Heightmap.Biome biome,
        bool protectedSite, bool playerPiece, bool hasNetworkView, bool validView, bool owned) =>
        IsNaturalBoulder(name) && registeredVegetation && (biome & SupportedBiomes) != 0 &&
        !protectedSite && !playerPiece && (!hasNetworkView || (validView && owned));
}
