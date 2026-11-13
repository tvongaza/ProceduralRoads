using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// A road laid in a trench or along a ridge: over at least <see cref="MinLength"/> metres of it, the natural ground
/// <see cref="Side"/> metres out to each side is higher on both sides by more than <see cref="Depth"/> (a trench) or lower
/// on both sides by more than it (a ridge). Its sides are read straight across the road and on the two diagonals across
/// it, the worst of the three: a fill climbing a slope stands over the ground on both its diagonals while straight across
/// it the slope may still rise to one side. Never along the road, where any climb is higher ahead and lower behind. A shelf on a hillside, the ground up on one side and down on the other, is
/// neither, however deep its cut or fill; a stretch shorter than <see cref="MinLength"/> is a blip, not a defect. The one
/// test of it: the generator's re-route and staircase fallback, and what they keep, ask it.
/// </summary>
public static class RoadRidges
{
    /// <summary>How far out to each side of the road the natural ground is read, metres.</summary>
    public const float Side = 7f;
    /// <summary>How much higher (a trench) or lower (a ridge) the ground must stand on both sides, metres: a player's height
    /// (<see cref="RoadRockPolicy.PlayerHeight"/>), past which a road reads as a causeway a player cannot step off, or a cut
    /// he cannot see out of. A 4 m depth let a road ride ground 2.6 to 3.2 m under it on both sides for more than 30 m.</summary>
    public const float Depth = RoadRockPolicy.PlayerHeight;
    /// <summary>The shortest stretch that is a trench or a ridge, metres.</summary>
    public const float MinLength = 10f;
    /// <summary>A trench (the ground more than <see cref="Depth"/> over the road on both sides for <see cref="MinLength"/> or
    /// more) is one only where it is this long or longer, metres, or <see cref="TrenchMinDepth"/> deep or deeper: a shorter,
    /// shallower cut is a notch, and is built. A ridge is held to <see cref="MinLength"/> alone.</summary>
    public const float TrenchMinLength = 20f;
    /// <summary>A trench at least this deep (its <see cref="Stretch.Worst"/>), metres, is one at any length from
    /// <see cref="MinLength"/> (<see cref="TrenchMinLength"/>).</summary>
    public const float TrenchMinDepth = 4f;

    /// <summary>Whether a stretch of <see cref="MinLength"/> or more is a trench or ridge: every ridge, and a trench
    /// <see cref="TrenchMinLength"/> long or <see cref="TrenchMinDepth"/> deep.</summary>
    public static bool Counts(Stretch stretch) =>
        stretch.Kind == Kind.Ridge || stretch.Length >= TrenchMinLength || stretch.Worst >= TrenchMinDepth;
    /// <summary>A stretch carries on over a gap of the road shorter than this, metres.</summary>
    public const float Link = 3f;
    /// <summary>Spacing of the points read along a road, metres: none, every planned point (about a metre apart) is read,
    /// so a stretch is measured to the metre the road stands on it (read every 2 m, its ends were placed up to a metre off
    /// either way, and a crest ridden for 11 m read as under 10).</summary>
    public const float Spacing = 0f;

    public enum Kind { Trench, Ridge }

    /// <summary>One trench or ridge: its kind, its length along the road, the most the ground stands over (a trench) or
    /// under (a ridge) the road on its lower (higher) side, and the points read on it.</summary>
    public sealed class Stretch
    {
        public Kind Kind;
        public float Length, Worst;
        public readonly List<Vector2> Points = new();
        /// <summary>Where along its road it runs, metres from the road's first point: from half way back to the reading
        /// before its first to half way on to the reading after its last.</summary>
        public float From, To;
    }

    /// <summary>The ground over the road on both sides (positive: a trench that deep) and under it on both sides (positive: a
    /// ridge that high) at <paramref name="at"/>, the road <paramref name="height"/> high and heading along
    /// <paramref name="heading"/>: the worst of straight across and the two diagonals.</summary>
    public static (float trench, float ridge) Across(Vector2 at, float height, Vector2 heading, Func<Vector2, float> ground)
    {
        Vector2 along = heading.normalized, across = new Vector2(-along.y, along.x);
        float trench = float.NegativeInfinity, ridge = float.NegativeInfinity;
        void Read(Vector2 side)
        {
            float left = ground(at + side * Side), right = ground(at - side * Side);
            trench = Mathf.Max(trench, Mathf.Min(left, right) - height);
            ridge = Mathf.Max(ridge, height - Mathf.Max(left, right));
        }
        Read(across);
        Read((across + along).normalized);
        Read((across - along).normalized);
        return (trench, ridge);
    }

    /// <summary>The trench or ridge the road builds at <paramref name="at"/>: as <see cref="Across"/>, but no deeper than its
    /// own cut or fill over the ground under it. A road at its ground along a crest or down a gully builds neither; one on fill
    /// along a crest, over its ground and both sides, builds a ridge.</summary>
    public static (float trench, float ridge) Built(Vector2 at, float height, Vector2 heading, Func<Vector2, float> ground)
    {
        var (trench, ridge) = Across(at, height, heading, ground);
        float under = ground(at);
        return (Mathf.Min(trench, under - height), Mathf.Min(ridge, height - under));
    }

    /// <summary>The trenches and ridges of at least <paramref name="minLength"/> metres along one road's points and
    /// heights, deeper than <paramref name="depth"/>; <paramref name="built"/>, only what the road builds (<see cref="Built"/>).</summary>
    public static List<Stretch> Find(IReadOnlyList<Vector2> points, IReadOnlyList<float> heights, Func<Vector2, float> ground,
        float minLength = MinLength, float depth = Depth, bool built = false)
    {
        var found = new List<Stretch>();
        int n = Mathf.Min(points.Count, heights.Count);
        if (n < 2) return found;
        Stretch? open = null;
        // Each reading stands for the road half way to the readings beside it: a stretch runs from half way back to the
        // reading before its first to half way on to the reading after its last (the road's own ends where none), so a
        // ridge is measured at its length, not short by up to a reading's spacing at each end.
        float openFrom = 0f, openTo = 0f, along = 0f, lastRead = float.NegativeInfinity, previous = float.NaN, afterLast = float.NaN;
        void Close()
        {
            float end = float.IsNaN(afterLast) ? openTo : (openTo + afterLast) / 2f;
            if (open != null && end - openFrom >= minLength) { open.Length = end - openFrom; open.From = openFrom; open.To = end; found.Add(open); }
            open = null;
        }
        for (int i = 0; i < n; i++)
        {
            if (i > 0) along += Vector2.Distance(points[i - 1], points[i]);
            if (i > 0 && i < n - 1 && along - lastRead < Spacing) continue;
            lastRead = along;
            Vector2 heading = points[Mathf.Min(n - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
            if (heading.sqrMagnitude < 1e-6f) continue;
            float before = previous;
            previous = along;
            var (trench, ridge) = built ? Built(points[i], heights[i], heading, ground) : Across(points[i], heights[i], heading, ground);
            Kind? kind = trench > depth ? Kind.Trench : ridge > depth ? Kind.Ridge : null;
            if (open != null)
            {
                bool gap = along - openTo > Link;
                if (float.IsNaN(afterLast) && (kind == null || kind != open.Kind)) afterLast = along;
                if (kind == null ? gap : kind != open.Kind || gap) Close();
            }
            if (kind == null) continue;
            if (open == null) { open = new Stretch { Kind = kind.Value }; openFrom = float.IsNaN(before) ? along : (before + along) / 2f; }
            open.Points.Add(points[i]);
            open.Worst = Mathf.Max(open.Worst, kind == Kind.Trench ? trench : ridge);
            openTo = along;
            afterLast = float.NaN;
        }
        Close();
        return found;
    }

    /// <summary>The trenches and ridges of a road's planned pieces (a ford painted on its bed is ground, not judged), read
    /// along the road as one: the pieces in their order, each joined to the one before where it starts within
    /// <see cref="Link"/> of where that one ends, their stretches placed along the joined road and one carried on into the
    /// next where no more than <see cref="Link"/> of road lies between them. A stretch is measured over the road it covers,
    /// the road between two it joins counted once; a piece that does not start where the one before ends (a carried end,
    /// another leg) starts the road again.</summary>
    public static List<Stretch> Find(IEnumerable<RoadSpatialGrid.PlannedPath> plans, Func<Vector2, float> ground)
    {
        var found = new List<Stretch>();
        Stretch? open = null;
        float offset = 0f;
        Vector2? lastEnd = null;
        void Close()
        {
            if (open != null && open.To - open.From >= MinLength) { open.Length = open.To - open.From; if (Counts(open)) found.Add(open); }
            open = null;
        }
        foreach (var plan in plans)
        {
            int n = Mathf.Min(plan.Points.Count, plan.Heights.Count);
            if (plan.FollowTerrain || n == 0) { Close(); lastEnd = null; continue; }
            if (lastEnd is Vector2 end && Vector2.Distance(end, plan.Points[0]) <= Link) offset += Vector2.Distance(end, plan.Points[0]);
            else { Close(); offset = 0f; }
            foreach (Stretch run in Find(plan.Points, plan.Heights, ground, minLength: 0f))
            {
                float from = offset + run.From, to = offset + run.To;
                if (open != null && (run.Kind != open.Kind || from - open.To > Link)) Close();
                if (open == null) { open = new Stretch { Kind = run.Kind, From = from, To = to }; }
                open.Points.AddRange(run.Points);
                open.Worst = Mathf.Max(open.Worst, run.Worst);
                open.To = Mathf.Max(open.To, to);
            }
            for (int i = 1; i < n; i++) offset += Vector2.Distance(plan.Points[i - 1], plan.Points[i]);
            lastEnd = plan.Points[n - 1];
        }
        Close();
        return found;
    }

    /// <summary>Metres of road in these stretches.</summary>
    public static float Metres(List<Stretch> stretches)
    {
        float metres = 0f;
        foreach (var s in stretches) metres += s.Length;
        return metres;
    }
}
