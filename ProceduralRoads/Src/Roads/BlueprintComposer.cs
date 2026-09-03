using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Bridges as blueprints, both ways: a kit of START / SPAN / END blueprints
/// is composed along a crossing into a plan (BridgePieces, what RuinPlacement
/// spawns), and a solved plan is exported as one blueprint per site, in the
/// format jneb802's valheimCreative saves and Expand World loads, so a
/// generated bridge can be inspected, edited in PlanBuild, or dropped into a
/// creative zone. Pure logic; the same code runs in the harness and the game.
///
/// Frame: a blueprint's local +z runs from the near bank toward the far
/// bank, +y up, +x to the road's right; its anchor (first snap point, else
/// bottom centre) is held at a distance along the crossing line, at the
/// deck height there. Rotations compose as Unity does: a piece's local yaw
/// adds to the crossing's, pitch and roll carry through unchanged.
/// </summary>
public static class BlueprintComposer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Where a placed piece's local +x goes: Unity's yaw turns +x
    /// onto (dir.y, -dir.x), the right-hand side of the crossing line.</summary>
    public static Vector2 Right(RoadCrossing c) => new(c.Direction.y, -c.Direction.x);

    /// <summary>The piece's role for the support model and ruin rules: what
    /// its data field says (our exports carry it), else what its prefab is
    /// in the kit. A deck-prefab piece with nothing else to go on is a deck.</summary>
    public static BridgePieceKind KindOf(BridgeStyle style, BlueprintPiece p)
    {
        string? kind = p.DataValue("kind");
        if (kind != null && Enum.TryParse(kind, true, out BridgePieceKind fromData))
            return fromData;
        if (Is(p.Prefab, style.PilingPrefab)) return BridgePieceKind.Piling;
        if (Is(p.Prefab, style.BeamPrefab)) return BridgePieceKind.Beam;
        if (Is(p.Prefab, style.ArchPrefab)) return BridgePieceKind.Arch;
        if (Is(p.Prefab, style.StairPrefab)) return BridgePieceKind.Stair;
        if (Is(p.Prefab, style.DeckPrefab)) return BridgePieceKind.Deck;
        if (Is(p.Prefab, style.AbutmentPrefab)) return BridgePieceKind.Abutment;
        return BridgePieceKind.Debris;
    }

    private static bool Is(string prefab, string kitPrefab) => !string.IsNullOrEmpty(kitPrefab) && prefab == kitPrefab;

    public static float HealthOf(BlueprintPiece p, float fallback)
    {
        string? h = p.DataValue("health");
        return h != null && float.TryParse(h, NumberStyles.Float, Inv, out float value) ? value : fallback;
    }

    /// <summary>
    /// Places one blueprint on a crossing: its anchor goes
    /// <paramref name="originAlong"/> metres from the near bank along the
    /// crossing line, and every piece sits <paramref name="heightAt"/>(its
    /// own distance along) plus its local height — so a graded deck grades
    /// piece by piece, the way BridgeLayout's stations do.
    /// </summary>
    public static void Place(List<BridgePiece> into, RoadBlueprint bp, RoadCrossing c, BridgeStyle style,
        float originAlong, Func<float, float> heightAt)
    {
        float yaw = BridgeLayout.YawDegrees(c.Direction);
        Vector2 right = Right(c);
        Vector3 anchor = bp.Anchor;
        foreach (BlueprintPiece p in bp.Pieces)
        {
            Vector3 local = p.LocalPosition - anchor;
            float along = originAlong + local.z;
            Vector2 xz = c.FromBank + c.Direction * along + right * local.x;
            (float pitch, float localYaw, float roll) = p.Euler;
            into.Add(new BridgePiece
            {
                Kind = KindOf(style, p),
                Prefab = p.Prefab,
                Position = new Vector3(xz.x, heightAt(along) + local.y, xz.y),
                YawDegrees = yaw + localYaw,
                PitchDegrees = pitch,
                RollDegrees = roll,
                HealthFraction = HealthOf(p, 1f),
            });
        }
    }

    /// <summary>Deck height along a crossing: graded between the deck's end
    /// heights (bank ground plus any stepped-end rise) and never below the
    /// water plus the kit's freeboard — BridgeLayout's rule.</summary>
    public static Func<float, float> DeckGrade(RoadCrossing c, WorldGenerator world, BridgeStyle style)
    {
        (float deckFromH, float deckToH) = BridgeLayout.DeckEndHeights(c, world);
        float minDeck = c.WaterLevel + style.DeckFreeboard;
        return along => Mathf.Max(Mathf.Lerp(deckFromH, deckToH, Mathf.Clamp01(along / c.Width)), minDeck);
    }

    /// <summary>
    /// Composes a kit across a crossing: START held at the near bank, SPAN
    /// repeated by snap-point chaining, END held so its far snap point lands
    /// on the far bank. A crossing is rarely a whole number of spans, so the
    /// spans are pitched evenly over what START and END leave, one more than
    /// fits rather than one fewer: joints overlap a little, they never open a
    /// hole a walker falls through (a 10 m crossing with a 4 m kit left a 2 m
    /// gap when rounded to nearest). Each kit brings its own span length (a
    /// 2 m plank, a 4 m arch); nothing here assumes one.
    /// </summary>
    public static List<BridgePiece> Tile(RoadCrossing c, WorldGenerator world, BridgeStyle style,
        RoadBlueprint start, RoadBlueprint span, RoadBlueprint end)
    {
        List<BridgePiece> pieces = new();
        Func<float, float> deckAt = DeckGrade(c, world, style);

        Place(pieces, start, c, style, 0f, deckAt);
        float budget = c.Width - start.Length - end.Length;
        int spans = SpanCount(budget, span.Length);
        float pitch = spans > 0 ? budget / spans : 0f;
        for (int i = 0; i < spans; i++)
            Place(pieces, span, c, style, start.Length + i * pitch, deckAt);
        Place(pieces, end, c, style, c.Width - end.Length, deckAt);
        return pieces;
    }

    /// <summary>How many spans fill <paramref name="budget"/> metres: enough
    /// to cover it (rounded up, with a hair of tolerance for a whole number),
    /// none when there is nothing to fill.</summary>
    public static int SpanCount(float budget, float spanLength)
    {
        if (budget <= 0.01f || spanLength <= 0.01f)
            return 0;
        return Mathf.Max(1, Mathf.CeilToInt(budget / spanLength - 0.001f));
    }

    /// <summary>Posts stacked down to the bed: a kit post is one segment
    /// under the deck; below it the ground decides how many more, exactly as
    /// BridgeLayout's stations do. Everything else passes through.</summary>
    public static List<BridgePiece> GroundPosts(List<BridgePiece> pieces, WorldGenerator world, BridgeStyle style)
    {
        List<BridgePiece> grounded = new();
        foreach (BridgePiece p in pieces)
        {
            grounded.Add(p);
            if (p.Kind != BridgePieceKind.Piling)
                continue;
            float ground = BiomeBlendedHeight.GetBlendedHeight(p.Position.x, p.Position.z, world);
            // Same stop as BridgeLayout.EmitColumn: the last segment's centre is at or below ground.
            for (float center = p.Position.y; center > ground;)
            {
                center -= style.PilingSegment;
                grounded.Add(new BridgePiece
                {
                    Kind = p.Kind, Prefab = p.Prefab,
                    Position = new Vector3(p.Position.x, center, p.Position.z),
                    YawDegrees = p.YawDegrees, PitchDegrees = p.PitchDegrees, RollDegrees = p.RollDegrees,
                    HealthFraction = p.HealthFraction,
                });
            }
        }
        return grounded;
    }

    // ---- weather ----

    /// <summary>
    /// The ruin pass for a composed kit, deterministic per (crossing, seed):
    /// the fairway is cleared whole (sailing is sacred, the same keep-clear
    /// BridgeLayout uses); the exposed parts — decks, beams, arches — fall
    /// with a probability peaking at mid-span; pier columns fall later and
    /// only whole (a column is one support); abutments stay; every survivor
    /// is damaged into the ruin health range; and whatever lost its support
    /// falls with it, so the plan stands under the support model whatever
    /// the draws.
    /// </summary>
    public static List<BridgePiece> Weather(List<BridgePiece> pieces, RoadCrossing c, BridgeStyle style, WorldGenerator world, int seed,
        float deckLoss = 0.6f, float postLoss = 0.15f)
    {
        System.Random rng = new(seed);
        float fairwayMid = c.Along(c.FairwayCenter);
        float fairwayHalf = c.FairwayWidth > 0f ? BridgeLayout.FairwayGap(c) * 0.5f + BridgeLayout.FairwayClearance : -1f;
        float MidCloseness(BridgePiece p) => 1f - Mathf.Abs(c.Along(new Vector2(p.Position.x, p.Position.z)) - c.Width * 0.5f) / (c.Width * 0.5f);
        bool InFairway(BridgePiece p) => Mathf.Abs(c.Along(new Vector2(p.Position.x, p.Position.z)) - fairwayMid) <= fairwayHalf;
        (int, int) Column(BridgePiece p) => (Mathf.RoundToInt(p.Position.x * 10f), Mathf.RoundToInt(p.Position.z * 10f));

        // One draw per post column, keyed by its footprint, in plan order.
        Dictionary<(int, int), bool> columnFalls = new();
        foreach (BridgePiece p in pieces)
            if (p.Kind == BridgePieceKind.Piling && !columnFalls.ContainsKey(Column(p)))
                columnFalls[Column(p)] = InFairway(p) || rng.NextDouble() < postLoss * MidCloseness(p);

        List<BridgePiece> kept = new();
        foreach (BridgePiece p in pieces)
        {
            bool falls = p.Kind == BridgePieceKind.Piling ? columnFalls[Column(p)]
                : p.Kind == BridgePieceKind.Abutment ? false
                : InFairway(p) || rng.NextDouble() < deckLoss * MidCloseness(p);
            if (falls)
                continue;
            kept.Add(new BridgePiece
            {
                Kind = p.Kind, Prefab = p.Prefab, Position = p.Position,
                YawDegrees = p.YawDegrees, PitchDegrees = p.PitchDegrees, RollDegrees = p.RollDegrees,
                HealthFraction = RoadConstants.RuinHealthMin + (float)rng.NextDouble() * (RoadConstants.RuinHealthMax - RoadConstants.RuinHealthMin),
            });
        }
        return SupportModel.DropUnsupported(kept, style, world);
    }

    // ---- export ----

    /// <summary>
    /// A solved plan as one blueprint in the crossing's frame: origin at
    /// the near bank at deck height, snap points at both bank contacts,
    /// each piece's kind and health in its data field so the plan survives
    /// a round trip through the file.
    /// </summary>
    public static RoadBlueprint Export(List<BridgePiece> plan, RoadCrossing c, WorldGenerator world, string name, string description = "")
    {
        (float deckFromH, float deckToH) = BridgeLayout.DeckEndHeights(c, world);
        float yaw = BridgeLayout.YawDegrees(c.Direction);
        Vector2 right = Right(c);
        RoadBlueprint bp = new() { Name = name, Description = description };
        bp.SnapPoints.Add(Vector3.zero);
        bp.SnapPoints.Add(new Vector3(0f, deckToH - deckFromH, c.Width));
        foreach (BridgePiece p in plan)
        {
            Vector2 rel = new Vector2(p.Position.x, p.Position.z) - c.FromBank;
            BlueprintPiece piece = new()
            {
                Prefab = p.Prefab,
                LocalPosition = new Vector3(Vector2.Dot(rel, right), p.Position.y - deckFromH, Vector2.Dot(rel, c.Direction)),
                Data = "kind=" + p.Kind + " health=" + p.HealthFraction.ToString("G9", Inv),
            };
            piece.SetEuler(p.PitchDegrees, p.YawDegrees - yaw, p.RollDegrees);
            bp.Pieces.Add(piece);
        }
        return bp;
    }

    /// <summary>A file name valheimCreative's "!creative load" accepts
    /// (letters, digits, '_' and '-', lower case), unique per site.</summary>
    public static string FileNameFor(RoadCrossing c) =>
        "proceduralroads_" + c.Kind.ToString().ToLowerInvariant() + "_" + Mathf.RoundToInt(c.Center.x) + "_" + Mathf.RoundToInt(c.Center.y) + ".blueprint";

    /// <summary>Writes every distinct site's solved plan as a blueprint file
    /// into <paramref name="dir"/> (valheimCreative and Expand World read
    /// BepInEx/config/expand_world/blueprints). Returns how many were written.</summary>
    public static int ExportAll(string dir, IEnumerable<RoadCrossing> crossings, WorldGenerator world, int worldSeed)
    {
        Directory.CreateDirectory(dir);
        int written = 0;
        foreach (RoadCrossing c in BridgeLayout.DistinctSites(crossings))
        {
            List<BridgePiece> plan = BridgePlanner.Plan(c, world, worldSeed);
            if (plan.Count == 0)
                continue;
            string file = FileNameFor(c);
            string description = c.Kind + (c.Style != FordStyle.None ? " " + c.Style : "") + " at (" + Mathf.RoundToInt(c.Center.x) + "," + Mathf.RoundToInt(c.Center.y) + "), "
                + c.Width.ToString("F0", Inv) + " m across, " + c.Biome + ", " + plan.Count + " pieces";
            File.WriteAllText(Path.Combine(dir, file), Export(plan, c, world, Path.GetFileNameWithoutExtension(file), description).Write());
            written++;
        }
        return written;
    }
}
