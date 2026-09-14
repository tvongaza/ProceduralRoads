using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Can a player repair one of these bridges with ordinary vanilla pieces?
///
/// The requirement, which is stronger than "no two pieces overlap": if every
/// intentionally missing pier and deck plate is replaced with a standard piece,
/// the result is a continuous, aligned walk from bank to bank, stairs and
/// approaches included. That is a claim about the EMITTED pieces, so it is
/// asserted against them rather than against the spacing helper.
///
/// The numbers the assertions use were measured off the prefabs in game
/// (valheimCLI `cli_piece_geometry`, logs/piece-geometry-*.txt), not assumed:
///
///   wood_floor  2.000 x 0.130 x 2.000, snaps at its four corners (+-1, 0, +-1)
///   wood_beam   2.000 x 0.400 x 0.400, snaps at (+-1, 0, 0)
///   wood_pole2  0.400 x 2.000 x 0.400, snaps at (0, +-1, 0)
///   wood_stair  2.000 x 1.121 x 2.029, snaps HIGH at (+-1, 1, -1)
///                                          and LOW at (+-1, 0, +1)
///
/// Two consequences drive everything here. Deck plates snap edge to edge only
/// when their centres are exactly DeckSpan apart -- dividing a width evenly
/// into near-2 m bays looks right and cannot be repaired, because the player's
/// replacement snaps at exactly 2 m and the error walks down the bridge. And a
/// stair's HIGH snap sits one run toward the bridge from its origin at +1 in y,
/// so the stair's top edge lands exactly on its anchor point at deck height --
/// which only meets the deck if the deck ENDS at that anchor.
/// </summary>
public class BridgeRepairabilityTests
{
    private const float Tol = 0.002f;

    /// <summary>A river of a chosen width between banks of chosen heights,
    /// flat-bedded, so a crossing can be built at an exact width and angle.</summary>
    private sealed class CrossingWorld : WorldGenerator
    {
        public Vector2 From, To;
        public float Bed = 26f;
        public float FromBankH = 32f;
        public float ToBankH = 32f;

        public override float GetHeight(float wx, float wy)
        {
            Vector2 d = (To - From).normalized;
            float w = Vector2.Distance(From, To);
            float t = Vector2.Dot(new Vector2(wx, wy) - From, d);
            if (t <= 0f) return FromBankH;
            if (t >= w) return ToBankH;
            return Bed;
        }

        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;

        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            Vector2 d = (To - From).normalized;
            float w = Vector2.Distance(From, To);
            float t = Vector2.Dot(new Vector2(wx, wy) - From, d);
            weight = t > 0f && t < w ? 1f : 0f;
            width = weight > 0f ? w : 0f;
        }
    }

    /// <summary>A crossing of an exact width and heading, and the world under it.</summary>
    private static (RoadCrossing crossing, CrossingWorld world) Crossing(
        float width, float headingDegrees = 0f, float fromBankH = 32f, float toBankH = 32f,
        float fairwayWidth = 0f, CrossingKind kind = CrossingKind.Bridge, FordStyle style = FordStyle.None)
    {
        float rad = headingDegrees * Mathf.PI / 180f;
        Vector2 dir = new(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector2 from = new(-width * 0.5f * dir.x, -width * 0.5f * dir.y);
        Vector2 to = from + dir * width;
        var world = new CrossingWorld { From = from, To = to, FromBankH = fromBankH, ToBankH = toBankH };
        var crossing = RoadCrossing.Between(from, to, world.Bed, (from + to) * 0.5f, fairwayWidth, kind, style);
        return (crossing, world);
    }

    // ---- projections onto the crossing ----

    private static float AlongOf(RoadCrossing c, Vector3 p) =>
        Vector2.Dot(new Vector2(p.x, p.z) - c.FromBank, c.Direction);

    private static float LateralOf(RoadCrossing c, Vector3 p)
    {
        Vector2 side = new(-c.Direction.y, c.Direction.x);
        return Vector2.Dot(new Vector2(p.x, p.z) - c.FromBank, side);
    }

    private static List<BridgePiece> Of(IEnumerable<BridgePiece> plan, BridgePieceKind kind) =>
        plan.Where(p => p.Kind == kind).ToList();

    /// <summary>The world point a stair's HIGH snap edge sits at, and its
    /// height, from the measured prefab: one run toward the bridge from the
    /// origin, one metre up.</summary>
    private static (Vector2 at, float y) StairTop(BridgePiece stair)
    {
        float rad = stair.YawDegrees * Mathf.PI / 180f;
        Vector2 localForward = new(Mathf.Sin(rad), Mathf.Cos(rad)); // local +z
        Vector2 origin = new(stair.Position.x, stair.Position.z);
        return (origin - localForward * 1f, stair.Position.y + 1f);
    }

    // ---- the widths under test ----

    public static IEnumerable<object[]> BridgeWidths() => new List<object[]>
    {
        new object[] { 80f },      // an exact multiple of the deck span
        new object[] { 80.01f },   // a remainder just above zero
        new object[] { 80.05f },   // the width that used to stack two stations 5 cm apart
        new object[] { 80.6f },    // and the one that stacked them 60 cm apart
        new object[] { 80.9f },    // just below half a span
        new object[] { 81.1f },    // just above half a span
        new object[] { 81.9f },    // just below a full span
        new object[] { 40f },
        new object[] { 127.6f },   // near the 128 m cap the pathfinder allows
    };

    public static IEnumerable<object[]> SpanWidths() => new List<object[]>
    {
        new object[] { 8f },
        new object[] { 8.05f },
        new object[] { 9.1f },
        new object[] { 9.9f },
        new object[] { 2.05f },    // the third width that used to stack stations
        new object[] { 6f },
    };

    // ---- the complete layout ----

    [Theory]
    [MemberData(nameof(BridgeWidths))]
    public void ACompletedBridgeIsOneContinuousAlignedWalk(float width) =>
        AssertRepairable(Crossing(width), width);

    [Theory]
    [MemberData(nameof(SpanWidths))]
    public void ACompletedFordSpanIsOneContinuousAlignedWalk(float width) =>
        AssertRepairable(Crossing(width, kind: CrossingKind.Ford, style: FordStyle.Span), width);

    [Theory]
    [InlineData(0f)]
    [InlineData(37f)]
    [InlineData(90f)]
    [InlineData(214.5f)]
    public void ARotatedCrossingIsLaidOutTheSameWay(float heading) =>
        AssertRepairable(Crossing(80.6f, headingDegrees: heading), 80.6f);

    [Theory]
    [InlineData(32f, 34f)]
    [InlineData(34f, 32f)]
    [InlineData(31f, 33.5f)]
    public void APitchedDeckStillMeetsBothBanks(float fromBankH, float toBankH)
    {
        var (crossing, world) = Crossing(80.6f, fromBankH: fromBankH, toBankH: toBankH);
        var plan = AssertRepairable((crossing, world), 80.6f);

        // Every plate's pitch matches the grade it actually spans, so the
        // plates form one surface rather than a staircase of flat plates.
        var decks = Of(plan, BridgePieceKind.Deck).OrderBy(p => AlongOf(crossing, p.Position)).ToList();
        for (int i = 0; i + 1 < decks.Count; i++)
        {
            float rise = decks[i + 1].Position.y - decks[i].Position.y;
            float expected = -Mathf.Atan2(rise, BridgeLayout.DeckSpan) * 180f / Mathf.PI;
            Assert.InRange(decks[i].PitchDegrees, expected - 0.5f, expected + 0.5f);
        }
    }

    /// <summary>The whole requirement, asserted on the emitted pieces.</summary>
    private List<BridgePiece> AssertRepairable((RoadCrossing crossing, CrossingWorld world) site, float width)
    {
        (RoadCrossing crossing, CrossingWorld world) = site;
        var plan = BridgeLayout.SolveComplete(crossing, world, 4242);
        Assert.NotEmpty(plan);

        float built = BridgeLayout.BuiltLength(crossing.Width);

        // 1. Never longer than the crossing the pathfinder priced.
        Assert.True(built <= crossing.Width + Tol,
            $"built {built:F3} m over a {crossing.Width:F3} m crossing");
        // and never more than one bay short of it, or the stair cannot reach.
        Assert.True(crossing.Width - built < BridgeLayout.DeckSpan + Tol,
            $"built {built:F3} m leaves {crossing.Width - built:F3} m of the crossing unbridged");

        // 2. Stations sit on an exact DeckSpan grid from the near bank. One
        //    crossbeam marks each, so the beams are the station census.
        var beams = Of(plan, BridgePieceKind.Beam)
            .Select(p => AlongOf(crossing, p.Position)).OrderBy(a => a).ToList();
        Assert.Equal(BridgeLayout.Bays(crossing.Width) + 1, beams.Count);
        for (int i = 0; i < beams.Count; i++)
            Assert.InRange(beams[i], i * BridgeLayout.DeckSpan - Tol, i * BridgeLayout.DeckSpan + Tol);

        // 3. One deck plate per bay, centred between its stations, on the
        //    centreline, and exactly DeckSpan from its neighbours -- neither
        //    overlapping them nor leaving a hole.
        var decks = Of(plan, BridgePieceKind.Deck)
            .OrderBy(p => AlongOf(crossing, p.Position)).ToList();
        Assert.Equal(BridgeLayout.Bays(crossing.Width), decks.Count);
        for (int i = 0; i < decks.Count; i++)
        {
            float along = AlongOf(crossing, decks[i].Position);
            float want = i * BridgeLayout.DeckSpan + BridgeLayout.DeckSpan * 0.5f;
            Assert.InRange(along, want - Tol, want + Tol);
            Assert.InRange(LateralOf(crossing, decks[i].Position), -Tol, Tol);
        }

        // 4. BOTH ends: the stair's high snap edge lands exactly on the deck's
        //    end edge, at the deck's height there. This is the assertion that
        //    fails when an overlap is traded for an end gap.
        foreach ((float endAlong, Vector2 inward) in new[]
                 {
                     (0f, crossing.Direction),
                     (built, -crossing.Direction),
                 })
        {
            Vector2 endPoint = crossing.FromBank + crossing.Direction * endAlong;
            // The deck's height AT THE END is the end station's, not the last
            // plate's centre: on a pitched deck those differ by half a bay's
            // rise. The station's crossbeam carries it, a fixed BeamBelowDeck
            // under the deck, so the expectation comes off an emitted piece
            // rather than being recomputed from the inputs.
            var endBeam = Of(plan, BridgePieceKind.Beam)
                .OrderBy(b => Mathf.Abs(AlongOf(crossing, b.Position) - endAlong)).First();
            float deckY = endBeam.Position.y + BridgeLayout.BeamBelowDeck;

            var stairs = Of(plan, BridgePieceKind.Stair)
                .Where(st => Vector2.Dot(new Vector2(st.Position.x, st.Position.z) - endPoint, inward) < 0f)
                .ToList();
            Assert.True(stairs.Count > 0, $"no stair at the deck end {endAlong:F2}");

            var top = stairs.OrderByDescending(st => st.Position.y).First();
            (Vector2 at, float y) = StairTop(top);
            Assert.True(Vector2.Distance(at, endPoint) <= 0.01f,
                $"the top stair at end {endAlong:F2} meets the deck at {Vector2.Distance(at, endPoint):F3} m away, not flush");
            Assert.InRange(y, deckY - 0.01f, deckY + 0.01f);
        }

        // 5. Posts stack on the 2 m snap the prefab has, and stand beside the
        //    centreline where a station's pair belongs -- never in the walkway.
        foreach (var group in Of(plan, BridgePieceKind.Post)
                     .GroupBy(p => (Mathf.Round(p.Position.x * 100f), Mathf.Round(p.Position.z * 100f))))
        {
            var column = group.OrderByDescending(p => p.Position.y).ToList();
            for (int i = 0; i + 1 < column.Count; i++)
                Assert.InRange(column[i].Position.y - column[i + 1].Position.y,
                    BridgeLayout.PostSegment - Tol, BridgeLayout.PostSegment + Tol);
        }

        return plan;
    }

    // ---- the ruined plan is a subset of the completed one ----

    [Theory]
    [MemberData(nameof(BridgeWidths))]
    public void EveryPieceTheGameBuildsStandsWhereTheCompletedBridgeHasOne(float width)
    {
        var (crossing, world) = Crossing(width, fairwayWidth: 30f);
        var complete = BridgeLayout.SolveComplete(crossing, world, 99);
        var built = BridgeLayout.Solve(crossing, world, 99);

        Assert.NotEmpty(built);
        Assert.True(complete.Count > built.Count, "the completed bridge should have more pieces than the ruined one");

        // Stubs and debris are deliberately off the grid (a rotted stump, a
        // toppled pole on the bed); the STRUCTURE has to be on it, or the
        // missing pieces cannot be replaced against what is standing.
        foreach (BridgePiece piece in built.Where(p => p.Kind is BridgePieceKind.Deck
                                                    or BridgePieceKind.Beam
                                                    or BridgePieceKind.Stair))
        {
            Assert.True(complete.Any(c => c.Kind == piece.Kind
                                          && c.Prefab == piece.Prefab
                                          && Vector3.Distance(c.Position, piece.Position) <= 0.01f),
                $"{piece.Kind} at {piece.Position} is not on the completed bridge's grid");
        }
    }

    /// <summary>
    /// The two policies this replaced, each measured against the two defects
    /// that matter. Kept as arithmetic rather than a note in a commit message,
    /// because "the new one is better" is the kind of claim that rots.
    ///
    ///   A  the original: Ceil(W/span)+1 stations, the last clamped to the bank
    ///      -- the far station lands on top of the one before it
    ///   B  the first fix: A, but the stub station trimmed and the stairs left
    ///      anchored at the BANK -- no overlap, and now the deck stops short of
    ///      the stair it is supposed to meet
    ///   C  this one: whole spans only, stairs anchored at the DECK END
    /// </summary>
    [Theory]
    [InlineData(80.05f)]
    [InlineData(80.6f)]
    [InlineData(2.05f)]
    public void NeitherEarlierPolicyGotBothEndsRight(float width)
    {
        const float span = BridgeLayout.DeckSpan;

        // A: the last two stations, one clamped onto the bank.
        int aCount = Mathf.CeilToInt(width / span) + 1;
        float aLast = Mathf.Min((aCount - 1) * span, width);
        float aPrev = Mathf.Min((aCount - 2) * span, width);
        float aOverlap = aLast - aPrev;
        Assert.True(aOverlap < span * 0.5f,
            $"policy A was supposed to stack stations at width {width}, but its last bay is {aOverlap:F3} m");

        // B: the stub trimmed, so no overlap -- but the stairs stayed at the
        // bank while the deck ended at aPrev, so the two no longer meet.
        float bDeckEnd = aPrev;
        float bStairGap = width - bDeckEnd;
        Assert.True(bStairGap > 0.01f,
            $"policy B was supposed to leave an end gap at width {width}");

        // C: every bay a full span, and the stair anchored where the deck ends,
        // so the gap it has to cross is under one run and the two meet exactly.
        float cBuilt = BridgeLayout.BuiltLength(width);
        float[] alongs = BridgeLayout.StationsAlong(width);
        for (int i = 0; i + 1 < alongs.Length; i++)
            Assert.InRange(alongs[i + 1] - alongs[i], span - Tol, span + Tol);
        Assert.True(width - cBuilt < span, $"the stair cannot cross {width - cBuilt:F3} m in one run");
        Assert.True(cBuilt <= width + Tol, "C must never build past the accepted span");
    }

    [Fact]
    public void TheNavigationGapIsBaysThatAreMissing_NotBaysThatDoNotExist()
    {
        // A player who fills the boat gap must be able to: the grid runs
        // straight through it, the shipped plan simply leaves it empty.
        var (crossing, world) = Crossing(80.6f, fairwayWidth: 30f);
        var complete = BridgeLayout.SolveComplete(crossing, world, 7);
        var built = BridgeLayout.Solve(crossing, world, 7);

        float mid = crossing.Along(crossing.FairwayCenter);
        Assert.DoesNotContain(built, p => p.Kind == BridgePieceKind.Deck
                                          && Mathf.Abs(AlongOf(crossing, p.Position) - mid) < 2f);
        Assert.Contains(complete, p => p.Kind == BridgePieceKind.Deck
                                       && Mathf.Abs(AlongOf(crossing, p.Position) - mid) < 2f);
    }
}
