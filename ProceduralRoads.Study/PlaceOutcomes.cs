using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ProceduralRoads.Study;

/// <summary>
/// One row per place, per run: what the run's own metrics decided about it.
///
/// The study reports three counts that never agree - selected, planned and
/// built, served - and for most of its life it reported only the counts. A
/// count cannot say whether two plans that serve 123 places each serve the
/// SAME 123, and that is the question a shortlist turns on. This writes the
/// identities, using exactly the definitions <see cref="NetworkMetrics"/>
/// uses, so the sets and the headline numbers cannot drift apart.
/// </summary>
internal static class PlaceOutcomes
{
    /// <summary>
    /// What a place is, decided by the generator's own priority table rather
    /// than by a name pattern: the table is what makes a place eligible at
    /// all, so every eligible place has exactly one category here.
    /// </summary>
    public static string Category(string name)
    {
        if (Eligible.IsBoss(name)) return "boss";
        switch (name)
        {
            case "Crypt3":
            case "Crypt4":
            case "SunkenCrypt4":
            case "MountainCave02":
            case "TrollCave02":
                return "dungeon";
            case "WoodVillage1":
            case "WoodFarm1":
            case "SwampHut5":
                return "settlement";
        }
        if (name.StartsWith("Mistlands_")) return "mistlands";
        if (!Eligible.IsRoadLocation(name)) return "not-eligible";
        return "ruin";
    }

    public static string ToCsv(
        IReadOnlyList<Program.Location> places,
        IReadOnlyList<RoadRoute> routes,
        IReadOnlyList<RoadAttempt> attempts,
        IReadOnlyList<Island> islands)
    {
        StringBuilder sb = new();
        sb.Append("name,category,x,z,radius,priority,island_id,eligible,selected,")
          .Append("planned_and_built,served,nearest_road_end_m,serving_reach_m\n");

        // Selection is recorded per island by the generator, keyed the same way
        // the selection CSV is: name and rounded position.
        HashSet<(string, int, int)> selected = new();
        foreach (RoadSelectionLog.Entry entry in RoadSelectionLog.Entries)
            if (entry.Selected)
                selected.Add((entry.Name, Mathf.RoundToInt(entry.Position.x), Mathf.RoundToInt(entry.Position.z)));

        foreach (Program.Location place in places)
        {
            Vector2 at = new(place.Position.x, place.Position.z);
            float reach = Mathf.Max(NetworkMetrics.ServedRadius, place.Radius);

            bool planned = false;
            foreach (RoadAttempt attempt in attempts)
            {
                if (!attempt.Connected) continue;
                if (Vector2.Distance(attempt.Start, at) <= NetworkMetrics.IdentityTolerance
                    || Vector2.Distance(attempt.End, at) <= NetworkMetrics.IdentityTolerance)
                { planned = true; break; }
            }

            float nearest = float.MaxValue;
            bool served = false;
            foreach (RoadRoute route in routes)
            {
                if (route.Points.Count == 0) continue;
                Vector3 first = route.Points[0], last = route.Points[route.Points.Count - 1];
                nearest = Mathf.Min(nearest, Mathf.Min(Flat(first, at), Flat(last, at)));
                // The same test the run's own counts use, not a paraphrase.
                if (NetworkMetrics.Serves(first, at, reach) || NetworkMetrics.Serves(last, at, reach))
                    served = true;
            }

            Island? island = null;
            foreach (Island candidate in islands)
                if (candidate.ContainsPoint(place.Position)) { island = candidate; break; }

            sb.Append('"').Append(place.Name.Replace("\"", "\"\"")).Append("\",")
              .Append(Category(place.Name)).Append(',')
              .Append(F(place.Position.x)).Append(',')
              .Append(F(place.Position.z)).Append(',')
              .Append(F(place.Radius)).Append(',')
              .Append(Eligible.Priority(place.Name)).Append(',')
              .Append(island?.Id ?? -1).Append(',')
              .Append(island != null && Eligible.IsRoadLocation(place.Name) ? "true" : "false").Append(',')
              .Append(selected.Contains((place.Name, Mathf.RoundToInt(place.Position.x),
                  Mathf.RoundToInt(place.Position.z))) ? "true" : "false").Append(',')
              .Append(planned ? "true" : "false").Append(',')
              .Append(served ? "true" : "false").Append(',')
              .Append(nearest == float.MaxValue ? "" : F(nearest)).Append(',')
              .Append(F(reach)).Append('\n');
        }

        return sb.ToString();
    }

    private static float Flat(Vector3 point, Vector2 at)
    {
        float dx = point.x - at.x, dz = point.z - at.y;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static string F(float value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
