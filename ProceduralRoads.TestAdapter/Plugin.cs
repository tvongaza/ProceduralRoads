using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;
using Valheim.Testing.Adapter;
using valheimCLI.Extensions;

namespace ProceduralRoads.TestAdapter;
[BepInPlugin("testing.proceduralroads.adapter", "ProceduralRoads Test Adapter", "0.1.0")]
[BepInDependency("warpalicious.ProceduralRoads")]
[BepInDependency("valheimCLI.valheimCLI")]
public sealed class Plugin : BaseUnityPlugin
{
    private ExtensionRegistration? _registration;
    // The toolkit's registration waits for ValheimCLI's extension API and serves the owned-session identity
    // (roads.testing/session), complete once the world is up and the roads are available.
    private IEnumerator Start() => TestExtension.Register("roads.testing", "0.1.0", "ROADS_TEST_SESSION_TOKEN",
        () => RoadNetworkGenerator.RoadsAvailable, registration => _registration = registration, Logger.LogError,
        new ExtensionCommand("terrain-persist", "Prepare one opt-in natural terrain replication fixture", PersistentTerrain.Run, role: ExtensionRole.Server, needsWorld: true),
        new ExtensionCommand("terrain-calibrate", "Run the opt-in two-zone declared terrain fixture", TerrainCalibration.Run, role: ExtensionRole.Server, needsWorld: true),
        new ExtensionCommand("generation-progress", "Read owned initial generation, including while loading or at menu", GenerationProgress, readOnly: true, needsWorld: false),
        new ExtensionCommand("cancel-generation", "Request cooperative initial-host cancellation: <expectedRunId>", CancelGeneration, role: ExtensionRole.Server, needsWorld: true),
        new ExtensionCommand("network", "Read completed network and outstanding append counts", Network, readOnly: true, role: ExtensionRole.Server, needsWorld: true),
        new ExtensionCommand("site-metrics", "Read route points near a site: <x> <z> <radius>", SiteMetrics, readOnly: true, role: ExtensionRole.Server, needsWorld: true),
        new ExtensionCommand("metadata-prefab", "Read the registered metadata prefab's network-view state", MetadataPrefab, readOnly: true, role: ExtensionRole.Server, needsWorld: true),
        new ExtensionCommand("bridge-zone", "Read marked bridge ZDOs in one zone: <zoneX> <zoneZ>", BridgeZone, readOnly: true, role: ExtensionRole.Server, needsWorld: true));
    private void OnDestroy() => _registration?.Dispose();
    private static IEnumerator GenerationProgress(ExtensionContext context)
    {
        if (context.Arguments.Count != 0) { context.Fail("usage", "generation-progress takes no arguments"); yield break; }
        var progress = RoadLifecycleManager.Progress;
        context.Succeed(new Dictionary<string, object?>
        {
            ["source"] = "roads-generation", ["complete"] = true,
            ["runId"] = progress.RunId, ["phase"] = progress.Phase,
            ["completedIslands"] = progress.Completed, ["totalIslands"] = progress.Total,
            ["elapsedSeconds"] = progress.ElapsedSeconds, ["totalKilometres"] = progress.TotalKilometres, ["frame"] = Time.frameCount,
            ["preparationStage"] = progress.PreparationStage, ["preparationCompleted"] = progress.PreparationCompleted,
            ["preparationTotal"] = progress.PreparationTotal, ["preparationUnit"] = progress.PreparationUnit,
            ["preparationActiveMilliseconds"] = progress.PreparationActiveMilliseconds,
            ["preparationMaxSliceMilliseconds"] = progress.PreparationMaxSliceMilliseconds,
            ["active"] = RoadLifecycleManager.InitialGenerationActive,
            ["isServer"] = ZNet.instance != null && ZNet.instance.IsServer(),
            ["isDedicated"] = ZNet.instance != null && ZNet.instance.IsDedicated(),
            ["roadsAvailable"] = RoadNetworkGenerator.RoadsAvailable,
            ["hasWorld"] = ZoneSystem.instance != null, ["hasLocalPlayer"] = Player.m_localPlayer != null,
            ["slots"] = progress.Islands.Select(row => new Dictionary<string, object?>
            {
                ["slot"] = row.Slot, ["assignment"] = row.Assignment, ["island"] = row.Island,
                ["active"] = row.Active, ["stage"] = row.Stage,
                ["processed"] = row.Processed, ["destinations"] = row.Destinations, ["reached"] = row.Reached,
                ["kilometres"] = row.Kilometres, ["pricesCompleted"] = row.PricesCompleted, ["pricesTotal"] = row.PricesTotal,
                ["searchSteps"] = row.SearchSteps, ["activeSearches"] = row.ActiveSearches, ["stageSeconds"] = row.StageSeconds
            }).ToArray()
        });
        yield break;
    }
    private static IEnumerator CancelGeneration(ExtensionContext context)
    {
        if (context.Arguments.Count != 1 || !long.TryParse(context.Arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out long runId) || runId <= 0)
        { context.Fail("usage", "cancel-generation <expectedRunId>, positive integer"); yield break; }
        if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.IsDedicated() || Game.instance == null || Hud.instance == null)
        { context.Fail("not_host", "Cancellation requires a local host loading screen."); yield break; }
        if (!RoadLifecycleManager.RequestCancelToMenu(runId))
        { context.Fail("rejected", "The run is stale, absent, or has begun publication."); yield break; }
        context.Succeed(new Dictionary<string, object?>
        {
            ["source"] = "roads-generation", ["accepted"] = true, ["runId"] = runId,
            ["phase"] = RoadLifecycleManager.Progress.Phase
        });
        yield break;
    }

    private static IEnumerator Network(ExtensionContext context)
    {
        if (context.Arguments.Count != 0) { context.Fail("usage", "network takes no arguments"); yield break; }
        if (RoadLifecycleManager.IsGenerating)
        {
            var progress = RoadLifecycleManager.Progress;
            context.Succeed(new Dictionary<string, object?>
            {
                ["source"] = "roads-progress", ["complete"] = false, ["phase"] = progress.Phase,
                ["completedIslands"] = progress.Completed, ["totalIslands"] = progress.Total
            });
            yield break;
        }
        context.Succeed(new Dictionary<string, object?>
        {
            ["source"] = "roads-memory", ["complete"] = RoadNetworkGenerator.RoadsAvailable,
            ["version"] = RoadSpatialGrid.RoadNetworkVersion, ["cells"] = RoadSpatialGrid.GridCellsWithRoads,
            ["points"] = RoadSpatialGrid.TotalRoadPoints, ["lengthMeters"] = RoadSpatialGrid.TotalRoadLength,
            ["crossings"] = RoadNetworkGenerator.GetRoadCrossings().Count,
            ["pendingZones"] = BridgeAppendQueue.Count, ["loadedFromSave"] = RoadNetworkGenerator.RoadsLoadedFromZDO
        }); yield break;
    }
    private static IEnumerator SiteMetrics(ExtensionContext context)
    {
        if (context.Arguments.Count != 3 ||
            !float.TryParse(context.Arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(context.Arguments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !float.TryParse(context.Arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) ||
            float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) ||
            float.IsNaN(radius) || float.IsInfinity(radius) ||
            Math.Abs(x) > 20000 || Math.Abs(z) > 20000 || radius < 1 || radius > 64)
        { context.Fail("usage", "site-metrics <x> <z> <radius>, finite world coordinates and 1..64 m radius"); yield break; }
        if (!RoadNetworkGenerator.RoadsAvailable)
        { context.Fail("not_ready", "Road network has not completed or loaded."); yield break; }
        var points = RoadSpatialGrid.GetRoadPointsNearPosition(new Vector3(x, 0, z), radius);
        if (points.Count > 2048)
        { context.Fail("census_limit", "More than 2048 road points near this site; choose a smaller radius."); yield break; }
        // Stable, culture-independent fingerprint of the complete ordered route-point set, without a huge CLI reply.
        string Line(RoadSpatialGrid.RoadPoint p) => string.Join("|", new[]
        {
            p.p.x.ToString("R", CultureInfo.InvariantCulture), p.p.y.ToString("R", CultureInfo.InvariantCulture),
            p.w.ToString("R", CultureInfo.InvariantCulture), p.h.ToString("R", CultureInfo.InvariantCulture),
            p.paintOnly ? "1" : "0", p.addition.ToString(CultureInfo.InvariantCulture)
        });
        var ordered = points.OrderBy(p => p.p.x).ThenBy(p => p.p.y).ThenBy(p => p.w).ThenBy(p => p.h)
            .ThenBy(p => p.paintOnly).ThenBy(p => p.addition).Select(Line).ToArray();
        byte[] digest;
        using (var sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", ordered)));
        var centre = new Vector2(x, z);
        var nearestPoint = points.OrderBy(p => (p.p - centre).sqrMagnitude)
            .ThenBy(p => p.p.x).ThenBy(p => p.p.y).FirstOrDefault();
        float nearest = points.Count == 0 ? -1 : Vector2.Distance(nearestPoint.p, centre);
        context.Succeed(new Dictionary<string, object?>
        {
            ["source"] = "roads-memory", ["complete"] = true, ["x"] = x, ["z"] = z,
            ["radius"] = radius, ["points"] = points.Count, ["paintOnlyPoints"] = points.Count(p => p.paintOnly),
            ["nearestMeters"] = nearest, ["nearestX"] = points.Count == 0 ? null : (object)nearestPoint.p.x,
            ["nearestZ"] = points.Count == 0 ? null : (object)nearestPoint.p.y,
            ["nearestPaintOnly"] = points.Count == 0 ? null : (object)nearestPoint.paintOnly,
            ["routeSha256"] = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant(),
        }); yield break;
    }
    private static IEnumerator BridgeZone(ExtensionContext context)
    {
        if (context.Arguments.Count != 2 || !int.TryParse(context.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(context.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) || Math.Abs((long)x) > ZoneTerrain.MaxZoneIndex || Math.Abs((long)z) > ZoneTerrain.MaxZoneIndex)
        { context.Fail("usage", "bridge-zone <zoneX> <zoneZ> within +/-" + ZoneTerrain.MaxZoneIndex); yield break; }
        // Every saved object in the zone, loaded or not; the census limit below is Roads' own, reported rather than thrown.
        var found = ZoneTerrain.ZoneObjects(new Vector2s(x, z), int.MaxValue);
        var pieces = found.Where(item => item.GetInt(BridgePlans.MarkerHash) == 1).ToArray();
        if (pieces.Length > 512) { context.Fail("census_limit", "More than 512 marked pieces in one zone; result omitted, not truncated."); yield break; }
        context.Succeed(new Dictionary<string, object?>
        {
            ["source"] = "zdo-store", ["complete"] = true, ["zoneX"] = x, ["zoneZ"] = z,
            ["pieces"] = pieces.Select(item => new Dictionary<string, object?>
            {
                ["id"] = item.m_uid.ToString(), ["prefabHash"] = item.GetPrefab(),
                ["position"] = new[] { item.GetPosition().x, item.GetPosition().y, item.GetPosition().z },
                ["rotation"] = new[] { item.GetRotation().x, item.GetRotation().y, item.GetRotation().z, item.GetRotation().w }
            }).ToArray()
        }); yield break;
    }

    private static IEnumerator MetadataPrefab(ExtensionContext context)
    {
        if (context.Arguments.Count != 0) { context.Fail("usage", "metadata-prefab takes no arguments"); yield break; }
        var prefab = ZNetScene.instance?.GetPrefab(RoadNetworkPersistence.MetadataPrefabName);
        var view = prefab?.GetComponent<ZNetView>();
        context.Succeed(new Dictionary<string, object?>
        {
            ["source"] = "znet-scene", ["complete"] = true,
            ["registered"] = prefab != null, ["hasZNetView"] = view != null,
            ["persistent"] = view != null && view.m_persistent,
        });
        yield break;
    }
}
