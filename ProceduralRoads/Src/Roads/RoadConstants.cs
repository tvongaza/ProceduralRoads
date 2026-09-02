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
    // True blockers (deep water, wide river cores, non-swamp shallows) use
    // float.PositiveInfinity in RoadPathfinder; the values below are additive
    // penalties on passable terrain.
    public const float DefaultRiverPenalty = 4000f;
    public const float DefaultSwampShallowWaterPenalty = 500f;
    public const float DefaultMountainSteepSlopePenalty = 2000f;
    public const float DefaultSteepSlopePenalty = 2000f;
    public const float DefaultSteepSlopeThreshold = 0.6f;
    public const float DefaultTerrainVariancePenalty = 1000f;
    public const float DefaultTerrainVarianceThreshold = 5f;

    // Short river crossings (fords): the pathfinder may jump an impassable
    // river core if dry, non-river ground exists within this many cells
    // (capped by world distance, so long diagonal directions don't stretch it).
    public const int MaxRiverCrossingCells = 6; // 6 * 8m = 48m max ford
    public const float RiverCrossingPenalty = 5000f;

    // Crossing-site selection: a ford whose two banks differ in height by
    // more than MaxFordBankDelta is not accepted at all (bridges across
    // badly mismatched banks stilt or grade absurdly), and any accepted
    // ford pays FordBankDeltaPenalty * delta^2 on top of the crossing
    // penalty, so the pathfinder seeks near-level banks the way a natural
    // road would (a 2 m step costs as much as the crossing itself).
    public const float MaxFordBankDelta = 4f;
    public const float FordBankDeltaPenalty = 1250f;

    // A channel whose bed stays within FordWadeDepth of the waterline is
    // knee-deep: the road goes through as a leveled ford (no bridge, no
    // painting exclusion). Must stay below the sailable fairway depth.
    public const float FordWadeDepth = 0.8f;

    // Road cross-section (see RoadProfile): flat core fully leveled and
    // solidly painted; paint fades out strictly inside the leveled footprint
    // so roads keep an unpainted, smoothed verge; leveling eases to natural
    // terrain over TerrainBlendMargin beyond the half-width. Ends ramp from
    // natural terrain height to smoothed road height over EndpointRampLength.
    public const float RoadFlatCoreRatio = 0.6f;
    public const float RoadPaintOuterRatio = 0.85f;
    public const float EndpointRampLength = 40f;

    // Terrain-quality guarantees (added after real-world selftest findings):
    // roads must keep this much height above the shallow-water threshold —
    // splined centerlines dip between 8m cell samples, so cells barely above
    // the waterline produce underwater road points. Moves are also sampled
    // at interior points so narrow dips between cell centers are seen.
    public const float WaterlineClearance = 0.75f;
    public const float MoveInteriorSampleSpacing = 4.5f;

    // Along-path grade shaping: grades above the comfort threshold get
    // per-meter quadratic cost (forcing contouring/switchbacks on steep
    // faces); grades above the traversable cap are impassable outright.
    public const float GradeComfortThreshold = 0.25f;
    public const float MaxTraversableGrade = 1.0f;
    
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
    
    public const float VegetationClearMargin = 1.5f;
    public const float VegetationClearSampleInterval = 2f;
    
    public const int MaxCoordDebugLogs = 3;
    public const int MaxVertexModificationLogs = 3;

    public static float GetVegetationClearRadius(float roadWidth)
    {
        return roadWidth * 0.5f + VegetationClearMargin;
    }
}
