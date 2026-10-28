namespace ProceduralRoads;

/// <summary>The panel's pure layout rules. No engine types, so the unit tests compile this file.</summary>
internal static partial class RoadGenerationPanel
{
    internal const int Columns = 2;

    /// <summary>Islands alternate between the two columns: slot 0 left, 1 right, 2 left, and so on. One island
    /// left is the same layout with an empty right column.</summary>
    internal static (int Column, int Row) Cell(int index) => (index % Columns, index / Columns);

    /// <summary>Text size and row height depend only on the viewport height, never on how many islands are
    /// active, so a row does not change size as islands finish. A resolution or UI-scale change still re-lays out.</summary>
    internal static (float FontSize, float RowHeight) Metrics(float viewportHeight)
    {
        float font = System.Math.Max(12f, System.Math.Min(20f, viewportHeight / 30f));
        return (font, font * 3.6f + 14f);
    }
}
