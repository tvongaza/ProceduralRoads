using System;
using System.Collections.Generic;
using UnityEngine;
using SoftReferenceableAssets;

namespace ProceduralRoads;

/// <summary>
/// Reads a location's levelling off its prefab, for LocationLevelling.
///
/// This is the engine half and is deliberately kept out of the pure sources:
/// it loads assets and walks Unity components, neither of which the test suite
/// can stand up. Everything it decides is a fact about the prefab; what that
/// means for a road is decided in LocationLevelling, which is tested.
///
/// A prefab is read once; only its copied operation values are kept. A world
/// has thousands of location instances and only dozens of distinct prefabs. Anything that goes wrong is
/// recorded as "this prefab levels nothing", which puts the road back on the
/// natural terrain under its own end - the behaviour before any of this.
/// </summary>
public static class LocationPrefabLevelling
{
    private static readonly Dictionary<string, IReadOnlyList<LevelOp>> m_byPrefab = new();

    /// <summary>Wire this up as LocationLevelling's source. Called from the plugin.</summary>
    public static void Install() => LocationLevelling.Source = OpsAt;

    public static void Reset() => m_byPrefab.Clear();

    private static IReadOnlyList<LevelOp>? OpsAt(Vector2 centre)
    {
        if (ZoneSystem.instance == null) return null;
        var list = ZoneSystem.instance.GetLocationList();
        if (list == null) return null;
        foreach (ZoneSystem.LocationInstance inst in list)
        {
            if (Mathf.Abs(inst.m_position.x - centre.x) > 0.5f ||
                Mathf.Abs(inst.m_position.z - centre.y) > 0.5f)
                continue;
            return ForPrefab(inst.m_location);
        }
        return null;
    }

    private static IReadOnlyList<LevelOp> ForPrefab(ZoneSystem.ZoneLocation location)
    {
        string name = location.m_prefab.Name;
        if (m_byPrefab.TryGetValue(name, out IReadOnlyList<LevelOp> cached))
            return cached;

        var ops = new List<LevelOp>();
        try
        {
            var reference = location.m_prefab;
            var loader = AssetBundleLoader.Instance;
            int index = loader.m_assetIDToLoaderIndex[reference.m_assetID];
            TemporaryAssetRead.Read(
                () => loader.m_assetLoaders[index].ReferenceCount,
                () => reference.Load(),
                () => reference.Release(),
                () =>
            {
                GameObject asset = reference.Asset;
                if (asset == null) return false;
                // Offsets are taken against the prefab's own root rather than
                // assumed to be zero: a prefab authored off its origin would
                // otherwise report every operation as concentric.
                Vector3 root = asset.transform.position;
                foreach (TerrainModifier modifier in asset.GetComponentsInChildren<TerrainModifier>(true))
                {
                    if (!modifier.m_level) continue;
                    Vector3 local = modifier.transform.position - root;
                    ops.Add(new LevelOp(local.x, local.z, local.y + modifier.m_levelOffset,
                        modifier.m_levelRadius, modifier.m_square));
                }
                return true;
            });
        }
        catch (Exception e)
        {
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogDebug(
                $"Could not read levelling for {name}: {e.GetType().Name}; road ends there meet natural terrain");
            ops.Clear();
        }

        m_byPrefab[name] = ops;
        return ops;
    }
}
