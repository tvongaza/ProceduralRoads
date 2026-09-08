namespace ProceduralRoads;

/// <summary>
/// Centralized constants for the road generation system.
/// </summary>
public static class RoadConstants
{
    public const float ZoneSize = 64f;
    public const float HalfZoneSize = ZoneSize / 2f;
    public const float SeaLevel = 30f;
    public const float DeepWaterHeight = 28f;
    public const float ShallowWaterHeight = 30.5f;
    public const float TerrainDeltaMin = -8f;
    public const float TerrainDeltaMax = 8f;
    
    public const float PathfindingCellSize = 8f;
    public const int PathfindingMaxIterations = 10000;
    public const float TerrainVarianceSampleRadius = 16f;
    public const int TerrainVarianceSampleCount = 8;
    public const float MountainSlopeThreshold = 0.4f;
    public const float RiverImpassableThreshold = 0.5f;
    
    public const float DefaultBaseCost = 1f;
    public const float DefaultSlopeMultiplier = 10f;
    public const float DefaultRiverPenalty = 100000f;
    public const float DefaultWaterPenalty = 100000f;
    public const float DefaultSteepSlopePenalty = 2000f;
    public const float DefaultSteepSlopeThreshold = 0.6f;
    public const float DefaultTerrainVariancePenalty = 1000f;
    public const float DefaultTerrainVarianceThreshold = 5f;

    // Bridges (prototype, off by default: config Bridges/Enabled). With
    // bridges on, the pathfinder may jump a river core in a straight line to
    // dry ground within MaxBridgeCrossingCells (measured in metres, so a
    // diagonal scan does not stretch it), between banks whose heights differ
    // by at most MaxBridgeBankDelta; a step in bank height pays
    // BridgeBankDeltaPenalty * delta^2 on top, so near-level banks are
    // preferred. The jump costs BridgeCostFixed + BridgeCostPerMeter per
    // metre (config Bridges/CostFixed and Bridges/CostPerMeter). For scale in
    // this cost model: easy ground costs about 1 per metre of road, rough or
    // steep ground 1000-2000 per cell, so the defaults make a 100 m bridge
    // worth roughly 1 km of rough detour; a bridge appears where the way
    // around is long or there is none.
    public const int MaxBridgeCrossingCells = 16; // 16 * 8 m = 128 m, dry cell to dry cell
    public const float MaxBridgeBankDelta = 4f;
    public const float BridgeBankDeltaPenalty = 1250f;
    public const float DefaultBridgeCostFixed = 60000f;
    public const float DefaultBridgeCostPerMeter = 600f;

    // Road cross-section (see RoadProfile): flat core fully leveled and
    // solidly painted; paint fades out strictly inside the leveled footprint
    // so roads keep an unpainted, smoothed verge; leveling eases to natural
    // terrain over TerrainBlendMargin beyond the half-width.
    public const float RoadFlatCoreRatio = 0.6f;
    public const float RoadPaintOuterRatio = 0.85f;

    // Terrain leveling fits a line along the road through the nearby road
    // points (see RoadTerrainModifier.CalculateBlendedHeight): the spread of
    // the prior that holds an undetermined gradient at zero, and the steepest
    // gradient the fit may report (metres per metre).
    public const float HeightFitRidgeMetres = 0.02f;
    public const float HeightFitMaxGradient = 1.5f;
    
    public const float SpatialGridSize = 64f;
    public const float DefaultRoadWidth = 4f;
    public const float EdgeFalloffStart = 0.6f;
    public const int HeightSmoothingWindow = 41;
    public const float OverlapThreshold = 0.3f;
    public const float OverlapSearchRadiusMultiplier = 0.6f;
    public const float OverlapBlendRadiusMultiplier = 0.8f;
    
    public const float TerrainBlendMargin = 2.0f;
    public const float PaintDedupeInterval = 1.5f;
    public const float MinHeightDeltaThreshold = 0.01f;
    public const float MinBlendForModification = 0.5f;
    
    public const float VegetationClearMultiplier = 0.6f;
    public const float VegetationClearSampleInterval = 4f;
    
    public const int MaxCoordDebugLogs = 3;
    public const int MaxVertexModificationLogs = 3;
}
