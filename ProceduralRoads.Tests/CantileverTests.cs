using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// Stairs on steep faces: turns priced by how steep the ground is where they
/// are made (aim a turn at a tree well). Each rule's test fails with that rule switched off.
/// </summary>
public class CantileverTests
{
    private readonly ITestOutputHelper _out;
    public CantileverTests(ITestOutputHelper output) => _out = output;

    private sealed class RulesOff : IDisposable
    {
        public RulesOff(SteepRules off) => StairLayout.RulesOff = off;
        public void Dispose() => StairLayout.RulesOff = SteepRules.None;
    }

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static void Near(Vector3 expected, Vector3 actual, string what)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.01f, $"{what}: expected {expected}, was {actual}");
    }

    /// <summary>The tree-well rule's price: a switchback on a bench costs what it always did, on steep
    /// ground more, the more so the steeper, up to a cap.</summary>
    [Fact]
    public void SwitchbacksCostMoreOnSteepGround()
    {
        Assert.Equal(0f, StairLayout.SwitchbackSteepPrice(0.3f));
        Assert.Equal(0f, StairLayout.SwitchbackSteepPrice(StairLayout.BenchGrade));
        Assert.True(StairLayout.SwitchbackSteepPrice(2f) > StairLayout.SwitchbackSteepPrice(1.5f));
        Assert.Equal(StairLayout.SwitchbackSteepPrice(StairLayout.SteepCap), StairLayout.SwitchbackSteepPrice(10f), 3);
        using (new RulesOff(SteepRules.TurnSteepness))
            Assert.Equal(0f, StairLayout.SwitchbackSteepPrice(2f));
    }

    /// <summary>
    /// A slope of 0.6 straight up the climb, steeper than a flight, the staircase kept to a strip 9 m
    /// wide (as a road's corridor keeps it), so it must switch back once. Past the strip's east side,
    /// 1.1 m beyond it, the ground falls away sheer, under no piece. Every other way the two sides are
    /// mirror images, and without the rule the search turns on the east side, beside the drop; priced
    /// by the ground round it, the turn moves to the west side, away from the drop ("Switch backs on a
    /// sheer cliff would be hard and I think we should aim for less steep terrain for those").
    /// </summary>
    private static float SlopeWithADropEast(float x, float z) => 0.6f * z - (x > 5.6f ? 20f * (x - 5.6f) : 0f);

    private List<StairPiece> SwitchbackFloors()
    {
        // Its switchback within five stairs of the foot: laid as before StairLayout.FirstFlightStairs, the rule this is about apart.
        SteepRules before = StairLayout.RulesOff;
        StairLayout.RulesOff = before | SteepRules.FirstFlight;
        StairPlan plan;
        try { plan = StairLayout.Build(new Vector2(0f, 0f), new Vector2(0f, 20f), SlopeWithADropEast, allowed: p => Mathf.Abs(p.x) <= 4.5f); }
        finally { StairLayout.RulesOff = before; }
        Assert.True(plan.Reached, plan.Reason);
        Assert.Equal(1, plan.Switchbacks);
        // A switchback's two floors lie side by side at one height.
        var floors = plan.Pieces.Where(p => p.Kind == StairPieceKind.Landing).ToList();
        var pads = floors.Where(f => floors.Exists(o => o != f && Mathf.Abs(o.Position.y - f.Position.y) < 0.01f
            && Mathf.Abs(Vector2.Distance(new Vector2(o.Position.x, o.Position.z), new Vector2(f.Position.x, f.Position.z)) - 2f) < 0.05f)).ToList();
        _out.WriteLine("pads " + string.Join(" ", pads.Select(p => $"({p.Position.x:F1},{p.Position.y:F1},{p.Position.z:F1})")));
        Assert.Equal(2, pads.Count);
        return pads;
    }

    [Fact]
    public void ASwitchbackTurnsAwayFromADrop()
    {
        Assert.All(SwitchbackFloors(), p => Assert.True(p.Position.x < 0f, $"pad at x {p.Position.x}, the drop's side"));
        using (new RulesOff(SteepRules.TurnSteepness))
            Assert.All(SwitchbackFloors(), p => Assert.True(p.Position.x > 0f, $"without the rule the pad was at x {p.Position.x}"));
    }

    /// <summary>No switchback in a staircase's first five stairs from its foot (Hildir_cave 1664,768): up a narrow gully, the first
    /// switchback comes after five stairs; before, three.</summary>
    [Fact]
    public void TheFirstSwitchbackComesFiveStairsUpFromTheFoot()
    {
        int StairsBeforeSwitchback(StairPlan plan)
        {
            int stairs = 0;
            StairPiece? previous = null;
            foreach (StairPiece p in plan.Pieces.Where(p => p.Kind == StairPieceKind.Stair || p.Kind == StairPieceKind.Landing))
            {
                if (previous != null && previous.Kind == StairPieceKind.Landing && p.Kind == StairPieceKind.Landing && previous.YawDegrees == p.YawDegrees
                    && Vector2.Distance(new Vector2(previous.Position.x, previous.Position.z), new Vector2(p.Position.x, p.Position.z)) > 1.5f) return stairs;
                if (p.Kind == StairPieceKind.Stair) stairs++;
                previous = p;
            }
            return int.MaxValue;
        }
        StairPlan Lay() => StairLayout.Build(new Vector2(0f, 0f), new Vector2(0f, 20f), SlopeWithADropEast, allowed: p => Mathf.Abs(p.x) <= 4.5f);
        StairPlan plan = Lay();
        Assert.True(plan.Reached, plan.Reason);
        Assert.True(StairsBeforeSwitchback(plan) >= StairLayout.FirstFlightStairs, $"switched back after {StairsBeforeSwitchback(plan)} stairs");
        StairLayout.RulesOff = SteepRules.FirstFlight;
        try { Assert.True(StairsBeforeSwitchback(Lay()) < StairLayout.FirstFlightStairs, "without the rule it waited anyway"); }
        finally { StairLayout.RulesOff = SteepRules.None; }
    }

}
