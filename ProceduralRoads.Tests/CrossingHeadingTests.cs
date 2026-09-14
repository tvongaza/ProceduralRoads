using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The heading constraint at the PLANNING level: what happens to a crossing
/// whose site has no line the vanilla hammer could build on.
///
/// The rule these tests hold to is that an unturnable heading never deletes a
/// crossing. The turn is decided in the detector, after the pathfinder has
/// accepted and priced the jump, and nothing re-searches afterwards -- so a
/// crossing dropped here does not move the road, it leaves the accepted path
/// painted as ordinary road over open water. A ford drops Span instead; a
/// bridge keeps the bearing the router priced and is reported as unturnable.
/// </summary>
public class CrossingHeadingTests
{
    /// <summary>A gully running north-south, land either side, crossed at
    /// whatever bearing the caller's road takes.</summary>
    private sealed class GullyWorld : WorldGenerator
    {
        public float Bed = 26f;
        public float HalfWidth = 12f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 400f || Mathf.Abs(wy) > 400f) return 20f;
            return Mathf.Abs(wx) < HalfWidth ? Bed : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / (HalfWidth * 2f));
            width = weight > 0f ? HalfWidth * 4f : 0f;
        }
    }

    /// <summary>Two land patches too narrow for a bank to be re-found on any
    /// turned line: the reviewer's shape, which is what makes it unturnable.
    /// Offsetting the far patch to make the road's own bearing off-grid does
    /// not work -- the crossing scan only walks the eight unit grid directions
    /// (see ACrossingScanOnlyWalksTheEightGridDirections), so the router cannot
    /// reach it at all.</summary>
    private sealed class NarrowBanksWorld : WorldGenerator
    {
        private static bool Land(float x, float z) =>
            Mathf.Abs(z) < 0.6f && (Mathf.Abs(x) < 0.6f || Mathf.Abs(x - 32f) < 0.6f);
        public override float GetHeight(float x, float z) => Land(x, z) ? 33f : 26f;
        public override Heightmap.Biome GetBiome(float x, float z) => Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float x, float z, out float weight, out float width)
        {
            weight = Land(x, z) ? 0f : 1f;
            width = 32f;
        }
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private static void TearDownGeneration()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        RoadCrossingDetector.SetFordStyleWeights(1f, 1f);
        WorldGenerator.instance = null;
    }

    // ---- the defect: an accepted route losing its bridge ----

    [Fact]
    public void AnUnturnableSiteKeepsTheBridgeTheRouterPriced()
    {
        // The pathfinder accepts this jump and prices it; the detector cannot
        // find a turned line, because neither bank is wide enough to re-find.
        // Dropping the crossing here would leave the accepted path painted as
        // ordinary road across 32 m of river.
        var world = new NarrowBanksWorld();
        var router = new RoadPathfinder(world) { Bridges = true, Fords = false };
        var path = router.FindPath(new Vector2(0f, 0f), new Vector2(32f, 0f));
        Assert.NotNull(path);

        var crossing = Assert.Single(RoadCrossingDetector.Detect(path!, world, bridges: true, fords: false));
        Assert.Equal(CrossingKind.Bridge, crossing.Kind);

        // It kept the line the router priced: the banks are the ones on the
        // accepted jump, not banks re-found somewhere else.
        Assert.True(Vector2.Distance(crossing.FromBank, new Vector2(0f, 0f)) < 2f, $"near bank at {crossing.FromBank}");
        Assert.True(Vector2.Distance(crossing.ToBank, new Vector2(32f, 0f)) < 2f, $"far bank at {crossing.ToBank}");
        // This fixture's retained bearing happens to be 90 deg, which IS on the
        // grid, so it cannot show the unturnable REPORT -- only that the
        // crossing survived, which is the defect being held down here.
    }

    [Fact]
    public void AnUnturnableSpanFordWadesOrRaisesRatherThanVanishing()
    {
        // A span is pieces, so it needs a placeable heading; wading and raising
        // are terrain and need nothing. Given a site that cannot be turned, the
        // ford must change STYLE, not disappear.
        var world = new NarrowBanksWorld();
        RoadCrossingDetector.SetFordStyleWeights(0f, 0f, 1f);   // Span, if it can
        try
        {
            var router = new RoadPathfinder(world) { Bridges = true, Fords = true };
            var path = router.FindPath(new Vector2(0f, 0f), new Vector2(32f, 0f));
            Assert.NotNull(path);

            var crossing = Assert.Single(RoadCrossingDetector.Detect(path!, world, bridges: true, fords: true));
            Assert.NotEqual(FordStyle.Span, crossing.Style);
        }
        finally { RoadCrossingDetector.SetFordStyleWeights(1f, 1f, 1f); }
    }

    // ---- the turn itself, and what the road does about it ----

    [Fact]
    public void AGenuinelyTurnedCrossingStillHasRoadAtBothOfItsMovedBanks()
    {
        var world = new GullyWorld();
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        SetPathfinder(new RoadPathfinder(world) { Bridges = true, Fords = false });
        try
        {
            // A road at a bearing deliberately between two of the sixteen.
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-60f, -30f), 0f, new Vector2(60f, 30f), 0f, 4f, "turned"));
            var crossing = Assert.Single(RoadNetworkGenerator.GetRoadCrossings());

            float heading = BridgeLayout.YawDegrees(crossing.Direction);
            Assert.True(BridgeLayout.HeadingIsPlaceable(heading),
                $"the crossing stands at {heading:F2}, which no hammer can turn to");

            // The deck is NOT on the road's own end-to-end bearing: the road
            // runs at 63.43 deg and the crossing does not. That is a bridgehead
            // the painter has to bend out to, whether the turn or the grid
            // scan put it there -- and the snap's own turning is proven
            // separately, on a hand-made path, in FordTests
            // (PiecesStandOnAPlaceableHeading_WhileTerrainKeepsTheRoadsOwnBearing).
            float roadBearing = BridgeLayout.YawDegrees(new Vector2(120f, 60f).normalized);
            Assert.False(BridgeLayout.HeadingIsPlaceable(roadBearing), "the fixture road is supposed to be off-grid");
            Assert.True(Mathf.Abs(heading - roadBearing) > 0.5f,
                $"road {roadBearing:F2}, deck {heading:F2}");

            // ... and the road reaches both of the bridgeheads.
            foreach (Vector2 bank in new[] { crossing.FromBank, crossing.ToBank })
                Assert.True(
                    RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(bank.x, 0f, bank.y), 6f).Count > 0,
                    $"no road within 6 m of the moved bank at ({bank.x:F2},{bank.y:F2})");
        }
        finally { TearDownGeneration(); }
    }

    /// <summary>
    /// Why the constraint costs so little: a crossing is found by walking the
    /// EIGHT unit grid directions out of a cell (TryGetRiverCrossing refuses a
    /// knight move outright), so the jump it accepts runs cell-centre to
    /// cell-centre along one of them and its bearing is a multiple of 45
    /// degrees -- already one of the sixteen the hammer can turn to. Nothing
    /// has to be turned unless a later step moves a bank OFF that line (the
    /// bank-top climb walks the road, which can bend).
    ///
    /// This is a fact about the router, not a guarantee about every crossing,
    /// which is why the snap still exists and still has to be correct.
    /// </summary>
    [Theory]
    [InlineData(-30f, 30f)]
    [InlineData(-8f, 40f)]
    [InlineData(0f, 0f)]
    [InlineData(24f, -24f)]
    public void ACrossingScanOnlyWalksTheEightGridDirections(float startZ, float endZ)
    {
        var world = new GullyWorld();
        var router = new RoadPathfinder(world) { Bridges = true, Fords = false };
        var path = router.FindPath(new Vector2(-60f, startZ), new Vector2(60f, endZ));
        Assert.NotNull(path);
        var crossings = RoadCrossingDetector.Detect(path!, world, bridges: true, fords: false);
        Assert.NotEmpty(crossings);

        foreach (var c in crossings)
        {
            Vector2 jump = path![c.ToIndex] - path[c.FromIndex];
            float bearing = BridgeLayout.YawDegrees(jump.normalized);
            float off = Mathf.Abs(bearing % 45f);
            Assert.True(Mathf.Min(off, 45f - off) < 0.01f,
                $"the accepted jump runs at {bearing:F3} deg, which is not a multiple of 45");
        }
    }

    [Fact]
    public void HowFarATurnMovesABridgehead_Measured()
    {
        // The bound quoted in the handoff was half the span times sin(half a
        // step), which assumes the endpoints are rotated rigidly. They are not:
        // the banks are re-found on the terrain along the turned line, and
        // NearestPlaceableHeadings may fall through to a neighbouring heading.
        // So measure it on a fixture instead of asserting the arithmetic.
        var world = new GullyWorld();
        var router = new RoadPathfinder(world) { Bridges = true, Fords = false };
        var path = router.FindPath(new Vector2(-60f, -30f), new Vector2(60f, 30f));
        Assert.NotNull(path);
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path!, world, bridges: true, fords: false));

        // Distance from each bridgehead to the nearest point of the road the
        // router accepted: what the painter has to bend across.
        float worst = 0f;
        foreach (Vector2 bank in new[] { crossing.FromBank, crossing.ToBank })
        {
            float nearest = float.MaxValue;
            for (int i = 1; i < path!.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(bank - a, b - a) / (len * len));
                nearest = Mathf.Min(nearest, Vector2.Distance(bank, Vector2.Lerp(a, b, t)));
            }
            worst = Mathf.Max(worst, nearest);
        }
        // Not a claimed universal bound -- a measured fact about this fixture,
        // kept so a change that moves bridgeheads much further is noticed.
        Assert.True(worst < BridgeLayout.DeckHalfWidth * 4f,
            $"a bridgehead sits {worst:F2} m off the accepted road on this fixture");
    }
}
