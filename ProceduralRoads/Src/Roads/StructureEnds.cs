using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// How a built structure's end meets the road: the one owner, so blocks/stairs and roads meet each other consistently. Every viaduct end asks it
/// (BridgeStructures.Ends, DvergrViaduct.Ends). A wooden bridge's ends are described by it (BridgeLayout.Ends) but the road
/// is not yet brought to them through it: that cost reach (RoadNetworkGenerator.MeetsTheRoad). A
/// staircase's foot and top keep their own rule (RoadStairRuns.Feet / Tops), with the same 8 m reach of the
/// road's travel (RoadStairRuns.TravelReach).
/// <para>It decides, for each end:</para>
/// <list type="number">
/// <item>its height from the ground the road arrives on (<see cref="Arrival"/>), over the last <see cref="ApronLength"/>
/// of road before it, never the bank point's own ground: viaduct ends were unreachable when judged on a bank point 2-6 m off the road that came to them;</item>
/// <item>a flush join, or stairs down to the road (<see cref="Decide"/>): their foot on the road, centred, the road brought
/// in along the flight's line (DvergrViaduct.MeetTheRoad);</item>
/// <item>the earthwork that levels the road's last <see cref="ApronLength"/> to the end's height and centres it on the
/// structure's line (RoadSpatialGrid.LevelledEnds, the seam at a bank);</item>
/// <item>the final check that a player walks from the road onto it (<see cref="Reachable"/>): where the road as planned does
/// not meet it, the structure is not built and the road is searched again without it (RoadNetworkGenerator).</item>
/// </list>
/// </summary>
public static class StructureEnds
{
    /// <summary>The road's last stretch before a structure's end that is levelled to the end's height and centred on its
    /// line (extending the earthwork slightly, the last ~6-8 m).</summary>
    public const float ApronLength = 7f;

    /// <summary>
    /// How far the road runs on past its meeting point, under the structure's joining piece: the deck's end module where
    /// the end is flush, the bottom step where it has stairs. The stairs' rule, generalised: a staircase's joining pieces sit ON the road, so the road is
    /// levelled right up under them, and a deck end does the same. The terrain is cut or filled to the meeting height there
    /// and the joining piece stands on it, so no gap is left where the road's earthwork stops.
    /// </summary>
    public const float JoinOverlap = 2f;

    /// <summary>How far the road where it meets an end may stand off the end's height for a player to walk on: about a
    /// step a player takes without a jump.</summary>
    public const float StepTolerance = 0.5f;

    /// <summary>One end of a structure as it meets the road.</summary>
    public readonly struct End
    {
        /// <summary>The middle of the structure's end edge: where its walking surface stops, or its stairs leave it.</summary>
        public readonly Vector2 Edge;
        /// <summary>The way onto the structure, along its line.</summary>
        public readonly Vector2 Up;
        /// <summary>The structure's walking surface at the edge.</summary>
        public readonly float Height;
        /// <summary>Stairs down from the edge, each <see cref="BridgeLayout.StairRise"/> down and
        /// <see cref="BridgeLayout.StairRun"/> out; 0 where the road runs onto the structure flush.</summary>
        public readonly int Steps;
        /// <summary>Whether this end is decided by this owner from the road's arrival. A wooden bridge's run is laid to the
        /// natural ground as before; the road is brought to its foot all the same.</summary>
        public readonly bool Decided;
        public End(Vector2 edge, Vector2 up, float height, int steps, bool decided = true)
        {
            Edge = edge; Up = up; Height = height; Steps = steps; Decided = decided;
        }
        public float Rise => Steps * BridgeLayout.StairRise;
        /// <summary>Where the road ends: the foot of the stairs, or the edge.</summary>
        public Vector2 Meet => Edge - Up * (BridgeLayout.StairRun * Steps);
        /// <summary>The road's height there.</summary>
        public float MeetHeight => Height - Rise;
        /// <summary>The far end of the road's levelled apron, on the structure's line.</summary>
        public Vector2 Apron => Meet - Up * ApronLength;
        /// <summary>Where the road ends: <see cref="JoinOverlap"/> past the meeting point, under the joining piece.</summary>
        public Vector2 Joint => Meet + Up * JoinOverlap;
        /// <summary>Whether <paramref name="p"/> lies on the stretch the road runs on under the joining piece (from the
        /// meeting point to <see cref="Joint"/>, <paramref name="halfWidth"/> either side of the line).</summary>
        public bool UnderTheJoint(Vector2 p, float halfWidth)
        {
            float along = Vector2.Dot(p - Meet, Up);
            Vector2 across = new(-Up.y, Up.x);
            return along >= -0.25f && along <= JoinOverlap + 0.25f && Mathf.Abs(Vector2.Dot(p - Meet, across)) <= halfWidth;
        }
        public bool IsSet => Up != default;
    }

    /// <summary>
    /// The height the road arrives at, outward of <paramref name="from"/> along <paramref name="outward"/> over
    /// <see cref="ApronLength"/>: the mean of the ground there, every metre. Where the bank is a steep rim the road comes
    /// to on its top (for example 45.0 m at the bank point, 51.4 m eight metres out), this is the road's height, not
    /// the rim's foot.
    /// </summary>
    public static float Arrival(Vector2 from, Vector2 outward, Func<Vector2, float> ground)
    {
        float sum = 0f;
        int n = 0;
        for (float d = 0f; d <= ApronLength + 1e-3f; d += 1f, n++)
            sum += ground(from + outward * d);
        return sum / n;
    }

    /// <summary>
    /// An end at <paramref name="edge"/> whose walking surface is <paramref name="height"/>: the stairs, 0 to
    /// <paramref name="mostSteps"/> of them, whose foot sits nearest the ground the road arrives on beyond it (the mean
    /// over the apron from the foot on); none where that is within half a rise. Ties go to fewer steps.
    /// </summary>
    public static End Decide(Vector2 edge, Vector2 up, float height, Func<Vector2, float> ground, int mostSteps = BridgeLayout.MaxStairSteps)
    {
        int best = 0;
        float bestOff = float.MaxValue;
        for (int s = 0; s <= mostSteps; s++)
        {
            Vector2 foot = edge - up * (BridgeLayout.StairRun * s);
            float off = Mathf.Abs(height - s * BridgeLayout.StairRise - Arrival(foot, -up, ground));
            if (off < bestOff - 1e-3f) { best = s; bestOff = off; }
            // Past a flush end that already meets the arriving road, no stairs.
            if (s == 0 && off <= BridgeLayout.StairRise * 0.5f) break;
        }
        return new End(edge, up, height, best);
    }

    /// <summary>
    /// The finished ground at <paramref name="p"/> near a structure's ends: the generator's, cut to the meeting height where
    /// the road runs on under a joining piece (<see cref="End.UnderTheJoint"/>). A structure judges which of its pieces lie
    /// wholly under the ground on this, so the deck's end module is not dropped as buried under a bank the road then cuts
    /// away (the deck was one module short of the levelled road).
    /// </summary>
    public static float FinishedGround(Vector2 p, float generator, End near, End far, float halfWidth)
    {
        foreach (End end in new[] { near, far })
            if (end.IsSet && end.UnderTheJoint(p, halfWidth)) generator = Mathf.Min(generator, end.MeetHeight);
        return generator;
    }

    /// <summary>Whether a player walks from road at <paramref name="roadHeight"/> where the road meets
    /// <paramref name="end"/> onto it.</summary>
    public static bool Reachable(float roadHeight, End end) => Mathf.Abs(roadHeight - end.MeetHeight) <= StepTolerance;
    // ------------------------------------------------------- the built road

    /// <summary>
    /// The road as built near <paramref name="at"/>, within <paramref name="reach"/>: every road point laid before
    /// (RoadSpatialGrid, the network so far) and this road's own planned pieces, walked every metre. The built-result
    /// checks judge a structure on this, not on the road as one search planned it (a check that looked only at
    /// the ends of this road's own pieces never saw another road under a deck, or a fill under a pier).
    /// </summary>
    internal static IEnumerable<(Vector2 p, float h, float w)> BuiltRoadNear(Vector2 at, float reach, IReadOnlyList<RoadSpatialGrid.PlannedPath>? plans)
    {
        foreach (RoadSpatialGrid.RoadPoint rp in RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(at.x, 0f, at.y), reach))
            if (rp.IsRoad) yield return (rp.p, rp.h, rp.w);
        if (plans == null) yield break;
        float reach2 = reach * reach;
        foreach (RoadSpatialGrid.PlannedPath plan in plans)
        {
            int n = plan.Points.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = plan.Points[i];
                float ha = plan.Heights[i];
                if ((a - at).sqrMagnitude <= reach2) yield return (a, ha, plan.Width);
                if (i + 1 >= n) continue;
                Vector2 b = plan.Points[i + 1];
                float len = Vector2.Distance(a, b);
                for (float d = 1f; d < len; d += 1f)
                {
                    Vector2 q = Vector2.Lerp(a, b, d / len);
                    if ((q - at).sqrMagnitude <= reach2) yield return (q, Mathf.Lerp(ha, plan.Heights[i + 1], d / len), plan.Width);
                }
            }
        }
    }
}
