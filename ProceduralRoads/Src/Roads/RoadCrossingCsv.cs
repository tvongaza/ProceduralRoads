using System.Collections.Generic;
using System.Text;

namespace ProceduralRoads;

/// <summary>
/// The river crossings of a network as a table: where the road met water and
/// what was built there.
///
/// Pure, so the same rows come out of a game run and an offline study run.
/// Study instrument (branch study/road-network-strategies).
/// </summary>
public static class RoadCrossingCsv
{
    public static string ToCsv(IReadOnlyList<RoadCrossing> crossings)
    {
        StringBuilder sb = new();
        sb.Append("index,kind,style,center_x,center_z,from_bank_x,from_bank_z,to_bank_x,to_bank_z,")
          .Append("width,water_level,riverbed_height,depth,fairway_width\n");
        for (int i = 0; i < crossings.Count; i++)
        {
            RoadCrossing c = crossings[i];
            sb.Append(i).Append(',')
              .Append(c.Kind).Append(',')
              .Append(c.Style).Append(',')
              .Append(c.Center.x.ToString("F1")).Append(',')
              .Append(c.Center.y.ToString("F1")).Append(',')
              .Append(c.FromBank.x.ToString("F1")).Append(',')
              .Append(c.FromBank.y.ToString("F1")).Append(',')
              .Append(c.ToBank.x.ToString("F1")).Append(',')
              .Append(c.ToBank.y.ToString("F1")).Append(',')
              .Append(c.Width.ToString("F1")).Append(',')
              .Append(c.WaterLevel.ToString("F2")).Append(',')
              .Append(c.RiverbedHeight.ToString("F2")).Append(',')
              .Append((c.WaterLevel - c.RiverbedHeight).ToString("F2")).Append(',')
              .Append(c.FairwayWidth.ToString("F1")).Append('\n');
        }

        return sb.ToString();
    }
}
