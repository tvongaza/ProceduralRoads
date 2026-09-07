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
    Arch,          // quarter-arch springing from a bank abutment over the water
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
public sealed record BridgeStyle
{
    /// <summary>Short kit name, for validation output.</summary>
    public string Name = "";

    public string PilingPrefab = "";
    public string BeamPrefab = "";       // empty: station has no crossbeam
    public string DeckPrefab = "";
    public string AbutmentPrefab = "";
    public string DebrisPrefab = "";
    public string ArchPrefab = "";       // empty: no abutment arches
    public string StairPrefab = "";      // approach steps up onto the deck

    // Stair geometry, as verified for the stair chains: one piece runs 2m
    // and rises 1m.
    public float StairRun = 2f;
    public float StairRise = 1f;
    /// <summary>Deck-above-bank gap that the approach absorbs with an apron
    /// slab; anything taller gets steps.</summary>
    public float ApproachStepThreshold = 0.6f;
    /// <summary>How far bank pieces sink below grade. Burying is deliberate —
    /// the road paint should lap onto the stonework, not stop at a lip.</summary>
    public float AbutmentEmbed = 0.3f;

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
    public float ArchTopBelowGrade = 0.1f; // arch flat top just under the bank surface

    /// <summary>Segmental camber: the deck line bows above the straight graded
    /// line, peaking at mid-span. Rise is CamberRatio of the span, capped at
    /// CamberMax. 0 = a flat deck (wood). Both ends stay exactly at bank
    /// height, so the road still meets the abutment at grade.</summary>
    public float CamberRatio = 0f;
    public float CamberMax = 0f;

    public float BankSurvival = 0.85f;   // piece survival probability near banks...
    public float MidSurvival = 0.4f;     // ...falling to this at mid-span
    public float StubChance = 0.5f;      // removed pier leaves a rotted stub
    public float DebrisChance = 0.5f;    // removed piece leaves riverbed debris

    /// <summary>How far one bridge's decay may drift from the kit's base
    /// figures. Every crossing weathers on its own schedule, so neighbouring
    /// bridges of the same kit are not ruined in the same pattern.</summary>
    public float WeatheringSpread = 0.15f;

    public static readonly BridgeStyle MeadowsWood = new()
    {
        Name = "wood",
        PilingPrefab = "wood_pole2",     // 2m pole, snaps (0,±1,0)
        BeamPrefab = "wood_beam",        // 2m beam, snaps (±1,0,0)
        DeckPrefab = "wood_floor",       // 2x2 plate, walking surface at origin
        AbutmentPrefab = "wood_floor",
        DebrisPrefab = "wood_pole2",
        StairPrefab = "wood_stair",
        PostSideOffset = 0.75f,
        PilingSegment = 2f,
    };

    public static readonly BridgeStyle MountainStone = new()
    {
        Name = "stone",
        PilingPrefab = "stone_wall_2x1", // 2m wide, 1m tall, snaps at y ±0.5
        BeamPrefab = "",
        DeckPrefab = "stone_floor_2x2",  // 2x2, 1m thick, top face at +0.5
        AbutmentPrefab = "stone_floor_2x2",
        DebrisPrefab = "stone_wall_1x1",
        ArchPrefab = "stone_arch",       // 2m quarter-arch: full 1m face at +x,
                                         // tapering to a top edge at -x, flat top
        StairPrefab = "stone_stair",
        PostSideOffset = 0f,             // full-width pier column
        PilingAcross = true,
        PilingSegment = 1f,              // was 2: stone walls stacked with air gaps
        DeckTopOffset = 0.5f,
        CamberRatio = 0.06f,             // 12m gap -> ~0.7m rise, matching the
        CamberMax = 1.2f,                // shallow segmental profile of the
                                         // community stone bridges
        BankSurvival = 0.9f,             // stone endures better than wood
        MidSurvival = 0.5f,
    };

    /// <summary>Stone substructure carrying a timber deck — the middle rung of
    /// the progression, and the way the community stone bridges are actually
    /// built: heavy stonework low over the water, wood for the span itself.
    /// The deck cambers less than an all-stone arch; timber spans flatter.</summary>
    public static readonly BridgeStyle StoneAndTimber = new()
    {
        Name = "hybrid",
        PilingPrefab = "stone_wall_2x1",
        BeamPrefab = "wood_beam",
        DeckPrefab = "wood_floor",       // walking surface at the origin
        AbutmentPrefab = "stone_floor_2x2",
        DebrisPrefab = "stone_wall_1x1",
        ArchPrefab = "stone_arch",
        StairPrefab = "wood_stair",      // timber deck, timber ramp onto it
        PostSideOffset = 0f,             // full-width stone pier under the deck
        PilingAcross = true,
        PilingSegment = 1f,
        DeckTopOffset = 0f,
        CamberRatio = 0.035f,
        CamberMax = 0.7f,
        BankSurvival = 0.85f,            // stone base outlasts the timber deck
        MidSurvival = 0.4f,
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
///  - stone kits then CAMBER that line: a shallow segmental bow peaking at
///    mid-span and vanishing at both banks, so the arch is read in the deck
///    profile itself (as the community stone bridges build it) rather than
///    perched on the abutments. Piers shorten toward the crown to match, and
///    the road still meets each abutment at grade;
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

    /// <summary>Steepest mean deck grade worth bridging. Past this the deck
    /// is a ramp between mismatched shores rather than a bridge, and the
    /// stonework needed to hold the low end turns into a wall.</summary>
    public const float MaxBankGrade = 0.20f;

    /// <summary>And an absolute cap, for spans wide enough to hide a large
    /// drop inside an acceptable grade.
    ///
    /// Both figures are calibrated against measured crossings rather than
    /// guessed: across RoadTestPC1's nine crossings the shore drops were
    /// 0.9, 2.0, 3.0, 3.8, 5.4, 5.9, 8.3, 9.1 and 9.9m, with a clean gap
    /// between the sixth and seventh. The three above the gap are the ones
    /// that read as a ramp from a beach to a clifftop; `road_spots` prints
    /// drop, grade and bridged for every crossing, so this stays tunable
    /// against evidence.</summary>
    public const float MaxBankDrop = 6f;

    /// <summary>
    /// Whether these two shores can carry a bridge at all. Where they cannot,
    /// no bridge is planned: the road runs to the water's edge, breaks, and
    /// picks up on the far bank — a crossing people gave up on, which reads
    /// better than a ramp bridging a cliff to a beach.
    /// </summary>
    public static bool CanBridge(RoadCrossing crossing, WorldGenerator world)
    {
        if (crossing == null || world == null || crossing.Width <= 0.01f)
            return false;

        float drop = Mathf.Abs(world.GetHeight(crossing.FromBank.x, crossing.FromBank.y)
            - world.GetHeight(crossing.ToBank.x, crossing.ToBank.y));

        return drop <= MaxBankDrop && drop / crossing.Width <= MaxBankGrade;
    }

    public static List<BridgePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed, BridgeStyle style)
    {
        List<BridgePiece> pieces = new();
        if (crossing == null || world == null || style == null || crossing.Width < style.DeckSpan)
            return pieces;
        if (!CanBridge(crossing, world))
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(crossing));

        // Each bridge decays on its own schedule: the kit sets the baseline,
        // this crossing shifts it. Drawn before any placement draw so the
        // offsets stay put however many pieces the layout goes on to emit.
        float spread = style.WeatheringSpread;
        float bankSurvival = Mathf.Clamp01(style.BankSurvival + Jitter(rng, spread));
        float midSurvival = Mathf.Clamp01(style.MidSurvival + Jitter(rng, spread));
        float stubChance = Mathf.Clamp01(style.StubChance + Jitter(rng, spread));
        float debrisChance = Mathf.Clamp01(style.DebrisChance + Jitter(rng, spread));
        // Survival must still fall toward mid-span, whatever the draw did.
        midSurvival = Mathf.Min(midSurvival, bankSurvival);

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
        // between the bank contact points, clamped above the water, then
        // cambered into a shallow segmental bow (stone kits).
        float camberRise = Mathf.Min(crossing.Width * style.CamberRatio, style.CamberMax);
        int stationCount = Mathf.CeilToInt(crossing.Width / style.DeckSpan) + 1;
        bool[] pierAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];
        float[] stationDeckH = new float[stationCount];

        for (int i = 0; i < stationCount; i++)
        {
            float along = Mathf.Min(i * style.DeckSpan, crossing.Width);
            stationPos[i] = from + dir * along;
            float t = crossing.Width > 0.01f ? along / crossing.Width : 0f;
            // sin(pi t) is exactly 0 at both banks: the camber never lifts the
            // deck off the road, it only bows the middle up out of the water.
            stationDeckH[i] = Mathf.Max(Mathf.Lerp(bankFromH, bankToH, t), minDeck)
                + camberRise * Mathf.Sin(t * Mathf.PI);

            bool inFairway = crossing.FairwayWidth > 0f && Mathf.Abs(along - fairwayMid) <= fairwayHalf;
            bool isBankStation = i == 0 || i == stationCount - 1;

            // Survival falls toward mid-span; the fairway is always cleared.
            float mid = (stationCount - 1) * 0.5f;
            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float survival = Mathf.Lerp(bankSurvival, midSurvival, midCloseness);

            bool alive = !inFairway && (isBankStation || NextFloat(rng) < survival);
            pierAlive[i] = alive;

            float ground = world.GetHeight(stationPos[i].x, stationPos[i].y);

            if (alive)
            {
                EmitStation(pieces, style, world, stationPos[i], side, stationDeckH[i], yaw, rng);
            }
            else if (!inFairway && NextFloat(rng) < stubChance)
            {
                // Rotted stub: a single buried segment poking out near the waterline.
                EmitColumn(pieces, style, stationPos[i], ground,
                    Mathf.Min(ground + style.PilingSegment, crossing.WaterLevel + 0.3f),
                    yaw, 0.25f + NextFloat(rng) * 0.15f);
            }
            else if (!inFairway && NextFloat(rng) < debrisChance)
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
        // terrain and paint lap onto the wood/stone. Stone kits also spring a
        // quarter-arch from each bank out over the water — the surviving
        // half of a broken arch bridge.
        for (int end = 0; end < 2; end++)
        {
            Vector2 bank = end == 0 ? from : to;
            Vector2 inward = end == 0 ? dir : -dir;
            float deckAtBank = end == 0 ? stationDeckH[0] : stationDeckH[stationCount - 1];
            float bankGround = world.GetHeight(bank.x, bank.y);
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Abutment,
                Prefab = style.AbutmentPrefab,
                Position = new Vector3(bank.x, bankGround - style.AbutmentEmbed, bank.y),
                YawDegrees = yaw,
                HealthFraction = 0.5f + NextFloat(rng) * 0.4f,
            });

            EmitApproach(pieces, style, world, bank, inward, bankGround, deckAtBank, rng);

            if (string.IsNullOrEmpty(style.ArchPrefab))
                continue;

            // Springing only makes sense off a bank that stands clear of the
            // water; a near-ford bank would put the arch in the mud.
            bool tallEnough = bankGround > crossing.WaterLevel + 0.8f;
            bool survives = NextFloat(rng) < bankSurvival; // draw always, for rng stability
            if (tallEnough && survives)
                EmitArch(pieces, style, bank, inward, bankGround, rng);
        }

        return pieces;
    }

    /// <summary>
    /// Ties the deck edge to the road it serves. Where the deck sits about at
    /// bank height, an apron slab laps outward under the road paint; where it
    /// stands higher — a low bank, or the water clamp lifting the deck — the
    /// gap is climbed with the kit's stairs so the road runs onto the bridge
    /// instead of stopping at a lip.
    ///
    /// Approach pieces bury freely: the bottom step is cut into the bank on
    /// purpose, because a grounded piece survives and a flush one leaves a
    /// seam. Steps follow the stair chains' convention — yaw from the
    /// climbing direction, origin at the piece's low end, centre half a run
    /// along it.
    /// </summary>
    private static void EmitApproach(List<BridgePiece> pieces, BridgeStyle style,
        WorldGenerator world, Vector2 bank, Vector2 inward, float bankGround, float deckAtBank,
        System.Random rng)
    {
        Vector2 outward = -inward;
        float gap = deckAtBank - bankGround;

        if (gap <= style.ApproachStepThreshold || string.IsNullOrEmpty(style.StairPrefab))
        {
            // Flush enough to walk onto: lap one slab out under the paint.
            Vector2 apron = bank + outward * style.StairRun;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Abutment,
                Prefab = style.AbutmentPrefab,
                Position = new Vector3(apron.x,
                    world.GetHeight(apron.x, apron.y) - style.AbutmentEmbed, apron.y),
                YawDegrees = Mathf.Atan2(inward.x, inward.y) * 180f / Mathf.PI,
                HealthFraction = 0.5f + NextFloat(rng) * 0.4f,
            });
            return;
        }

        int steps = Mathf.CeilToInt(gap / style.StairRise);
        float stairYaw = Mathf.Atan2(inward.x, inward.y) * 180f / Mathf.PI;

        for (int k = 0; k < steps; k++)
        {
            int below = steps - k;                      // rises still to climb
            Vector2 lowEnd = bank + outward * (below * style.StairRun);
            Vector2 center = lowEnd + inward * (style.StairRun * 0.5f);
            float baseY = deckAtBank - below * style.StairRise;
            float health = RuinHealth(rng);

            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.StairStep,
                Prefab = style.StairPrefab,
                Position = new Vector3(center.x, baseY, center.y),
                YawDegrees = stairYaw,
                HealthFraction = health,
            });

            // Steps nearer the deck stand clear of the falling bank; carry
            // them on a buried column like every other piece in the grammar.
            // EmitColumn no-ops where the step is already in the ground.
            EmitColumn(pieces, style, center, world.GetHeight(center.x, center.y), baseY, stairYaw, health);
        }
    }

    /// <summary>One quarter-arch springing from the bank: the full-height
    /// face (local +x) seats into the bank at the abutment, the tapered top
    /// edge reaches inward over the water. The tall face is embedded below
    /// grade so the piece is grounded (stone has little horizontal support).</summary>
    private static void EmitArch(List<BridgePiece> pieces, BridgeStyle style,
        Vector2 bank, Vector2 inward, float bankGround, System.Random rng)
    {
        // Yaw mapping local +x onto -inward (tall face toward the bank):
        // R(yaw)*(1,0,0) = (cos yaw, 0, -sin yaw)  =>  cos = t.x, sin = -t.y.
        Vector2 t = -inward;
        float archYaw = Mathf.Atan2(-t.y, t.x) * 180f / Mathf.PI;

        // Center sits one half-length inward of the bank contact point; the
        // flat top lands ArchTopBelowGrade under the bank surface, so the
        // tall face is buried into the bank (grounded) and the curve emerges
        // from the slope as the ground falls away toward the water.
        Vector2 center = bank + inward * 1f;
        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Arch,
            Prefab = style.ArchPrefab,
            Position = new Vector3(center.x, bankGround - style.ArchTopBelowGrade - 0.5f, center.y),
            YawDegrees = archYaw,
            HealthFraction = RuinHealth(rng),
        });
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

    /// <summary>Symmetric offset in [-spread, +spread].</summary>
    private static float Jitter(System.Random rng, float spread) => (NextFloat(rng) * 2f - 1f) * spread;

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
