using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

public enum BridgePieceKind
{
    Piling,        // vertical support segment, stacked from the riverbed
    Deck,          // walkable span resting on two pilings
    Abutment,      // bank platform, sunk into the road surface
    Debris,        // collapsed piece settled on the riverbed, outside the fairway
    StairStep,     // one staircase step following a stair run's centerline
    StairSupport,  // vertical support under a floating stair step
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
/// Prefab names are verified against game data before placement ships.
/// </summary>
public sealed class BridgeStyle
{
    public string PilingPrefab = "";
    public string DeckPrefab = "";
    public string AbutmentPrefab = "";
    public string DebrisPrefab = "";

    public float DeckSpan = 2f;          // meters between pilings / one deck piece
    public float PilingSegment = 2f;     // vertical meters per piling piece
    public float DeckFreeboard = 0.5f;   // deck height above water level
    public float BankSurvival = 0.85f;   // piece survival probability near banks...
    public float MidSurvival = 0.4f;     // ...falling to this at mid-span
    public float StubChance = 0.5f;      // removed pier leaves a rotted stub
    public float DebrisChance = 0.5f;    // removed piece leaves riverbed debris

    public static readonly BridgeStyle MeadowsWood = new()
    {
        PilingPrefab = "wood_pole2",
        DeckPrefab = "wood_floor",
        AbutmentPrefab = "wood_floor",
        DebrisPrefab = "wood_pole2",
    };

    public static readonly BridgeStyle MountainStone = new()
    {
        PilingPrefab = "stone_wall_1x1",
        DeckPrefab = "stone_floor_2x2",
        AbutmentPrefab = "stone_floor_2x2",
        DebrisPrefab = "stone_wall_1x1",
        BankSurvival = 0.9f,   // stone endures better than wood
        MidSurvival = 0.5f,
    };
}

/// <summary>
/// Deterministic layout solver for ruined bridges at recorded crossings.
/// Pure logic — placement in-game happens later from the returned plan.
///
/// Grammar (support-safe by construction):
///  - piers are piling columns stacked FROM THE RIVERBED, so every piece
///    traces support to ground (WearNTear demolishes floating pieces);
///  - deck pieces exist only where BOTH end piers survive;
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
        float yaw = Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

        float deckHeight = Mathf.Max(
            world.GetHeight(from.x, from.y),
            world.GetHeight(to.x, to.y),
            crossing.WaterLevel + style.DeckFreeboard);

        // Fairway keep-clear interval, projected onto the crossing line.
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - from, dir);
        float fairwayHalf = crossing.FairwayWidth * 0.5f + FairwayClearance;

        // Pier stations every DeckSpan from bank to bank.
        int stationCount = Mathf.CeilToInt(crossing.Width / style.DeckSpan) + 1;
        bool[] pierAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];

        for (int i = 0; i < stationCount; i++)
        {
            float along = Mathf.Min(i * style.DeckSpan, crossing.Width);
            stationPos[i] = from + dir * along;

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
                EmitPilingColumn(pieces, style, stationPos[i], ground, deckHeight, yaw, rng, full: true);
            }
            else if (!inFairway && NextFloat(rng) < style.StubChance)
            {
                // Rotted stub: bottom segment(s) only, poking out near the waterline.
                EmitPilingColumn(pieces, style, stationPos[i], ground,
                    Mathf.Min(ground + style.PilingSegment, crossing.WaterLevel + 0.3f), yaw, rng, full: false);
            }
            else if (!inFairway && NextFloat(rng) < style.DebrisChance)
            {
                EmitDebris(pieces, style, stationPos[i], dir, world, rng);
            }
        }

        // Deck pieces exist only where both end piers survive.
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!pierAlive[i] || !pierAlive[i + 1])
                continue;

            Vector2 mid2 = (stationPos[i] + stationPos[i + 1]) * 0.5f;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Deck,
                Prefab = style.DeckPrefab,
                Position = new Vector3(mid2.x, deckHeight, mid2.y),
                YawDegrees = yaw,
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

    private static void EmitPilingColumn(List<BridgePiece> pieces, BridgeStyle style,
        Vector2 pos, float ground, float topHeight, float yaw, System.Random rng, bool full)
    {
        float health = full ? RuinHealth(rng) : 0.25f + NextFloat(rng) * 0.15f;
        for (float h = ground; h < topHeight - 0.01f; h += style.PilingSegment)
        {
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Piling,
                Prefab = style.PilingPrefab,
                Position = new Vector3(pos.x, h, pos.y),
                YawDegrees = yaw,
                HealthFraction = health,
            });
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
