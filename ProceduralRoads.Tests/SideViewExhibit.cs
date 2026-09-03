using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ProceduralRoads.Tests;

/// <summary>Writes a side view of a crossing — the terrain profile along
/// the crossing line, the waterline, and every planned piece as a box — as
/// an SVG next to the other validation results, for a human to look at.</summary>
public static class SideViewExhibit
{
    public static string OutputDir
    {
        get
        {
            string dir = Path.Combine(Path.GetDirectoryName(typeof(SideViewExhibit).Assembly.Location)!, "..", "..", "..", "validation-results");
            Directory.CreateDirectory(dir);
            return Path.GetFullPath(dir);
        }
    }

    public static string Write(string file, RoadCrossing c, List<BridgePiece> plan, WorldGenerator world, BridgeStyle style, string title)
    {
        const float margin = 24f, scale = 12f;
        float alongMin = -margin, alongMax = c.Width + margin;
        float yMin = c.RiverbedHeight - 2f, yMax = c.WaterLevel + 12f;
        foreach (var p in plan) yMax = Mathf.Max(yMax, p.Position.y + 2f);
        float W = (alongMax - alongMin) * scale, H = (yMax - yMin) * scale;
        float X(float along) => (along - alongMin) * scale;
        float Y(float y) => (yMax - y) * scale;
        string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(W)}\" height=\"{F(H + 24)}\" viewBox=\"0 0 {F(W)} {F(H + 24)}\" font-family=\"sans-serif\" font-size=\"11\">\n");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"white\"/>\n<text x=\"6\" y=\"14\">{title} — width {F(c.Width)} m, bed {F(c.RiverbedHeight)}, water {F(c.WaterLevel)}</text>\n");
        sb.Append($"<g transform=\"translate(0,24)\">\n");
        // terrain profile
        sb.Append("<polyline fill=\"#d9c9a5\" stroke=\"#7a6a48\" points=\"");
        sb.Append($"{F(X(alongMin))},{F(Y(yMin))} ");
        for (float a = alongMin; a <= alongMax; a += 0.5f)
        {
            Vector2 p = c.FromBank + c.Direction * a;
            float h = BiomeBlendedHeight.GetBlendedHeight(p.x, p.y, world);
            sb.Append($"{F(X(a))},{F(Y(h))} ");
        }
        sb.Append($"{F(X(alongMax))},{F(Y(yMin))}\"/>\n");
        // waterline
        sb.Append($"<line x1=\"0\" y1=\"{F(Y(c.WaterLevel))}\" x2=\"{F(W)}\" y2=\"{F(Y(c.WaterLevel))}\" stroke=\"#4a90d9\" stroke-dasharray=\"4 3\"/>\n");
        // pieces
        foreach (var p in plan)
        {
            Vector2 rel = new(p.Position.x - c.FromBank.x, p.Position.z - c.FromBank.y);
            float along = Vector2.Dot(rel, c.Direction);
            (float w, float bottom, float top, string color) = p.Kind switch
            {
                BridgePieceKind.Piling => (0.5f, -style.PilingSegment * 0.5f, style.PilingSegment * 0.5f, "#8b5a2b"),
                BridgePieceKind.Beam => (2f, -0.15f, 0.15f, "#a0522d"),
                BridgePieceKind.Deck => (style.DeckSpan, style.DeckTopOffset - 0.2f, style.DeckTopOffset, "#c08040"),
                BridgePieceKind.Abutment => (2f, -0.2f, 0f, "#806040"),
                BridgePieceKind.Stair => (2f, 0f, 1f, "#d4a060"),
                BridgePieceKind.Arch => (2f, -0.5f, 0.5f, "#909090"),
                _ => (1f, -0.3f, 0.3f, "#666666"),
            };
            sb.Append($"<rect x=\"{F(X(along - w * 0.5f))}\" y=\"{F(Y(p.Position.y + top))}\" width=\"{F(w * scale)}\" height=\"{F((top - bottom) * scale)}\" fill=\"{color}\" fill-opacity=\"0.85\" stroke=\"#333\" stroke-width=\"0.5\"/>\n");
        }
        // bank markers
        foreach (float a in new[] { 0f, c.Width })
            sb.Append($"<line x1=\"{F(X(a))}\" y1=\"0\" x2=\"{F(X(a))}\" y2=\"{F(H)}\" stroke=\"#c33\" stroke-width=\"0.5\" stroke-dasharray=\"2 2\"/>\n");
        sb.Append("</g>\n</svg>\n");

        string path = Path.Combine(OutputDir, file);
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
