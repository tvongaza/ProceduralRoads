using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Exhibits for Tys's four open decisions (2026-09-02): each test writes a
/// render or a data dump next to the test assembly. Not assertions about
/// behaviour — the assertions live in the feature tests — but they must
/// keep producing their files so the decision brief can be regenerated.
/// </summary>
public class DecisionExhibitTests
{
    private static string Out(string name) =>
        Path.Combine(Path.GetDirectoryName(typeof(DecisionExhibitTests).Assembly.Location)!, name);

    /// <summary>80 m flat-bottomed sailable river, level banks.</summary>
    private sealed class WideChannelWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 220f || Mathf.Abs(wy) > 120f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax <= 35f) return 26f;
            if (ax >= 45f) return 32f;
            return Mathf.Lerp(26f, 32f, (ax - 35f) / 10f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 80f);
            width = weight > 0f ? 160f : 0f;
        }
    }

    private static RoadCrossing WideCrossing(WorldGenerator world)
    {
        var path = new RoadPathfinder(world).FindPath(new Vector2(-160f, 0f), new Vector2(160f, 0f));
        Assert.NotNull(path);
        return Assert.Single(RoadCrossingDetector.Detect(path!, world));
    }

    private static void DumpPlan(string file, RoadCrossing c, List<BridgePiece> plan)
    {
        using var w = new StreamWriter(file);
        w.WriteLine("kind,prefab,along,y,across");
        foreach (var p in plan)
        {
            Vector2 rel = new(p.Position.x - c.FromBank.x, p.Position.z - c.FromBank.y);
            float along = Vector2.Dot(rel, c.Direction);
            float across = rel.x * c.Direction.y - rel.y * c.Direction.x;
            w.WriteLine($"{p.Kind},{p.Prefab},{along:F2},{p.Position.y:F2},{across:F2}");
        }
    }

    [Fact]
    public void Decision1_RuinRule_CurrentVsPersistentPiers()
    {
        var world = new WideChannelWorld();
        var c = WideCrossing(world);
        var current = BridgeLayout.Solve(c, world, 42, BridgeStyle.MeadowsWood);
        var piers = BridgeLayout.Solve(c, world, 42, BridgeStyle.MeadowsWood.WithPierPersistence(0.85f));
        DumpPlan(Out("decision1-current.csv"), c, current);
        DumpPlan(Out("decision1-piers.csv"), c, piers);
        File.WriteAllText(Out("decision1-crossing.txt"),
            $"width={c.Width:F1} water={c.WaterLevel} bed={c.RiverbedHeight:F1} fairwayWidth={c.FairwayWidth:F1} fairwayMid={Vector2.Dot(c.FairwayCenter - c.FromBank, c.Direction):F1} bankFrom={world.GetHeight(c.FromBank.x, c.FromBank.y):F2} bankTo={world.GetHeight(c.ToBank.x, c.ToBank.y):F2}");
        Assert.True(piers.Count(p => p.Kind == BridgePieceKind.Piling) >= current.Count(p => p.Kind == BridgePieceKind.Piling));
        // Default plans must be byte-identical to before the knob existed.
        var again = BridgeLayout.Solve(c, world, 42, BridgeStyle.MeadowsWood);
        Assert.Equal(current.Count, again.Count);
    }

    private sealed class FordGullyWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 100f || Mathf.Abs(wy) > 100f) return 20f;
            float ax = Mathf.Abs(wx);
            if (ax < 6f) return 29.5f;
            if (ax < 9f) return Mathf.Lerp(29.5f, 32f, (ax - 6f) / 3f);
            return 32f;
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = Mathf.Clamp01(1f - Mathf.Abs(wx) / 12f);
            width = weight > 0f ? 24f : 0f;
        }
    }

    [Fact]
    public void FordStyles_SpanPlanAndWadeProfile()
    {
        var world = new FordGullyWorld();
        var path = new List<Vector2> { new(-32f, 0f), new(-24f, 0f), new(-16f, 0f), new(16f, 0f), new(24f, 0f), new(32f, 0f) };
        var crossing = Assert.Single(RoadCrossingDetector.Detect(path, world));
        Assert.Equal(CrossingKind.Ford, crossing.Kind);
        crossing.Style = FordStyle.Span;
        var plan = BridgeLayout.Solve(crossing, world, 7, BridgeStyle.MeadowsWood);
        DumpPlan(Out("fordstyle-span.csv"), crossing, plan);
        File.WriteAllText(Out("fordstyle-span.txt"),
            $"width={crossing.Width:F1} water={crossing.WaterLevel} bed={crossing.RiverbedHeight:F1} bankFrom={world.GetHeight(crossing.FromBank.x, crossing.FromBank.y):F2} bankTo={world.GetHeight(crossing.ToBank.x, crossing.ToBank.y):F2} fromX={crossing.FromBank.x:F1} toX={crossing.ToBank.x:F1}");
        Assert.NotEmpty(plan);
    }

    private sealed class KneeDeepGullyWorld : WorldGenerator
    {
        public override float GetHeight(float wx, float wy) => Mathf.Abs(wx) < 6f ? 29.4f : 33f;
    }

    [Fact]
    public void Decision2_FordProfile()
    {
        var world = new KneeDeepGullyWorld();
        RoadSpatialGrid.Clear();
        try
        {
            var path = new List<Vector2>();
            for (float x = -40f; x <= 40f; x += 4f) path.Add(new Vector2(x, 0f));
            RoadSpatialGrid.AddRoadPath(path, 4f, world);
            using var w = new StreamWriter(Out("decision2-ford-profile.csv"));
            w.WriteLine("x,terrain,road");
            for (float x = -30f; x <= 30f; x += 1f)
            {
                var near = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(x, 0f, 0f), 1.2f);
                float road = near.Count > 0 ? near.Min(rp => rp.h) : float.NaN;
                w.WriteLine($"{x:F0},{world.GetHeight(x, 0f):F2},{road:F2}");
            }
        }
        finally { RoadSpatialGrid.Clear(); }
        Assert.True(File.Exists(Out("decision2-ford-profile.csv")));
    }

    /// <summary>Wide river ending at y = 600; rough ground everywhere.</summary>
    private sealed class RiverWithAnEndWorld : WorldGenerator
    {
        private static float Hash(int x, int y)
        {
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xFFFF) / 65535f; }
        }
        public override float GetHeight(float wx, float wy)
        {
            if (Mathf.Abs(wx) > 220f || wy < -120f || wy > 700f) return 20f;
            // Rough ground everywhere EXCEPT the crossing approach (level within
            // 20 m of the straight line), so the bridge is refused or taken on
            // COST alone, never on bank delta — and the detour is honestly rough.
            float ax = Mathf.Abs(wx);
            bool approach = ax <= 60f && Mathf.Abs(wy) <= 20f;
            float rough = approach ? 0f
                : (Hash(Mathf.FloorToInt(wx / 6f), Mathf.FloorToInt(wy / 6f)) - 0.5f) * 7f;
            if (wy > 600f) return 33f + rough;
            if (ax <= 35f) return 26f;
            if (ax >= 45f) return 33f + rough;
            return Mathf.Lerp(26f, 33f, (ax - 35f) / 10f);
        }
        public override Heightmap.Biome GetBiome(float wx, float wy) =>
            GetHeight(wx, wy) < RoadConstants.SeaLevel - 2f ? Heightmap.Biome.Ocean : Heightmap.Biome.Meadows;
        public override void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            weight = wy > 600f ? 0f : Mathf.Clamp01(1f - Mathf.Abs(wx) / 80f);
            width = weight > 0f ? 160f : 0f;
        }
    }

    [Fact]
    public void Decision4_BridgeCost_OldVsLastResort()
    {
        var world = new RiverWithAnEndWorld();
        var from = new Vector2(-160f, 0f); var to = new Vector2(160f, 0f);
        var variants = new (string name, float fixedCost, float perMeter, byte r, byte g, byte b)[]
        {
            ("old 20000 flat", 20000f, 0f, 240, 90, 60),
            ("current 50000 + 400/m", RoadConstants.BridgeCrossingPenalty, RoadConstants.BridgeCostPerMeter, 250, 230, 90),
            ("stricter 50000 + 1500/m", 50000f, 1500f, 120, 220, 250),
        };
        var paths = new List<(List<Vector2>, byte, byte, byte)>();
        using var log = new StreamWriter(Out("decision4-bridge-cost.txt"));
        foreach (var v in variants)
        {
            var pf = new RoadPathfinder(world) { BridgeCrossingPenalty = v.fixedCost, BridgeCostPerMeter = v.perMeter };
            var path = pf.FindPath(from, to);
            Assert.NotNull(path);
            bool bridged = false; float length = 0f;
            for (int i = 1; i < path!.Count; i++)
            {
                length += Vector2.Distance(path[i - 1], path[i]);
                Vector2 mid = (path[i - 1] + path[i]) * 0.5f;
                world.GetRiverWeight(mid.x, mid.y, out float w, out _);
                if (w > RoadConstants.RiverImpassableThreshold && Vector2.Distance(path[i - 1], path[i]) > RoadPathfinder.CellSize * 1.5f) bridged = true;
            }
            log.WriteLine($"{v.name}: {(bridged ? "BRIDGE" : "DETOUR")} length={length:F0}m");
            paths.Add((path, v.r, v.g, v.b));
        }
        var markers = new List<(Vector2, byte, byte, byte)> { (from, 255, 255, 255), (to, 255, 255, 255) };
        WorldRenderer.RenderCentered(world, paths, markers, Out("decision4-bridge-cost.bmp"), new Vector2(0f, 280f), 420f, 1.6f);
        Assert.True(File.Exists(Out("decision4-bridge-cost.bmp")));
    }
}
