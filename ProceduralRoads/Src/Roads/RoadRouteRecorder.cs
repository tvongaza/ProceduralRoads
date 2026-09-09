using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Records the centerline of every road as generation lays it down, so a run
/// can be inspected — and compared with another run — without loading the
/// world again.
///
/// The spatial grid buckets points by cell and loses route identity; this
/// keeps the route. It observes only: <see cref="RoadSpatialGrid.AddRoadPath"/>
/// reports the dense points and final heights it has already computed, and
/// nothing here is read back by generation, routing or painting.
///
/// Study instrument (branch study/road-network-strategies). Not part of any PR.
/// </summary>
public static class RoadRouteRecorder
{
    private static readonly List<RoadRoute> m_routes = new();

    private static bool m_recording;
    private static string m_label = "";
    private static float m_width;
    private static List<Vector3> m_points = new();
    private static List<RoadRouteSegment> m_segments = new();

    public static IReadOnlyList<RoadRoute> Routes => m_routes;

    public static void Clear()
    {
        m_routes.Clear();
        m_recording = false;
        m_points = new List<Vector3>();
        m_segments = new List<RoadRouteSegment>();
    }

    /// <summary>Starts a route. Every path added until <see cref="End"/> is one road.</summary>
    public static void Begin(string label, float width)
    {
        m_recording = true;
        m_label = label;
        m_width = width;
        m_points = new List<Vector3>();
        m_segments = new List<RoadRouteSegment>();
    }

    /// <summary>
    /// One stretch of the current road, as the grid stored it: positions with
    /// the heights the grid actually wrote (smoothed, ramped, floored).
    /// </summary>
    public static void Record(IReadOnlyList<Vector2> points, IReadOnlyList<float> heights, RoadSegmentKind kind)
    {
        if (!m_recording || points == null || heights == null)
            return;

        int start = m_points.Count;
        int count = Mathf.Min(points.Count, heights.Count);
        for (int i = 0; i < count; i++)
            m_points.Add(new Vector3(points[i].x, heights[i], points[i].y));

        if (count > 0)
            m_segments.Add(new RoadRouteSegment(kind, start, count));
    }

    /// <summary>
    /// A crossing nothing was painted over — a bridge, or a ford spanned by
    /// pieces. Recorded as its two banks so the route is continuous and the
    /// unpainted gap is visible.
    /// </summary>
    public static void RecordSpan(Vector3 fromBank, Vector3 toBank)
    {
        if (!m_recording)
            return;

        int start = m_points.Count;
        m_points.Add(fromBank);
        m_points.Add(toBank);
        m_segments.Add(new RoadRouteSegment(RoadSegmentKind.Span, start, 2));
    }

    /// <summary>Closes the current route and keeps it. Null if nothing was laid.</summary>
    public static RoadRoute? End()
    {
        if (!m_recording)
            return null;

        m_recording = false;
        if (m_points.Count < 2)
            return null;

        RoadRoute route = new(m_routes.Count, m_label, m_width, m_points, m_segments);
        m_routes.Add(route);
        return route;
    }

    /// <summary>
    /// One row per centerline point: the route it belongs to, the stretch kind
    /// that laid it, its position and the height the grid holds for it.
    /// </summary>
    public static string ToCsv() => ToCsv(m_routes);

    public static string ToCsv(IReadOnlyList<RoadRoute> routes)
    {
        StringBuilder sb = new();
        sb.Append("route_index,label,width,segment_index,kind,point_index,x,y,z\n");
        foreach (RoadRoute route in routes)
        {
            for (int s = 0; s < route.Segments.Count; s++)
            {
                RoadRouteSegment segment = route.Segments[s];
                for (int i = 0; i < segment.Count; i++)
                {
                    int p = segment.StartIndex + i;
                    Vector3 point = route.Points[p];
                    sb.Append(route.Index).Append(',')
                      .Append('"').Append(Escape(route.Label)).Append('"').Append(',')
                      .Append(route.Width.ToString("F1")).Append(',')
                      .Append(s).Append(',')
                      .Append(segment.Kind).Append(',')
                      .Append(p).Append(',')
                      .Append(point.x.ToString("F1")).Append(',')
                      .Append(point.y.ToString("F1")).Append(',')
                      .Append(point.z.ToString("F1")).Append('\n');
                }
            }
        }

        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
