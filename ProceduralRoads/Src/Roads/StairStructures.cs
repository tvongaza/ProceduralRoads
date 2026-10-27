using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>One staircase in the network: its pieces and the space each walked piece needs clear of rock.</summary>
public sealed class StairRun
{
    public Vector2 From;
    public Vector2 To;
    public float Width;
    public readonly List<StructurePiece> Pieces = new();
    public readonly List<StairClearance> Clearances = new();

    /// <summary>A run from a finished plan. The plan's cuts are not kept here:
    /// they go into the network as cut-only road points, which the road terrain
    /// writer applies as zones load.</summary>
    public static StairRun FromPlan(Vector2 from, Vector2 to, float width, StairPlan plan)
    {
        var run = new StairRun { From = from, To = to, Width = width };
        foreach (StairPiece p in plan.Pieces)
            run.Pieces.Add(new StructurePiece
            {
                Kind = p.Kind switch
                {
                    StairPieceKind.Stair => StructurePieceKind.StairStep,
                    StairPieceKind.Landing => StructurePieceKind.StairFloor,
                    StairPieceKind.Filler => StructurePieceKind.StairFiller,
                    _ => StructurePieceKind.StairSupport,
                },
                Prefab = p.Prefab,
                Position = p.Position,
                YawDegrees = p.YawDegrees,
                Cantilevered = p.Cantilevered,
            });
        run.Clearances.AddRange(plan.Clearances);
        return run;
    }
}

/// <summary>
/// Staircases as a structure source. Unlike a bridge, a staircase is not
/// recomputed from the network on demand: a valley-to-summit plan takes seconds,
/// which a zone load cannot wait for. So each run's plan is made once, when the
/// run is added, and its pieces and clearances are saved with the network
/// (<see cref="Serialize"/>). Pure logic.
/// </summary>
public sealed class StairStructures : IStructureSource
{
    private static readonly List<StairRun> s_runs = new();

    public static IReadOnlyList<StairRun> Runs => s_runs;

    public static void Reset()
    {
        s_runs.Clear();
    }

    /// <summary>Pieces of the network's staircases within <paramref name="reach"/> across of a point.</summary>
    public static List<StructurePiece> PiecesNear(Vector3 point, float reach)
    {
        var near = new List<StructurePiece>();
        var at = new Vector2(point.x, point.z);
        lock (s_runs)
            foreach (StairRun run in s_runs)
            {
                if (Vector2.Distance(at, run.From) > 400f && Vector2.Distance(at, run.To) > 400f) continue;
                foreach (StructurePiece p in run.Pieces)
                    if (Vector2.Distance(at, new Vector2(p.Position.x, p.Position.z)) < reach) near.Add(p);
            }
        return near;
    }

    /// <summary>
    /// Pieces of the network's staircases within <paramref name="reach"/> across of any of
    /// <paramref name="points"/>, under one lock: a staircase's overlap check asked
    /// <see cref="PiecesNear"/> once for each of its pieces, each time taking the lock every island
    /// worker shares and scanning the runs again. Empty for no points.
    /// </summary>
    public static List<StructurePiece> PiecesNearAny(IReadOnlyList<Vector2> points, float reach)
    {
        var near = new List<StructurePiece>();
        if (points.Count == 0) return near;
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        foreach (Vector2 q in points)
        {
            minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x);
            minZ = Mathf.Min(minZ, q.y); maxZ = Mathf.Max(maxZ, q.y);
        }
        // A run whose ends are both further than this from the points' box holds no piece near them
        // (as PiecesNear, by 400 m from its ends, widened by the box).
        var middle = new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f);
        float runReach = 400f + Vector2.Distance(middle, new Vector2(maxX, maxZ));
        lock (s_runs)
            foreach (StairRun run in s_runs)
            {
                if (Vector2.Distance(middle, run.From) > runReach && Vector2.Distance(middle, run.To) > runReach) continue;
                foreach (StructurePiece p in run.Pieces)
                {
                    var at = new Vector2(p.Position.x, p.Position.z);
                    if (at.x < minX - reach || at.x > maxX + reach || at.y < minZ - reach || at.y > maxZ + reach) continue;
                    foreach (Vector2 q in points)
                        if (Vector2.Distance(at, q) < reach) { near.Add(p); break; }
                }
            }
        return near;
    }

    /// <summary>A run with a walked piece within <paramref name="reach"/> of each of
    /// two points, and those pieces; null if none.</summary>
    public static (StairRun run, StructurePiece nearA, StructurePiece nearB)? Carrying(Vector2 a, Vector2 b, float reach)
    {
        lock (s_runs)
            foreach (StairRun run in s_runs)
            {
                StructurePiece? nearA = null, nearB = null;
                float bestA = reach, bestB = reach;
                foreach (StructurePiece p in run.Pieces)
                {
                    if (p.Kind == StructurePieceKind.StairSupport) continue;
                    var at = new Vector2(p.Position.x, p.Position.z);
                    float da = Vector2.Distance(at, a), db = Vector2.Distance(at, b);
                    if (da <= bestA) { bestA = da; nearA = p; }
                    if (db <= bestB) { bestB = db; nearB = p; }
                }
                if (nearA != null && nearB != null && nearA != nearB) return (run, nearA, nearB);
            }
        return null;
    }

    /// <summary>The run already laid between these two ends (either way round, within <paramref name="reach"/>), if any.</summary>
    public static StairRun? Between(Vector2 a, Vector2 b, float reach)
    {
        lock (s_runs)
            foreach (StairRun run in s_runs)
                if ((Vector2.Distance(run.From, a) <= reach && Vector2.Distance(run.To, b) <= reach)
                    || (Vector2.Distance(run.From, b) <= reach && Vector2.Distance(run.To, a) <= reach))
                    return run;
        return null;
    }

    /// <summary>Add a run; the structure plans are recomputed on their next use.</summary>
    public static void Add(StairRun run)
    {
        lock (s_runs) s_runs.Add(run);
        StructurePlans.InvalidatePlans();
    }

    /// <summary>Runs in a fixed order, whichever island laid its runs first:
    /// generation lays islands in parallel, and the saved bytes must not
    /// depend on which finished first.</summary>
    public static void SortCanonical()
    {
        lock (s_runs) s_runs.Sort((a, b) =>
        {
            int c = a.From.x.CompareTo(b.From.x);
            if (c == 0) c = a.From.y.CompareTo(b.From.y);
            if (c == 0) c = a.To.x.CompareTo(b.To.x);
            return c != 0 ? c : a.To.y.CompareTo(b.To.y);
        });
    }

    public string LogTag => "[STAIRS]";

    public IEnumerable<StructurePiece> Plan(WorldGenerator world, int seed)
    {
        foreach (StairRun run in s_runs)
            foreach (StructurePiece piece in run.Pieces)
                yield return piece;
    }

    /// <summary>Vegetation kept off every walked piece: a circle round its 2 m footprint.</summary>
    public void AddClearAreas(List<StructurePiece> zonePieces, List<(Vector3 centre, float radius)> areas)
    {
        foreach (StructurePiece piece in zonePieces)
            if (piece.Kind != StructurePieceKind.StairSupport)
                areas.Add((piece.Position, StairLayout.PieceRun * 0.75f));
    }

    public void AddClearances(Vector3 centre, float radius, List<StructureClearance> clearances)
    {
        var c = new Vector2(centre.x, centre.z);
        float reach = radius + StairLayout.PieceRun;
        foreach (StairRun run in s_runs)
            foreach (StairClearance box in run.Clearances)
                if (Vector2.Distance(new Vector2(box.Centre.x, box.Centre.z), c) <= reach)
                    clearances.Add(new StructureClearance(box.Centre, box.Half, box.YawDegrees, box.Surface));
    }

    private const int FormatVersion = 1;

    public static byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(FormatVersion);
        writer.Write(s_runs.Count);
        foreach (StairRun run in s_runs)
        {
            writer.Write(run.From.x); writer.Write(run.From.y);
            writer.Write(run.To.x); writer.Write(run.To.y);
            writer.Write(run.Width);
            writer.Write(run.Pieces.Count);
            foreach (StructurePiece p in run.Pieces)
            {
                writer.Write((int)p.Kind); writer.Write(p.Prefab);
                writer.Write(p.Position.x); writer.Write(p.Position.y); writer.Write(p.Position.z);
                writer.Write(p.YawDegrees);
            }
            writer.Write(run.Clearances.Count);
            foreach (StairClearance b in run.Clearances)
            {
                writer.Write(b.Centre.x); writer.Write(b.Centre.y); writer.Write(b.Centre.z);
                writer.Write(b.Half.x); writer.Write(b.Half.y); writer.Write(b.Half.z);
                writer.Write(b.YawDegrees); writer.Write(b.Surface);
            }
        }
        return stream.ToArray();
    }

    /// <summary>Replace the runs with saved ones (none for null or empty data). Malformed data throws, changing nothing.</summary>
    public static void Load(byte[]? bytes) => Replace(Parse(bytes));

    /// <summary>Set the runs to ones already parsed (a client's network snapshot).</summary>
    public static void Replace(List<StairRun> runs)
    {
        s_runs.Clear();
        s_runs.AddRange(runs);
        StructurePlans.InvalidatePlans();
    }

    /// <summary>Saved runs, read without touching the current ones. Malformed data throws.</summary>
    public static List<StairRun> Parse(byte[]? bytes)
    {
        var loaded = new List<StairRun>();
        if (bytes != null && bytes.Length > 0)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != FormatVersion) throw new InvalidDataException("Unknown stair run data version.");
            int runs = reader.ReadInt32();
            if (runs < 0 || runs > 10000) throw new InvalidDataException("Invalid stair run count.");
            int total = 0;
            for (int i = 0; i < runs; i++)
            {
                var run = new StairRun
                {
                    From = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    To = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    Width = reader.ReadSingle(),
                };
                int pieces = reader.ReadInt32();
                if (pieces < 0 || (total += pieces) > 500000) throw new InvalidDataException("Invalid stair piece count.");
                for (int j = 0; j < pieces; j++)
                {
                    int kind = reader.ReadInt32();
                    string prefab = reader.ReadString();
                    var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    float yaw = reader.ReadSingle();
                    if (prefab.Length > 100 || !Enum.IsDefined(typeof(StructurePieceKind), kind) ||
                        !ManualRoadDraft.ValidPoint(new Vector2(pos.x, pos.z)) || !ManualRoadDraft.IsFinite(pos.y) || !ManualRoadDraft.IsFinite(yaw))
                        throw new InvalidDataException("Invalid stair piece.");
                    run.Pieces.Add(new StructurePiece { Kind = (StructurePieceKind)kind, Prefab = prefab, Position = pos, YawDegrees = yaw });
                }
                int boxes = reader.ReadInt32();
                if (boxes < 0 || boxes > 500000) throw new InvalidDataException("Invalid stair clearance count.");
                for (int j = 0; j < boxes; j++)
                {
                    var centre = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var half = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    float yaw = reader.ReadSingle(), surface = reader.ReadSingle();
                    if (!ManualRoadDraft.ValidPoint(new Vector2(centre.x, centre.z)) || !ManualRoadDraft.IsFinite(centre.y) ||
                        !ManualRoadDraft.IsFinite(half.x) || !ManualRoadDraft.IsFinite(half.y) || !ManualRoadDraft.IsFinite(half.z) ||
                        !ManualRoadDraft.IsFinite(yaw) || !ManualRoadDraft.IsFinite(surface))
                        throw new InvalidDataException("Invalid stair clearance.");
                    run.Clearances.Add(new StairClearance(centre, half, yaw, surface));
                }
                loaded.Add(run);
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing stair run data.");
        }
        return loaded;
    }
}
