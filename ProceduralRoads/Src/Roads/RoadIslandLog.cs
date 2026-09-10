using System.Collections.Generic;
using System.Text;

namespace ProceduralRoads;

/// <summary>
/// What each island cost and what it got, measured around the island's own
/// pass through the generator.
///
/// A whole-world runtime cannot be divided by the island count: islands differ
/// by two orders of magnitude in area, and a search that runs out of frontier
/// spends its budget on whatever ground it can reach. The only honest way to
/// say what one island's regeneration costs is to time one island, which is
/// what this records.
///
/// Study instrument (branch study/road-network-strategies). Not part of any PR.
/// </summary>
public static class RoadIslandLog
{
    public sealed class Entry
    {
        public int IslandId;
        public float AreaKm2;
        public int Ring;
        public int Candidates;
        public int Selected;
        public double Seconds;
        /// <summary>Roads committed while this island was generating.</summary>
        public int Roads;
        /// <summary>Attempt rows written while this island was generating.</summary>
        public int Searches;
        public int FailedSearches;
        /// <summary>Connections opened while this island was generating.</summary>
        public int Connections;
        /// <summary>Cells the searches on this island settled, added up.</summary>
        public long SettledCells;
    }

    private static readonly List<Entry> m_entries = new();

    public static IReadOnlyList<Entry> Entries => m_entries;

    public static void Clear() => m_entries.Clear();

    public static void Add(Entry entry) => m_entries.Add(entry);

    public static string ToCsv()
    {
        StringBuilder sb = new();
        sb.Append("island_id,area_km2,ring,candidates,selected,roads,searches,failed_searches,")
          .Append("connections,settled_cells,seconds\n");
        foreach (Entry e in m_entries)
        {
            sb.Append(e.IslandId).Append(',')
              .Append(e.AreaKm2.ToString("F2")).Append(',')
              .Append(e.Ring).Append(',')
              .Append(e.Candidates).Append(',')
              .Append(e.Selected).Append(',')
              .Append(e.Roads).Append(',')
              .Append(e.Searches).Append(',')
              .Append(e.FailedSearches).Append(',')
              .Append(e.Connections).Append(',')
              .Append(e.SettledCells).Append(',')
              .Append(e.Seconds.ToString("F3")).Append('\n');
        }
        return sb.ToString();
    }
}
