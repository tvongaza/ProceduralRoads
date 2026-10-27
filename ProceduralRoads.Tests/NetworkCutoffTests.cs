using System;
using System.Collections.Generic;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// Which zones automatic writers leave alone (NetworkCutoff): every zone generated before the network reached the
/// world, across sessions, downgrades and regenerations.
/// </summary>
public sealed class NetworkCutoffTests : IDisposable
{
    private static readonly Vector2s A = new(0, 0), B = new(1, 0), C = new(2, 0), D = new(3, 0);

    public NetworkCutoffTests() { ZoneSystem.instance = new ZoneSystem(); NetworkCutoff.Forget(); }
    public void Dispose() { NetworkCutoff.Forget(); ZoneSystem.instance = null; }

    [Fact]
    public void ANewNetworkOnAnExploredWorldLeavesTheExploredZonesAlone()
    {
        NetworkCutoff.SnapshotWorld(new[] { A, B });
        NetworkCutoff.UseNewNetwork();
        Assert.True(NetworkCutoff.Excludes(A));
        Assert.True(NetworkCutoff.Excludes(B));
        Assert.False(NetworkCutoff.Excludes(C));
    }

    [Fact]
    public void AFreshWorldExcludesNothing()
    {
        NetworkCutoff.SnapshotWorld(Array.Empty<Vector2s>());
        NetworkCutoff.UseNewNetwork();
        Assert.Equal(0, NetworkCutoff.ExcludedCount);
    }

    [Fact]
    public void ASaveWithoutARecordExcludesEveryGeneratedZone()
    {
        NetworkCutoff.SnapshotWorld(new[] { A, B });
        NetworkCutoff.UseLoadedNetwork(42, null);
        Assert.True(NetworkCutoff.Excludes(A));
        Assert.True(NetworkCutoff.Excludes(B));
    }

    [Fact]
    public void ZonesGeneratedWithTheNetworkStayItsAndZonesADowngradeGeneratedDoNot()
    {
        // Session 1 (this build): A was explored before, C generated with the network.
        NetworkCutoff.SnapshotWorld(new[] { A });
        NetworkCutoff.UseLoadedNetwork(42, null);
        byte[] record = NetworkCutoff.Record(42, new[] { A, C })!;
        // Session 2 (1.4.4, downgraded): generates D without any road. Session 3 (this build again):
        NetworkCutoff.Forget();
        NetworkCutoff.SnapshotWorld(new[] { A, C, D });
        NetworkCutoff.UseLoadedNetwork(42, record);
        Assert.True(NetworkCutoff.Excludes(A));
        Assert.False(NetworkCutoff.Excludes(C));
        Assert.True(NetworkCutoff.Excludes(D), "a zone generated while the network was not in place stays excluded");
    }

    [Fact]
    public void ARecordForAnotherNetworkCountsForNothing()
    {
        NetworkCutoff.SnapshotWorld(new[] { A, C });
        NetworkCutoff.UseLoadedNetwork(42, null);
        byte[] record = NetworkCutoff.Record(41, new[] { C })!;
        NetworkCutoff.UseLoadedNetwork(42, record);
        Assert.True(NetworkCutoff.Excludes(C));
    }

    [Fact]
    public void AnotherWorldNeverInheritsTheCutoff()
    {
        NetworkCutoff.SnapshotWorld(new[] { A });
        NetworkCutoff.UseNewNetwork();
        ZoneSystem.instance = new ZoneSystem();
        Assert.False(NetworkCutoff.Excludes(A));
    }

    /// <summary>
    /// road_generate or road_regen_island in a running session: zones generated this session under the old network
    /// may hold a player's work too, so the new network leaves them alone as well. The first network of a fresh world
    /// still reaches the zones generated around spawn this session.
    /// </summary>
    [Fact]
    public void ANetworkReplacedInTheSessionLeavesThisSessionsZonesAlone()
    {
        NetworkCutoff.SnapshotWorld(new[] { A });
        ZoneSystem.instance!.m_generatedZones.Add(A);
        ZoneSystem.instance.m_generatedZones.Add(B);   // generated this session, before the first network
        NetworkCutoff.UseNewNetwork();
        Assert.False(NetworkCutoff.Excludes(B), "the first network reaches this session's zones");
        ZoneSystem.instance.m_generatedZones.Add(C);   // generated with the first network
        NetworkCutoff.UseNewNetwork();          // a command replaces it
        Assert.True(NetworkCutoff.Excludes(A));
        Assert.True(NetworkCutoff.Excludes(B));
        Assert.True(NetworkCutoff.Excludes(C));
        Assert.False(NetworkCutoff.Excludes(D));
    }

    [Fact]
    public void ALateTeardownOfAnotherWorldKeepsThisOnesCutoffAndNoRecordIsWrittenForAStrangeWorld()
    {
        NetworkCutoff.SnapshotWorld(new[] { A });
        NetworkCutoff.UseNewNetwork();
        NetworkCutoff.Forget(new object());
        Assert.True(NetworkCutoff.Excludes(A));
        ZoneSystem.instance = new ZoneSystem();
        Assert.Null(NetworkCutoff.Record(1, new[] { A, B }));
    }
}
