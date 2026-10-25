using System;
using System.Collections.Generic;
using System.Collections;
using Jotunn.Entities;
using Jotunn.Managers;
using HarmonyLib;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Read-only network snapshots to peers running this command build.
/// Clients cannot submit paths or choose terrain; only the host can append.</summary>
public static class ManualRoadNetworkSync
{
    private static CustomRPC snapshotRpc = null!;

    // Jotunn compresses and fragments snapshots, respecting Steam's message limit.
    // A full world is several megabytes: sending it through one vanilla RPC
    // blocks the socket's send queue and eventually disconnects the client.
    public static void Register()
    {
        snapshotRpc = NetworkManager.Instance.AddRPC("ManualRoadSnapshot", RequestSnapshot, ReceiveSnapshot);
    }

    private static IEnumerator RequestSnapshot(long sender, ZPackage package)
    {
        if (ZNet.instance.IsServer() && ZNet.instance.GetPeer(sender) != null)
        {
            readers.Add(sender);
            if (RoadNetworkGenerator.RoadsAvailable) Send(sender);
        }
        yield break;
    }

    private static IEnumerator ReceiveSnapshot(long sender, ZPackage package)
    {
        Receive(sender, package);
        yield break;
    }
    private static readonly HashSet<long> readers = new();
    private static float nextRetry;

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    private static class RetryLocalApplication
    {
        private static void Postfix()
        {
            if (RoadLifecycleManager.IsGenerating) return;
            if (RoadSpatialGrid.AppendCount == 0 || Time.realtimeSinceStartup < nextRetry) return;
            nextRetry = Time.realtimeSinceStartup + 2f;
            try
            {
                RoadTerrainModifier.ApplyAdditionsToLoadedZones();
                if (ZNet.instance.IsServer()) BridgePlacement.ApplyPendingAppends();
            }
            catch (Exception ex) { ProceduralRoadsPlugin.ProceduralRoadsLogger.LogWarning($"Manual road application remains pending: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Start))]
    private static class RegisterOnStart
    {
        private static void Postfix()
        {
            readers.Clear(); nextRetry = 0;

        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    private static class RequestOnArrival
    {
        private static void Postfix()
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer())
                snapshotRpc.Initiate();
        }
    }

    public static void Publish()
    {
        if (RoadLifecycleManager.IsGenerating) return;
        if (ZRoutedRpc.instance == null || !ZNet.instance.IsServer()) return;
        readers.RemoveWhere(id => ZNet.instance.GetPeer(id) == null);
        foreach (long id in readers) Send(id);
    }
    /// <summary>
    /// Version 2 carries the zones the client must leave alone (NetworkCutoff):
    /// a client cannot see the save, so without them it would write road
    /// terrain into zones explored before the network. A version-1 snapshot
    /// (no such list) is refused, so a client never writes on a guess.
    /// Version 3 carries the grid in format 4 (the committed path length): a version-2 peer cannot read that grid,
    /// so the two refuse each other by version instead of failing inside the grid.
    /// </summary>
    internal const int SnapshotVersion = 3;

    private static void Send(long peer)
    {
        ZPackage? package = Snapshot();
        if (package != null) snapshotRpc.SendPackage(peer, package);
    }

    internal static ZPackage? Snapshot()
    {
        if (RoadLifecycleManager.IsGenerating) return null;
        byte[]? data = RoadSpatialGrid.SerializeAllRoadPoints();
        if (data == null) return null;
        var package = new ZPackage();
        package.Write(SnapshotVersion);
        package.Write(data);
        package.Write(RoadNetworkPersistence.SerializeRoadStartPoints(RoadNetworkGenerator.GetRoadStartPoints()));
        package.Write(RoadNetworkPersistence.SerializeRoadCrossings(RoadNetworkGenerator.GetRoadCrossings()));
        package.Write(NetworkCutoff.ClientList(RoadSpatialGrid.GetZonesWithRoadPoints()));
        return package;
    }

    private static void Receive(long sender, ZPackage package)
    {
        if (ZNet.instance == null || ZNet.instance.IsServer() || sender != ZRoutedRpc.instance.GetServerPeerID()) return;
        Apply(package);
    }

    internal static bool Apply(ZPackage package)
    {
        try
        {
            if (package.ReadInt() != SnapshotVersion) throw new InvalidOperationException("Unknown manual road snapshot version.");
            byte[] grid = package.ReadByteArray(), starts = package.ReadByteArray(), crossings = package.ReadByteArray(),
                excluded = package.ReadByteArray();
            if (grid.Length > 64 * 1024 * 1024 || starts.Length > 1024 * 1024 || crossings.Length > 16 * 1024 * 1024 ||
                excluded.Length > 16 * 1024 * 1024)
                throw new InvalidOperationException("Manual road snapshot is too large.");
            RoadNetworkGenerator.AcceptManualSnapshot(grid, starts, crossings, excluded);
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug($"Manual road snapshot applied: version={RoadSpatialGrid.RoadNetworkVersion}, additions={RoadSpatialGrid.AppendCount}, stored-records={RoadSpatialGrid.StoredPointRecords}, excluded zones={NetworkCutoff.ExcludedCount}");
            RoadTerrainModifier.ApplyAdditionsToLoadedZones();
            return true;
        }
        catch (Exception ex) { ProceduralRoadsPlugin.ProceduralRoadsLogger.LogError($"Road snapshot not applied: {ex}"); return false; }
    }
}
