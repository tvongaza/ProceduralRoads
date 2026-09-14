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
/// The bridge is a row of stations every <see cref="DeckSpan"/> metres ALONG
/// THE DECK from bank to bank -- slope distance, so a pitched plate still
/// meets its neighbours snap to snap. A station is a pair of poles stacked
/// down from just under the deck until buried in the riverbed, tied by two
/// crossbeams; the deck is two lanes of plates, left and right of the
/// crossing line, four metres wide like the road. Plates exist only where
/// both end stations survive and pitch to follow the deck, which grades
/// between the two bank heights and never sits below the water plus a
/// freeboard. Ruin is deterministic per (crossing, world seed):
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

    public const float DeckSpan = 2f;         // one deck plate, measured ALONG the deck (slope distance)
    public const float PostSegment = 2f;      // vertical metres per pole
    public const float LaneOffset = 1f;       // the two lanes' centres, either side of the crossing line
    public const float DeckHalfWidth = 2f;    // two 2 m plates side by side: a 4 m deck, the road's width
    public const float PostSideOffset = 1.75f; // post pairs under the outer halves of the beams, never in the walkway
    public const int MaxStairSteps = 12;      // a stair marches outward until its foot is in the dirt, at most this far

    /// <summary>Bumped whenever the emitted geometry changes shape. Persisted
    /// with the spawned-zone record, so a world built by an older layout is
    /// recognised on load and its pieces are replaced rather than mixed with
    /// new ones (see RoadNetworkPersistence.TryLoadBridgeZones).</summary>
    public const int LayoutVersion = 2;
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
    /// distance of the site's: a lane is 2 m wide, so anything further off
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

    // ------------------------------------------------------------ geometry

    /// <summary>
    /// The deck's grade: a straight line from the near bank's deck height to
    /// the far bank's. DeckEndHeights already lifts both ends to the water
    /// plus freeboard, and a straight line between two points above a level
    /// stays above it, so there is no kink to handle.
    /// </summary>
    public static float Grade(RoadCrossing crossing, WorldGenerator world)
    {
        (float from, float to) = DeckEndHeights(crossing, world);
        return crossing.Width > 0.01f ? (to - from) / crossing.Width : 0f;
    }

    /// <summary>
    /// Horizontal distance between stations for a deck at this grade. A
    /// wood_floor is DeckSpan long along its own surface, so on a slope its
    /// two ends are DeckSpan apart ALONG THE SLOPE and DeckSpan * cos(theta)
    /// apart horizontally. Stations placed every 2 m horizontally on a graded
    /// deck would leave each plate 2 - 2cos(theta) short of the next: 9 cm
    /// per bay on the steepest crossing the pathfinder allows, and a player's
    /// replacement -- which snaps to its neighbour's corner, along the slope
    /// -- would drift off the stations by that much per bay.
    /// </summary>
    public static float StationSpacing(float grade) => DeckSpan / Mathf.Sqrt(1f + grade * grade);

    /// <summary>Whole spans that fit in the width at this grade, at least one.
    /// The epsilon keeps a width that IS a whole number of spans from losing
    /// one to float error.</summary>
    public static int Bays(float width, float grade = 0f) =>
        Mathf.Max(1, Mathf.FloorToInt(width / StationSpacing(grade) + 1e-3f));

    /// <summary>The horizontal length actually built: whole spans only, and
    /// never longer than the crossing the pathfinder accepted.</summary>
    public static float BuiltLength(float width, float grade = 0f) => Bays(width, grade) * StationSpacing(grade);

    /// <summary>Where the stations stand, measured horizontally along the
    /// crossing from the near bank. The remainder past the last station is
    /// under one stair run by construction; the far stair, anchored at that
    /// station, crosses it in its first step.</summary>
    internal static float[] StationsAlong(float width, float grade = 0f)
    {
        int bays = Bays(width, grade);
        float d = StationSpacing(grade);
        float[] alongs = new float[bays + 1];
        for (int i = 0; i <= bays; i++)
            alongs[i] = i * d;
        return alongs;
    }

    /// <summary>Whether a point this far along the crossing lies in the
    /// navigation gap: the run of bays left open on purpose so boats pass.
    /// Not ruin -- the diagnostic reports the two separately.</summary>
    public static bool InNavigationGap(RoadCrossing crossing, float along)
    {
        if (crossing.Kind != CrossingKind.Bridge || crossing.FairwayWidth <= 0f)
            return false;
        float mid = crossing.Along(crossing.FairwayCenter);
        float half = FairwayGap(crossing) * 0.5f + FairwayClearance;
        return Mathf.Abs(along - mid) <= half;
    }

    // ---------------------------------------------------------- snap points

    // Measured off the prefabs in game (valheimCLI cli_piece_geometry):
    //   wood_floor  snaps at its four corners      (+-1, 0, +-1)
    //   wood_beam   snaps at its two ends          (+-1, 0, 0)
    //   wood_pole2  snaps top and bottom           (0, +-1, 0)
    //   wood_stair  snaps HIGH (+-1, 1, -1), LOW (+-1, 0, +1)
    private static readonly Vector3[] FloorSnaps = { new(1, 0, -1), new(-1, 0, -1), new(1, 0, 1), new(-1, 0, 1) };
    private static readonly Vector3[] BeamSnaps = { new(-1, 0, 0), new(1, 0, 0) };
    private static readonly Vector3[] PoleSnaps = { new(0, 1, 0), new(0, -1, 0) };
    private static readonly Vector3[] StairSnaps = { new(-1, 1, -1), new(1, 1, -1), new(-1, 0, 1), new(1, 0, 1) };

    /// <summary>
    /// A piece's snap points in the world, from the prefab's measured local
    /// snaps and the rotation the game applies when spawning it
    /// (Quaternion.Euler(pitch, yaw, roll), which Unity composes as
    /// yaw about y, then pitch about x, then roll about z, applied to the
    /// local vector in the order roll, pitch, yaw). This is what "two pieces
    /// meet" means for a player: a snap of one lands on a snap of the other.
    /// </summary>
    public static Vector3[] SnapPoints(BridgePiece piece)
    {
        Vector3[] local = piece.Prefab switch
        {
            DeckPrefab => FloorSnaps,
            BeamPrefab => BeamSnaps,
            StairPrefab => StairSnaps,
            PostPrefab => PoleSnaps,
            _ => System.Array.Empty<Vector3>(),
        };
        Vector3[] world = new Vector3[local.Length];
        for (int i = 0; i < local.Length; i++)
            world[i] = piece.Position + Rotate(local[i], piece.PitchDegrees, piece.YawDegrees, piece.RollDegrees);
        return world;
    }

    /// <summary>Unity's Euler convention, spelled out so the tests can rely on
    /// it without a Quaternion: roll about z, then pitch about x, then yaw
    /// about y.</summary>
    public static Vector3 Rotate(Vector3 v, float pitchDeg, float yawDeg, float rollDeg)
    {
        const float k = Mathf.PI / 180f;
        float cr = Mathf.Cos(rollDeg * k), sr = Mathf.Sin(rollDeg * k);
        v = new Vector3(cr * v.x - sr * v.y, sr * v.x + cr * v.y, v.z);
        float cp = Mathf.Cos(pitchDeg * k), sp = Mathf.Sin(pitchDeg * k);
        v = new Vector3(v.x, cp * v.y - sp * v.z, sp * v.y + cp * v.z);
        float cy = Mathf.Cos(yawDeg * k), sy = Mathf.Sin(yawDeg * k);
        return new Vector3(cy * v.x + sy * v.z, v.y, -sy * v.x + cy * v.z);
    }

    // ---------------------------------------------------------------- ruin

    /// <summary>
    /// Every random decision in a bridge, keyed by WHAT it decides rather than
    /// by draw order: (world seed, crossing, bay, side, role). Adding a piece
    /// on one side therefore cannot reshuffle the damage on the other, a
    /// respawn reproduces the same ruin, and the two lanes of one bay are
    /// ruined independently because they are different keys.
    /// </summary>
    public readonly struct Ruin
    {
        private readonly int m_seed;
        public Ruin(int worldSeed, RoadCrossing crossing) => m_seed = worldSeed ^ StableSeed(crossing);

        public const int RoleStation = 1, RoleDeck = 2, RoleStair = 3, RoleStub = 4, RoleDebris = 5,
            RolePostHealth = 6, RoleBeamHealth = 7, RoleDeckHealth = 8, RoleStairHealth = 9, RoleColumnHealth = 10;
        public const int Left = -1, Centre = 0, Right = 1;

        /// <summary>A stable float in [0, 1).
        ///
        /// The four keys are folded into one 32-bit word with distinct odd
        /// multipliers and the word is finalised TWICE with murmur3's fmix32.
        /// A first version mixed once per key with an XOR, and two rolls that
        /// differed only in the role -- "does this station stand" and "does its
        /// ruin leave a stub" -- came out correlated strongly enough that every
        /// dead station in 100 seeds rolled a stub and none toppled. The roles
        /// have to be independent for the ruin to look like ruin.
        /// </summary>
        public float Roll(int bay, int side, int role, int extra = 0)
        {
            unchecked
            {
                uint h = (uint)m_seed * 0x9E3779B1u;
                h += (uint)bay * 0x85EBCA6Bu;
                h = Fmix(h);
                h += (uint)(side + 7) * 0xC2B2AE35u;
                h = Fmix(h);
                h += (uint)role * 0x27D4EB2Fu;
                h = Fmix(h);
                h += (uint)extra * 0x165667B1u;
                h = Fmix(Fmix(h));
                return (h >> 8) * (1f / 16777216f);
            }
        }

        public float Health(int bay, int side, int role, int extra = 0) =>
            RuinHealthMin + Roll(bay, side, role, extra) * (RuinHealthMax - RuinHealthMin);

        /// <summary>murmur3's 32-bit finaliser.</summary>
        private static uint Fmix(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }
    }

    // --------------------------------------------------------------- solve

    /// <summary>
    /// The bridge as it would stand with nothing missing: every station, every
    /// plate in both lanes, both stairs, the navigation gap filled in too.
    /// Diagnostic and test-only -- the game always builds the ruined plan --
    /// and the reference the repairability checks compare against. The
    /// fairway gap is included deliberately: it is not ruin, but it is a run
    /// of bays a player COULD fill, and if the grid did not continue across it
    /// they could not.
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

        Ruin r = new(worldSeed, crossing);

        if (crossing.Kind == CrossingKind.Ford)
        {
            // Wading and raised fords are road, not pieces; a span is a short
            // low footbridge with steps.
            if (crossing.Style == FordStyle.Span)
                EmitShallowSpan(pieces, crossing, world, r, ruin);
            return pieces;
        }

        Vector2 from = crossing.FromBank;
        Vector2 dir = crossing.Direction;
        Vector2 side = new(-dir.y, dir.x);
        float yaw = YawDegrees(dir);
        (float deckFromH, _) = DeckEndHeights(crossing, world);
        float grade = Grade(crossing, world);

        float[] alongs = StationsAlong(crossing.Width, grade);
        int stationCount = alongs.Length;
        // The deck ends here, a remainder short of the far bank at most. Both
        // stairs are anchored at a DECK END so their top snap meets the deck
        // edge; anchoring the far one at the bank instead would leave exactly
        // the gap that anchoring here removes.
        Vector2 deckEnd = from + dir * alongs[stationCount - 1];
        bool[] stationAlive = new bool[stationCount];
        Vector2[] stationPos = new Vector2[stationCount];
        float[] stationDeckH = new float[stationCount];
        float mid = (stationCount - 1) * 0.5f;

        for (int i = 0; i < stationCount; i++)
        {
            float along = alongs[i];
            stationPos[i] = from + dir * along;
            stationDeckH[i] = deckFromH + grade * along;

            // The navigation gap is intentional and permanent, not ruin -- but
            // it is still a section a player may fill in, so the complete
            // layout includes it: "fully repaired" means every bay on the grid.
            bool inGap = ruin && InNavigationGap(crossing, along);
            bool isBankStation = i == 0 || i == stationCount - 1;

            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i - mid) / mid : 0f;
            float survival = Mathf.Lerp(BankSurvival, MidSurvival, midCloseness);
            float pierSurvival = survival + (1f - survival) * PierPersistence;
            stationAlive[i] = !inGap && (!ruin || isBankStation || r.Roll(i, Ruin.Centre, Ruin.RoleStation) < pierSurvival);

            float ground = BiomeBlendedHeight.GetBlendedHeight(stationPos[i].x, stationPos[i].y, world);
            if (stationAlive[i])
            {
                EmitStation(pieces, world, stationPos[i], side, stationDeckH[i], yaw, r, i);
            }
            else if (ruin && !inGap && r.Roll(i, Ruin.Centre, Ruin.RoleStub) < StubChance)
            {
                // Rotted stub: a single buried segment poking out near the waterline.
                EmitColumn(pieces, stationPos[i], ground,
                    Mathf.Min(ground + PostSegment, crossing.WaterLevel + 0.3f), yaw,
                    0.25f + r.Roll(i, Ruin.Centre, Ruin.RoleStub, 1) * 0.15f);
            }
            else if (ruin && !inGap && r.Roll(i, Ruin.Centre, Ruin.RoleDebris) < DebrisChance)
            {
                EmitDebris(pieces, stationPos[i], dir, world, r, i);
            }
        }

        // A plate needs both of its stations standing, so every surviving
        // plate rests on a beam at each end. Beyond that the two lanes of a
        // bay are ruined on their own: left and right may each be present or
        // missing, and carry their own health.
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!stationAlive[i] || !stationAlive[i + 1])
                continue;
            float midCloseness = mid > 0f ? 1f - Mathf.Abs(i + 0.5f - mid) / mid : 0f;
            float survival = Mathf.Lerp(BankSurvival, MidSurvival, midCloseness);
            bool endBay = i == 0 || i + 2 == stationCount;
            foreach (int lane in new[] { Ruin.Left, Ruin.Right })
            {
                if (ruin && !endBay && r.Roll(i, lane, Ruin.RoleDeck) >= survival)
                    continue;
                EmitDeck(pieces, stationPos[i], stationPos[i + 1], stationDeckH[i], stationDeckH[i + 1],
                    side * (lane * LaneOffset), yaw, r.Health(i, lane, Ruin.RoleDeckHealth));
            }
        }

        // Every end is a stair down from the deck edge into the bank, two
        // abreast like the deck.
        EmitSteps(pieces, world, from, dir, side, stationDeckH[0], r, 0);
        EmitSteps(pieces, world, deckEnd, -dir, side, stationDeckH[stationCount - 1], r, stationCount);

        return pieces;
    }

    /// <summary>
    /// Ford span: a continuous low deck from shore to shore, one station per
    /// DeckSpan with posts to the bed, deck at least FordSpanDeckClearance
    /// above the water and FordSpanDeckRise above the higher bank, lightly
    /// ruined (it is a footbridge, not a monument), a stair at each end. The
    /// same two-lane grid as a bridge: a footbridge a player cannot repair
    /// with an ordinary wood_floor is no better than one they cannot walk.
    /// </summary>
    private static void EmitShallowSpan(List<BridgePiece> pieces, RoadCrossing crossing, WorldGenerator world, Ruin r, bool ruin)
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

        float[] alongs = StationsAlong(crossing.Width);   // flat deck: no grade
        int stationCount = alongs.Length;
        Vector2 deckEnd = from + dir * alongs[stationCount - 1];
        bool[] alive = new bool[stationCount];
        Vector2[] pos = new Vector2[stationCount];
        for (int i = 0; i < stationCount; i++)
        {
            pos[i] = from + dir * alongs[i];
            bool isEnd = i == 0 || i == stationCount - 1;
            alive[i] = !ruin || isEnd || r.Roll(i, Ruin.Centre, Ruin.RoleStation) < BankSurvival;
            if (alive[i])
                EmitStation(pieces, world, pos[i], side, deckH, yaw, r, i);
        }
        for (int i = 0; i + 1 < stationCount; i++)
        {
            if (!alive[i] || !alive[i + 1])
                continue;
            foreach (int lane in new[] { Ruin.Left, Ruin.Right })
            {
                if (ruin && r.Roll(i, lane, Ruin.RoleDeck) >= BankSurvival)
                    continue;
                EmitDeck(pieces, pos[i], pos[i + 1], deckH, deckH, side * (lane * LaneOffset), yaw,
                    r.Health(i, lane, Ruin.RoleDeckHealth));
            }
        }

        EmitSteps(pieces, world, from, dir, side, deckH, r, 0);
        EmitSteps(pieces, world, deckEnd, -dir, side, deckH, r, stationCount);
    }

    // ---------------------------------------------------------------- emit

    /// <summary>One deck plate between two stations, offset to its lane. Its
    /// centre is the midpoint and its pitch the grade between the stations;
    /// because the stations are DeckSpan apart ALONG the slope, the plate's
    /// two ends land exactly on them.</summary>
    private static void EmitDeck(List<BridgePiece> pieces, Vector2 a, Vector2 b, float hA, float hB, Vector2 laneOffset, float yaw, float health)
    {
        Vector2 mid2 = (a + b) * 0.5f + laneOffset;
        float run = Vector2.Distance(a, b);
        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Deck,
            Prefab = DeckPrefab,
            Position = new Vector3(mid2.x, (hA + hB) * 0.5f, mid2.y),
            YawDegrees = yaw,
            PitchDegrees = -Mathf.Atan2(hB - hA, run) * 180f / Mathf.PI,
            HealthFraction = health,
        });
    }

    /// <summary>
    /// Steps down from a deck edge into the bank, two abreast: each step's
    /// top edge meets the previous one's foot (or the deck), and the stair
    /// marches OUTWARD until a step's foot is in the dirt -- sampled at that
    /// step's own footprint, per lane, not at the bank point the crossing was
    /// detected at. The far stair is anchored inward of the bank by the
    /// remainder, so the ground under it is not the ground at the bank; the
    /// old fixed count from the bank height could stop a step early with its
    /// foot in the air. A step whose foot is above the ground gets a post
    /// under it, so every step is grounded by construction.
    /// </summary>
    private static void EmitSteps(List<BridgePiece> pieces, WorldGenerator world,
        Vector2 anchor, Vector2 inward, Vector2 side, float deckH, Ruin r, int bayKey)
    {
        float stepYaw = YawDegrees(inward) + 180f; // the stair prefab rises toward local -z
        float yaw = YawDegrees(inward);
        for (int k = 0; k < MaxStairSteps; k++)
        {
            Vector2 c = anchor - inward * (1f + k * 2f);
            float foot = deckH - 1f - k * 1f;
            bool grounded = true;
            foreach (int lane in new[] { Ruin.Left, Ruin.Right })
            {
                Vector2 p = c + side * (lane * LaneOffset);
                float health = r.Health(bayKey, lane, Ruin.RoleStairHealth, k);
                pieces.Add(new BridgePiece
                {
                    Kind = BridgePieceKind.Stair,
                    Prefab = StairPrefab,
                    Position = new Vector3(p.x, foot, p.y),
                    YawDegrees = stepYaw,
                    HealthFraction = health,
                });
                float ground = BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);
                if (foot > ground + 0.15f)
                {
                    EmitColumn(pieces, p, ground, foot, yaw, health);
                    grounded = false;
                }
            }
            if (grounded)
                break;
        }
    }

    /// <summary>One surviving station: two post pairs stacked down into the
    /// riverbed under the outer halves of the deck, tied by two crossbeams
    /// just under it -- one per lane, meeting at the centre seam.</summary>
    private static void EmitStation(List<BridgePiece> pieces, WorldGenerator world,
        Vector2 pos, Vector2 side, float deckH, float yaw, Ruin r, int bay)
    {
        float postTop = deckH - PostTopBelowDeck;
        foreach (float s in new[] { -PostSideOffset, PostSideOffset })
        {
            Vector2 postPos = pos + side * s;
            EmitColumn(pieces, postPos, BiomeBlendedHeight.GetBlendedHeight(postPos.x, postPos.y, world), postTop, yaw,
                r.Health(bay, s < 0 ? Ruin.Left : Ruin.Right, Ruin.RolePostHealth));
        }
        foreach (int lane in new[] { Ruin.Left, Ruin.Right })
        {
            Vector2 beamPos = pos + side * (lane * LaneOffset);
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.Beam,
                Prefab = BeamPrefab,
                Position = new Vector3(beamPos.x, deckH - BeamBelowDeck, beamPos.y),
                YawDegrees = yaw, // the beam runs along its local x: across the deck
                HealthFraction = r.Health(bay, lane, Ruin.RoleBeamHealth),
            });
        }
    }

    /// <summary>A column of PostSegment poles from topHeight down until one is
    /// buried in the ground (grounded by construction).</summary>
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
    private static void EmitDebris(List<BridgePiece> pieces, Vector2 station, Vector2 dir, WorldGenerator world, Ruin r, int bay)
    {
        Vector2 side = new(-dir.y, dir.x);
        float offset = 1f + r.Roll(bay, Ruin.Centre, Ruin.RoleDebris, 1) * 2f;
        if (r.Roll(bay, Ruin.Centre, Ruin.RoleDebris, 2) < 0.5f) offset = -offset;

        Vector2 pos = station + side * offset;
        float ground = BiomeBlendedHeight.GetBlendedHeight(pos.x, pos.y, world);
        pieces.Add(new BridgePiece
        {
            Kind = BridgePieceKind.Debris,
            Prefab = DebrisPrefab,
            Position = new Vector3(pos.x, ground + 0.2f, pos.y),
            YawDegrees = r.Roll(bay, Ruin.Centre, Ruin.RoleDebris, 3) * 360f,
            PitchDegrees = 50f + r.Roll(bay, Ruin.Centre, Ruin.RoleDebris, 4) * 70f, // toppled, not standing
            RollDegrees = r.Roll(bay, Ruin.Centre, Ruin.RoleDebris, 5) * 30f,
            HealthFraction = 0.2f + r.Roll(bay, Ruin.Centre, Ruin.RoleDebris, 6) * 0.2f,
        });
    }

    /// <summary>Unity yaw (degrees about +y) that turns local +z onto a world XZ direction.</summary>
    public static float YawDegrees(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

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
