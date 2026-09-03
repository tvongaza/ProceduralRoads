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
    Stair,         // a step piece from the road up onto a raised ford span
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
    public string ArchPrefab = "";       // empty: no abutment arches
    public string StairPrefab = "";      // step piece for ford spans (2 m run, 1 m rise)

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

    public float BankSurvival = 0.85f;   // piece survival probability near banks...
    public float MidSurvival = 0.4f;     // ...falling to this at mid-span
    // Decision knob (2026-09-02): 0 = every station is one coin flip (piers and
    // deck live or die together, long spans read as jetties); >0 = piers
    // outlive the deck — pier survival is lifted toward 1 by this fraction
    // while the deck keeps the original curve, so piers march across and
    // the deck is what collapses.
    public float PierPersistence = 0f;

    public BridgeStyle WithPierPersistence(float value)
    {
        BridgeStyle copy = (BridgeStyle)MemberwiseClone();
        copy.PierPersistence = value;
        return copy;
    }
    public float StubChance = 0.5f;      // removed pier leaves a rotted stub
    public float DebrisChance = 0.5f;    // removed piece leaves riverbed debris

    public static readonly BridgeStyle MeadowsWood = new()
    {
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
        PilingPrefab = "stone_wall_2x1", // 2m wide, 1m tall, snaps at y ±0.5
        BeamPrefab = "",
        DeckPrefab = "stone_floor_2x2",  // 2x2, 1m thick, top face at +0.5
        AbutmentPrefab = "stone_floor_2x2",
        DebrisPrefab = "stone_wall_1x1",
        StairPrefab = "stone_stair",
        ArchPrefab = "stone_arch",       // 2m quarter-arch: full 1m face at +x,
                                         // tapering to a top edge at -x, flat top
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

    /// <summary>Minimum keep-clear width over the fairway: the collapsed
    /// section a longship sails through (beam ~6 m, plus room to line up).</summary>
    public const float FairwayGapWidth = 20f;

    /// <summary>The collapsed middle grows with the span (Tys, 2 Sep 2026:
    /// a 171 m deck with a 20 m hole reads wrong): this fraction of the
    /// crossing width, never less than FairwayGapWidth, never more than
    /// the fairway itself.</summary>
    public const float FairwayGapFraction = 0.3f;

    /// <summary>Two routes crossing the same water (RoadTestMac2 c1/c2:
    /// Eikthyrnir-GDKing and GDKing-Bonemass, opposite directions) each
    /// record a crossing, and each record solved to the same bridge on the
    /// same spot. One bridge per site: crossings whose centers lie within
    /// SharedSiteRadius of an earlier one share its plan. Every route keeps
    /// its own record (the validator exempts wet points per route).</summary>
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

    /// <summary>Player-facing lever (config "Bridges/PierPersistence", 0..1):
    /// how much the piers outlive the deck. Applied to every kit by StyleFor;
    /// set at config read like the pathfinder's levers.</summary>
    public static float ConfiguredPierPersistence = RoadConstants.DefaultPierPersistence;

    /// <summary>The kit for a crossing's biome with the configured ruin rule
    /// applied. Progression-aligned; Mistlands gets black marble later. The
    /// kit templates keep PierPersistence 0, so a lever of 0 reproduces the
    /// plans from before the lever existed.</summary>
    public static BridgeStyle StyleFor(Heightmap.Biome biome)
    {
        BridgeStyle kit = biome switch
        {
            Heightmap.Biome.Mountain or Heightmap.Biome.Plains or Heightmap.Biome.Mistlands
                => BridgeStyle.MountainStone,
            _ => BridgeStyle.MeadowsWood,
        };
        return kit.WithPierPersistence(Mathf.Clamp01(ConfiguredPierPersistence));
    }

    public static List<BridgePiece> Solve(RoadCrossing crossing, WorldGenerator world, int worldSeed, BridgeStyle style)
    {
        List<BridgePiece> pieces = new();
        if (crossing == null || world == null || style == null || crossing.Width < style.DeckSpan)
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(crossing));

        // Fords: wading and raised fords are road, not pieces; a span is a
        // short low bridge with steps.
        if (crossing.Kind == CrossingKind.Ford)
        {
            if (crossing.Style == FordStyle.Span)
                EmitShallowSpan(pieces, crossing, world, style, rng);
            return pieces;
        }

        Vector2 from = crossing.FromBank;
        Vector2 to = crossing.ToBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

        float bankFromH = BiomeBlendedHeight.GetBlendedHeight(from.x, from.y, world);
        float bankToH = BiomeBlendedHeight.GetBlendedHeight(to.x, to.y, world);
        float minDeck = crossing.WaterLevel + style.DeckFreeboard;

        // Stepped ends: per site (by hash, so unstepped sites keep their
        // plans byte for byte) the deck sits SteppedEndRise above the road
        // at both ends, with steps up to it; otherwise it meets the road flush.
        float endRise = SteppedEndRise(crossing);
        float deckFromH = bankFromH + endRise;
        float deckToH = bankToH + endRise;

        // Fairway keep-clear interval, projected onto the crossing line.
        float fairwayMid = Vector2.Dot(crossing.FairwayCenter - from, dir);
        // SAILING IS SACRED, but a ship needs one gap, not the whole deep
        // bed: on a wide flat channel the keep-clear is a navigation gap
        // around the fairway center, so the ruin still reads as a bridge.
        float fairwayHalf = FairwayGap(crossing) * 0.5f + FairwayClearance;

        // Stations every DeckSpan from bank to bank, deck height graded
        // between the bank contact points and clamped above the water.
        int stationCount = Mathf.CeilToInt(crossing.Width / style.DeckSpan) + 1;
        bool[] pierAlive = new bool[stationCount];
        bool[] deckAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];
        float[] stationDeckH = new float[stationCount];

        for (int i = 0; i < stationCount; i++)
        {
            float along = Mathf.Min(i * style.DeckSpan, crossing.Width);
            stationPos[i] = from + dir * along;
            float t = crossing.Width > 0.01f ? along / crossing.Width : 0f;
            stationDeckH[i] = Mathf.Max(Mathf.Lerp(deckFromH, deckToH, t), minDeck);

            bool inFairway = crossing.FairwayWidth > 0f && Mathf.Abs(along - fairwayMid) <= fairwayHalf;
            bool isBankStation = i == 0 || i == stationCount - 1;

            // Survival falls toward mid-span; the fairway is always cleared.
            float mid = (stationCount - 1) * 0.5f;
            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float survival = Mathf.Lerp(style.BankSurvival, style.MidSurvival, midCloseness);

            float pierSurvival = survival + (1f - survival) * style.PierPersistence;
            bool alive = !inFairway && (isBankStation || NextFloat(rng) < pierSurvival);
            pierAlive[i] = alive;
            // With persistent piers the deck decays on its own curve (extra
            // draw only in that mode, so default plans are byte-identical).
            deckAlive[i] = alive && (style.PierPersistence <= 0f || isBankStation || NextFloat(rng) < survival);

            float ground = BiomeBlendedHeight.GetBlendedHeight(stationPos[i].x, stationPos[i].y, world);

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
            if (!deckAlive[i] || !deckAlive[i + 1])
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
        foreach (Vector2 bank in new[] { from, to })
        {
            float bankGround = BiomeBlendedHeight.GetBlendedHeight(bank.x, bank.y, world);
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Abutment,
                Prefab = style.AbutmentPrefab,
                Position = new Vector3(bank.x, bankGround - 0.3f, bank.y),
                YawDegrees = yaw,
                HealthFraction = 0.5f + NextFloat(rng) * 0.4f,
            });

            if (string.IsNullOrEmpty(style.ArchPrefab))
                continue;

            // Springing only makes sense off a bank that stands clear of the
            // water; a near-ford bank would put the arch in the mud.
            Vector2 inward = bank == from ? dir : -dir;
            bool tallEnough = bankGround > crossing.WaterLevel + 0.8f;
            bool survives = NextFloat(rng) < style.BankSurvival; // draw always, for rng stability
            if (tallEnough && survives)
                EmitArch(pieces, style, bank, inward, bankGround, rng);
        }

        // Steps up onto a stepped end, last so the rest of the plan draws the
        // same random sequence whether or not the site is stepped.
        if (endRise > 0f)
        {
            EmitSteps(pieces, style, world, from, dir, bankFromH, stationDeckH[0], yaw, rng);
            EmitSteps(pieces, style, world, to, -dir, bankToH, stationDeckH[stationCount - 1], yaw, rng);
        }

        return pieces;
    }

    /// <summary>How far above the road a site's deck ends sit: 0 for a
    /// flush site, SteppedEndMinRise..MaxRise for a stepped one. Decided by
    /// the site hash, not the plan's rng, so the choice never disturbs the
    /// ruin draws of the rest of the plan.</summary>
    public static float SteppedEndRise(RoadCrossing crossing)
    {
        int h = StableSeed(crossing);
        unchecked
        {
            uint u = (uint)h;
            u ^= u >> 16; u *= 0x7feb352du; u ^= u >> 15; u *= 0x846ca68bu; u ^= u >> 16;
            float pick = (u & 0xFFFF) / 65536f;
            if (pick >= RoadConstants.SteppedEndChance)
                return 0f;
            float span = ((u >> 16) & 0xFFFF) / 65536f;
            return RoadConstants.SteppedEndMinRise + span * (RoadConstants.SteppedEndMaxRise - RoadConstants.SteppedEndMinRise);
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

    /// <summary>
    /// Ford span: a continuous low deck from shore to shore, one station per
    /// DeckSpan with posts to the bed, deck at least FordSpanDeckClearance
    /// above the water and never below either bank, lightly ruined (it is a
    /// footbridge, not a monument). A step piece at each end where the deck
    /// sits above the road.
    /// </summary>
    private static void EmitShallowSpan(List<BridgePiece> pieces, RoadCrossing crossing,
        WorldGenerator world, BridgeStyle style, System.Random rng)
    {
        Vector2 from = crossing.FromBank;
        Vector2 to = crossing.ToBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

        float bankFromH = BiomeBlendedHeight.GetBlendedHeight(from.x, from.y, world);
        float bankToH = BiomeBlendedHeight.GetBlendedHeight(to.x, to.y, world);
        float deckH = Mathf.Max(Mathf.Max(bankFromH, bankToH) + RoadConstants.FordSpanDeckRise,
            crossing.WaterLevel + RoadConstants.FordSpanDeckClearance);

        int stationCount = Mathf.CeilToInt(crossing.Width / style.DeckSpan) + 1;
        bool[] alive = new bool[stationCount];
        Vector2[] pos = new Vector2[stationCount];
        for (int i = 0; i < stationCount; i++)
        {
            float along = Mathf.Min(i * style.DeckSpan, crossing.Width);
            pos[i] = from + dir * along;
            bool isEnd = i == 0 || i == stationCount - 1;
            alive[i] = isEnd || NextFloat(rng) < style.BankSurvival;
            if (alive[i])
                EmitStation(pieces, style, world, pos[i], side, deckH, yaw, rng);
        }
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!alive[i] || !alive[i + 1])
                continue;
            Vector2 mid2 = (pos[i] + pos[i + 1]) * 0.5f;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Deck,
                Prefab = style.DeckPrefab,
                Position = new Vector3(mid2.x, deckH - style.DeckTopOffset, mid2.y),
                YawDegrees = yaw,
                HealthFraction = RuinHealth(rng),
            });
        }

        // Steps: the road arrives at bank height; the deck may sit above it.
        EmitSteps(pieces, style, world, from, dir, bankFromH, deckH, yaw, rng);
        EmitSteps(pieces, style, world, to, -dir, bankToH, deckH, yaw, rng);
    }

    /// <summary>Steps from the road at a bank up onto a deck edge that sits
    /// above it: one step piece per metre of rise, marching outward from the
    /// abutment so the top step meets the deck edge. A step whose foot is
    /// above the ground gets a post under it (grounded by construction, like
    /// a station).</summary>
    private static void EmitSteps(List<BridgePiece> pieces, BridgeStyle style, WorldGenerator world,
        Vector2 bank, Vector2 inward, float bankH, float deckH, float yaw, System.Random rng)
    {
        if (string.IsNullOrEmpty(style.StairPrefab) || deckH - bankH < 0.4f)
            return;
        int steps = Mathf.Max(1, Mathf.CeilToInt((deckH - bankH) / 1f - 0.02f)); // tolerate float noise
        float stepYaw = Mathf.Atan2(inward.x, inward.y) * 180f / Mathf.PI + 180f; // stair prefab rises toward local -z
        for (int k = 0; k < steps; k++)
        {
            Vector2 c = bank - inward * (1f + k * 2f);
            float foot = deckH - 1f - k * 1f;
            float health = RuinHealth(rng);
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Stair,
                Prefab = style.StairPrefab,
                Position = new Vector3(c.x, foot, c.y),
                YawDegrees = stepYaw,
                HealthFraction = health,
            });
            float ground = BiomeBlendedHeight.GetBlendedHeight(c.x, c.y, world);
            if (foot > ground + 0.15f)
                EmitColumn(pieces, style, c, ground, foot, yaw, health);
        }
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
                EmitColumn(pieces, style, postPos, BiomeBlendedHeight.GetBlendedHeight(postPos.x, postPos.y, world), postTop, yaw, health);
            }
        }
        else
        {
            EmitColumn(pieces, style, pos, BiomeBlendedHeight.GetBlendedHeight(pos.x, pos.y, world), postTop, yaw, health);
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
        float ground = BiomeBlendedHeight.GetBlendedHeight(pos.x, pos.y, world);

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
