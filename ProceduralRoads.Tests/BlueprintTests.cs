using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Bridges as blueprints (Tys, 3 Sep 2026: use the format jneb802's own
/// tooling uses — valheimCreative's PlanBuild-format .blueprint text, the
/// same files Expand World spawns). Three things are proven here, before
/// anything reaches the game:
///  - the FORMAT: what their writer emits parses back, what we write they
///    can read (13 fields, quoted description, quaternion rotation), and the
///    Unity Euler/quaternion arithmetic the frame depends on;
///  - COMPOSITION: START / SPAN / END kits chain by snap points across a
///    recorded crossing, each kit with its own span length (2 m planks, 4 m
///    arch bays), double-wide where the causeway is 4 m, and every piece
///    ends up grounded under the support model;
///  - EXPORT: a solved bridge written as one blueprint per site comes back
///    piece for piece.
/// The kits live in ProceduralRoads.Tests/blueprints/ as embedded resources.
/// </summary>
public class BlueprintTests
{
    private static RoadBlueprint Load(string file)
    {
        using var stream = typeof(BlueprintTests).Assembly.GetManifestResourceStream("blueprints/" + file)
            ?? throw new FileNotFoundException("embedded blueprint missing: " + file);
        using var reader = new StreamReader(stream);
        return RoadBlueprint.Parse(reader.ReadToEnd());
    }

    private static (RoadBlueprint start, RoadBlueprint span, RoadBlueprint end) Kit(string prefix) =>
        (Load(prefix + "-start.blueprint"), Load(prefix + "-span.blueprint"), Load(prefix + "-end.blueprint"));

    private static (RoadCrossing crossing, WorldGenerator world) Crossing()
    {
        var world = new SupportModelTests.WideSteppedWorld();
        var path = new List<Vector2> { new(-64f, 0f), new(-56f, 0f), new(-48f, 0f), new(48f, 0f), new(56f, 0f), new(64f, 0f) };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));
        return (crossing, world);
    }

    private static float Along(RoadCrossing c, BridgePiece p) => c.Along(new Vector2(p.Position.x, p.Position.z));
    private static float Across(RoadCrossing c, BridgePiece p) => Vector2.Dot(new Vector2(p.Position.x, p.Position.z) - c.FromBank, BlueprintComposer.Right(c));

    // ================= format =================

    /// <summary>Exactly what valheimCreative's Write() produces for a two-piece
    /// build: quoted, escaped description; 13 fields; G9 floats; no snap points.</summary>
    private const string ValheimCreativeText =
        "#Name:the_midnight_tavern\n" +
        "#Creator:Tys\n" +
        "#Description:\"a \\\"tavern\\\" by the road\"\n" +
        "#Category:Blueprints\n" +
        "#Pieces\n" +
        "wood_floor;Misc;-1;0;3.5;0;0.707106769;0;0.707106769;;1;1;1\n" +
        "wood_pole2;Building;0.5;-1.2;2;0;0;0;1;kind=Piling health=0.25;1;1;1\n";

    [Fact]
    public void ParsesValheimCreativeWriterOutput()
    {
        var bp = RoadBlueprint.Parse(ValheimCreativeText);
        Assert.Equal("the_midnight_tavern", bp.Name);
        Assert.Equal("Tys", bp.Creator);
        Assert.Equal("a \"tavern\" by the road", bp.Description);
        Assert.Equal("Blueprints", bp.Category);
        Assert.Empty(bp.SnapPoints);
        Assert.Equal(2, bp.Pieces.Count);

        var floor = bp.Pieces[0];
        Assert.Equal("wood_floor", floor.Prefab);
        Assert.Equal("Misc", floor.Category);
        Assert.Equal(new Vector3(-1f, 0f, 3.5f), floor.LocalPosition);
        Assert.Equal(90f, floor.Euler.yaw, 3);
        Assert.Equal("", floor.Data);
        Assert.Equal(new Vector3(1f, 1f, 1f), floor.Scale);

        var pole = bp.Pieces[1];
        Assert.Equal("Piling", pole.DataValue("kind"));
        Assert.Equal(0.25f, BlueprintComposer.HealthOf(pole, 1f));
        Assert.Null(pole.DataValue("missing"));

        // Without snap points the anchor is the bottom centre of the bounds
        // (valheimCreative and Expand World both hold a file there).
        Assert.Equal(new Vector3(-0.25f, -1.2f, 2.75f), bp.Anchor);
        Assert.Equal(1.5f, bp.Length, 3);
    }

    [Fact]
    public void ReadsOtherDialectsLeniently()
    {
        // Expand World: zdoData and chance as fields 14-15.
        var ew = RoadBlueprint.Parse("#Pieces\nMarketplaceStall;0;6;0;4;0;0;0;1;;1;1;1;infinite_health;0.35\n");
        var stall = Assert.Single(ew.Pieces);
        Assert.Equal(new Vector3(6f, 0f, 4f), stall.LocalPosition);
        Assert.Equal("", stall.Data);

        // PlanBuild: scale optional, "" for empty info, decimal comma tolerated.
        var pb = RoadBlueprint.Parse("#Pieces\nstone_arch;Building;1,5;-1;0;0;0;0;1;\"\"\n");
        var arch = Assert.Single(pb.Pieces);
        Assert.Equal(1.5f, arch.LocalPosition.x);
        Assert.Equal("", arch.Data);
        Assert.Equal(new Vector3(1f, 1f, 1f), arch.Scale);

        // Unknown sections (PlanBuild #Terrain, Infinity Hammer #center:) are skipped, not read as pieces.
        var mixed = RoadBlueprint.Parse("#center:wood_floor\n#Terrain\n1;2;3;4;5;6;7;8;9\n#Pieces\nwood_floor;Building;0;0;0;0;0;0;1;;1;1;1\n");
        Assert.Single(mixed.Pieces);
    }

    [Fact]
    public void WriteIsTheirLayoutAndParsesBack()
    {
        var bp = new RoadBlueprint { Name = "unit", Creator = "ProceduralRoads", Description = "say \"hi\"", Category = "ProceduralRoads" };
        bp.SnapPoints.Add(Vector3.zero);
        bp.SnapPoints.Add(new Vector3(0f, 0.25f, 4f));
        var piece = new BlueprintPiece { Prefab = "stone_arch", LocalPosition = new Vector3(-1f, -1.5f, 1f), Data = "kind=Arch health=0.5", Scale = new Vector3(1f, 1f, 1f) };
        piece.SetEuler(0f, 90f, 0f);
        bp.Pieces.Add(piece);

        string text = bp.Write();
        string[] lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal("#Name:unit", lines[0]);
        Assert.Equal("#Creator:ProceduralRoads", lines[1]);
        Assert.Equal("#Description:\"say \\\"hi\\\"\"", lines[2]);
        Assert.Equal("#Category:ProceduralRoads", lines[3]);
        Assert.Equal("#SnapPoints", lines[4]);
        Assert.Equal("#Pieces", lines[7]);
        Assert.Equal(13, lines[8].Split(';').Length);
        Assert.StartsWith("stone_arch;Building;-1;-1.5;1;0;0.707106769;0;0.707106769;kind=Arch health=0.5;1;1;1", lines[8]);

        var back = RoadBlueprint.Parse(text);
        Assert.Equal(bp.Description, back.Description);
        Assert.Equal(bp.SnapPoints, back.SnapPoints);
        Assert.Equal(4f, back.Length);
        var p = Assert.Single(back.Pieces);
        Assert.Equal(piece.LocalPosition, p.LocalPosition);
        Assert.Equal(90f, p.Euler.yaw, 3);
        Assert.Equal(piece.Data, p.Data);

        // No snap points: no section at all, as their writer.
        bp.SnapPoints.Clear();
        Assert.DoesNotContain("#SnapPoints", bp.Write());
    }

    [Fact]
    public void ReadsValheimCreativeSidecarOffsets()
    {
        const string json = "{\n  \"blueprints\": {\n    \"the_midnight_tavern.blueprint\": {\n      \"loadYOffset\": -1.0,\n      \"biome\": \"Plains\"\n    },\n    \"old_pad\": 0.5\n  }\n}";
        Assert.Equal(-1f, RoadBlueprint.ReadLoadYOffset(json, "the_midnight_tavern.blueprint"));
        Assert.Equal(-1f, RoadBlueprint.ReadLoadYOffset(json, "The_Midnight_Tavern"));
        Assert.Equal(0.5f, RoadBlueprint.ReadLoadYOffset(json, "old_pad.blueprint"));
        Assert.Equal(0f, RoadBlueprint.ReadLoadYOffset(json, "unknown.blueprint"));
        Assert.Equal(0f, RoadBlueprint.ReadLoadYOffset("", "the_midnight_tavern.blueprint"));
    }

    // ================= math =================

    [Theory]
    [InlineData(0f, 90f, 0f, 0f, 0.70710678f, 0f, 0.70710678f)]
    [InlineData(90f, 0f, 0f, 0.70710678f, 0f, 0f, 0.70710678f)]
    [InlineData(0f, 0f, 90f, 0f, 0f, 0.70710678f, 0.70710678f)]
    [InlineData(90f, 90f, 0f, 0.5f, 0.5f, -0.5f, 0.5f)] // Unity: Quaternion.Euler(90, 90, 0)
    public void EulerToQuaternionMatchesUnity(float pitch, float yaw, float roll, float x, float y, float z, float w)
    {
        var q = BlueprintMath.FromEuler(pitch, yaw, roll);
        Assert.Equal(x, q.x, 5);
        Assert.Equal(y, q.y, 5);
        Assert.Equal(z, q.z, 5);
        Assert.Equal(w, q.w, 5);
    }

    [Fact]
    public void EulerRoundTripsThroughTheQuaternion()
    {
        foreach (float pitch in new[] { -60f, -10f, 0f, 35f, 80f })
        foreach (float yaw in new[] { -170f, -90f, 0f, 45f, 135f, 179f })
        foreach (float roll in new[] { -30f, 0f, 25f })
        {
            var q = BlueprintMath.FromEuler(pitch, yaw, roll);
            var e = BlueprintMath.ToEuler(q.x, q.y, q.z, q.w);
            var q2 = BlueprintMath.FromEuler(e.pitch, e.yaw, e.roll);
            float dot = q.x * q2.x + q.y * q2.y + q.z * q2.z + q.w * q2.w;
            Assert.True(Mathf.Abs(dot) > 0.99999f, $"({pitch},{yaw},{roll}) came back as ({e.pitch:F3},{e.yaw:F3},{e.roll:F3})");
        }
    }

    [Fact]
    public void RotateFollowsUnityHandedness()
    {
        var yaw90 = BlueprintMath.FromEuler(0f, 90f, 0f);
        var fwd = BlueprintMath.Rotate(yaw90, new Vector3(0f, 0f, 1f));
        var right = BlueprintMath.Rotate(yaw90, new Vector3(1f, 0f, 0f));
        Assert.Equal(new Vector3(1f, 0f, 0f), Round(fwd));
        Assert.Equal(new Vector3(0f, 0f, -1f), Round(right));
    }

    private static Vector3 Round(Vector3 v) => new(Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f);

    // ================= composition =================

    /// <summary>The composer's frame IS Unity's: a piece placed with the
    /// crossing's yaw has its local +z along the crossing and its local +x
    /// where Right() says, for any heading.</summary>
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(0.6f, -0.8f)]
    public void FrameAgreesWithUnityYaw(float dx, float dz)
    {
        var c = new RoadCrossing { FromBank = new Vector2(10f, 20f), Direction = new Vector2(dx, dz), Width = 30f, WaterLevel = 30f };
        var q = BlueprintMath.FromEuler(0f, BridgeLayout.YawDegrees(c.Direction), 0f);
        var fwd = BlueprintMath.Rotate(q, new Vector3(0f, 0f, 1f));
        var right = BlueprintMath.Rotate(q, new Vector3(1f, 0f, 0f));
        Assert.Equal(c.Direction.x, fwd.x, 4);
        Assert.Equal(c.Direction.y, fwd.z, 4);
        Assert.Equal(BlueprintComposer.Right(c).x, right.x, 4);
        Assert.Equal(BlueprintComposer.Right(c).y, right.z, 4);

        // And Place puts a piece at local (+1, 0, +2) exactly there.
        var bp = new RoadBlueprint();
        bp.SnapPoints.Add(Vector3.zero);
        bp.Pieces.Add(new BlueprintPiece { Prefab = "wood_floor", LocalPosition = new Vector3(1f, 0f, 2f) });
        var placed = new List<BridgePiece>();
        BlueprintComposer.Place(placed, bp, c, BridgeStyle.MeadowsWood, 5f, _ => 33f);
        var p = Assert.Single(placed);
        Vector2 expected = c.FromBank + c.Direction * 7f + BlueprintComposer.Right(c) * 1f;
        Assert.Equal(expected.x, p.Position.x, 4);
        Assert.Equal(expected.y, p.Position.z, 4);
        Assert.Equal(33f, p.Position.y, 4);
        Assert.Equal(BridgeLayout.YawDegrees(c.Direction), p.YawDegrees, 4);
    }

    [Theory]
    [InlineData(7f, 4f, 2)]
    [InlineData(5f, 4f, 1)]
    [InlineData(0f, 4f, 0)]
    [InlineData(1f, 4f, 1)]
    [InlineData(83.5f, 2f, 42)]
    public void SpanCountIsTheNearestWholeNumber(float budget, float span, int expected) =>
        Assert.Equal(expected, BlueprintComposer.SpanCount(budget, span));

    public static IEnumerable<object[]> Kits()
    {
        yield return new object[] { "wood-bridge", "MeadowsWood", 2f, 2f };
        yield return new object[] { "stone-arch", "MountainStone", 4f, 4f };
        yield return new object[] { "hybrid", "HybridStoneWood", 4f, 4f };
    }

    private static BridgeStyle StyleNamed(string name) => name switch
    {
        "MeadowsWood" => BridgeStyle.MeadowsWood,
        "MountainStone" => BridgeStyle.MountainStone,
        "HybridStoneWood" => BridgeStyle.HybridStoneWood,
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [MemberData(nameof(Kits))]
    public void EveryKitTilesBankToBankAndStandsUp(string kit, string styleName, float spanLength, float deckWidth)
    {
        var (c, world) = Crossing();
        var style = StyleNamed(styleName);
        var (start, span, end) = Kit(kit);
        Assert.Equal(spanLength, span.Length);
        Assert.Equal(2f, start.Length);
        Assert.Equal(2f, end.Length);

        var plan = BlueprintComposer.GroundPosts(BlueprintComposer.Tile(c, world, style, start, span, end), world, style);

        // Deck runs continuously from bank to bank; joints never open wider
        // than the pitch rounding allows (half a span over the whole bridge).
        var plateRows = plan.Where(p => p.Kind == BridgePieceKind.Deck).Select(p => Along(c, p)).Distinct().OrderBy(a => a).ToList();
        Assert.True(plateRows.First() <= 1.5f, $"first plate at {plateRows.First():F1} m");
        Assert.True(plateRows.Last() >= c.Width - 1.5f, $"last plate at {plateRows.Last():F1} m of {c.Width:F1}");
        float plateStep = deckWidth >= 4f ? 2f : span.Length; // plates are 2 m long in every kit
        for (int i = 1; i < plateRows.Count; i++)
            Assert.True(plateRows[i] - plateRows[i - 1] <= plateStep * 1.5f + 0.01f, $"{kit}: {plateRows[i] - plateRows[i - 1]:F2} m between plate rows at {plateRows[i - 1]:F1}");

        // The spans are pitched at the kit's own length (rounded to fit), not a global 2 m.
        int spans = BlueprintComposer.SpanCount(c.Width - 4f, spanLength);
        var pierRows = plan.Where(p => p.Kind == BridgePieceKind.Piling).Select(p => Mathf.Round(Along(c, p) * 10f) / 10f).Distinct().Count();
        Assert.Equal(spans + 1, pierRows); // START's pier plus one per span

        // Double-wide kits carry two of everything abreast, ±1 m off the centreline: the causeway's 4 m.
        if (deckWidth >= 4f)
        {
            var offsets = plan.Where(p => p.Kind == BridgePieceKind.Deck).Select(p => Mathf.Round(Across(c, p) * 10f) / 10f).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new List<float> { -1f, 1f }, offsets);
            var pierOffsets = plan.Where(p => p.Kind == BridgePieceKind.Piling).Select(p => Mathf.Round(Across(c, p) * 10f) / 10f).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new List<float> { -1f, 1f }, pierOffsets);
        }

        SupportModelTests.AssertGrounded(plan, style, world, kit + " kit");
    }

    [Fact]
    public void StoneArchBaysCarryFourQuarterArchesEach()
    {
        var (c, world) = Crossing();
        var (start, span, end) = Kit("stone-arch");
        var plan = BlueprintComposer.Tile(c, world, BridgeStyle.MountainStone, start, span, end);
        int spans = BlueprintComposer.SpanCount(c.Width - 4f, 4f);
        var arches = plan.Where(p => p.Kind == BridgePieceKind.Arch).ToList();
        Assert.Equal(4 * spans, arches.Count);
        // Each bay: two arches face +z from the near pier (yaw +90 relative), two face back (-90).
        float crossingYaw = BridgeLayout.YawDegrees(c.Direction);
        Assert.Equal(2 * spans, arches.Count(a => Mathf.Abs(Mathf.DeltaAngle(a.YawDegrees - crossingYaw, 90f)) < 0.01f));
        Assert.Equal(2 * spans, arches.Count(a => Mathf.Abs(Mathf.DeltaAngle(a.YawDegrees - crossingYaw, -90f)) < 0.01f));
        // Arch tops meet the slab undersides (slab origin 0.5 under the top, arch 1 m tall, centre 1.5 under).
        var slabs = plan.Where(p => p.Kind == BridgePieceKind.Deck).ToList();
        foreach (var a in arches)
            Assert.Contains(slabs, s => Mathf.Abs(Along(c, s) - Along(c, a)) < 0.01f && Mathf.Abs(Across(c, s) - Across(c, a)) < 0.01f && Mathf.Abs(s.Position.y - a.Position.y - 1f) < 0.01f);
    }

    [Fact]
    public void HybridKitIsStoneBelowWoodAbove()
    {
        var (c, world) = Crossing();
        var (start, span, end) = Kit("hybrid");
        var plan = BlueprintComposer.GroundPosts(BlueprintComposer.Tile(c, world, BridgeStyle.HybridStoneWood, start, span, end), world, BridgeStyle.HybridStoneWood);
        Assert.All(plan.Where(p => p.Kind == BridgePieceKind.Piling), p => Assert.Equal("stone_wall_2x1", p.Prefab));
        Assert.All(plan.Where(p => p.Kind is BridgePieceKind.Deck or BridgePieceKind.Abutment), p => Assert.Equal("wood_floor", p.Prefab));
        Assert.All(plan.Where(p => p.Kind == BridgePieceKind.Beam), p => Assert.Equal("wood_beam", p.Prefab));
        // Stone piers stack in 1 m courses down to the bed.
        var oneColumn = plan.Where(p => p.Kind == BridgePieceKind.Piling && Mathf.Abs(Along(c, p) - 2f) < 0.01f && Across(c, p) > 0f).OrderByDescending(p => p.Position.y).ToList();
        Assert.True(oneColumn.Count >= 2, "a pier is a column of courses");
        for (int i = 1; i < oneColumn.Count; i++)
            Assert.Equal(1f, oneColumn[i - 1].Position.y - oneColumn[i].Position.y, 3);
    }

    // ================= weather =================

    /// <summary>Post columns by footprint (the fairway's are the deepest, so
    /// segment counts would overstate what the fairway keep-clear removes).</summary>
    private static int Columns(List<BridgePiece> plan) =>
        plan.Where(p => p.Kind == BridgePieceKind.Piling).Select(p => (Mathf.RoundToInt(p.Position.x * 10f), Mathf.RoundToInt(p.Position.z * 10f))).Distinct().Count();

    [Theory]
    [MemberData(nameof(Kits))]
    public void WeatherRemovesHighPiecesFirstAndKeepsEveryKitGrounded(string kit, string styleName, float spanLength, float deckWidth)
    {
        _ = spanLength; _ = deckWidth;
        var (c, world) = Crossing();
        var style = StyleNamed(styleName);
        var (start, span, end) = Kit(kit);
        var full = BlueprintComposer.GroundPosts(BlueprintComposer.Tile(c, world, style, start, span, end), world, style);
        int decksBefore = full.Count(p => p.Kind == BridgePieceKind.Deck);
        int columnsBefore = Columns(full);
        for (int seed = 1; seed <= 10; seed++)
        {
            var ruin = BlueprintComposer.Weather(full, c, style, world, seed);
            int decks = ruin.Count(p => p.Kind == BridgePieceKind.Deck);
            int columns = Columns(ruin);
            Assert.True(decks < decksBefore, "some deck must fall");
            Assert.True((float)decks / decksBefore < (float)columns / columnsBefore, $"decks fall before post columns (seed {seed}: {decks}/{decksBefore} decks, {columns}/{columnsBefore} columns)");
            var again = BlueprintComposer.Weather(full, c, style, world, seed);
            Assert.Equal(ruin.Select(p => (p.Prefab, p.Position, p.HealthFraction)), again.Select(p => (p.Prefab, p.Position, p.HealthFraction)));
            SupportModelTests.AssertGrounded(ruin, style, world, $"{kit} weathered seed {seed}");
        }
    }

    [Fact]
    public void AMistlandsBridgeCrossingGetsNoPlan()
    {
        var (c, world) = Crossing();
        Assert.NotEmpty(BridgeLayout.Solve(c, world, 7, BridgeLayout.StyleFor(c.Biome)));
        c.Biome = Heightmap.Biome.Mistlands;
        Assert.Empty(BridgeLayout.Solve(c, world, 7, BridgeLayout.StyleFor(c.Biome)));
    }

    [Fact]
    public void WeatherClearsTheFairwayLikeTheSolver()
    {
        var (c, world) = Crossing();
        Assert.True(c.FairwayWidth > 0f, "the wide river is sailable");
        var (start, span, end) = Kit("stone-arch");
        var full = BlueprintComposer.GroundPosts(BlueprintComposer.Tile(c, world, BridgeStyle.MountainStone, start, span, end), world, BridgeStyle.MountainStone);
        float mid = c.Along(c.FairwayCenter);
        float half = BridgeLayout.FairwayGap(c) * 0.5f + BridgeLayout.FairwayClearance;
        Assert.Contains(full, p => Mathf.Abs(Along(c, p) - mid) <= half); // the kit did build there
        for (int seed = 1; seed <= 5; seed++)
        {
            var ruin = BlueprintComposer.Weather(full, c, BridgeStyle.MountainStone, world, seed);
            Assert.DoesNotContain(ruin, p => Mathf.Abs(Along(c, p) - mid) <= half);
        }
    }

    // ================= planner =================

    [Theory]
    [InlineData("MeadowsWood")]
    [InlineData("MountainStone")]
    public void PlannerDefaultIsTheSolverPieceForPiece(string styleName)
    {
        var (c, world) = Crossing();
        c.Biome = styleName == "MountainStone" ? Heightmap.Biome.Mountain : Heightmap.Biome.Meadows;
        var solved = BridgeLayout.Solve(c, world, 11, BridgeLayout.StyleFor(c.Biome));
        var planned = BridgePlanner.Plan(c, world, 11, BridgeKit.Solver);
        Assert.Equal(solved.Select(p => (p.Prefab, p.Position, p.HealthFraction)), planned.Select(p => (p.Prefab, p.Position, p.HealthFraction)));
        Assert.Equal(BridgeKit.Solver, BridgePlanner.ConfiguredKit); // the shipped default
    }

    [Theory]
    [InlineData(BridgeKit.Wood, "wood_pole2")]
    [InlineData(BridgeKit.StoneArch, "stone_arch")]
    [InlineData(BridgeKit.Hybrid, "wood_beam")]
    public void PlannerComposesGroundsAndWeathersTheKit(BridgeKit kit, string signaturePrefab)
    {
        var (c, world) = Crossing();
        var plan = BridgePlanner.Plan(c, world, 11, kit);
        Assert.Contains(plan, p => p.Prefab == signaturePrefab);
        Assert.Contains(plan, p => p.Kind == BridgePieceKind.Abutment);
        Assert.All(plan, p => Assert.InRange(p.HealthFraction, RoadConstants.RuinHealthMin - 0.001f, RoadConstants.RuinHealthMax + 0.001f));
        SupportModelTests.AssertGrounded(plan, BridgeKits.StyleOf(kit), world, kit + " planned");
        var again = BridgePlanner.Plan(c, world, 11, kit);
        Assert.Equal(plan.Select(p => (p.Prefab, p.Position, p.HealthFraction)), again.Select(p => (p.Prefab, p.Position, p.HealthFraction)));
        Assert.NotEqual(plan.Select(p => p.HealthFraction), BridgePlanner.Plan(c, world, 12, kit).Select(p => p.HealthFraction));
    }

    [Fact]
    public void PlannerByBiomeFollowsTheSolverStyleMap()
    {
        var (c, world) = Crossing();
        c.Biome = Heightmap.Biome.Meadows;
        Assert.Contains(BridgePlanner.Plan(c, world, 11, BridgeKit.ByBiome), p => p.Prefab == "wood_pole2");
        c.Biome = Heightmap.Biome.Mountain;
        Assert.Contains(BridgePlanner.Plan(c, world, 11, BridgeKit.ByBiome), p => p.Prefab == "stone_arch");
        c.Biome = Heightmap.Biome.Mistlands;
        Assert.Empty(BridgePlanner.Plan(c, world, 11, BridgeKit.ByBiome));
        // Fords go to the solver whatever the kit.
        c.Biome = Heightmap.Biome.Meadows;
        c.Kind = CrossingKind.Ford; c.Style = FordStyle.Wade;
        Assert.Empty(BridgePlanner.Plan(c, world, 11, BridgeKit.StoneArch));
    }

    [Fact]
    public void AKitUnitCanBeReplacedFromTheOverrideDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "proads-kits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var span = Load("wood-bridge-span.blueprint");
            span.Description = "a player's plank";
            span.Pieces.Add(new BlueprintPiece { Prefab = "wood_pole2", LocalPosition = new Vector3(0f, -1.2f, 1f), Data = "kind=Piling" });
            File.WriteAllText(Path.Combine(dir, "wood-bridge-span.blueprint"), span.Write());
            BridgeKits.OverrideDirectory = dir;
            BridgeKits.ClearCache();
            var (start, loadedSpan, end) = BridgeKits.Load(BridgeKit.Wood);
            Assert.Equal("a player's plank", loadedSpan.Description);
            Assert.Equal(5, loadedSpan.Pieces.Count);
            Assert.Equal(2f, loadedSpan.Length);
            Assert.Equal("ProceduralRoads wood bridge START", start.Name); // the others still come from the mod
            Assert.Equal("ProceduralRoads wood bridge END", end.Name);
        }
        finally
        {
            BridgeKits.OverrideDirectory = null;
            BridgeKits.ClearCache();
            Directory.Delete(dir, true);
        }
    }

    // ================= export =================

    [Theory]
    [InlineData("MeadowsWood")]
    [InlineData("MountainStone")]
    public void SolvedBridgeExportsAndComesBackPieceForPiece(string styleName)
    {
        var (c, world) = Crossing();
        var style = StyleNamed(styleName);
        var plan = BridgeLayout.Solve(c, world, 7, style);
        Assert.NotEmpty(plan);

        var bp = BlueprintComposer.Export(plan, c, world, "site", "test");
        Assert.Equal(2, bp.SnapPoints.Count);
        Assert.Equal(c.Width, bp.SnapPoints[1].z, 3);
        var back = RoadBlueprint.Parse(bp.Write());
        Assert.Equal(plan.Count, back.Pieces.Count);

        (float deckFromH, _) = BridgeLayout.DeckEndHeights(c, world);
        var placed = new List<BridgePiece>();
        BlueprintComposer.Place(placed, back, c, style, 0f, _ => deckFromH);
        for (int i = 0; i < plan.Count; i++)
        {
            BridgePiece a = plan[i], b = placed[i];
            Assert.Equal(a.Prefab, b.Prefab);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.HealthFraction, b.HealthFraction, 5);
            Assert.True(Vector3.Distance(a.Position, b.Position) < 0.002f, $"{a.Kind} {i}: {a.Position.x:F3},{a.Position.y:F3},{a.Position.z:F3} vs {b.Position.x:F3},{b.Position.y:F3},{b.Position.z:F3}");
            var qa = BlueprintMath.FromEuler(a.PitchDegrees, a.YawDegrees, a.RollDegrees);
            var qb = BlueprintMath.FromEuler(b.PitchDegrees, b.YawDegrees, b.RollDegrees);
            float dot = qa.x * qb.x + qa.y * qb.y + qa.z * qb.z + qa.w * qb.w;
            Assert.True(Mathf.Abs(dot) > 0.99999f, $"{a.Kind} {i}: rotation ({a.PitchDegrees:F1},{a.YawDegrees:F1},{a.RollDegrees:F1}) came back as ({b.PitchDegrees:F1},{b.YawDegrees:F1},{b.RollDegrees:F1})");
        }
    }

    [Fact]
    public void ExportAllWritesOneLoadableFilePerSite()
    {
        var (c, world) = Crossing();
        string dir = Path.Combine(Path.GetTempPath(), "proads-blueprints-" + Guid.NewGuid().ToString("N"));
        try
        {
            int written = BlueprintComposer.ExportAll(dir, new[] { c, c }, world, 7); // duplicates collapse to one site
            Assert.Equal(1, written);
            string file = Assert.Single(Directory.GetFiles(dir));
            string name = Path.GetFileName(file);
            Assert.Matches("^proceduralroads_bridge_-?[0-9]+_-?[0-9]+\\.blueprint$", name); // valheimCreative's name rules
            var bp = RoadBlueprint.Parse(File.ReadAllText(file));
            Assert.Equal(Path.GetFileNameWithoutExtension(file), bp.Name);
            Assert.Contains("Bridge at", bp.Description);
            Assert.NotEmpty(bp.Pieces);
            Assert.All(bp.Pieces, p => Assert.NotNull(p.DataValue("kind")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ================= exhibits =================

    /// <summary>Side views and blueprint files of the three kits, composed
    /// and weathered on the wide river, for a human to look at
    /// (validation-results/kit-*.svg, validation-results/blueprints/kit-*.blueprint).</summary>
    [Theory]
    [MemberData(nameof(Kits))]
    public void KitExhibits(string kit, string styleName, float spanLength, float deckWidth)
    {
        _ = spanLength; _ = deckWidth;
        var (c, world) = Crossing();
        var style = StyleNamed(styleName);
        var (start, span, end) = Kit(kit);
        var full = BlueprintComposer.GroundPosts(BlueprintComposer.Tile(c, world, style, start, span, end), world, style);
        var ruin = BlueprintComposer.Weather(full, c, style, world, 3);
        SideViewExhibit.Write($"kit-{kit}-side.svg", c, ruin, world, style, $"{kit} kit composed from blueprints, weathered (seed 3)");
        string dir = Path.Combine(SideViewExhibit.OutputDir, "blueprints");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"kit-{kit}.blueprint"),
            BlueprintComposer.Export(ruin, c, world, $"kit-{kit}", $"{kit} kit on the wide river, weathered (seed 3)").Write());
    }
}
