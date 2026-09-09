using System;
using System.IO;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The world read back from a dump: exact where the dump has a sample,
/// bilinear between samples, finer tiles first, and an error - never a guess -
/// outside the dumped area.
/// </summary>
public class CsvWorldTests : IDisposable
{
    private readonly string m_dir = Path.Combine(Path.GetTempPath(), "csvworld-" + Guid.NewGuid().ToString("N"));

    public CsvWorldTests() => Directory.CreateDirectory(m_dir);

    public void Dispose()
    {
        try { Directory.Delete(m_dir, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A dump whose height is a known function of position, so an
    /// interpolated answer can be checked against the truth.</summary>
    private string WriteGrid(string name, float x0, float z0, float step, int n,
        bool baseHeight = false, bool riverWidth = false, Func<float, float, float>? height = null)
    {
        height ??= (x, z) => 30f + x * 0.01f + z * 0.02f;
        string path = Path.Combine(m_dir, name);
        using StreamWriter writer = new(path);
        writer.Write("x,z,height,biome,river");
        if (baseHeight) writer.Write(",base_height");
        if (riverWidth) writer.Write(",river_width");
        writer.Write('\n');
        for (int iz = 0; iz < n; iz++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                float x = x0 + ix * step, z = z0 + iz * step;
                float h = height(x, z);
                writer.Write($"{x},{z},{h},{(h < 30f ? "Ocean" : "Meadows")},{(h < 30f ? 1f : 0f)}");
                if (baseHeight) writer.Write($",{0.05f + (h - 30f) / 200f}");
                if (riverWidth) writer.Write($",{(h < 30f ? 64f : 0f)}");
                writer.Write('\n');
            }
        }
        return path;
    }

    [Fact]
    public void ASampleIsReturnedUnchangedAndOffGridIsBilinear()
    {
        var world = new CsvWorld().Load(WriteGrid("w.csv", -64f, -64f, 8f, 17));

        // On a sample: the dumped value, not an interpolation of it.
        Assert.Equal(30f + 0f * 0.01f + 0f * 0.02f, world.GetHeight(0f, 0f), 4);
        Assert.Equal(30f + 24f * 0.01f + -16f * 0.02f, world.GetHeight(24f, -16f), 4);

        // Between samples: bilinear. The dumped surface is linear in x and z,
        // so bilinear reproduces it exactly here - which is the point: the
        // error comes from terrain that is not linear, not from the method.
        Assert.Equal(30f + 4f * 0.01f + 4f * 0.02f, world.GetHeight(4f, 4f), 4);
        Assert.Equal(30f + 11.3f * 0.01f + -11.3f * 0.02f, world.GetHeight(11.3f, -11.3f), 3);
    }

    [Fact]
    public void OutsideTheDumpItThrowsInsteadOfGuessing()
    {
        var world = new CsvWorld().Load(WriteGrid("w.csv", -64f, -64f, 8f, 17));
        Assert.True(world.Covers(64f, 64f));
        Assert.False(world.Covers(72f, 0f));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => world.GetHeight(72f, 0f));
        Assert.Contains("outside every dump", error.Message);
        Assert.Throws<InvalidOperationException>(() => world.GetBiome(0f, -1000f));
        Assert.Throws<InvalidOperationException>(() =>
            world.GetRiverWeight(0f, 1000f, out float _, out float _));
    }

    [Fact]
    public void AFinerWindowAnswersInsideItsOwnBounds()
    {
        // A coarse map that says 40 m everywhere, and an 8 m window over the
        // middle that says 60 m: inside the window the finer tile wins.
        string coarse = WriteGrid("coarse.csv", -1280f, -1280f, 128f, 21, height: (x, z) => 40f);
        string window = WriteGrid("window.csv", -64f, -64f, 8f, 17, height: (x, z) => 60f);
        var world = new CsvWorld().Load(coarse).Load(window);

        Assert.Equal(60f, world.GetHeight(0f, 0f), 4);
        Assert.Equal(60f, world.GetHeight(63f, -63f), 4);
        Assert.Equal(40f, world.GetHeight(600f, 600f), 4);
        Assert.Contains("step 8", world.Describe());
        Assert.Contains("step 128", world.Describe());
    }

    [Fact]
    public void IslandDetectionNeedsTheBaseHeightColumnAndSaysSoWhenItIsMissing()
    {
        var without = new CsvWorld().Load(WriteGrid("w.csv", -128f, -128f, 128f, 3));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => without.GetBaseHeight(0f, 0f, false));
        Assert.Contains("no base_height column", error.Message);
        Assert.Contains("no base_height column", string.Join(" ", without.Approximations));

        var with = new CsvWorld().Load(WriteGrid("b.csv", -128f, -128f, 128f, 3, baseHeight: true));
        // Exact at a sample of the 128 m grid, which is where island detection
        // asks: 0.05 + (h - 30) / 200 for the dumped h.
        Assert.Equal(0.05f + (30f + 128f * 0.01f + 0f * 0.02f - 30f) / 200f,
            with.GetBaseHeight(128f, 0f, false), 5);
    }

    [Fact]
    public void RiverWidthIsZeroAndSaidToBeUnknownWhenTheDumpLacksIt()
    {
        var without = new CsvWorld().Load(WriteGrid("w.csv", -64f, -64f, 8f, 17,
            height: (x, z) => x < 0f ? 26f : 34f));
        without.GetRiverWeight(-32f, 0f, out float weight, out float width);
        Assert.Equal(1f, weight, 4);
        Assert.Equal(0f, width);
        Assert.Contains("no river_width column", string.Join(" ", without.Approximations));

        var with = new CsvWorld().Load(WriteGrid("r.csv", -64f, -64f, 8f, 17, riverWidth: true,
            height: (x, z) => x < 0f ? 26f : 34f));
        with.GetRiverWeight(-32f, 0f, out float _, out float knownWidth);
        Assert.Equal(64f, knownWidth, 4);
    }

    [Fact]
    public void ABiomeIsTheNearestSamplesLabelNotAnAverage()
    {
        var world = new CsvWorld().Load(WriteGrid("w.csv", -64f, -64f, 8f, 17,
            height: (x, z) => x < 0f ? 26f : 34f));
        Assert.Equal(Heightmap.Biome.Ocean, world.GetBiome(-8f, 0f));
        Assert.Equal(Heightmap.Biome.Meadows, world.GetBiome(8f, 0f));
        // Halfway between an ocean sample and a meadows sample the answer is
        // one of the two, never something in between.
        Heightmap.Biome edge = world.GetBiome(-4f, 0f);
        Assert.True(edge == Heightmap.Biome.Ocean || edge == Heightmap.Biome.Meadows);
    }

    [Fact]
    public void ADumpThatIsNotAnEvenGridIsRefused()
    {
        string path = Path.Combine(m_dir, "ragged.csv");
        File.WriteAllText(path, "x,z,height,biome,river\n0,0,30,Meadows,0\n8,0,30,Meadows,0\n20,0,30,Meadows,0\n" +
                                "0,8,30,Meadows,0\n8,8,30,Meadows,0\n20,8,30,Meadows,0\n");
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => new CsvWorld().Load(path));
        Assert.Contains("not evenly spaced", error.Message);

        string missing = Path.Combine(m_dir, "holey.csv");
        File.WriteAllText(missing, "x,z,height,biome,river\n0,0,30,Meadows,0\n8,0,30,Meadows,0\n0,8,30,Meadows,0\n");
        Assert.Throws<InvalidDataException>(() => new CsvWorld().Load(missing));
    }

    [Fact]
    public void TheApproximationsAreStatedSoARunCanCarryThem()
    {
        var world = new CsvWorld().Load(WriteGrid("w.csv", -64f, -64f, 8f, 17, baseHeight: true, riverWidth: true));
        string notes = string.Join(" ", world.Approximations);
        Assert.Contains("bilinear", notes);
        Assert.Contains("biome blending", notes);
        Assert.DoesNotContain("no river_width", notes);
        Assert.DoesNotContain("no base_height", notes);
    }
}
