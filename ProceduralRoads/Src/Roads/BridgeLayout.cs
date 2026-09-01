using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

public enum BridgePieceKind
{
    Piling,        // vertical support segment, stacked down into the riverbed
    Deck,          // walkable span resting on a station pair
    Abutment,      // bank platform, sunk into the road surface
    Debris,        // collapsed piece settled on the riverbed, outside the fairway
    StairStep,     // one staircase step following a stair run's centerline
    StairSupport,  // vertical support under a floating stair step
    Landing,       // flat piece: switchback turn platform or flat chain stretch
    Beam,          // crossbeam tying a station's post pair under the deck
}

/// <summary>One placed piece of a ruined bridge (a persistent ZDO once spawned).</summary>
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
/// Piece kit + ruin tuning for one bridge style. Styles follow player
/// progression: humble wood near spawn, stone and marble further out.
/// Prefab geometry verified in-game via road_snap_probe.
/// </summary>
public sealed class BridgeStyle
{
    public string PilingPrefab = "";
    public string BeamPrefab = "";       // empty: station has no crossbeam
    public string DeckPrefab = "";
    public string AbutmentPrefab = "";
    public string DebrisPrefab = "";

    public float DeckSpan = 2f;          // meters between stations / one deck piece
    public float DeckWidth = 2f;         // deck piece width across the crossing
    public float DeckTopOffset = 0f;     // walking surface height relative to deck origin
    public float PilingSegment = 2f;     // vertical meters per piling piece
    public float DeckFreeboard = 0.5f;   // deck height above water level

    /// <summary>Post pair side offset from the centerline; 0 = one central
    /// pier column per station (stone).</summary>
    public float PostSideOffset = 0f;
    /// <summary>Rotate piling pieces 90° so their long axis spans across the
    /// deck (stone walls); poles are symmetric and don't care.</summary>
    public bool PilingAcross = false;
    public float PostTopBelowDeck = 0.2f; // post tops tuck under the deck
    public float BeamBelowDeck = 0.13f;   // beam center under the deck surface

    public float BankSurvival = 0.85f;   // piece survival probability near banks...
    public float MidSurvival = 0.4f;     // ...falling to this at mid-span
    public float StubChance = 0.5f;      // removed pier leaves a rotted stub
    public float DebrisChance = 0.5f;    // removed piece leaves riverbed debris

    public static readonly BridgeStyle MeadowsWood = new()
    {
        PilingPrefab = "wood_pole2",     // 2m pole, snaps (0,±1,0)
        BeamPrefab = "wood_beam",        // 2m beam, snaps (±1,0,0)
        DeckPrefab = "wood_floor",       // 2x2 plate, walking surface at origin
        AbutmentPrefab = "wood_floor",
        DebrisPrefab = "wood_pole2",
        PostSideOffset = 0.75f,
        PilingSegment = 2f,
    };

    public static readonly BridgeStyle MountainStone = new()
    {
        PilingPrefab = "stone_wall_2x1", // 2m wide, 1m tall, snaps at y ±0.5
        BeamPrefab = "",
        DeckPrefab = "stone_floor_2x2",  // 2x2, 1m thick, top face at +0.5
        AbutmentPrefab = "stone_floor_2x2",
        DebrisPrefab = "stone_wall_1x1",
        PostSideOffset = 0f,             // full-width pier column
        PilingAcross = true,
        PilingSegment = 1f,              // was 2: stone walls stacked with air gaps
        DeckTopOffset = 0.5f,
        BankSurvival = 0.9f,             // stone endures better than wood
        MidSurvival = 0.5f,
    };
}

/// <summary>
/// Deterministic layout solver for ruined bridges at recorded crossings.
/// Pure logic — placement in-game happens later from the returned plan.
///
/// Grammar (support-safe by construction):
///  - the deck line GRADES between the two bank contact heights (clamped
///    above water level), instead of running level at the higher bank —
///    hilly banks no longer hoist the whole bridge onto stilts;
///  - each surviving station is an assembly: a post pair (or full-width
///    stone pier) stacked DOWNWARD from just under the deck until buried in
///    the riverbed, tied by a crossbeam where the kit has one — every
///    column is grounded by construction (WearNTear demolishes floaters);
///  - deck pieces exist only where BOTH end stations survive, and pitch to
///    follow the graded deck line;
///  - the fairway (deepest sailable stretch) never contains piers or
///    debris, and the deck over it is always collapsed — the bridge broke
///    exactly where boats pass;
///  - ruin removal is deterministic per (crossing, seed): survival falls
///    toward mid-span, removed piers may leave waterline stubs, removed
///    pieces may leave tilted debris settled on the bed outside the fairway.
/// </summary>
public static class BridgeLayout
{
    public const float FairwayClearance = 1f;

    public static List<BridgePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed, BridgeStyle style)
    {
        List<BridgePiece> pieces = new();
        if (crossing == null || world == null || style == null || crossing.Width < style.DeckSpan)
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(crossing));

        Vector2 from = crossing.FromBank;
        Vector2 to = crossing.ToBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

        float bankFromH = world.GetHeight(from.x, from.y);
        float bankToH = world.GetHeight(to.x, to.y);
        float minDeck = crossing.WaterLevel + style.DeckFreeboard;

        // Fairway keep-clear interval, projected onto the crossing line.
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - from, dir);
        float fairwayHalf = crossing.FairwayWidth * 0.5f + FairwayClearance;

        // Stations every DeckSpan from bank to bank, deck height graded
        // between the bank contact points and clamped above the water.
        int stationCount = Mathf.CeilToInt(crossing.Width / style.DeckSpan) + 1;
        bool[] pierAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];
        float[] stationDeckH = new float[stationCount];

        for (int i = 0; i < stationCount; i++)
        {
            float along = Mathf.Min(i * style.DeckSpan, crossing.Width);
            stationPos[i] = from + dir * along;
            float t = crossing.Width > 0.01f ? along / crossing.Width : 0f;
            stationDeckH[i] = Mathf.Max(Mathf.Lerp(bankFromH, bankToH, t), minDeck);

            bool inFairway = crossing.FairwayWidth > 0f && Mathf.Abs(along - fairwayMid) <= fairwayHalf;
            bool isBankStation = i == 0 || i == stationCount - 1;

            // Survival falls toward mid-span; the fairway is always cleared.
            float mid = (stationCount - 1) * 0.5f;
            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float survival = Mathf.Lerp(style.BankSurvival, style.MidSurvival, midCloseness);

            bool alive = !inFairway && (isBankStation || NextFloat(rng) < survival);
            pierAlive[i] = alive;

            float ground = world.GetHeight(stationPos[i].x, stationPos[i].y);

            if (alive)
            {
                EmitStation(pieces, style, world, stationPos[i], side, stationDeckH[i], yaw, rng);
            }
            else if (!inFairway && NextFloat(rng) < style.StubChance)
            {
                // Rotted stub: a single buried segment poking out near the waterline.
                EmitColumn(pieces, style, stationPos[i], ground,
                    Mathf.Min(ground + style.PilingSegment, crossing.WaterLevel + 0.3f),
                    yaw, 0.25f + NextFloat(rng) * 0.15f);
            }
            else if (!inFairway && NextFloat(rng) < style.DebrisChance)
            {
                EmitDebris(pieces, style, stationPos[i], dir, world, rng);
            }
        }

        // Deck pieces exist only where both end stations survive; each one
        // pitches to follow the graded deck line.
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!pierAlive[i] || !pierAlive[i + 1])
                continue;

            Vector2 mid2 = (stationPos[i] + stationPos[i + 1]) * 0.5f;
            float hA = stationDeckH[i];
            float hB = stationDeckH[i + 1];
            float pitch = -Mathf.Atan2(hB - hA, style.DeckSpan) * 180f / Mathf.PI;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Deck,
                Prefab = style.DeckPrefab,
                Position = new Vector3(mid2.x, (hA + hB) * 0.5f - style.DeckTopOffset, mid2.y),
                YawDegrees = yaw,
                PitchDegrees = pitch,
                HealthFraction = RuinHealth(rng),
            });
        }

        // Abutments: bank platforms sunk slightly below the road surface so
        // terrain and paint lap onto the wood/stone.
        foreach (Vector2 bank in new[] { from, to })
        {
            float bankGround = world.GetHeight(bank.x, bank.y);
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Abutment,
                Prefab = style.AbutmentPrefab,
                Position = new Vector3(bank.x, bankGround - 0.3f, bank.y),
                YawDegrees = yaw,
                HealthFraction = 0.5f + NextFloat(rng) * 0.4f,
            });
        }

        return pieces;
    }

    /// <summary>One surviving station: post pair (or single full-width pier)
    /// stacked down into the riverbed, plus a crossbeam where the kit has one.</summary>
    private static void EmitStation(List<BridgePiece> pieces, BridgeStyle style,
        WorldGenerator world, Vector2 pos, Vector2 sideDir, float deckH, float yaw, System.Random rng)
    {
        float health = RuinHealth(rng);
        float postTop = deckH - style.PostTopBelowDeck;

        if (style.PostSideOffset > 0.01f)
        {
            foreach (float s in new[] { -style.PostSideOffset, style.PostSideOffset })
            {
                Vector2 postPos = pos + sideDir * s;
                EmitColumn(pieces, style, postPos, world.GetHeight(postPos.x, postPos.y), postTop, yaw, health);
            }
        }
        else
        {
            EmitColumn(pieces, style, pos, world.GetHeight(pos.x, pos.y), postTop, yaw, health);
        }

        if (!string.IsNullOrEmpty(style.BeamPrefab))
        {
            // Beam long axis ties the post pair across the deck.
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Beam,
                Prefab = style.BeamPrefab,
                Position = new Vector3(pos.x, deckH - style.BeamBelowDeck, pos.y),
                YawDegrees = yaw, // beam runs along local x — already across the deck
                HealthFraction = health,
            });
        }
    }

    /// <summary>Segments stacked downward from a required top height until the
    /// bottom is buried below ground — exact top, grounded base.</summary>
    private static void EmitColumn(List<BridgePiece> pieces, BridgeStyle style,
        Vector2 pos, float ground, float topHeight, float yaw, float health)
    {
        if (topHeight <= ground - style.PilingSegment)
            return;

        float half = style.PilingSegment * 0.5f;
        float pieceYaw = style.PilingAcross ? yaw + 90f : yaw;
        for (float top = topHeight; ; top -= style.PilingSegment)
        {
            float center = top - half;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Piling,
                Prefab = style.PilingPrefab,
                Position = new Vector3(pos.x, center, pos.y),
                YawDegrees = pieceYaw,
                HealthFraction = health,
            });
            if (center <= ground)
                break;
        }
    }

    private static void EmitDebris(List<BridgePiece> pieces, BridgeStyle style,
        Vector2 station, Vector2 dir, WorldGenerator world, System.Random rng)
    {
        // Settle a tilted piece into the bed, displaced to the side of the
        // crossing line (never along it toward the fairway).
        Vector2 side = new(-dir.y, dir.x);
        float offset = 1f + NextFloat(rng) * 2f;
        if (NextFloat(rng) < 0.5f) offset = -offset;

        Vector2 pos = station + side * offset;
        float ground = world.GetHeight(pos.x, pos.y);

        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Debris,
            Prefab = style.DebrisPrefab,
            Position = new Vector3(pos.x, ground + 0.2f, pos.y),
            YawDegrees = NextFloat(rng) * 360f,
            PitchDegrees = 50f + NextFloat(rng) * 70f, // toppled, not standing
            RollDegrees = NextFloat(rng) * 30f,
            HealthFraction = 0.2f + NextFloat(rng) * 0.2f,
        });
    }

    private static float RuinHealth(System.Random rng) => 0.3f + NextFloat(rng) * 0.4f;

    private static float NextFloat(System.Random rng) => (float)rng.NextDouble();

    private static int StableSeed(RoadCrossing crossing)
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
