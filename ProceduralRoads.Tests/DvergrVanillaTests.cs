using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Both Dvergr kits against the vanilla prefabs, as read from the game's assets: the pieces' measured sizes, the harbour's stringers, and a piece's roll as
/// spawned.
/// </summary>
public sealed class DvergrVanillaTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();

    public DvergrVanillaTests() { RoadNetworkGenerator.Reset(); RoadSpatialGrid.Clear(); }
    public void Dispose() { RoadNetworkGenerator.Reset(); RoadSpatialGrid.Clear(); _scope.Dispose(); }

    [Fact]
    public void TheViaductPiecesHaveTheirMeasuredSizes()
    {
        // Half-extents (along the deck, across it, up) at the turns the layout gives each piece, from each prefab's
        // colliders.
        Assert.Equal((1f, 1f, 4f), DvergrViaduct.Extents(DvergrViaduct.Footing));        // blackmarble_column_3 2 x 8 x 2
        Assert.Equal((1f, 1f, 1f), DvergrViaduct.Extents(DvergrViaduct.Block));          // blackmarble_2x2x2
        Assert.Equal((1f, 1f, 1f), DvergrViaduct.Extents(DvergrViaduct.Band));           // blackmarble_2x2_enforced
        Assert.Equal((1f, 1f, 1f), DvergrViaduct.Extents(DvergrViaduct.CorbelCorner));   // blackmarble_outcorner 2 x 2 x 2
        Assert.Equal((1f, 0.5f, 1f), DvergrViaduct.Extents(DvergrViaduct.Corbel));       // blackmarble_out_1 2 x 2 x 1
        Assert.Equal((1f, 0.5f, 1f), DvergrViaduct.Extents(DvergrViaduct.Filler));       // blackmarble_2x2x1
        Assert.Equal((1f, 0.5f, 1f), DvergrViaduct.Extents(DvergrViaduct.Arch));         // blackmarble_arch
        Assert.Equal((1f, 1f, 0.05f), DvergrViaduct.Extents(DvergrViaduct.Floor));       // iron_floor_2x2 2 x 0.1 x 2
        Assert.Equal((0.05f, 1f, 1f), DvergrViaduct.Extents(DvergrViaduct.Screen));      // iron_wall_2x2 2 x 2 x 0.1, turned 0/180
        Assert.Equal((1f, 1f, 1f), DvergrViaduct.Extents(DvergrViaduct.HeadLeft));       // blackmarble_head_big01 2 x 2 x 2
        Assert.Equal((1f, 1f, 1f), DvergrViaduct.Extents(DvergrViaduct.HeadRight));      // blackmarble_head_big02
    }

    [Fact]
    public void TheDvergrWoodStringersReachIntoThePlanksAsTheHarboursDo()
    {
        var w = new DvergrViaductTests.MistRiver { Bank = 34f };
        _scope.WithWorld(w);
        RoadCrossing c = DvergrViaductTests.River();
        float deck = BridgeLayout.DeckHeight(c, w);
        var pieces = DvergrWoodBridge.Solve(c, w, 5);
        float plank = Assert.Single(pieces.Where(p => p.Kind == StructurePieceKind.Deck).Select(p => p.Position.y).Distinct());
        Assert.Equal(deck - 0.4f, plank, 3);
        // Mistlands_Harbour1 fort (1)/pier: planks centred y 1.5, stringers y 1.0, both 0.8 m deep.
        Assert.All(pieces.Where(p => p.Kind == StructurePieceKind.Beam), p => Assert.Equal(plank - 0.5f, p.Position.y, 3));
    }
}

/// <summary>A piece's roll reaches the game: the spawner turned pieces by pitch and yaw only, so a hanging vine (roll 180)
/// and the plain kit's toppled debris (rolled up to 30 degrees) came out upright.</summary>
public sealed class StructureRollTests : IDisposable
{
    private readonly ValheimWorldScope _world = new ValheimWorldScope()
        .WithWorld(new BridgeTests.WideRiverWorld()).WithZdos().WithZoneSystem().WithScene().AsServer();

    public StructureRollTests()
    {
        RoadNetworkGenerator.Reset(); RoadTerrainModifier.ResetDebugCounters();
        RoadNetworkGenerator.MarkLocationsReady(); RoadSiteProtection.Set(Array.Empty<RoadSiteProtection.Footprint>());
        RoadGrade.Configured = .35f; RoadNetworkGenerator.RoadWidth = 4;
        var island = new Island { Id = 1, CellSize = 8, WorldOffset = 0, Min = new Vector2(-216, -120), Max = new Vector2(216, 120) };
        for (int x = -27; x <= 27; x++) for (int z = -15; z <= 15; z++) island.Cells.Add(new Vector2Int(x, z));
        ManualRoads.Commit(ManualRoads.Prepare(new[] { new Vector2(-160, 0), new Vector2(160, 0) }, false, new[] { island }));
    }
    public void Dispose() { NetworkCutoff.Forget(); RoadNetworkGenerator.Reset(); _world.Dispose(); }

    [Fact]
    public void ARollIsPartOfThePiecesTurn()
    {
        // A vine hanging as vanilla's do (Mistlands_Viaduct1: yaw 270, roll 180): its up points down.
        var vine = new StructurePiece { Prefab = "vines", YawDegrees = 270f, RollDegrees = 180f };
        Vector3 up = StructurePlacement.Rotation(vine) * new Vector3(0f, 1f, 0f);
        Assert.True(Vector3.Distance(up, new Vector3(0f, -1f, 0f)) < 1e-4f, $"up {up}");
        var tilted = new StructurePiece { Prefab = "vines", PitchDegrees = -26f, YawDegrees = 90f, RollDegrees = 90f };
        Quaternion want = Quaternion.Euler(-26f, 90f, 90f), got = StructurePlacement.Rotation(tilted);
        Assert.True(Mathf.Abs(want.x * got.x + want.y * got.y + want.z * got.z + want.w * got.w) > 0.99999f);
    }

    [Fact]
    public void EveryPieceIsSpawnedWithItsPlannedTurn()
    {
        StructureAppendQueue.Reset();
        List<Vector2s> zones = StructurePlans.PlannedZones;
        var planned = zones.SelectMany(z => StructurePlans.PlanFor(z)!).ToList();
        Assert.NotEmpty(planned);
        foreach (var piece in planned)
            if (ZNetScene.instance!.GetPrefab(piece.Prefab) == null) ZNetScene.instance.AddPrefab(piece.Prefab);
        NetworkCutoff.Forget();
        NetworkCutoff.SnapshotWorld(Array.Empty<Vector2s>());
        NetworkCutoff.UseNewNetwork();
        foreach (Vector2s zone in zones) StructurePlacement.OnZoneSpawned(zone, ZoneSystem.SpawnMode.Full);
        var spawned = ZDOMan.instance!.Zdos.Where(z => z.GetInt(StructurePlans.MarkerHash) == 1).ToList();
        foreach (StructurePiece p in planned)
        {
            ZDO zdo = Assert.Single(spawned, z => Vector3.Distance(z.GetPosition(), p.Position) < 0.01f && z.GetPrefab() == p.Prefab.GetStableHashCode());
            Quaternion want = Quaternion.Euler(p.PitchDegrees, p.YawDegrees, p.RollDegrees);
            Quaternion got = zdo.GetRotation();
            float dot = Mathf.Abs(want.x * got.x + want.y * got.y + want.z * got.z + want.w * got.w);
            Assert.True(dot > 0.99999f, $"{p.Prefab} at {p.Position}: rotation {got} not {want}");
        }
    }
}

/// <summary>Mist lights on Dvergr wood bridges (only some bridges have them, and on those only some posts), mounted as Mistlands_Harbour1's: 0.5 m over a pile's top.</summary>
public sealed class DvergrWoodLightTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();
    private readonly DvergrViaductTests.MistRiver _world = new() { Bank = 34f };
    private readonly RoadCrossing _crossing = DvergrViaductTests.River();

    public DvergrWoodLightTests() { RoadNetworkGenerator.Reset(); RoadSpatialGrid.Clear(); _scope.WithWorld(_world); }
    public void Dispose() { RoadNetworkGenerator.Reset(); RoadSpatialGrid.Clear(); _scope.Dispose(); }

    private static List<StructurePiece> Lights(List<StructurePiece> ps) => ps.Where(p => p.Prefab == DvergrWoodBridge.Light).ToList();

    [Fact]
    public void OnlySomeBridgesAndOnThemOnlySomePilesHaveMistLights()
    {
        int lit = 0, tops = 0, lights = 0;
        const int seeds = 600;
        for (int s = 1; s <= seeds; s++)
        {
            var pieces = DvergrWoodBridge.Solve(_crossing, _world, s);
            var l = Lights(pieces);
            if (l.Count == 0) continue;
            lit++;
            // Every top pile of the bridge, where a light may stand.
            var topPiles = pieces.Where(p => p.Prefab == DvergrWoodBridge.Pile && p.Kind == StructurePieceKind.Post)
                .GroupBy(p => (Mathf.Round(p.Position.x * 100f), Mathf.Round(p.Position.z * 100f))).Select(g => g.OrderByDescending(p => p.Position.y).First())
                .Where(p => !BridgeLayout.InNavigationGap(_crossing, DvergrViaductTests.Along(_crossing, p))
                    && DvergrViaductTests.Along(_crossing, p) > -0.01f && DvergrViaductTests.Along(_crossing, p) < BridgeLayout.BuiltLength(_crossing.Width) + 0.01f).ToList();
            Assert.True(l.Count < topPiles.Count, "every pile lit");
            tops += topPiles.Count - 1;      // one always carries a light
            lights += l.Count - 1;
        }
        // About one bridge in three (DvergrWoodBridge.LitBridgeChance) ...
        Assert.InRange(lit / (float)seeds, 1f / 3f - 0.06f, 1f / 3f + 0.06f);
        // ... and on those a third of the other piles: the harbour's RandomSpawn 33 %.
        Assert.InRange(lights / (float)tops, 0.33f - 0.04f, 0.33f + 0.04f);
    }

    [Fact]
    public void AMistLightStandsHalfAMetreOverAPilesTopTurnedAsThePile()
    {
        int checkedLights = 0;
        for (int s = 1; s <= 60; s++)
        {
            var pieces = DvergrWoodBridge.Solve(_crossing, _world, s);
            foreach (StructurePiece light in Lights(pieces))
            {
                Assert.Equal(StructurePieceKind.Flair, light.Kind);
                StructurePiece top = pieces.Where(p => p.Prefab == DvergrWoodBridge.Pile && Mathf.Abs(p.Position.x - light.Position.x) < 1e-3f
                    && Mathf.Abs(p.Position.z - light.Position.z) < 1e-3f).OrderByDescending(p => p.Position.y).First();
                // Mistlands_Harbour1 fort (1)/pier: dverger_demister (3) at y 5, its pile centred y 2.5 (4 m long, top 4.5).
                Assert.Equal(top.Position.y + DvergrWoodBridge.PileLength * 0.5f + 0.5f, light.Position.y, 3);
                Assert.Equal(top.YawDegrees, light.YawDegrees);
                Assert.Equal(0f, light.PitchDegrees);
                Assert.Equal(0f, light.RollDegrees);
                checkedLights++;
            }
        }
        Assert.True(checkedLights > 10);
    }

    [Fact]
    public void TheLightsAreFixedPerCrossingAndSeed()
    {
        string Sig(List<StructurePiece> ps) => string.Join("|", Lights(ps).Select(p => FormattableString.Invariant($"{p.Position.x:F2},{p.Position.y:F2},{p.Position.z:F2}/{p.HealthFraction:F3}")));
        for (int s = 1; s <= 30; s++)
        {
            Assert.Equal(Sig(DvergrWoodBridge.Solve(_crossing, _world, s)), Sig(DvergrWoodBridge.Solve(_crossing, _world, s)));
            // The complete bridge has the same lights (ruin takes none away, but the navigation gap's piles carry none).
            Assert.Equal(Lights(DvergrWoodBridge.Solve(_crossing, _world, s)).Select(p => p.Position),
                Lights(DvergrWoodBridge.SolveComplete(_crossing, _world, s)).Select(p => p.Position));
        }
        // Worn as every harbour piece (vanilla's location damage, 10-60 %).
        Assert.All(Enumerable.Range(1, 30).SelectMany(s => Lights(DvergrWoodBridge.Solve(_crossing, _world, s))),
            p => Assert.InRange(p.HealthFraction, BridgeLayout.LocationDamageMin, BridgeLayout.LocationDamageMax));
    }

    [Fact]
    public void AMistlandsFordSpanMayHaveThemTooAndThePlainKitNever()
    {
        RoadCrossing ford = RoadCrossing.Between(new Vector2(-31f, 0f), new Vector2(31f, 0f), 26f, new Vector2(-6f, 0f), 20f, CrossingKind.Ford, FordStyle.Span);
        Assert.Equal(BridgeStructures.Kit.DvergrWood, BridgeStructures.KitFor(ford, _world));
        Assert.Contains(Enumerable.Range(1, 40), s => Lights(DvergrWoodBridge.Solve(ford, _world, s)).Count > 0);
        var meadows = new DvergrViaductTests.MistRiver { Bank = 34f, Biome = Heightmap.Biome.Meadows };
        _scope.WithWorld(meadows);
        Assert.All(Enumerable.Range(1, 40), s => Assert.Empty(Lights(BridgeStructures.Solve(_crossing, meadows, s))));
    }
}
