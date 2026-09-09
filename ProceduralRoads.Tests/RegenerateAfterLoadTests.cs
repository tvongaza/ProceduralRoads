using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Regenerating roads in a world that already has a saved network must start
/// from nothing. The guard on the reset asks whether roads were GENERATED this
/// session, which is false when they were loaded from the save, so the reset
/// was skipped and the new network was laid on top of the old one: the spatial
/// grid kept both sets of points, and with crossings on, the old network's
/// bridges kept their sites.
/// </summary>
public class RegenerateAfterLoadTests
{
    private static void SetPathfinder(RoadPathfinder? pathfinder) =>
        typeof(RoadNetworkGenerator).GetField("m_pathfinder", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, pathfinder);

    private static void MarkLoadedFromSave() =>
        RoadNetworkGenerator.MarkRoadsLoadedFromZDO();

    private static void TearDown()
    {
        SetPathfinder(null);
        RoadNetworkGenerator.Reset();
        WorldGenerator.instance = null;
        ZoneSystem.instance = null;
    }

    /// <summary>Places the generator can actually build a network between,
    /// so a regeneration really regenerates instead of bailing out early -
    /// without them the test would pass on a run that did nothing.</summary>
    private static void GiveTheWorldSomePlaces()
    {
        ZoneSystem zones = new();
        (string name, float x, float z)[] places =
        {
            ("StartTemple", 0f, 0f),
            ("Eikthyrnir", -220f, -120f),
            ("GDKing", 260f, 140f),
            ("Crypt4", -120f, 240f),
            ("SunkenCrypt4", 180f, -260f),
        };
        foreach ((string name, float x, float z) place in places)
        {
            zones.Locations.Add(new ZoneSystem.LocationInstance
            {
                m_position = new Vector3(place.x, 0f, place.z),
                m_location = new ZoneSystem.ZoneLocation
                {
                    m_prefab = new ZoneSystem.ZoneLocation.PrefabEntry { Name = place.name },
                    m_exteriorRadius = 16f,
                },
            });
        }

        ZoneSystem.instance = zones;
    }

    [Fact]
    public void ANetworkLoadedFromTheSaveIsClearedBeforeANewOneIsGenerated()
    {
        var world = new SyntheticWorld { HasRiver = false, HasMountain = false };
        WorldGenerator.instance = world;
        RoadNetworkGenerator.Reset();
        GiveTheWorldSomePlaces();
        try
        {
            // A network already in the world, as a load leaves it: points in
            // the grid, and the generator told they came from the save.
            SetPathfinder(new RoadPathfinder(world));
            Assert.True(RoadNetworkGenerator.GenerateRoad(
                new Vector2(-300f, -100f), 0f, new Vector2(200f, 150f), 0f, 4f, "loaded"));
            int loadedPoints = RoadSpatialGrid.TotalRoadPoints;
            Assert.True(loadedPoints > 0);

            typeof(RoadNetworkGenerator).GetField("m_roadsGenerated", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, false);
            MarkLoadedFromSave();
            Assert.False((bool)typeof(RoadNetworkGenerator)
                .GetField("m_roadsGenerated", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!,
                "the generated flag should be false, as it is after a load");

            // Now regenerate. Whatever it builds, it must not be built on top
            // of what was there.
            RoadNetworkGenerator.GenerateRoads(force: true);

            Assert.True(RoadNetworkGenerator.RoadsGenerated,
                "the regeneration did not run, so this test proves nothing");
            Assert.NotEmpty(RoadRouteRecorder.Routes);

            // The grid should hold the network just built and nothing else.
            // Its own count runs a little above the route points, because a
            // crossing is painted as its own stretch, so the test allows a
            // margin and still fails on a whole second network.
            // The old network must be gone, not merely outnumbered: the road
            // that was there before must not still be in the record, and the
            // grid must hold only what the regeneration built.
            foreach (RoadRoute route in RoadRouteRecorder.Routes)
                Assert.False(route.Label == "loaded",
                    "the road loaded from the save survived a regeneration, so the new network " +
                    "was laid on top of the old one");

            int built = 0;
            foreach (RoadRoute route in RoadRouteRecorder.Routes)
                built += route.Points.Count;
            Assert.True(RoadSpatialGrid.TotalRoadPoints <= built,
                $"the grid holds {RoadSpatialGrid.TotalRoadPoints} points for a network of {built}: " +
                "points from the loaded network are still in it");
        }
        finally { TearDown(); }
    }
}
