using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Picks which bridge kit a crossing was built from.
///
/// Material is a function of PROGRESSION, not biome alone: the same Black
/// Forest river is spanned by a wood trestle near spawn and by stonework out
/// on the rim, because the people who built it had better materials by then.
/// Biome contributes a tier, the world ring contributes the rest, and the two
/// sum into a single advancement value.
///
/// Selection is weighted, never absolute. Every crossing keeps at least a
/// MinorityFloor chance of the "wrong" material for its position, so roads
/// stay varied instead of turning into one kit per region — an old stone
/// bridge near spawn and a timber one on the frontier are both stories.
/// Weights are sampled from a per-crossing seed, so the choice is stable for
/// a given world but differs from crossing to crossing.
/// </summary>
public static class BridgeStyleSelection
{
    /// <summary>Minimum share held by both all-wood and all-stone everywhere,
    /// however lopsided the position makes the rest.</summary>
    public const float MinorityFloor = 0.10f;

    /// <summary>Share held by the hybrid (stone substructure, timber deck)
    /// kit at every position — it is the natural middle everywhere.</summary>
    public const float HybridShare = 0.25f;

    /// <summary>Biome contribution to advancement, before the ring is added.
    /// Mirrors player progression rather than geography.</summary>
    public static int BiomeTier(Heightmap.Biome biome) => biome switch
    {
        Heightmap.Biome.Swamp => 1,
        Heightmap.Biome.Plains => 2,
        Heightmap.Biome.Mountain or Heightmap.Biome.Mistlands
            or Heightmap.Biome.AshLands or Heightmap.Biome.DeepNorth => 3,
        _ => 0, // Meadows, Black Forest, Ocean
    };

    /// <summary>Advancement in [0,1]: biome tier plus world ring, normalised.
    /// Inner Meadows/Black Forest sit at 0; Mountain, or Plains out on the
    /// rim, saturate at 1.</summary>
    public static float Advancement(Heightmap.Biome biome, int ring)
        => Mathf.Clamp01((BiomeTier(biome) + ring) / 4f);

    /// <summary>Kit weights at an advancement value, in the order
    /// (wood, hybrid, stone). Always sums to 1; wood and stone never fall
    /// below MinorityFloor.</summary>
    public static (float Wood, float Hybrid, float Stone) Weights(float advancement)
    {
        float span = 1f - HybridShare - 2f * MinorityFloor;
        float wood = MinorityFloor + span * (1f - advancement);
        float stone = MinorityFloor + span * advancement;
        return (wood, HybridShare, stone);
    }

    /// <summary>The kit for one crossing. Deterministic per (world, crossing):
    /// the same river always carries the same bridge, but its neighbour
    /// upstream may well be built of something else.</summary>
    public static BridgeStyle StyleFor(Heightmap.Biome biome, int ring, int worldSeed, Vector2 crossingCenter)
    {
        (float wood, float hybrid, float _) = Weights(Advancement(biome, ring));

        // Own rng stream, so adding or reordering ruin draws later cannot
        // silently repaint every bridge in the world.
        System.Random rng = new System.Random(worldSeed ^ StyleSeed(crossingCenter));
        float roll = (float)rng.NextDouble();

        if (roll < wood)
            return BridgeStyle.MeadowsWood;
        if (roll < wood + hybrid)
            return BridgeStyle.StoneAndTimber;
        return BridgeStyle.MountainStone;
    }

    private static int StyleSeed(Vector2 center)
    {
        unchecked
        {
            int h = 7919;
            h = h * 31 + Mathf.RoundToInt(center.x * 10f);
            h = h * 31 + Mathf.RoundToInt(center.y * 10f);
            return h;
        }
    }
}
