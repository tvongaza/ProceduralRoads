using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// A support model for ruin plans (Tys, 2 Sep 2026): the ruin state is
/// decided by the solver, never by the game, so nothing may be left for
/// Valheim's support system to knock down on zone load (the player arriving
/// to a crash, a sound, and dropped materials). Every planned piece must be
/// grounded or connected, through touching pieces, to one that is.
///
/// Deliberately stricter than vanilla: it knows nothing of wood's
/// horizontal reach and demands actual contact, so a plan that passes here
/// stands in the game; the in-game census is the final word. Used by the
/// harness to assert on every plan and by the blueprint weathering pass to
/// drop what lost its support.
/// </summary>
public static class SupportModel
{
    public const float GroundTolerance = 0.15f;
    public const float ContactTolerance = 0.3f;

    /// <summary>Piece extents: vertical interval relative to Position.y and
    /// a horizontal reach from the origin, per kind and kit.</summary>
    public static (float bottom, float top, float reach) Extent(BridgePiece p, BridgeStyle style)
    {
        float half = style.PilingSegment * 0.5f;
        float deckThickness = style.DeckTopOffset > 0f ? style.DeckTopOffset * 2f : 0.1f; // stone slab vs wood plate
        return p.Kind switch
        {
            BridgePieceKind.Piling => (-half, half, style.PilingAcross ? 1f : 0.25f),
            BridgePieceKind.Beam => (-0.15f, 0.15f, 1f),
            BridgePieceKind.Deck => (style.DeckTopOffset - deckThickness, style.DeckTopOffset, 1f),
            BridgePieceKind.Abutment => (style.DeckTopOffset - deckThickness, style.DeckTopOffset, 1f),
            BridgePieceKind.Stair => (0f, 1f, 1f),      // step: 2 m run, 1 m rise, origin at the foot
            BridgePieceKind.Debris => (-0.5f, 0.5f, 1f),
            BridgePieceKind.Arch => (-0.5f, 0.5f, 1f),
            _ => (-0.5f, 0.5f, 1f),
        };
    }

    /// <summary>Indices of pieces the model cannot support: neither buried
    /// nor connected through touching pieces to one that is.</summary>
    public static List<int> Floaters(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world)
    {
        int n = plan.Count;
        var ext = new (float bottom, float top, float reach)[n];
        for (int i = 0; i < n; i++)
            ext[i] = Extent(plan[i], style);
        var supported = new bool[n];
        var queue = new Queue<int>();

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = plan[i].Position;
            float ground = BiomeBlendedHeight.GetBlendedHeight(pos.x, pos.z, world);
            if (pos.y + ext[i].bottom <= ground + GroundTolerance)
            {
                supported[i] = true;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            for (int b = 0; b < n; b++)
            {
                if (supported[b] || !Touch(plan[a], ext[a], plan[b], ext[b]))
                    continue;
                supported[b] = true;
                queue.Enqueue(b);
            }
        }

        List<int> floaters = new();
        for (int i = 0; i < n; i++)
            if (!supported[i])
                floaters.Add(i);
        return floaters;
    }

    /// <summary>The plan without the pieces the model cannot support. One
    /// pass is enough: support is a closure from the ground, so removing
    /// what lies outside it changes nothing inside it.</summary>
    public static List<BridgePiece> DropUnsupported(List<BridgePiece> plan, BridgeStyle style, WorldGenerator world)
    {
        var floaters = new HashSet<int>(Floaters(plan, style, world));
        if (floaters.Count == 0)
            return plan;
        List<BridgePiece> kept = new(plan.Count - floaters.Count);
        for (int i = 0; i < plan.Count; i++)
            if (!floaters.Contains(i))
                kept.Add(plan[i]);
        return kept;
    }

    /// <summary>Two pieces touch when their vertical intervals overlap (or
    /// meet within tolerance) and their origins lie within reach of each
    /// other horizontally — resting, hanging, side-snapped or interpenetrating
    /// all count, as they do for vanilla colliders.</summary>
    private static bool Touch(BridgePiece a, (float bottom, float top, float reach) ea,
                              BridgePiece b, (float bottom, float top, float reach) eb)
    {
        float dx = a.Position.x - b.Position.x, dz = a.Position.z - b.Position.z;
        if (dx * dx + dz * dz > (ea.reach + eb.reach) * (ea.reach + eb.reach))
            return false;
        float aBottom = a.Position.y + ea.bottom, aTop = a.Position.y + ea.top;
        float bBottom = b.Position.y + eb.bottom, bTop = b.Position.y + eb.top;
        return aBottom <= bTop + ContactTolerance && aTop >= bBottom - ContactTolerance;
    }
}
