using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Why a river crossing was not available for a blocked move.</summary>
public enum CrossingRejection
{
    /// <summary>Neither fords nor bridges are on, so a blocked move is simply blocked.</summary>
    CrossingsDisabled,
    /// <summary>The move is a knight move: the scan walks whole cells and would
    /// skip the ones the jump passes over.</summary>
    KnightMove,
    /// <summary>Dry ground was reached with no river water behind it: a dip, not a crossing.</summary>
    NoRiver,
    /// <summary>The crossing this site needs (a ford, or a bridge) is switched off.</summary>
    KindDisabled,
    /// <summary>Farther than the crossing kind may span.</summary>
    TooLong,
    /// <summary>A bridge in the Mistlands.</summary>
    MistlandsBridge,
    /// <summary>The two banks differ in height by more than the kind allows.</summary>
    BankDelta,
    /// <summary>The scan ran out of cells without finding a far bank.</summary>
    NoBankFound,
    /// <summary>A far bank was found, but the search has already settled that cell.</summary>
    LandingClosed,
}

/// <summary>
/// Watches one pathfinding attempt from inside the search, so a failure can be
/// explained by what the search did rather than guessed at from its result.
///
/// Study instrument. <see cref="RoadPathfinder.Probe"/> is null in every
/// ordinary run and the search then makes no call at all; nothing an
/// implementation does may change what the pathfinder decides.
/// </summary>
public interface IPathfinderProbe
{
    void AttemptBegan(Vector2 start, Vector2 end, Vector2i startGrid, Vector2i endGrid);
    /// <summary>A cell left the frontier and was settled.</summary>
    void Popped(Vector2i cell, int iteration);
    /// <summary>A move cost at or above the river penalty: impassable as an ordinary step.</summary>
    void MoveBlocked(Vector2i from, Vector2i to);
    void CrossingRejected(Vector2i from, Vector2Int direction, CrossingRejection cause);
    void CrossingAccepted(Vector2i from, Vector2i landing, float cost);
    /// <summary>The attempt ended: a path, an empty frontier, or the iteration budget.</summary>
    void AttemptEnded(bool found, string outcome, int iterations);
}

/// <summary>
/// The default probe: what the search reached, how near it came, and why each
/// blocked move stayed blocked.
/// </summary>
public sealed class PathfinderTrace : IPathfinderProbe
{
    public Vector2 Start { get; private set; }
    public Vector2 End { get; private set; }

    /// <summary>Cells settled, counted once each. The iteration count can
    /// exceed this: the same cell can be re-queued at a lower cost.</summary>
    public int UniqueExpandedCells => m_expanded.Count;
    public int Iterations { get; private set; }
    public bool Found { get; private set; }
    public string Outcome { get; private set; } = "";

    /// <summary>Bounds of the settled set, in grid cells.</summary>
    public int MinX { get; private set; } = int.MaxValue;
    public int MinY { get; private set; } = int.MaxValue;
    public int MaxX { get; private set; } = int.MinValue;
    public int MaxY { get; private set; } = int.MinValue;

    /// <summary>The nearest the search came to the destination, in metres.</summary>
    public float ClosestApproach { get; private set; } = float.MaxValue;
    public Vector2i ClosestCell { get; private set; }

    public int BlockedMoves { get; private set; }
    public int CrossingsAccepted { get; private set; }

    public readonly Dictionary<CrossingRejection, int> Rejections = new();

    private readonly HashSet<Vector2i> m_expanded = new();
    private Vector2i m_endGrid;

    public void AttemptBegan(Vector2 start, Vector2 end, Vector2i startGrid, Vector2i endGrid)
    {
        Start = start;
        End = end;
        m_endGrid = endGrid;
    }

    public void Popped(Vector2i cell, int iteration)
    {
        m_expanded.Add(cell);
        Iterations = iteration;
        if (cell.x < MinX) MinX = cell.x;
        if (cell.y < MinY) MinY = cell.y;
        if (cell.x > MaxX) MaxX = cell.x;
        if (cell.y > MaxY) MaxY = cell.y;

        float dx = (cell.x - m_endGrid.x) * RoadPathfinder.CellSize;
        float dy = (cell.y - m_endGrid.y) * RoadPathfinder.CellSize;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        if (distance < ClosestApproach)
        {
            ClosestApproach = distance;
            ClosestCell = cell;
        }
    }

    public void MoveBlocked(Vector2i from, Vector2i to) => BlockedMoves++;

    public void CrossingRejected(Vector2i from, Vector2Int direction, CrossingRejection cause)
    {
        Rejections.TryGetValue(cause, out int count);
        Rejections[cause] = count + 1;
    }

    public void CrossingAccepted(Vector2i from, Vector2i landing, float cost) => CrossingsAccepted++;

    public void AttemptEnded(bool found, string outcome, int iterations)
    {
        Found = found;
        Outcome = outcome;
        Iterations = iterations;
        if (found)
        {
            // The search returns on reaching the destination cell, before that
            // cell is settled, so it is never popped: a successful attempt
            // would otherwise report the last cell before it as its closest
            // approach - up to a knight move away.
            ClosestApproach = 0f;
            ClosestCell = m_endGrid;
        }
    }

    /// <summary>One line for a log or a table.</summary>
    public string Summary()
    {
        List<string> causes = new();
        foreach (KeyValuePair<CrossingRejection, int> entry in Rejections)
            causes.Add($"{entry.Key}={entry.Value}");
        causes.Sort();
        string bounds = UniqueExpandedCells > 0
            ? $"[{MinX},{MinY}..{MaxX},{MaxY}] ({(MaxX - MinX + 1) * RoadPathfinder.CellSize:F0}x{(MaxY - MinY + 1) * RoadPathfinder.CellSize:F0} m)"
            : "[]";
        return $"{Outcome} after {Iterations} iterations, {UniqueExpandedCells} cells settled, " +
               $"closest {ClosestApproach:F0} m, bounds {bounds}, {BlockedMoves} blocked moves, " +
               $"{CrossingsAccepted} crossings taken" +
               (causes.Count > 0 ? $", rejected {string.Join(" ", causes)}" : "");
    }
}
