using System.Linq;
using Xunit;

namespace ProceduralRoads.Tests;

public sealed class RoadGenerationPanelTests
{
    [Fact]
    public void IslandsAlternateBetweenTheTwoColumns()
    {
        var cells = Enumerable.Range(0, 7).Select(RoadGenerationPanel.Cell).ToArray();
        Assert.Equal(new[] { (0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (0, 3) }, cells);
    }

    [Fact]
    public void OneIslandIsTheLeftColumnOfTheSameTwoColumnLayout()
    {
        Assert.Equal(2, RoadGenerationPanel.Columns);
        Assert.Equal((0, 0), RoadGenerationPanel.Cell(0));
    }

    [Theory]
    [InlineData(300f)]
    [InlineData(546f)]
    [InlineData(900f)]
    [InlineData(5000f)]
    public void FontAndRowHeightDependOnlyOnTheViewport(float height)
    {
        var (font, row) = RoadGenerationPanel.Metrics(height);
        Assert.InRange(font, 12f, 20f);
        Assert.True(row > font * 3f);
        Assert.Equal((font, row), RoadGenerationPanel.Metrics(height));
    }

    [Fact]
    public void ALargerViewportNeverShrinksTheText()
    {
        Assert.True(RoadGenerationPanel.Metrics(900f).FontSize >= RoadGenerationPanel.Metrics(400f).FontSize);
    }
}
