using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Tys (2 Sep 2026, c12 "looks great") via the night plan 2026-09-03 task
/// 1c: bridge ends come in two variants sampled per site — the deck meets
/// the road flush, or sits 0.5-1.5 m above it with steps up at both ends.
/// </summary>
public class SteppedEndTests
{
    [Fact]
    public void SteppedEnds_AppearOnSomeSitesAndAreGrounded()
    {
        // Night plan 2026-09-03 task 1c: per site hash the deck either meets
        // the road flush or sits 0.5-1.5 m above it with steps at both ends.
        // Sites along the river differ only in position, so their hashes
        // sample both variants; every plan stays grounded.
        var world = new SupportModelTests.WideSteppedWorld();
        var style = BridgeStyle.MeadowsWood.WithPierPersistence(0.85f);
        int stepped = 0, flush = 0;
        for (float y = -100f; y <= 100f; y += 8f)
        {
            var path = new List<Vector2>();
            for (float x = -64f; x <= 64f; x += 8f)
                if (Mathf.Abs(x) >= 48f) path.Add(new Vector2(x, y)); // dry cells only; the middle is the jump
            var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));
            float rise = BridgeLayout.SteppedEndRise(crossing);
            var plan = BridgeLayout.Solve(crossing, world, 7, style);
            SupportModelTests.AssertGrounded(plan, style, world, $"stepped ends y={y} rise {rise:F2}");

            float bankH = world.GetHeight(crossing.FromBank.x, crossing.FromBank.y);
            var endDeck = plan.Where(p => p.Kind == BridgePieceKind.Deck)
                .OrderBy(p => Vector2.Distance(new Vector2(p.Position.x, p.Position.z), crossing.FromBank)).First();
            if (rise > 0f)
            {
                stepped++;
                Assert.InRange(rise, RoadConstants.SteppedEndMinRise, RoadConstants.SteppedEndMaxRise);
                Assert.Contains(plan, p => p.Kind == BridgePieceKind.Stair);
                Assert.InRange(endDeck.Position.y - bankH, rise - 0.3f, rise + 0.3f);
            }
            else
            {
                flush++;
                Assert.DoesNotContain(plan, p => p.Kind == BridgePieceKind.Stair);
                Assert.InRange(endDeck.Position.y - bankH, -0.3f, 0.3f);
            }
        }
        Assert.True(stepped > 0 && flush > 0, $"stepped {stepped}, flush {flush}: both variants must appear");
    }

}
