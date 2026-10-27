using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Columns under pieces not sunk into the ground (one owner of the column's sizing): a pillar column's whole base SupportBury
/// under the lowest ground at its corners, carried on below the staircase's own cuts, never by a pillar wholly under the ground
/// (BuriedPieces.ColumnGoesOn).
/// </summary>
public static partial class StairLayout
{
    /// <summary>The lowest pillar ends at least this far into the ground.</summary>
    public const float SupportBury = 0.25f;

    /// <summary>Half the width of a support column (stone and black marble are 1 m square).</summary>
    public const float SupportHalfWidth = 0.5f;

    /// <summary>
    /// Columns carried on down below the earthwork beside them: a cut laid for another piece, and its batter climbing back
    /// to the ground over the next few metres, can lower the ground under a column after it was sized, leaving its base in
    /// the air (one stood 1.6 m
    /// short). Each column is carried on by the one column rule (<see cref="BuriedPieces.ColumnGoesOn"/>) over the ground
    /// as its own cuts and their batters leave it.
    /// </summary>
    private static void SinkColumnsBelowCuts(StairPlan plan, StairKit kit, Func<float, float, float> ground)
    {
        if (plan.Cuts.Count == 0) return;
        var bottoms = new Dictionary<(int, int), (Vector3 at, float yaw)>();
        foreach (StairPiece p in plan.Pieces)
        {
            if (p.Kind != StairPieceKind.Support) continue;
            var key = (Mathf.RoundToInt(p.Position.x * 100f), Mathf.RoundToInt(p.Position.z * 100f));
            if (!bottoms.TryGetValue(key, out var lowest) || p.Position.y < lowest.at.y) bottoms[key] = (p.Position, p.YawDegrees);
        }
        float reach = CutRadius + RoadTerrainModifier.CutBatterReach + SupportHalfWidth * 1.5f;
        foreach (var (at, yaw) in bottoms.Values)
        {
            float floor = float.PositiveInfinity;
            foreach (Vector3 cut in plan.Cuts)
            {
                var c = new Vector2(cut.x, cut.z);
                if (Vector2.Distance(c, new Vector2(at.x, at.z)) > reach) continue;
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        float d = Vector2.Distance(c, new Vector2(at.x + cx * SupportHalfWidth, at.z + cz * SupportHalfWidth));
                        if (d > CutRadius + RoadTerrainModifier.CutBatterReach) continue;
                        floor = Mathf.Min(floor, cut.y + Mathf.Max(0f, d - CutRadius) * RoadTerrainModifier.CutBatterSlope);
                    }
            }
            if (float.IsPositiveInfinity(floor)) continue;
            // The ground the cuts leave, at the vertices: the lowest of the natural ground and every cut's flat and batter.
            Func<int, int, float> cutGround = (i, k) =>
            {
                float h = ground(i, k);
                foreach (Vector3 cut in plan.Cuts)
                {
                    float d = Vector2.Distance(new Vector2(cut.x, cut.z), new Vector2(i, k));
                    if (d <= CutRadius + RoadTerrainModifier.CutBatterReach) h = Mathf.Min(h, cut.y + Mathf.Max(0f, d - CutRadius) * RoadTerrainModifier.CutBatterSlope);
                }
                return h;
            };
            for (float top = at.y - kit.SupportHeight / 2f; BuriedPieces.ColumnGoesOn(top, floor, kit.Support, kit.SupportHeight, new Vector2(at.x, at.z), yaw, cutGround); top -= kit.SupportHeight)
                plan.Pieces.Add(new StairPiece { Kind = StairPieceKind.Support, Prefab = kit.Support, Position = new Vector3(at.x, top - kit.SupportHeight / 2f, at.z), YawDegrees = yaw });
        }
    }
}
