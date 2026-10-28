using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Mistlands road bridges in the style of the game's own Dvergr viaduct
/// (Mistlands_Viaduct1). Pure logic; the game places the plan
/// through the bridges' structure source (<see cref="BridgeStructures"/>), so
/// the old-world cutoff, the spawned-zone record and the append queue are the
/// wooden bridges' own.
///
/// <para><b>The template is the vanilla viaduct.</b> Every offset below was read
/// off the 377 build pieces of one Mistlands_Viaduct1 (location centre
/// (-7804.310, 41.698, 4351.460), yaw 135), rotated onto the bridge's
/// own axes: u along the deck, v across it (to the left of the way the deck
/// runs, as <see cref="BridgeLayout"/>'s side), y up from the deck's walking
/// surface D, which is the top of the deck's black marble course. Its middle
/// pier, the one with every piece, is the pattern.</para>
///
/// <para><b>Piers</b>: 16 m centre to centre as in vanilla.
/// Off a known river, one at the crossing's midpoint and then every 16 m
/// outward while inside the gap; a short crossing still gets the middle one.
/// On a known river (<see cref="BridgeLayout.FairwayGap"/>'s fairway, the
/// wooden kit's one test of "sailable"), the navigation bay first: two vanilla
/// bays wide, piers at the fairway centre +-16 m with <see cref="NavigationGapFor"/>
/// of deck missing between them (the wooden kit's <see cref="BridgeLayout.FairwayGap"/>),
/// then every 16 m outward. Each pier is four shafts in a
/// diamond, (u +-2, v 0) and (u 0, v +-1): a blackmarble_column_3 footing, then
/// blackmarble_2x2x2 stacks with a blackmarble_2x2_enforced band every 8 m,
/// then the corbel band of the top 8 m (out_1, outcorner, 2x2x1 and the
/// spreading 2x2x2s) under the deck, and an iron_wall_2x2 screen through the
/// pier at u +-1 from the footing to the deck, as vanilla has it.</para>
///
/// <para><b>The deck</b> is 2 m modules on the piers' grid: two iron_floor_2x2
/// abreast (4 m), a course of blackmarble_2x2x2 on them (three across, 6 m;
/// over a pier two, with a head either side), and blackmarble_arch along both
/// edges every 2 m. It runs bank to bank: the remainder past the outer piers
/// goes onto the banks. Its height and each end are the one owner's of how a
/// structure meets the road (<see cref="StructureEnds"/>): the deck at the
/// height the road arrives at, an end lower than it walked off on
/// blackmarble_stair steps (the wooden kit's run, <see cref="BridgeLayout.EmitSteps"/>),
/// at most <see cref="MaxEndStairRise"/> of them (<see cref="Ends"/>), their foot on
/// the road, which comes in along the deck's line over its levelled apron
/// (<see cref="MeetTheRoad"/>); an end the road arrives level with has none.</para>
///
/// <para><b>Price</b> (<see cref="Price"/>): dearer the
/// lower its piers, steeply cheaper over 10 m (<see cref="HeightFactor"/>),
/// and its end stairs at ten times an ordinary staircase's metre, so a deep
/// valley crossed high from level banks is the one bought.</para>
///
/// <para><b>Ruin</b> is the wooden kit's rule (<see cref="BridgeLayout.Ruin"/>,
/// <see cref="BridgeLayout.SpanSurvival"/>, <see cref="BridgeLayout.PierSurvival"/>):
/// fixed per crossing and world seed, survival falling toward mid-span, piers
/// outliving the deck. Each part takes a share of wood's decay calibrated on the
/// vanilla viaduct (<see cref="DeckRuinShare"/>, <see cref="CourseRuinShare"/>,
/// <see cref="ArchRuinShare"/>, <see cref="HoleRuinShare"/>): few floors, a third
/// of the course and nearly half the arches gone, now and then a whole module open.
/// A pier's core (footing, shafts, bands, the corbel's 2x2x2s) never goes.
/// The deck keeps a way across a player jumps: no two modules running are both open.</para>
///
/// <para><b>Every piece is grounded or rests on one that is, by
/// construction</b>, and no piece is wholly under the ground (the game breaks
/// one there: <see cref="BuriedPieces"/>). Whether the game's support holds
/// them has to be measured in game: the support formula puts one marble column at
/// about 18.8 m, the vanilla piers stand about 21 m, and
/// <see cref="PierCeiling"/> is a guess between them until it is measured.</para>
/// </summary>
public static class DvergrViaduct
{
    // ---------------------------------------------------------------- pieces
    // Sizes from cli_piece_geometry where it was read:
    // column_3 2 x 8 x 2, out_1 and arch 2 x 2 x 1, origin at the centre of their bounds.
    // The others are read off the vanilla layout (their neighbours' spacing), not measured.
    public const string Footing = "blackmarble_column_3";
    public const string Band = "blackmarble_2x2_enforced";
    public const string Block = "blackmarble_2x2x2";
    public const string Filler = "blackmarble_2x2x1";
    public const string Corbel = "blackmarble_out_1";
    public const string CorbelCorner = "blackmarble_outcorner";
    public const string Floor = "iron_floor_2x2";
    public const string Screen = "iron_wall_2x2";
    public const string Arch = "blackmarble_arch";
    public const string HeadLeft = "blackmarble_head_big01";
    public const string HeadRight = "blackmarble_head_big02";
    public const string StepPrefab = "blackmarble_stair";

    // ------------------------------------------------------------- the rules

    /// <summary>The floor: no viaduct whose tallest pier is under it. A valley then has its road cross lower or take its stairs, and
    /// a river its wooden bridge: <see cref="Refusal"/> is the one owner of the kit, and this carries its "under 6 m
    /// stays wood" (<see cref="MinClearance"/>) up to the floor. Its default.</summary>
    public const float DefaultMinViaductPier = 12f;
    /// <summary>As set: PROCEDURALROADS_VIADUCT_MIN_PIER (metres, 6-20). Settable for tests.</summary>
    public static float MinViaductPier = DebugSwitches.Number("VIADUCT_MIN_PIER", DefaultMinViaductPier, MinClearance, PierCeiling);

    /// <summary>Pier centre to pier centre, as the vanilla viaduct.</summary>
    public const float PierSpacing = 16f;
    /// <summary>One deck module: an iron floor's length.</summary>
    public const float Module = 2f;
    /// <summary>A crossing whose deck stands less than this over the ground or the water keeps
    /// the wooden kit.</summary>
    public const float MinClearance = 6f;
    /// <summary>
    /// The tallest pier, deck surface to the ground under its lowest shaft, a viaduct is built
    /// on; taller, the crossing keeps the wooden kit (on a river) or the road keeps its stairs
    /// (over a dry valley). NOT MEASURED: by WearNTear's rule one marble column holds about
    /// 18.8 m and the vanilla piers stand about 21 m (four shafts, enforced bands and iron
    /// screens); the same arithmetic for wood came out 5 m short of what the game held. It
    /// has to be measured in game (cli_piece_support on the tallest generated pier) before this is trusted.
    /// </summary>
    public const float PierCeiling = 20f;
    /// <summary>The navigation bay over a known river: two vanilla bays (32 m) centred on the fairway, the pier on the
    /// fairway's line left out (a boat found a 16 m bay too tight, so the gap over a river is
    /// wider if possible).</summary>
    public const float NavigationBay = 2f * PierSpacing;
    /// <summary>Least deck kept out from each bay pier's centre: past its shafts, onto its corbels.</summary>
    public const float NavigationStub = 3f;

    /// <summary>
    /// The deck missing over the fairway, centred on it: at least the wooden kit's gap
    /// (<see cref="BridgeLayout.FairwayGap"/>, the one owner of how wide a boat's way is: 16 m or more, growing with the
    /// river), in whole modules, an odd number of them centred on the fairway (4 k + 2 m), and never closer than
    /// <see cref="NavigationStub"/> to a bay pier. At 16 m that is 18 m open and 7 m stubs; the corbels over the
    /// fairway are kept for now.
    /// </summary>
    public static float NavigationGapFor(RoadCrossing crossing)
    {
        float want = Mathf.Max(BridgeLayout.FairwayGapWidth, BridgeLayout.FairwayGap(crossing));
        int k = Mathf.CeilToInt((want - Module) / (2f * Module) - 1e-4f);
        int most = Mathf.FloorToInt((NavigationBay - 2f * NavigationStub - Module) / (2f * Module) + 1e-4f);
        return (2 * Mathf.Clamp(k, 0, most) + 1) * Module;
    }
    /// <summary>A pier stands only this far or more inside each bank (half its corbelled length
    /// along the deck is 6 m; its shafts reach 3 m).</summary>
    public const float PierBankMargin = 3f;
    /// <summary>A pier shorter than this under the deck is the bank itself: its band is left out.</summary>
    public const float MinPier = 2f;

    // Ruin, like vanilla's damage to its viaducts: more deck and railing pieces missing and the iron showing through in
    // some spots are fine, even if the player has to jump over a gap. Each is the share of the wooden kit's losses (BridgeLayout.SpanSurvival: 15 % at a bank, 60 % at
    // mid-span, 37.5 % on average) a part takes, read off the vanilla Mistlands_Viaduct1 (24 modules): 3 of 48 iron floors missing (6 %), 21 of 69 deck course blocks
    // (30 %), 21 of 46 arches (46 %), and one whole module open (floors and course), a 2 m gap a player jumps.
    /// <summary>Iron floors: about 6 % gone on average (vanilla 6 %).</summary>
    public const float DeckRuinShare = 0.15f;
    // Too many missing blocks looked wrong on the 80 m viaduct, so each rate is halfway between a light ruin (course share
    // 0.15 with the middle slot never taken, about 4 % gone; arches 0.5, about 19 %; no whole module open) and a heavy one
    // (course 0.8, 30 %; arches 1.2, 45 %; holes 0.1). Floors were the same in both. The middle slot still goes,
    // so the iron shows through in places.
    /// <summary>The marble course on the floors: about 17 % gone (halfway between 4 % and 30 %).</summary>
    public const float CourseRuinShare = 0.45f;
    /// <summary>Arches: about 32 % gone (halfway between 19 % and 45 %).</summary>
    public const float ArchRuinShare = 0.85f;
    /// <summary>A whole module open, both floors and the course over them: about 2 % of modules (half of the heavy ruin's 0.1). Never two
    /// running, so every gap is one module, 2 m, which a player jumps.</summary>
    public const float HoleRuinShare = 0.05f;

    /// <summary>Heights relative to the deck surface D (the course's top).</summary>
    public const float FloorDrop = 2f, CourseDrop = 1f, ArchRise = 1f;
    /// <summary>The corbel band's lowest level; the shafts stand below it.</summary>
    private const float ShaftTop = -8f;

    // Ruin roles, apart from the wooden kit's own (1-10) so a key never means two things.
    private const int RoleFloor = 21, RoleCourse = 22, RoleArch = 23, RoleOrnament = 24, RoleScreen = 25, RoleHead = 26, RoleHole = 27;
    private const int RoleHealth = 35;

    /// <summary>One piece of the pier pattern: offsets from the pier's centre on the deck line
    /// (du along, v across), its centre's height under D, its turn from the deck's heading, and
    /// whether it is the pier's core (never ruined).</summary>
    private readonly struct Part
    {
        public readonly string Prefab;
        public readonly float Du, V, Dy, Turn;
        public readonly bool Core;
        public Part(string prefab, float du, float v, float dy, float turn, bool core = false)
        {
            Prefab = prefab; Du = du; V = v; Dy = dy; Turn = turn; Core = core;
        }
    }

    /// <summary>The four shafts of a pier, (du, v), and the turn of each one's footing (vanilla's).</summary>
    private static readonly (float du, float v, float footingTurn)[] Shafts =
    {
        (-2f, 0f, 180f), (2f, 0f, 180f), (0f, -1f, 90f), (0f, 1f, 270f),
    };

    /// <summary>The corbel band, the top 8 m of the pier under the deck: vanilla's middle pier, piece for piece.</summary>
    private static readonly Part[] CorbelBand =
    {
        // 9-7 m under the deck: two blocks between the shafts' heads, corbels stepping out.
        new(Block, -1f, 0f, -7f, 90f, core: true), new(Block, 1f, 0f, -7f, 90f, core: true),
        new(Corbel, -3f, -0.5f, -7f, 90f), new(Corbel, -3f, 0.5f, -7f, 90f),
        new(Corbel, -1f, -1.5f, -7f, 90f), new(Corbel, -1f, 1.5f, -7f, 90f),
        new(Corbel, 1f, -1.5f, -7f, 270f), new(Corbel, 1f, 1.5f, -7f, 270f),
        new(Corbel, 3f, -0.5f, -7f, 270f), new(Corbel, 3f, 0.5f, -7f, 270f),
        // 7-5 m: three blocks, fillers either side of the middle one, corbels a metre further out.
        new(Block, -2f, 0f, -5f, 90f, core: true), new(Block, 0f, 0f, -5f, 90f, core: true), new(Block, 2f, 0f, -5f, 90f, core: true),
        new(Filler, 0f, -1.5f, -5f, 90f), new(Filler, 0f, 1.5f, -5f, 90f),
        new(Corbel, -4f, -0.5f, -5f, 90f), new(Corbel, -4f, 0.5f, -5f, 90f),
        new(Corbel, -2f, -1.5f, -5f, 90f), new(Corbel, -2f, 1.5f, -5f, 90f),
        new(Corbel, 2f, -1.5f, -5f, 270f), new(Corbel, 2f, 1.5f, -5f, 270f),
        new(Corbel, 4f, -0.5f, -5f, 270f), new(Corbel, 4f, 0.5f, -5f, 270f),
        // 4-2 m: four blocks under the floors, fillers, corner pieces, the outermost corbels.
        new(Block, -3f, 0f, -3f, 90f, core: true), new(Block, -1f, 0f, -3f, 90f, core: true),
        new(Block, 1f, 0f, -3f, 90f, core: true), new(Block, 3f, 0f, -3f, 90f, core: true),
        new(Filler, 0f, -1.5f, -3f, 90f), new(Filler, 0f, 1.5f, -3f, 90f),
        new(Corbel, -5f, -0.5f, -3f, 90f), new(Corbel, -5f, 0.5f, -3f, 90f),
        new(Corbel, 5f, -0.5f, -3f, 270f), new(Corbel, 5f, 0.5f, -3f, 270f),
        new(CorbelCorner, -3f, -2f, -3f, 90f), new(CorbelCorner, -3f, 2f, -3f, 180f),
        new(CorbelCorner, -1f, -2f, -3f, 0f), new(CorbelCorner, -1f, 2f, -3f, 270f),
        new(CorbelCorner, 1f, 2f, -3f, 180f), new(CorbelCorner, 1f, -2f, -3f, 90f),
        new(CorbelCorner, 3f, -2f, -3f, 0f), new(CorbelCorner, 3f, 2f, -3f, 270f),
    };

    /// <summary>Half-extents of a piece's bounds along the deck, across it and up, at the turns
    /// this layout gives it.</summary>
    public static (float u, float v, float y) Extents(string prefab) => prefab switch
    {
        Footing => (1f, 1f, 4f),
        Band or Block => (1f, 1f, 1f),
        Filler or Corbel or Arch => (1f, 0.5f, 1f),
        CorbelCorner => (0.5f, 0.5f, 1f),
        Floor => (1f, 1f, 0.1f),
        Screen => (0.1f, 1f, 1f),
        HeadLeft or HeadRight => (0.5f, 1f, 1f),
        StepPrefab => (1f, 1f, 0.5f),
        _ => (0.5f, 0.5f, 0.5f),
    };

    // --------------------------------------------------------------- choice

    /// <summary>
    /// Whether a crossing is built as a Dvergr viaduct rather than the wooden kit: a bridge
    /// (never a ford) in the Mistlands whose deck stands <see cref="MinClearance"/> or more over
    /// the ground or the water, its tallest pier at least <see cref="MinViaductPier"/> and no taller than
    /// <see cref="PierCeiling"/>, with no more
    /// than <see cref="MaxEndStairRise"/> of stairs at either end (<see cref="EndStairs"/>). The
    /// one owner of that choice: the plan, the search's valley jumps and the dev command all ask it.
    /// </summary>
    public static bool Applies(RoadCrossing crossing, WorldGenerator world) => Refusal(crossing, world) == null;

    /// <summary>Why <see cref="Applies"/> says no, or null.</summary>
    public static string? Refusal(RoadCrossing crossing, WorldGenerator world)
    {
        if (crossing == null || world == null) return "no crossing";
        if (crossing.Kind != CrossingKind.Bridge) return "a ford";
        if (world.GetBiome(crossing.Center.x, crossing.Center.y) != Heightmap.Biome.Mistlands) return "not in the Mistlands";
        float clearance = Clearance(crossing, world);
        if (clearance < MinClearance) return FormattableString.Invariant($"deck {clearance:F1} m over the ground or water, under {MinClearance:F0} m");
        float tallest = TallestPier(crossing, world);
        if (tallest > PierCeiling) return FormattableString.Invariant($"a pier {tallest:F1} m tall, over the {PierCeiling:F0} m ceiling");
        if (tallest < MinViaductPier - 1e-3f) return FormattableString.Invariant($"tallest pier {tallest:F1} m, under the {MinViaductPier:F0} m floor");
        var (near, far) = EndStairs(crossing, world);
        foreach (EndStair end in new[] { near, far })
            if (!end.Landed || end.Rise > MaxEndStairRise + 1e-3f)
                return FormattableString.Invariant($"end stairs {end.Rise:F0} m{(end.Landed ? "" : " and not down")}, over the {MaxEndStairRise:F0} m most");
        return null;
    }

    /// <summary>The deck's walking surface: the higher of the two heights the road arrives at its ends
    /// (<see cref="StructureEnds.Arrival"/> over the apron outward of each deck end), lifted clear of any water, not the
    /// bank points' own ground (a bank point can be 2-6 m off the road that comes to it).</summary>
    public static float DeckHeight(RoadCrossing crossing, WorldGenerator world)
    {
        var (near, far) = EdgesOf(crossing);
        Vector2 dir = crossing.Direction;
        float G(Vector2 p) => Ground(p, world);
        float deck = Mathf.Max(StructureEnds.Arrival(near, -dir, G), StructureEnds.Arrival(far, dir, G));
        return Mathf.Max(deck, crossing.WaterLevel + BridgeLayout.DeckFreeboard);
    }

    /// <summary>The middles of the deck's two end edges, near then far: where its walking surface stops.</summary>
    internal static (Vector2 near, Vector2 far) EdgesOf(RoadCrossing crossing)
    {
        var (_, _, first, last) = DeckGrid(crossing);
        return (crossing.FromBank + crossing.Direction * (first - Module * 0.5f), crossing.FromBank + crossing.Direction * (last + Module * 0.5f));
    }

    /// <summary>How far the deck stands over the lowest ground between the banks, or over the
    /// water where there is any (the rule's "above the ground or water").</summary>
    public static float Clearance(RoadCrossing crossing, WorldGenerator world) =>
        DeckHeight(crossing, world) - Mathf.Max(crossing.RiverbedHeight, crossing.WaterLevel);

    /// <summary>Whether the crossing is over a known, sailable river: the wooden kit's test
    /// (<see cref="BridgeLayout.FairwayGap"/> reads the same field), not a second one.</summary>
    public static bool OverKnownRiver(RoadCrossing crossing) =>
        crossing.Kind == CrossingKind.Bridge && crossing.FairwayWidth > 0f && BridgeLayout.FairwayGap(crossing) > 0f;

    // ---------------------------------------------------------------- piers

    /// <summary>
    /// Where the piers stand, metres along the crossing from its near bank, nearest first.
    /// <paramref name="fairway"/>: the fairway centre along the crossing on a known river, else null.
    /// Off a river, the midpoint and then every <see cref="PierSpacing"/> outward while inside the
    /// gap, at least the midpoint. On a river, the navigation bay's two piers at the fairway +- 16 m
    /// (<see cref="NavigationBay"/>) first, then every 16 m outward; a bay pier that falls outside the gap is left out.
    /// </summary>
    public static List<float> PierAlongs(float width, float? fairway)
    {
        var piers = new List<float>();
        bool Inside(float a) => a >= PierBankMargin - 1e-3f && a <= width - PierBankMargin + 1e-3f;
        if (fairway is float f)
        {
            float half = NavigationBay * 0.5f;
            for (float a = f - half; a >= -1e-3f; a -= PierSpacing)
                if (Inside(a)) piers.Add(a);
            for (float a = f + half; a <= width + 1e-3f; a += PierSpacing)
                if (Inside(a)) piers.Add(a);
        }
        else
        {
            float mid = width * 0.5f;
            piers.Add(mid);
            for (float a = mid - PierSpacing; Inside(a); a -= PierSpacing) piers.Add(a);
            for (float a = mid + PierSpacing; Inside(a); a += PierSpacing) piers.Add(a);
        }
        piers.Sort();
        return piers;
    }

    /// <summary>The fairway centre along the crossing on a known river, else null.</summary>
    public static float? FairwayAlong(RoadCrossing crossing) =>
        OverKnownRiver(crossing) ? crossing.Along(crossing.FairwayCenter) : null;

    public static List<float> PierAlongs(RoadCrossing crossing) => PierAlongs(crossing.Width, FairwayAlong(crossing));

    /// <summary>The lowest ground under a pier's four shafts.</summary>
    public static float PierGround(RoadCrossing crossing, float along, WorldGenerator world)
    {
        Vector2 dir = crossing.Direction, side = new(-dir.y, dir.x);
        Vector2 at = crossing.FromBank + dir * along;
        float lowest = float.MaxValue;
        foreach (var (du, v, _) in Shafts)
        {
            Vector2 p = at + dir * du + side * v;
            lowest = Mathf.Min(lowest, Ground(p, world));
        }
        return lowest;
    }

    /// <summary>The tallest pier, deck surface to the ground under its lowest shaft (0 with none).</summary>
    public static float TallestPier(RoadCrossing crossing, WorldGenerator world)
    {
        float deck = DeckHeight(crossing, world), tallest = 0f;
        foreach (float a in PierAlongs(crossing))
            tallest = Mathf.Max(tallest, deck - PierGround(crossing, a, world));
        return tallest;
    }

    /// <summary>
    /// The tallest pier over the FINISHED ground: under each pier, the higher of the generator's ground and any road built
    /// there (<see cref="StructureEnds.BuiltRoadNear"/>: the network so far and <paramref name="plans"/>), since a road's
    /// fill raises the valley floor. The floor (<see cref="MinViaductPier"/>) holds on what a player sees: a
    /// viaduct (16 m, centre 5288, 4640) stood about 9 m over its floor, with a road running partly under it, and was
    /// built because the floor was judged on the generator's ground alone.
    /// </summary>
    public static float TallestPierOnFinishedGround(RoadCrossing crossing, WorldGenerator world, IReadOnlyList<RoadSpatialGrid.PlannedPath>? plans)
    {
        float deck = DeckHeight(crossing, world), tallest = 0f;
        Vector2 dir = crossing.Direction;
        foreach (float a in PierAlongs(crossing))
        {
            float ground = PierGround(crossing, a, world);
            Vector2 at = crossing.FromBank + dir * a;
            foreach (var (_, h, w) in StructureEnds.BuiltRoadNear(at, HalfWidth + RoadConstants.DefaultRoadWidth * 0.5f + 1f, plans))
                if (h < deck) ground = Mathf.Max(ground, h);
            tallest = Mathf.Max(tallest, deck - ground);
        }
        return tallest;
    }

    // --------------------------------------------------------------- valleys

    /// <summary>The longest dry valley a viaduct is laid over (six bays).</summary>
    public const float MaxValleySpan = 96f;
    /// <summary>The shortest: two search cells.</summary>
    public const float MinValleySpan = 16f;
    /// <summary>Ground this far over the deck pokes through it.</summary>
    public const float DeckClip = 0.25f;

    /// <summary>
    /// A viaduct straight across a dry Mistlands valley from <paramref name="from"/> to
    /// <paramref name="to"/>, the deck at the higher bank, or null with <paramref name="reason"/>.
    /// What makes the search offer it (RoadPathfinder's valley jump) and the generator record
    /// it: one predicate, so the two cannot disagree. Refused: a span outside
    /// <see cref="MinValleySpan"/>..<see cref="MaxValleySpan"/>, a heading the hammer cannot
    /// build, banks further apart in height than a bridge's (<see cref="RoadConstants.MaxBridgeBankDelta"/>),
    /// water or a river under it (a river is a bridge's crossing), ground over the deck, and
    /// whatever <see cref="Refusal"/> refuses (not the Mistlands, under 6 m, piers too tall).
    /// </summary>
    public static RoadCrossing? ValleyCrossing(Vector2 from, Vector2 to, WorldGenerator world, out string reason)
    {
        float length = Vector2.Distance(from, to);
        if (length < MinValleySpan - 1e-3f || length > MaxValleySpan + 1e-3f) { reason = "span"; return null; }
        Vector2 dir = (to - from) * (1f / length);
        if (!BridgeLayout.HeadingIsPlaceable(BridgeLayout.YawDegrees(dir), 0.5f)) { reason = "heading"; return null; }
        float hFrom = Ground(from, world), hTo = Ground(to, world);
        if (Mathf.Abs(hFrom - hTo) > RoadConstants.MaxBridgeBankDelta) { reason = "banks apart"; return null; }
        float lowest = float.MaxValue;
        var probe = RoadCrossing.Between(from, to, Mathf.Min(hFrom, hTo), (from + to) * 0.5f, 0f, CrossingKind.Bridge);
        float deck = DeckHeight(probe, world);
        int samples = Mathf.CeilToInt(length);
        for (int i = 0; i <= samples; i++)
        {
            Vector2 p = Vector2.Lerp(from, to, i / (float)samples);
            float g = Ground(p, world);
            lowest = Mathf.Min(lowest, g);
            if (g < RoadPathfinder.LandingFloor) { reason = "water"; return null; }
            if (i % 2 == 0)
            {
                world.GetRiverWeight(p.x, p.y, out float river, out _);
                if (river > 0f) { reason = "river"; return null; }
            }
            if (g > deck + DeckClip) { reason = "ground over the deck"; return null; }
        }
        var crossing = RoadCrossing.Between(from, to, lowest, (from + to) * 0.5f, 0f, CrossingKind.Bridge);
        crossing.ValleyJump = true;
        string? refused = Refusal(crossing, world);
        if (refused != null) { reason = refused; return null; }
        reason = "";
        return crossing;
    }

    /// <summary>
    /// The viaducts a search bought (RoadPathfinder.LastViaductJumps) as crossings of the finished
    /// path: a segment whose two ends lie on a jump's (either way round, within a metre) is one,
    /// judged again at the path's own points. Added to <paramref name="crossings"/> in path order;
    /// a segment another crossing already covers is left to it.
    /// </summary>
    public static void AddJumps(List<Vector2> path, IReadOnlyList<(Vector2 from, Vector2 to)> jumps, WorldGenerator world, List<RoadCrossing> crossings)
    {
        if (jumps.Count == 0 || path.Count < 2) return;
        bool added = false;
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1], b = path[i];
            bool onJump = false;
            foreach (var (from, to) in jumps)
                if ((Vector2.Distance(a, from) <= 1f && Vector2.Distance(b, to) <= 1f) || (Vector2.Distance(a, to) <= 1f && Vector2.Distance(b, from) <= 1f))
                { onJump = true; break; }
            if (!onJump) continue;
            bool taken = false;
            foreach (RoadCrossing c in crossings)
                if (c.FromIndex < i && c.ToIndex > i - 1) { taken = true; break; }
            if (taken) continue;
            RoadCrossing? viaduct = ValleyCrossing(a, b, world, out _);
            if (viaduct == null) continue;
            viaduct.FromIndex = i - 1;
            viaduct.ToIndex = i;
            crossings.Add(viaduct);
            added = true;
        }
        if (added) crossings.Sort((x, y) => x.FromIndex.CompareTo(y.FromIndex));
    }

    // ------------------------------------------- what later roads keep clear of

    /// <summary>Pathfinding cells under the viaducts laid this generation, their bank cells apart:
    /// no later road is built through a pier or under a deck. Generation only, like the stairs'.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int, int), byte> s_standing = new();
    private static volatile bool s_anyStanding;
    /// <summary>Cells within this of a viaduct's line stand under it.</summary>
    public const float StandingReach = 6f;

    public static void ResetStanding()
    {
        s_standing.Clear();
        s_anyStanding = false;
        lock (s_laid) s_laid.Clear();
    }

    // ------------------------------------------- between structures

    /// <summary>No viaduct's end within this of another viaduct's end: one vanilla bay (at
    /// 6895,-880 two viaducts' ends stood 8 m apart at different deck heights, a tiny land link and a 90 degree turn, the
    /// second's end out of reach). The road crosses that valley another way; a corner landing at deck height is a later
    /// idea.</summary>
    public const float EndSpacing = PierSpacing;

    /// <summary>How far past each bank a viaduct's end-approach volume reaches: its most end stairs, then the road's
    /// levelled apron (StructureEnds).</summary>
    public static float ApproachReach => BridgeLayout.StairRun * Mathf.CeilToInt(MaxEndStairRise / BridgeLayout.StairRise - 1e-3f) + StructureEnds.ApronLength;

    /// <summary>Half a viaduct's width, its arches and heads included (the course and arches stand 3 m out).</summary>
    public const float HalfWidth = 3f;

    /// <summary>Viaducts laid this generation, banks only (valley and river alike).</summary>
    private static readonly List<(Vector2 from, Vector2 to)> s_laid = new();

    /// <summary>Record a viaduct laid this generation, for <see cref="TooClose"/>.</summary>
    public static void MarkLaid(RoadCrossing crossing)
    {
        lock (s_laid) s_laid.Add((crossing.FromBank, crossing.ToBank));
    }

    private static bool Same((Vector2 from, Vector2 to) x, Vector2 a, Vector2 b) =>
        (Vector2.Distance(x.from, a) <= 1f && Vector2.Distance(x.to, b) <= 1f) || (Vector2.Distance(x.from, b) <= 1f && Vector2.Distance(x.to, a) <= 1f);

    /// <summary>
    /// Whether a viaduct from <paramref name="a"/> to <paramref name="b"/> would stand too close to another: one owner of
    /// the clearance between structures, like stairs against roads. Back to back: an end within <see cref="EndSpacing"/> of
    /// the other's end. Or in it: its deck and end-approach volume (<see cref="Footprint"/>) overlapping the other's, as an
    /// 80 m span's arches ran on past its end into a 64 m span's deck at (6895,-880). The same viaduct (a shared crossing) is
    /// not another. <paramref name="others"/>: the viaducts to keep clear of, else those laid this generation.
    /// </summary>
    public static string? TooClose(Vector2 a, Vector2 b, IReadOnlyList<(Vector2 from, Vector2 to)>? others = null)
    {
        List<(Vector2 from, Vector2 to)> list;
        if (others != null) list = new List<(Vector2, Vector2)>(others);
        else lock (s_laid) { if (s_laid.Count == 0) return null; list = new List<(Vector2, Vector2)>(s_laid); }
        Vector2[] mine = Footprint(a, b);
        foreach (var other in list)
        {
            if (Same(other, a, b)) continue;
            foreach (Vector2 end in new[] { a, b })
                foreach (Vector2 theirs in new[] { other.from, other.to })
                    if (Vector2.Distance(end, theirs) < EndSpacing - 1e-3f)
                        return FormattableString.Invariant($"back to back: an end {Vector2.Distance(end, theirs):F0} m from another viaduct's at ({theirs.x:F0}, {theirs.y:F0})");
            if (Overlap(mine, Footprint(other.from, other.to)))
                return FormattableString.Invariant($"in another viaduct's deck or approach, ({other.from.x:F0}, {other.from.y:F0}) to ({other.to.x:F0}, {other.to.y:F0})");
        }
        return null;
    }

    /// <summary>A viaduct's deck and end-approach volume on the ground: its line from bank to bank lengthened by
    /// <see cref="ApproachReach"/> at each end, <see cref="HalfWidth"/> either side. Four corners.</summary>
    public static Vector2[] Footprint(Vector2 a, Vector2 b)
    {
        Vector2 dir = (b - a).normalized, side = new(-dir.y, dir.x);
        Vector2 s0 = a - dir * ApproachReach, s1 = b + dir * ApproachReach;
        return new[] { s0 - side * HalfWidth, s1 - side * HalfWidth, s1 + side * HalfWidth, s0 + side * HalfWidth };
    }

    /// <summary>Two convex quadrilaterals overlap (separating axes on their edges).</summary>
    private static bool Overlap(Vector2[] p, Vector2[] q)
    {
        foreach (Vector2[] poly in new[] { p, q })
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 e = poly[(i + 1) % poly.Length] - poly[i], axis = new(-e.y, e.x);
                float pMin = float.MaxValue, pMax = float.MinValue, qMin = float.MaxValue, qMax = float.MinValue;
                foreach (Vector2 v in p) { float d = Vector2.Dot(v, axis); pMin = Mathf.Min(pMin, d); pMax = Mathf.Max(pMax, d); }
                foreach (Vector2 v in q) { float d = Vector2.Dot(v, axis); qMin = Mathf.Min(qMin, d); qMax = Mathf.Max(qMax, d); }
                if (pMax <= qMin + 1e-4f || qMax <= pMin + 1e-4f) return false;
            }
        return true;
    }

    /// <summary>Mark the cells under a viaduct closed to road, but for its two bank cells.</summary>
    public static void MarkStanding(RoadCrossing crossing)
    {
        float cell = RoadPathfinder.CellSize;
        (int, int) Cell(Vector2 p) => (Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell));
        var ends = new HashSet<(int, int)> { Cell(crossing.FromBank), Cell(crossing.ToBank) };
        Vector2 a = crossing.FromBank, b = crossing.ToBank;
        int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - StandingReach) / cell), x1 = Mathf.CeilToInt((Mathf.Max(a.x, b.x) + StandingReach) / cell);
        int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - StandingReach) / cell), z1 = Mathf.CeilToInt((Mathf.Max(a.y, b.y) + StandingReach) / cell);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (ends.Contains((x, z))) continue;
                var p = new Vector2(x * cell, z * cell);
                if (DistanceToSegment(p, a, b) <= StandingReach) s_standing[(x, z)] = 0;
            }
        s_anyStanding = true;
    }

    /// <summary>Whether a viaduct laid this generation stands over this pathfinding cell.</summary>
    public static bool Standing(int cellX, int cellZ) => s_anyStanding && s_standing.ContainsKey((cellX, cellZ));

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    // ------------------------------------------------------------ end stairs

    /// <summary>The most stairs, bank to deck, a viaduct has at either end (at most about 3 m of end
    /// stairs between the bank and the deck on each side; past that, no viaduct there). Its default.</summary>
    public const float DefaultMaxEndStairRise = 3f;
    /// <summary>The most end stairs, as set: PROCEDURALROADS_VIADUCT_END_STAIRS (metres, 0-12).
    /// Past it at either end <see cref="Refusal"/> refuses the viaduct: a valley keeps
    /// its stairs or road, a river its wooden bridge. 0: level banks only. Settable for tests.</summary>
    public static float MaxEndStairRise = DebugSwitches.Number("VIADUCT_END_STAIRS", DefaultMaxEndStairRise, 0f, BridgeLayout.MaxStairSteps * BridgeLayout.StairRise);

    /// <summary>
    /// One end's stairs between the deck and the bank: <see cref="Steps"/> blackmarble_stair steps two abreast (the
    /// wooden kit's run, <see cref="BridgeLayout.EmitSteps"/>), each 1 m down and 2 m out along the deck's line, from
    /// the deck's end <see cref="Top"/> to the <see cref="Foot"/> edge a walker steps off onto. None where the bank is
    /// at the deck's height (within half a step): the road runs onto the deck.
    /// </summary>
    public readonly struct EndStair
    {
        public readonly int Steps;
        /// <summary>Whether the run reached the ground (not, when the bank falls away faster than the steps).</summary>
        public readonly bool Landed;
        /// <summary>The middle of the deck's end edge, where the stairs leave it.</summary>
        public readonly Vector2 Top;
        /// <summary>The middle of the bottom step's foot edge: where the road has to be.</summary>
        public readonly Vector2 Foot;
        /// <summary>The way up the flight, onto the deck (along the deck's line, toward the other bank).</summary>
        public readonly Vector2 Up;
        public float Rise => Steps * BridgeLayout.StairRise;
        public EndStair(int steps, bool landed, Vector2 top, Vector2 up)
        {
            Steps = steps; Landed = landed; Top = top; Up = up;
            Foot = top - up * (BridgeLayout.StairRun * steps);
        }
    }

    /// <summary>The deck's module grid along the crossing: the piers, the anchor every module hangs on, and the first and
    /// last module centres (each reaching onto its bank: its centre within a metre of it, or behind it).</summary>
    private static (List<float> piers, float anchor, float first, float last) DeckGrid(RoadCrossing crossing)
    {
        float width = crossing.Width;
        float? fairway = FairwayAlong(crossing);
        List<float> piers = PierAlongs(width, fairway);
        // Every module, pier and arch hangs on one grid: the piers' (vanilla's modules are centred on its piers).
        float anchor = piers.Count > 0 ? piers[0] : fairway ?? width * 0.5f;
        float first = anchor - Module * Mathf.CeilToInt((anchor - 1f) / Module - 1e-4f);
        float last = anchor + Module * Mathf.CeilToInt((width - 1f - anchor) / Module - 1e-4f);
        return (piers, anchor, first, last);
    }

    /// <summary>
    /// The stairs at each end, near (from-bank) end first: the one owner of how many steps an end has, where their foot
    /// is, and whether there are any. <see cref="Solve"/> lays exactly these, <see cref="Refusal"/> caps them
    /// (<see cref="MaxEndStairRise"/>), <see cref="Price"/> charges for them, and the generator puts the road through
    /// their foot (<see cref="MeetTheRoad"/>).
    /// </summary>
    public static (EndStair near, EndStair far) EndStairs(RoadCrossing crossing, WorldGenerator world)
    {
        var (near, far) = Ends(crossing, world);
        if (!near.IsSet) return default;
        return (new EndStair(near.Steps, true, near.Edge, near.Up), new EndStair(far.Steps, true, far.Edge, far.Up));
    }

    /// <summary>
    /// Each end as it meets the road, near (from-bank) end first, decided by the one owner (<see cref="StructureEnds.Decide"/>):
    /// the deck's edge, its height, and the stairs whose foot sits on the ground the road arrives on. A run always lands:
    /// the road's apron is levelled to its foot (RoadSpatialGrid.LevelledEnds), and the generator refuses the viaduct where
    /// the road as planned does not meet it.
    /// </summary>
    public static (StructureEnds.End near, StructureEnds.End far) Ends(RoadCrossing crossing, WorldGenerator world)
    {
        if (crossing == null || world == null || crossing.Width < Module) return default;
        Vector2 dir = crossing.Direction;
        float deck = DeckHeight(crossing, world);
        var (nearEdge, farEdge) = EdgesOf(crossing);
        float G(Vector2 p) => Ground(p, world);
        return (StructureEnds.Decide(nearEdge, dir, deck, G), StructureEnds.Decide(farEdge, -dir, deck, G));
    }

    // ----------------------------------------------------------------- price

    /// <summary>
    /// What the search pays for a viaduct, the road on it apart. Its own price,
    /// <paramref name="fixedCost"/> plus <paramref name="perMeter"/> per metre of span, times
    /// <see cref="HeightFactor"/> of its tallest pier: "the price falls as the piers get taller, up to the pier
    /// ceiling", and steeply over <see cref="PriceHeight"/> (height pays more, then a lower crossing is the fallback
    /// if nothing is found). On top, <paramref name="endStairPerMeter"/> per metre of its end stairs
    /// (<see cref="EndStairs"/>): "stairs to or from a viaduct cost much more than ordinary stairs", so level banks win.
    /// The one owner of a viaduct's price: the search's valley jump asks it.
    /// </summary>
    public static float Price(RoadCrossing crossing, WorldGenerator world, float fixedCost, float perMeter, float endStairPerMeter)
    {
        var (near, far) = EndStairs(crossing, world);
        return (fixedCost + perMeter * crossing.Width) * HeightFactor(TallestPier(crossing, world)) + endStairPerMeter * (near.Rise + far.Rise);
    }

    /// <summary>
    /// The share of its base price a viaduct with its tallest pier <paramref name="tallest"/> m pays (taken between
    /// <see cref="MinClearance"/> and <see cref="PierCeiling"/>).
    /// <para>Up to <see cref="PriceHeight"/> it is the plain inverse of the height (1 at 10 m, 1.67 at 6 m), so a low
    /// viaduct costs what it did and is never priced out into stairs: the fallback is the same search, not a second
    /// pass. Over it the share falls geometrically to <see cref="CeilingPriceShare"/> at the ceiling (0.57 at 14 m,
    /// 0.38 at 17 m, 0.25 at 20 m, against the inverse's 0.71, 0.59 and 0.5), and never above the inverse: a valley that
    /// offers a high crossing and a low one has the high one win clearly. With <see cref="CeilingPriceShare"/> 0.5 it
    /// is the inverse price exactly.</para>
    /// </summary>
    public static float HeightFactor(float tallest)
    {
        float h = Mathf.Clamp(tallest, MinClearance, PierCeiling);
        float inverse = PriceHeight / h;
        if (h <= PriceHeight || PierCeiling <= PriceHeight) return inverse;
        float steep = (float)Math.Pow(CeilingPriceShare, (h - PriceHeight) / (PierCeiling - PriceHeight));
        return Mathf.Min(inverse, steep);
    }

    /// <summary>The pier height at which a viaduct costs its base price (<see cref="Price"/>), and under which it is
    /// priced as the plain inverse. Its default, about the median tallest pier (9.8 m on six test
    /// worlds): with the inverse price anchored at 20 m (the ceiling) 16 valley viaducts were left of 79, at 10 m
    /// 59.</summary>
    public const float DefaultPriceHeight = 10f;
    /// <summary>Settable for tests.</summary>
    public static float PriceHeight = DefaultPriceHeight;
    /// <summary>The share of its base price a viaduct on piers at the ceiling pays (about a quarter).
    /// Its default.</summary>
    public const float DefaultCeilingPriceShare = 0.25f;
    /// <summary>Settable for tests (0.5: the inverse price).</summary>
    public static float CeilingPriceShare = DefaultCeilingPriceShare;

    // ------------------------------------------------------- meeting the road

    /// <summary>
    /// Puts <paramref name="path"/> through the foot of each end's stairs of every viaduct on it, so the road arrives
    /// at the foot along the flight's own line: the in-line case of the staircases' foot rule, the foot centred on the
    /// road (at the far bank at -6598, 4551 the stairs came down the deck's line while the road
    /// came in at the side of its end, so they never met). The search buys a viaduct from whatever side its road comes
    /// to the rim: making it come at the rim head on, a cell straight on each side, was tried and lost 9 of 10 valley
    /// viaducts (a rim is often a crest the road climbs to on stairs, seen on RoadSeedC and D). So the
    /// path is turned onto the flight's line here, after the search, for valley and river viaducts alike; the
    /// planner then holds the foot (RoadSpatialGrid.HeldPoints) so the pull and the sway keep it.
    /// <para>From each bank (<see cref="RoadCrossing.FromIndex"/> back, <see cref="RoadCrossing.ToIndex"/> on) the road
    /// is walked away from the deck. Its points beside the flight (nearer the bank than the foot, off the flight's
    /// line) are dropped; the foot goes in before the first point at or behind it, which turns the road onto the line
    /// where that point is off it, and pins the road's curve to the foot where it is on it. Where the road ends beside
    /// or under the flight, or the stretch is a stair step the search bought (<paramref name="isStep"/>: a staircase
    /// is laid from its two ends), the path is left and the end counts in <paramref name="unmet"/>.</para>
    /// Returns how many ends were turned onto the flight's line; <paramref name="pinned"/> counts the feet put in on a
    /// road already on it. The caller finds the crossings on the path again when either is above 0.
    /// </summary>
    public static int MeetTheRoad(List<Vector2> path, IReadOnlyList<RoadCrossing> viaducts, WorldGenerator world,
        out int unmet, out int pinned, Func<Vector2, Vector2, bool>? isStep = null, List<Vector2>? unmetFeet = null)
    {
        int changed = 0;
        unmet = pinned = 0;
        // Last first, far end before near, so a change does not move an index still to do.
        foreach (RoadCrossing crossing in viaducts.OrderByDescending(c => c.FromIndex))
        {
            var (near, far) = BridgeStructures.Ends(crossing, world);
            if (!near.IsSet) continue;
            foreach (var (end, bank, start, step) in new[] { (far, crossing.ToBank, crossing.ToIndex, +1), (near, crossing.FromBank, crossing.FromIndex, -1) })
            {
                int before = path.Count;
                bool? met = Meet(path, end, bank, start, step, isStep);
                if (met == null) { unmet++; unmetFeet?.Add(end.Meet); }
                else if (met == false) changed++;
                else if (path.Count != before) pinned++;
            }
        }
        return changed;
    }

    /// <summary>One end of <see cref="MeetTheRoad"/>: true where the road already ran up the flight's line (the foot
    /// pinned on it), false where the path was turned onto it, null where it cannot be (the path left as it was).</summary>
    private static bool? Meet(List<Vector2> path, StructureEnds.End structureEnd, Vector2 bank, int start, int step, Func<Vector2, Vector2, bool>? isStep)
    {
        // The road comes in along the structure's line over its levelled apron (StructureEnds.ApronLength): its far end is
        // the point the path is put through, and what lies between it and the bank stays only on the line.
        var end = (Up: structureEnd.Up, Foot: structureEnd.Apron);
        Vector2 back = -end.Up, across = new(-back.y, back.x);
        var beside = new List<int>();
        int previous = -1;
        for (int i = start; i >= 0 && i < path.Count; i += step)
        {
            Vector2 q = path[i];
            if (Vector2.Distance(q, bank) < 0.5f) { previous = i; continue; }
            float along = Vector2.Dot(q - end.Foot, back);
            bool onLine = Mathf.Abs(Vector2.Dot(q - end.Foot, across)) <= 1f + Mathf.Max(0f, along) * 0.0875f;   // tan 5 degrees
            bool roadsEnd = i == 0 || i == path.Count - 1;
            if (along < -0.5f)
            {
                // Beside or under the flight: on its line the road runs up it; off it, the point is dropped.
                if (roadsEnd) return null;
                if (!onLine) beside.Add(i);
                if (isStep?.Invoke(path[previous < 0 ? i : previous], q) ?? false) return null;
                previous = i;
                continue;
            }
            if (isStep?.Invoke(path[previous < 0 ? i : previous], q) ?? false) return onLine && beside.Count == 0 ? true : null;
            // Drop the points beside the flight, then put the foot in between this point and the next one in: off the
            // line it turns the road onto it; on it, it pins the road's curve to the foot, which a corner a few metres
            // on otherwise bows off the line (0.6 m at the foot on a test valley).
            foreach (int k in beside.OrderByDescending(k => k)) path.RemoveAt(k);
            int at = step > 0 ? i - beside.Count : i + 1;
            if (Vector2.Distance(path[step > 0 ? at : at - 1], end.Foot) > 0.5f) path.Insert(at, end.Foot);
            return onLine && beside.Count == 0;
        }
        return null;   // the road ends under the flight
    }

    // ---------------------------------------------------------------- solve

    /// <summary>The viaduct as built: ruined, the navigation gap open.</summary>
    public static List<StructurePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Solve(crossing, world, worldSeed, ruin: true);

    /// <summary>The viaduct with nothing missing but the navigation gap's deck (a diagnostic, and
    /// the tests' reference).</summary>
    internal static List<StructurePiece> SolveComplete(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Solve(crossing, world, worldSeed, ruin: false);

    private static List<StructurePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed, bool ruin)
    {
        var pieces = new List<StructurePiece>();
        if (crossing == null || world == null || crossing.Width < Module) return pieces;

        var r = new BridgeLayout.Ruin(worldSeed, crossing);
        Vector2 dir = crossing.Direction, side = new(-dir.y, dir.x);
        float yaw = BridgeLayout.YawDegrees(dir);
        float deck = DeckHeight(crossing, world);
        float width = crossing.Width;
        float? fairway = FairwayAlong(crossing);
        float navigationGap = fairway.HasValue ? NavigationGapFor(crossing) : 0f;
        // The first module reaches onto the near bank (its centre within a metre of it, or behind it), the last onto the far.
        var (piers, anchor, first, last) = DeckGrid(crossing);
        int modules = Mathf.RoundToInt((last - first) / Module) + 1;
        float Mid(float along) => width <= 0f ? 0f : Mathf.Clamp01(1f - Mathf.Abs(along - width * 0.5f) / (width * 0.5f));
        float Survives(float along, float share) => 1f - (1f - BridgeLayout.SpanSurvival(Mid(along))) * share;

        Vector2 At(float along, float v) => crossing.FromBank + dir * along + side * v;
        // The ground as finished at the ends: cut to the meeting height where the road runs on under the end module
        // (StructureEnds.JoinOverlap), so that module is not dropped as buried under a bank the road cuts away.
        var (nearEnd, farEnd) = Ends(crossing, world);
        float G(Vector2 p) => StructureEnds.FinishedGround(p, Ground(p, world), nearEnd, farEnd, HalfWidth + BuriedPieces.Growth);
        void Add(StructurePieceKind kind, string prefab, float along, float v, float y, float turn, float health)
        {
            Vector2 p = At(along, v);
            if (WhollyBuried(prefab, p, dir, side, y, G)) return;
            pieces.Add(new StructurePiece
            {
                Kind = kind, Prefab = prefab, Position = new Vector3(p.x, y, p.y),
                YawDegrees = Wrap(yaw + turn), HealthFraction = health,
            });
        }

        // ----- piers
        var pierTops = new List<float>();
        for (int k = 0; k < piers.Count; k++)
        {
            float a = piers[k];
            float pierSurvival = BridgeLayout.PierSurvival(BridgeLayout.SpanSurvival(Mid(a)));
            if (deck - PierGround(crossing, a, world) < MinPier) continue;   // the bank itself: nothing to stand
            pierTops.Add(a);
            foreach (Part part in CorbelBand)
            {
                int key = PartKey(part);
                if (ruin && !part.Core && r.Roll(k, key, RoleOrnament) >= pierSurvival) continue;
                Add(StructurePieceKind.ViaductPier, part.Prefab, a + part.Du, part.V, deck + part.Dy, part.Turn,
                    1f);
            }
            foreach (var (du, v, footingTurn) in Shafts)
            {
                float ground = Ground(At(a + du, v), world);
                float bottom = deck + ShaftTop;
                // Down from under the corbel band, a band every 8 m (vanilla's two at 9 and 17 m under the deck).
                for (int level = 0; ; level++)
                {
                    float centre = deck + ShaftTop - 1f - level * 2f;
                    if (centre - 1f <= ground) break;
                    Add(StructurePieceKind.ViaductPier, level % 4 == 0 ? Band : Block, a + du, v, centre, 90f, 1f);
                    bottom = centre - 1f;
                }
                // The footing, its top under the lowest block or the corbel band (at most 2 m over the
                // ground), sunk 8 m: through the ground's surface, so the game grounds it.
                if (bottom > ground + 0.01f)
                    Add(StructurePieceKind.ViaductPier, Footing, a + du, v, bottom - 4f, footingTurn, 1f);
            }
            // The iron screens through the pier, from the deck down until one is in the ground.
            foreach (float du in new[] { -1f, 1f })
            {
                float ground = Ground(At(a + du, 0f), world);
                for (int level = 0; ; level++)
                {
                    float centre = deck - FloorDrop - level * 2f;
                    if (centre + 1f <= ground) break;
                    if (!ruin || level == 0 || r.Roll(k, du < 0 ? -1 : 1, RoleScreen, level) < pierSurvival)
                        Add(StructurePieceKind.ViaductPier, Screen, a + du, 0f, centre, du < 0 ? 0f : 180f, 1f);
                    if (centre <= ground) break;
                }
            }
        }

        // ----- deck: which floors stand
        var floor = new bool[modules, 2];
        var gap = new bool[modules];
        for (int m = 0; m < modules; m++)
        {
            float c = first + m * Module;
            gap[m] = fairway is float f && Mathf.Abs(c - f) <= navigationGap * 0.5f - Module * 0.5f + 1e-3f;
            bool end = m == 0 || m == modules - 1;
            for (int lane = 0; lane < 2; lane++)
                floor[m, lane] = !gap[m] && (!ruin || end || r.Roll(m, lane == 0 ? -1 : 1, RoleFloor) < Survives(c, DeckRuinShare));
        }
        // Now and then a whole module open, a gap a player jumps (never at an end, never two within three modules).
        var holes = new List<int>();
        if (ruin)
            for (int m = 1; m + 1 < modules; m++)
                if (!gap[m] && (holes.Count == 0 || m - holes[holes.Count - 1] >= 3)
                    && r.Roll(m, 0, RoleHole) >= Survives(first + m * Module, HoleRuinShare))
                {
                    holes.Add(m);
                    floor[m, 0] = floor[m, 1] = false;
                }
        // A way across a player jumps: never two modules running open from side to side (but where the river's gap is).
        for (int m = 1; m < modules; m++)
            if (!gap[m] && !gap[m - 1] && !floor[m, 0] && !floor[m, 1] && !floor[m - 1, 0] && !floor[m - 1, 1])
                floor[m, r.Roll(m, 0, RoleFloor, 1) < 0.5f ? 0 : 1] = true;
        // Every floor joined, floor to floor, to one a pier's corbel or the ground carries.
        var anchored = new bool[modules, 2];
        for (int m = 0; m < modules; m++)
        {
            float c = first + m * Module;
            bool onPier = pierTops.Exists(p => Mathf.Abs(c - p) <= 4f + 1e-3f);
            bool grounded = deck - FloorDrop - 0.1f <= G(At(c, 0f)) + 0.05f;
            for (int lane = 0; lane < 2; lane++)
                anchored[m, lane] = onPier || grounded || deck - FloorDrop - 0.1f <= G(At(c, lane == 0 ? -1f : 1f)) + 0.05f;
        }
        bool[,] Carry()
        {
            var carried = new bool[modules, 2];
            var queue = new Queue<(int m, int lane)>();
            for (int m = 0; m < modules; m++)
                for (int lane = 0; lane < 2; lane++)
                    if (floor[m, lane] && anchored[m, lane]) { carried[m, lane] = true; queue.Enqueue((m, lane)); }
            while (queue.Count > 0)
            {
                var (m, lane) = queue.Dequeue();
                foreach (var (nm, nl) in new[] { (m - 1, lane), (m + 1, lane), (m, 1 - lane) })
                {
                    if (nm < 0 || nm >= modules || !floor[nm, nl] || carried[nm, nl]) continue;
                    carried[nm, nl] = true;
                    queue.Enqueue((nm, nl));
                }
            }
            return carried;
        }
        bool[,] carried = Carry();
        // A hole that cut floors off from every pier left a longer gap than a jump: closed again, nearest the cut first.
        for (int guard = 0; guard < modules; guard++)
        {
            int open = -1;
            for (int m = 1; m < modules && open < 0; m++)
                if (!gap[m] && !gap[m - 1] && !carried[m, 0] && !carried[m, 1] && !carried[m - 1, 0] && !carried[m - 1, 1]) open = m;
            if (open < 0) break;
            int hole = holes.Count == 0 ? -1 : holes.OrderBy(h => Mathf.Abs(h - open)).First();
            if (hole < 0 || Mathf.Abs(hole - open) > 8)
            {
                // Not a hole's doing: both floors of the two modules put back.
                floor[open, 0] = floor[open, 1] = floor[open - 1, 0] = floor[open - 1, 1] = true;
            }
            else
            {
                holes.Remove(hole);
                floor[hole, 0] = floor[hole, 1] = true;
            }
            carried = Carry();
        }

        // ----- deck pieces
        var course = new bool[modules, 3];   // v = -2, 0, +2
        for (int m = 0; m < modules; m++)
        {
            float c = first + m * Module;
            // An end module is the joining piece the road runs on under (StructureEnds.JoinOverlap): its course is never
            // ruined, or the deck's walking surface stops a module short of the road (at the 80 m viaduct at (-3968, -5384) a
            // black marble floor laid by hand there left the road and deck nearly even).
            bool joining = m == 0 || m == modules - 1;
            bool overPier = pierTops.Exists(p => Mathf.Abs(c - p) < 0.01f);
            for (int lane = 0; lane < 2; lane++)
            {
                if (!carried[m, lane]) continue;
                int sideKey = lane == 0 ? -1 : 1;
                Add(StructurePieceKind.ViaductDeck, Floor, c, sideKey * 1f, deck - FloorDrop, 0f, 1f);
            }
            if (overPier)
            {
                // Over a pier: two blocks on the floors, a head either side (vanilla's).
                for (int lane = 0; lane < 2; lane++)
                {
                    if (!carried[m, lane]) continue;
                    int sideKey = lane == 0 ? -1 : 1;
                    if (ruin && !joining && r.Roll(m, sideKey, RoleCourse) >= Survives(c, CourseRuinShare)) continue;
                    course[m, lane == 0 ? 0 : 2] = true;
                    Add(StructurePieceKind.ViaductDeck, Block, c, sideKey * 1f, deck - CourseDrop, 90f, 1f);
                    if (!ruin || r.Roll(m, sideKey, RoleHead) < BridgeLayout.PierSurvival(Survives(c, ArchRuinShare)))
                        Add(StructurePieceKind.ViaductParapet, sideKey > 0 ? HeadLeft : HeadRight, c, sideKey * 3f, deck - CourseDrop,
                            sideKey > 0 ? 270f : 90f, 1f);
                }
                continue;
            }
            for (int slot = 0; slot < 3; slot++)
            {
                int v = (slot - 1) * 2;
                bool under = v < 0 ? carried[m, 0] : v > 0 ? carried[m, 1] : carried[m, 0] || carried[m, 1];
                if (!under) continue;
                if (ruin && !joining && r.Roll(m, v, RoleCourse) >= Survives(c, CourseRuinShare)) continue;
                course[m, slot] = true;
                Add(StructurePieceKind.ViaductDeck, Block, c, v, deck - CourseDrop, 90f, 1f);
            }
        }
        // Arches along both edges every 2 m, between module centres, each on the course of at
        // least one of the two modules it stands across; vanilla pairs them, tall ends together
        // every 4 m from a pier.
        for (int m = 0; m + 1 < modules; m++)
        {
            float a = first + m * Module + Module * 0.5f;
            int phase = ((Mathf.RoundToInt(a - anchor) % 4) + 4) % 4;
            foreach (int sideKey in new[] { -1, 1 })
            {
                int slot = sideKey < 0 ? 0 : 2;
                // Over a pier the course is at v +-1: its outer face carries the arch there.
                if (!course[m, slot] && !course[m + 1, slot]) continue;
                if (ruin && r.Roll(m, sideKey, RoleArch) >= Survives(a, ArchRuinShare)) continue;
                Add(StructurePieceKind.ViaductParapet, Arch, a, sideKey * 2.5f, deck + ArchRise, phase == 1 ? 270f : 90f,
                    1f);
            }
        }

        // ----- ends: a bank lower than the deck is walked off on marble steps, the wooden kit's run (EndStairs: none
        // where the bank is at the deck's height, and the road runs onto the deck).
        foreach (var (end, key) in new[] { (nearEnd, 0), (farEnd, modules) })
        {
            if (end.Steps == 0) continue;
            var steps = new List<StructurePiece>();
            BridgeLayout.EmitSteps(steps, world, end.Edge, end.Up, side, deck, r, key, StepPrefab, Block, end.Steps);
            foreach (StructurePiece s in steps)
            {
                s.HealthFraction = 1f;
                pieces.Add(s);
            }
        }
        if (ruin) ApplyRandomDamage(pieces, r);
        return pieces;
    }

    /// <summary>The health a ruined viaduct's piece is placed with, as a fraction of its prefab's: vanilla's
    /// Location.m_applyRandomDamage range. ZoneSystem.SpawnLocation sets WearNTear.m_randomInitialDamage for a location that
    /// applies it (the Mistlands viaducts do), and WearNTear.Awake then puts every piece at Random.Range(0.1, 0.6) of its
    /// health (decompiled game, WearNTear.cs 322-325). A ruin follows what the game does to the Mistlands prefabs it places, not 100 % health.</summary>
    public const float RandomDamageMin = 0.1f, RandomDamageMax = 0.6f;

    /// <summary>
    /// Every piece of a ruined viaduct damaged as vanilla damages a location's pieces (<see cref="RandomDamageMin"/> to
    /// <see cref="RandomDamageMax"/> of its health), fixed per crossing and world seed: the roll is keyed by the piece's
    /// place and prefab, not drawn from Unity's random state, so the server and every reload agree.
    /// </summary>
    internal static void ApplyRandomDamage(List<StructurePiece> pieces, BridgeLayout.Ruin r)
    {
        foreach (StructurePiece p in pieces)
        {
            int key = unchecked(Mathf.RoundToInt(p.Position.x * 2f) * 73856093 ^ Mathf.RoundToInt(p.Position.z * 2f) * 19349663);
            int side = Mathf.RoundToInt(p.Position.y * 2f);
            int extra = StableHash(p.Prefab) ^ Mathf.RoundToInt(p.YawDegrees);
            p.HealthFraction = RandomDamageMin + r.Roll(key, side, RoleHealth, extra) * (RandomDamageMax - RandomDamageMin);
        }
    }

    /// <summary>A hash of a prefab name that is the same in every process (string.GetHashCode is not on .NET Core).</summary>
    private static int StableHash(string? s)
    {
        unchecked
        {
            int h = 17;
            if (s != null) foreach (char ch in s) h = h * 31 + ch;
            return h;
        }
    }

    /// <summary>A stable small number per pier part, for its ruin key.</summary>
    private static int PartKey(Part part) =>
        Mathf.RoundToInt(part.Du * 2f) * 1000 + Mathf.RoundToInt(part.V * 2f) * 10 + Mathf.RoundToInt(-part.Dy);

    private static float Wrap(float deg)
    {
        deg %= 360f;
        return deg < 0f ? deg + 360f : deg;
    }

    /// <summary>The ground the layout stands on: the game's blended terrain, as the wooden kit's.</summary>
    internal static float Ground(Vector2 p, WorldGenerator world) => BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);

    /// <summary>Whether a piece would be wholly under the ground: its top at or under the ground
    /// over its whole footprint grown by <see cref="BuriedPieces.Growth"/> (the game breaks such a
    /// piece: it touches no terrain triangle, so it is not grounded).</summary>
    internal static bool WhollyBuried(string prefab, Vector2 centre, Vector2 dir, Vector2 side, float y, WorldGenerator world) =>
        WhollyBuried(prefab, centre, dir, side, y, p => Ground(p, world));

    /// <summary>As above, on <paramref name="ground"/> (the finished ground at the ends, StructureEnds.FinishedGround).</summary>
    internal static bool WhollyBuried(string prefab, Vector2 centre, Vector2 dir, Vector2 side, float y, Func<Vector2, float> ground)
    {
        var (hu, hv, hy) = Extents(prefab);
        float top = y + hy + BuriedPieces.Growth;
        hu += BuriedPieces.Growth; hv += BuriedPieces.Growth;
        foreach (float su in new[] { -1f, 0f, 1f })
            foreach (float sv in new[] { -1f, 0f, 1f })
                if (ground(centre + dir * (su * hu) + side * (sv * hv)) < top)
                    return false;
        return true;
    }

    // ----------------------------------------------------------- describing

    /// <summary>One line about a viaduct: where, how long, how high, its piers. For the dev
    /// command and the generation log.</summary>
    public static string Describe(RoadCrossing crossing, WorldGenerator world)
    {
        float deck = DeckHeight(crossing, world);
        List<float> piers = PierAlongs(crossing);
        return FormattableString.Invariant(
            $"viaduct at ({crossing.Center.x:F0}, {deck:F0}, {crossing.Center.y:F0}): {crossing.Width:F0} m from ({crossing.FromBank.x:F0}, {crossing.FromBank.y:F0}) to ({crossing.ToBank.x:F0}, {crossing.ToBank.y:F0}), deck {Clearance(crossing, world):F1} m over the {(OverKnownRiver(crossing) ? "water" : "ground")}, {piers.Count} pier(s), tallest {TallestPier(crossing, world):F1} m{(OverKnownRiver(crossing) ? ", navigation gap" : "")}");
    }
}
