using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Low Mistlands bridges built from the vanilla Dvergr harbour pier's wooden pieces, for lower bridges in the Mistlands. Pure logic; the
/// game places the plan through the bridges' structure source (<see cref="BridgeStructures"/>), so the old-world cutoff,
/// the spawned-zone record and the append queue are the wooden bridges' own. Which crossings get it is
/// <see cref="BridgeStructures.KitFor"/>'s choice, nowhere else.
///
/// <para><b>Only the look changes.</b> The deck height, its length and both ends are the wooden kit's
/// (<see cref="BridgeLayout.DeckHeight"/>, <see cref="BridgeLayout.BuiltLength"/>, <see cref="BridgeLayout.Ends"/>), so the
/// road meets the bridge where it met the wooden one and the network is the same on the same world (ideally at
/// similar locations, so before and after can be compared). The bank stairs are the wooden kit's run, laid in
/// <see cref="StairPrefab"/>.</para>
///
/// <para><b>The template is the vanilla pier</b> (a Dvergr
/// harbour at LocationProxy (2238.7, 30.0, -6218.0), yaw 135), turned onto the pier's own axes:</para>
/// <list type="bullet">
/// <item>bents of two <see cref="Pile"/>s, 5.75 m apart across the deck, every 5 m along it (<see cref="BentSpacing"/>);
/// the piles stack 4 m apart (y 28.5 and 32.5) and their tops stand about a metre over the planks;</item>
/// <item>planks: <see cref="Plank"/> laid ACROSS the deck every metre (<see cref="PlankPitch"/>) at y 31.5, alternating
/// end for end (yaw 0 / 180);</item>
/// <item>stringers: the same beam laid ALONG the deck under the planks (y 31.0), two of them 4 m apart, centres 6 m apart
/// along the pier: the beam is read as 6 m long, 1 m wide, 0.5 m deep;</item>
/// <item>two <c>dvergrprops_lantern</c> on the pier, hung from its crane.</item>
/// </list>
/// <para>Sizes and origins are READ OFF THE LAYOUT (neighbours' spacing), not measured with cli_piece_geometry: the pile
/// is taken as 4 m with its origin at its top (two stacked 4 m apart, the upper's origin a metre over the planks, which is
/// where the screenshot's post tops stand), the beam as 6 x 1 x 0.5 m with its origin at its centre and its length along
/// its local x. Both are to be measured in game before this is trusted.</para>
///
/// <para><b>Ruin, the navigation gap and the free span</b> are the wooden kit's rules (<see cref="BridgeLayout.Ruin"/>,
/// <see cref="BridgeLayout.SpanSurvival"/>, <see cref="BridgeLayout.PierSurvival"/>, <see cref="BridgeLayout.InNavigationGap"/>,
/// <see cref="BridgeLayout.IsFreeSpan"/>): fixed per crossing and world seed, bank bents always stand, a bent in the gap
/// is never built, and a free span has bents only at its banks with its stringers carried bank to bank.</para>
///
/// <para><b>Every piece is grounded or rests on one that is, by construction</b>: a bent's piles go down until the lowest
/// one's foot is in the ground, a stringer lies under planks that rest on a bent's piles (as on the vanilla pier, where the
/// stringers stand inside the piles and hang from the planks), and a plank is laid only over a bay whose two bents stand.</para>
/// </summary>
public static class DvergrWoodBridge
{
    // ---------------------------------------------------------------- pieces
    /// <summary>The vanilla pier's plank and stringer.</summary>
    public const string Plank = "dvergrprops_wood_beam";
    /// <summary>The vanilla pier's pile.</summary>
    public const string Pile = "dvergrprops_wood_pole";
    /// <summary>A lantern standing on the deck beside a pile. The pier's own <c>dvergrprops_lantern</c> hangs from its crane,
    /// and a bridge has no crane: the standing one is its sibling in the game (StockPrefabs), so nothing hangs in the air.</summary>
    public const string Lantern = "dvergrprops_lantern_standing";
    /// <summary>
    /// The bank stairs: the Dvergr wooden stair (not the corner
    /// stairs). Laid on the wooden kit's 2 m run and 1 m rise, so the run meets the road where the plain one did
    /// (<see cref="BridgeLayout.Ends"/>). Its size, origin and snaps are taken to be the wood stair's until measured in game.
    /// </summary>
    public const string StairPrefab = "dvergrprops_wood_stair";

    // -------------------------------------------------- the pier, read off

    /// <summary>Most bent to bent along the deck: the vanilla pier's 5 m.</summary>
    public const float BentSpacing = 5f;
    /// <summary>A pile's centre either side of the crossing line: the pier's two piles stand 5.75 m apart.</summary>
    public const float PileSideOffset = 2.875f;
    /// <summary>A stringer's centre either side of the crossing line: the pier's stand 4 m apart.</summary>
    public const float StringerSideOffset = 2f;
    /// <summary>Plank to plank along the deck.</summary>
    public const float PlankPitch = 1f;
    /// <summary>The beam's length (along its local x), width and depth, read off the layout.</summary>
    public const float BeamLength = 6f, BeamWidth = 1f, BeamDepth = 0.5f;
    /// <summary>One pile's length; its origin is at its top.</summary>
    public const float PileLength = 4f;
    /// <summary>How far a pile's top stands over the walking surface (the pier's: 32.5 against plank tops at 31.75).</summary>
    public const float PileAboveDeck = 0.75f;
    /// <summary>A lantern stands this far in from its pile, on the planks and outside the 4 m road lane.</summary>
    public const float LanternInset = 0.6f;

    // ------------------------------------------------------------ the ruin
    /// <summary>A surviving pile carries a lantern this often, at most <see cref="MaxLanterns"/> to a bridge (the pier has 2).</summary>
    public const float LanternChance = 0.15f;
    public const int MaxLanterns = 2;
    /// <summary>Ruin roles of this kit's own decisions, past <see cref="BridgeLayout.Ruin"/>'s.</summary>
    public const int RolePlank = 11, RolePlankHealth = 12, RoleLantern = 13, RoleStringerHealth = 14;

    /// <summary>The bents along the deck, from the near deck end (0) to the far one (<see cref="BridgeLayout.BuiltLength"/>):
    /// as many equal bays as keep each at most <see cref="BentSpacing"/>.</summary>
    public static float[] BentAlongs(float builtLength)
    {
        int bays = Mathf.Max(1, Mathf.CeilToInt(builtLength / BentSpacing - 1e-3f));
        var a = new float[bays + 1];
        for (int i = 0; i <= bays; i++) a[i] = builtLength * i / bays;
        return a;
    }

    internal static List<StructurePiece> SolveComplete(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Solve(crossing, world, worldSeed, ruin: false);

    public static List<StructurePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Solve(crossing, world, worldSeed, ruin: true);

    private static List<StructurePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed, bool ruin)
    {
        var pieces = new List<StructurePiece>();
        if (crossing == null || world == null || crossing.Kind != CrossingKind.Bridge || crossing.Width < BridgeLayout.DeckSpan)
            return pieces;

        var r = new BridgeLayout.Ruin(worldSeed, crossing);
        Vector2 from = crossing.FromBank, dir = crossing.Direction, side = new(-dir.y, dir.x);
        float yaw = BridgeLayout.YawDegrees(dir);              // a beam's length (local x) across the deck
        float alongYaw = yaw + 90f;                            // ... and along it
        float deckH = BridgeLayout.DeckHeight(crossing, world); // the wooden kit's, so the ends are its own
        float length = BridgeLayout.BuiltLength(crossing.Width);
        bool freeSpan = BridgeLayout.IsFreeSpan(crossing, world);

        float[] bents = BentAlongs(length);
        int n = bents.Length;
        float mid = (n - 1) * 0.5f;
        var alive = new bool[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 at = from + dir * bents[i];
            bool bank = i == 0 || i == n - 1;
            bool inGap = ruin && !freeSpan && BridgeLayout.InNavigationGap(crossing, bents[i]);
            float closeness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float pierSurvival = BridgeLayout.PierSurvival(BridgeLayout.SpanSurvival(closeness));
            alive[i] = !inGap && (freeSpan ? bank : !ruin || bank || r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleStation) < pierSurvival);
            float ground = BiomeBlendedHeight.GetBlendedHeight(at.x, at.y, world);
            if (alive[i])
            {
                foreach (int s in new[] { BridgeLayout.Ruin.Left, BridgeLayout.Ruin.Right })
                {
                    Vector2 p = at + side * (s * PileSideOffset);
                    EmitPiles(pieces, p, BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world), deckH + PileAboveDeck, yaw,
                        r.Health(i, s, BridgeLayout.Ruin.RolePostHealth));
                }
            }
            else if (freeSpan || inGap || !ruin)
            {
                // nothing: a free span never stood in the water, and the gap is kept open on purpose
            }
            else if (r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleStub) < BridgeLayout.StubChance)
            {
                // A rotted pile: one, its top near the waterline, its foot in the bed.
                float top = Mathf.Min(ground + PileLength * 0.5f, crossing.WaterLevel + 0.3f);
                if (top > ground + 0.25f)
                    EmitPiles(pieces, at + side * (r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleStub, 1) < 0.5f ? -PileSideOffset : PileSideOffset),
                        ground, top, yaw, 0.25f + r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleStub, 2) * 0.15f);
            }
            else if (r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleDebris) < BridgeLayout.DebrisChance)
            {
                // A plank fallen onto the bed, beside the line (never along it, toward the fairway).
                float off = 1f + r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleDebris, 1) * 2f;
                if (r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleDebris, 2) < 0.5f) off = -off;
                Vector2 p = at + side * off;
                pieces.Add(new StructurePiece
                {
                    Kind = StructurePieceKind.Debris,
                    Prefab = Plank,
                    Position = new Vector3(p.x, BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world) + BeamDepth * 0.5f, p.y),
                    YawDegrees = r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleDebris, 3) * 360f,
                    PitchDegrees = r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleDebris, 4) * 25f, // lying, a little askew
                    HealthFraction = 0.2f + r.Roll(i, BridgeLayout.Ruin.Centre, BridgeLayout.Ruin.RoleDebris, 6) * 0.2f,
                });
            }
        }

        // A bay is carried when both its bents stand; a free span's one bay is carried from its two banks.
        bool Carried(int bay) => freeSpan || (alive[bay] && alive[bay + 1]);
        int BayOf(float along)
        {
            for (int i = 0; i + 1 < n; i++) if (along <= bents[i + 1] + 1e-3f) return i;
            return n - 2;
        }

        // Stringers: two lines along the deck under the planks, one beam per bay centred on it (the pier's overlap the
        // bents too), or, over a free span, a chain of beams from bank to bank.
        float stringerY = deckH - BeamDepth * 1.5f;
        if (freeSpan)
        {
            int k = Mathf.Max(1, Mathf.CeilToInt(length / BeamLength - 1e-3f));
            for (int j = 0; j < k; j++)
                EmitStringers(pieces, from + dir * (length * (j + 0.5f) / k), side, stringerY, alongYaw, r, j);
        }
        else
        {
            for (int i = 0; i + 1 < n; i++)
                if (Carried(i))
                    EmitStringers(pieces, from + dir * ((bents[i] + bents[i + 1]) * 0.5f), side, stringerY, alongYaw, r, i);
        }

        // Planks: one across the deck every metre, end for end in turn, over carried bays only. The end bays keep every
        // plank (the way on from the stairs); elsewhere a plank goes by the wooden kit's survival at its place on the span.
        int planks = Mathf.RoundToInt(length / PlankPitch);
        var plankAt = new bool[planks];
        for (int j = 0; j < planks; j++)
        {
            float along = (j + 0.5f) * PlankPitch;
            int bay = BayOf(along);
            if (!Carried(bay)) continue;
            bool endBay = bay == 0 || bay == n - 2;
            float closeness = length > 0f ? 1f - Mathf.Abs(along - length * 0.5f) / (length * 0.5f) : 0f;
            if (ruin && !endBay && r.Roll(j, BridgeLayout.Ruin.Centre, RolePlank) >= BridgeLayout.SpanSurvival(closeness)) continue;
            plankAt[j] = true;
            Vector2 p = from + dir * along;
            pieces.Add(new StructurePiece
            {
                Kind = StructurePieceKind.Deck,
                Prefab = Plank,
                Position = new Vector3(p.x, deckH - BeamDepth * 0.5f, p.y),
                YawDegrees = j % 2 == 0 ? yaw : yaw + 180f,
                HealthFraction = r.Health(j, BridgeLayout.Ruin.Centre, RolePlankHealth),
            });
        }

        // Lanterns, now and then, on the planks beside a standing pile: deterministic per crossing, never in the road's lane.
        int lanterns = 0;
        for (int i = 0; i < n && lanterns < MaxLanterns; i++)
        {
            if (!alive[i]) continue;
            int j = Mathf.Clamp(Mathf.FloorToInt(bents[i] / PlankPitch), 0, planks - 1);
            if (!plankAt[j]) continue;
            foreach (int s in new[] { BridgeLayout.Ruin.Left, BridgeLayout.Ruin.Right })
            {
                if (lanterns >= MaxLanterns || r.Roll(i, s, RoleLantern) >= LanternChance) continue;
                Vector2 p = from + dir * ((j + 0.5f) * PlankPitch) + side * (s * (PileSideOffset - LanternInset));
                pieces.Add(new StructurePiece
                {
                    Kind = StructurePieceKind.Post, // stands on the deck like a pile's top; no kind of its own (the enum is saved)
                    Prefab = Lantern,
                    Position = new Vector3(p.x, deckH, p.y),
                    YawDegrees = yaw,
                });
                lanterns++;
            }
        }

        // The bank stairs: the wooden kit's run, laid exactly as the wooden bridge's (its ends are what the road meets).
        Vector2 deckEnd = from + dir * length;
        BridgeLayout.EmitSteps(pieces, world, from, dir, side, deckH, r, 0, StairPrefab);
        BridgeLayout.EmitSteps(pieces, world, deckEnd, -dir, side, deckH, r, BridgeLayout.Bays(crossing.Width) + 1, StairPrefab);
        return pieces;
    }

    /// <summary>A pile column from <paramref name="top"/> down until the lowest pile's foot is in the ground. Each pile is
    /// <see cref="PileLength"/> with its origin at its top; none is laid wholly under the ground.</summary>
    private static void EmitPiles(List<StructurePiece> pieces, Vector2 pos, float ground, float top, float yaw, float health)
    {
        for (float t = top; t > ground; t -= PileLength)
        {
            pieces.Add(new StructurePiece
            {
                Kind = StructurePieceKind.Post,
                Prefab = Pile,
                Position = new Vector3(pos.x, t, pos.y),
                YawDegrees = yaw,
                HealthFraction = health,
            });
            if (t - PileLength <= ground) break;
        }
    }

    private static void EmitStringers(List<StructurePiece> pieces, Vector2 centre, Vector2 side, float y, float alongYaw, BridgeLayout.Ruin r, int key)
    {
        foreach (int s in new[] { BridgeLayout.Ruin.Left, BridgeLayout.Ruin.Right })
        {
            Vector2 p = centre + side * (s * StringerSideOffset);
            pieces.Add(new StructurePiece
            {
                Kind = StructurePieceKind.Beam,
                Prefab = Plank,
                Position = new Vector3(p.x, y, p.y),
                YawDegrees = alongYaw,
                HealthFraction = r.Health(key, s, RoleStringerHealth),
            });
        }
    }

    /// <summary>One line for road_bridge_repairs and road_crossings.</summary>
    public static string Describe(RoadCrossing crossing, WorldGenerator world) =>
        System.FormattableString.Invariant(
            $"deck {BridgeLayout.DeckHeight(crossing, world):F1} m, {BentAlongs(BridgeLayout.BuiltLength(crossing.Width)).Length} bents over {BridgeLayout.BuiltLength(crossing.Width):F0} m{(BridgeLayout.IsFreeSpan(crossing, world) ? ", free span" : "")}");
}
