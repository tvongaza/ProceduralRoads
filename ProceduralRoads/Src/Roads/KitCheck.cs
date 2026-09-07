using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Checks a kit unit a player built (in PlanBuild or valheimCreative) against
/// what the composer needs, and says what is wrong in plain words. Pure
/// logic; the game adds prefab existence on top. Used by road_kits_check
/// and by the harness.
/// </summary>
public static class KitCheck
{
    /// <summary>How far off the snap lattice a piece may sit before it is
    /// called out.</summary>
    public const float LatticeTolerance = 0.05f;

    public sealed class Report
    {
        public string Name = "";
        public List<string> Problems = new();
        public List<string> Notes = new();
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>
    /// The rules a unit must follow:
    ///  - two snap points, the second straight along +z from the first (the
    ///    unit's length), at the deck height (y = 0);
    ///  - at least one piece, every piece with a prefab name and a kind the
    ///    style can classify (kind= data, or a prefab from the kit);
    ///  - a deck at y = 0 within the unit's length (START and SPAN carry the
    ///    road; END too), so the chain has a walking surface;
    ///  - a START has a stair before z = 0, an END a stair past its length;
    ///  - pieces on the 1 m snap lattice along z (Valheim's 2 m pieces snap
    ///    at whole metres along the chain), otherwise the chain drifts;
    ///    across the deck a piece sits where its own snaps put it (a wood
    ///    post pair at ±0.75 m).
    /// </summary>
    public static Report Check(RoadBlueprint unit, BridgeStyle style, string role)
    {
        Report r = new() { Name = unit.Name };
        if (unit.SnapPoints.Count < 2)
        {
            r.Problems.Add("needs two snap points: the first at the near end at deck height, the second at the far end (PlanBuild: two snap point markers; the first placed is the origin)");
        }
        else
        {
            Vector3 a = unit.SnapPoints[0], b = unit.SnapPoints[1];
            if (Mathf.Abs(b.x - a.x) > LatticeTolerance || Mathf.Abs(b.y - a.y) > LatticeTolerance)
                r.Problems.Add($"the far snap point must sit straight along +z from the near one; it is off by x {b.x - a.x:F2}, y {b.y - a.y:F2}");
            if (b.z - a.z < 1f)
                r.Problems.Add($"the unit is only {b.z - a.z:F2} m long between its snap points");
            else if (Mathf.Abs(Mathf.Round((b.z - a.z) / 2f) * 2f - (b.z - a.z)) > LatticeTolerance)
                r.Notes.Add($"length {b.z - a.z:F2} m is not a multiple of 2 m: fine, but plates will not meet the next unit's edge");
            if (unit.SnapPoints.Count > 2)
                r.Notes.Add($"{unit.SnapPoints.Count} snap points; only the first two are used");
        }
        if (unit.Pieces.Count == 0)
        {
            r.Problems.Add("no pieces");
            return r;
        }

        Vector3 anchor = unit.Anchor;
        float length = unit.Length;
        bool deck = false, stairBefore = false, stairAfter = false;
        foreach (BlueprintPiece p in unit.Pieces)
        {
            Vector3 local = p.LocalPosition - anchor;
            if (string.IsNullOrEmpty(p.Prefab))
            {
                r.Problems.Add("a piece has no prefab name");
                continue;
            }
            BridgePieceKind kind = BlueprintComposer.KindOf(style, p);
            if (kind == BridgePieceKind.Debris && p.DataValue("kind") == null)
                r.Problems.Add($"{p.Prefab} at ({local.x:F1},{local.y:F1},{local.z:F1}): not a kit prefab and no kind= data; the support model cannot place it (add kind=Deck|Piling|Beam|Arch|Stair to its data field, or use the kit's prefabs)");
            if (kind == BridgePieceKind.Deck && Mathf.Abs(local.y) <= 0.6f && local.z >= -LatticeTolerance && local.z <= length + LatticeTolerance)
                deck = true;
            if (kind == BridgePieceKind.Stair && local.z < 0f) stairBefore = true;
            if (kind == BridgePieceKind.Stair && local.z > length) stairAfter = true;
            if (OffLattice(local.z))
                r.Notes.Add($"{p.Prefab} at ({local.x:F2},{local.y:F2},{local.z:F2}) is off the 1 m snap lattice along the chain");
        }
        if (!deck)
            r.Problems.Add("no deck piece at the snap-point height within the unit: the road has nothing to walk on");
        if (role == "start" && !stairBefore)
            r.Problems.Add("a START needs a stair before z = 0, stepping down into the bank");
        if (role == "end" && !stairAfter)
            r.Problems.Add("an END needs a stair past its length, stepping down into the far bank");
        return r;
    }

    private static bool OffLattice(float v) => Mathf.Abs(v - Mathf.Round(v)) > LatticeTolerance;
}
