using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The strategy switch: the mod's shipped policy is the control and is
/// untouched, PR #16's policy is a candidate selected by
/// <see cref="RoadNetworkGenerator.Strategy"/>, and the differences between
/// them are the ones the study means to measure.
/// </summary>
public class StrategySwitchTests
{
    /// <summary>Dry meadows with one round pond, deep and river-weighted, at
    /// (200, 0): a place whose centre no road can stand on. The land is kept
    /// small on purpose - an unreachable destination is only reported as
    /// unreachable once the search has settled every cell it can get to, so a
    /// larger field would exhaust the iteration budget first and report that
    /// instead.</summary>
    private sealed class PondWorld : WorldGenerator
    {
        public static readonly Vector2 PondCentre = new(200f, 0f);
        public const float PondRadius = 24f;

        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 300f || Mathf.Abs(wy) > 300f) return 20f;
            return Vector2.Distance(new Vector2(wx, wy), PondCentre) < PondRadius ? 24f : 34f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            float d = Vector2.Distance(new Vector2(wx, wy), PondCentre);
            weight = d < PondRadius ? 1f : 0f;
            width = weight > 0f ? PondRadius * 2f : 0f;
        }
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private static void TearDownGeneration()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        RoadNetworkGenerator.Strategy = RoadNetworkStrategy.Shipped;
        WorldGenerator.instance = null;
    }

    [Fact]
    public void TheShippedStrategyIsWhatRunsUnlessAStudyRunSaysOtherwise()
    {
        Assert.Equal(RoadNetworkStrategy.Shipped, RoadNetworkGenerator.Strategy);
    }

    [Fact]
    public void Pr16SnapsAnEndpointOntoGroundARoadCanStandOnAndTheShippedPolicyDoesNot()
    {
        var world = new PondWorld();
        Vector2 pond = PondWorld.PondCentre;

        // Short enough that the search finishes inside the default budget: a
        // failure here has to be the pond, not an exhausted frontier, and the
        // outcome is asserted below rather than inferred from "no road".
        Vector2 start = new(80f, 0f);

        (bool built, string outcome) Run(RoadNetworkStrategy strategy, float endRadius)
        {
            WorldGenerator.instance = world;
            RoadNetworkGenerator.Reset();
            RoadNetworkGenerator.Strategy = strategy;
            SetPathfinder(new RoadPathfinder(world));
            bool ok = RoadNetworkGenerator.GenerateRoad(start, 0f, pond, endRadius, 4f, "to the pond");
            return (ok, RoadAttemptLog.Attempts[RoadAttemptLog.Attempts.Count - 1].Outcome);
        }

        try
        {
            // The shipped policy asks for a road to the middle of the water and
            // gets nothing: the destination cell is impassable, and the search
            // says so - it emptied its frontier, it did not run out of budget.
            (bool shippedBuilt, string shippedOutcome) = Run(RoadNetworkStrategy.Shipped, 20f);
            Assert.False(shippedBuilt);
            Assert.Equal("no reachable path", shippedOutcome);

            // PR #16 asks for the nearest ground a road can stand on, and the
            // road is built to the pond's shore.
            (bool built, string outcome) = Run(RoadNetworkStrategy.Reachable, 20f);
            Assert.True(built, $"expected a road, the search ended: {outcome}");
            RoadRoute route = Assert.Single(RoadRouteRecorder.Routes);
            Vector3 last = route.Points[route.Points.Count - 1];
            float distanceToCentre = Vector2.Distance(new Vector2(last.x, last.z), pond);
            Assert.True(distanceToCentre >= PondWorld.PondRadius - RoadPathfinder.CellSize,
                $"the road should stop at the shore, it ended {distanceToCentre:F0} m from the centre");
            Assert.True(last.y > RoadConstants.SeaLevel, $"the road ended at height {last.y:F1}");

            // The rescue has a reach: the search runs out at the location's
            // radius plus two cells, so a centre further into the water than
            // that is not saved. Measured behaviour, worth reporting - a
            // shoreline location with a small radius still gets nothing.
            (bool rescued, string smallRadiusOutcome) = Run(RoadNetworkStrategy.Reachable, 0f);
            Assert.False(rescued);
            Assert.Equal("no reachable path", smallRadiusOutcome);
        }
        finally { TearDownGeneration(); }
    }

    [Fact]
    public void Pr16sQuotaPrefersTheNearerPlaceAmongEqualPriorities()
    {
        // Measured behaviour worth reporting: the quota's distance term is a
        // penalty that grows with distance from what is already chosen, so it
        // packs the selection together rather than spreading it.
        MethodInfo method = typeof(RoadNetworkGenerator).GetMethod(
            "SelectLocationsPriorityThenNearest", BindingFlags.NonPublic | BindingFlags.Static)!;

        List<(string name, Vector3 position, float radius)> candidates = new()
        {
            ("Eikthyrnir", new Vector3(0f, 0f, 0f), 20f),      // priority 100, the anchor
            ("WoodVillage1", new Vector3(100f, 0f, 0f), 20f),  // same priority as the far one
            ("WoodVillage1", new Vector3(1800f, 0f, 0f), 20f),
        };

        var selected = (List<(string name, Vector3 position, float radius)>)
            method.Invoke(null, new object[] { candidates, 2 })!;

        Assert.Equal(2, selected.Count);
        Assert.Equal("Eikthyrnir", selected[0].name);
        Assert.Equal(100f, selected[1].position.x);
    }

    [Fact]
    public void Pr16BalancesIslandsOverRingsWhereTheShippedPolicyTakesTheLargest()
    {
        MethodInfo select = typeof(RoadNetworkGenerator).GetMethod(
            "SelectBalancedIslands", BindingFlags.NonPublic | BindingFlags.Static)!;
        System.Type candidateType = typeof(RoadNetworkGenerator)
            .GetNestedType("IslandCandidate", BindingFlags.NonPublic)!;

        object Candidate(int id, float area, int ring)
        {
            object candidate = System.Activator.CreateInstance(candidateType)!;
            Island island = new() { Id = id };
            typeof(Island).GetProperty("ApproxArea")?.SetValue(island, area);
            typeof(Island).GetField("ApproxArea")?.SetValue(island, area);
            candidateType.GetField("Island")!.SetValue(candidate, island);
            candidateType.GetField("Ring")!.SetValue(candidate, ring);
            return candidate;
        }

        // Four islands: the two largest are both in the outer ring.
        System.Collections.IList candidates =
            (System.Collections.IList)System.Activator.CreateInstance(
                typeof(List<>).MakeGenericType(candidateType))!;
        candidates.Add(Candidate(1, 9_000_000f, 2));
        candidates.Add(Candidate(2, 8_000_000f, 2));
        candidates.Add(Candidate(3, 3_000_000f, 0));
        candidates.Add(Candidate(4, 2_000_000f, 1));

        object result = select.Invoke(null, new object[] { candidates, 50 })!;
        List<int> ids = new();
        foreach (object candidate in (System.Collections.IEnumerable)result)
        {
            Island island = (Island)candidateType.GetField("Island")!.GetValue(candidate)!;
            ids.Add(island.Id);
        }

        // Two islands for a 50% quota, taken one per ring from the inside
        // out: the shipped policy's largest-first would have taken 1 and 2,
        // both in the outer ring. Neither of them is selected here, which is
        // the whole point of the change - and also means the largest island
        // in the world can go without roads. Measured, for the author.
        Assert.Equal(new[] { 3, 4 }, ids.ToArray());
    }
}
