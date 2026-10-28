using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// Bridges as a structure source: the ruined wooden bridges of
/// <see cref="BridgeLayout"/> at the network's recorded crossings. Pure logic.
/// </summary>
public sealed class BridgeStructures : IStructureSource
{
    /// <summary>How far below a deck the deck clearance starts, metres: the
    /// deck's own beams and a rock the deck runs into.</summary>
    public const float DeckBelow = 1f;

    public string LogTag => "[BRIDGES]";

    public IEnumerable<StructurePiece> Plan(WorldGenerator world, int seed)
    {
        foreach (RoadCrossing crossing in BridgeLayout.DistinctSites(RoadNetworkGenerator.GetRoadCrossings()))
        {
            List<StructurePiece> bridge = BridgeLayout.Solve(crossing, world, seed);
            var bridgeZones = new HashSet<Vector2s>();
            foreach (StructurePiece piece in bridge)
            {
                // Debris on the riverbed is not part of the bridge: it never holds one back.
                if (piece.Kind != StructurePieceKind.Debris)
                    bridgeZones.Add(ZoneSystem.GetZone(piece.Position));
                piece.StructureZones = bridgeZones;
            }
            foreach (StructurePiece piece in bridge)
                yield return piece;
        }
    }

    /// <summary>Pieces the plan puts at a crossing (0 for a ford that is not spanned).</summary>
    public static int PiecesAt(RoadCrossing crossing) =>
        WorldGenerator.instance == null ? 0 : BridgeLayout.Solve(crossing, WorldGenerator.instance, WorldGenerator.instance.GetSeed()).Count;

    /// <summary>
    /// Clear-areas around the plan so vegetation does not spawn through the
    /// deck (a bridge zone often carries no painted road points of its own).
    /// </summary>
    public void AddClearAreas(List<StructurePiece> zonePieces, List<(Vector3 centre, float radius)> areas)
    {
        Vector3? last = null;
        foreach (StructurePiece piece in zonePieces)
        {
            if (last.HasValue && Vector3.Distance(last.Value, piece.Position) < 2f)
                continue;
            // Wide enough for a two-lane deck: pieces sit up to DeckHalfWidth
            // either side of the crossing line, and the clearing has to keep
            // trees out of BOTH lanes, not just the centreline.
            areas.Add((piece.Position, BridgeLayout.DeckHalfWidth + 1.5f));
            last = piece.Position;
        }
    }

    /// <summary>
    /// Bridge decks are not road points, so rocks on a bridge were never looked
    /// at. Each span in reach is walked every metre at deck height (the road's
    /// stored height at each bank, interpolated), from just under the deck; a
    /// rock there goes whole: bridges are few, and no rock is missed.
    /// </summary>
    public void AddClearances(Vector3 centre, float radius, List<StructureClearance> clearances)
    {
        var deck = new List<(RoadSpatialGrid.RoadPoint point, float height)>();
        var c = new Vector2(centre.x, centre.z);
        foreach (var crossing in RoadNetworkGenerator.GetRoadCrossings())
        {
            // Every bridge, and a ford built as a footbridge.
            bool span = crossing.Kind == CrossingKind.Ford && crossing.Style == FordStyle.Span;
            if (crossing.Kind != CrossingKind.Bridge && !span) continue;
            Vector2 a = crossing.FromBank, b = crossing.ToBank;
            float length = Vector2.Distance(a, b);
            if (length < 1f) continue;
            // Nearest point of the span to the probe centre.
            float t0 = Mathf.Clamp01(Vector2.Dot(c - a, b - a) / (length * length));
            if (Vector2.Distance(a + (b - a) * t0, c) > radius) continue;
            float ha, hb;
            if (span && WorldGenerator.instance != null) ha = hb = BridgeLayout.SpanDeckHeight(crossing, WorldGenerator.instance);
            else
            {
                if (!RoadSpatialGrid.TryGetRoadHeightWithin(a, 4f, out ha)) ha = RoadConstants.SeaLevel + BridgeLayout.DeckFreeboard;
                if (!RoadSpatialGrid.TryGetRoadHeightWithin(b, 4f, out hb)) hb = ha;
            }
            int n = Mathf.CeilToInt(length);
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                deck.Add((new RoadSpatialGrid.RoadPoint(a + (b - a) * t, RoadSpatialGrid.DefaultRoadWidth, Mathf.Lerp(ha, hb, t)), Mathf.Lerp(ha, hb, t)));
            }
        }
        // Headings come from every deck point in reach, as they always have.
        var line = deck.ConvertAll(d => d.point);
        foreach (var (point, height) in deck)
            clearances.Add(new StructureClearance(point, line, height - DeckBelow, DeckBelow + RoadRockPolicy.ClearanceHeight, wholeOnly: true));
    }
}
