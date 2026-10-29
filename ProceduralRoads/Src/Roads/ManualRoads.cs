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
    }

    public sealed class Plan
    {
        internal int Epoch, Version;
        internal List<RoadSpatialGrid.PlannedPath> Paths = new();
        internal List<RoadCrossing> Crossings = new();
        /// <summary>The staircases the road's search bought, laid by the generator's own rule.</summary>
        internal List<RoadStairSpan> Stairs = new();
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
    internal static HashSet<Vector2s> ZonesReached(IEnumerable<RoadSpatialGrid.PlannedPath> paths, IEnumerable<RoadCrossing> crossings,
        IEnumerable<StairRun>? stairs = null)
    {
        var zones = new HashSet<Vector2s>();
        foreach (StairRun run in stairs ?? Array.Empty<StairRun>())
        {
            foreach (StructurePiece piece in run.Pieces) zones.Add(ZoneSystem.GetZone(piece.Position));
            foreach (StairClearance box in run.Clearances) zones.Add(ZoneSystem.GetZone(box.Centre));
        }
        foreach (RoadSpatialGrid.PlannedPath path in paths)
            for (int i = 0; i < path.Points.Count; i++)
            {
                // As far as a zone gathers the point (RoadSpatialGrid.ZoneReach), at the width the append stores.
                float reach = RoadSpatialGrid.ZoneReach(path.Width);
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
    /// Nor does one with a saved record this build could not read.
    /// </summary>
    internal static void RefuseOnReleasedNetwork()
    {
        if (RoadSpatialGrid.LoadedFromReleasedFormat)
            throw new InvalidOperationException("This world's roads were saved by ProceduralRoads 1.4 or earlier. " +
                "Adding a road would make them unreadable to that version, so roads cannot be added to this world.");
        // A record kept unread (RoadNetworkPersistence.ReadOrKeep) is written back as it was: what a road added to it would be lost.
        string kept = string.Join(", ", RoadNetworkPersistence.KeptRecords);
        if (kept.Length > 0)
            throw new InvalidOperationException($"This world's saved {kept} could not be read, so no road is added this session.");
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
            if (RoadSiteProtection.BlocksSegment(point, point, RoadSiteProtection.Clearance(width), null, null))
                throw new ArgumentException($"Waypoint {point} is inside a protected POI or its road clearance. Mark a point outside its edge.");
            if (!RoadWater.OnLand(world, point))
                throw new ArgumentException($"Waypoint {point} is under water; mark usable ground instead.");
        }
        var groups = knownIslands ?? (islands ??= IslandDetector.DetectRoadIslands());
        var island = groups.FirstOrDefault(i => i.ContainsPoint(points[0].x, points[0].y));
        if (island == null) throw new InvalidOperationException("No road island was detected at the first waypoint.");
        bool OnIsland(Vector2 p) => island.ContainsPoint(p.x, p.y);
        if (points.Any(p => !OnIsland(p))) throw new ArgumentException("All waypoints must be on the same island.");
        var plan = new Plan { Epoch = worldEpoch, Version = RoadSpatialGrid.RoadNetworkVersion, Start = points[0] };
        // The generator's search, as it searches for one road: it may buy stair steps and Mistlands viaducts; only where
        // the route comes from (the marks, or the network to connect to) is the operator's.
        var finder = RoadNetworkGenerator.NewFinder(world, RoadSiteProtection.Clearance(width));
        var path = new List<Vector2>();
        var boughtSteps = new List<(Vector2 from, Vector2 to)>();
        var boughtJumps = new List<(Vector2 from, Vector2 to)>();
        void KeepBought() { boughtSteps.AddRange(finder.LastStairSteps); boughtJumps.AddRange(finder.LastViaductJumps); }
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
            KeepBought();
            path.AddRange(route);
        }
        else
        {
            for (int i = 1; i < points.Count; i++)
            {
                var leg = finder.FindPath(points[i - 1], points[i]);
                if (leg == null || leg.Count < 2)
                    throw new InvalidOperationException($"Leg {i} ({points[i - 1]} → {points[i]}) has no usable route within the routing limits. Nothing added.");
                KeepBought();
                if (path.Count > 0) leg.RemoveAt(0);
                path.AddRange(leg);
            }
        }
        // Check the complete polyline, including the reverse-search's short join.
        for (int i = 1; i < path.Count; i++)
            if (RoadSiteProtection.BlocksSegment(path[i - 1], path[i], finder.SiteClearance, null, null))
                throw new InvalidOperationException("The requested road would cross a protected POI.");
        // One road through every leg: the steps and jumps each leg's search bought, as one search would have.
        finder.LastStairSteps.Clear(); finder.LastStairSteps.AddRange(boughtSteps);
        finder.LastViaductJumps.Clear(); finder.LastViaductJumps.AddRange(boughtJumps);
        // The generator's one planning pipeline for a road (crossings, staircases, structure ends, profile), then its
        // refusals of the road as planned: a marked road has no other route, so it is refused with the reason.
        LocationLevelling.BeginRoad();
        var planned = RoadNetworkGenerator.PlanFromPath(finder, path, path[0], 0f, path[path.Count - 1], 0f,
            false, false, false, false, width);
        switch (planned.Stop)
        {
            case RoadNetworkGenerator.PlanStop.Staircase:
                throw new InvalidOperationException("A staircase the road needs could not be laid. Move a mark. Nothing added.");
            case RoadNetworkGenerator.PlanStop.Preparation:
                throw new InvalidOperationException("A place near the road was not prepared for roads; try again once the world has loaded. Nothing added.");
            case RoadNetworkGenerator.PlanStop.NothingBetween:
                throw new InvalidOperationException("There is no road to lay between the marks. Nothing added.");
        }
        var paths = planned.Plans;
        if (paths == null)
            throw new InvalidOperationException($"The complete road cannot be built ({RoadSpatialGrid.LastRefusal ?? "grade, turn-space, water or POI limits"}). Nothing added.");
        if (RoadNetworkGenerator.BuiltRoadRefusal(paths, planned.Crossings, out Vector2 refusedAt) is string why)
            throw new InvalidOperationException(FormattableString.Invariant(
                $"The road cannot be built: {why} at ({refusedAt.x:F0}, {refusedAt.y:F0}). Move a mark. Nothing added."));
        var crossings = planned.Crossings;
        // Turning landings may move a marked pivot slightly. Large deviations
        // need a new mark instead of silently ignoring the user's requested route.
        if (!connect)
            foreach (var point in points)
                if (!paths.Any(p => p.Points.Any(q => Vector2.Distance(point, q) <= width * 2f)))
                    throw new InvalidOperationException($"The road cannot stay within {width * 2f:F0} m of waypoint {point}. Move that mark.");
        plan.Paths = RoadNetworkGenerator.OwnPieces(paths, crossings, out _, out _);
        plan.Crossings = crossings.Where(c => !RoadNetworkGenerator.GetRoadCrossings().Any(old => RoadCrossing.SameBanks(old, c))).ToList();
        plan.Stairs = planned.Stairs.FindAll(st => !st.Existing);
        return plan;
    }

    public static void Commit(Plan plan)
    {
        if (Busy || RoadNetworkGenerator.IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        if (plan.Epoch != worldEpoch || plan.Version != RoadSpatialGrid.RoadNetworkVersion)
            throw new InvalidOperationException("The world or road network changed; plan the road again.");
        if (plan.AlreadyConnected) return;
        Busy = true;
        try { plan.ExploredZonesWritten = RoadNetworkGenerator.AppendManualRoad(plan.Paths, plan.Crossings, plan.Stairs, plan.Start, plan.Version); }
        finally { Busy = false; }
    }
}

public static partial class RoadNetworkGenerator
{
    public static bool IsGenerating => RoadLifecycleManager.IsGenerating;
    /// <summary>Returns the zones explored before the network that the road is built through (see NetworkCutoff.Admit).</summary>
    internal static List<Vector2s> AppendManualRoad(IReadOnlyList<RoadSpatialGrid.PlannedPath> paths,
        List<RoadCrossing> crossings, IReadOnlyList<RoadStairSpan> stairs, Vector2 start, int expectedVersion)
    {
        if (IsGenerating) throw new InvalidOperationException("Road generation is busy.");
        ManualRoads.RefuseOnReleasedNetwork();
        // Its staircases as the generator lays them, settled on the ground their cuts and the roads leave (BuriedPieces).
        var runs = new List<StairRun>();
        var cuts = new List<Vector3>();
        foreach (RoadStairSpan stair in stairs)
        {
            runs.Add(StairRun.FromPlan(stair.FromPoint, stair.ToPoint, RoadStairRuns.Width, stair.Plan, stair.Kit));
            cuts.AddRange(stair.Plan.Cuts);
        }
        // An explicit manual road is built in full, explored zones included; before the bridge plans are read, so
        // a bridge this makes whole is built whole.
        List<Vector2s> explored = NetworkCutoff.Admit(ManualRoads.ZonesReached(paths, crossings, runs));
        Dictionary<Vector2s, List<StructurePiece>> bridgeAdditions;
        try
        {
            bridgeAdditions = StructureAppendQueue.Prepare(crossings);
            RoadNetworkPersistence.EnsureMetadataInstance();
            // The road and its staircases' earthwork in one append: both or neither.
            RoadSpatialGrid.CommitAppend(paths, expectedVersion, cuts: cuts, cutWidth: StairLayout.CutRadius * 2f);
        }
        catch
        {
            NetworkCutoff.Readmit(explored);
            throw;
        }
        if (explored.Count > 0)
            Log.LogWarning(ManualRoads.ExploredZonesWarning(explored));
        if (runs.Count > 0 && WorldGenerator.instance is WorldGenerator world)
        {
            Func<int, int, float> finished = RoadTerrainModifier.FinishedGround((x, z) => BiomeBlendedHeight.GetBlendedHeight(x, z, world));
            foreach (StairRun run in runs)
            {
                BuriedPieces.CarryColumns(run, finished);
                BuriedPieces.Drop(run, finished);
            }
        }
        StructureAppendQueue.Enqueue(bridgeAdditions);
        for (int i = 0; i < runs.Count; i++)
        {
            // The pieces of zones already spawned are owed through the append queue, as a manual road's bridges are.
            StructureAppendQueue.Enqueue(StructureAppendQueue.Prepare(runs[i].Pieces));
            AddToNetwork(runs[i], stairs[i].Plan, stairs[i].Kit);
        }
        foreach (RoadCrossing c in crossings) c.Kit ??= BridgeStructures.KitFor(c, GenerationWorld);
        m_roadCrossings.AddRange(crossings);
        m_roadStartPoints.Add((start, $"Manual road {RoadSpatialGrid.AppendCount}"));
        m_networkDirty = true; // Saved in full; a loaded network stays loaded.
        StructurePlans.InvalidatePlans();
        RoadClearAreaManager.ClearCache();
        return explored;
    }
}
