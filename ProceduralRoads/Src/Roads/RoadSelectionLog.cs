using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Which places an island offered and which of them the quota took.
///
/// Without this, "selected" and "attempted" cannot be told apart: a place that
/// lost its island's quota and a place that was chosen and then failed to
/// connect both end up as no road, and they are different findings.
///
/// Study instrument (branch study/road-network-strategies). Not part of any PR.
/// </summary>
public static class RoadSelectionLog
{
    public sealed class Entry
    {
        public int IslandId;
        public string Name = "";
        public Vector3 Position;
        public float Radius;
        public int Priority;
        /// <summary>Whether the island's quota took it.</summary>
        public bool Selected;
    }

    public static bool Enabled = true;

    private static readonly List<Entry> m_entries = new();

    public static IReadOnlyList<Entry> Entries => m_entries;

    public static void Clear() => m_entries.Clear();

    /// <summary>
    /// One island's candidates and the subset the quota kept. Called once per
    /// island that reached selection, so an island whose places were all
    /// filtered out earlier contributes no rows - and that absence is itself
    /// visible against the full location list.
    /// </summary>
    public static void Record(
        Island island,
        IReadOnlyList<(string name, Vector3 position, float radius)> candidates,
        IReadOnlyList<(string name, Vector3 position, float radius)> selected)
    {
        if (!Enabled)
            return;

        foreach ((string name, Vector3 position, float radius) candidate in candidates)
        {
            bool kept = false;
            foreach ((string name, Vector3 position, float radius) chosen in selected)
            {
                if (chosen.name == candidate.name
                    && Vector3.SqrMagnitude(chosen.position - candidate.position) < 1f)
                {
                    kept = true;
                    break;
                }
            }

            m_entries.Add(new Entry
            {
                IslandId = island.Id,
                Name = candidate.name,
                Position = candidate.position,
                Radius = candidate.radius,
                Priority = RoadNetworkGenerator.PriorityOf(candidate.name),
                Selected = kept,
            });
        }
    }

    public static string ToCsv()
    {
        StringBuilder sb = new();
        sb.Append("island_id,name,x,z,radius,priority,selected\n");
        foreach (Entry entry in m_entries)
        {
            sb.Append(entry.IslandId).Append(',')
              .Append('"').Append(entry.Name.Replace("\"", "\"\"")).Append('"').Append(',')
              .Append(entry.Position.x.ToString("F1")).Append(',')
              .Append(entry.Position.z.ToString("F1")).Append(',')
              .Append(entry.Radius.ToString("F1")).Append(',')
              .Append(entry.Priority).Append(',')
              .Append(entry.Selected ? "true" : "false").Append('\n');
        }

        return sb.ToString();
    }
}
