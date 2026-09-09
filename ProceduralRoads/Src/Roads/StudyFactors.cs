namespace ProceduralRoads;

/// <summary>Where an island's network is rooted.</summary>
public enum AnchorMode
{
    /// <summary>The shipped rule off the starter island: the island cell
    /// nearest its own bounding box, radius 0 - a coast cell, not a place.</summary>
    IslandEdgeCell,

    /// <summary>PR #16's rule: the island's highest-priority location,
    /// nearest its centre on a tie.</summary>
    HighestPriorityLocation,
}

/// <summary>Which islands get roads.</summary>
public enum IslandSelection
{
    /// <summary>The shipped rule: sort by area, take the largest by percentage.</summary>
    LargestFirst,

    /// <summary>PR #16's rule: the quota spread over three world rings.</summary>
    RingBalanced,
}

/// <summary>Which places on an island fill its quota.</summary>
public enum LocationQuota
{
    /// <summary>The shipped rule: by priority, truncated at the cap.</summary>
    PriorityTruncated,

    /// <summary>PR #16's rule: priority, less a penalty that grows with
    /// distance from what is already chosen.</summary>
    PriorityThenNearest,
}

/// <summary>How many places on an island may have roads.</summary>
public enum IslandQuota
{
    /// <summary>The shipped rule: 2 + area / 2 km², clamped to the configured
    /// ceiling.</summary>
    AreaFormula,

    /// <summary>Every eligible place on the island. Answers what the quota
    /// costs, which no other run can.</summary>
    EveryEligiblePlace,

    /// <summary>A fixed number per island, whatever its size.</summary>
    FixedCount,
}

/// <summary>How the chosen places are connected.</summary>
public enum ConnectionPlan
{
    /// <summary>The shipped rule: MST on even island ids, nearest-neighbour
    /// chain on odd ones.</summary>
    ChainOrMstByParity,

    /// <summary>PR #16's rule: one tree grown outward from the anchor, failed
    /// edges remembered, a link-distance limit and an attempt cap.</summary>
    TreeWithRetries,

    /// <summary>Study proposal: a spanning tree on what the pathfinder charges
    /// to get between places, not on straight-line distance.</summary>
    RoutedMst,

    /// <summary>Study proposal: one long road along the island's routed axis,
    /// everything else joined to the network where it is nearest.</summary>
    TrunkAndSpurs,

    /// <summary>Study proposal: the anchor serves what is near it; a cluster
    /// too far to serve gets a hub of its own.</summary>
    HubAndSpoke,
}

/// <summary>
/// The policy differences between the shipped network and PR #16's, one
/// switch each, so a run can change exactly one of them and the study can say
/// what that one change was worth.
///
/// <see cref="RoadNetworkGenerator.Strategy"/> sets all six at once to one
/// package or the other; a study run may then override individual factors.
/// Ordinary play never touches them: the defaults are the shipped rules.
///
/// Study branch (study/road-network-strategies). Never part of a PR.
/// </summary>
public static class StudyFactors
{
    public static AnchorMode Anchor = AnchorMode.IslandEdgeCell;

    /// <summary>How many places an island may have roads to.</summary>
    public static IslandQuota Quantity = IslandQuota.AreaFormula;

    /// <summary>Places per island when <see cref="Quantity"/> is a fixed count.</summary>
    public static int FixedPlaceCount = 8;

    /// <summary>
    /// How many neighbours a plan that prices its edges may consider per place.
    /// All pairs is fine for a dozen places and hopeless for four hundred: the
    /// cost is one pathfinding search per pair. Nearest neighbours by
    /// straight-line distance are the candidates; the choice between them is
    /// still made on routed cost.
    /// </summary>
    public static int RoutedPlanNeighbours = 8;
    public static IslandSelection Islands = IslandSelection.LargestFirst;
    public static LocationQuota Quota = LocationQuota.PriorityTruncated;
    public static ConnectionPlan Plan = ConnectionPlan.ChainOrMstByParity;

    /// <summary>PR #16: a place with no reachable ground near it is dropped
    /// before it is ever attempted.</summary>
    public static bool FilterUnreachableEndpoints;

    /// <summary>PR #16: each end of a road is moved onto ground a road can
    /// stand on before the search starts.</summary>
    public static bool SnapEndpointsToPathableGround;

    /// <summary>Sets every factor to one package's rules.</summary>
    public static void Apply(RoadNetworkStrategy strategy)
    {
        bool reachable = strategy == RoadNetworkStrategy.Reachable;
        Anchor = reachable ? AnchorMode.HighestPriorityLocation : AnchorMode.IslandEdgeCell;
        Quantity = IslandQuota.AreaFormula;
        Islands = reachable ? IslandSelection.RingBalanced : IslandSelection.LargestFirst;
        Quota = reachable ? LocationQuota.PriorityThenNearest : LocationQuota.PriorityTruncated;
        Plan = reachable ? ConnectionPlan.TreeWithRetries : ConnectionPlan.ChainOrMstByParity;
        FilterUnreachableEndpoints = reachable;
        SnapEndpointsToPathableGround = reachable;
    }

    /// <summary>One line for a manifest or a caption.</summary>
    public static string Describe() =>
        $"anchor={Anchor}, islands={Islands}, places={Quantity}" +
        (Quantity == IslandQuota.FixedCount ? $"({FixedPlaceCount})" : "") +
        $", quota={Quota}, plan={Plan}, " +
        $"filterEndpoints={FilterUnreachableEndpoints}, snapEndpoints={SnapEndpointsToPathableGround}";
}
