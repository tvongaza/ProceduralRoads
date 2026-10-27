using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Approach staircases owed to places whose flat only the game's placement
/// decides (<see cref="RoadStairRuns.LaySiteApproach"/>) and whose placement is the
/// player's to make: unique, or shown on the map (<see cref="PlacedSites.IsPlayerDecided"/>);
/// every other such place is placed before its roads and reached by them. Generation
/// queues them; SiteApproachRunner lays each when a player has come by and its place's
/// zone has loaded, and the queue is saved with the network so a world saved first
/// still gets them. Pure.
/// </summary>
public static class SiteApproachQueue
{
    private static readonly List<SiteApproachRequest> s_pending = new();
    private static readonly object s_gate = new();

    public static int Count { get { lock (s_gate) return s_pending.Count; } }
    public static List<SiteApproachRequest> Snapshot() { lock (s_gate) return new List<SiteApproachRequest>(s_pending); }
    public static void Reset() { lock (s_gate) s_pending.Clear(); }
    public static void Remove(SiteApproachRequest request) { lock (s_gate) s_pending.Remove(request); }

    /// <summary>Queue the approach at the end of <paramref name="path"/> (index <paramref name="endIndex"/>,
    /// its first or last point). Called from island workers.</summary>
    /// <param name="roadEnd">Where the road's walking ends, if not at <paramref name="endIndex"/>:
    /// the top or foot of a staircase the road ends on.</param>
    public static void Add(List<Vector2> path, int endIndex, float roadHeight, float arriving, Vector2 centre, Vector2? roadEnd = null)
    {
        var tail = new List<Vector2>();
        if (endIndex == 0)
            for (int i = Mathf.Min(SiteApproachRequest.TailPoints, path.Count - 1); i >= 0; i--) tail.Add(path[i]);
        else
            for (int i = Mathf.Max(0, path.Count - 1 - SiteApproachRequest.TailPoints); i < path.Count; i++) tail.Add(path[i]);
        var request = new SiteApproachRequest { RoadEnd = roadEnd ?? path[endIndex], RoadHeight = roadHeight, Arriving = arriving, Centre = centre, RoadTail = tail };
        lock (s_gate) s_pending.Add(request);
    }

    public static byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        lock (s_gate)
        {
            writer.Write(1);
            writer.Write(s_pending.Count);
            foreach (SiteApproachRequest r in s_pending)
            {
                writer.Write(r.RoadEnd.x); writer.Write(r.RoadEnd.y); writer.Write(r.RoadHeight); writer.Write(r.Arriving);
                writer.Write(r.Centre.x); writer.Write(r.Centre.y);
                writer.Write(r.RoadTail.Count);
                foreach (Vector2 p in r.RoadTail) { writer.Write(p.x); writer.Write(p.y); }
            }
        }
        return stream.ToArray();
    }

    /// <summary>Replace the queue with a saved one; none saved (an older network) leaves it empty.
    /// A damaged record throws <see cref="InvalidDataException"/> or <see cref="IOException"/> and changes nothing.</summary>
    public static void Load(byte[]? data)
    {
        var loaded = new List<SiteApproachRequest>();
        if (data != null && data.Length > 0)
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            int format = reader.ReadInt32();
            if (format != 1) throw new InvalidDataException($"site approach queue format {format}");
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000) throw new InvalidDataException($"site approach queue count {count}");
            for (int i = 0; i < count; i++)
            {
                var r = new SiteApproachRequest
                {
                    RoadEnd = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    RoadHeight = reader.ReadSingle(),
                    Arriving = reader.ReadSingle(),
                    Centre = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                };
                int tail = reader.ReadInt32();
                if (tail < 1 || tail > 1000) throw new InvalidDataException($"site approach tail {tail}");
                for (int k = 0; k < tail; k++) r.RoadTail.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                loaded.Add(r);
            }
        }
        lock (s_gate) { s_pending.Clear(); s_pending.AddRange(loaded); }
    }
}

/// <summary>
/// What a refused approach's ground file (SiteApproachRunner.WriteGround) adds to its header so the approach
/// can be laid again offline exactly as in play (ProceduralRoads.Tests.ApproachGroundFile):
/// the place's root, its rock radius, whether it keeps every rock, its footprint, its kept levelling in full and
/// the staircase row the road ends on. Without them the root of Hildir's cave had to be inferred from the landings the game logged. Pure.
/// </summary>
public static class ApproachGroundHeader
{
    public static string Extras(float root, float rockRadius, bool keepSmallRocks, float footprint, IEnumerable<LevelOp> ops, StairContinuation? top) =>
        FormattableString.Invariant($"root={root:F3},rockRadius={rockRadius:F2},keepSmallRocks={keepSmallRocks},footprint={footprint:F3},ops=[") +
        string.Join(" ", ops.Select(o => FormattableString.Invariant(
            $"{o.OffsetX:R};{o.OffsetZ:R};{o.Height:R};{o.Radius:R};{(o.Square ? 1 : 0)};{o.SmoothRadius:R};{o.SmoothPower:R}"))) +
        "],top=" + (top is StairContinuation t
            ? FormattableString.Invariant($"{t.Edge.x:R};{t.Edge.y:R};{t.Heading:R};{t.Height:R};{t.Kind}")
            : "none");
}
