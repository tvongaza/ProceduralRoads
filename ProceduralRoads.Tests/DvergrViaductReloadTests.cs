using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// A network saved with a valley viaduct gives the viaduct back on a reload: crossings are
/// restored, so <see cref="RoadCrossing.ValleyJump"/> is saved with them, and the reload plans the same pieces and
/// puts the viaduct's keep-out back. Once nothing came back at all: the reader refused every network with a
/// staircase's cuts, and ValleyJump was not saved either.
/// </summary>
public sealed class DvergrViaductReloadTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();

    public DvergrViaductReloadTests()
    {
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
    }

    public void Dispose()
    {
        RoadNetworkGenerator.Reset();
        RoadNetworkPersistence.Reset();
        RoadSpatialGrid.Clear();
        _scope.Dispose();
    }

    /// <summary>A world generated with a valley viaduct, saved and loaded the way a world load does: the crossing comes
    /// back as a viaduct (the same banks, still a valley jump), its pieces are planned again, the same ones, its keep-out
    /// stands again, and the record of the crossing is the same bytes.</summary>
    [Fact]
    public void ASavedAndReloadedNetworkStillYieldsItsViaducts()
    {
        var world = new DvergrViaductTests.MistValley();
        _scope.WithWorld(world).WithZoneSystem();
        foreach ((string name, float x) in new[] { ("StartTemple", -100f), ("Eikthyrnir", 100f) })
            TestLocations.Add(ZoneSystem.instance!, new ZoneSystem.LocationInstance
            {
                m_location = new ZoneSystem.ZoneLocation { m_prefab = new SoftReferenceableAssets.SoftReference<GameObject>(name), m_exteriorRadius = 10f },
                m_position = new Vector3(x, world.GetHeight(x, 0f), 0f),
            });
        _scope.WithZdos();
        RoadNetworkGenerator.Reset();
        bool viaducts = RoadPathfinder.ConfiguredViaducts;
        RoadPathfinder.ConfiguredViaducts = true;
        try
        {
            RoadNetworkGenerator.GenerateRoads(force: true);
        }
        finally
        {
            RoadPathfinder.ConfiguredViaducts = viaducts;
        }

        RoadCrossing before = Assert.Single(RoadNetworkGenerator.GetRoadCrossings(), c => c.ValleyJump);
        Assert.True(DvergrViaduct.Applies(before, world));
        int seed = world.GetSeed();
        List<StructurePiece> planned = BridgeStructures.Solve(before, world, seed);
        Assert.NotEmpty(planned);
        List<Vector2s> zones = StructurePlans.PlannedZones;
        int piecesInZones = zones.Sum(z => StructurePlans.PlannedPieceCount(z));
        byte[] record = RoadNetworkPersistence.SerializeRoadCrossings(RoadNetworkGenerator.GetRoadCrossings());
        Vector2 under = before.Center;
        (int, int) Cell(Vector2 p) => (Mathf.RoundToInt(p.x / RoadPathfinder.CellSize), Mathf.RoundToInt(p.y / RoadPathfinder.CellSize));
        Assert.True(DvergrViaduct.Standing(Cell(under).Item1, Cell(under).Item2));

        RoadNetworkGenerator.SaveGlobalRoadData();
        RoadNetworkGenerator.Reset();
        Assert.Empty(RoadNetworkGenerator.GetRoadCrossings());
        Assert.False(DvergrViaduct.Standing(Cell(under).Item1, Cell(under).Item2));

        Assert.True(RoadNetworkGenerator.TryLoadGlobalRoadData(), $"the network did not load back: {RoadSpatialGrid.LastReadRefusal}");
        RoadCrossing after = Assert.Single(RoadNetworkGenerator.GetRoadCrossings(), c => c.ValleyJump);
        Assert.Equal(before.FromBank, after.FromBank);
        Assert.Equal(before.ToBank, after.ToBank);
        Assert.True(DvergrViaduct.Applies(after, world), DvergrViaduct.Refusal(after, world));
        List<StructurePiece> again = BridgeStructures.Solve(after, world, seed);
        Assert.Equal(planned.Select(p => (p.Prefab, p.Position, p.YawDegrees)), again.Select(p => (p.Prefab, p.Position, p.YawDegrees)));
        Assert.Equal(zones, StructurePlans.PlannedZones);
        Assert.Equal(piecesInZones, zones.Sum(z => StructurePlans.PlannedPieceCount(z)));
        Assert.True(DvergrViaduct.Standing(Cell(under).Item1, Cell(under).Item2), "the viaduct's keep-out did not come back");
        Assert.Equal(record, RoadNetworkPersistence.SerializeRoadCrossings(RoadNetworkGenerator.GetRoadCrossings()));
    }

    [Fact]
    public void AVersionOneCrossingsRecordStillReads()
    {
        // Written before the flags: [1][count] and ten fields a crossing.
        using var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1); w.Write(1);
            foreach (float f in new[] { -24f, 0f, 24f, 0f, 46f, 0f, 0f, 0f }) w.Write(f);
            w.Write((int)CrossingKind.Bridge); w.Write((int)FordStyle.None);
        }
        var loaded = new List<RoadCrossing>();
        Assert.True(RoadNetworkPersistence.DeserializeRoadCrossings(ms.ToArray(), loaded));
        RoadCrossing c = Assert.Single(loaded);
        Assert.False(c.ValleyJump);
        Assert.Equal(new Vector2(24f, 0f), c.ToBank);
        Assert.Equal(2, BitConverter.ToInt32(RoadNetworkPersistence.SerializeRoadCrossings(loaded), 0));
    }
}
