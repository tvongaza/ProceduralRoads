using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Night plan 2026-09-03 task 5, harness-only spike of Tys's direction: a
/// bridge expressed as BLUEPRINTS — a START unit at the near bank, a SPAN
/// unit repeated along the crossing, an END unit at the far bank — read
/// from PlanBuild-style .blueprint text, tiled across a recorded crossing
/// into BridgePieces, then weathered by the seed. Nothing here reaches the
/// game; it proves the format, the tiling and the support model agree.
/// </summary>
public class BlueprintTests
{
    // ---- format ----

    /// <summary>One piece of a blueprint in the unit's local frame: +z runs
    /// along the bridge, +y up, origin at the unit's near snap point.</summary>
    public sealed class BlueprintPiece
    {
        public readonly string Prefab;
        public readonly Vector3 LocalPosition;
        public readonly float LocalYawDegrees;
        public BlueprintPiece(string prefab, Vector3 localPosition, float localYawDegrees)
        {
            Prefab = prefab; LocalPosition = localPosition; LocalYawDegrees = localYawDegrees;
        }
    }

    /// <summary>A parsed .blueprint: pieces plus the two snap points (near,
    /// far) that say how long the unit is along +z.</summary>
    public sealed class BlueprintUnit
    {
        public string Name = "";
        public List<BlueprintPiece> Pieces = new();
        public List<Vector3> SnapPoints = new();
        public float Length => SnapPoints.Count >= 2 ? SnapPoints[1].z - SnapPoints[0].z : 0f;
    }

    /// <summary>
    /// PlanBuild's text format as we understand it (to be verified against a
    /// real export before anything ships): '#Key:value' headers, a
    /// '#SnapPoints' section of 'x;y;z' lines, a '#Pieces' section of
    /// 'prefab;category;posX;posY;posZ;rotX;rotY;rotZ;rotW;additionalInfo;scaleX;scaleY;scaleZ'
    /// lines. Rotation is a quaternion; only its yaw matters for our flat
    /// bridge grammar, so the parser keeps yaw and drops the rest.
    /// </summary>
    public static BlueprintUnit Parse(string text)
    {
        var unit = new BlueprintUnit();
        string section = "";
        var inv = CultureInfo.InvariantCulture;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#"))
            {
                if (line.StartsWith("#Name:")) unit.Name = line.Substring(6).Trim();
                else if (line == "#SnapPoints" || line == "#Pieces") section = line;
                continue;
            }
            string[] f = line.Split(';');
            if (section == "#SnapPoints" && f.Length >= 3)
            {
                unit.SnapPoints.Add(new Vector3(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv)));
            }
            else if (section == "#Pieces" && f.Length >= 9)
            {
                var pos = new Vector3(float.Parse(f[2], inv), float.Parse(f[3], inv), float.Parse(f[4], inv));
                float qx = float.Parse(f[5], inv), qy = float.Parse(f[6], inv), qz = float.Parse(f[7], inv), qw = float.Parse(f[8], inv);
                float yaw = Mathf.Atan2(2f * (qw * qy + qx * qz), 1f - 2f * (qy * qy + qz * qz)) * 180f / Mathf.PI;
                unit.Pieces.Add(new BlueprintPiece(f[0], pos, yaw));
            }
        }
        return unit;
    }

    /// <summary>The three wood-kit units ship as embedded resources
    /// (ProceduralRoads.Tests/blueprints/*.blueprint), so both runtimes find
    /// them wherever the test assembly runs from.</summary>
    private static BlueprintUnit Load(string file)
    {
        using var stream = typeof(BlueprintTests).Assembly.GetManifestResourceStream("blueprints/" + file)
            ?? throw new FileNotFoundException("embedded blueprint missing: " + file);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    // ---- tiling ----

    /// <summary>
    /// Lays START, n × SPAN, END along the crossing line at deck height:
    /// the units' snap points chain (each unit's far snap point is the next
    /// unit's origin), the deck grades between the bank contact heights
    /// like BridgeLayout's, and every local position is rotated by the
    /// crossing's yaw. The last span may be shortened by the crossing's
    /// remainder, which is what a builder would do with the last plate.
    /// </summary>
    public static List<BridgePiece> Tile(RoadCrossing c, WorldGenerator world, BlueprintUnit start, BlueprintUnit span, BlueprintUnit end)
    {
        var pieces = new List<BridgePiece>();
        float bankFromH = BiomeBlendedHeight.GetBlendedHeight(c.FromBank.x, c.FromBank.y, world);
        float bankToH = BiomeBlendedHeight.GetBlendedHeight(c.ToBank.x, c.ToBank.y, world);
        float minDeck = c.WaterLevel + BridgeStyle.MeadowsWood.DeckFreeboard;
        float yaw = BridgeLayout.YawDegrees(c.Direction);
        Vector2 side = new(-c.Direction.y, c.Direction.x);

        float DeckAt(float along) => Mathf.Max(Mathf.Lerp(bankFromH, bankToH, Mathf.Clamp01(along / c.Width)), minDeck);

        void Place(BlueprintUnit unit, float originAlong)
        {
            foreach (BlueprintPiece p in unit.Pieces)
            {
                float along = originAlong + p.LocalPosition.z;
                Vector2 xz = c.FromBank + c.Direction * along + side * p.LocalPosition.x;
                pieces.Add(new BridgePiece
                {
                    Kind = KindOf(p.Prefab),
                    Prefab = p.Prefab,
                    Position = new Vector3(xz.x, DeckAt(along) + p.LocalPosition.y, xz.y),
                    YawDegrees = yaw + p.LocalYawDegrees,
                    HealthFraction = 1f,
                });
            }
        }

        float cursor = 0f;
        Place(start, cursor);
        cursor += start.Length;
        float spanBudget = c.Width - start.Length - end.Length;
        int spans = Mathf.Max(0, Mathf.FloorToInt(spanBudget / span.Length + 0.001f));
        for (int i = 0; i < spans; i++)
        {
            Place(span, cursor);
            cursor += span.Length;
        }
        Place(end, c.Width - end.Length);
        return pieces;
    }

    private static BridgePieceKind KindOf(string prefab) => prefab switch
    {
        "wood_pole2" => BridgePieceKind.Piling,
        "wood_beam" => BridgePieceKind.Beam,
        "wood_floor" => BridgePieceKind.Deck,
        _ => BridgePieceKind.Debris,
    };

    /// <summary>Posts stacked down to the bed: a blueprint post is one
    /// 2 m segment under the deck; below it the ground decides how many
    /// more, exactly as BridgeLayout's stations do.</summary>
    public static List<BridgePiece> GroundPosts(List<BridgePiece> pieces, WorldGenerator world)
    {
        var grounded = new List<BridgePiece>();
        foreach (BridgePiece p in pieces)
        {
            grounded.Add(p);
            if (p.Kind != BridgePieceKind.Piling) continue;
            float ground = BiomeBlendedHeight.GetBlendedHeight(p.Position.x, p.Position.z, world);
            for (float center = p.Position.y - 2f; center > ground; center -= 2f)
                grounded.Add(new BridgePiece { Kind = p.Kind, Prefab = p.Prefab, Position = new Vector3(p.Position.x, center, p.Position.z), YawDegrees = p.YawDegrees, HealthFraction = p.HealthFraction });
        }
        return grounded;
    }

    // ---- weather ----

    /// <summary>
    /// Seed-driven weathering, starting from the high points: decks and
    /// beams (the exposed parts) go first with a probability that peaks at
    /// mid-span, post COLUMNS go later and only whole (a column is one
    /// support: removing a segment from its middle would strand the rest),
    /// and every survivor is damaged. Posts are grounded by construction,
    /// so the result stays support-safe whatever falls.
    /// </summary>
    public static List<BridgePiece> Weather(List<BridgePiece> pieces, RoadCrossing c, int seed, float deckLoss = 0.6f, float postLoss = 0.15f)
    {
        var rng = new System.Random(seed);
        float MidCloseness(BridgePiece p)
        {
            float along = c.Along(new Vector2(p.Position.x, p.Position.z));
            return 1f - Mathf.Abs(along - c.Width * 0.5f) / (c.Width * 0.5f); // 0 at the banks, 1 mid-span
        }
        // One draw per post column, keyed by its footprint.
        var columnFalls = new Dictionary<(int, int), bool>();
        foreach (BridgePiece p in pieces)
        {
            if (p.Kind != BridgePieceKind.Piling) continue;
            var key = (Mathf.RoundToInt(p.Position.x * 10f), Mathf.RoundToInt(p.Position.z * 10f));
            if (!columnFalls.ContainsKey(key))
                columnFalls[key] = rng.NextDouble() < postLoss * MidCloseness(p);
        }
        var kept = new List<BridgePiece>();
        foreach (BridgePiece p in pieces)
        {
            bool falls;
            if (p.Kind == BridgePieceKind.Piling)
                falls = columnFalls[(Mathf.RoundToInt(p.Position.x * 10f), Mathf.RoundToInt(p.Position.z * 10f))];
            else
                falls = rng.NextDouble() < deckLoss * MidCloseness(p);
            if (falls) continue;
            kept.Add(new BridgePiece { Kind = p.Kind, Prefab = p.Prefab, Position = p.Position, YawDegrees = p.YawDegrees,
                HealthFraction = 0.3f + (float)rng.NextDouble() * 0.5f });
        }
        return kept;
    }

    // ---- tests ----

    private static (RoadCrossing crossing, WorldGenerator world) Crossing()
    {
        var world = new SupportModelTests.WideSteppedWorld();
        var path = new List<Vector2> { new(-64f, 0f), new(-56f, 0f), new(-48f, 0f), new(48f, 0f), new(56f, 0f), new(64f, 0f) };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));
        return (crossing, world);
    }

    [Fact]
    public void BlueprintTextParsesIntoPiecesAndSnapPoints()
    {
        var span = Load("wood-bridge-span.blueprint");
        Assert.Equal("ProceduralRoads wood bridge SPAN", span.Name);
        Assert.Equal(2f, span.Length);
        Assert.Equal(4, span.Pieces.Count);
        Assert.Contains(span.Pieces, p => p.Prefab == "wood_floor" && Mathf.Abs(p.LocalPosition.z - 1f) < 0.01f);
        Assert.All(span.Pieces, p => Assert.Equal(0f, p.LocalYawDegrees, 3));
    }

    [Fact]
    public void UnitsTileBankToBankAndEveryPieceIsGrounded()
    {
        var (c, world) = Crossing();
        var plan = GroundPosts(Tile(c, world, Load("wood-bridge-start.blueprint"), Load("wood-bridge-span.blueprint"), Load("wood-bridge-end.blueprint")), world);

        // Deck plates run continuously from the near bank to the far bank.
        var decks = plan.Where(p => p.Kind == BridgePieceKind.Deck).Select(p => c.Along(new Vector2(p.Position.x, p.Position.z))).OrderBy(a => a).ToList();
        Assert.True(decks.First() <= 0.5f, $"first plate at {decks.First():F1} m");
        Assert.True(decks.Last() >= c.Width - 0.5f, $"last plate at {decks.Last():F1} m of {c.Width:F1}");
        for (int i = 1; i < decks.Count; i++)
            Assert.True(decks[i] - decks[i - 1] <= 2.01f, $"gap of {decks[i] - decks[i - 1]:F1} m between plates");

        SupportModelTests.AssertGrounded(plan, BridgeStyle.MeadowsWood, world, "blueprint bridge");
    }

    [Fact]
    public void WeatherRemovesHighPiecesFirstAndKeepsThePlanGrounded()
    {
        var (c, world) = Crossing();
        var full = GroundPosts(Tile(c, world, Load("wood-bridge-start.blueprint"), Load("wood-bridge-span.blueprint"), Load("wood-bridge-end.blueprint")), world);
        int decksBefore = full.Count(p => p.Kind == BridgePieceKind.Deck);
        int postsBefore = full.Count(p => p.Kind == BridgePieceKind.Piling);
        for (int seed = 1; seed <= 10; seed++)
        {
            var ruin = Weather(full, c, seed);
            int decks = ruin.Count(p => p.Kind == BridgePieceKind.Deck);
            int posts = ruin.Count(p => p.Kind == BridgePieceKind.Piling);
            Assert.True(decks < decksBefore, "some deck must fall");
            Assert.True((float)decks / decksBefore < (float)posts / postsBefore, "decks fall before posts");
            var again = Weather(full, c, seed); // seeded: same ruin twice
            Assert.Equal(ruin.Select(p => (p.Prefab, p.Position, p.HealthFraction)), again.Select(p => (p.Prefab, p.Position, p.HealthFraction)));
            SupportModelTests.AssertGrounded(ruin, BridgeStyle.MeadowsWood, world, $"weathered seed {seed}");
        }
    }
}
