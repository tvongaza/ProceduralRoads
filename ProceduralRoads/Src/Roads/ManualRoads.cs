using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Host-side append planning. All engine-facing command/sync work is in ManualRoadCommands.</summary>
public static class ManualRoads
{
    private static readonly Dictionary<string, ManualRoadDraft> drafts = new();
    private static List<Island>? islands;
    private static int worldEpoch;
    /// <summary>Changes whenever the world does: work planned in one world must not land in another.</summary>
    public static int WorldEpoch => worldEpoch;
    public static bool Busy { get; private set; }
    public static ManualRoadDraft Draft(string actor)
    {
        if (!drafts.TryGetValue(actor, out var draft)) drafts.Add(actor, draft = new ManualRoadDraft());
        return draft;
    }
    public static void Reset()
    {
        drafts.Clear(); islands = null; worldEpoch++; Busy = false;
        StructureAppendQueue.Reset();
        SiteApproachQueue.Reset();
    }

    public sealed class Plan
    {
        internal int Epoch, Version;
        internal List<RoadSpatialGrid.PlannedPath> Paths = new();
        internal List<RoadCrossing> Crossings = new();
        public IReadOnlyList<RoadCrossing> NewCrossings => Crossings.AsReadOnly();
        public float Length => Paths.Sum(p => p.TotalLength);
        public bool AlreadyConnected { get; internal set; }
        public Vector2 Start { get; internal set; }
        /// <summary>After Commit: the zones explored before the network that this road was built through.</summary>
        public IReadOnlyList<Vector2s> ExploredZonesWritten { get; internal set; } = Array.Empty<Vector2s>();
    }

    /// <summary>
    /// Every zone a manual road writes: each zone its levelling reaches (as RoadSpatialGrid.GetRoadPointsInZone
    /// gathers points for a zone) and each zone its new bridges put pieces in.
    /// </summary>
    internal static HashSet<Vector2s> ZonesReached(IEnumerable<RoadSpatialGrid.PlannedPath> paths, IEnumerable<RoadCrossing> crossings)
    {
        var zones = new HashSet<Vector2s>();
        foreach (RoadSpatialGrid.PlannedPath path in paths)
            for (int i = 0; i < path.Points.Count; i++)
            {
                // The width the append stores for every point (RoadSpatialGrid.CommitAppend).
                float width = path.Width;
                float reach = Mathf.Max(width, RoadTerrainModifier.GatherRadius(width));
                Vector2 p = path.Points[i];
                Vector2s low = ZoneSystem.GetZone(new Vector3(p.x - reach, 0f, p.y - reach));
                Vector2s high = ZoneSystem.GetZone(new Vector3(p.x + reach, 0f, p.y + reach));
                for (int x = low.x; x <= high.x; x++)
                    for (int y = low.y; y <= high.y; y++)
                        zones.Add(new Vector2s((short)x, (short)y));
            }
        var world = WorldGenerator.instance;
        if (world != null)
            foreach (RoadCrossing crossing in BridgeLayout.DistinctSites(crossings.ToList()))
                foreach (StructurePiece piece in BridgeStructures.Solve(crossing, world, world.GetSeed()))
                    zones.Add(ZoneSystem.GetZone(piece.Position));
        return zones;
    }

    /// <summary>What the operator is told when a manual road is built through zones explored before the network.</summary>
    public static string ExploredZonesWarning(IReadOnlyList<Vector2s> zones) =>
        NetworkCutoff.AdmissionWarning("this road crosses", zones);

    public static Plan Prepare(IReadOnlyList<Vector2> points, bool connect,
        IReadOnlyList<Island>? knownIslands = null)
    {
        if (Busy || RoadNetworkGenerator.IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        Busy = true;
        try { return PrepareCore(points, connect, knownIslands); }
        finally { Busy = false; }
    }

    /// <summary>
    /// A network a released build saved (format 1) takes no additions: saving
    /// one would rewrite it in a format that build cannot read, and that build
    /// would then throw the network away and generate another over the world.
    /// </summary>
    internal static void RefuseOnReleasedNetwork()
    {
        if (RoadSpatialGrid.LoadedFromReleasedFormat)
            throw new InvalidOperationException("This world's roads were saved by ProceduralRoads 1.4 or earlier. " +
                "Adding a road would make them unreadable to that version, so roads cannot be added to this world.");
    }

    private static Plan PrepareCore(IReadOnlyList<Vector2> points, bool connect, IReadOnlyList<Island>? knownIslands)
    {
        RefuseOnReleasedNetwork();
        var world = WorldGenerator.instance;
        if (world == null || !RoadNetworkGenerator.IsLocationsReady)
            throw new InvalidOperationException("Wait for the world and its locations to finish loading.");
        if (connect)
        {
            if (points.Count != 1 || !ManualRoadDraft.ValidPoint(points[0])) throw new ArgumentException("Connect needs one valid X,Z position.");
            if (!RoadNetworkGenerator.RoadsAvailable || RoadSpatialGrid.StoredPointRecords == 0)
                throw new InvalidOperationException("No road network exists on this island.");
        }
        else ManualRoadDraft.Validate(points);
        float width = RoadNetworkGenerator.RoadWidth;
        if (!ManualRoadDraft.IsFinite(width) || width < 1 || width > 32) throw new InvalidOperationException("Road width must be between 1 and 32 metres.");
        foreach (var point in points)
        {
            if (RoadSiteProtection.BlocksSegment(point, point, width * 0.5f + 2f, null, null))
                throw new ArgumentException($"Waypoint {point} is inside a protected POI or its road clearance. Mark a point outside its edge.");
            if (world.GetHeight(point.x, point.y) < RoadPathfinder.FloorFor(world.GetBiome(point.x, point.y)))
                throw new ArgumentException($"Waypoint {point} is under water; mark usable ground instead.");
        }
        var groups = knownIslands ?? (islands ??= IslandDetector.DetectRoadIslands());
        var island = groups.FirstOrDefault(i => i.ContainsPoint(points[0].x, points[0].y));
        if (island == null) throw new InvalidOperationException("No road island was detected at the first waypoint.");
        bool OnIsland(Vector2 p) => island.ContainsPoint(p.x, p.y);
        if (points.Any(p => !OnIsland(p))) throw new ArgumentException("All waypoints must be on the same island.");
        var plan = new Plan { Epoch = worldEpoch, Version = RoadSpatialGrid.RoadNetworkVersion, Start = points[0] };
        // Manual roads are laid as drawn: no staircases on them.
        var finder = new RoadPathfinder(world) { SiteClearance = width * 0.5f + 2f, Stairs = false };
        var path = new List<Vector2>();
        if (connect)
        {
            var targets = RoadSpatialGrid.RoadTargets(OnIsland);
            if (targets.Count == 0) throw new InvalidOperationException("No road network exists on this island.");
            if (RoadSpatialGrid.TryGetRoadWithin(points[0], width * 0.5f, out _, OnIsland))
            { plan.AlreadyConnected = true; return plan; }
            // Sample hints bound heuristic cost. The success predicate checks ALL
            // actual road points and filters them by island, not just the hints.
            int stride = Math.Max(1, targets.Count / 128);
            var hints = targets.Where((_, i) => i % stride == 0).Take(129).ToList();
            var route = finder.FindPathToNetwork(points[0], RoadPathfinder.CellSize, hints, OnIsland);
            if (route == null && finder.AlreadyOnNetwork && RoadSpatialGrid.TryGetRoadWithin(
                points[0], RoadPathfinder.CellSize * 2, out var junction, OnIsland))
                route = new List<Vector2> { points[0], junction };
            if (route == null || route.Count < 2) throw new InvalidOperationException("No usable connection found within the routing limits.");
            path.AddRange(route);
        }
        else
        {
            for (int i = 1; i < points.Count; i++)
            {
                var leg = finder.FindPath(points[i - 1], points[i]);
                if (leg == null || leg.Count < 2)
                    throw new InvalidOperationException($"Leg {i} ({points[i - 1]} → {points[i]}) has no usable route within the routing limits. Nothing added.");
                if (path.Count > 0) leg.RemoveAt(0);
                path.AddRange(leg);
            }
        }
        // Check the complete polyline, including the reverse-search's short join.
        for (int i = 1; i < path.Count; i++)
            if (RoadSiteProtection.BlocksSegment(path[i - 1], path[i], finder.SiteClearance, null, null))
                throw new InvalidOperationException("The requested road would cross a protected POI.");
        var crossings = RoadCrossingDetector.Detect(path, world, finder.Bridges, finder.Fords);
        foreach (var crossing in crossings)
            foreach (var old in RoadNetworkGenerator.GetRoadCrossings())
                if (RoadCrossing.SameBanks(old, crossing)) { crossing.SnapTo(old); break; }
        float Ground(Vector2 p)
        {
            var near = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(p.x, 0, p.y), 1f);
            near.RemoveAll(n => n.cutOnly);
            if (near.Count > 0) return near.OrderBy(n => (n.p - p).sqrMagnitude).First().h;
            return BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);
        }
        var paths = RoadNetworkGenerator.PlanRoadPathWithCrossings(path, crossings, width,
            Ground(path[0]), Ground(path[path.Count - 1]));
        if (paths == null) throw new InvalidOperationException("The complete road cannot meet the grade, turn-space, water or POI limits. Nothing added.");
        // Turning landings may move a marked pivot slightly. Large deviations
        // need a new mark instead of silently ignoring the user's requested route.
        if (!connect)
            foreach (var point in points)
                if (!paths.Any(p => p.Points.Any(q => Vector2.Distance(point, q) <= width * 2f)))
                    throw new InvalidOperationException($"The road cannot stay within {width * 2f:F0} m of waypoint {point}. Move that mark.");
        plan.Paths = paths;
        plan.Crossings = crossings.Where(c => !RoadNetworkGenerator.GetRoadCrossings().Any(old => RoadCrossing.SameBanks(old, c))).ToList();
        return plan;
    }

    public static void Commit(Plan plan)
    {
        if (Busy || RoadNetworkGenerator.IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        if (plan.Epoch != worldEpoch || plan.Version != RoadSpatialGrid.RoadNetworkVersion)
            throw new InvalidOperationException("The world or road network changed; plan the road again.");
        if (plan.AlreadyConnected) return;
        Busy = true;
        try { plan.ExploredZonesWritten = RoadNetworkGenerator.AppendManualRoad(plan.Paths, plan.Crossings, plan.Start, plan.Version); }
        finally { Busy = false; }
    }
}

public static partial class RoadNetworkGenerator
{
    public static bool IsGenerating => RoadLifecycleManager.IsGenerating;
    /// <summary>Returns the zones explored before the network that the road is built through (see NetworkCutoff.Admit).</summary>
    internal static List<Vector2s> AppendManualRoad(IReadOnlyList<RoadSpatialGrid.PlannedPath> paths,
        List<RoadCrossing> crossings, Vector2 start, int expectedVersion)
    {
        if (IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        ManualRoads.RefuseOnReleasedNetwork();
        // An explicit manual road is built in full, explored zones included; before the bridge plans are read, so
        // a bridge this makes whole is built whole.
        List<Vector2s> explored = NetworkCutoff.Admit(ManualRoads.ZonesReached(paths, crossings));
        Dictionary<Vector2s, List<StructurePiece>> bridgeAdditions;
        try
        {
            bridgeAdditions = StructureAppendQueue.Prepare(crossings);
            RoadNetworkPersistence.EnsureMetadataInstance();
            RoadSpatialGrid.CommitAppend(paths, expectedVersion);
        }
        catch
        {
            NetworkCutoff.Readmit(explored);
            throw;
        }
        if (explored.Count > 0)
            Log.LogWarning(ManualRoads.ExploredZonesWarning(explored));
        StructureAppendQueue.Enqueue(bridgeAdditions);
        m_roadCrossings.AddRange(crossings);
        m_roadStartPoints.Add((start, $"Manual road {RoadSpatialGrid.AppendCount}"));
        m_roadsGenerated = true; // A loaded network is now dirty and must be saved in full.
        m_roadsLoadedFromZDO = false;
        StructurePlans.InvalidatePlans();
        RoadClearAreaManager.ClearCache();
        return explored;
    }
}

public static partial class RoadNetworkGenerator
{
    /// <summary>
    /// Add a staircase to the network: its pieces as a structure source's run,
    /// owed through the append queue to every zone it touches (as a manual
    /// road's bridges are), its cuts as one cut-only append. Rock, cuts and pieces then reach
    /// each zone as it loads, like a manual road and its bridges.
    /// </summary>
    internal static void AppendStairRun(StairRun run, IReadOnlyList<Vector3> cuts, int expectedVersion)
    {
        if (ManualRoads.Busy || IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        ManualRoads.RefuseOnReleasedNetwork();
        // The saved staircases could not be read: a staircase added now would never be saved.
        if (RoadNetworkPersistence.StairRecordKept)
            throw new InvalidOperationException("This world's saved staircases could not be read, so no staircase is added this session.");
        RoadNetworkPersistence.EnsureMetadataInstance();
        RoadSpatialGrid.CommitAppendCuts(cuts, StairLayout.CutRadius * 2f, expectedVersion);
        // Settled on the ground its cuts and the roads leave: columns carried down, pieces wholly under it not placed (BuriedPieces).
        if (WorldGenerator.instance is WorldGenerator world)
        {
            Func<int, int, float> finished = RoadTerrainModifier.FinishedGround((x, z) => BiomeBlendedHeight.GetBlendedHeight(x, z, world));
            BuriedPieces.CarryColumns(run, finished);
            BuriedPieces.Drop(run, finished);
        }
        var additions = StructureAppendQueue.Prepare(run.Pieces);
        StairStructures.Add(run);
        StructureAppendQueue.Enqueue(additions);
        m_roadsGenerated = true; // A loaded network is now dirty and must be saved in full.
        m_roadsLoadedFromZDO = false;
        StructurePlans.InvalidatePlans();
        RoadClearAreaManager.ClearCache();
    }

    /// <summary>
    /// Carry a road on onto a place's landing (<see cref="RoadSiteEntry"/>): one append whose
    /// points level and paint inside the place's footprint along it.
    /// </summary>
    internal static void AppendSiteEntryRoad(RoadSpatialGrid.PlannedPath road, int expectedVersion)
    {
        if (ManualRoads.Busy || IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        ManualRoads.RefuseOnReleasedNetwork();
        RoadNetworkPersistence.EnsureMetadataInstance();
        RoadSpatialGrid.CommitAppend(new[] { road }, expectedVersion, intoSite: true);
        m_roadsGenerated = true; // A loaded network is now dirty and must be saved in full.
        m_roadsLoadedFromZDO = false;
        StructurePlans.InvalidatePlans();
        RoadClearAreaManager.ClearCache();
    }

    internal static void AcceptManualSnapshot(byte[] data, byte[] starts, byte[] crossings, byte[] excluded, byte[]? stairs = null)
    {
        if (IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        var newStarts = new List<(Vector2 position, string label)>();
        var newCrossings = new List<RoadCrossing>();
        // Parsed first: a damaged stair record throws here, before anything is replaced.
        List<StairRun> newStairs;
        try { newStairs = StairStructures.Parse(stairs); }
        catch (System.IO.IOException e) { throw new InvalidOperationException($"Incomplete road snapshot: {e.Message}"); }
        catch (System.IO.InvalidDataException e) { throw new InvalidOperationException($"Incomplete road snapshot: {e.Message}"); }
        if (!NetworkCutoff.TryReadClientList(excluded, out HashSet<Vector2s> excludedZones) ||
            !RoadNetworkPersistence.DeserializeRoadStartPoints(starts, newStarts) ||
            !RoadNetworkPersistence.DeserializeRoadCrossings(crossings, newCrossings))
            throw new InvalidOperationException("Incomplete road snapshot.");
        if (!RoadSpatialGrid.DeserializeAllRoadPoints(data))
            throw new InvalidOperationException($"Incomplete road snapshot: its road network cannot be read ({RoadSpatialGrid.LastReadRefusal}).");
        // Before the network is available to any writer on this client.
        NetworkCutoff.UseServerList(excludedZones);
        m_roadStartPoints = newStarts;
        m_roadCrossings.Clear(); m_roadCrossings.AddRange(newCrossings);
        StairStructures.Replace(newStairs);
        m_roadsLoadedFromZDO = true;
        m_roadsGenerated = false;
        StructurePlans.InvalidatePlans(); RoadClearAreaManager.ClearCache();
    }
}
