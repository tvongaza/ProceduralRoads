using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;
using static ProceduralRoads.Tests.DvergrViaductTests;

namespace ProceduralRoads.Tests;

/// <summary>
/// Low Mistlands bridges of the Dvergr harbour pier's wood (DvergrWoodBridge): the kit choice, that only
/// the look changes (deck, ends and stair positions are the plain wooden kit's), piles grounded, determinism, ruin, the navigation
/// gap and the free span. Whether the game's support holds the pier's pieces as laid, and what they drop, has to be
/// measured in game; these tests check the layout by construction.
/// </summary>
public sealed class DvergrWoodBridgeTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();

    public DvergrWoodBridgeTests() { RoadNetworkGenerator.Reset(); RoadSpatialGrid.Clear(); }
    public void Dispose() { RoadNetworkGenerator.Reset(); RoadSpatialGrid.Clear(); _scope.Dispose(); }

    /// <summary>A Mistlands river whose deck stands 4 m over the water (banks at 34 m, the bed at 26 m).</summary>
    private (RoadCrossing crossing, MistRiver world) Low()
    {
        var w = new MistRiver { Bank = 34f };
        _scope.WithWorld(w);
        return (River(), w);
    }

    private static float Ground(StructurePiece p, WorldGenerator w) => BiomeBlendedHeight.GetBlendedHeight(p.Position.x, p.Position.z, w);

    // ------------------------------------------------------------- the kit

    [Fact]
    public void AMistlandsBridgeUnderTheFloorIsDvergrWoodAndNothingElseIs()
    {
        var (c, low) = Low();
        Assert.Equal(BridgeStructures.Kit.DvergrWood, BridgeStructures.KitFor(c, low));
        Assert.Contains(BridgeStructures.Solve(c, low, 1), p => p.Prefab == DvergrWoodBridge.Plank);

        // From 6 m up, at 12 m piers and more (bed 26, banks 45): the viaduct.
        var high = new MistRiver();
        _scope.WithWorld(high);
        Assert.Equal(BridgeStructures.Kit.DvergrViaduct, BridgeStructures.KitFor(c, high));

        // Over the 20 m ceiling: the plain wooden kit, as before.
        var deep = new MistRiver { Bed = 20f };
        _scope.WithWorld(deep);
        Assert.Equal(BridgeStructures.Kit.Wood, BridgeStructures.KitFor(c, deep));

        // The same low river out of the Mistlands: the plain wooden kit.
        var meadows = new MistRiver { Bank = 34f, Biome = Heightmap.Biome.Meadows };
        _scope.WithWorld(meadows);
        Assert.Equal(BridgeStructures.Kit.Wood, BridgeStructures.KitFor(c, meadows));
        Assert.All(BridgeStructures.Solve(c, meadows, 1), p => Assert.StartsWith("wood_", p.Prefab));

        // A ford in the Mistlands, even a spanned one: road or the plain footbridge.
        _scope.WithWorld(low);
        var ford = RoadCrossing.Between(new Vector2(-31f, 0f), new Vector2(31f, 0f), 26f, new Vector2(-6f, 0f), 20f, CrossingKind.Ford, FordStyle.Span);
        Assert.Equal(BridgeStructures.Kit.Wood, BridgeStructures.KitFor(ford, low));
    }

    [Theory]
    [InlineData(5f)]
    [InlineData(11.5f)]
    public void TheFloorIsTheViaductsOwn(float floor)
    {
        // Moving the viaduct's floor moves the line with it: one floor, read in one place.
        float was = DvergrViaduct.MinViaductPier;
        try
        {
            DvergrViaduct.MinViaductPier = Mathf.Max(DvergrViaduct.MinClearance, floor);
            var high = new MistRiver();
            _scope.WithWorld(high);
            RoadCrossing c = River();
            bool under = DvergrViaduct.TallestPier(c, high) < DvergrViaduct.MinViaductPier - 1e-3f;
            Assert.Equal(under ? BridgeStructures.Kit.DvergrWood : BridgeStructures.Kit.DvergrViaduct, BridgeStructures.KitFor(c, high));
        }
        finally { DvergrViaduct.MinViaductPier = was; }
    }

    // ---------------------------------------------- only the look changes

    [Fact]
    public void TheDeckTheEndsAndTheStairsAreThePlainWoodenKits()
    {
        var (c, low) = Low();
        Assert.Equal(BridgeLayout.DeckHeight(c, low), BridgeStructures.DeckHeight(c, low));
        var (n, f) = BridgeStructures.Ends(c, low);
        var (wn, wf) = BridgeLayout.Ends(c, low);
        foreach (var (a, b) in new[] { (n, wn), (f, wf) })
        {
            Assert.Equal(b.Edge, a.Edge);
            Assert.Equal(b.Height, a.Height);
            Assert.Equal(b.Steps, a.Steps);
        }
        // The stairs, piece for piece, where the plain wooden bridge lays them, in the Dvergr stair.
        string Key(StructurePiece p) => FormattableString.Invariant($"{p.Position.x:F3} {p.Position.y:F3} {p.Position.z:F3} {p.YawDegrees:F2}");
        var wood = BridgeLayout.Solve(c, low, 1).Where(p => p.Kind == StructurePieceKind.Stair).Select(Key).OrderBy(k => k).ToList();
        var dvergr = DvergrWoodBridge.Solve(c, low, 1).Where(p => p.Kind == StructurePieceKind.Stair).Select(Key).OrderBy(k => k).ToList();
        Assert.NotEmpty(wood);
        Assert.Equal(wood, dvergr);
        Assert.All(DvergrWoodBridge.Solve(c, low, 1).Where(p => p.Kind == StructurePieceKind.Stair),
            p => Assert.Equal(DvergrWoodBridge.StairPrefab, p.Prefab));
        // The planks' walking surface is the deck's.
        foreach (StructurePiece p in DvergrWoodBridge.Solve(c, low, 1).Where(p => p.Kind == StructurePieceKind.Deck))
            Assert.Equal(BridgeLayout.DeckHeight(c, low), p.Position.y + DvergrWoodBridge.BeamDepth * 0.5f, 3);
    }

    // ------------------------------------------------------ by construction

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void EveryPileColumnReachesTheGroundAndNoneIsBuried(int seed)
    {
        var (c, low) = Low();
        float deck = BridgeLayout.DeckHeight(c, low);
        var piles = DvergrWoodBridge.Solve(c, low, seed).Where(p => p.Prefab == DvergrWoodBridge.Pile).ToList();
        Assert.NotEmpty(piles);
        foreach (var column in piles.GroupBy(p => (Mathf.Round(p.Position.x * 100f), Mathf.Round(p.Position.z * 100f))))
        {
            var stack = column.OrderByDescending(p => p.Position.y).ToList();
            float ground = Ground(stack[0], low);
            // Each pile hangs from the one above (origin at its top, PileLength long), none wholly under the ground,
            // and the lowest one's foot is in it.
            for (int i = 1; i < stack.Count; i++) Assert.Equal(stack[i - 1].Position.y - DvergrWoodBridge.PileLength, stack[i].Position.y, 3);
            Assert.All(stack, p => Assert.True(p.Position.y > ground, $"pile at {p.Position} is wholly under the ground {ground}"));
            Assert.True(stack[stack.Count - 1].Position.y - DvergrWoodBridge.PileLength <= ground, $"pile column at {stack[0].Position} stops above the ground {ground}");
            // A standing bent's piles rise past the deck as the pier's do; a stub stops at the waterline.
            float top = stack[0].Position.y;
            Assert.True(Mathf.Abs(top - (deck + DvergrWoodBridge.PileAboveDeck)) < 1e-3f || top <= c.WaterLevel + 0.3f + 1e-3f, $"pile top {top}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void EveryPlankLiesOverABayWhoseBentsStand(int seed)
    {
        var (c, low) = Low();
        float length = BridgeLayout.BuiltLength(c.Width);
        float[] bents = DvergrWoodBridge.BentAlongs(length);
        var pieces = DvergrWoodBridge.Solve(c, low, seed);
        var standing = new HashSet<int>();
        for (int i = 0; i < bents.Length; i++)
            if (pieces.Any(p => p.Prefab == DvergrWoodBridge.Pile && p.Kind == StructurePieceKind.Post && Mathf.Abs(Along(c, p) - bents[i]) < 1e-2f
                && p.Position.y > BridgeLayout.DeckHeight(c, low)))
                standing.Add(i);
        Assert.Contains(0, standing);
        Assert.Contains(bents.Length - 1, standing);
        foreach (StructurePiece plank in pieces.Where(p => p.Kind == StructurePieceKind.Deck))
        {
            float a = Along(c, plank);
            int bay = Array.FindIndex(bents, b => b >= a) - 1;
            Assert.True(standing.Contains(bay) && standing.Contains(bay + 1), $"plank at {a:F1} m over bay {bay} with a bent down");
        }
        // Every stringer lies under planks of its own bay.
        foreach (StructurePiece s in pieces.Where(p => p.Kind == StructurePieceKind.Beam))
            Assert.Contains(pieces, p => p.Kind == StructurePieceKind.Deck && Mathf.Abs(Along(c, p) - Along(c, s)) <= DvergrWoodBridge.BeamLength * 0.5f);
        // The bents are the pier's: at most 5 m apart, piles 5.75 m apart across.
        for (int i = 1; i < bents.Length; i++) Assert.InRange(bents[i] - bents[i - 1], 0.1f, DvergrWoodBridge.BentSpacing + 1e-3f);
        Assert.Equal(5.75f, 2f * DvergrWoodBridge.PileSideOffset, 3);
    }

    [Fact]
    public void TheLayoutIsFixedPerCrossingAndSeedAndRuinOnlyTakesAway()
    {
        var (c, low) = Low();
        string Sig(List<StructurePiece> ps) => string.Join("|", ps.Select(p => FormattableString.Invariant($"{p.Prefab}@{p.Position.x:F2},{p.Position.y:F2},{p.Position.z:F2}/{p.YawDegrees:F1}/{p.HealthFraction:F3}")));
        Assert.Equal(Sig(DvergrWoodBridge.Solve(c, low, 1)), Sig(DvergrWoodBridge.Solve(c, low, 1)));
        // Another seed ruins it otherwise, over the same complete bridge.
        var seeds = Enumerable.Range(1, 12).Select(s => Sig(DvergrWoodBridge.Solve(c, low, s))).Distinct().Count();
        Assert.True(seeds > 1, "the ruin never varies with the seed");
        var complete = DvergrWoodBridge.SolveComplete(c, low, 1);
        string P(StructurePiece p) => FormattableString.Invariant($"{p.Prefab}@{p.Position.x:F2},{p.Position.y:F2},{p.Position.z:F2}");
        var completeSet = new HashSet<string>(complete.Select(P));
        for (int s = 1; s <= 12; s++)
        {
            var ruined = DvergrWoodBridge.Solve(c, low, s);
            Assert.True(ruined.Count(p => p.Kind == StructurePieceKind.Deck) <= complete.Count(p => p.Kind == StructurePieceKind.Deck));
            // Every standing piece but the riverbed's debris and stubs is one of the complete bridge's.
            Assert.All(ruined.Where(p => p.Kind != StructurePieceKind.Debris && !(p.Prefab == DvergrWoodBridge.Pile && p.Position.y < BridgeLayout.DeckHeight(c, low))
                && p.Prefab != DvergrWoodBridge.Lantern), p => Assert.Contains(P(p), completeSet));
            Assert.True(ruined.Count(p => p.Prefab == DvergrWoodBridge.Lantern) <= DvergrWoodBridge.MaxLanterns);
        }
        // Somewhere over twelve seeds a plank is missing mid-span, and somewhere a lantern stands.
        Assert.Contains(Enumerable.Range(1, 12), s => DvergrWoodBridge.Solve(c, low, s).Count(p => p.Kind == StructurePieceKind.Deck) < complete.Count(p => p.Kind == StructurePieceKind.Deck));
        Assert.Contains(Enumerable.Range(1, 40), s => DvergrWoodBridge.Solve(c, low, s).Any(p => p.Prefab == DvergrWoodBridge.Lantern));
    }

    [Fact]
    public void LanternsStandOnPlanksOutsideTheRoadsLane()
    {
        var (c, low) = Low();
        for (int s = 1; s <= 40; s++)
        {
            var pieces = DvergrWoodBridge.Solve(c, low, s);
            foreach (StructurePiece l in pieces.Where(p => p.Prefab == DvergrWoodBridge.Lantern))
            {
                Vector2 side = new(-c.Direction.y, c.Direction.x);
                float across = Mathf.Abs(Vector2.Dot(new Vector2(l.Position.x, l.Position.z) - c.FromBank, side));
                Assert.InRange(across, BridgeLayout.DeckHalfWidth, DvergrWoodBridge.BeamLength * 0.5f);
                Assert.Contains(pieces, p => p.Kind == StructurePieceKind.Deck && Mathf.Abs(Along(c, p) - Along(c, l)) < 0.01f);
                Assert.Equal(BridgeLayout.DeckHeight(c, low), l.Position.y, 3);
            }
        }
    }

    [Fact]
    public void TheNavigationGapStaysOpen()
    {
        var (c, low) = Low();
        Assert.True(c.FairwayWidth > 0f && !BridgeLayout.IsFreeSpan(c, low));
        for (int s = 1; s <= 12; s++)
            foreach (StructurePiece p in DvergrWoodBridge.Solve(c, low, s).Where(p => p.Kind != StructurePieceKind.Debris))
                Assert.False(BridgeLayout.InNavigationGap(c, Along(c, p)), $"{p.Prefab} at {Along(c, p):F1} m stands in the navigation gap");
        // The complete bridge fills it, as the wooden kit's does: it is a run of bays a player could close.
        Assert.Contains(DvergrWoodBridge.SolveComplete(c, low, 1), p => p.Kind == StructurePieceKind.Deck && BridgeLayout.InNavigationGap(c, Along(c, p)));
    }

    [Fact]
    public void AFreeSpanHasBentsOnlyAtItsBanksAndStringersBankToBank()
    {
        var w = new MistRiver { Bank = 34f };
        _scope.WithWorld(w);
        // 14 m over a 14 m fairway: the boat's lane covers it all, so nothing stands in the water.
        RoadCrossing c = RoadCrossing.Between(new Vector2(-7f, 0f), new Vector2(7f, 0f), 26f, new Vector2(0f, 0f), 14f, CrossingKind.Bridge);
        Assert.True(BridgeLayout.IsFreeSpan(c, w));
        Assert.Equal(BridgeStructures.Kit.DvergrWood, BridgeStructures.KitFor(c, w));
        float length = BridgeLayout.BuiltLength(c.Width);
        var pieces = DvergrWoodBridge.Solve(c, w, 3);
        Assert.All(pieces.Where(p => p.Prefab == DvergrWoodBridge.Pile), p => Assert.True(Along(c, p) < 0.01f || Mathf.Abs(Along(c, p) - length) < 0.01f, $"pile at {Along(c, p):F1}"));
        Assert.DoesNotContain(pieces, p => p.Kind == StructurePieceKind.Debris);
        var stringers = pieces.Where(p => p.Kind == StructurePieceKind.Beam).Select(p => Along(c, p)).Distinct().OrderBy(a => a).ToList();
        Assert.True(stringers.First() - DvergrWoodBridge.BeamLength * 0.5f <= 0.01f && stringers.Last() + DvergrWoodBridge.BeamLength * 0.5f >= length - 0.01f);
        for (int i = 1; i < stringers.Count; i++) Assert.True(stringers[i] - stringers[i - 1] <= DvergrWoodBridge.BeamLength + 1e-3f);
    }

    [Fact]
    public void EveryHeadingIsOneTheHammerHas()
    {
        var (c, low) = Low();
        Assert.All(DvergrWoodBridge.Solve(c, low, 1).Where(p => p.Kind != StructurePieceKind.Debris),
            p => Assert.True(BridgeLayout.HeadingIsPlaceable(p.YawDegrees), $"{p.Prefab} at yaw {p.YawDegrees}"));
    }
}

/// <summary>NetworkCutoff for the Dvergr wood kit: a generated low Mistlands bridge that would put any piece in a zone
/// explored before the network is built nowhere, as every bridge (BridgeLifecycleTests).</summary>
public sealed class DvergrWoodBridgeCutoffTests : IDisposable
{
    private readonly ValheimWorldScope _world = new ValheimWorldScope()
        .WithWorld(new BridgeTests.WideRiverWorld { Biome = Heightmap.Biome.Mistlands }).WithZdos().WithZoneSystem().WithScene().AsServer();

    public DvergrWoodBridgeCutoffTests()
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
    public void ADvergrWoodBridgeReachingAnExploredZoneIsBuiltNowhere()
    {
        RoadCrossing bridge = Assert.Single(RoadNetworkGenerator.GetRoadCrossings(), c => c.Kind == CrossingKind.Bridge);
        Assert.Equal(BridgeStructures.Kit.DvergrWood, BridgeStructures.KitFor(bridge, WorldGenerator.instance!));
        StructureAppendQueue.Reset();
        List<Vector2s> zones = StructurePlans.PlannedZones;
        Assert.True(zones.Count >= 2, "the fixture's bridge must span more than one zone");
        Assert.Contains(zones.SelectMany(z => StructurePlans.PlanFor(z)!), p => p.Prefab == DvergrWoodBridge.Plank);
        foreach (var piece in zones.SelectMany(z => StructurePlans.PlanFor(z)!))
            if (ZNetScene.instance!.GetPrefab(piece.Prefab) == null) ZNetScene.instance.AddPrefab(piece.Prefab);
        NetworkCutoff.Forget();
        NetworkCutoff.SnapshotWorld(new[] { zones[zones.Count - 1] });
        NetworkCutoff.UseNewNetwork();
        foreach (Vector2s zone in zones)
        {
            Assert.Equal(0, StructurePlacement.OnZoneSpawned(zone, ZoneSystem.SpawnMode.Full));
            Assert.False(StructurePlans.IsSpawned(zone));
        }
        Assert.DoesNotContain(ZDOMan.instance!.Zdos, z => z.GetInt(StructurePlans.MarkerHash) == 1);

        // On a world with nothing explored the same bridge gets every planned piece, Dvergr planks among them.
        NetworkCutoff.Forget();
        NetworkCutoff.SnapshotWorld(Array.Empty<Vector2s>());
        NetworkCutoff.UseNewNetwork();
        Assert.Equal(StructurePlans.TotalPlannedPieces, zones.Sum(z => StructurePlacement.OnZoneSpawned(z, ZoneSystem.SpawnMode.Full)));
        Assert.Contains(ZDOMan.instance.Zdos, z => z.GetPrefab() == DvergrWoodBridge.Plank.GetStableHashCode());
    }
}
