using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>A flat piece a turn's wedge may be filled with: <see cref="Across"/>
/// metres across its heading, <see cref="Depth"/> along it.</summary>
public readonly struct FillerKind
{
    public readonly string Prefab;
    public readonly float Across;
    public readonly float Depth;

    public FillerKind(string prefab, float across, float depth)
    {
        Prefab = prefab; Across = across; Depth = depth;
    }
}

/// <summary>
/// The pieces one staircase is built of. Every kit's stair is the same 2 x 2 m
/// with a 1 m rise, so the layout is the same whatever it is built of; the
/// floors, fillers and supports differ in thickness and height. Sizes from
/// cli_piece_geometry, Valheim 1.0.16.
/// </summary>
public sealed class StairKit
{
    public readonly string Name;
    /// <summary>2 x 2 m, 1 m rise; origin at the foot, centred; climbs toward local -z.</summary>
    public readonly string Stair;
    /// <summary>2 x 2 m floor; its walking surface stands <see cref="FloorTop"/> above its origin.</summary>
    public readonly string Floor;
    public readonly float FloorTop;
    public readonly float FloorThickness;
    /// <summary>Flat pieces for a turn's wedge, as thick as the floor and level with it.</summary>
    public readonly FillerKind[] Fillers;
    /// <summary>Stacked under a piece that does not reach the ground; origin at its centre.</summary>
    public readonly string Support;
    public readonly float SupportHeight;

    private StairKit(string name, string stair, string floor, float floorTop, float floorThickness, FillerKind[] fillers, string support, float supportHeight)
    {
        Name = name; Stair = stair; Floor = floor; FloorTop = floorTop; FloorThickness = floorThickness;
        Fillers = fillers; Support = support; SupportHeight = supportHeight;
    }

    public static readonly StairKit Stone = new("stone", "stone_stair", "stone_floor_2x2", 0.5f, 1f,
        new[] { new FillerKind("stone_wall_2x1", 2f, 1f), new FillerKind("stone_wall_1x1", 1f, 1f), new FillerKind("stone_floor_2x2", 2f, 2f) },
        "stone_pillar", 2f);
}
