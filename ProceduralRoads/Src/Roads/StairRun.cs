using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// A steep section of road realized as a staircase instead of terrain
/// modification: the ground is left natural and stair pieces are placed
/// along the centerline at zone spawn. Vanilla stairs run 2m per 1m rise
/// (grade 0.5); steeper sections read as stairways cut into the slope.
/// </summary>
public sealed class StairRun
{
    public int RouteIndex;

    /// <summary>Path indices of the run's endpoints (pre-spline waypoints).</summary>
    public int FromIndex;
    public int ToIndex;

    public Vector2 FromPos;
    public Vector2 ToPos;

    /// <summary>Centerline waypoints of the run (copied from the path).</summary>
    public List<Vector2> Points = new();

    public float Length;
    public float MaxGrade;
    public Heightmap.Biome Biome;
}

/// <summary>
/// Detects stair runs on a finished path: maximal sequences of segments
/// whose along-path grade is within the stair band. Pure logic — placement
/// happens later from the recorded runs.
/// </summary>
public static class StairRunDetector
{
    /// <summary>Grades below this are ordinary road (terrain-modified).</summary>
    public const float StairMinGrade = 0.35f;

    /// <summary>
    /// Grades above this are not stair-able even with cutting; the pathfinder
    /// should not produce them at cell scale, but spline-scale terrain spikes
    /// can exceed the cell-scale cap.
    /// </summary>
    public const float StairMaxGrade = 1.8f;

    /// <summary>Runs shorter than this are absorbed into the road.</summary>
    public const float MinRunLength = 4f;

    /// <summary>Adjacent runs closer than this merge into one staircase.</summary>
    public const float MergeGapLength = 6f;

    /// <summary>Segments longer than this are subdivided for grading, so
    /// spline-scale terrain spikes between waypoints are not missed.</summary>
    public const float GradeSampleSpacing = 4f;

    public static List<StairRun> Detect(List<Vector2> rawPath, WorldGenerator world)
    {
        List<StairRun> runs = new();
        if (rawPath == null || rawPath.Count < 2 || world == null)
            return runs;

        // Resample long segments so grades are measured at spline scale,
        // keeping a map back to original waypoint indices (the generator
        // splits the ORIGINAL path by FromIndex/ToIndex).
        List<Vector2> path = new() { rawPath[0] };
        List<int> origFloor = new() { 0 };
        List<int> origCeil = new() { 0 };
        for (int i = 1; i < rawPath.Count; i++)
        {
            float len = Vector2.Distance(rawPath[i - 1], rawPath[i]);
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / GradeSampleSpacing));
            for (int k = 1; k <= pieces; k++)
            {
                float t = (float)k / pieces;
                path.Add(rawPath[i - 1] + (rawPath[i] - rawPath[i - 1]) * t);
                origFloor.Add(k == pieces ? i : i - 1);
                origCeil.Add(i);
            }
        }

        // Classify each segment by along-path grade of the natural terrain.
        int n = path.Count;
        bool[] steep = new bool[n - 1];
        float[] grade = new float[n - 1];
        float[] segLen = new float[n - 1];
        float[] height = new float[n];
        for (int i = 0; i < n; i++)
            height[i] = BiomeBlendedHeight.GetBlendedHeight(path[i].x, path[i].y, world);

        for (int i = 0; i < n - 1; i++)
        {
            segLen[i] = Vector2.Distance(path[i], path[i + 1]);
            if (segLen[i] < 0.01f)
                continue;
            grade[i] = Mathf.Abs(height[i + 1] - height[i]) / segLen[i];
            steep[i] = grade[i] >= StairMinGrade && grade[i] <= StairMaxGrade;
        }

        // Merge steep segments separated by short flat gaps, then emit runs.
        int runStart = -1;
        float gap = 0f;
        for (int i = 0; i < n - 1; i++)
        {
            if (steep[i])
            {
                if (runStart < 0)
                    runStart = i;
                gap = 0f;
            }
            else if (runStart >= 0)
            {
                gap += segLen[i];
                if (gap > MergeGapLength)
                {
                    EmitRun(runs, path, grade, segLen, world, runStart, i, gap);
                    runStart = -1;
                    gap = 0f;
                }
            }
        }
        if (runStart >= 0)
            EmitRun(runs, path, grade, segLen, world, runStart, n - 1, 0f);

        // Map run endpoints back to original waypoint indices (conservative:
        // expand to the enclosing original waypoints).
        foreach (StairRun run in runs)
        {
            int f = run.FromIndex, t = run.ToIndex;
            run.FromIndex = origFloor[f];
            run.ToIndex = origCeil[t];
        }

        return runs;
    }

    private static void EmitRun(List<StairRun> runs, List<Vector2> path,
        float[] grade, float[] segLen, WorldGenerator world, int fromSeg, int endExclusive, float trailingGap)
    {
        // Trim the trailing gap segments back off the run.
        int toIndex = endExclusive;
        float gapLeft = trailingGap;
        while (toIndex - 1 > fromSeg && gapLeft > 0f && grade[toIndex - 1] < StairMinGrade)
        {
            gapLeft -= segLen[toIndex - 1];
            toIndex--;
        }

        float length = 0f;
        float maxGrade = 0f;
        for (int i = fromSeg; i < toIndex; i++)
        {
            length += segLen[i];
            maxGrade = Mathf.Max(maxGrade, grade[i]);
        }

        if (length < MinRunLength)
            return;

        StairRun run = new()
        {
            FromIndex = fromSeg,
            ToIndex = toIndex,
            FromPos = path[fromSeg],
            ToPos = path[toIndex],
            Length = length,
            MaxGrade = maxGrade,
        };
        for (int i = fromSeg; i <= toIndex; i++)
            run.Points.Add(path[i]);

        Vector2 mid = (run.FromPos + run.ToPos) * 0.5f;
        run.Biome = world.GetBiome(mid.x, mid.y);
        runs.Add(run);
    }
}
