using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// Two networks compared route by route: how many roads both runs built the
/// same way, and where they parted.
///
/// This is what lets an offline run stand in for the game. The offline terrain
/// is a dump - exact where a query lands on a sample, interpolated between
/// samples - so agreement has to be measured, not assumed, and the routes that
/// disagree are the ones no causal claim may rest on.
///
/// Routes are matched by their endpoints, not by their order: the two runs
/// attempt the same connections but need not record them in the same sequence.
/// </summary>
internal static class Compare
{
    /// <summary>
    /// Two road ends this close are the same connection. A road ends anywhere
    /// on its location's approach circle, so two runs can finish the same road
    /// tens of metres apart and still have built the same thing.
    /// </summary>
    public const float EndpointTolerance = 48f;

    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: roads-study compare <a.routes.csv> <b.routes.csv> [--tolerance M] [--out FILE]");
            return 2;
        }

        string aPath = args[1], bPath = args[2];
        float tolerance = float.Parse(Options.Value(args, "--tolerance") ?? "2", CultureInfo.InvariantCulture);
        string? outPath = Options.Value(args, "--out");

        List<Route> a = ReadRoutes(aPath);
        List<Route> b = ReadRoutes(bPath);
        Console.WriteLine($"a: {Path.GetFileName(aPath)} {a.Count} routes, {a.Sum(r => r.Length) / 1000f:F1} km");
        Console.WriteLine($"b: {Path.GetFileName(bPath)} {b.Count} routes, {b.Sum(r => r.Length) / 1000f:F1} km");

        List<(Route a, Route b)> pairs = new();
        List<Route> onlyA = new();
        HashSet<Route> matched = new();
        HashSet<Route> paired = new();

        // Two passes. A road between the same two places, labelled the same and
        // starting from the same point, is the same road even when the two runs
        // finished it at different points on the destination's approach circle -
        // that is a route that differs, not a route that is missing. Everything
        // else is matched on both endpoints.
        foreach (Route ra in a)
        {
            Route? best = null;
            float bestScore = float.MaxValue;
            foreach (Route rb in b)
            {
                if (matched.Contains(rb) || rb.Label != ra.Label)
                    continue;
                float score = Mathf.Min(
                    Vector2.Distance(ra.Start, rb.Start),
                    Vector2.Distance(ra.Start, rb.End));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = rb;
                }
            }

            if (best != null && bestScore <= EndpointTolerance)
            {
                matched.Add(best);
                paired.Add(ra);
                pairs.Add((ra, best));
            }
        }

        foreach (Route ra in a)
        {
            if (paired.Contains(ra))
                continue;

            Route? best = null;
            float bestScore = float.MaxValue;
            foreach (Route rb in b)
            {
                if (matched.Contains(rb))
                    continue;
                float score = EndpointDistance(ra, rb);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = rb;
                }
            }

            if (best != null && bestScore <= EndpointTolerance)
            {
                matched.Add(best);
                pairs.Add((ra, best));
            }
            else
            {
                onlyA.Add(ra);
            }
        }

        List<Route> onlyB = b.Where(r => !matched.Contains(r)).ToList();

        int same = 0;
        List<(Route a, Route b, Deviation d, float lengthDelta)> deviating = new();
        List<float> medians = new();
        foreach ((Route ra, Route rb) in pairs)
        {
            Deviation deviation = Measure(ra, rb);
            medians.Add(deviation.Median);
            // A route "follows the same line" when the line does, not when
            // every last point does: one point pushed off by a cell at a
            // junction should not condemn a road that is otherwise identical.
            if (deviation.Median <= tolerance)
                same++;
            else
                deviating.Add((ra, rb, deviation, rb.Length - ra.Length));
        }

        deviating.Sort((x, y) => y.d.Median.CompareTo(x.d.Median));
        medians.Sort();

        Console.WriteLine();
        Console.WriteLine($"matched:   {pairs.Count} routes by endpoint (within {EndpointTolerance:F0} m)");
        Console.WriteLine($"agreeing:  {same} of {pairs.Count} follow the same line (median deviation <= {tolerance:F0} m)" +
                          (pairs.Count > 0 ? $" ({100f * same / pairs.Count:F0}%)" : ""));
        if (medians.Count > 0)
            Console.WriteLine($"deviation: median {medians[medians.Count / 2]:F1} m across routes, " +
                              $"p90 {medians[(int)(medians.Count * 0.9f)]:F1} m, worst {medians[medians.Count - 1]:F1} m");
        Console.WriteLine($"deviating: {deviating.Count}");
        Console.WriteLine($"only in a: {onlyA.Count}   only in b: {onlyB.Count}");

        foreach ((Route ra, Route rb, Deviation deviation, float lengthDelta) in deviating.Take(10))
            Console.WriteLine($"   {ra.Label,-34} median {deviation.Median,5:F0} m, worst {deviation.Max,5:F0} m, " +
                              $"{lengthDelta,+6:F0} m longer");
        foreach (Route r in onlyA.Take(6))
            Console.WriteLine($"   only in a: {r.Label,-30} {r.Length:F0} m from ({r.Start.x:F0},{r.Start.y:F0})");
        foreach (Route r in onlyB.Take(6))
            Console.WriteLine($"   only in b: {r.Label,-30} {r.Length:F0} m from ({r.Start.x:F0},{r.Start.y:F0})");

        if (outPath != null)
        {
            using StreamWriter writer = new(outPath);
            writer.WriteLine("state,label_a,label_b,length_a,length_b,median_deviation_m,p90_deviation_m,max_deviation_m,start_x,start_z,end_x,end_z");
            foreach ((Route ra, Route rb) in pairs)
            {
                Deviation deviation = Measure(ra, rb);
                writer.WriteLine(string.Join(",",
                    deviation.Median <= tolerance ? "agree" : "deviate",
                    Quote(ra.Label), Quote(rb.Label),
                    ra.Length.ToString("F0", CultureInfo.InvariantCulture),
                    rb.Length.ToString("F0", CultureInfo.InvariantCulture),
                    deviation.Median.ToString("F1", CultureInfo.InvariantCulture),
                    deviation.P90.ToString("F1", CultureInfo.InvariantCulture),
                    deviation.Max.ToString("F1", CultureInfo.InvariantCulture),
                    ra.Start.x.ToString("F0", CultureInfo.InvariantCulture),
                    ra.Start.y.ToString("F0", CultureInfo.InvariantCulture),
                    ra.End.x.ToString("F0", CultureInfo.InvariantCulture),
                    ra.End.y.ToString("F0", CultureInfo.InvariantCulture)));
            }
            foreach (Route r in onlyA)
                WriteUnmatched(writer, "only_a", r);
            foreach (Route r in onlyB)
                WriteUnmatched(writer, "only_b", r);
            Console.WriteLine($"wrote:     {outPath}");
        }

        return 0;
    }

    private static void WriteUnmatched(StreamWriter writer, string state, Route r) =>
        writer.WriteLine(string.Join(",", state, Quote(r.Label), "", r.Length.ToString("F0", CultureInfo.InvariantCulture),
            "", "", "", "", r.Start.x.ToString("F0", CultureInfo.InvariantCulture), r.Start.y.ToString("F0", CultureInfo.InvariantCulture),
            r.End.x.ToString("F0", CultureInfo.InvariantCulture), r.End.y.ToString("F0", CultureInfo.InvariantCulture)));

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>Endpoint distance, either way round: a route may be recorded
    /// from either end.</summary>
    private static float EndpointDistance(Route a, Route b)
    {
        float forward = Mathf.Max(Vector2.Distance(a.Start, b.Start), Vector2.Distance(a.End, b.End));
        float reverse = Mathf.Max(Vector2.Distance(a.Start, b.End), Vector2.Distance(a.End, b.Start));
        return Mathf.Min(forward, reverse);
    }

    internal struct Deviation
    {
        public float Median;
        public float P90;
        public float Max;
    }

    /// <summary>
    /// How far apart two lines run: for every point of each route, the distance
    /// to the nearest point of the other. Two roads that follow the same line
    /// score near zero however differently they are sampled; the median says
    /// whether the line is the same, and the maximum says how far the worst
    /// part of it strayed.
    /// </summary>
    private static Deviation Measure(Route a, Route b)
    {
        List<float> distances = new();
        Collect(a, b, distances);
        Collect(b, a, distances);
        distances.Sort();
        return new Deviation
        {
            Median = distances[distances.Count / 2],
            P90 = distances[(int)(distances.Count * 0.9f)],
            Max = distances[distances.Count - 1],
        };
    }

    private static void Collect(Route from, Route to, List<float> into)
    {
        foreach (Vector2 point in from.Points)
        {
            float nearest = float.MaxValue;
            foreach (Vector2 other in to.Points)
            {
                float d = Vector2.SqrMagnitude(point - other);
                if (d < nearest) nearest = d;
            }
            into.Add(Mathf.Sqrt(nearest));
        }
    }

    internal sealed class Route
    {
        public string Label = "";
        public List<Vector2> Points = new();
        public Vector2 Start => Points[0];
        public Vector2 End => Points[Points.Count - 1];
        public float Length
        {
            get
            {
                float length = 0f;
                for (int i = 1; i < Points.Count; i++)
                    length += Vector2.Distance(Points[i - 1], Points[i]);
                return length;
            }
        }
    }

    /// <summary>Reads a routes CSV written by road_routes or by a study run.</summary>
    internal static List<Route> ReadRoutes(string path)
    {
        Dictionary<string, Route> routes = new();
        List<Route> order = new();

        using StreamReader reader = new(path);
        string? header = reader.ReadLine() ?? throw new InvalidDataException($"{path}: empty");
        string[] columns = SplitCsv(header);
        int index = Array.IndexOf(columns, "route_index");
        int label = Array.IndexOf(columns, "label");
        int x = Array.IndexOf(columns, "x");
        int z = Array.IndexOf(columns, "z");
        if (index < 0 || label < 0 || x < 0 || z < 0)
            throw new InvalidDataException($"{path}: needs route_index,label,x,z columns");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            string[] cells = SplitCsv(line);
            string key = cells[index];
            if (!routes.TryGetValue(key, out Route? route))
            {
                route = new Route { Label = cells[label].Trim('"') };
                routes[key] = route;
                order.Add(route);
            }

            route.Points.Add(new Vector2(
                float.Parse(cells[x], NumberStyles.Float, CultureInfo.InvariantCulture),
                float.Parse(cells[z], NumberStyles.Float, CultureInfo.InvariantCulture)));
        }

        return order.Where(r => r.Points.Count >= 2).ToList();
    }

    /// <summary>Splits a CSV line, honouring the quotes around a label.</summary>
    private static string[] SplitCsv(string line)
    {
        List<string> cells = new();
        bool quoted = false;
        System.Text.StringBuilder cell = new();
        foreach (char c in line)
        {
            if (c == '"') { quoted = !quoted; cell.Append(c); }
            else if (c == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells.ToArray();
    }
}
