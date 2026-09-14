using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

public enum BridgePieceKind
{
    Post,    // vertical pole segment, stacked down into the riverbed
    Beam,    // crossbeam tying a station's post pair under the deck
    Deck,    // walkable plank plate resting on a station pair
    Stair,   // a step piece from the road up onto the deck
    Debris,  // a collapsed pole settled on the riverbed, outside the fairway
}

/// <summary>One placed piece of a bridge (a persistent ZDO once spawned).</summary>
public sealed class BridgePiece
{
    public BridgePieceKind Kind;
    public string Prefab = "";
    public Vector3 Position;
    public float YawDegrees;
    public float PitchDegrees;
    public float RollDegrees;
    /// <summary>WearNTear health fraction (drives the vanilla damage visuals).</summary>
    public float HealthFraction = 1f;
}

/// <summary>
/// Deterministic layout of a ruined wooden bridge at a recorded crossing
/// (bridges prototype). Pure logic; the game places the returned plan later.
///
/// The bridge is a row of stations every <see cref="DeckSpan"/> metres from
/// bank to bank. A station is a pair of poles stacked down from just under
/// the deck until buried in the riverbed, tied by a crossbeam; deck plates
/// exist only where both end stations survive and pitch to follow the deck,
/// which grades between the two bank heights and never sits below the
/// water plus a freeboard. Ruin is deterministic per (crossing, world seed):
/// survival falls toward mid-span, piers outlive the deck, a removed
/// station may leave a rotted stub or a toppled pole on the bed, and a
/// navigation gap around the fairway is always left open so boats still
/// pass. Every end is a stair down from the deck edge into the bank. A ford
/// gets no pieces unless it is spanned: a low footbridge, lightly ruined.
/// Every piece is grounded or rests on a grounded one by construction.
/// </summary>
public static class BridgeLayout
{
    // Vanilla wood pieces; geometry as measured in-game.
    public const string PostPrefab = "wood_pole2";   // 2 m pole, origin at its centre
    public const string BeamPrefab = "wood_beam";    // 2 m beam along its local x
    public const string DeckPrefab = "wood_floor";   // 2 x 2 m plate, walking surface at its origin
    public const string StairPrefab = "wood_stair";  // 2 m run, 1 m rise, origin at the foot, rises toward local -z
    public const string DebrisPrefab = "wood_pole2";

    public const float DeckSpan = 2f;         // metres between stations: one deck plate
    public const float PostSegment = 2f;      // vertical metres per pole
    public const float PostSideOffset = 0.75f; // post pair offset from the centreline
    public const float DeckFreeboard = 0.5f;  // deck at least this far above the water
    public const float PostTopBelowDeck = 0.2f;
    public const float BeamBelowDeck = 0.13f;

    // Ruin rule: station survival falls from BankSurvival at the banks to
    // MidSurvival at mid-span; piers outlive the deck by PierPersistence, so
    // a long span reads as a row of piers with a collapsed deck; a removed
    // station leaves a stub or debris by StubChance / DebrisChance.
    public const float BankSurvival = 0.85f;
    public const float MidSurvival = 0.4f;
    public const float PierPersistence = 0.85f;
    public const float StubChance = 0.5f;
    public const float DebrisChance = 0.5f;
    public const float RuinHealthMin = 0.05f;
    public const float RuinHealthMax = 0.70f;

    /// <summary>The navigation gap kept clear of piers and deck: at least
    /// FairwayGapWidth (a longship plus room to line up), growing with the
    /// span, never wider than the fairway itself, plus a clearance each side.</summary>
    public const float FairwayGapWidth = 20f;
    public const float FairwayGapFraction = 0.3f;
    public const float FairwayClearance = 1f;

    /// <summary>A crossing shares a site only when its banks lie within this
    /// distance of the site's: a deck is 2 m wide, so anything further off
    /// would leave a road pointing at water. Crossings that were snapped onto
    /// a site (RoadNetworkGenerator) have identical banks.</summary>
    public const float SameSiteRadius = 0.5f;

    /// <summary>One bridge per site: crossings with the same banks share the
    /// first one's plan.</summary>
    public static List<RoadCrossing> DistinctSites(IEnumerable<RoadCrossing> crossings)
    {
        List<RoadCrossing> sites = new();
        foreach (RoadCrossing c in crossings)
        {
            bool shared = false;
            foreach (RoadCrossing s in sites)
            {
                if (RoadCrossing.SameBanks(s, c, SameSiteRadius))
                {
                    shared = true;
                    break;
                }
            }
            if (!shared)
                sites.Add(c);
        }
        return sites;
    }

    public static float FairwayGap(RoadCrossing crossing) =>
        Mathf.Min(crossing.FairwayWidth, Mathf.Max(FairwayGapWidth, crossing.Width * FairwayGapFraction));

    /// <summary>Deck height where it meets each bank: the bank's ground,
    /// lifted to the water plus freeboard where the ground is lower.</summary>
    public static (float from, float to) DeckEndHeights(RoadCrossing crossing, WorldGenerator world)
    {
        float minDeck = crossing.WaterLevel + DeckFreeboard;
        return (Mathf.Max(BiomeBlendedHeight.GetBlendedHeight(crossing.FromBank.x, crossing.FromBank.y, world), minDeck),
                Mathf.Max(BiomeBlendedHeight.GetBlendedHeight(crossing.ToBank.x, crossing.ToBank.y, world), minDeck));
    }

    /// <summary>
    /// Where the stations stand, measured along the crossing from the near
    /// bank: one every <see cref="DeckSpan"/>, and NOTHING else. The built
    /// length is therefore always a whole number of spans, which is the only
    /// spacing a player can repair.
    ///
    /// Measured from the prefabs rather than assumed (cli_piece_geometry):
    /// wood_floor is 2.000 x 2.000 with snap points at its four corners
    /// (+-1, 0, +-1), and wood_pole2 snaps top to bottom at (0, +-1, 0). Two
    /// plates whose centres are DeckSpan apart therefore share an edge snap
    /// exactly. Put the centres 1.95 m apart instead -- which is what dividing
    /// the width evenly would do -- and the plates still LOOK right, but a
    /// player replacing a missing one snaps it 2.00 m from its neighbour and
    /// the error walks down the bridge.
    ///
    /// The remainder is deliberately NOT spread over the bays, and NOT given
    /// its own stub station either: the far end simply stops at the last whole
    /// span, and <see cref="EmitSteps"/> is anchored THERE rather than at the
    /// bank, so the stair's top snap still meets the deck edge. The stair's run
    /// is DeckSpan, so its first step reaches from the deck end across the
    /// remainder (under 2 m by construction) and lands on the bank.
    ///
    /// Rounding DOWN and never up is what keeps the built bridge inside the
    /// span the pathfinder priced: a route accepted at one length and built at
    /// another is the bug this file has already had once.
    /// </summary>
    internal static float[] StationsAlong(float width)
    {
        int bays = Bays(width);
        float[] alongs = new float[bays + 1];
        for (int i = 0; i <= bays; i++)
            alongs[i] = i * DeckSpan;
        return alongs;
    }

    /// <summary>Whole spans that fit in the width, at least one. The epsilon
    /// keeps a width that IS a whole number of spans from losing one to float
    /// error (80f / 2f can arrive as 39.999998).</summary>
    internal static int Bays(float width) => Mathf.Max(1, Mathf.FloorToInt(width / DeckSpan + 1e-3f));

    /// <summary>The length actually built, always a whole number of spans and
    /// never longer than the crossing the pathfinder accepted.</summary>
    internal static float BuiltLength(float width) => Bays(width) * DeckSpan;

    /// <summary>
    /// The bridge as it would stand with nothing missing: every station, every
    /// deck plate and both stairs, the navigation gap filled in too. Diagnostic
    /// and test-only -- the game always builds the ruined plan -- and the
    /// reference the repairability checks compare against, since "a player
    /// replaces the missing pieces" only means anything if the completed thing
    /// is known.
    ///
    /// The fairway gap is included deliberately. It is not ruin: it is left
    /// open on purpose so boats pass. But it is still a run of bays a player
    /// COULD fill, and if the grid did not continue across it they could not.
    /// </summary>
    internal static List<BridgePiece> SolveComplete(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Solve(crossing, world, worldSeed, ruin: false);

    public static List<BridgePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed) =>
        Solve(crossing, world, worldSeed, ruin: true);

    private static List<BridgePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed, bool ruin)
    {
        List<BridgePiece> pieces = new();
        if (crossing == null || world == null || crossing.Width < DeckSpan)
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(crossing));

        if (crossing.Kind == CrossingKind.Ford)
        {
            // Wading and raised fords are road, not pieces; a span is a short
            // low footbridge with steps.
            if (crossing.Style == FordStyle.Span)
                EmitShallowSpan(pieces, crossing, world, rng, ruin);
            return pieces;
        }

        Vector2 from = crossing.FromBank;
        Vector2 to = crossing.ToBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = YawDegrees(dir);
        float bankFromH = BiomeBlendedHeight.GetBlendedHeight(from.x, from.y, world);
        float bankToH = BiomeBlendedHeight.GetBlendedHeight(to.x, to.y, world);
        (float deckFromH, float deckToH) = DeckEndHeights(crossing, world);
        float minDeck = crossing.WaterLevel + DeckFreeboard;

        // Navigation gap, projected onto the crossing line.
        float fairwayMid = crossing.Along(crossing.FairwayCenter);
        float fairwayHalf = FairwayGap(crossing) * 0.5f + FairwayClearance;

        float[] alongs = StationsAlong(crossing.Width);
        int stationCount = alongs.Length;
        float built = alongs[stationCount - 1];
        // The deck ends here, a remainder short of the far bank at most. Both
        // stairs are anchored at a DECK END so their top snap meets the deck
        // edge; anchoring the far one at the bank instead would leave exactly
        // the gap the trim was meant to remove.
        Vector2 deckEnd = from + dir * built;
        bool[] deckAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];
        float[] stationDeckH = new float[stationCount];
        float mid = (stationCount - 1) * 0.5f;

        for (int i = 0; i < stationCount; i++)
        {
            float along = alongs[i];
            stationPos[i] = from + dir * along;
            float t = crossing.Width > 0.01f ? along / crossing.Width : 0f;
            stationDeckH[i] = Mathf.Max(Mathf.Lerp(deckFromH, deckToH, t), minDeck);

            // The navigation gap is intentional and permanent, not ruin -- but
            // it is still a section a player may fill in, so the complete
            // layout includes it: "fully repaired" means every bay on the grid.
            bool inFairway = ruin && crossing.FairwayWidth > 0f && Mathf.Abs(along - fairwayMid) <= fairwayHalf;
            bool isBankStation = i == 0 || i == stationCount - 1;

            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float survival = Mathf.Lerp(BankSurvival, MidSurvival, midCloseness);
            float pierSurvival = survival + (1f - survival) * PierPersistence;
            bool pierAlive = !inFairway && (!ruin || isBankStation || NextFloat(rng) < pierSurvival);
            deckAlive[i] = pierAlive && (!ruin || isBankStation || NextFloat(rng) < survival);

            float ground = BiomeBlendedHeight.GetBlendedHeight(stationPos[i].x, stationPos[i].y, world);
            if (pierAlive)
            {
                EmitStation(pieces, world, stationPos[i], side, stationDeckH[i], yaw, rng);
            }
            else if (ruin && !inFairway && NextFloat(rng) < StubChance)
            {
                // Rotted stub: a single buried segment poking out near the waterline.
                EmitColumn(pieces, stationPos[i], ground,
                    Mathf.Min(ground + PostSegment, crossing.WaterLevel + 0.3f), yaw, 0.25f + NextFloat(rng) * 0.15f);
            }
            else if (ruin && !inFairway && NextFloat(rng) < DebrisChance)
            {
                EmitDebris(pieces, stationPos[i], dir, world, rng);
            }
        }

        // Deck plates exist only where both end stations carry a deck; each
        // pitches to follow the graded deck line.
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!deckAlive[i] || !deckAlive[i + 1])
                continue;
            EmitDeck(pieces, stationPos[i], stationPos[i + 1], stationDeckH[i], stationDeckH[i + 1], yaw, rng);
        }

        // Every end is a stair down from the deck edge into the bank. Last,
        // so the rest of the plan draws the same random sequence either way.
        EmitSteps(pieces, world, from, dir, bankFromH, stationDeckH[0], rng);
        EmitSteps(pieces, world, deckEnd, -dir, bankToH, stationDeckH[stationCount - 1], rng);

        return pieces;
    }

    /// <summary>
    /// Ford span: a continuous low deck from shore to shore, one station per
    /// DeckSpan with posts to the bed, deck at least FordSpanDeckClearance
    /// above the water and FordSpanDeckRise above the higher bank, lightly
    /// ruined (it is a footbridge, not a monument), a stair at each end.
    /// </summary>
    private static void EmitShallowSpan(List<BridgePiece> pieces, RoadCrossing crossing, WorldGenerator world, System.Random rng, bool ruin)
    {
        Vector2 from = crossing.FromBank;
        Vector2 to = crossing.ToBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = YawDegrees(dir);

        float bankFromH = BiomeBlendedHeight.GetBlendedHeight(from.x, from.y, world);
        float bankToH = BiomeBlendedHeight.GetBlendedHeight(to.x, to.y, world);
        float deckH = Mathf.Max(Mathf.Max(bankFromH, bankToH) + RoadConstants.FordSpanDeckRise,
            crossing.WaterLevel + RoadConstants.FordSpanDeckClearance);

        // The same grid as a bridge, for the same reason: a footbridge a player
        // cannot repair with an ordinary wood_floor is no better than one they
        // cannot walk. This used to keep the clamped final station, so a ford
        // span whose width was not a whole number of spans ended in two
        // stations inside each other exactly as a bridge did.
        float[] alongs = StationsAlong(crossing.Width);
        int stationCount = alongs.Length;
        Vector2 deckEnd = from + dir * alongs[stationCount - 1];
        bool[] alive = new bool[stationCount];
        Vector2[] pos = new Vector2[stationCount];
        for (int i = 0; i < stationCount; i++)
        {
            pos[i] = from + dir * alongs[i];
            bool isEnd = i == 0 || i == stationCount - 1;
            alive[i] = !ruin || isEnd || NextFloat(rng) < BankSurvival;
            if (alive[i])
                EmitStation(pieces, world, pos[i], side, deckH, yaw, rng);
        }
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (alive[i] && alive[i + 1])
                EmitDeck(pieces, pos[i], pos[i + 1], deckH, deckH, yaw, rng);
        }

        EmitSteps(pieces, world, from, dir, bankFromH, deckH, rng);
        EmitSteps(pieces, world, deckEnd, -dir, bankToH, deckH, rng);
    }

    private static void EmitDeck(List<BridgePiece> pieces, Vector2 a, Vector2 b, float hA, float hB, float yaw, System.Random rng)
    {
        Vector2 mid2 = (a + b) * 0.5f;
        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Deck,
            Prefab = DeckPrefab,
            Position = new Vector3(mid2.x, (hA + hB) * 0.5f, mid2.y),
            YawDegrees = yaw,
            PitchDegrees = -Mathf.Atan2(hB - hA, DeckSpan) * 180f / Mathf.PI,
            HealthFraction = RuinHealth(rng),
        });
    }

    /// <summary>Steps down from a deck edge into the bank: one step piece per
    /// metre the deck sits above the road, at least one, marching outward so
    /// the top step meets the deck edge. A flush end's single step is mostly
    /// inside the dirt, which is the point: the road meets the deck through
    /// a stair, never an overlapping plate. A step whose foot is above the
    /// ground gets a post under it (grounded by construction, like a station).</summary>
    private static void EmitSteps(List<BridgePiece> pieces, WorldGenerator world,
        Vector2 bank, Vector2 inward, float bankH, float deckH, System.Random rng)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt((deckH - bankH) / 1f - 0.02f)); // tolerate float noise
        float stepYaw = YawDegrees(inward) + 180f; // the stair prefab rises toward local -z
        float yaw = YawDegrees(inward);
        for (int k = 0; k < steps; k++)
        {
            Vector2 c = bank - inward * (1f + k * 2f);
            float foot = deckH - 1f - k * 1f;
            float health = RuinHealth(rng);
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Stair,
                Prefab = StairPrefab,
                Position = new Vector3(c.x, foot, c.y),
                YawDegrees = stepYaw,
                HealthFraction = health,
            });
            float ground = BiomeBlendedHeight.GetBlendedHeight(c.x, c.y, world);
            if (foot > ground + 0.15f)
                EmitColumn(pieces, c, ground, foot, yaw, health);
        }
    }

    /// <summary>One surviving station: a post pair stacked down into the
    /// riverbed, tied by a crossbeam just under the deck.</summary>
    private static void EmitStation(List<BridgePiece> pieces, WorldGenerator world,
        Vector2 pos, Vector2 side, float deckH, float yaw, System.Random rng)
    {
        float health = RuinHealth(rng);
        float postTop = deckH - PostTopBelowDeck;
        foreach (float s in new[] { -PostSideOffset, PostSideOffset })
        {
            Vector2 postPos = pos + side * s;
            EmitColumn(pieces, postPos, BiomeBlendedHeight.GetBlendedHeight(postPos.x, postPos.y, world), postTop, yaw, health);
        }
        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Beam,
            Prefab = BeamPrefab,
            Position = new Vector3(pos.x, deckH - BeamBelowDeck, pos.y),
            YawDegrees = yaw, // the beam runs along its local x: across the deck
            HealthFraction = health,
        });
    }

    /// <summary>Pole segments stacked downward from the required top until the
    /// lowest one's centre is at or below the ground: exact top, grounded base.</summary>
    private static void EmitColumn(List<BridgePiece> pieces, Vector2 pos, float ground, float topHeight, float yaw, float health)
    {
        if (topHeight <= ground - PostSegment)
            return;
        for (float top = topHeight; ; top -= PostSegment)
        {
            float center = top - PostSegment * 0.5f;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Post,
                Prefab = PostPrefab,
                Position = new Vector3(pos.x, center, pos.y),
                YawDegrees = yaw,
                HealthFraction = health,
            });
            if (center <= ground)
                break;
        }
    }

    /// <summary>A toppled pole settled into the bed, displaced to the side of
    /// the crossing line (never along it toward the fairway).</summary>
    private static void EmitDebris(List<BridgePiece> pieces, Vector2 station, Vector2 dir, WorldGenerator world, System.Random rng)
    {
        Vector2 side = new(-dir.y, dir.x);
        float offset = 1f + NextFloat(rng) * 2f;
        if (NextFloat(rng) < 0.5f) offset = -offset;

        Vector2 pos = station + side * offset;
        float ground = BiomeBlendedHeight.GetBlendedHeight(pos.x, pos.y, world);
        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Debris,
            Prefab = DebrisPrefab,
            Position = new Vector3(pos.x, ground + 0.2f, pos.y),
            YawDegrees = NextFloat(rng) * 360f,
            PitchDegrees = 50f + NextFloat(rng) * 70f, // toppled, not standing
            RollDegrees = NextFloat(rng) * 30f,
            HealthFraction = 0.2f + NextFloat(rng) * 0.2f,
        });
    }

    /// <summary>Unity yaw (degrees about +y) that turns local +z onto a world XZ direction.</summary>
    public static float YawDegrees(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

    private static float RuinHealth(System.Random rng) => RuinHealthMin + NextFloat(rng) * (RuinHealthMax - RuinHealthMin);

    private static float NextFloat(System.Random rng) => (float)rng.NextDouble();

    /// <summary>Per-site seed from the crossing's centre, so a world regenerates with the same ruins.</summary>
    public static int StableSeed(RoadCrossing crossing)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt(crossing.Center.x * 10f);
            h = h * 31 + Mathf.RoundToInt(crossing.Center.y * 10f);
            return h;
        }
    }
}
