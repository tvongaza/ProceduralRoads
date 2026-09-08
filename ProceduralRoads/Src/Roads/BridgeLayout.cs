using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

public enum BridgePieceKind
{
    Post,   // vertical pole segment, stacked down into the riverbed
    Beam,   // crossbeam tying a station's post pair under the deck
    Deck,   // walkable plank plate resting on a station pair
}

/// <summary>One placed piece of a bridge (a persistent ZDO once spawned).</summary>
public sealed class BridgePiece
{
    public BridgePieceKind Kind;
    public string Prefab = "";
    public Vector3 Position;
    public float YawDegrees;
    public float PitchDegrees;
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
/// survival falls toward mid-span, piers outlive the deck, and a navigation
/// gap around the fairway is always left open so boats still pass. Every
/// piece is grounded or rests on a grounded one by construction.
/// </summary>
public static class BridgeLayout
{
    // Vanilla wood pieces; geometry as measured in-game.
    public const string PostPrefab = "wood_pole2"; // 2 m pole, origin at its centre
    public const string BeamPrefab = "wood_beam";  // 2 m beam along its local x
    public const string DeckPrefab = "wood_floor"; // 2 x 2 m plate, walking surface at its origin

    public const float DeckSpan = 2f;         // metres between stations: one deck plate
    public const float PostSegment = 2f;      // vertical metres per pole
    public const float PostSideOffset = 0.75f; // post pair offset from the centreline
    public const float DeckFreeboard = 0.5f;  // deck at least this far above the water
    public const float PostTopBelowDeck = 0.2f;
    public const float BeamBelowDeck = 0.13f;

    // Ruin rule: station survival falls from BankSurvival at the banks to
    // MidSurvival at mid-span; piers outlive the deck by PierPersistence, so
    // a long span reads as a row of piers with a collapsed deck.
    public const float BankSurvival = 0.85f;
    public const float MidSurvival = 0.4f;
    public const float PierPersistence = 0.85f;
    public const float RuinHealthMin = 0.05f;
    public const float RuinHealthMax = 0.70f;

    /// <summary>The navigation gap kept clear of piers and deck: at least
    /// FairwayGapWidth (a longship plus room to line up), growing with the
    /// span, never wider than the fairway itself, plus a clearance each side.</summary>
    public const float FairwayGapWidth = 20f;
    public const float FairwayGapFraction = 0.3f;
    public const float FairwayClearance = 1f;

    /// <summary>Two routes crossing the same water each record a crossing;
    /// crossings whose centres lie within this radius of an earlier one share
    /// its bridge.</summary>
    public const float SharedSiteRadius = 6f;

    public static List<RoadCrossing> DistinctSites(IEnumerable<RoadCrossing> crossings)
    {
        List<RoadCrossing> sites = new();
        foreach (RoadCrossing c in crossings)
        {
            bool shared = false;
            foreach (RoadCrossing s in sites)
            {
                if ((s.Center - c.Center).sqrMagnitude <= SharedSiteRadius * SharedSiteRadius)
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

    public static List<BridgePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed)
    {
        List<BridgePiece> pieces = new();
        if (crossing == null || world == null || crossing.Width < DeckSpan)
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(crossing));

        Vector2 from = crossing.FromBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = YawDegrees(dir);
        (float deckFromH, float deckToH) = DeckEndHeights(crossing, world);
        float minDeck = crossing.WaterLevel + DeckFreeboard;

        // Navigation gap, projected onto the crossing line.
        float fairwayMid = crossing.Along(crossing.FairwayCenter);
        float fairwayHalf = FairwayGap(crossing) * 0.5f + FairwayClearance;

        int stationCount = Mathf.CeilToInt(crossing.Width / DeckSpan) + 1;
        bool[] deckAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];
        float[] stationDeckH = new float[stationCount];
        float mid = (stationCount - 1) * 0.5f;

        for (int i = 0; i < stationCount; i++)
        {
            float along = Mathf.Min(i * DeckSpan, crossing.Width);
            stationPos[i] = from + dir * along;
            float t = crossing.Width > 0.01f ? along / crossing.Width : 0f;
            stationDeckH[i] = Mathf.Max(Mathf.Lerp(deckFromH, deckToH, t), minDeck);

            bool inFairway = crossing.FairwayWidth > 0f && Mathf.Abs(along - fairwayMid) <= fairwayHalf;
            bool isBankStation = i == 0 || i == stationCount - 1;

            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float survival = Mathf.Lerp(BankSurvival, MidSurvival, midCloseness);
            float pierSurvival = survival + (1f - survival) * PierPersistence;
            bool pierAlive = !inFairway && (isBankStation || NextFloat(rng) < pierSurvival);
            deckAlive[i] = pierAlive && (isBankStation || NextFloat(rng) < survival);

            if (pierAlive)
                EmitStation(pieces, world, stationPos[i], side, stationDeckH[i], yaw, rng);
        }

        // Deck plates exist only where both end stations carry a deck; each
        // pitches to follow the graded deck line.
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!deckAlive[i] || !deckAlive[i + 1])
                continue;
            Vector2 mid2 = (stationPos[i] + stationPos[i + 1]) * 0.5f;
            float hA = stationDeckH[i], hB = stationDeckH[i + 1];
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

        return pieces;
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
