using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// A bridge layout change (BridgeLayout.LayoutVersion) on a world that already has bridges. The hard rule for old
/// worlds: what is built stays built. Zones whose pieces stand keep them; only zones without pieces get the new layout.
/// </summary>
public sealed class BridgeLayoutTests
{
    [Fact]
    public void AStaleLayoutLeavesSpawnedZonesAlone()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        ZDOMan.instance = new ZDOMan();
        RoadSpatialGrid.Clear(); RoadNetworkPersistence.Reset(); RoadNetworkGenerator.Reset();
        try
        {
            RoadSpatialGrid.AddRoadPath(new List<Vector2> { new(-40f, 0f), new(40f, 0f) }, 4f, world);
            RoadSpatialGrid.FinalizeRoadNetwork();
            RoadNetworkPersistence.EnsureMetadataInstance();
            RoadNetworkPersistence.SaveGlobalRoadData(new List<(Vector2 position, string label)>(), new List<RoadCrossing>(),
                new HashSet<Vector2s>());

            // The record an older build wrote: format 1, the narrow single-lane layout, one zone with its pieces.
            var zone = new Vector2s(3, 4);
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1); w.Write(1); w.Write((int)zone.x); w.Write((int)zone.y);
                LegacyWorldTests.Metadata().Set("ProceduralRoads_BridgeZones".GetStableHashCode(), ms.ToArray());
            }
            ZDO piece = ZDOMan.instance.CreateNewZDO(ZoneSystem.GetZonePos(zone), "wood_beam".GetStableHashCode());
            piece.Persistent = true;
            piece.Set(BridgePlans.MarkerHash, 1);
            int cleared = BridgePlacement.ClearedByTests;

            RoadSpatialGrid.Clear(); RoadNetworkPersistence.Reset();
            Assert.True(RoadNetworkGenerator.TryLoadGlobalRoadData());

            Assert.True(RoadNetworkPersistence.BridgeLayoutIsStale);
            Assert.True(BridgePlans.IsSpawned(zone), "the zone's old pieces must stay its pieces");
            Assert.Equal(cleared, BridgePlacement.ClearedByTests);
            Assert.Contains(piece, ZDOMan.instance.Zdos);
            Assert.Equal(1, piece.GetInt(BridgePlans.MarkerHash));
        }
        finally
        {
            RoadNetworkPersistence.Reset(); RoadSpatialGrid.Clear(); RoadNetworkGenerator.Reset();
            ZDOMan.instance = null; WorldGenerator.instance = null;
        }
    }
}
