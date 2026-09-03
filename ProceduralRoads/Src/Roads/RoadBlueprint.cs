using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>
/// One piece of a blueprint in the blueprint's local frame: +z along the
/// bridge, +y up, +x to the right of +z (Unity's handedness, so a piece with
/// identity rotation placed at yaw θ has its +x on the road's right). Mirrors
/// one line of the PlanBuild piece format that jneb802's valheimCreative
/// writes and Expand World reads:
/// prefab;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;data;scaleX;scaleY;scaleZ.
/// </summary>
public sealed class BlueprintPiece
{
    public string Prefab = "";
    public string Category = "Building";
    public Vector3 LocalPosition;
    /// <summary>Local rotation as the unit quaternion the file stores.
    /// Euler access goes through <see cref="BlueprintMath"/> so the harness
    /// and the game agree without UnityEngine.Quaternion.</summary>
    public float RotX, RotY, RotZ, RotW = 1f;
    /// <summary>Free text the loaders keep with the piece (valheimCreative
    /// stores it on the ZDO). Our exports put "kind=Deck health=0.62" here:
    /// space-separated, since PlanBuild rewrites commas as decimal points.</summary>
    public string Data = "";
    public Vector3 Scale = new(1f, 1f, 1f);

    public (float pitch, float yaw, float roll) Euler => BlueprintMath.ToEuler(RotX, RotY, RotZ, RotW);

    public void SetEuler(float pitch, float yaw, float roll)
    {
        (RotX, RotY, RotZ, RotW) = BlueprintMath.FromEuler(pitch, yaw, roll);
    }

    /// <summary>Value of a "key=value" entry in <see cref="Data"/>, or null.</summary>
    public string? DataValue(string key)
    {
        foreach (string entry in Data.Split(' '))
        {
            int eq = entry.IndexOf('=');
            if (eq > 0 && entry.Substring(0, eq).Trim() == key)
                return entry.Substring(eq + 1).Trim();
        }
        return null;
    }
}

/// <summary>
/// A PlanBuild-format text blueprint (the format jneb802's own valheimCreative
/// saves with "!creative save", Expand World spawns, and PlanBuild edits), as
/// the bridge kits and player-built bridges are stored. Pure logic: the same
/// code parses and writes in the harness and in the game.
///
/// File layout, exactly as valheimCreative's writer emits it:
/// <code>
/// #Name:name
/// #Creator:who
/// #Description:"text"
/// #Category:Blueprints
/// #SnapPoints            (PlanBuild; optional, our kits use it)
/// x;y;z
/// #Pieces
/// prefab;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;data;scaleX;scaleY;scaleZ
/// </code>
/// Readers differ only in leniency: valheimCreative wants 13+ fields and
/// ignores extra ones, PlanBuild reads 10+ (scale optional), Expand World
/// adds zdoData and chance as fields 14-15. Our writer emits 13; our reader
/// takes 9+ and ignores any '#' section it does not know.
/// </summary>
public sealed class RoadBlueprint
{
    public string Name = "";
    public string Creator = "ProceduralRoads";
    public string Description = "";
    public string Category = "ProceduralRoads";
    /// <summary>Optional PlanBuild snap points. A kit unit has two: its
    /// near end (the origin the unit is held by) and its far end, where the
    /// next unit starts.</summary>
    public List<Vector3> SnapPoints = new();
    public List<BlueprintPiece> Pieces = new();

    /// <summary>The point the blueprint is held by when placed: its first snap
    /// point, or — the rule valheimCreative and Expand World apply to files
    /// without one — the bottom centre of the piece bounds.</summary>
    public Vector3 Anchor
    {
        get
        {
            if (SnapPoints.Count > 0)
                return SnapPoints[0];
            if (Pieces.Count == 0)
                return Vector3.zero;
            Vector3 min = Pieces[0].LocalPosition, max = Pieces[0].LocalPosition;
            foreach (BlueprintPiece p in Pieces)
            {
                min = new Vector3(Mathf.Min(min.x, p.LocalPosition.x), Mathf.Min(min.y, p.LocalPosition.y), Mathf.Min(min.z, p.LocalPosition.z));
                max = new Vector3(Mathf.Max(max.x, p.LocalPosition.x), Mathf.Max(max.y, p.LocalPosition.y), Mathf.Max(max.z, p.LocalPosition.z));
            }
            return new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
        }
    }

    /// <summary>How much of the crossing the blueprint covers along +z: from
    /// the anchor to the second snap point, or the pieces' extent along z.</summary>
    public float Length
    {
        get
        {
            if (SnapPoints.Count >= 2)
                return SnapPoints[1].z - SnapPoints[0].z;
            if (Pieces.Count == 0)
                return 0f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (BlueprintPiece p in Pieces)
            {
                min = Mathf.Min(min, p.LocalPosition.z);
                max = Mathf.Max(max, p.LocalPosition.z);
            }
            return max - min;
        }
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static RoadBlueprint Parse(string text)
    {
        RoadBlueprint bp = new();
        string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                section = "";
                if (line.Equals("#SnapPoints", StringComparison.OrdinalIgnoreCase)) section = "snap";
                else if (line.Equals("#Pieces", StringComparison.OrdinalIgnoreCase)) section = "pieces";
                else ReadHeader(bp, line);
                continue;
            }
            string[] f = line.Split(';');
            if (section == "snap" && f.Length >= 3)
            {
                bp.SnapPoints.Add(new Vector3(F(f[0]), F(f[1]), F(f[2])));
            }
            else if (section == "pieces" && f.Length >= 9)
            {
                BlueprintPiece p = new()
                {
                    Prefab = f[0].Trim(),
                    Category = f[1].Trim(),
                    LocalPosition = new Vector3(F(f[2]), F(f[3]), F(f[4])),
                    RotX = F(f[5]), RotY = F(f[6]), RotZ = F(f[7]), RotW = F(f[8]),
                };
                if (f.Length > 9 && f[9] != "\"\"")
                    p.Data = f[9].Trim();
                if (f.Length >= 13)
                    p.Scale = new Vector3(F(f[10]), F(f[11]), F(f[12]));
                bp.Pieces.Add(p);
            }
        }
        return bp;
    }

    private static void ReadHeader(RoadBlueprint bp, string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
            return;
        string key = line.Substring(1, colon - 1).Trim().ToLowerInvariant();
        string value = line.Substring(colon + 1).Trim();
        switch (key)
        {
            case "name": bp.Name = value; break;
            case "creator": bp.Creator = value; break;
            case "category": bp.Category = value; break;
            case "description":
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"");
                bp.Description = value;
                break;
        }
    }

    /// <summary>PlanBuild tolerates a decimal comma from locale-formatted exports.</summary>
    private static float F(string s) => float.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, Inv);

    /// <summary>valheimCreative's float format: round-trippable, invariant.</summary>
    private static string S(float v) => v.ToString("G9", Inv);

    public string Write()
    {
        StringBuilder sb = new();
        sb.Append("#Name:").Append(Name).Append('\n');
        sb.Append("#Creator:").Append(Creator).Append('\n');
        sb.Append("#Description:\"").Append(Description.Replace("\"", "\\\"")).Append("\"\n");
        sb.Append("#Category:").Append(Category).Append('\n');
        if (SnapPoints.Count > 0)
        {
            sb.Append("#SnapPoints\n");
            foreach (Vector3 s in SnapPoints)
                sb.Append(S(s.x)).Append(';').Append(S(s.y)).Append(';').Append(S(s.z)).Append('\n');
        }
        sb.Append("#Pieces\n");
        foreach (BlueprintPiece p in Pieces)
        {
            sb.Append(string.Join(";", new[]
            {
                p.Prefab, p.Category,
                S(p.LocalPosition.x), S(p.LocalPosition.y), S(p.LocalPosition.z),
                S(p.RotX), S(p.RotY), S(p.RotZ), S(p.RotW),
                p.Data,
                S(p.Scale.x), S(p.Scale.y), S(p.Scale.z),
            })).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// The per-file load offset from valheimCreative's sidecar
    /// blueprint-metadata.json ({"blueprints": {"name.blueprint": {"loadYOffset": -1.0}}},
    /// keyed by file name or stem, a bare number in the legacy form). Read
    /// with a pattern rather than a JSON library so the harness needs none.
    /// </summary>
    public static float ReadLoadYOffset(string metadataJson, string fileName)
    {
        if (string.IsNullOrEmpty(metadataJson))
            return 0f;
        string stem = fileName.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - ".blueprint".Length)
            : fileName;
        foreach (string key in new[] { stem + ".blueprint", stem })
        {
            Match m = Regex.Match(metadataJson, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\{[^}]*\\}|-?[0-9.]+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;
            string value = m.Groups[1].Value;
            if (value.StartsWith("{", StringComparison.Ordinal))
            {
                Match o = Regex.Match(value, "\"loadYOffset\"\\s*:\\s*(-?[0-9.]+)", RegexOptions.IgnoreCase);
                return o.Success && float.TryParse(o.Groups[1].Value, NumberStyles.Float, Inv, out float inner) ? inner : 0f;
            }
            return float.TryParse(value, NumberStyles.Float, Inv, out float legacy) ? legacy : 0f;
        }
        return 0f;
    }
}

/// <summary>
/// Quaternion arithmetic in Unity's conventions (Quaternion.Euler applies
/// z, then x, then y: q = qy * qx * qz; left-handed frame, +y up), written
/// out so the harness — which has no UnityEngine — produces the very numbers
/// the game will load.
/// </summary>
public static class BlueprintMath
{
    private const double DegToRad = Math.PI / 180.0;

    public static (float x, float y, float z, float w) FromEuler(float pitchDegrees, float yawDegrees, float rollDegrees)
    {
        double hx = pitchDegrees * DegToRad * 0.5, hy = yawDegrees * DegToRad * 0.5, hz = rollDegrees * DegToRad * 0.5;
        (double x, double y, double z, double w) qx = (Math.Sin(hx), 0, 0, Math.Cos(hx));
        (double x, double y, double z, double w) qy = (0, Math.Sin(hy), 0, Math.Cos(hy));
        (double x, double y, double z, double w) qz = (0, 0, Math.Sin(hz), Math.Cos(hz));
        (double x, double y, double z, double w) q = Mul(qy, Mul(qx, qz));
        return ((float)q.x, (float)q.y, (float)q.z, (float)q.w);
    }

    /// <summary>Inverse of <see cref="FromEuler"/>: the (pitch, yaw, roll) in
    /// degrees whose Unity Euler rotation is this quaternion. Pitch lies in
    /// [-90, 90]; at the poles roll is folded into yaw.</summary>
    public static (float pitch, float yaw, float roll) ToEuler(float qx, float qy, float qz, float qw)
    {
        double n = Math.Sqrt((double)qx * qx + (double)qy * qy + (double)qz * qz + (double)qw * qw);
        if (n < 1e-12)
            return (0f, 0f, 0f);
        double x = qx / n, y = qy / n, z = qz / n, w = qw / n;
        // Rotation matrix rows of Ry*Rx*Rz: m12 = -sin(pitch), m02/m22 give yaw, m10/m11 give roll.
        double m00 = 1 - 2 * (y * y + z * z), m02 = 2 * (x * z + w * y);
        double m10 = 2 * (x * y + w * z), m11 = 1 - 2 * (x * x + z * z), m12 = 2 * (y * z - w * x);
        double m20 = 2 * (x * z - w * y), m22 = 1 - 2 * (x * x + y * y);
        double sinPitch = Math.Max(-1.0, Math.Min(1.0, -m12));
        double pitch = Math.Asin(sinPitch), yaw, roll;
        if (Math.Abs(sinPitch) < 0.9999995)
        {
            yaw = Math.Atan2(m02, m22);
            roll = Math.Atan2(m10, m11);
        }
        else
        {
            yaw = Math.Atan2(-m20, m00);
            roll = 0;
        }
        return ((float)(pitch / DegToRad), (float)(yaw / DegToRad), (float)(roll / DegToRad));
    }

    public static (float x, float y, float z, float w) Mul((float x, float y, float z, float w) a, (float x, float y, float z, float w) b)
    {
        (double x, double y, double z, double w) r = Mul((a.x, a.y, a.z, a.w), ((double)b.x, b.y, b.z, b.w));
        return ((float)r.x, (float)r.y, (float)r.z, (float)r.w);
    }

    private static (double x, double y, double z, double w) Mul((double x, double y, double z, double w) a, (double x, double y, double z, double w) b) =>
        (a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
         a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
         a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
         a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

    /// <summary>v rotated by the unit quaternion q.</summary>
    public static Vector3 Rotate((float x, float y, float z, float w) q, Vector3 v)
    {
        // v' = v + 2w (q × v) + 2 q × (q × v)
        float cx = q.y * v.z - q.z * v.y, cy = q.z * v.x - q.x * v.z, cz = q.x * v.y - q.y * v.x;
        float ccx = q.y * cz - q.z * cy, ccy = q.z * cx - q.x * cz, ccz = q.x * cy - q.y * cx;
        return new Vector3(v.x + 2f * (q.w * cx + ccx), v.y + 2f * (q.w * cy + ccy), v.z + 2f * (q.w * cz + ccz));
    }
}
