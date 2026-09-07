using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tests for the snap-chained composition grammar (the 2026-09 rework):
/// stair pieces mate at their snap edges instead of drifting apart, hard
/// turns happen on landings, support and pier columns are grounded by
/// burial, the bridge deck grades between the banks instead of running
/// level at the higher one, and wood stations are post-pair assemblies.
///
/// Piece dimensions mirror road_snap_probe output: stairs are 2m-run/1m-rise
/// with bottom-edge snaps at (±1,0,+1) and top-edge snaps at (±1,1,-1);
/// wood_pole2 is 2m tall; stone walls are 1m tall.
/// </summary>
public class SnapChainTests
{
    /// <summary>Constant-grade ramp climbing toward +y (north).</summary>
    private sealed class RampWorld : WorldGenerator
    {
        public float Grade = 0.5f;
        public override float GetHeight(float wx, float wy) => wy * Grade;
    }

    /// <summary>Ramp with a gully whose walls drop faster than a stair chain
    /// can descend (3.5m over 2.5m against the climb): the chain must span
    /// part of it on support columns.</summary>
    private sealed class GullyWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            float t = Mathf.Abs(wy - 20f);
            return t < 2.5f ? -3.5f * (1f - t / 2.5f) + wy * 0.45f : wy * 0.45f;
        }
    }

    private static StairRun MakeRun(params Vector2[] points)
    {
        var run = new StairRun { FromPos = points[0], ToPos = points[points.Length - 1] };
        run.Points.AddRange(points);
        float len = 0f;
        for (int i = 1; i < points.Length; i++)
            len += Vector2.Distance(points[i - 1], points[i]);
        run.Length = len;
        run.MaxGrade = 0.6f;
        return run;
    }

    [Fact]
    public void StraightChainStepsMateAtSnapEdges()
    {
        var world = new RampWorld { Grade = 0.5f };
        var run = MakeRun(new Vector2(0f, 0f), new Vector2(0f, 40f));
        var pieces = StairLayout.Solve(run, world, 1234, StairStyle.MeadowsWood);

        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep)
            .OrderBy(p => p.Position.z).ToList();
        Assert.True(steps.Count >= 5, $"Expected a long chain, got {steps.Count} steps");

        var style = StairStyle.MeadowsWood;
        for (int i = 1; i < steps.Count; i++)
        {
            var a = steps[i - 1];
            var b = steps[i];
            float dz = b.Position.z - a.Position.z;
            if (dz > style.StepRun + 0.01f)
                continue; // ruin gap — chain broken on purpose

            // On a straight run every joint mates: one run forward, one rise up.
            Assert.Equal(style.StepRun, dz, 2);
            Assert.Equal(style.StepRise, b.Position.y - a.Position.y, 2);
            Assert.Equal(a.YawDegrees, b.YawDegrees, 1);
            Assert.Equal(a.Position.x, b.Position.x, 2);
        }
    }

    [Fact]
    public void SteepTerrainStacksStepsAtHalfRun()
    {
        var world = new RampWorld { Grade = 1.0f };
        var run = MakeRun(new Vector2(0f, 0f), new Vector2(0f, 30f));
        var pieces = StairLayout.Solve(run, world, 7, StairStyle.MountainStone);

        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep)
            .OrderBy(p => p.Position.z).ToList();
        Assert.True(steps.Count >= 5);

        // Grade 1.0 cannot be climbed at 2m/rise; the chain must stack at
        // half-run spacing (the vanilla steep-stair pattern) at least once,
        // and consecutive live steps never rise more than one piece rise.
        var style = StairStyle.MountainStone;
        bool sawStacked = false;
        for (int i = 1; i < steps.Count; i++)
        {
            float dz = steps[i].Position.z - steps[i - 1].Position.z;
            float dy = steps[i].Position.y - steps[i - 1].Position.y;
            if (dz <= style.StepRun * 0.5f + 0.01f)
                sawStacked = true;
            // Ruin gaps make spacing vary; the invariant is the chain's max
            // grade of one rise per half-run (never steeper than stacked).
            if (dz <= style.StepRun + 0.01f)
                Assert.True(dy <= dz + 0.01f, $"Step {i} rises {dy:F2} over {dz:F2}m");
        }
        Assert.True(sawStacked, "Steep ramp never produced stacked steps");
    }

    [Fact]
    public void HardTurnHappensOnALanding()
    {
        var world = new RampWorld { Grade = 0.45f };
        // Switchback: climb north, hairpin, climb north-east.
        var run = MakeRun(
            new Vector2(0f, 0f), new Vector2(0f, 16f),
            new Vector2(6f, 18f), new Vector2(14f, 26f));
        var pieces = StairLayout.Solve(run, world, 99, StairStyle.MeadowsWood);

        Assert.Contains(pieces, p => p.Kind == BridgePieceKind.Landing);

        // Wherever two live steps are adjacent in the chain, their headings
        // differ by at most the per-joint pivot allowance.
        var style = StairStyle.MeadowsWood;
        var steps = pieces.Where(p => p.Kind == BridgePieceKind.StairStep).ToList();
        for (int i = 1; i < steps.Count; i++)
        {
            float dist = Vector2.Distance(
                new Vector2(steps[i].Position.x, steps[i].Position.z),
                new Vector2(steps[i - 1].Position.x, steps[i - 1].Position.z));
            if (dist > style.StepRun + 0.01f)
                continue; // gap or landing between them

            float dyaw = Mathf.Abs(Mathf.DeltaAngle(steps[i - 1].YawDegrees, steps[i].YawDegrees));
            if (dyaw > 90f) dyaw = Mathf.Abs(dyaw - 180f); // descending pieces face backward
            Assert.True(dyaw <= style.TurnPerJointDegrees + 0.5f,
                $"Adjacent steps turn {dyaw:F1}° without a landing");
        }
    }

    [Fact]
    public void SupportColumnsAreGroundedByBurial()
    {
        var world = new GullyWorld();
        var run = MakeRun(new Vector2(0f, 0f), new Vector2(0f, 40f));
        var pieces = StairLayout.Solve(run, world, 5, StairStyle.MeadowsWood);

        var supports = pieces.Where(p => p.Kind == BridgePieceKind.StairSupport).ToList();
        Assert.NotEmpty(supports); // the gully forces supported spans

        foreach (var column in supports.GroupBy(p => new Vector2(p.Position.x, p.Position.z)))
        {
            float ground = world.GetHeight(column.Key.x, column.Key.y);
            Assert.True(column.Min(p => p.Position.y) <= ground,
                $"Support column at {column.Key} does not reach into the ground");

            var ys = column.Select(p => p.Position.y).OrderBy(y => y).ToList();
            for (int i = 1; i < ys.Count; i++)
                Assert.True(ys[i] - ys[i - 1] <= StairStyle.MeadowsWood.SupportSegment + 0.01f,
                    $"Air gap inside support column at {column.Key}");
        }
    }

    // ---- bridge assemblies ----

    private sealed class AsymmetricRiverWorld : WorldGenerator
    {
        // Channel along x ∈ [-10, 10]: low bank west (y=31), high bank east
        // (y=34.5) — mismatched, but inside the grade a bridge will serve.
        public override float GetHeight(float wx, float wy)
        {
            if (wx < -10f) return 31f;
            if (wx > 10f) return 34.5f;
            float t = (wx + 10f) / 20f;
            float banks = Mathf.Lerp(31f, 34.5f, t);
            float dip = 6f * (1f - Mathf.Abs(wx) / 10f);
            return banks - dip;
        }
    }

    private static RoadCrossing MakeCrossing()
    {
        var from = new Vector2(-10f, 0f);
        var to = new Vector2(10f, 0f);
        return new RoadCrossing
        {
            FromBank = from,
            ToBank = to,
            Center = (from + to) * 0.5f,
            Direction = (to - from).normalized,
            Width = 20f,
            WaterLevel = 30f,
            RiverbedHeight = 25f,
            FairwayCenter = new Vector2(0f, 0f),
            FairwayWidth = 4f,
        };
    }

    /// <summary>A kit that never loses a piece, for tests about the geometry
    /// of the whole span rather than about how it ruins. Without this the
    /// assertion samples whichever stations happened to survive.</summary>
    private static BridgeStyle Intact(BridgeStyle style) => style with
    {
        BankSurvival = 1f,
        MidSurvival = 1f,
        WeatheringSpread = 0f,
    };

    [Fact]
    public void DeckGradesBetweenBanksInsteadOfStilting()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        crossing.FairwayWidth = 0f; // keep the whole deck line, ruins tested elsewhere
        var style = Intact(BridgeStyle.MeadowsWood);
        var plan = BridgeLayout.Solve(crossing, world, 42, style);

        float bankLow = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
        float bankHigh = world.GetHeight(crossing.ToBank.x, crossing.ToBank.y);
        var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck).ToList();
        Assert.NotEmpty(decks);

        foreach (var deck in decks)
        {
            float surface = deck.Position.y + style.DeckTopOffset;
            float t = (deck.Position.x - crossing.FromBank.x) / crossing.Width;
            float graded = Mathf.Max(Mathf.Lerp(bankLow, bankHigh, t),
                crossing.WaterLevel + style.DeckFreeboard);
            Assert.True(Mathf.Abs(surface - graded) < 0.35f,
                $"Deck at t={t:F2} surface {surface:F1} vs graded line {graded:F1} — stilted");
        }

        // The old bug: every deck at the higher bank's height. With a 5m bank
        // difference the graded deck line must actually vary.
        float spread = decks.Max(d => d.Position.y) - decks.Min(d => d.Position.y);
        Assert.True(spread > 1f, $"Deck line is flat (spread {spread:F2}m) over asymmetric banks");
    }

    [Fact]
    public void WoodStationsArePostPairsWithBeams()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MeadowsWood;
        var plan = BridgeLayout.Solve(crossing, world, 3, style);

        // Post columns sit off the centerline (paired), never on it.
        var columns = plan.Where(p => p.Kind == BridgePieceKind.Piling)
            .GroupBy(p => new Vector2(p.Position.x, p.Position.z)).ToList();
        Assert.NotEmpty(columns);

        var fullColumns = columns
            .Where(c => c.Count() > 1 || c.Any(p => p.Position.y > crossing.WaterLevel + 0.5f))
            .ToList();
        Assert.NotEmpty(fullColumns);
        foreach (var column in fullColumns)
            Assert.Equal(style.PostSideOffset, Mathf.Abs(column.Key.y), 2);

        // Each full station ties its pair with a crossbeam under the deck.
        var beams = plan.Where(p => p.Kind == BridgePieceKind.Beam).ToList();
        Assert.NotEmpty(beams);
        int pairedStations = fullColumns.Select(c => c.Key.x).Distinct().Count();
        Assert.True(beams.Count >= pairedStations / 2,
            $"{pairedStations} paired stations but only {beams.Count} beams");
    }

    /// <summary>Sample the kit actually chosen across many crossings at one
    /// biome/ring, so the weighted draw can be checked as a distribution.</summary>
    private static (int Wood, int Hybrid, int Stone) SampleKits(Heightmap.Biome biome, int ring, int count = 600)
    {
        int wood = 0, hybrid = 0, stone = 0;
        for (int i = 0; i < count; i++)
        {
            // Spread sample points so each draws its own crossing seed.
            Vector2 center = new Vector2(100f + i * 7f, -60f - i * 11f);
            var style = BridgeStyleSelection.StyleFor(biome, ring, 1337, center);
            if (ReferenceEquals(style, BridgeStyle.MeadowsWood)) wood++;
            else if (ReferenceEquals(style, BridgeStyle.StoneAndTimber)) hybrid++;
            else stone++;
        }
        return (wood, hybrid, stone);
    }

    [Fact]
    public void BridgeMaterialFollowsProgressionNotBiomeAlone()
    {
        // The same Black Forest river: timber near spawn, stonework on the rim.
        var inner = SampleKits(Heightmap.Biome.BlackForest, 0);
        var outer = SampleKits(Heightmap.Biome.BlackForest, 2);
        Assert.True(inner.Wood > inner.Stone * 2,
            $"Inner Black Forest should be mostly wood (got {inner.Wood}w/{inner.Stone}s)");
        Assert.True(outer.Stone > inner.Stone,
            $"Stone must become commoner outward ({inner.Stone} -> {outer.Stone})");

        // Mountain is stone country; Plains climbs to it as the ring rises.
        var mountain = SampleKits(Heightmap.Biome.Mountain, 1);
        Assert.True(mountain.Stone > mountain.Wood * 2,
            $"Mountain should be mostly stone (got {mountain.Stone}s/{mountain.Wood}w)");
        Assert.True(SampleKits(Heightmap.Biome.Plains, 2).Stone > SampleKits(Heightmap.Biome.Plains, 0).Stone,
            "Plains should turn to stone further out");

        // Swamp is the declared middle ground: neither material dominates.
        var swamp = SampleKits(Heightmap.Biome.Swamp, 1);
        Assert.True(swamp.Wood > swamp.Stone / 2 && swamp.Stone > swamp.Wood / 2,
            $"Swamp should be a genuine mix (got {swamp.Wood}w/{swamp.Hybrid}h/{swamp.Stone}s)");
    }

    [Fact]
    public void EveryPositionKeepsAChanceOfBothMaterials()
    {
        // The world should never turn into one kit per region: a stone bridge
        // near spawn and a timber one on the frontier both stay possible.
        foreach (Heightmap.Biome biome in new[]
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Mistlands,
        })
        {
            for (int ring = 0; ring <= 2; ring++)
            {
                var w = BridgeStyleSelection.Weights(BridgeStyleSelection.Advancement(biome, ring));
                Assert.True(w.Wood >= BridgeStyleSelection.MinorityFloor - 0.001f,
                    $"{biome} ring {ring}: wood share {w.Wood:F3} under the floor");
                Assert.True(w.Stone >= BridgeStyleSelection.MinorityFloor - 0.001f,
                    $"{biome} ring {ring}: stone share {w.Stone:F3} under the floor");
                Assert.Equal(1f, w.Wood + w.Hybrid + w.Stone, 3);

                // And the floor is reachable in practice, not just on paper.
                var sample = SampleKits(biome, ring);
                Assert.True(sample.Wood > 0 && sample.Stone > 0 && sample.Hybrid > 0,
                    $"{biome} ring {ring}: a kit never appeared ({sample.Wood}w/{sample.Hybrid}h/{sample.Stone}s)");
            }
        }
    }

    [Fact]
    public void KitChoiceIsStablePerCrossingButVariesBetweenThem()
    {
        Vector2 a = new(4000f, 1200f);
        Vector2 b = new(4120f, 1160f);

        // Same crossing, same world: the bridge does not change under you.
        Assert.Same(
            BridgeStyleSelection.StyleFor(Heightmap.Biome.Swamp, 1, 99, a),
            BridgeStyleSelection.StyleFor(Heightmap.Biome.Swamp, 1, 99, a));

        // Different worlds disagree about the same spot.
        bool differsBySeed = false;
        for (int seed = 0; seed < 40 && !differsBySeed; seed++)
            differsBySeed = !ReferenceEquals(
                BridgeStyleSelection.StyleFor(Heightmap.Biome.Swamp, 1, seed, a),
                BridgeStyleSelection.StyleFor(Heightmap.Biome.Swamp, 1, 99, a));
        Assert.True(differsBySeed, "Kit choice ignores the world seed");

        // Neighbouring crossings are drawn independently.
        bool differsByPlace = false;
        for (int seed = 0; seed < 40 && !differsByPlace; seed++)
            differsByPlace = !ReferenceEquals(
                BridgeStyleSelection.StyleFor(Heightmap.Biome.Swamp, 1, seed, a),
                BridgeStyleSelection.StyleFor(Heightmap.Biome.Swamp, 1, seed, b));
        Assert.True(differsByPlace, "Neighbouring crossings always get the same kit");
    }

    [Fact]
    public void BridgesOfOneKitDecayOnTheirOwnSchedules()
    {
        var world = new AsymmetricRiverWorld();
        var style = BridgeStyle.MountainStone;

        // Same kit, same world, different crossings: the ruin pattern must
        // differ, or every bridge of a kit looks ruined the same way.
        var survivalCounts = new List<int>();
        for (int i = 0; i < 12; i++)
        {
            var crossing = MakeCrossing();
            crossing.Center = new Vector2(i * 137f, i * -91f);
            var plan = BridgeLayout.Solve(crossing, world, 2024, style);
            survivalCounts.Add(plan.Count(p => p.Kind == BridgePieceKind.Piling));
        }
        Assert.True(survivalCounts.Distinct().Count() > 3,
            $"Bridges decay identically across crossings: {string.Join(",", survivalCounts)}");

        // But one crossing is still reproducible — decay is authored, not random.
        var fixedCrossing = MakeCrossing();
        var first = BridgeLayout.Solve(fixedCrossing, world, 2024, style);
        var again = BridgeLayout.Solve(fixedCrossing, world, 2024, style);
        Assert.Equal(first.Count, again.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Position, again[i].Position);
    }

    /// <summary>Channel along x ∈ [-10, 10] with both banks at the same
    /// height: a level deck line, so pier/deck heights compare exactly.</summary>
    private sealed class LevelRiverWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 10f) return 34f;
            return 34f - 6f * (1f - Mathf.Abs(wx) / 10f);
        }
    }

    /// <summary>Both banks well below the deck's water clamp, so the deck
    /// cannot come down to meet the road: the approach must climb.</summary>
    private sealed class LowBankWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 10f) return 30.4f;   // barely above the water
            return 30.4f - 5f * (1f - Mathf.Abs(wx) / 10f);
        }
    }

    [Fact]
    public void ApproachStepsClimbFromRoadGradeUpOntoTheDeck()
    {
        var world = new LowBankWorld();
        var crossing = MakeCrossing();
        // Freeboard lifts the deck above both banks, so a lip would otherwise
        // appear exactly where the road paint meets the bridge.
        var style = Intact(BridgeStyle.MountainStone) with { DeckFreeboard = 2.5f };
        var plan = BridgeLayout.Solve(crossing, world, 7, style);

        float deckAtBank = plan.Where(p => p.Kind == BridgePieceKind.Deck)
            .OrderBy(d => Mathf.Abs(d.Position.x - crossing.FromBank.x))
            .First().Position.y + style.DeckTopOffset;

        var steps = plan.Where(p => p.Kind == BridgePieceKind.StairStep)
            .Where(p => p.Position.x < crossing.FromBank.x + 1f)   // the west approach
            .OrderBy(p => p.Position.y).ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, s => Assert.Equal(style.StairPrefab, s.Prefab));

        // Each step is one rise above the last, and they march outward from
        // the bridge as they descend — a ramp, not a stack.
        for (int i = 1; i < steps.Count; i++)
        {
            Assert.Equal(style.StairRise, steps[i].Position.y - steps[i - 1].Position.y, 2);
            Assert.True(steps[i].Position.x > steps[i - 1].Position.x,
                "Steps do not advance toward the bridge as they climb");
        }

        // The chain reaches the deck at the top and digs into the bank at the
        // bottom, which is allowed: buried beats leaving a lip.
        float topStep = steps.Last().Position.y + style.StairRise;
        Assert.True(Mathf.Abs(topStep - deckAtBank) < style.StairRise + 0.01f,
            $"Top step lands at {topStep:F2}, deck edge is at {deckAtBank:F2}");
        float bankGround = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
        Assert.True(steps.First().Position.y <= bankGround + 0.01f,
            "Bottom step floats above the bank instead of cutting into it");

        // No step is left hanging: anything standing clear of the bank is
        // carried on a column, the same rule the rest of the grammar follows.
        var columns = plan.Where(p => p.Kind == BridgePieceKind.Piling).ToList();
        foreach (var step in steps)
        {
            Vector2 at = new(step.Position.x, step.Position.z);
            if (step.Position.y <= world.GetHeight(at.x, at.y) + 0.01f)
                continue; // sitting in the ground already

            Assert.Contains(columns, c =>
                Vector2.Distance(new Vector2(c.Position.x, c.Position.z), at) < 0.5f);
        }
    }

    [Fact]
    public void DeckMeetingGradeLapsAnApronUnderTheRoad()
    {
        // Banks high above the water: the deck grades right onto them, so no
        // steps are needed — but the stonework must still reach out under the
        // paint rather than stopping dead at the bank line.
        var world = new LevelRiverWorld();
        var crossing = MakeCrossing();
        var style = Intact(BridgeStyle.MountainStone);
        var plan = BridgeLayout.Solve(crossing, world, 11, style);

        Assert.Empty(plan.Where(p => p.Kind == BridgePieceKind.StairStep));

        var abutments = plan.Where(p => p.Kind == BridgePieceKind.Abutment).ToList();
        foreach (Vector2 bank in new[] { crossing.FromBank, crossing.ToBank })
        {
            Vector2 outward = (bank - crossing.Center).normalized;
            Vector2 expected = bank + outward * style.StairRun;
            Assert.Contains(abutments, a =>
                Vector2.Distance(new Vector2(a.Position.x, a.Position.z), expected) < 0.6f);
        }

        // Everything on the bank sits below grade, so terrain and paint lap
        // over it instead of meeting an edge.
        foreach (var a in abutments)
        {
            float ground = world.GetHeight(a.Position.x, a.Position.z);
            Assert.True(a.Position.y <= ground - style.AbutmentEmbed + 0.01f,
                $"Abutment at {a.Position.y:F2} stands proud of grade {ground:F2}");
        }
    }

    /// <summary>Cliff on one side, beach on the other — the case a bridge
    /// cannot serve without becoming a ramp.</summary>
    private sealed class MismatchedShoreWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (wx < -10f) return 31f;    // low beach
            if (wx > 10f) return 48f;     // clifftop, 17m above it
            float t = (wx + 10f) / 20f;
            return Mathf.Lerp(31f, 48f, t) - 6f * (1f - Mathf.Abs(wx) / 10f);
        }
    }

    [Fact]
    public void MismatchedShoresGetNoBridgeAtAll()
    {
        var crossing = MakeCrossing();

        // Shores that roughly match still carry a bridge.
        Assert.True(BridgeLayout.CanBridge(crossing, new AsymmetricRiverWorld()));
        Assert.NotEmpty(BridgeLayout.Solve(crossing, new AsymmetricRiverWorld(), 42, BridgeStyle.MountainStone));

        // A beach on one side and a cliff on the other does not.
        var cliff = new MismatchedShoreWorld();
        Assert.False(BridgeLayout.CanBridge(crossing, cliff));

        // And nothing is planned there — not a stub, not an abutment. The
        // road simply stops at the water and picks up on the far bank.
        foreach (var style in new[]
        {
            BridgeStyle.MeadowsWood, BridgeStyle.StoneAndTimber, BridgeStyle.MountainStone,
        })
            Assert.Empty(BridgeLayout.Solve(crossing, cliff, 42, style));
    }

    [Fact]
    public void BridgeViabilityUsesGradeNotDropAlone()
    {
        // The same 7m drop is fine across a wide river and hopeless across a
        // narrow one: it is the grade the deck would have to run at.
        var world = new AsymmetricRiverWorld();
        float drop = Mathf.Abs(world.GetHeight(-10f, 0f) - world.GetHeight(10f, 0f));

        var wide = MakeCrossing();
        wide.Width = drop / (BridgeLayout.MaxBankGrade * 0.5f);   // half the limit
        Assert.True(BridgeLayout.CanBridge(wide, world));

        var narrow = MakeCrossing();
        narrow.Width = drop / (BridgeLayout.MaxBankGrade * 2f);   // twice the limit
        Assert.False(BridgeLayout.CanBridge(narrow, world));

        // However gentle the grade, a huge absolute drop is still refused.
        var enormous = MakeCrossing();
        enormous.Width = 500f;
        Assert.False(BridgeLayout.CanBridge(enormous, new MismatchedShoreWorld()));
    }

    [Fact]
    public void HybridKitPutsTimberDeckOnStonePiers()
    {
        // Level banks and no camber: the deck runs flat, so "the pier top
        // tucks under the deck" is an exact comparison rather than one
        // confounded by the deck's own pitch.
        var world = new LevelRiverWorld();
        var crossing = MakeCrossing();
        var plan = BridgeLayout.Solve(crossing, world, 42,
            Intact(BridgeStyle.StoneAndTimber) with { CamberRatio = 0f, CamberMax = 0f });

        var pilings = plan.Where(p => p.Kind == BridgePieceKind.Piling).ToList();
        var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck).ToList();
        Assert.NotEmpty(pilings);
        Assert.NotEmpty(decks);
        Assert.All(pilings, p => Assert.StartsWith("stone_", p.Prefab));
        Assert.All(decks, d => Assert.StartsWith("wood_", d.Prefab));

        // The timber deck rides on top of the stonework carrying it: at every
        // station, the tallest pier's top tucks under the deck it supports.
        var style = Intact(BridgeStyle.StoneAndTimber) with { CamberRatio = 0f, CamberMax = 0f };
        foreach (var deck in decks)
        {
            Vector2 deckPos = new(deck.Position.x, deck.Position.z);
            var carrying = pilings
                .Where(p => Vector2.Distance(new Vector2(p.Position.x, p.Position.z), deckPos) <= style.DeckSpan)
                .ToList();
            Assert.NotEmpty(carrying);

            float pierTop = carrying.Max(p => p.Position.y) + style.PilingSegment * 0.5f;
            float deckSurface = deck.Position.y + style.DeckTopOffset;
            Assert.True(pierTop <= deckSurface + 0.01f,
                $"Pier top {pierTop:F2} pokes through the deck surface {deckSurface:F2}");
        }
    }

    [Fact]
    public void StoneDeckCambersWithoutLiftingOffTheBanks()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MountainStone;
        var plan = BridgeLayout.Solve(crossing, world, 42, style);

        var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck)
            .OrderBy(p => p.Position.x).ToList();
        Assert.NotEmpty(decks);

        float bankFrom = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
        float bankTo = world.GetHeight(crossing.ToBank.x, crossing.ToBank.y);
        float expectedRise = Mathf.Min(crossing.Width * style.CamberRatio, style.CamberMax);
        Assert.True(expectedRise > 0.1f, "Stone kit should camber at this span");

        // Every deck rides the sine bow exactly: raised above the straight
        // graded line, by the rise the profile calls for at its own station
        // pair. Half a deck span either side of the piece's midpoint.
        float halfT = style.DeckSpan * 0.5f / crossing.Width;
        float peak = 0f;
        foreach (var deck in decks)
        {
            float t = (deck.Position.x - crossing.FromBank.x) / crossing.Width;
            float straight = Mathf.Lerp(bankFrom, bankTo, t);
            float rise = deck.Position.y + style.DeckTopOffset - straight;
            float expected = expectedRise * 0.5f
                * (Mathf.Sin((t - halfT) * Mathf.PI) + Mathf.Sin((t + halfT) * Mathf.PI));

            Assert.True(rise >= -0.01f,
                $"Cambered deck at t={t:F2} sags {-rise:F2}m below the graded line");
            Assert.True(rise <= expectedRise + 0.01f,
                $"Camber at t={t:F2} rises {rise:F2}m, over the {expectedRise:F2}m design rise");
            Assert.Equal(expected, rise, 2);
            peak = Mathf.Max(peak, rise);
        }

        // Whatever survived, the surviving deck nearest mid-span is genuinely
        // lifted — the bow is visible, not a rounding artifact.
        Assert.True(peak > 0.2f, $"Camber barely rises ({peak:F2}m at its highest surviving deck)");

        // Wood kits stay flat — the camber is a stone-kit idiom.
        var woodPlan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);
        foreach (var deck in woodPlan.Where(p => p.Kind == BridgePieceKind.Deck))
        {
            float t = (deck.Position.x - crossing.FromBank.x) / crossing.Width;
            float straight = Mathf.Max(Mathf.Lerp(bankFrom, bankTo, t),
                crossing.WaterLevel + BridgeStyle.MeadowsWood.DeckFreeboard);
            Assert.Equal(straight, deck.Position.y + BridgeStyle.MeadowsWood.DeckTopOffset, 1);
        }
    }

    [Fact]
    public void StoneAbutmentsSpringGroundedArches()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MountainStone;
        var plan = BridgeLayout.Solve(crossing, world, 42, style);

        var arches = plan.Where(p => p.Kind == BridgePieceKind.Arch).ToList();
        Assert.NotEmpty(arches); // both banks stand clear of the water here
        Assert.True(arches.Count <= 2, "At most one arch per bank");

        foreach (var arch in arches)
        {
            // Long axis parallel to the crossing direction.
            float yawRad = arch.YawDegrees * Mathf.PI / 180f;
            Vector2 axis = new(Mathf.Cos(yawRad), -Mathf.Sin(yawRad));
            float align = Mathf.Abs(Vector2.Dot(axis, crossing.Direction));
            Assert.True(align > 0.99f, $"Arch axis misaligned (|dot|={align:F3})");

            // The tall face (local +x end) is buried into the bank: its top
            // sits at or below grade at the face position, grounding the piece.
            Vector2 facePos = new Vector2(arch.Position.x, arch.Position.z) + axis * 1f;
            float faceGround = world.GetHeight(facePos.x, facePos.y);
            float archTop = arch.Position.y + 0.5f;
            Assert.True(archTop <= faceGround + 0.01f,
                $"Arch top {archTop:F2} above grade {faceGround:F2} at the bank face — not grounded");

            // And the tapered end reaches inward over lower ground, not into the bank.
            Vector2 tipPos = new Vector2(arch.Position.x, arch.Position.z) - axis * 1f;
            float tipGround = world.GetHeight(tipPos.x, tipPos.y);
            Assert.True(tipGround < faceGround + 0.01f,
                "Arch points into rising ground instead of out over the water");
        }

        // Wood kits have no arch prefab and must emit none.
        var woodPlan = BridgeLayout.Solve(crossing, world, 42, BridgeStyle.MeadowsWood);
        Assert.DoesNotContain(woodPlan, p => p.Kind == BridgePieceKind.Arch);
    }

    [Fact]
    public void StoneStationsStackWallsWithoutAirGaps()
    {
        var world = new AsymmetricRiverWorld();
        var crossing = MakeCrossing();
        var style = BridgeStyle.MountainStone;
        var plan = BridgeLayout.Solve(crossing, world, 11, style);

        var columns = plan.Where(p => p.Kind == BridgePieceKind.Piling)
            .GroupBy(p => new Vector2(p.Position.x, p.Position.z)).ToList();
        Assert.NotEmpty(columns);

        foreach (var column in columns)
        {
            float ground = world.GetHeight(column.Key.x, column.Key.y);
            Assert.True(column.Min(p => p.Position.y) <= ground,
                $"Stone pier at {column.Key} not buried");
            var ys = column.Select(p => p.Position.y).OrderBy(y => y).ToList();
            for (int i = 1; i < ys.Count; i++)
                Assert.True(ys[i] - ys[i - 1] <= style.PilingSegment + 0.01f,
                    $"1m walls stacked with an air gap at {column.Key}");
        }
    }
}
