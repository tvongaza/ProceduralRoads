using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// A road staircase's top (one owner): it arrives facing the way its road leaves, only its joining piece lies on that road
/// (else it is laid again beside the road's start), and under ground that stands over it it is carried out over the lip and
/// finished onto the surface (StairLayout.FinishTop).
/// </summary>
public static partial class RoadStairRuns
{
    /// <summary>Road staircase tops finished out over a lip with a crest this generation (<see cref="TopOntoSurface"/>).</summary>
    public static int RoadTopsCrested;

    /// <summary>
    /// A road staircase whose top stops under the ground it leads onto (more than a step-up under it 2.5 m on, the
    /// way the road carries on) is carried out over the lip, up to <see cref="StairLayout.MostClimbedOn"/> stairs, and
    /// finished with the back-to-back crest pair landing on the surface; the road then starts on the surface past the
    /// crest (at 6468.3,-1522.5 the top is better a bit out, doing the double stair ridge to land on the
    /// surface). Where the ground past the top is level with it or under it, the road's earthwork meets it as before.
    /// Only where the road carries on within 60 degrees of the top's heading, and once per plan (a plan is shared
    /// between the roads that walk it). <paramref name="next"/>: the path index the road carries on from.
    /// </summary>
    internal static bool TopOntoSurface(StairPlan plan, List<Vector2> path, int next, Func<float, float, float> ground, out Vector2 past, out float pastHeight)
    {
        past = default; pastHeight = 0f;
        if (!plan.Reached || StairLayout.NoCrest || next < 0 || next >= path.Count) return false;
        lock (plan)
        {
            if (plan.TopCrested is Vector3 done) { past = new Vector2(done.x, done.z); pastHeight = done.y; return true; }
            Vector2 top = new Vector2(plan.End.x, plan.End.z), dir = StairLayout.Forward(plan.EndHeading);
            Vector2 toRoad = path[next] - top;
            if (toRoad.sqrMagnitude < 1e-4f || Vector2.Dot(dir, toRoad.normalized) < 0.5f) return false; // more than 60 degrees off
            Vector2 ahead = top + dir * StairLayout.TopAhead;
            if (ground(ahead.x, ahead.y) - plan.End.y <= StairLayout.TopFloorBand) return false;
            int pieces = plan.Pieces.Count, clearances = plan.Clearances.Count;
            Vector3 end = plan.End;
            int stairs = plan.Stairs;
            float rise = plan.Rise, walked = plan.WalkedLength;
            // Climbed on, then out to where the surface is within the floor's band: a floor level with the top onto it, the
            // road on from its far edge at its height; higher, the crest. Its top row is not a floor to keep the floor off.
            plan.LastRowPieces = -1;
            StairLayout.TopFinish finish = StairLayout.FinishTop(plan, ground, Width, StairLayout.MaxBuriedDepth, _ => true, out _);
            if (finish == StairLayout.TopFinish.TopFloor)
            {
                Vector2 floorEnd = new Vector2(plan.End.x, plan.End.z) + StairLayout.Forward(plan.EndHeading) * StairLayout.PieceRun;
                past = floorEnd;
                pastHeight = plan.End.y;
                plan.TopCrested = new Vector3(past.x, pastHeight, past.y);
                System.Threading.Interlocked.Increment(ref RoadTopsCrested);
                System.Threading.Interlocked.Increment(ref TopFloors);
                return true;
            }
            if (finish == StairLayout.TopFinish.None)
            {
                plan.Pieces.RemoveRange(pieces, plan.Pieces.Count - pieces);
                plan.Clearances.RemoveRange(clearances, plan.Clearances.Count - clearances);
                plan.End = end; plan.Stairs = stairs; plan.Rise = rise; plan.WalkedLength = walked;
                return false;
            }
            Vector2 crestTop = new Vector2(plan.End.x, plan.End.z);
            past = crestTop + StairLayout.Forward(plan.EndHeading) * (2f * StairLayout.PieceRun);
            pastHeight = ground(past.x, past.y);
            plan.TopCrested = new Vector3(past.x, pastHeight, past.y);
            System.Threading.Interlocked.Increment(ref RoadTopsCrested);
            return true;
        }
    }

    /// <summary>Staircase tops finished with a floor out onto the ground this generation (<see cref="StairLayout.AddTopFloor"/>).</summary>
    public static int TopFloors;

    /// <summary>The way the road comes to the foot of the staircase over path[from..to]: its heading over its last
    /// stretch into the lower end (as BesideItsRoad reads it), or null where no road comes to that end.</summary>
    /// <summary>The way the road leaves the top of the staircase over path[from..to]: its heading over its first stretch
    /// on from the higher end, or null where no road leaves that end.</summary>
    internal static float? TopFacing(List<Vector2> path, int from, int to, Func<float, float, float> ground)
    {
        bool reversed = ground(path[to].x, path[to].y) < ground(path[from].x, path[from].y);
        int top = reversed ? from : to, beyond = reversed ? from - 1 : to + 1;
        if (beyond < 0 || beyond >= path.Count) return null;
        return RoadTravel(path, top, beyond, into: false);
    }

    /// <summary>
    /// A staircase over path[from..to] whose top lay on the road it hands to, laid again with its top beside that road's
    /// start, its edge on the road's edge, on each side in turn (the side it comes in from first): the road carries on from
    /// the top's own end (at the top at 6495.5,-1464.9 the top entered the road on the wrong side,
    /// taking over the road itself). Null where neither side lays.
    /// </summary>
    private static StairPlan? TopBesideItsRoad(List<Vector2> path, int from, int to, Vector2 a, Vector2 b, StairKit kit,
        Func<float, float, float> ground, List<Vector2> corridor, int tops, float? facing, float? topFacing, WorldGenerator world,
        List<RoadStairSpan> spans)
    {
        if (topFacing is not float leave) return null;
        bool reversed = ground(b.x, b.y) < ground(a.x, a.y);
        Vector2 top = reversed ? a : b, foot = reversed ? b : a;
        // The side it comes in from: the foot's side of the road leaving the top.
        Vector2 right = StairLayout.Right(leave);
        int first = Vector2.Dot(foot - top, right) >= 0f ? 1 : -1;
        float offset = BesideOffset(0f);
        foreach (int side in new[] { first, -first })
        {
            Vector2 beside = top + right * (side * offset);
            var line = new List<Vector2>(corridor);
            line[reversed ? 0 : line.Count - 1] = beside;
            StairPlan again = reversed
                ? Plan(beside, b, kit, ground, null, null, line, topLandings: tops, retried: true, facing: facing, topFacing: topFacing)
                : Plan(a, beside, kit, ground, null, null, line, topLandings: tops, retried: true, facing: facing, topFacing: topFacing);
            if (Usable(again, path, from, to, world, spans)) { System.Threading.Interlocked.Increment(ref TopsBeside); return again; }
        }
        return null;
    }

    /// <summary>Staircase tops laid again beside the road they hand to this generation (<see cref="TopBesideItsRoad"/>).</summary>
    public static int TopsBeside;

    /// <summary>Settable for tests: false lets every piece within <see cref="JunctionReach"/> of a staircase's top at the road's
    /// level lie on the road.</summary>
    internal static bool TopJoinsOnly = true;
}
