using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valheim.Testing.Doubles;
using Xunit;
using static ProceduralRoads.Tests.DvergrViaductTests;

namespace ProceduralRoads.Tests;

/// <summary>
/// Ruin of the Dvergr viaducts like the vanilla viaduct's.
/// </summary>
public sealed class DvergrViaductRuinTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();

    public DvergrViaductRuinTests()
    {
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
    }

    public void Dispose()
    {
        RoadNetworkGenerator.Reset();
        RoadSpatialGrid.Clear();
        _scope.Dispose();
    }

    // ---------------------------------------------------------------- ruin

    /// <summary>The vanilla Mistlands_Viaduct1 (24 modules) is missing 6 % of its iron floors, 30 % of its deck
    /// course and 46 % of its arches, with one module wholly open. Ours, over 40 seeds of a 96 m valley viaduct: each within a
    /// vanilla-like range, the iron showing through the course, and now and then a whole module open, never two running.</summary>
    [Fact]
    public void RuinIsInAVanillaLikeRangeWithTheIronShowingAndOnlyJumpableGaps()
    {
        var world = new MistValley { Depth = 12f, FloorHalf = 36f, Face = 1f };
        _scope.WithWorld(world);
        RoadCrossing c = DvergrViaduct.ValleyCrossing(new Vector2(-48f, 0f), new Vector2(48f, 0f), world, out string why)!;
        Assert.True(c != null, why);
        float deck = DvergrViaduct.DeckHeight(c!, world);
        int floors = 0, allFloors = 0, course = 0, allCourse = 0, arches = 0, allArches = 0, holes = 0, ironShowing = 0;
        foreach (int seed in Enumerable.Range(1, 40))
        {
            List<StructurePiece> built = DvergrViaduct.Solve(c!, world, seed), complete = DvergrViaduct.SolveComplete(c!, world, seed);
            bool IsCourse(StructurePiece p) => p.Prefab == DvergrViaduct.Block && p.Kind == StructurePieceKind.ViaductDeck;
            floors += built.Count(p => p.Prefab == DvergrViaduct.Floor); allFloors += complete.Count(p => p.Prefab == DvergrViaduct.Floor);
            course += built.Count(IsCourse); allCourse += complete.Count(IsCourse);
            arches += built.Count(p => p.Prefab == DvergrViaduct.Arch); allArches += complete.Count(p => p.Prefab == DvergrViaduct.Arch);
            // Modules by their centre along the deck.
            var modules = complete.Where(p => p.Prefab == DvergrViaduct.Floor).Select(p => Mathf.RoundToInt(Along(c!, p))).Distinct().OrderBy(a => a).ToList();
            var floored = new HashSet<int>(built.Where(p => p.Prefab == DvergrViaduct.Floor).Select(p => Mathf.RoundToInt(Along(c!, p))));
            Vector2 side = new(-c!.Direction.y, c.Direction.x);
            var coursed = built.Where(IsCourse)
                .Select(p => (Mathf.RoundToInt(Along(c, p)), Mathf.RoundToInt(Vector2.Dot(new Vector2(p.Position.x, p.Position.z) - c.FromBank, side)))).ToHashSet();
            for (int i = 0; i < modules.Count; i++)
            {
                if (!floored.Contains(modules[i]))
                {
                    holes++;
                    Assert.True(i > 0 && i + 1 < modules.Count, $"seed {seed}: an end module open at {modules[i]}");
                    Assert.True(floored.Contains(modules[i - 1]) && floored.Contains(modules[i + 1]),
                        $"seed {seed}: two modules running open at {modules[i]}: not a gap a player jumps");
                }
                else if (!coursed.Contains((modules[i], 0))) ironShowing++;
            }
            Assert.All(built, p => Assert.True(p.Kind != StructurePieceKind.ViaductDeck || p.Position.y <= deck + 0.01f));
        }
        float Missing(int have, int all) => 1f - have / (float)all;
        Assert.InRange(Missing(floors, allFloors), 0.03f, 0.12f);
        Assert.InRange(Missing(course, allCourse), 0.2f, 0.4f);
        Assert.InRange(Missing(arches, allArches), 0.3f, 0.6f);
        Assert.InRange(holes, 1, 40 * 3);
        Assert.True(ironShowing > 40, $"the iron floor shows through the course on {ironShowing} module(s) over 40 seeds");
    }
}
