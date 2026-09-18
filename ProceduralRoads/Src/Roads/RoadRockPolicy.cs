namespace ProceduralRoads;

/// <summary>Only stock natural mountain boulders, never ore or arbitrary destructibles.</summary>
public static class RoadRockPolicy
{
    public static bool IsMountainRock(string name) => name == "rock1_mountain" ||
        name == "rock2_mountain" || name == "rock3_mountain" || name == "rock3_mountain_1";

    public static bool CanClear(string name, bool mountainVegetation, bool protectedSite,
        bool playerPiece, bool hasNetworkView, bool validView, bool owned) =>
        IsMountainRock(name) && mountainVegetation && !protectedSite && !playerPiece &&
        (!hasNetworkView || (validView && owned));
}
