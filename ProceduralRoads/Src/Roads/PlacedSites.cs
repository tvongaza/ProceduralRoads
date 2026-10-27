using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// One placed place as the game placed it: its turn and root, the levelling its parts
/// replayed, its rocks, and the grid its approach was laid out over. Everything
/// <see cref="PlacedSiteGround"/> and <see cref="PlacedSiteShape"/> need to rebuild its
/// ground and landing offline, from the natural ground of a world dump (
/// the turn is drawn from the game's unseeded Random when the zone is
/// placed, so only a run can say it).
/// </summary>
public sealed class PlacedSiteRecord
{
    public string Prefab = "";
    public Vector2 Centre;
    public float Yaw, Root, RockRadius;
    public bool KeepSmallRocks;
    public List<LevelOp> Ops = new();
    public List<SiteRock> Rocks = new();
    /// <summary>The ground grid the approach was laid out over (vertices, row by row in z).</summary>
    public int X0, Z0, W, H;

    /// <summary>The place's ground over its grid, rebuilt from <paramref name="natural"/>
    /// (the terrain before anything shaped it) and its own levelling; no road earthwork.</summary>
    public float[] Ground(Func<int, int, float> natural) =>
        PlacedSiteGround.Build(X0, Z0, W, H, Centre, Root, Yaw, Ops, natural);
}

/// <summary>
/// A place placed before its roads were generated, ready for them: its record's ground rebuilt
/// over its grid and its landing worked out. Built on the main thread (<see cref="PlacedSites.Prepare"/>)
/// and only read after: the island workers share it.
/// </summary>
public sealed class PlacedSite
{
    public PlacedSiteRecord Record = null!;
    public Vector2 Centre => Record.Centre;
    /// <summary>The ground the placed place leaves (<see cref="PlacedSiteGround"/>).</summary>
    public Func<float, float, float> Ground = (_, _) => 0f;
    public PlacedSiteShape Shape = null!;
}

/// <summary>
/// The places placed this session (their approaches' jobs remember them, and the places placed
/// before the roads), written by road_placed_sites_dump and read back by offline tools.
/// Placed before the roads, a place is reached by its road all the way onto its real
/// landing (the sites are placed first and the roads built to their real landings; before, 14 of 36
/// approaches were given up as "too far": the road ended at the
/// outskirts on whichever side it came in, 80 m from Mistlands_DvergrTownEntrance2's landing at
/// -4160,-4672). Its road is aimed at the ring nearest its landing (<see cref="Aim"/>) and carried
/// on onto the landing as part of the same road (<see cref="RoadNetworkGenerator"/>).
/// </summary>
public static class PlacedSites
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int), PlacedSiteRecord> s_known = new();

    private static (int, int) Key(Vector2 centre) => (Mathf.RoundToInt(centre.x), Mathf.RoundToInt(centre.y));

    public static void Remember(PlacedSiteRecord record) => s_known[Key(record.Centre)] = record;
    public static bool TryGet(Vector2 centre, out PlacedSiteRecord record) => s_known.TryGetValue(Key(centre), out record!);
    public static IReadOnlyList<PlacedSiteRecord> All => s_known.Values.OrderBy(r => r.Centre.x).ThenBy(r => r.Centre.y).ToList();
    public static int Count => s_known.Count;
    public static void Reset()
    {
        s_known.Clear();
        s_ready.Clear();
    }

    /// <summary>The places ready for the roads, by centre: <see cref="Prepare"/>d from the records.</summary>
    private static readonly Dictionary<(int, int), PlacedSite> s_ready = new();

    /// <summary>Off, a road to a place here is built as to any other (the outskirts end). Settable for tests.</summary>
    internal static bool Enabled = true;

    /// <summary>
    /// Every remembered place made ready for the roads: its ground rebuilt from <paramref name="natural"/>
    /// over its grid with its own levelling, and its landing from its flat. Main thread, before the
    /// islands are generated; a place whose levelling has no flat is left out (no landing to go to).
    /// </summary>
    public static void Prepare(Func<float, float, float> natural)
    {
        s_ready.Clear();
        foreach (PlacedSiteRecord r in s_known.Values) PrepareOne(r, natural);
    }

    /// <summary><see cref="Prepare"/> as a road generation's preparation steps: one place a step, counted on
    /// its own stage of the loading panel.</summary>
    internal static IEnumerable<bool> PrepareSteps(Func<float, float, float> natural, RoadPreparation preparation)
    {
        s_ready.Clear();
        var records = s_known.Values.ToList();
        if (records.Count == 0) yield break;
        preparation.BeginStage("Rebuilding placed places' ground", records.Count, "places"); yield return true;
        foreach (PlacedSiteRecord r in records)
        {
            PrepareOne(r, natural);
            preparation.Checkpoint(); yield return true;
        }
    }

    private static void PrepareOne(PlacedSiteRecord r, Func<float, float, float> natural)
    {
        LevelOp? flat = RoadStairRuns.FlatOf(r.Ops);
        if (flat == null || r.W <= 1 || r.H <= 1) return;
        Func<float, float, float> grid = PlacedSiteGround.Sampler(r.Ground((x, z) => natural(x, z)), r.X0, r.Z0, r.W, r.H);
        // Past its grid the place has shaped nothing: the natural ground, where a way onto its landing
        // that starts further back along its road (RoadNetworkGenerator.PlanEntry) begins.
        Func<float, float, float> ground = (x, z) => grid(x, z) is float h && h < PlacedSiteGround.OffGrid ? h : natural(x, z);
        s_ready[Key(r.Centre)] = new PlacedSite
        {
            Record = r, Ground = ground,
            Shape = PlacedSiteShape.Of(r.Centre, r.Yaw, flat.Value, r.RockRadius, r.Rocks, ground, r.KeepSmallRocks, r.Root, r.Prefab),
        };
    }

    /// <summary>Register a place ready for the roads as it is (tests).</summary>
    internal static void Ready(PlacedSite site) => s_ready[Key(site.Centre)] = site;

    /// <summary>The place at <paramref name="centre"/>, if it was placed before its roads and made ready for them.</summary>
    public static bool TryGetReady(Vector2 centre, out PlacedSite site)
    {
        site = null!;
        return Enabled && s_ready.TryGetValue(Key(centre), out site!);
    }

    public static int ReadyCount => s_ready.Count;

    public static void Load(IEnumerable<PlacedSiteRecord> records) { foreach (var r in records) Remember(r); }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static float P(string s) => s.Length == 0 ? float.NaN : float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// One block per place: a "site" row, then its "op" rows, then a "rock" row per rock, each
    /// followed by a "top" row of its real top over its grid (empty cells: nothing of it stands
    /// there). Floats round-trip exactly.
    /// </summary>
    public static string Write(IEnumerable<PlacedSiteRecord> records)
    {
        var sb = new StringBuilder("# placed sites: site,prefab,cx,cz,yaw,root,rockRadius,keepSmallRocks,x0,z0,w,h | op,offsetX,offsetZ,height,radius,square,smoothRadius,smoothPower,sortOrder | rock,name,x,z,halfX,halfZ,yaw,minY,maxY,topX0,topZ0,topW | top,v;v;...\n");
        foreach (PlacedSiteRecord r in records)
        {
            sb.Append("site,").Append(r.Prefab.Replace(',', '_')).Append(',').Append(F(r.Centre.x)).Append(',').Append(F(r.Centre.y)).Append(',')
              .Append(F(r.Yaw)).Append(',').Append(F(r.Root)).Append(',').Append(F(r.RockRadius)).Append(',').Append(r.KeepSmallRocks ? 1 : 0).Append(',')
              .Append(r.X0).Append(',').Append(r.Z0).Append(',').Append(r.W).Append(',').Append(r.H).Append('\n');
            foreach (LevelOp op in r.Ops)
                sb.Append("op,").Append(F(op.OffsetX)).Append(',').Append(F(op.OffsetZ)).Append(',').Append(F(op.Height)).Append(',').Append(F(op.Radius)).Append(',')
                  .Append(op.Square ? 1 : 0).Append(',').Append(F(op.SmoothRadius)).Append(',').Append(F(op.SmoothPower)).Append(',').Append(op.SortOrder).Append('\n');
            foreach (SiteRock k in r.Rocks)
            {
                sb.Append("rock,").Append(k.Name.Replace(',', '_')).Append(',').Append(F(k.At.x)).Append(',').Append(F(k.At.y)).Append(',').Append(F(k.HalfX)).Append(',')
                  .Append(F(k.HalfZ)).Append(',').Append(F(k.Yaw)).Append(',').Append(F(k.MinY)).Append(',').Append(F(k.MaxY)).Append(',')
                  .Append(k.TopX0).Append(',').Append(k.TopZ0).Append(',').Append(k.TopW).Append('\n');
                sb.Append("top,");
                if (k.Top != null)
                    for (int i = 0; i < k.Top.Length; i++) sb.Append(i == 0 ? "" : ";").Append(float.IsNaN(k.Top[i]) ? "" : F(k.Top[i]));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    public static List<PlacedSiteRecord> Read(string text)
    {
        var records = new List<PlacedSiteRecord>();
        PlacedSiteRecord? current = null;
        string[]? rock = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            string[] c = line.Split(',');
            switch (c[0])
            {
                case "site":
                    current = new PlacedSiteRecord
                    {
                        Prefab = c[1], Centre = new Vector2(P(c[2]), P(c[3])), Yaw = P(c[4]), Root = P(c[5]), RockRadius = P(c[6]),
                        KeepSmallRocks = c[7] == "1", X0 = int.Parse(c[8], CultureInfo.InvariantCulture), Z0 = int.Parse(c[9], CultureInfo.InvariantCulture),
                        W = int.Parse(c[10], CultureInfo.InvariantCulture), H = int.Parse(c[11], CultureInfo.InvariantCulture),
                    };
                    records.Add(current);
                    break;
                case "op":
                    current!.Ops.Add(new LevelOp(P(c[1]), P(c[2]), P(c[3]), P(c[4]), c[5] == "1", P(c[6]), P(c[7]), int.Parse(c[8], CultureInfo.InvariantCulture)));
                    break;
                case "rock":
                    rock = c;
                    break;
                case "top":
                    string[] k = rock ?? throw new FormatException("a top row with no rock row before it");
                    string values = line.Substring(4);
                    float[]? top = values.Length == 0 ? null : values.Split(';').Select(P).ToArray();
                    current!.Rocks.Add(new SiteRock(new Vector2(P(k[2]), P(k[3])), P(k[4]), P(k[5]), P(k[6]), P(k[7]), P(k[8]), k[1], top,
                        int.Parse(k[9], CultureInfo.InvariantCulture), int.Parse(k[10], CultureInfo.InvariantCulture), int.Parse(k[11], CultureInfo.InvariantCulture)));
                    rock = null;
                    break;
                default:
                    throw new FormatException($"unknown row kind '{c[0]}'");
            }
        }
        return records;
    }
}
