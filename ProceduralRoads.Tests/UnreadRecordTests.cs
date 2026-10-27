using System.Collections.Generic;
using System.Linq;
using ProceduralRoads;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;

/// <summary>
/// A saved stair, approach or icon record this build cannot read is logged as an error, kept as saved and written back
/// unchanged on every save, with nothing new under its key. Dropped at load and saved back empty, a
/// world saved by this release lost its staircases for good.
/// </summary>
public class UnreadRecordTests : System.IDisposable
{
    private readonly ValheimWorldScope _world = new ValheimWorldScope();

    public void Dispose()
    {
        RoadNetworkPersistence.Reset(); RoadSpatialGrid.Clear(); StructureAppendQueue.Reset(); StairStructures.Reset();
        _world.Dispose();
    }

    private static void Save() => RoadNetworkPersistence.SaveGlobalRoadData(new List<(Vector2, string)>(), new List<RoadCrossing>(), new HashSet<Vector2s>());
    private static bool Load() => RoadNetworkPersistence.TryLoadGlobalRoadData(new List<(Vector2, string)>(), new List<RoadCrossing>(), new HashSet<Vector2s>());

    [Theory]
    [InlineData("ProceduralRoads_StairRuns")]
    [InlineData("ProceduralRoads_SiteApproaches")]
    [InlineData("ProceduralRoads_HiddenSiteIcons")]
    public void ARecordThatCannotBeReadIsWrittenBackAsItWas(string key)
    {
        _world.WithZdos();
        RoadNetworkPersistence.Reset(); RoadSpatialGrid.Clear(); StructureAppendQueue.Reset();
        RoadNetworkPersistence.EnsureMetadataInstance();
        RoadSpatialGrid.CommitLevelled(new List<Vector2> { new(0, 0), new(8, 0), new(16, 0) }, new List<float> { 40, 40, 40 }, 4f);
        RoadSpatialGrid.FinalizeRoadNetwork();
        Save();
        byte[] damaged = { 99, 1, 2, 3, 4, 5, 6 };
        RoadNetworkPersistence.SetSavedRecord(key, damaged);

        RoadSpatialGrid.Clear();
        Assert.True(Load(), "the roads did not load");
        if (key == "ProceduralRoads_StairRuns") Assert.True(RoadNetworkPersistence.StairRecordKept);
        Save();
        Save();

        Assert.Equal(damaged, RoadNetworkPersistence.SavedRecord(key));
    }
}
