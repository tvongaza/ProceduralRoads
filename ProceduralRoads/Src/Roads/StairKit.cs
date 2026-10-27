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
///
/// Materials by biome: Mistlands stairs are black marble or similar pieces, meadows wood.
/// The Black Forest can be either, with stone more likely further out; its stone resolves
/// to 0 in the early centre game, and wood stairs never fail to resolve, the probability just lowers.
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

    /// <summary>Mistlands. Every piece here is a 1 m thick block like stone's;
    /// the 1 m column stacks where stone stacks a 2 m pillar.</summary>
    public static readonly StairKit BlackMarble = new("blackmarble", "blackmarble_stair", "blackmarble_floor", 0.5f, 1f,
        new[] { new FillerKind("blackmarble_2x1x1", 2f, 1f), new FillerKind("blackmarble_1x1", 1f, 1f), new FillerKind("blackmarble_floor", 2f, 2f) },
        "blackmarble_column_1", 1f);

    /// <summary>Meadows, and the Black Forest's centre. A wood floor is a 13 cm
    /// board whose origin is its walking surface (it snaps level with the
    /// stair's top edge); wood has no 2 x 1 floor, so wedges take 1 x 1s.</summary>
    public static readonly StairKit Wood = new("wood", "wood_stair", "wood_floor", 0f, 0.13f,
        new[] { new FillerKind("wood_floor_1x1", 1f, 1f), new FillerKind("wood_floor", 2f, 2f) },
        "wood_pole2", 2f);

    public static StairKit? Named(string name) => name switch
    {
        "stone" => Stone,
        "blackmarble" or "marble" => BlackMarble,
        "wood" => Wood,
        _ => null,
    };

    /// <summary>In the Black Forest no stair is stone this close to the centre ...</summary>
    public const float ForestStoneFrom = 1200f;
    /// <summary>... and from here out stone's share holds at <see cref="ForestStoneShareMax"/>.</summary>
    public const float ForestStoneFull = 3000f;
    /// <summary>Wood never runs out: its chance only falls, to one in five.</summary>
    public const float ForestStoneShareMax = 0.8f;

    /// <summary>Chance a Black Forest staircase this far from the world's centre is
    /// stone. Its own curve, wider and further out than the road surface's
    /// 1000 m handover (a bit wider range and a bit further out).</summary>
    public static float ForestStoneShare(float distanceFromCentre)
    {
        float t = Mathf.Clamp01((distanceFromCentre - ForestStoneFrom) / (ForestStoneFull - ForestStoneFrom));
        return ForestStoneShareMax * t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The kit for a staircase starting at <paramref name="at"/> in
    /// <paramref name="biome"/>: deterministic from the world seed and the
    /// spot, so the same world builds the same stairs. Biomes nobody has asked
    /// about yet build stone.
    /// </summary>
    public static StairKit ForSite(Heightmap.Biome biome, Vector2 at, int seed)
    {
        switch (biome)
        {
            case Heightmap.Biome.Mistlands: return BlackMarble;
            case Heightmap.Biome.Meadows: return Wood;
            case Heightmap.Biome.BlackForest:
                return Draw(at, seed) < ForestStoneShare(at.magnitude) ? Stone : Wood;
            default: return Stone;
        }
    }

    /// <summary>Share of a staircase in the Mistlands from which it is black marble.</summary>
    public const float MistlandsShare = 0.4f;

    /// <summary>
    /// The kit for a staircase over <paramref name="line"/>: the kit of the
    /// biome most of it lies in, black marble where at least
    /// <see cref="MistlandsShare"/> of it is
    /// in the Mistlands (a staircase that starts in the Mistlands but is 90% in the mountains uses stone;
    /// black marble only if it is 50%+ in the Mistlands; the rule was later changed to
    /// 40%, after a staircase along the boundary came out 45% Mistlands and stone).
    /// The Black Forest's draw
    /// is made at the line's first point.
    /// </summary>
    public static StairKit ForLine(IReadOnlyList<Vector2> line, Func<float, float, Heightmap.Biome> biomeAt, int seed)
    {
        var counts = new Dictionary<Heightmap.Biome, int>();
        int samples = 0;
        for (int i = 0; i < line.Count; i++)
        {
            Vector2 a = line[i], b = i + 1 < line.Count ? line[i + 1] : line[i];
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
            for (int k = 0; k < n; k++)
            {
                Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                Heightmap.Biome biome = biomeAt(p.x, p.y);
                counts[biome] = counts.TryGetValue(biome, out int c) ? c + 1 : 1;
                samples++;
            }
            if (i + 1 >= line.Count) break;
        }
        if (samples == 0) return Stone;
        int mist = counts.TryGetValue(Heightmap.Biome.Mistlands, out int m) ? m : 0;
        Heightmap.Biome most = Heightmap.Biome.Mountain;
        int best = -1;
        foreach (var kv in counts)
            if (kv.Key != Heightmap.Biome.Mistlands && (kv.Value > best || (kv.Value == best && kv.Key < most))) { most = kv.Key; best = kv.Value; }
        if (mist >= MistlandsShare * samples || best < 0) return BlackMarble;
        return ForSite(most, line[0], seed);
    }

    /// <summary>A number in [0, 1) from the seed and the 4 m cell of the spot.</summary>
    internal static float Draw(Vector2 at, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u;
            h ^= (uint)Mathf.FloorToInt(at.x / 4f) * 374761393u;
            h = (h ^ (h >> 15)) * 2246822519u;
            h ^= (uint)Mathf.FloorToInt(at.y / 4f) * 668265263u;
            h = (h ^ (h >> 13)) * 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
