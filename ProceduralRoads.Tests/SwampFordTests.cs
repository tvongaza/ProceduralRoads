using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tys (2026-09-02, c6/c7) via the night plan 2026-09-03 task 1b: swamp
/// crossings at wading depth get the wade / raise / span mix instead of a
/// bridge (RoadTestMac2 c5/c6: 60 m swamp "bridges" over a 28.5 bed with a
/// 3-7 m dip of sailable depth). A swamp channel that is sailable for a
/// boat's length or deeper than wading depth stays a bridge.
/// </summary>
public class SwampFordTests
{
    /// <summary>Flat swamp at 33 with a channel around x = 0: bed height,
    /// half width and an optional deeper dip of DipHalfWidth around x = 0.</summary>
    private sealed class SwampChannelWorld : WorldGenerator
    {
        public float Bed = 29f;
        public float HalfWidth = 10f;
        public float DipBed = 29f;
        public float DipHalfWidth = 0f;
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 100f || Mathf.Abs(wy) > 4100f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax < DipHalfWidth) return DipBed;
            return ax < HalfWidth ? Bed : 33f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < 22f ? Heightmap.Biome.Ocean : Heightmap.Biome.Swamp;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / (HalfWidth * 2f));
            width = weight > 0f ? HalfWidth * 4f : 0f;
        }
    }

    private static List<Vector2> Path(float y) =>
        new() { new(-40f, y), new(-32f, y), new(-24f, y), new(24f, y), new(32f, y), new(40f, y) };

    [Fact]
    public void WadingDepthSwampChannelIsAFordInEveryStyle()
    {
        // Bed 29.0 (1 m of water: too deep to wade outside a swamp, not yet
        // sailable) with a 3 m dip to 28.2 (sailable, shorter than a boat) — c5/c6.
        var world = new SwampChannelWorld { Bed = 29f, DipBed = 28.2f, DipHalfWidth = 1.5f };
        var styles = new HashSet<FordStyle>();
        for (float y = 0f; y < 4000f; y += 40f)
        {
            var crossing = Assert.Single(RoadCrossingDetector.Detect(Path(y), world));
            Assert.Equal(CrossingKind.Ford, crossing.Kind);
            Assert.NotEqual(FordStyle.None, crossing.Style);
            // Wading-depth swamp is road: the ford runs bank to bank along the
            // jump (the whole channel is wadeable, the banks are its ends).
            Assert.Equal(2, crossing.FromIndex);
            Assert.Equal(3, crossing.ToIndex);
            styles.Add(crossing.Style);
        }
        Assert.Contains(FordStyle.Wade, styles);
        Assert.Contains(FordStyle.Raise, styles);
        Assert.Contains(FordStyle.Span, styles);
    }

    [Fact]
    public void SailableOrDeepSwampChannelStaysABridge()
    {
        // A boat's length of sailable depth: sailing is sacred.
        var sailable = new SwampChannelWorld { Bed = 29f, DipBed = 28.6f, DipHalfWidth = 5f };
        var bridge = Assert.Single(RoadCrossingDetector.Detect(Path(0f), sailable));
        Assert.Equal(CrossingKind.Bridge, bridge.Kind);
        Assert.True(bridge.FairwayWidth >= RoadConstants.SwampFordMaxFairway);

        // Deeper than wading depth anywhere: a bridge. With dry land at 33
        // only 6 m from the wadeable bank at 29 the high-bridge rule fires
        // (a 4 m rise within reach) and the deck springs from the dry edge —
        // abutments out of the swamp water, the shelf under the deck.
        var deep = new SwampChannelWorld { Bed = 29f, DipBed = 26f, DipHalfWidth = 4f };
        var deepBridge = Assert.Single(RoadCrossingDetector.Detect(Path(0f), deep));
        Assert.Equal(CrossingKind.Bridge, deepBridge.Kind);
        Assert.InRange(deepBridge.Width, 19f, 21f); // dry edge to dry edge, not the 48 m jump
        Assert.Equal(2, deepBridge.FromIndex);
        Assert.Equal(3, deepBridge.ToIndex);

        // With a wide wade shelf (dry land 26 m away, beyond HighBankReach)
        // the bridge still starts and ends on land above the water (Tys,
        // 3 Sep 2026): its banks walk outward over the shelf to the dry
        // edge at |x| = 30, so the deck spans the whole 60 m shelf, not the
        // 8 m dip — and never the 80 m jump.
        var wide = new SwampChannelWorld { Bed = 29f, HalfWidth = 30f, DipBed = 26f, DipHalfWidth = 4f };
        var widePath = new List<Vector2> { new(-56f, 0f), new(-48f, 0f), new(-40f, 0f), new(40f, 0f), new(48f, 0f), new(56f, 0f) };
        var wideBridge = Assert.Single(RoadCrossingDetector.Detect(widePath, wide));
        Assert.Equal(CrossingKind.Bridge, wideBridge.Kind);
        Assert.InRange(wideBridge.Width, 59f, 62f);
        float dryFloor = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        Assert.True(wide.GetHeight(wideBridge.FromBank.x, 0f) >= dryFloor, "FromBank stands in the water");
        Assert.True(wide.GetHeight(wideBridge.ToBank.x, 0f) >= dryFloor, "ToBank stands in the water");
        Assert.InRange(wideBridge.RiverbedHeight, 25.9f, 26.1f); // the profile still knows the dip
    }

    /// <summary>Regression (RoadTestMac2 c33, 3 Sep 2026): where the approach
    /// bends on the wade shelf, the dry-bank walk must go straight out along
    /// the crossing line, not along the path — a path walk carried the far
    /// abutment 8 m off the deck chord and left 11 route points over the
    /// channel outside the recorded span.</summary>
    [Fact]
    public void DryBankWalkFollowsTheCrossingLineNotABentApproach()
    {
        var wide = new SwampChannelWorld { Bed = 29f, HalfWidth = 30f, DipBed = 26f, DipHalfWidth = 4f };
        var bent = new List<Vector2> { new(-56f, 0f), new(-48f, 0f), new(-40f, 0f), new(20f, 0f), new(28f, 8f), new(36f, 16f), new(48f, 16f), new(56f, 16f) };
        // The bend splits the wet run: the channel crossing, then two short
        // shelf fords along the bent leg (wade shelf = road, task 1b).
        var crossings = RoadCrossingDetector.Detect(bent, wide);
        var bridge = crossings.OrderByDescending(x => x.Width).First();
        Assert.Equal(CrossingKind.Bridge, bridge.Kind);
        Assert.All(crossings.Where(x => x != bridge), x => Assert.Equal(CrossingKind.Ford, x.Kind));
        float dryFloor = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        Assert.True(wide.GetHeight(bridge.FromBank.x, bridge.FromBank.y) >= dryFloor, "FromBank stands in the water");
        Assert.True(wide.GetHeight(bridge.ToBank.x, bridge.ToBank.y) >= dryFloor, "ToBank stands in the water");
        // Both abutments sit on the channel's own line (y = 0), the deck is straight along it.
        Assert.Equal(0f, bridge.FromBank.y, 1);
        Assert.Equal(0f, bridge.ToBank.y, 1);
        Assert.Equal(1f, bridge.Direction.x, 3);
        Assert.InRange(bridge.Width, 59f, 62f);
    }

    [Fact]
    public void OutsideSwampsTheWadingRulesAreUnchanged()
    {
        // A 28.6 channel in the Meadows is deep water: a bridge with
        // banks above the waterline clearance.
        var world = new MeadowsChannelWorld();
        var crossing = Assert.Single(RoadCrossingDetector.Detect(Path(0f), world));
        Assert.Equal(CrossingKind.Bridge, crossing.Kind);
        float minBank = RoadConstants.ShallowWaterHeight + RoadConstants.WaterlineClearance;
        Assert.True(world.GetHeight(crossing.FromBank.x, crossing.FromBank.y) >= minBank);
        Assert.True(world.GetHeight(crossing.ToBank.x, crossing.ToBank.y) >= minBank);
    }

    private sealed class MeadowsChannelWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Mathf.Abs(wx) < 10f ? 28.6f : 33f;
        public override Heightmap.Biome GetBiome(float wx, float wy) => Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 20f);
            width = weight > 0f ? 40f : 0f;
        }
    }
}
