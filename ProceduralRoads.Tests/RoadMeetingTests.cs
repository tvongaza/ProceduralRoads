using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;
using Valheim.Testing.Doubles;

namespace ProceduralRoads.Tests;

/// <summary>
/// How a generated road may meet the roads already built: at a junction, at its
/// ends, or along a trunk it shares; never across one, nor beside one at its height.
/// </summary>
public class RoadMeetingTests : IDisposable
{
    private readonly ValheimWorldScope _scope = new();

    public RoadMeetingTests() => RoadNetworkGenerator.Reset();

    public void Dispose()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        _scope.Dispose();
    }

    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    /// <summary>A flat world: a road built north-south, then one east-west across it. The second would cross the first at
    /// grade and is not built (roads that cross are disallowed even if that rejects a POI); one that
    /// ends on the first (a T) is.</summary>
    private sealed class FlatWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Mathf.Abs(wx) > 200f || Mathf.Abs(wy) > 200f ? 0f : 40f;
        public override Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) => Heightmap.Biome.Meadows;
        public override int GetSeed() => 77;
    }

    [Fact]
    public void ARoadDoesNotCrossAnotherButMayMeetIt()
    {
        bool Cross(out bool tee)
        {
            RoadNetworkGenerator.Reset();
            var world = new FlatWorld();
            _scope.WithWorld(world);
            SetPathfinder(new RoadPathfinder(world) { TerrainVarianceThreshold = 1000f, Meander = 0f });
            Assert.True(RoadNetworkGenerator.GenerateRoad(new Vector2(0f, -80f), 0f, new Vector2(0f, 80f), 0f, 4f, "trunk"));
            tee = RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, 10f), 0f, new Vector2(0f, 10f), 0f, 4f, "tee");
            return RoadNetworkGenerator.GenerateRoad(new Vector2(-80f, -30f), 0f, new Vector2(80f, -30f), 0f, 4f, "across");
        }
        Assert.False(Cross(out bool tee), "the road across was built");
        Assert.True(tee, "the road ending on the trunk was refused");
        RoadNetworkGenerator.NoCrossings = false;
        try { Assert.True(Cross(out _), "without the rule the road across was refused anyway"); }
        finally { RoadNetworkGenerator.NoCrossings = true; }
    }
}
