using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// Stairs on steep faces: flights held out from a face on cantilevers
/// (a hand-built reference: pillars clipped into the wall, arches out from them, the stair on the
/// arches, a 2x1 block beside it), priced by the face's grade; turns priced by how steep the
/// ground is where they are made (aim a turn at a tree well); cantilevered legs may pass over
/// lower ones with headroom; and the road search offers stairs over faces steeper than 1:1 where
/// the stairs can be held out. Each rule's test fails with that rule switched off.
/// </summary>
public class CantileverTests
{
    private readonly ITestOutputHelper _out;
    public CantileverTests(ITestOutputHelper output) => _out = output;

    private sealed class RulesOff : IDisposable
    {
        public RulesOff(SteepRules off) => StairLayout.RulesOff = off;
        public void Dispose() => StairLayout.RulesOff = SteepRules.None;
    }

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static void Near(Vector3 expected, Vector3 actual, string what)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.01f, $"{what}: expected {expected}, was {actual}");
    }

    /// <summary>The pieces of one stair held out from the face, at a hand-built reference stair (read from the snap
    /// points): stone_stair 58027 rising toward +x over x -2301.4..-2299.4,
    /// z 1553.5..1555.5, foot at 278.611; the face on its -z side. Its pillars, arches and block
    /// stand exactly where the cantilever puts them, and each arch's tall end (its local +x end:
    /// snaps top and bottom there, cli_piece_geometry) is against the pillars.</summary>
    [Fact]
    public void AStairIsHeldOutWhereTheHandBuiltOneIs()
    {
        var plan = new StairPlan();
        // heading 90 (+x); its right is -z, the face's side.
        StairLayout.AddCantilever(plan, StairKit.Stone.Cantilever!, new Vector2(-2301.396f, 1554.542f), 90f, 278.611f, side: 1, across: 2f);

        StairPiece[] pillars = plan.Pieces.Where(p => p.Prefab == "stone_pillar").ToArray();
        StairPiece[] arches = plan.Pieces.Where(p => p.Prefab == "stone_arch").ToArray();
        StairPiece[] blocks = plan.Pieces.Where(p => p.Prefab == "stone_wall_2x1").ToArray();
        Assert.Equal(2, pillars.Length);
        Assert.Equal(2, arches.Length);
        Assert.Single(blocks);
        Assert.All(plan.Pieces, p => Assert.True(p.Cantilevered && p.Kind == StairPieceKind.Support));
        // Pillars 57992/57996: x -2301.4..-2300.4 and -2300.4..-2299.4, y 276.6..278.6, z 1552.5..1553.5.
        Near(V(-2300.896f, 277.611f, 1553.042f), pillars[0].Position, "first pillar");
        Near(V(-2299.896f, 277.611f, 1553.042f), pillars[1].Position, "second pillar");
        // Arches 58015/58005: the same x, y 277.6..278.6, z 1553.5..1555.5.
        Near(V(-2300.896f, 278.111f, 1554.542f), arches[0].Position, "first arch");
        Near(V(-2299.896f, 278.111f, 1554.542f), arches[1].Position, "second arch");
        // Block 58021: x -2301.4..-2299.4, y 278.6..279.6, z 1552.5..1553.5, its 2 m (local x) along the flight.
        Near(V(-2300.396f, 279.111f, 1553.042f), blocks[0].Position, "block");
        Vector2 blockLength = StairLayout.Right(blocks[0].YawDegrees);
        Assert.True(Mathf.Abs(blockLength.x) > 0.99f, $"block's local x points {blockLength}, not along the flight");
        foreach (StairPiece arch in arches)
        {
            // Local x is Right(yaw); the tall end (+1) sits at z 1553.542, against the pillars.
            Vector2 tall = new Vector2(arch.Position.x, arch.Position.z) + StairLayout.Right(arch.YawDegrees) * StairKit.Stone.Cantilever!.ArchTallEnd;
            Assert.True(Mathf.Abs(tall.y - 1553.542f) < 0.01f, $"arch's tall end at z {tall.y}");
        }
    }

    /// <summary>Black marble's arch is tall at its local -x end (cli_piece_geometry): turned
    /// the other way round from stone's, its tall end still meets the pillars, and its pillars are
    /// two 1 m columns a metre.</summary>
    [Fact]
    public void BlackMarblesArchIsTurnedSoItsTallEndMeetsThePillars()
    {
        var plan = new StairPlan();
        CantileverKit ck = StairKit.BlackMarble.Cantilever!;
        StairLayout.AddCantilever(plan, ck, new Vector2(0f, 0f), 0f, 10f, side: -1, across: 2f);
        StairPiece[] arches = plan.Pieces.Where(p => p.Prefab == "blackmarble_arch").ToArray();
        Assert.Equal(2, arches.Length);
        Assert.Equal(4, plan.Pieces.Count(p => p.Prefab == "blackmarble_column_1"));
        foreach (StairPiece arch in arches)
        {
            // Heading 0 (+z), face on the left: -x. Pillars at x -1.5.
            Vector2 tall = new Vector2(arch.Position.x, arch.Position.z) + StairLayout.Right(arch.YawDegrees) * ck.ArchTallEnd;
            Assert.True(Mathf.Abs(tall.x + 1f) < 0.01f, $"arch's tall end at x {tall.x}");
            Assert.Equal(9f, arch.Position.y, 3); // 2 m tall, its top under the stair's foot
        }
        Assert.All(plan.Pieces.Where(p => p.Prefab == "blackmarble_column_1"), p => Assert.Equal(-1.5f, p.Position.x, 3));
    }

    /// <summary>
    /// A planar face of 6:1 across z (the reference face is about 6:1), rising along x at a flight's
    /// 1:2 so a flight along it keeps its place on the face, and a shelf at its top (x past 20)
    /// for the staircase to finish on. Without cantilevers every piece stood on a column down the
    /// face (up to 12 m of pillars); held out, no column hangs into the drop.
    /// </summary>
    private static float Face(float x, float z) => x < 20f ? 0.5f * x - 6f * z : Mathf.Max(4f, 10f - 6f * z);

    private StairPlan LayAlongTheFace() =>
        StairLayout.Build(new Vector2(0f, 2f), new Vector2(21f, 2f), Face, footHeight: -6f, arriving: 90f);

    [Fact]
    public void AFlightAlongASheerFaceIsHeldOutNotStoodOnColumns()
    {
        StairPlan plan = LayAlongTheFace();
        _out.WriteLine($"reached {plan.Reached} {plan.Reason}; stairs {plan.Stairs}, cantilevered {plan.Cantilevered}, steepest face {plan.SteepestFace:F2}; " +
                       $"columns {plan.Pieces.Count(p => p.Kind == StairPieceKind.Support && !p.Cantilevered)}");
        Assert.True(plan.Reached, plan.Reason);
        Assert.True(plan.Cantilevered >= 8, $"{plan.Cantilevered} rows held out");
        Assert.True(plan.SteepestFace >= 5f, $"steepest face {plan.SteepestFace}");
        Assert.Contains(plan.Pieces, p => p.Prefab == "stone_arch");
        // Nothing hangs into the drop: no column under any piece.
        Assert.DoesNotContain(plan.Pieces, p => p.Kind == StairPieceKind.Support && !p.Cantilevered);

        using (new RulesOff(SteepRules.Cantilevers))
        {
            StairPlan columns = LayAlongTheFace();
            Assert.Equal(0, columns.Cantilevered);
            Assert.DoesNotContain(columns.Pieces, p => p.Prefab == "stone_arch");
            Assert.Contains(columns.Pieces, p => p.Kind == StairPieceKind.Support && p.Position.y < -8f);
        }
    }

    /// <summary>Wood has no arch: its flights stand on poles as ever.</summary>
    [Fact]
    public void WoodHasNoCantilever()
    {
        Assert.Null(StairKit.Wood.Cantilever);
        StairPlan plan = StairLayout.Build(new Vector2(0f, 2f), new Vector2(21f, 2f), Face, kit: StairKit.Wood, footHeight: -6f, arriving: 90f);
        Assert.Equal(0, plan.Cantilevered);
    }

    /// <summary>What holds a cantilever is its pillars sunk into the face: read in game, stone
    /// arches 1.6 m clear of the face held at 720 off the pillars alone (minimum 100). So a piece whose
    /// arches stand clear of the face is held out as long as its pillars are sunk; one whose pillars
    /// would not reach the ground (stacked no deeper than CantileverKit.MaxDepth) is not.</summary>
    [Fact]
    public void ACantileverIsHeldByItsPillarsSunkIntoTheFace()
    {
        Vector2 start = new(0f, 2f), dir = StairLayout.Forward(90f), right = StairLayout.Right(90f);
        // On the 6:1 face of the test above, the stair's wall edge at the ground: held out.
        Assert.Equal(1, StairLayout.CantileverSide(start, dir, right, 1f, -6f, StairKit.Stone, Face, out float face));
        Assert.Equal(6f, face, 2);
        // Two and a half metres higher, the arches' tall ends 2 m over the face, the pillars still sunk: held out,
        // stone as black marble.
        Assert.Equal(1, StairLayout.CantileverSide(start, dir, right, 1f, -3.5f, StairKit.Stone, Face, out _));
        Assert.Equal(1, StairLayout.CantileverSide(start, dir, right, 1f, -3.5f, StairKit.BlackMarble, Face, out _));
        // Up to 8 m over the face its strip columns still reach down into it (four stone pillars); higher, not held out.
        Assert.Equal(1, StairLayout.CantileverSide(start, dir, right, 1f, 4.5f, StairKit.Stone, Face, out _));
        Assert.Equal(0, StairLayout.CantileverSide(start, dir, right, 1f, 6f, StairKit.Stone, Face, out _));
        // A gentle slope is not held out from at all.
        Assert.Equal(0, StairLayout.CantileverSide(start, dir, right, 1f, 1f, StairKit.Stone, (x, z) => -0.8f * z, out _));
    }

    /// <summary>The price of a cantilevered piece rises with the face's grade (cheaper at lesser grades) and stops rising at its maximum.</summary>
    [Fact]
    public void ACantileversPriceRisesWithTheFaceUpToAMaximum()
    {
        float last = 0f;
        for (float g = StairLayout.CantileverFromGrade; g <= StairLayout.CantileverFullGrade; g += 0.5f)
        {
            float price = StairLayout.CantileverPrice(g);
            Assert.True(price > last, $"price at {g} is {price}, not above {last}");
            last = price;
        }
        Assert.Equal(StairLayout.CantileverMaxPrice, StairLayout.CantileverPrice(StairLayout.CantileverFullGrade + 1f), 3);
        Assert.Equal(StairLayout.CantileverMaxPrice, StairLayout.CantileverPrice(20f), 3);
    }

    /// <summary>A cantilevered piece may pass over a lower one of its own staircase where its lowest
    /// part keeps headroom over that piece; a piece on columns never may, and with the overhang rule off
    /// neither may a cantilever.</summary>
    [Fact]
    public void ACantileveredLegMayPassOverALowerOneWithHeadroom()
    {
        // A lower flight's piece at (0, 0) heading +x, its top at 0; then a piece it carries on to.
        var laid = new List<(Vector2, float, float, int)> { (new Vector2(0f, 0f), 90f, 0f, 0), (new Vector2(20f, 0f), 90f, 1f, 0) };
        // Straight above it, held out from a face on its right (-z): arch bottom 1 under its underside.
        Assert.False(StairLayout.CrossesLaid(laid, new Vector2(0f, 0f), 90f, underside: 3.5f, cantilever: 1, StairKit.Stone));
        // Its arch would stand in the headroom: 2.6 - 1 is 1.6 over the lower top.
        Assert.True(StairLayout.CrossesLaid(laid, new Vector2(0f, 0f), 90f, underside: 2.6f, cantilever: 1, StairKit.Stone));
        // Its pillars (2 m deep, in the strip a metre and a half to its right) over the lower piece:
        // shifted so the strip lies over the lower piece, 3.5 leaves 1.5 of headroom under them.
        Assert.True(StairLayout.CrossesLaid(laid, new Vector2(0f, 1.5f), 90f, underside: 3.5f, cantilever: 1, StairKit.Stone));
        Assert.False(StairLayout.CrossesLaid(laid, new Vector2(0f, 1.5f), 90f, underside: 4.5f, cantilever: 1, StairKit.Stone));
        // On columns: never over another piece.
        Assert.True(StairLayout.CrossesLaid(laid, new Vector2(0f, 0f), 90f, underside: 10f, cantilever: 0, StairKit.Stone));
        using (new RulesOff(SteepRules.Overhang))
            Assert.True(StairLayout.CrossesLaid(laid, new Vector2(0f, 0f), 90f, underside: 3.5f, cantilever: 1, StairKit.Stone));
    }

    /// <summary>The tree-well rule's price: a switchback on a bench costs what it always did, on steep
    /// ground more, the more so the steeper, up to a cap.</summary>
    [Fact]
    public void SwitchbacksCostMoreOnSteepGround()
    {
        Assert.Equal(0f, StairLayout.SwitchbackSteepPrice(0.3f));
        Assert.Equal(0f, StairLayout.SwitchbackSteepPrice(StairLayout.BenchGrade));
        Assert.True(StairLayout.SwitchbackSteepPrice(2f) > StairLayout.SwitchbackSteepPrice(1.5f));
        Assert.Equal(StairLayout.SwitchbackSteepPrice(StairLayout.SteepCap), StairLayout.SwitchbackSteepPrice(10f), 3);
        using (new RulesOff(SteepRules.TurnSteepness))
            Assert.Equal(0f, StairLayout.SwitchbackSteepPrice(2f));
    }

    /// <summary>
    /// A slope of 0.6 straight up the climb, steeper than a flight, the staircase kept to a strip 9 m
    /// wide (as a road's corridor keeps it), so it must switch back once. Past the strip's east side,
    /// 1.1 m beyond it, the ground falls away sheer, under no piece. Every other way the two sides are
    /// mirror images, and without the rule the search turns on the east side, beside the drop; priced
    /// by the ground round it, the turn moves to the west side, away from the drop ("Switch backs on a
    /// sheer cliff would be hard and I think we should aim for less steep terrain for those").
    /// </summary>
    private static float SlopeWithADropEast(float x, float z) => 0.6f * z - (x > 5.6f ? 20f * (x - 5.6f) : 0f);

    private List<StairPiece> SwitchbackFloors()
    {
        // Its switchback within five stairs of the foot: laid as before StairLayout.FirstFlightStairs, the rule this is about apart.
        SteepRules before = StairLayout.RulesOff;
        StairLayout.RulesOff = before | SteepRules.FirstFlight;
        StairPlan plan;
        try { plan = StairLayout.Build(new Vector2(0f, 0f), new Vector2(0f, 20f), SlopeWithADropEast, allowed: p => Mathf.Abs(p.x) <= 4.5f); }
        finally { StairLayout.RulesOff = before; }
        Assert.True(plan.Reached, plan.Reason);
        Assert.Equal(1, plan.Switchbacks);
        // A switchback's two floors lie side by side at one height.
        var floors = plan.Pieces.Where(p => p.Kind == StairPieceKind.Landing).ToList();
        var pads = floors.Where(f => floors.Exists(o => o != f && Mathf.Abs(o.Position.y - f.Position.y) < 0.01f
            && Mathf.Abs(Vector2.Distance(new Vector2(o.Position.x, o.Position.z), new Vector2(f.Position.x, f.Position.z)) - 2f) < 0.05f)).ToList();
        _out.WriteLine("pads " + string.Join(" ", pads.Select(p => $"({p.Position.x:F1},{p.Position.y:F1},{p.Position.z:F1})")));
        Assert.Equal(2, pads.Count);
        return pads;
    }

    [Fact]
    public void ASwitchbackTurnsAwayFromADrop()
    {
        Assert.All(SwitchbackFloors(), p => Assert.True(p.Position.x < 0f, $"pad at x {p.Position.x}, the drop's side"));
        using (new RulesOff(SteepRules.TurnSteepness))
            Assert.All(SwitchbackFloors(), p => Assert.True(p.Position.x > 0f, $"without the rule the pad was at x {p.Position.x}"));
    }

    /// <summary>A scarp of 2.5:1 rising 24 m across the whole map: no road climbs it, and no step of a
    /// staircase on columns (1:1 at most, knight's moves included). Held out on cantilevers, stone
    /// stairs climb it on the diagonal (1.8:1 between cells), priced up with the grade; wood's (Meadows) still cannot.</summary>
    private sealed class SheerScarp : WorldGenerator
    {
        public Heightmap.Biome Biome = Heightmap.Biome.Mountain;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f) return 0f;
            return 40f + Mathf.Clamp(wx * 2.5f, 0f, 24f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Biome;
        public override int GetSeed() => 77;
    }

    [Fact]
    public void TheRoadSearchOffersStairsUpFacesSteeperThan1To1WhereTheyCanBeHeldOut()
    {
        var world = new SheerScarp();
        using var worldScope = new ValheimWorldScope().WithWorld(world);
        RoadNetworkGenerator.Reset();
        try
        {
            RoadPathfinder Finder(bool cantilevers) => new(world) { Stairs = true, CantileverStairs = cantilevers, TerrainVarianceThreshold = 1000f, Meander = 0f };
            Assert.Null(Finder(cantilevers: false).FindPath(new Vector2(-80f, 0f), new Vector2(100f, 0f)));
            RoadPathfinder finder = Finder(cantilevers: true);
            Assert.NotNull(finder.FindPath(new Vector2(-80f, 0f), new Vector2(100f, 0f)));
            Assert.Contains(finder.LastStairSteps, s =>
                Mathf.Abs(world.GetHeight(s.to.x, s.to.y) - world.GetHeight(s.from.x, s.from.y)) / Vector2.Distance(s.from, s.to) > RoadPathfinder.StairMaxGrade);
            // In the Meadows (wood) the search is as it was before cantilevers.
            var wood = new SheerScarp { Biome = Heightmap.Biome.Meadows };
            worldScope.WithWorld(wood);
            RoadNetworkGenerator.Reset();
            RoadPathfinder meadows = new(wood) { Stairs = true, CantileverStairs = true, TerrainVarianceThreshold = 1000f, Meander = 0f };
            RoadPathfinder before = new(wood) { Stairs = true, CantileverStairs = false, TerrainVarianceThreshold = 1000f, Meander = 0f };
            List<Vector2>? a = meadows.FindPath(new Vector2(-80f, 0f), new Vector2(100f, 0f)), b = before.FindPath(new Vector2(-80f, 0f), new Vector2(100f, 0f));
            Assert.Equal(b, a);
            Assert.Equal(before.LastStairSteps, meadows.LastStairSteps);
        }
        finally
        {
            RoadNetworkGenerator.Reset();
        }
    }

    /// <summary>The price per metre climbed rises from 1:1 to its maximum at the steepest step offered.</summary>
    [Fact]
    public void AStairStepsPriceRisesWithItsGrade()
    {
        Assert.Equal(1f, RoadPathfinder.SteepStairFactor(0.5f));
        Assert.Equal(1f, RoadPathfinder.SteepStairFactor(RoadPathfinder.StairMaxGrade));
        Assert.True(RoadPathfinder.SteepStairFactor(2f) > RoadPathfinder.SteepStairFactor(1.5f));
        Assert.Equal(RoadPathfinder.SteepStairPriceMax, RoadPathfinder.SteepStairFactor(RoadPathfinder.StairCantileverMaxGrade), 3);
    }

    /// <summary>The wedge a turn opens is read where its filler lies: a turn left off a flight climbing +z on
    /// ground rising to the west (left) at 2:1 has its wedge on the outside (east, downhill), clear; turned
    /// right, its wedge lies uphill (west) and the ground covers it.</summary>
    [Fact]
    public void ATurnsWedgeIsReadWhereItsFillerLies()
    {
        float West(float x, float z) => -2f * x;
        // The piece before ends at (0, 2), its top at 0.5 (the ground at its middle line is 0).
        Vector2 edge = new(0f, 2f);
        // Turned right 45 degrees: pivot on the right (east) corner, the wedge on the west.
        Vector2 pivotRight = edge + StairLayout.Right(0f) * 1f, startRight = pivotRight - StairLayout.Right(45f) * 1f;
        // Turned left 45: pivot on the west corner, the wedge on the east.
        Vector2 pivotLeft = edge - StairLayout.Right(0f) * 1f, startLeft = pivotLeft + StairLayout.Right(-45f) * 1f;
        float right = StairLayout.WedgeBuried(edge, 0.5f, pivotRight, startRight, West);
        float left = StairLayout.WedgeBuried(edge, 0.5f, pivotLeft, startLeft, West);
        Assert.True(right > 0.3f, $"wedge uphill buried {right:F2}");
        Assert.True(left < 0f, $"wedge downhill buried {left:F2}");
    }

    /// <summary>A hand-built set of cantilevered turns (at 1934,164,705): five stairs climbing on
    /// 202.5, 157.5, 135, 157.5 and 202.5 (turns of -45, -22.5, +22.5 and +45, the face on the right), each held
    /// out, every turn's wedge on an arch and a strip column. Its pieces where they follow the one pattern
    /// (prefab, x, y, z, yaw); where they do not (CantileverTurnDifferences), the pattern is followed.</summary>
    private static readonly (string prefab, float x, float y, float z, float yaw)[] HandBuiltTurns =
    {
        // the five stairs
        ("stone_stair", 1933.632f, 161.496f, 711.875f, 22.5f), ("stone_stair", 1933.632f, 162.496f, 709.262f, 337.5f),
        ("stone_stair", 1934.938f, 163.496f, 707.306f, 315f), ("stone_stair", 1936.245f, 164.496f, 705.351f, 337.5f),
        ("stone_stair", 1936.245f, 165.496f, 702.738f, 22.5f),
        // each stair's two arches
        ("stone_arch", 1933.823f, 160.996f, 712.337f, 202.5f), ("stone_arch", 1933.440f, 160.996f, 711.413f, 202.5f),
        ("stone_arch", 1933.440f, 161.996f, 709.723f, 157.5f), ("stone_arch", 1933.823f, 161.996f, 708.800f, 157.5f),
        ("stone_arch", 1934.585f, 162.996f, 707.660f, 135f), ("stone_arch", 1935.292f, 162.996f, 706.953f, 135f),
        ("stone_arch", 1936.053f, 163.996f, 705.813f, 157.5f), ("stone_arch", 1936.436f, 163.996f, 704.889f, 157.5f),
        ("stone_arch", 1936.436f, 164.996f, 703.200f, 202.5f), ("stone_arch", 1936.053f, 164.996f, 702.276f, 202.5f),
        // each turn: the arch under its wedge, the 2x1 across the old edge, a 1x1 behind the new edge at 45
        ("stone_arch", 1933.058f, 160.996f, 710.489f, 202.5f), ("stone_wall_2x1", 1933.058f, 161.996f, 710.489f, 22.5f), ("stone_wall_1x1", 1932.596f, 161.996f, 710.456f, 67.5f),
        ("stone_arch", 1934.206f, 161.996f, 707.876f, 157.5f), ("stone_wall_2x1", 1934.206f, 162.996f, 707.876f, 337.5f),
        ("stone_arch", 1935.999f, 162.996f, 706.246f, 135f), ("stone_wall_2x1", 1935.999f, 163.996f, 706.246f, 135f),
        ("stone_arch", 1936.819f, 163.996f, 703.965f, 157.5f), ("stone_wall_2x1", 1936.819f, 164.996f, 703.965f, 157.5f), ("stone_wall_1x1", 1937.281f, 164.996f, 703.932f, 112.5f),
        // strip columns' top pillars and blocks beside, where they are laid (stairs 1, 4, 5), the turn columns (turns 1, 3),
        // and turn 1's block carried on past the first stair's top, turned away from the face
        ("stone_pillar", 1932.437f, 160.496f, 712.911f, 202.5f), ("stone_pillar", 1932.054f, 160.496f, 711.987f, 202.5f), ("stone_wall_2x1", 1932.246f, 161.996f, 712.449f, 292.5f),
        ("stone_pillar", 1934.667f, 163.496f, 705.239f, 337.5f), ("stone_pillar", 1935.050f, 163.496f, 704.315f, 337.5f), ("stone_wall_2x1", 1934.859f, 164.996f, 704.777f, 67.5f),
        ("stone_pillar", 1935.050f, 164.496f, 703.774f, 202.5f), ("stone_pillar", 1934.667f, 164.496f, 702.850f, 202.5f), ("stone_wall_2x1", 1934.859f, 165.996f, 703.312f, 292.5f),
        ("stone_pillar", 1931.672f, 160.496f, 711.063f, 202.5f), ("stone_pillar", 1934.938f, 162.496f, 705.185f, 135f),
        ("stone_wall_2x1", 1931.480f, 161.996f, 710.601f, 292.5f),
    };

    /// <summary>A piece's yaw as the game sees it: a pillar or 1x1 looks the same every quarter turn, a 2x1 every half.</summary>
    private static float Symmetric(string prefab, float yaw) =>
        StairLayout.Wrap(yaw) % (prefab == "stone_pillar" || prefab == "stone_wall_1x1" ? 90f : prefab == "stone_wall_2x1" ? 180f : 360f);

    /// <summary>Laid on ground far under it, so every block beside is laid whether or not the face would hold it
    /// (the hug rule, which leaves out blocks standing out of the face, is off: his flight stands against the face).</summary>
    private static StairPlan LayHandBuiltTurns()
    {
        SteepRules before = StairLayout.RulesOff;
        StairLayout.RulesOff = before | SteepRules.Hug;
        try
        {
            return StairLayout.LayFlight(new Vector2(1934.015f, 712.799f), 161.496f,
                new[] { 202.5f, 157.5f, 135f, 157.5f, 202.5f }, StairKit.Stone, (x, z) => 150f, side: 1);
        }
        finally { StairLayout.RulesOff = before; }
    }

    [Fact]
    public void CantileveredTurnsAreBuiltAsTheHandBuiltOnes()
    {
        StairPlan plan = LayHandBuiltTurns();
        foreach (var (prefab, x, y, z, yaw) in HandBuiltTurns)
        {
            var at = new Vector3(x, y, z);
            Assert.True(plan.Pieces.Exists(p => p.Prefab == prefab && Vector3.Distance(p.Position, at) < 0.02f
                && Math.Abs(Symmetric(prefab, p.YawDegrees) - Symmetric(prefab, yaw)) < 0.1f),
                $"no {prefab} at {at} yaw {yaw}; nearest: " + string.Join(" ", plan.Pieces.Where(p => p.Prefab == prefab)
                    .OrderBy(p => Vector3.Distance(p.Position, at)).Take(2).Select(p => $"{p.Position} {p.YawDegrees}")));
        }
        // Nothing hangs into the drop: every support is the cantilever's.
        Assert.DoesNotContain(plan.Pieces, p => p.Kind == StairPieceKind.Support && !p.Cantilevered);
        Assert.Equal(4, plan.Turns);

        StairLayout.RulesOff = SteepRules.TurnCantilever;
        try
        {
            StairPlan before = LayHandBuiltTurns();
            // Without the rule a turn's wedge is not held: no arch under it, its fillers on columns down the drop.
            Assert.DoesNotContain(before.Pieces, p => p.Prefab == "stone_arch" && Vector3.Distance(p.Position, new Vector3(1933.058f, 160.996f, 710.489f)) < 0.02f);
            Assert.Contains(before.Pieces, p => p.Kind == StairPieceKind.Support && !p.Cantilevered);
        }
        finally { StairLayout.RulesOff = SteepRules.None; }
    }

    /// <summary>Strip columns go on down until sunk into the face or the ground (a hand-built stack: where the
    /// lowest stair's top pillars hung 3-4 m over the ground, they were carried on down to it).</summary>
    [Fact]
    public void AStripColumnGoesDownUntilItIsSunk()
    {
        StairPlan high = StairLayout.LayFlight(new Vector2(0f, 0f), 10f, new[] { 0f }, StairKit.Stone, (x, z) => x > 1f ? 9.5f : 0f, side: 1);
        StairPlan low = StairLayout.LayFlight(new Vector2(0f, 0f), 10f, new[] { 0f }, StairKit.Stone, (x, z) => x > 1f ? 5.5f : 0f, side: 1);
        // Heading 0, face on the right (+x): the strip at x = 1.5. A face at 9.5 takes the one 2 m pillar (8..10);
        // at 5.5 the column goes on to 4..6, sunk 1.5 m.
        Assert.Equal(2, high.Pieces.Count(p => p.Prefab == "stone_pillar"));
        Assert.Equal(6, low.Pieces.Count(p => p.Prefab == "stone_pillar"));
        Assert.Equal(5f, low.Pieces.Where(p => p.Prefab == "stone_pillar").Min(p => p.Position.y), 3);
    }

    /// <summary>A flight set off a metre further from the 6:1 face than its first one: without the hug rule it
    /// keeps its line and its strip stands out of the face; priced by the strip that shows, it steps in to the face
    /// (the stairs hug the wall closer, so less of the side block shows through). Every tread stays clear.</summary>
    [Fact]
    public void ACantileveredFlightHugsTheFace()
    {
        float Shown(StairPlan plan) => plan.Pieces.Where(p => p.Cantilevered && p.Prefab == "stone_wall_2x1" && p.Kind == StairPieceKind.Support)
            .Select(p => Math.Max(0f, p.Position.y + 0.5f - Face(p.Position.x, p.Position.z))).DefaultIfEmpty(0f).Average();
        StairPlan Lay() => StairLayout.Build(new Vector2(0f, 2.5f), new Vector2(21f, 2.5f), Face, footHeight: -6f, arriving: 90f);
        StairPlan hug = Lay();
        Assert.True(hug.Reached, hug.Reason);
        Assert.True(hug.Cantilevered >= 6, $"{hug.Cantilevered} rows held out");
        StairPlan apart;
        StairLayout.RulesOff = SteepRules.Hug;
        try { apart = Lay(); }
        finally { StairLayout.RulesOff = SteepRules.None; }
        _out.WriteLine($"strip shown: hugging {Shown(hug):F2} m, without {Shown(apart):F2} m");
        Assert.True(Shown(hug) < Shown(apart) - 0.2f, $"hugging {Shown(hug):F2} m, without {Shown(apart):F2} m");
        Assert.True(hug.MaxBuried <= StairLayout.MaxBuriedDepth);
    }

    /// <summary>The block beside a held-out stair is laid where the face holds most of it, and left out where it would
    /// stand clear of the rock between the stair and the face (the hug applies to the stairs, not the side wall).</summary>
    [Fact]
    public void ABlockBesideIsLaidOnlyWhereTheFaceHoldsIt()
    {
        // Heading 0, face on the right (+x): the strip's middle at x = 1.5. Base 10: the block spans 10..11.
        StairPlan sunk = StairLayout.LayFlight(new Vector2(0f, 0f), 10f, new[] { 0f }, StairKit.Stone, (x, z) => x > 1f ? 10.8f : 0f, side: 1);
        StairPlan clear = StairLayout.LayFlight(new Vector2(0f, 0f), 10f, new[] { 0f }, StairKit.Stone, (x, z) => x > 1f ? 9f : 0f, side: 1);
        Assert.Single(sunk.Pieces, p => p.Prefab == "stone_wall_2x1");
        Assert.DoesNotContain(clear.Pieces, p => p.Prefab == "stone_wall_2x1");
        StairLayout.RulesOff = SteepRules.Hug;
        try { Assert.Single(StairLayout.LayFlight(new Vector2(0f, 0f), 10f, new[] { 0f }, StairKit.Stone, (x, z) => x > 1f ? 9f : 0f, side: 1).Pieces, p => p.Prefab == "stone_wall_2x1"); }
        finally { StairLayout.RulesOff = SteepRules.None; }
    }

    /// <summary>At -5853,5918: a flight running beside its road a few metres off, both climbing, mostly floors
    /// (better to avoid running parallel, even if it costs POIs). Three walked pieces
    /// in a row within 5 m of a road and 1.5 m of its height, away from the staircase's own ends, refuse the layout.</summary>
    [Fact]
    public void AStaircaseRunningBesideARoadAtItsHeightIsRefused()
    {
        float Ground(float x, float z) => 40f + 0.1f * x;
        var path = new List<Vector2>();
        for (int x = -40; x <= 40; x += 8) path.Add(new Vector2(x, 0f));
        path.Add(new Vector2(40f, 16f));
        StairPlan Beside(float lift)
        {
            var plan = new StairPlan { Reached = true };
            foreach (float x in new[] { -24f, -22f, -20f, -18f })
                plan.Pieces.Add(new StairPiece { Kind = StairPieceKind.Landing, Position = new Vector3(x, Ground(x, 0f) - 0.5f + lift, 3f), YawDegrees = 90f });
            return plan;
        }
        // Its stretch is the road's last leg, (40,0) to (40,16): the run at x -24..-18 lies beside the road before it.
        Assert.True(RoadStairRuns.BesideARoad(Beside(0f), path, 10, 11, Ground));
        Assert.False(RoadStairRuns.BesideARoad(Beside(3f), path, 10, 11, Ground), "3 m over the road it is not beside it");
        RoadStairRuns.NoParallelStairs = false;
        try { Assert.False(RoadStairRuns.BesideARoad(Beside(0f), path, 10, 11, Ground)); }
        finally { RoadStairRuns.NoParallelStairs = true; }
    }

    /// <summary>No switchback in a staircase's first five stairs from its foot (Hildir_cave 1664,768): up a narrow gully, the first
    /// switchback comes after five stairs; before, three.</summary>
    [Fact]
    public void TheFirstSwitchbackComesFiveStairsUpFromTheFoot()
    {
        int StairsBeforeSwitchback(StairPlan plan)
        {
            int stairs = 0;
            StairPiece? previous = null;
            foreach (StairPiece p in plan.Pieces.Where(p => p.Kind == StairPieceKind.Stair || p.Kind == StairPieceKind.Landing))
            {
                if (previous != null && previous.Kind == StairPieceKind.Landing && p.Kind == StairPieceKind.Landing && previous.YawDegrees == p.YawDegrees
                    && Vector2.Distance(new Vector2(previous.Position.x, previous.Position.z), new Vector2(p.Position.x, p.Position.z)) > 1.5f) return stairs;
                if (p.Kind == StairPieceKind.Stair) stairs++;
                previous = p;
            }
            return int.MaxValue;
        }
        StairPlan Lay() => StairLayout.Build(new Vector2(0f, 0f), new Vector2(0f, 20f), SlopeWithADropEast, allowed: p => Mathf.Abs(p.x) <= 4.5f);
        StairPlan plan = Lay();
        Assert.True(plan.Reached, plan.Reason);
        Assert.True(StairsBeforeSwitchback(plan) >= StairLayout.FirstFlightStairs, $"switched back after {StairsBeforeSwitchback(plan)} stairs");
        StairLayout.RulesOff = SteepRules.FirstFlight;
        try { Assert.True(StairsBeforeSwitchback(Lay()) < StairLayout.FirstFlightStairs, "without the rule it waited anyway"); }
        finally { StairLayout.RulesOff = SteepRules.None; }
    }

}
