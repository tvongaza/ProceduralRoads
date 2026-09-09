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

    /// <summary>Study: the shipped edge cell, walked inland until it stands
    /// above the waterline. The island grid is 128 m, so the centre of an
    /// edge cell is often in the sea, and a search that starts in the sea
    /// never takes a step.</summary>
    IslandEdgeCellOnLand,
}

/// <summary>What happens when a planned connection cannot be built.</summary>
public enum FailureFallback
{
    /// <summary>The shipped behaviour: nothing. The plan carries on from the
    /// place it was heading for, whether or not the leg to it was built.</summary>
    None,

    /// <summary>Try again to the nearest point on a road already built. A road
    /// runs where the ground allowed it, so it is often reachable from
    /// somewhere a place is not.</summary>
    NearestRoad,

    /// <summary>Try again to the nearest place already on the network, which
    /// may be neither of the two the plan chose.</summary>
    NearestConnectedPlace,

    /// <summary>The road first, then the place: two more searches at
    /// most.</summary>
    RoadThenPlace,
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

    /// <summary>Study: priority, but each further place as far as possible
    /// from those already chosen - the opposite arrangement, same count.</summary>
    PriorityThenFarthest,

    /// <summary>Study: a fixed draw from the island's places, ignoring
    /// priority. The control for both of the above.</summary>
    SeededRandom,
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

    /// <summary>Study proposal: no tree at all. Each place in turn joins the
    /// network at the nearest point on a road already built, so every
    /// connection after the first is a junction by construction.</summary>
    GrowFromNetwork,

    /// <summary>Study proposal: the search runs from the place outward and
    /// stops at the first road it reaches, so nothing has to guess which point
    /// on the network to aim at.</summary>
    ReverseToNetwork,
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

    /// <summary>What a plan does with a connection its pathfinder could not
    /// build. The shipped plans do nothing: the place is left where it is and
    /// the plan carries on.</summary>
    public static FailureFallback Fallback = FailureFallback.None;

    /// <summary>How near a road the reverse search must come to count as
    /// having reached the network. One pathfinding cell: closer than this and
    /// the junction is on the road; further and it is a road beside a road.</summary>
    public static float ReverseSearchReach = 8f;

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

    /// <summary>
    /// What a step onto ground that already carries road costs, as a fraction
    /// of what it would otherwise cost. One is the shipped behaviour: a road
    /// gets no discount for following an earlier one, so two roads to nearby
    /// places run side by side and the network grows no junctions. Below one,
    /// a later road is drawn onto an existing one and they merge.
    ///
    /// Only crossings are shared today, at half price; ordinary road is not
    /// shared at all.
    /// </summary>
    public static float ExistingRoadCostFraction = 1f;

    /// <summary>
    /// How near an existing road a step must be to count as following it. The
    /// pathfinder moves in eight-metre steps and a road is four metres wide,
    /// so asking whether the step lands on the paint almost never says yes:
    /// this has to be a proximity, not a hit.
    /// </summary>
    public static float ExistingRoadReach = 12f;

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
        $"anchor={Anchor}, islands={Islands}, sharing={ExistingRoadCostFraction:0.##}, places={Quantity}" +
        (Quantity == IslandQuota.FixedCount ? $"({FixedPlaceCount})" : "") +
        $", quota={Quota}, plan={Plan}, fallback={Fallback}, " +
        $"filterEndpoints={FilterUnreachableEndpoints}, snapEndpoints={SnapEndpointsToPathableGround}";
}
