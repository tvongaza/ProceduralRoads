using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>One attempt to connect two places, and what became of it.</summary>
public sealed class RoadAttempt
{
    public int Index;
    public string Label = "";
    /// <summary>Which island's generation produced this row. -1 when the row
    /// was written outside an island's loop.</summary>
    public int IslandId = -1;
    /// <summary>What the plan was doing when it ran this search. A plan may
    /// write more than one row for one connection - the reverse plan writes a
    /// destination-free search and then the road build - so a count of rows is
    /// not a count of connections, and this column is what separates
    /// them.</summary>
    public string Role = "leg";
    /// <summary>The connection this row belongs to. Rows sharing an id are one
    /// connection: the plan decided once, and this is how often it had to ask
    /// the pathfinder to carry that decision out.</summary>
    public int ConnectionId = -1;
    public Vector2 Start;
    public Vector2 End;
    public bool Connected;
    /// <summary>"found", "no reachable path", "max iterations reached", or the
    /// stage that dropped a found path ("path too short after trimming").</summary>
    public string Outcome = "";
    public int Iterations;
    public int UniqueExpandedCells;
    public int BlockedMoves;
    public int CrossingsTaken;
    /// <summary>How near the search came to the destination, in metres.</summary>
    public float ClosestApproach;
    public Vector2 ClosestPoint;
    /// <summary>Bounds of the settled set, in metres.</summary>
    public Vector2 SettledMin;
    public Vector2 SettledMax;
    public float RouteLength;
    public int CrossingsOnRoad;
    public readonly Dictionary<CrossingRejection, int> Rejections = new();

    /// <summary>The straight-line distance the attempt had to cover.</summary>
    public float DirectDistance => Vector2.Distance(Start, End);
}

/// <summary>
/// Every connection generation tried, connected or not, with the search's own
/// account of why. Without this a missing road leaves no trace at all: the
/// generator drops a failed edge and moves on.
///
/// Study instrument (branch study/road-network-strategies). Recording is on by
/// default here so no run is wasted; a control run can switch it off to show
/// the instrument costs nothing that matters.
/// </summary>
public static class RoadAttemptLog
{
    public static bool Enabled = true;

    private static readonly List<RoadAttempt> m_attempts = new();

    public static IReadOnlyList<RoadAttempt> Attempts => m_attempts;

    /// <summary>
    /// The island, role and connection every row written from here belongs to.
    /// The generator sets them; the log only stamps them. They exist because a
    /// row count answers no question on its own: the study needs to say how
    /// many CONNECTIONS a plan made and how many SEARCHES that cost, and only
    /// the generator knows which rows are the same decision.
    /// </summary>
    public static int Island = -1;

    private static int m_connection = -1;
    private static int m_nextConnection;
    private static string m_role = "leg";
    private static int? m_join;
    private static string? m_nextRole;

    /// <summary>
    /// The role the next row should carry, and - when <paramref name="join"/>
    /// is given - the connection it belongs to rather than a new one. Both are
    /// consumed by the row that follows, so a caller that sets neither gets a
    /// fresh connection labelled with the plan's default role.
    /// </summary>
    public static void NextRow(string role, int? join = null)
    {
        m_nextRole = role;
        m_join = join;
    }

    /// <summary>Opens a connection explicitly, for a plan that runs more than
    /// one search for one decision. Returns the id to hand back to
    /// <see cref="NextRow"/>.</summary>
    public static int OpenConnection(string role)
    {
        m_connection = m_nextConnection++;
        m_role = role;
        return m_connection;
    }

    /// <summary>Connections opened in this run, however many rows each cost.</summary>
    public static int ConnectionCount => m_nextConnection;

    /// <summary>Takes the pending role and connection for one row.</summary>
    private static void ClaimRow()
    {
        if (m_join.HasValue)
            m_connection = m_join.Value;
        else
            m_connection = m_nextConnection++;
        m_role = m_nextRole ?? "leg";
        m_join = null;
        m_nextRole = null;
    }

    public static void Clear()
    {
        m_attempts.Clear();
        m_connection = -1;
        m_nextConnection = 0;
        m_role = "leg";
        m_join = null;
        m_nextRole = null;
        Island = -1;
    }

    /// <summary>Installs a trace for one attempt, if recording is on and no
    /// other probe is watching. Returns the probe to hand back to
    /// <see cref="Finish"/>, or null when nothing is being recorded.</summary>
    public static PathfinderTrace? Begin()
    {
        if (!Enabled || RoadPathfinder.Probe != null)
            return null;

        PathfinderTrace trace = new();
        RoadPathfinder.Probe = trace;
        return trace;
    }

    /// <summary>Closes the attempt started by <see cref="Begin"/> and keeps it.</summary>
    public static void Finish(PathfinderTrace? trace, string label, Vector2 start, Vector2 end,
        bool connected, string outcome, float routeLength, int crossingsOnRoad)
    {
        if (trace == null)
            return;

        RoadPathfinder.Probe = null;
        ClaimRow();

        RoadAttempt attempt = new()
        {
            Index = m_attempts.Count,
            Label = label,
            Start = start,
            End = end,
            Connected = connected,
            Outcome = outcome,
            Iterations = trace.Iterations,
            UniqueExpandedCells = trace.UniqueExpandedCells,
            BlockedMoves = trace.BlockedMoves,
            CrossingsTaken = trace.CrossingsAccepted,
            ClosestApproach = trace.ClosestApproach == float.MaxValue ? 0f : trace.ClosestApproach,
            ClosestPoint = new Vector2(
                trace.ClosestCell.x * RoadPathfinder.CellSize,
                trace.ClosestCell.y * RoadPathfinder.CellSize),
            RouteLength = routeLength,
            CrossingsOnRoad = crossingsOnRoad,
            IslandId = Island,
            Role = m_role,
            ConnectionId = m_connection,
        };

        if (trace.UniqueExpandedCells > 0)
        {
            attempt.SettledMin = new Vector2(trace.MinX * RoadPathfinder.CellSize, trace.MinY * RoadPathfinder.CellSize);
            attempt.SettledMax = new Vector2(trace.MaxX * RoadPathfinder.CellSize, trace.MaxY * RoadPathfinder.CellSize);
        }

        foreach (KeyValuePair<CrossingRejection, int> entry in trace.Rejections)
            attempt.Rejections[entry.Key] = entry.Value;

        m_attempts.Add(attempt);
    }

    /// <summary>Abandons a trace without keeping an attempt (an attempt that
    /// never reached the pathfinder).</summary>
    public static void Abandon(PathfinderTrace? trace)
    {
        if (trace != null)
            RoadPathfinder.Probe = null;
    }

    /// <summary>One row per attempt, with a column per rejection cause so the
    /// causes can be totalled without parsing.</summary>
    public static string ToCsv()
    {
        CrossingRejection[] causes = (CrossingRejection[])System.Enum.GetValues(typeof(CrossingRejection));

        StringBuilder sb = new();
        sb.Append("attempt_index,connection_id,role,island_id,label,connected,outcome,start_x,start_z,end_x,end_z,direct_distance,")
          .Append("route_length,crossings_on_road,iterations,settled_cells,blocked_moves,crossings_taken,")
          .Append("closest_approach,closest_x,closest_z,settled_min_x,settled_min_z,settled_max_x,settled_max_z");
        foreach (CrossingRejection cause in causes)
            sb.Append(",reject_").Append(cause);
        sb.Append('\n');

        foreach (RoadAttempt a in m_attempts)
        {
            sb.Append(a.Index).Append(',')
              .Append(a.ConnectionId).Append(',')
              .Append('"').Append(a.Role).Append('"').Append(',')
              .Append(a.IslandId).Append(',')
              .Append('"').Append(a.Label.Replace("\"", "\"\"")).Append('"').Append(',')
              .Append(a.Connected ? "true" : "false").Append(',')
              .Append('"').Append(a.Outcome).Append('"').Append(',')
              .Append(a.Start.x.ToString("F1")).Append(',')
              .Append(a.Start.y.ToString("F1")).Append(',')
              .Append(a.End.x.ToString("F1")).Append(',')
              .Append(a.End.y.ToString("F1")).Append(',')
              .Append(a.DirectDistance.ToString("F1")).Append(',')
              .Append(a.RouteLength.ToString("F1")).Append(',')
              .Append(a.CrossingsOnRoad).Append(',')
              .Append(a.Iterations).Append(',')
              .Append(a.UniqueExpandedCells).Append(',')
              .Append(a.BlockedMoves).Append(',')
              .Append(a.CrossingsTaken).Append(',')
              .Append(a.ClosestApproach.ToString("F1")).Append(',')
              .Append(a.ClosestPoint.x.ToString("F1")).Append(',')
              .Append(a.ClosestPoint.y.ToString("F1")).Append(',')
              .Append(a.SettledMin.x.ToString("F1")).Append(',')
              .Append(a.SettledMin.y.ToString("F1")).Append(',')
              .Append(a.SettledMax.x.ToString("F1")).Append(',')
              .Append(a.SettledMax.y.ToString("F1"));
            foreach (CrossingRejection cause in causes)
            {
                a.Rejections.TryGetValue(cause, out int count);
                sb.Append(',').Append(count);
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
