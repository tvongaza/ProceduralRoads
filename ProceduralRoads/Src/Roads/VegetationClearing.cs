using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Trees and rocks on the roads of zones that were generated before the road.
///
/// The game keeps vegetation off a road only while it generates a zone: the
/// road's clear areas are handed to PlaceVegetation, and nothing is placed
/// inside them. A zone generated earlier -- before the mod was installed, or
/// before this network existed -- already has everything it grew, as
/// persistent objects, and nothing ever takes them away. So the server
/// removes, once per zone and network, exactly what generation would have left
/// out: objects of the game's own vegetation prefabs, inside the same clear
/// areas, tested the way the game tests them (ZoneSystem.InsideClearArea, an
/// axis-aligned square). Anything that is not a vegetation prefab -- player
/// builds, containers, bridge pieces -- is never touched, and objects within a
/// location's exterior radius are left to the location.
///
/// Pure logic; ServerTerrainBake finds and removes the objects.
/// </summary>
public static class VegetationClearing
{
    /// <summary>A clear area as the game tests it: an axis-aligned square of half-size Radius.</summary>
    public readonly struct Area
    {
        public readonly Vector2 Centre;
        public readonly float Radius;

        public Area(Vector2 centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }
    }

    /// <summary>A location's footprint: a circle of its exterior radius.</summary>
    public readonly struct Footprint
    {
        public readonly Vector2 Centre;
        public readonly float Radius;

        public Footprint(Vector2 centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }
    }

    /// <summary>ZoneSystem.InsideClearArea, the test PlaceVegetation uses: strict, and square.</summary>
    public static bool InsideClearArea(IReadOnlyList<Area> areas, Vector2 point)
    {
        foreach (Area area in areas)
        {
            if (point.x > area.Centre.x - area.Radius && point.x < area.Centre.x + area.Radius &&
                point.y > area.Centre.y - area.Radius && point.y < area.Centre.y + area.Radius)
                return true;
        }
        return false;
    }

    /// <summary>Whether an existing object is one generation would not have placed on the road.</summary>
    public static bool ShouldRemove(int prefab, Vector2 position, ICollection<int> vegetationPrefabs,
        IReadOnlyList<Area> clearAreas, IReadOnlyList<Footprint> locations)
    {
        if (!vegetationPrefabs.Contains(prefab) || !InsideClearArea(clearAreas, position))
            return false;
        foreach (Footprint location in locations)
        {
            if ((position - location.Centre).sqrMagnitude < location.Radius * location.Radius)
                return false;
        }
        return true;
    }

    // ---- which zones' vegetation already matches the current network ----
    //
    // A zone generated with the road's clear areas matches from the start; a
    // zone generated before it matches once it has been cleared. Clearing
    // happens once per zone and network, so a tree a player plants on the road
    // later is theirs to keep. The set is saved with the network
    // (RoadNetworkPersistence) and belongs to one network version.

    private static readonly HashSet<Vector2s> s_cleared = new();

    /// <summary>The network version the cleared zones belong to (0 = none).</summary>
    public static int Version { get; private set; }

    public static IReadOnlyCollection<Vector2s> ClearedZones => s_cleared;

    public static bool IsCleared(Vector2s zone, int version) =>
        version != 0 && version == Version && s_cleared.Contains(zone);

    public static void MarkCleared(Vector2s zone, int version)
    {
        if (version == 0)
            return;
        if (version != Version)
        {
            s_cleared.Clear();
            Version = version;
        }
        s_cleared.Add(zone);
    }

    /// <summary>Take the saved record, unless it belongs to another network.</summary>
    public static void Load(int savedVersion, IEnumerable<Vector2s> zones, int currentVersion)
    {
        Reset();
        if (savedVersion == 0 || savedVersion != currentVersion)
            return;
        Version = savedVersion;
        foreach (Vector2s zone in zones)
            s_cleared.Add(zone);
    }

    public static void Reset()
    {
        s_cleared.Clear();
        Version = 0;
    }
}
