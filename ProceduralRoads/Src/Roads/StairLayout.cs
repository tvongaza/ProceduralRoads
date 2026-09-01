using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Piece kit + ruin tuning for staircases, by progression tier.</summary>
public sealed class StairStyle
{
    public string StepPrefab = "";
    public string SupportPrefab = "";
    public string LandingPrefab = "";

    // Stair prefab geometry, verified in-game via road_snap_probe:
    // wood_stair / stone_stair / blackmarble_stair are all 2m wide with
    // bottom-edge snaps at (±1, 0, +1) and top-edge snaps at (±1, 1, -1):
    // one piece runs 2m and rises 1m, climbing toward local -z.
    public float StepRun = 2f;        // horizontal meters per step piece
    public float StepRise = 1f;       // vertical meters per step piece
    public float SupportSegment = 2f; // vertical meters per support piece
    public float LandingTopOffset = 0f;  // walking surface height relative to landing origin
    public float MaxUndersideGap = 0.4f; // beyond this, emit support to ground
    public float Survival = 0.85f;    // per-step survival probability

    /// <summary>Ground embedding at chain contact points: bases sit slightly
    /// below terrain so vanilla WearNTear counts them as grounded (pieces
    /// hovering even 0.1m above terrain self-demolish — measured in-game).</summary>
    public float GroundEmbed = 0.05f;

    /// <summary>Largest heading change absorbed by pivoting two stairs around
    /// their shared edge; larger turns emit a flat landing to turn on.</summary>
    public float TurnPerJointDegrees = 15f;

    public static readonly StairStyle MeadowsWood = new()
    {
        StepPrefab = "wood_stair",
        SupportPrefab = "wood_pole2",
        LandingPrefab = "wood_floor",
        SupportSegment = 2f,   // wood_pole2: snaps at (0,±1,0)
        LandingTopOffset = 0f, // wood_floor: walking surface at origin height
    };

    public static readonly StairStyle MountainStone = new()
    {
        StepPrefab = "stone_stair",
        SupportPrefab = "stone_wall_1x1",
        LandingPrefab = "stone_floor_2x2",
        SupportSegment = 1f,     // stone_wall_1x1 is 1m tall (snaps at y ±0.5) —
                                 // stacking it every 2m left 1m air gaps
        LandingTopOffset = 0.5f, // stone_floor_2x2: top face at +0.5
    };
}

/// <summary>
/// Deterministic layout solver for ruined staircases on recorded stair runs.
///
/// Snap-chained grammar: each piece is placed by mating its bottom-edge snap
/// line to the previous piece's top-edge snap line, so runs read as one
/// staircase instead of disconnected treads. The chain follows the run
/// centerline pure-pursuit style, choosing per joint between
///  - a standard step (advance one run, rise one),
///  - a stacked steep step (advance half a run, rise one — the vanilla way
///    to build grade-1.0 stairs),
///  - a descending step (top edge mates the chain, bottom edge ahead),
///  - a flat landing piece where terrain flattens or the path turns hard
///    (landings are how builders turn switchbacks; they also satisfy the
///    fill-the-wedge rule for kits with no corner prefab).
/// Heading changes up to TurnPerJointDegrees pivot around the shared edge
/// midpoint; the chain rebases to ground level when terrain leaves its
/// reachable envelope (a genuine break in the ruin).
/// Support-safety: chain contact points embed GroundEmbed below terrain and
/// floating steps get support columns embedded into the ground below.
/// </summary>
public static class StairLayout
{
    private enum Move { Step, SteepStep, DownStep, Landing }

    public static List<BridgePiece> Solve(StairRun run, WorldGenerator world, int worldSeed, StairStyle style)
    {
        List<BridgePiece> pieces = new();
        if (run == null || world == null || style == null || run.Points.Count < 2)
            return pieces;

        System.Random rng = new System.Random(worldSeed ^ StableSeed(run));

        // Arc-length lookup over the centerline for pure-pursuit headings.
        List<Vector2> pts = run.Points;
        float totalLen = 0f;
        List<float> arc = new() { 0f };
        for (int i = 1; i < pts.Count; i++)
        {
            totalLen += Vector2.Distance(pts[i - 1], pts[i]);
            arc.Add(totalLen);
        }
        if (totalLen < style.StepRun)
            return pieces;

        // Chain state: the joint is the midpoint of the current piece's
        // top-edge snap line — where the next piece's bottom edge mates.
        Vector2 joint = pts[0];
        float jointY = world.GetHeight(joint.x, joint.y) - style.GroundEmbed;
        float traveled = 0f;
        Vector2 heading = (pts[1] - pts[0]).normalized;

        while (traveled < totalLen - style.StepRun * 0.5f)
        {
            // Pure pursuit: steer toward the centerline one step ahead.
            Vector2 target = PointAtArc(pts, arc, traveled + style.StepRun);
            Vector2 desired = target - joint;
            float desiredLen = desired.magnitude;
            if (desiredLen > 0.01f)
                desired *= 1f / desiredLen;
            else
                desired = heading;

            float turn = Vector2.SignedAngle(heading, desired);
            bool hardTurn = Mathf.Abs(turn) > style.TurnPerJointDegrees;
            if (!hardTurn)
                heading = desired;

            // Terrain ahead decides the move; the wedge between pieces at a
            // hard turn is covered by the landing the turn happens on.
            Vector2 aheadFull = joint + heading * style.StepRun;
            float groundAhead = world.GetHeight(aheadFull.x, aheadFull.y);
            float riseNeeded = groundAhead - jointY;

            Move move;
            if (hardTurn)
                move = Move.Landing;
            else if (riseNeeded >= 1.5f * style.StepRise)
                move = Move.SteepStep;
            else if (riseNeeded >= 0.4f * style.StepRise)
                move = Move.Step;
            else if (riseNeeded <= -0.4f * style.StepRise)
                move = Move.DownStep;
            else
                move = Move.Landing;

            float advance;
            float rise;
            switch (move)
            {
                case Move.SteepStep: advance = style.StepRun * 0.5f; rise = style.StepRise; break;
                case Move.DownStep: advance = style.StepRun; rise = -style.StepRise; break;
                case Move.Landing: advance = style.StepRun; rise = 0f; break;
                default: advance = style.StepRun; rise = style.StepRise; break;
            }

            Vector2 nextJoint = joint + heading * advance;
            float nextY = jointY + rise;

            // Rebase when the chain leaves the terrain envelope: buried past
            // one rise (cut too deep) or beyond support range. The chain
            // BREAKS here — skip forward one full run without emitting, so
            // the ruin shows a genuine gap instead of a step teleporting to
            // the new level.
            float nextGround = world.GetHeight(nextJoint.x, nextJoint.y);
            if (nextY < nextGround - 1.5f * style.StepRise || nextY - nextGround > 4f)
            {
                joint = nextJoint + heading * style.StepRun;
                jointY = world.GetHeight(joint.x, joint.y) - style.GroundEmbed;
                traveled += advance + style.StepRun;
                continue;
            }

            float yaw = Mathf.Atan2(heading.x, heading.y) * 180f / Mathf.PI;
            bool alive = NextFloat(rng) < style.Survival;
            float health = 0.35f + NextFloat(rng) * 0.4f;

            if (alive)
            {
                if (move == Move.Landing)
                {
                    // Flat piece whose walking surface continues the chain.
                    Vector2 center = joint + heading * (style.StepRun * 0.5f);
                    pieces.Add(new BridgePiece
                    {
                        Kind = BridgePieceKind.Landing,
                        Prefab = style.LandingPrefab,
                        Position = new Vector3(center.x, jointY - style.LandingTopOffset, center.y),
                        YawDegrees = yaw,
                        HealthFraction = health,
                    });
                    EmitSupport(pieces, style, world, center, jointY, rng);
                }
                else
                {
                    // Stair placed by its snap edges. Ascending: bottom edge
                    // at the joint, top edge one rise up, 'advance' ahead.
                    // Descending: top edge at the joint instead.
                    Vector2 center = joint + heading * (advance * 0.5f);
                    float baseY = move == Move.DownStep ? nextY : jointY;
                    float stairYaw = move == Move.DownStep ? yaw + 180f : yaw;
                    pieces.Add(new BridgePiece
                    {
                        Kind = BridgePieceKind.StairStep,
                        Prefab = style.StepPrefab,
                        Position = new Vector3(center.x, baseY, center.y),
                        YawDegrees = stairYaw,
                        HealthFraction = health,
                    });
                    EmitSupport(pieces, style, world, center, baseY, rng);
                }
            }
            else
            {
                // Consume the rng draws a live step would have used so ruin
                // choices stay independent of neighbors.
                NextFloat(rng);
            }

            joint = nextJoint;
            jointY = nextY;
            traveled += advance;
        }

        return pieces;
    }

    /// <summary>Support column under a piece whose underside floats: segments
    /// stacked downward from just under the piece until embedded in ground,
    /// so every column is grounded by construction.</summary>
    private static void EmitSupport(List<BridgePiece> pieces, StairStyle style,
        WorldGenerator world, Vector2 pos, float baseY, System.Random rng)
    {
        float ground = world.GetHeight(pos.x, pos.y);
        float gap = baseY - ground;
        if (gap <= style.MaxUndersideGap)
            return;

        float health = 0.3f + NextFloat(rng) * 0.3f;
        // Segment centers walk down from the piece underside until one sits
        // at or below ground level (buried base = vanilla support).
        float half = style.SupportSegment * 0.5f;
        for (float top = baseY; ; top -= style.SupportSegment)
        {
            float center = top - half;
            pieces.Add(new BridgePiece
            {
                Kind = BridgePieceKind.StairSupport,
                Prefab = style.SupportPrefab,
                Position = new Vector3(pos.x, center, pos.y),
                YawDegrees = 0f,
                HealthFraction = health,
            });
            if (center <= ground)
                break;
        }
    }

    private static Vector2 PointAtArc(List<Vector2> pts, List<float> arc, float distance)
    {
        if (distance <= 0f)
            return pts[0];
        for (int i = 1; i < pts.Count; i++)
        {
            if (arc[i] >= distance)
            {
                float segLen = arc[i] - arc[i - 1];
                float t = segLen > 0.001f ? (distance - arc[i - 1]) / segLen : 0f;
                return Vector2.Lerp(pts[i - 1], pts[i], t);
            }
        }
        return pts[pts.Count - 1];
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
