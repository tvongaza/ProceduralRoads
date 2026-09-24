using System.IO;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The road network version (RoadSpatialGrid.FinalizeRoadNetwork, an FNV hash
/// of the seed and every stored point) pinned for a small fixed network.
///
/// If this test changes, a build would re-apply roads on every existing
/// world. Each zone's terrain compiler is stamped with the version it carries,
/// the version of a loaded network is recomputed from its saved points on every
/// load, and a zone whose stamp no longer matches is written again with the
/// whole network's points -- over every hole players have dug and every floor
/// they have laid in the road since. That must never ship to a production
/// server. (PROCEDURALROADS_LOCK_NETWORK refuses those writes, but an unlocked
/// server has no such guard.) Change the hash only together with a migration
/// that re-stamps existing compilers, and never on a server that is live.
///
/// The points are read from fixed bytes, not generated, so a change to how
/// roads are planned does not move this pin: only a change to how a stored
/// network is hashed (or read) does. The suite runs it on net10.0 and on Mono
/// (net48), whose float hash codes must agree with the game's.
/// </summary>
public class RoadNetworkVersionGoldenTests
{
    private sealed class SeededWorld : WorldGenerator
    {
        public override int GetSeed() => 1234567;
    }

    /// <summary>
    /// Pinned. See the class comment before touching it.
    /// </summary>
    private const int PinnedVersion = 584354106;

    /// <summary>Format 2: three cells, a paint-only point, a negative cell, heights with fractions.</summary>
    private static byte[] FixedNetwork()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(2);   // format: paint-only flag per point
        writer.Write(3);   // cells
        void Point(float x, float y, float w, float h, bool paintOnly)
        {
            writer.Write(x); writer.Write(y); writer.Write(w); writer.Write(h); writer.Write(paintOnly);
        }
        writer.Write(0); writer.Write(0); writer.Write(3);
        Point(1.5f, 2.25f, 4f, 31.75f, false);
        Point(3.5f, 2.5f, 4f, 31.8125f, false);
        Point(5.5f, 2.75f, 4f, 31.875f, false);
        writer.Write(1); writer.Write(0); writer.Write(2);
        Point(33.5f, 3f, 4f, 32.5f, false);
        Point(35.5f, 3.25f, 3.5f, 29.9f, true);
        writer.Write(-1); writer.Write(-2); writer.Write(1);
        Point(-20.125f, -60.5f, 4f, 45.0625f, false);
        return ms.ToArray();
    }

    [Fact]
    public void TheVersionOfAFixedNetworkIsPinned()
    {
        WorldGenerator.instance = new SeededWorld();
        try
        {
            RoadSpatialGrid.Clear();
            Assert.True(RoadSpatialGrid.DeserializeAllRoadPoints(FixedNetwork()));
            Assert.Equal(6, RoadSpatialGrid.TotalRoadPoints);
            Assert.Equal(PinnedVersion, RoadSpatialGrid.RoadNetworkVersion);

            // The save round trip (format 2 as written today) keeps it.
            byte[] saved = RoadSpatialGrid.SerializeAllRoadPoints()!;
            RoadSpatialGrid.Clear();
            Assert.True(RoadSpatialGrid.DeserializeAllRoadPoints(saved));
            Assert.Equal(PinnedVersion, RoadSpatialGrid.RoadNetworkVersion);
        }
        finally
        {
            RoadSpatialGrid.Clear();
            WorldGenerator.instance = null;
        }
    }
}
