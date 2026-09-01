using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Piece kit + ruin tuning for staircases, by progression tier.</summary>
public sealed class StairStyle
{
    public string StepPrefab = "";
    public string SupportPrefab = "";

    public float StepRun = 2f;        // horizontal meters per step piece
    public float StepRise = 1f;       // vertical meters per step piece
    public float SupportSegment = 2f; // vertical meters per support piece
    public float MaxUndersideGap = 0.4f; // beyond this, emit support to ground
    public float Survival = 0.85f;    // per-step survival probability

    public static readonly StairStyle MeadowsWood = new()
    {
        StepPrefab = "wood_stair",
        SupportPrefab = "wood_pole2",
    };

    public static readonly StairStyle MountainStone = new()
    {
        StepPrefab = "stone_stair",
        SupportPrefab = "stone_wall_1x1",
    };
}

/// <summary>
/// Deterministic layout solver for ruined staircases on recorded stair runs.
/// Steps march along the centerline every StepRun meters, climbing at the
/// piece's fixed rise while tracking the terrain: clipping slightly into
/// rising ground (a cut) and standing on support columns over dips — never
/// floating (WearNTear). Ruin removes steps deterministically; a removed
/// step's supports go with it.
/// </summary>
public static class StairLayout
{
    public static List<BridgePiece> Solve(StairRun run, WorldGenerator world, int worldSeed, StairStyle style)
    {
        List<BridgePiece> pieces = new();
        if (run == null || world == null || style == null || run.Points.Count < 2)
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(run));

        // Walk the centerline, emitting a step every StepRun meters.
        float startGround = world.GetHeight(run.Points[0].x, run.Points[0].y);
        float y = startGround;
        float carried = 0f;

        for (int i = 1; i < run.Points.Count; i++)
        {
            Vector2 a = run.Points[i - 1];
            Vector2 b = run.Points[i];
            float segLen = Vector2.Distance(a, b);
            if (segLen < 0.01f)
                continue;

            Vector2 dir = (b - a) * (1f / segLen);
            float yaw = Mathf.Atan2(dir.x, dir.y) * 180f / Mathf.PI;

            float pos = style.StepRun - carried;
            while (pos <= segLen)
            {
                Vector2 p = a + dir * pos;
                float ground = world.GetHeight(p.x, p.y);

                // Track terrain at the piece's fixed rise per step: climb or
                // descend toward ground, clipping into cuts on steep rises.
                y += Mathf.Clamp(ground + 0.1f - y, -style.StepRise, style.StepRise);

                bool alive = NextFloat(rng) < style.Survival;
                if (alive)
                {
                    pieces.Add(new BridgePiece
                    {
                        Kind = BridgePieceKind.StairStep,
                        Prefab = style.StepPrefab,
                        Position = new Vector3(p.x, y, p.y),
                        YawDegrees = yaw,
                        HealthFraction = 0.35f + NextFloat(rng) * 0.4f,
                    });

                    // Support to ground whenever the step's underside floats.
                    float gap = y - ground;
                    if (gap > style.MaxUndersideGap)
                    {
                        float health = 0.3f + NextFloat(rng) * 0.3f;
                        for (float h = ground; h < y - 0.01f; h += style.SupportSegment)
                        {
                            pieces.Add(new BridgePiece
                            {
                                Kind = BridgePieceKind.StairSupport,
                                Prefab = style.SupportPrefab,
                                Position = new Vector3(p.x, h, p.y),
                                YawDegrees = yaw,
                                HealthFraction = health,
                            });
                        }
                    }
                }
                else
                {
                    // Consume the rng draws a live step would have used so
                    // ruin choices stay independent of neighbors.
                    NextFloat(rng);
                }

                pos += style.StepRun;
            }
            carried = segLen - (pos - style.StepRun);
        }

        return pieces;
    }

    public static StairStyle StyleFor(Heightmap.Biome biome)
    {
        return biome switch
        {
            Heightmap.Biome.Mountain or Heightmap.Biome.Plains or Heightmap.Biome.Mistlands
                => StairStyle.MountainStone,
            _ => StairStyle.MeadowsWood,
        };
    }

    private static float NextFloat(System.Random rng) => (float)rng.NextDouble();

    private static int StableSeed(StairRun run)
    {
        unchecked
        {
            int h = 23;
            h = h * 31 + Mathf.RoundToInt(run.FromPos.x * 10f);
            h = h * 31 + Mathf.RoundToInt(run.FromPos.y * 10f);
            return h;
        }
    }
}
