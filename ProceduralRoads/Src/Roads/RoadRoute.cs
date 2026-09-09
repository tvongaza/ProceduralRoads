using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>How a stretch of a road's centerline was laid onto the ground.</summary>
public enum RoadSegmentKind
{
    /// <summary>Ordinary road: leveled and painted.</summary>
    Road,
    /// <summary>A waded ford: painted at the ground's own height, not leveled.</summary>
    Wade,
    /// <summary>A raised ford: leveled up to the bank clearance floor.</summary>
    Raise,
    /// <summary>A bridge or a spanned ford: nothing is painted, the pieces carry
    /// the road. Recorded as its two banks so the gap in the centerline is
    /// visible rather than silent.</summary>
    Span,
}

/// <summary>One stretch of a route, in the order it was laid.</summary>
public sealed class RoadRouteSegment
{
    public RoadSegmentKind Kind { get; }
    /// <summary>Index of this segment's first point in <see cref="RoadRoute.Points"/>.</summary>
    public int StartIndex { get; }
    public int Count { get; }

    public RoadRouteSegment(RoadSegmentKind kind, int startIndex, int count)
    {
        Kind = kind;
        StartIndex = startIndex;
        Count = count;
    }
}

/// <summary>
/// The centerline of one generated road, as it was actually laid on the
/// ground: the dense points the spatial grid received, at their final
/// heights, in path order, with the stretch kinds that produced them.
///
/// Study instrument. It records what generation did; it never feeds back
/// into routing or painting.
/// </summary>
public sealed class RoadRoute
{
    public int Index { get; }
    public string Label { get; }
    public float Width { get; }
    public List<Vector3> Points { get; }
    public List<RoadRouteSegment> Segments { get; }
    public float Length { get; }

    public RoadRoute(int index, string label, float width, List<Vector3> points, List<RoadRouteSegment> segments)
    {
        Index = index;
        Label = label;
        Width = width;
        Points = points;
        Segments = segments;
        Length = CalculateLength(points);
    }

    private static float CalculateLength(List<Vector3> points)
    {
        float length = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            float dx = points[i].x - points[i + 1].x;
            float dz = points[i].z - points[i + 1].z;
            length += Mathf.Sqrt(dx * dx + dz * dz);
        }

        return length;
    }
}
