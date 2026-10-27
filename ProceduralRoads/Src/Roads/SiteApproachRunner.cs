using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Lays the approach staircases <see cref="SiteApproachQueue"/> owes. Host,
/// single player or dedicated server; one at a time, the layout always on a
/// worker, so the main thread is not stalled.
///
/// The ground it is laid on, for each queued road end, in order of preference:
/// <list type="bullet">
/// <item>loaded: the place's zone and everything round it is loaded; the ground
/// the game drew is read on a 1 m grid, a few thousand samples spread over frames;</item>
/// <item>rebuilt: the place is placed (its LocationProxy is saved with the world)
/// but not loaded; its turn and root are read off the proxy and the ground is
/// rebuilt on the worker (<see cref="PlacedSiteGround"/>, matched to 1 mm);</item>
/// </list>
/// Only places whose placement is the player's to make are queued (unique, or shown
/// on the map: <see cref="PlacedSites.IsPlayerDecided"/>); the server never places
/// them, so each is laid once a player has come by. Every other such place is placed
/// before its roads (<see cref="SitesFirstLoad"/>) and reached by them.
/// On that ground the road itself is carried on onto the place's landing where it can
/// get there (<see cref="RoadSiteEntry"/>); a staircase is laid only where it cannot, or
/// where the road already arrives on a staircase. Either keeps clear of the place's own
/// boulders, read off its saved objects (<see cref="PlacedSiteReader.PlaceRocks"/>).
/// Then, on the main thread, what a staircase would run into is checked and the
/// approach joins the network. Legacy: the places placed before their roads are reached
/// by them (the just in time stair/road generation is to be removed); this serves only the places left for a player to find.
/// With PROCEDURALROADS_ROADS_LOG each approach is logged, with where its time went.
/// </summary>
public static class SiteApproachRunner
{
    /// <summary>Ground samples read per frame.</summary>
    private const int SamplesPerFrame = 1500;
    /// <summary>A place's terrain settles a moment after it is placed: wait this long after first seeing it.</summary>
    private const float SettleSeconds = 2f;
    private const float CheckEvery = 1f;

    private sealed class Job
    {
        public SiteApproachRequest Request = null!;
        /// <summary>"loaded" or "rebuilt".</summary>
        public string Ground = "";
        public float Yaw, Root, Rock;
        public IReadOnlyList<LevelOp> Ops = null!;
        public int X0, Z0, W, H, Next;
        public float[] Heights = null!;
        public int Version, Epoch;
        public double SampleMs, SampleMaxMs, ModelMs, PlanMs;
        public int SampleFrames;
        /// <summary>For a rebuild: each zone's road points, read on the main thread.</summary>
        public Dictionary<Vector2s, List<RoadSpatialGrid.RoadPoint>>? RoadPoints;
        /// <summary>The place's own boulders, read on the main thread.</summary>
        public List<SiteRock> Rocks = new();
        /// <summary>Boulders in the footprint that are not the place's own: a road's, as anywhere.</summary>
        public int WorldRocks;
        /// <summary>The road's width at its end, for a road onto the landing.</summary>
        public float RoadWidth;
        /// <summary>The road arrives on a staircase: the approach is a staircase too.</summary>
        public bool OnStairs;
        /// <summary>The top row of the staircase the road ends on, if it ends on one's top: the approach carries on from it.</summary>
        public StairContinuation? Top;
        /// <summary>Every rock of the place is kept, small ones too (<see cref="SiteRock.KeepsEveryRock"/>).</summary>
        public bool KeepSmallRocks;
    }

    /// <summary>What the worker laid out: a road onto the landing, or else a staircase, or neither.</summary>
    private sealed class Layout
    {
        public RoadSpatialGrid.PlannedPath? Road;
        public float RoadEarthwork;
        public RoadStairSpan? Stairs;
        /// <summary>Why there is no road, where there is none.</summary>
        public string NoRoad = "";
        /// <summary>Why nothing was laid, where nothing was.</summary>
        public string Why = "";
    }

    private static Job? s_sampling;
    /// <summary>The approach being laid out on a worker, one at a time.</summary>
    private static readonly List<(Job job, System.Threading.Tasks.Task<Layout> task)> s_jobs = new();
    private static float s_nextCheck;
    private static readonly Dictionary<SiteApproachRequest, float> s_firstSeen = new();

    /// <summary>Called every frame: one approach at a time, a check a second.</summary>
    public static void Tick()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null || WorldGenerator.instance == null
            || !RoadNetworkGenerator.RoadsAvailable || RoadNetworkGenerator.IsGenerating || ManualRoads.Busy)
            return;
        for (int i = 0; i < s_jobs.Count; i++)
            if (s_jobs[i].task.IsCompleted)
            {
                var (job, task) = s_jobs[i];
                s_jobs.RemoveAt(i);
                Finish(job, task);
                return;
            }
        if (s_jobs.Count > 0) return;
        if (s_sampling != null)
        {
            if (s_sampling.Epoch == ManualRoads.WorldEpoch) Sample(s_sampling); else s_sampling = null;
            return;
        }
        if (SiteApproachQueue.Count == 0 || Time.time < s_nextCheck) return;
        s_nextCheck = Time.time + CheckEvery;
        foreach (SiteApproachRequest request in SiteApproachQueue.Snapshot())
            if (Next(request)) return;
    }

    /// <summary>Start the next stage for this road end; true if it took this check.</summary>
    private static bool Next(SiteApproachRequest request)
    {
        Vector2s zone = ZoneSystem.GetZone(new Vector3(request.Centre.x, 0f, request.Centre.y));
        // A place, or a road end, in a zone explored before the road network: nothing is laid
        // there, now or later (NetworkCutoff), so the approach is not owed.
        Vector2s end = ZoneSystem.GetZone(new Vector3(request.RoadEnd.x, 0f, request.RoadEnd.y));
        if (NetworkCutoff.Excludes(zone) || NetworkCutoff.Excludes(end))
        {
            Drop(request, "zone explored before the road network");
            return false;
        }
        if (Loaded(request, out Location? place))
        {
            // The flat is found from the prefab's list; the ground itself is read, not rebuilt.
            Begin(request, "loaded", place!.transform.rotation.eulerAngles.y, place.transform.position.y);
            return s_sampling != null;
        }
        if (PlacedSiteReader.Placed(zone, request.Centre, out ZDO? proxy))
        {
            // Which of the place's parts this placement kept: the game's draw, replayed here.
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            IReadOnlyList<LevelOp>? kept = PlacedSiteReader.KeptOps(request.Centre, proxy!);
            double ms = Ms(began);
            if (kept == null) return false;
            Begin(request, "rebuilt", proxy!.GetRotation().eulerAngles.y, proxy.GetPosition().y, kept, ms);
            return s_jobs.Exists(j => j.job.Request == request);
        }
        return false;
    }

    /// <summary>A placement's parts, compactly, for the log.</summary>
    private static string Parts(IReadOnlyList<LevelOp> ops) => "[" + string.Join(" ", ops.Select(op =>
        FormattableString.Invariant($"({op.OffsetX:F1},{op.OffsetZ:F1}){(op.Radius > 0f ? "L" : "S")}{Mathf.Max(op.Radius, op.SmoothRadius):F0}"))) + "]";

    private static bool Loaded(SiteApproachRequest request, out Location? place)
    {
        place = Location.GetLocation(new Vector3(request.Centre.x, 0f, request.Centre.y), checkDungeons: false);
        if (place == null || Vector2.Distance(new Vector2(place.transform.position.x, place.transform.position.z), request.Centre) > 1f)
        {
            s_firstSeen.Remove(request);
            return false;
        }
        if (!s_firstSeen.TryGetValue(request, out float seen)) { s_firstSeen[request] = Time.time; return false; }
        if (Time.time - seen < SettleSeconds) return false;
        Bounds(request, out int x0, out int z0, out int w, out int h);
        for (int z = z0; z <= z0 + h; z += 32)
            for (int x = x0; x <= x0 + w; x += 32)
                if (!ZoneSystem.instance.IsZoneLoaded(new Vector3(x, 0f, z))) return false;
        return ZoneSystem.instance.IsZoneLoaded(new Vector3(x0 + w, 0f, z0 + h));
    }

    private static double Ms(long began) => (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>The ground read or rebuilt: round the road's end, and round the place out past
    /// its rock, where a staircase led round it goes.</summary>
    private static void Bounds(SiteApproachRequest r, out int x0, out int z0, out int w, out int h)
    {
        const float margin = PlacedSiteReader.Margin;
        float round = PlacedSiteReader.RockRadius(r.Centre) + RoadStairRuns.RockMargin + RoadStairRuns.RockClearance + margin;
        x0 = Mathf.FloorToInt(Mathf.Min(r.RoadEnd.x - margin, r.Centre.x - round));
        z0 = Mathf.FloorToInt(Mathf.Min(r.RoadEnd.y - margin, r.Centre.y - round));
        w = Mathf.CeilToInt(Mathf.Max(r.RoadEnd.x + margin, r.Centre.x + round)) - x0;
        h = Mathf.CeilToInt(Mathf.Max(r.RoadEnd.y + margin, r.Centre.y + round)) - z0;
    }

    private static void Begin(SiteApproachRequest request, string ground, float yaw, float root,
        IReadOnlyList<LevelOp>? kept = null, double replayMs = 0)
    {
        s_firstSeen.Remove(request);
        IReadOnlyList<LevelOp>? ops = kept ?? LocationLevelling.OpsAt(request.Centre);
        if (ops == null || ops.Count == 0) { Drop(request, "no levelling known"); return; }
        Bounds(request, out int x0, out int z0, out int w, out int h);
        var job = new Job
        {
            Request = request, Ground = ground, Yaw = yaw, Root = root, Ops = ops, Rock = PlacedSiteReader.RockRadius(request.Centre),
            X0 = x0, Z0 = z0, W = w + 1, H = h + 1, Heights = new float[(w + 1) * (h + 1)],
            Version = RoadSpatialGrid.RoadNetworkVersion, Epoch = ManualRoads.WorldEpoch,
            Rocks = PlacedSiteReader.PlaceRocks(x0, z0, w, h, request.Centre, root, yaw, out int world), WorldRocks = world, RoadWidth = RoadWidthAt(request.RoadEnd),
            KeepSmallRocks = PlacedSiteReader.KeepsEveryRock(request.Centre),
            OnStairs = StairStructures.PiecesNear(new Vector3(request.RoadEnd.x, 0f, request.RoadEnd.y), RoadStairRuns.OnStairReach)
                .Exists(p => p.Kind != StructurePieceKind.StairSupport),
            Top = RoadStairRuns.TopAt(request.RoadEnd, request.RoadHeight,
                StairStructures.PiecesNear(new Vector3(request.RoadEnd.x, 0f, request.RoadEnd.y), RoadStairRuns.OnStairReach)),
        };
        // As placed, for offline tools (road_placed_sites_dump).
        PlacedSites.Remember(new PlacedSiteRecord
        {
            Prefab = ZoneSystem.instance.m_locationInstances.TryGetValue(ZoneSystem.GetZone(new Vector3(request.Centre.x, 0f, request.Centre.y)), out var placed)
                ? placed.m_location.m_prefab.Name : "?",
            Centre = request.Centre, Yaw = yaw, Root = root, RockRadius = job.Rock, KeepSmallRocks = job.KeepSmallRocks,
            Ops = new List<LevelOp>(ops), Rocks = new List<SiteRock>(job.Rocks), X0 = job.X0, Z0 = job.Z0, W = job.W, H = job.H,
        });
        if (ground == "loaded") { s_sampling = job; return; }
        // A rebuild: the road points of every zone it covers, read here on the main thread.
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        job.RoadPoints = new Dictionary<Vector2s, List<RoadSpatialGrid.RoadPoint>>();
        for (int z = z0; z <= z0 + h + 64; z += 64)
            for (int x = x0; x <= x0 + w + 64; x += 64)
            {
                Vector2s zone = ZoneSystem.GetZone(new Vector3(Mathf.Min(x, x0 + w), 0f, Mathf.Min(z, z0 + h)));
                if (!job.RoadPoints.ContainsKey(zone))
                    job.RoadPoints[zone] = new List<RoadSpatialGrid.RoadPoint>(RoadSpatialGrid.GetRoadPointsInZone(zone));
            }
        job.SampleMs = Ms(began) + replayMs;
        job.SampleFrames = 1;
        job.SampleMaxMs = job.SampleMs;
        Start(job);
    }

    /// <summary>The road's width at its end: its nearest road point's.</summary>
    private static float RoadWidthAt(Vector2 end)
    {
        float width = RoadConstants.DefaultRoadWidth, best = float.PositiveInfinity;
        foreach (RoadSpatialGrid.RoadPoint p in RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(end.x, 0f, end.y), 4f))
        {
            if (!p.IsRoad) continue;
            float d = (p.p - end).sqrMagnitude;
            if (d < best) { best = d; width = p.w; }
        }
        return width;
    }

    private static void Sample(Job s)
    {
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        try { SampleSome(s); }
        finally
        {
            double ms = Ms(began);
            s.SampleMs += ms; s.SampleMaxMs = Math.Max(s.SampleMaxMs, ms); s.SampleFrames++;
        }
    }

    private static void SampleSome(Job s)
    {
        int end = Mathf.Min(s.Heights.Length, s.Next + SamplesPerFrame);
        for (; s.Next < end; s.Next++)
        {
            int x = s.X0 + s.Next % s.W, z = s.Z0 + s.Next / s.W;
            if (!ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z), out float g))
            {
                // A zone unloaded under us: try again when it is back.
                s_sampling = null;
                return;
            }
            s.Heights[s.Next] = g;
        }
        if (s.Next < s.Heights.Length) return;
        s_sampling = null;
        Start(s);
    }

    /// <summary>The worker: for a rebuild, the ground first; then the layout.</summary>
    private static void Start(Job s)
    {
        WorldGenerator world = WorldGenerator.instance;
        int seed = world.GetSeed();
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (s.Ground == "rebuilt")
            {
                var fills = new Dictionary<Vector2s, float[]?>();
                foreach (var kv in s.RoadPoints!) fills[kv.Key] = RoadTerrainModifier.FillExtras(kv.Value, world);
                SiteApproachRequest r = s.Request;
                s.Heights = PlacedSiteGround.Build(s.X0, s.Z0, s.W, s.H, r.Centre, s.Root, s.Yaw, s.Ops,
                    (x, z) => BiomeBlendedHeight.GetBlendedHeight(x, z, world),
                    RoadSiteProtection.Contains,
                    (x, z, here) =>
                    {
                        Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0f, z));
                        return s.RoadPoints!.TryGetValue(zone, out var points) && points.Count > 0
                            ? RoadTerrainModifier.ModelledHeight(points, new Vector2(x, z), here, fills[zone]) : here;
                    });
                s.ModelMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
            }
            var ground = PlacedSiteGround.Sampler(s.Heights, s.X0, s.Z0, s.W, s.H);
            var layout = new Layout();
            string why;
            LevelOp? flat = RoadStairRuns.FlatOf(s.Ops);
            if (s.OnStairs) layout.NoRoad = "road: arrives on a staircase";
            else if (flat == null) layout.NoRoad = "road: no flat ground";
            else
            {
                PlacedSiteShape shape = PlacedSiteShape.Of(s.Request.Centre, s.Yaw, flat.Value, s.Rock, s.Rocks, ground, s.KeepSmallRocks, s.Root);
                layout.Road = RoadSiteEntry.Plan(s.Request, shape, s.RoadWidth, ground, world, out why, RoadNetworkGenerator.RoadsNear);
                layout.NoRoad = why;
                layout.RoadEarthwork = RoadSiteEntry.Worst;
            }
            if (layout.Road == null)
            {
                layout.Stairs = RoadStairRuns.PlanPlacedSiteApproach(s.Request, s.Yaw, s.Ops,
                    ground, (x, z) => world.GetBiome(x, z), seed, out why, s.Rock, RoadSiteProtection.Contains, s.Rocks, s.KeepSmallRocks, s.Root, s.Top);
                layout.Why = layout.NoRoad + "; stairs: " + why;
            }
            s.PlanMs = clock.Elapsed.TotalMilliseconds;
            return layout;
        });
        s_jobs.Add((s, task));
    }

    /// <summary>Where the time went, for the log.</summary>
    private static string Timing(Job s, double finishMs) => FormattableString.Invariant(
        $"ground {s.Ground}; main thread: {(s.Ground == "loaded" ? $"reading the ground {s.SampleMs:F1} ms over {s.SampleFrames} frame(s), {s.SampleMaxMs:F1} ms at most in one" : $"replaying its parts and road points {s.SampleMs:F1} ms")}, checking and adding {finishMs:F1} ms; worker: {(s.Ground == "rebuilt" ? $"rebuilding the ground {s.ModelMs:F0} ms, " : "")}layout {s.PlanMs:F0} ms; {s.Heights.Length} vertices");

    private static void Finish(Job s, System.Threading.Tasks.Task<Layout> task)
    {
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        SiteApproachRequest r = s.Request;

        if (task.IsFaulted) { Drop(r, "planning failed: " + task.Exception?.GetBaseException().Message); return; }
        if (s.Epoch != ManualRoads.WorldEpoch) return;
        Layout layout = task.Result;
        if (layout.Road != null) { AddRoad(s, layout, began); return; }
        RoadStairSpan? span = layout.Stairs;
        if (span == null) { Drop(r, layout.Why + (RoadNetworkGenerator.RoadsLog ? "; ground written to " + WriteGround(s) : "") + "; " + Timing(s, Ms(began))); return; }
        float Ground(float x, float z) =>
            ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z), out float g) ? g : WorldGenerator.instance.GetHeight(x, z);
        var steps = new List<string>();
        double mark = 0;
        void Step(string name) { double now = Ms(began); steps.Add(FormattableString.Invariant($"{name} {now - mark:F1}")); mark = now; }
        string conflict = RoadStairRuns.SiteConflict(span.Plan, r.Centre, r.RoadTail, r.RoadTail.Count - 1,
            s.Ground == "rebuilt" ? PlacedSiteGround.Sampler(s.Heights, s.X0, s.Z0, s.W, s.H) : Ground,
            WorldGenerator.instance, new List<RoadStairSpan>(), s.OnStairs ? r.RoadEnd : null);
        Step("check");
        if (conflict != "") { Drop(r, conflict + "; " + Timing(s, Ms(began))); return; }
        try
        {
            // Its cuts outside every footprint (the terrain writer skips the others anyway).
            var cuts = RoadStairRuns.CutsOutsideFootprints(span.Plan);
            StairRun run = StairRun.FromPlan(span.FromPoint, span.ToPoint, RoadStairRuns.Width, span.Plan);
            run.KeepsSiteRocks = true;
            RoadNetworkGenerator.AppendStairRun(run, cuts, s.Version);
        }
        catch (InvalidOperationException e)
        {
            // The network changed while it was planned: plan it again on the next check.
            if (RoadNetworkGenerator.RoadsLog) ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"[STAIRS] site approach at {r.Centre} not added yet: {e.Message}");
            return;
        }
        Step("append");
        SiteApproachQueue.Remove(r);
        // Not saved here: the world's own save writes the network with the staircase and
        // the queue without it (RoadLifecycleManager.OnPrepareSave), as it writes the pieces.
        // Two saves here held the main thread most of 641 ms.
        ManualRoadNetworkSync.Publish();
        RoadTerrainModifier.ApplyAdditionsToLoadedZones();
        Step("publish");
        RoadRockClearing.Reset();
        Step("rocks");
        StructurePlacement.ApplyPendingAppends();
        Step("pieces");
        double total = Ms(began);
        if (RoadNetworkGenerator.RoadsLog) ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo(
            $"[STAIRS] site approach at {r.Centre}: {span.Kit.Name} {span.Plan.Rise:F1} m {(span.Plan.Reversed ? "down" : "up")}, " +
            $"{span.Plan.Pieces.Count} pieces, from {span.FromPoint} to {span.ToPoint}, turn {s.Yaw:F1}, parts {Parts(s.Ops)}, {s.Rocks.Count} of its rock(s) kept clear ({s.Rocks.Count(k => k.Top != null)} by their real top), {s.WorldRocks} world boulder(s) left to the road's rules; " +
            $"no road ({layout.NoRoad}); {SiteApproachQueue.Count} still owed; " +
            Timing(s, total) + " (ms: " + string.Join(", ", steps) + ")");
    }

    /// <summary>The road carried on onto the landing: one append, its points into the site.</summary>
    private static void AddRoad(Job s, Layout layout, long began)
    {
        SiteApproachRequest r = s.Request;
        RoadSpatialGrid.PlannedPath road = layout.Road!;
        try { RoadNetworkGenerator.AppendSiteEntryRoad(road, s.Version); }
        catch (InvalidOperationException e)
        {
            if (RoadNetworkGenerator.RoadsLog) ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"[STAIRS] site approach road at {r.Centre} not added yet: {e.Message}");
            return;
        }
        SiteApproachQueue.Remove(r);
        ManualRoadNetworkSync.Publish();
        RoadTerrainModifier.ApplyAdditionsToLoadedZones();
        RoadRockClearing.Reset();
        double total = Ms(began);
        float rise = road.Heights[road.Heights.Count - 1] - road.Heights[0];
        if (RoadNetworkGenerator.RoadsLog) ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo(FormattableString.Invariant(
            $"[STAIRS] site approach at {r.Centre}: road {road.TotalLength:F1} m, {rise:F1} m {(rise >= 0f ? "up" : "down")}, {road.Width:F1} m wide, earthwork {layout.RoadEarthwork:F1} m at worst, ") +
            $"from {r.RoadEnd} to {road.Points[road.Points.Count - 1]}, turn {s.Yaw:F1}, parts {Parts(s.Ops)}, {s.Rocks.Count} of its rock(s) kept clear ({s.Rocks.Count(k => k.Top != null)} by their real top), {s.WorldRocks} world boulder(s) left to the road's rules; {SiteApproachQueue.Count} still owed; " +
            Timing(s, total));
    }

    /// <summary>The ground a refused approach was laid out on, for reproducing it offline:
    /// a header line with the request, the grid's corner and size, the place's turn and
    /// boulders (name@x;z;halfX;halfZ;yaw;minY;maxY), the road's width and <see cref="ApproachGroundHeader.Extras"/>,
    /// then one row of heights per metre of z.</summary>
    private static string WriteGround(Job s)
    {
        SiteApproachRequest r = s.Request;
        string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
            FormattableString.Invariant($"ProceduralRoads.approach_{r.Centre.x:F0}_{r.Centre.y:F0}.csv"));
        var sb = new System.Text.StringBuilder();
        sb.Append(FormattableString.Invariant(
            $"x0={s.X0},z0={s.Z0},w={s.W},h={s.H},yaw={s.Yaw:F2},roadEnd={r.RoadEnd.x:F3};{r.RoadEnd.y:F3},roadHeight={r.RoadHeight:F3},arriving={r.Arriving:F2},centre={r.Centre.x:F3};{r.Centre.y:F3},ground={s.Ground},parts={Parts(s.Ops).Replace(',', ';')},rocks=[{string.Join(" ", s.Rocks.Select(k => FormattableString.Invariant($"{k.Name}@{k.At.x:F2};{k.At.y:F2};{k.HalfX:F2};{k.HalfZ:F2};{k.Yaw:F1};{k.MinY:F1};{k.MaxY:F1}")))}],roadWidth={s.RoadWidth:F2},onStairs={s.OnStairs},"));
        sb.Append(ApproachGroundHeader.Extras(s.Root, s.Rock, s.KeepSmallRocks,
            RoadSiteProtection.NearestFootprint(r.Centre, 0f, out RoadSiteProtection.Footprint footprint) ? footprint.Radius : 0f, s.Ops, s.Top)).Append('\n');
        for (int k = 0; k < s.H; k++)
        {
            for (int i = 0; i < s.W; i++) sb.Append(i == 0 ? "" : ",").Append(s.Heights[k * s.W + i].ToString("F3", CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
        try { System.IO.File.WriteAllText(path, sb.ToString()); return path; }
        catch (Exception e) { return "nowhere (" + e.Message + ")"; }
    }

    /// <summary>No staircase here: said once, and the road stays as it is.</summary>
    private static void Drop(SiteApproachRequest r, string why)
    {
        // Saved with the world, as a laid one is.
        SiteApproachQueue.Remove(r);
        if (RoadNetworkGenerator.RoadsLog) ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"[STAIRS] site approach at {r.Centre} not laid: {why}; {SiteApproachQueue.Count} still owed");
    }

    /// <summary>Forget the approaches in hand with the network they were planned on.</summary>
    public static void Install() => RoadNetworkGenerator.OnReset += Reset;

    public static void Reset()
    {
        s_sampling = null;
        // A layout on a worker reads the footprint index and the road grid the reset is about to clear:
        // it finishes first (one approach at a time, so at most one).
        foreach (var (_, task) in s_jobs)
            try { task.Wait(); } catch (AggregateException) { }
        s_jobs.Clear();
        s_firstSeen.Clear();
    }
}
