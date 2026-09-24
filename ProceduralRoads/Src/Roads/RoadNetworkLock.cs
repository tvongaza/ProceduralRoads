using System.Collections.Generic;
using BepInEx.Logging;

namespace ProceduralRoads;

/// <summary>
/// The production lock: PROCEDURALROADS_LOCK_NETWORK=1 promises that this
/// world's road network is never regenerated or re-applied, whatever a later
/// build changes.
///
/// The operating model: the first boot of a fresh production world runs
/// WITHOUT the lock and generates roads; once they have been walked and
/// accepted, the operator sets the lock and leaves it set. From then on the
/// saved network is the network. A bug-fix build can change how the network's
/// version is hashed, the bridge layout, or the way road terrain is written,
/// and unlocked each of those re-applies roads over zones players have since
/// dug, built on and planted -- the mod cannot tell a player's edit from its
/// own stale road. Locked, none of them may:
///
///   - The saved network does not load (missing, empty, unreadable, a load
///     that throws): no network is generated in its place. Roads are simply
///     absent for the session, which is the safe failure, and nothing writes
///     terrain, clears vegetation or rocks, or spawns or removes a bridge piece.
///   - road_generate, road_regen_island, GenerateRoads, RegenerateIslandAt and
///     road_bridges respawn refuse. Manual additions (road_connect, road_path,
///     road_mark) stay allowed: they only append to the network.
///   - A zone whose terrain compiler is stamped with a network that is neither
///     the loaded one nor one of its append ancestors is never written; a zone
///     with no stamp (never visited) still gets its roads, which is how new
///     areas get roads as players explore. A forced re-application is demoted
///     to an ordinary one, so a zone already carrying the network is left alone.
///   - A bridge layout older than the build's destroys nothing and spawns
///     nothing (see <see cref="FreezeBridges"/>).
///   - A vegetation record written for another network version is kept as it
///     is instead of being thrown away, so zones already cleared are not
///     cleared again.
///
/// Every refusal logs a line starting "[LOCK] refused", and a locked network
/// that loaded logs "[LOCK] road network locked: ..." once, so an operator can
/// grep for both. With the lock off nothing here changes what the mod does.
/// </summary>
public static class RoadNetworkLock
{
    private static ManualLogSource Log => ProceduralRoadsPlugin.ProceduralRoadsLogger;

    /// <summary>
    /// Whether the lock is on. Set from PROCEDURALROADS_LOCK_NETWORK when the
    /// configuration is applied; a process setting, not reset with the world.
    /// Tests set it directly and must put it back.
    /// </summary>
    public static bool Enabled;

    /// <summary>Locked, and the saved network did not load: no roads at all this session.</summary>
    public static bool RoadsDisabled { get; private set; }

    /// <summary>Why <see cref="RoadsDisabled"/> is set, for the log and road_bake.</summary>
    public static string? DisabledReason { get; private set; }

    /// <summary>
    /// Locked, and the saved bridges were laid out by another layout than this
    /// build's: no bridge piece from the plans is spawned or destroyed, and the
    /// saved bridge-zone record is not rewritten.
    /// </summary>
    public static bool BridgesFrozen { get; private set; }

    /// <summary>Whether this session's selftest verdict has been logged (exactly one per session).</summary>
    private static bool s_verdictLogged;

    private static readonly HashSet<Vector2s> s_refusedZones = new();
    private static readonly HashSet<Vector2s> s_demotedZones = new();

    /// <summary>Zones whose terrain the lock has refused this session.</summary>
    public static int RefusedZoneCount => s_refusedZones.Count;

    /// <summary>Forget this world's refusals (world unload, network reset). <see cref="Enabled"/> stays.</summary>
    public static void ResetSession()
    {
        RoadsDisabled = false;
        DisabledReason = null;
        BridgesFrozen = false;
        s_verdictLogged = false;
        s_refusedZones.Clear();
        s_demotedZones.Clear();
    }

    /// <summary>
    /// A terrain stamp the loaded network does not account for: not zero (never
    /// written), not the loaded network, and not one of the versions it was
    /// before a manual append. Writing such a zone replays the whole network's
    /// points over whatever the zone holds now.
    /// </summary>
    public static bool IsForeignStamp(int applied) =>
        applied != 0 && applied != RoadSpatialGrid.RoadNetworkVersion && !RoadSpatialGrid.IsAppendAncestor(applied);

    /// <summary>
    /// Whether the lock refuses a terrain write to this zone: always when roads
    /// are disabled, and for a foreign stamp. Logs once per zone per session.
    /// </summary>
    public static bool RefusesTerrain(Vector2s zone, int applied, string path)
    {
        if (!Enabled)
            return false;
        if (RoadsDisabled)
            return true;
        if (!IsForeignStamp(applied))
            return false;
        if (s_refusedZones.Add(zone))
            Log.LogWarning($"[LOCK] refused terrain write to zone {zone} ({path}): its terrain carries road network " +
                           $"{applied}, which is neither the loaded network {RoadSpatialGrid.RoadNetworkVersion} " +
                           "nor one of its append ancestors. The zone is left as it is.");
        return true;
    }

    /// <summary>Whether the lock has refused this zone's terrain this session (its rocks and vegetation are then left too).</summary>
    public static bool IsRefusedZone(Vector2s zone) => Enabled && s_refusedZones.Contains(zone);

    /// <summary>
    /// A forced write (explicit re-application) under the lock is an ordinary
    /// one: a zone already carrying the network is not written again. Returns
    /// the force to use, and says once per zone when it was dropped.
    /// </summary>
    public static bool Force(Vector2s zone, bool force)
    {
        if (!force || !Enabled)
            return force;
        if (s_demotedZones.Add(zone))
            Log.LogWarning($"[LOCK] refused forced re-application of road terrain in zone {zone}: " +
                           "only road points its terrain does not carry yet are written");
        return false;
    }

    /// <summary>
    /// Locked, and the saved network did not load. Nothing may stand in for it.
    /// </summary>
    public static void DisableRoads(string reason)
    {
        reason = OneLine(reason);
        RoadsDisabled = true;
        DisabledReason = reason;
        Log.LogError($"[LOCK] refused road generation: the road network is locked (PROCEDURALROADS_LOCK_NETWORK) " +
                     $"but the saved network did not load ({reason}). Roads are DISABLED for this session: no network " +
                     "is generated, no road terrain is written, no vegetation or rock is cleared and no bridge piece is " +
                     "spawned or removed. The saved data is left untouched. Fix the save, or unset the lock deliberately.");
        Verdict(pass: false, reason);
    }

    /// <summary>
    /// A command or API call that would build the network again. Returns the
    /// refusal (also logged) when locked, or null when it may run.
    /// </summary>
    public static string? RefuseRegeneration(string what)
    {
        if (!Enabled)
            return null;
        string message = $"[LOCK] refused {what}: the road network is locked (PROCEDURALROADS_LOCK_NETWORK), so it is " +
                         "never regenerated or re-applied. Manual additions (road_connect, road_path, road_mark) are " +
                         "still allowed.";
        Log.LogWarning(message);
        return message;
    }

    /// <summary>
    /// A manual road (road_connect, road_path, road_mark build). Appends are
    /// incremental and allowed under the lock, except in a session whose saved
    /// network did not load: the append would be saved as the whole network,
    /// over the saved one. Returns the refusal (also logged), or null.
    /// </summary>
    public static string? RefuseAppend()
    {
        if (!Enabled || !RoadsDisabled)
            return null;
        string message = $"[LOCK] refused manual road: the locked network did not load this session ({DisabledReason}); " +
                         "a road added now would be saved over it.";
        Log.LogWarning(message);
        return message;
    }

    /// <summary>
    /// The saved bridges were laid out by <paramref name="savedLayout"/> and
    /// this build lays them out as <paramref name="buildLayout"/>. Unlocked,
    /// the old pieces are destroyed everywhere and replaced. Locked, nothing is
    /// destroyed -- and nothing is spawned from the plans either, in zones not
    /// yet spawned included. The spawned-zone record of an older layout does
    /// not say which crossings it covers, and a crossing is often split over
    /// several zones: spawning this build's pieces into the zones that have
    /// none would put new-layout halves beside old-layout halves of the same
    /// bridge, which is exactly what the replacement exists to prevent. An
    /// unvisited crossing therefore gets no bridge this session -- its road
    /// still comes, and stops at the water, like a road with no bridge -- and
    /// the saved record is left untouched (not relabelled with this build's
    /// layout), so an operator who later lifts the lock still gets the
    /// ordinary replacement. Bridges owed to manual additions still spawn:
    /// they are new pieces for new roads.
    /// </summary>
    public static void FreezeBridges(int savedLayout, int buildLayout, int zones)
    {
        BridgesFrozen = true;
        string saved = savedLayout > 0
            ? $"the saved bridges were laid out by layout {savedLayout} and this build uses layout {buildLayout}. Their {zones} zone(s) keep their pieces"
            : $"the saved bridge record cannot be read by this build (layout {buildLayout}). Every standing piece is kept";
        Log.LogWarning($"[LOCK] refused bridge layout replacement: {saved}; no bridge piece is destroyed, and none is " +
                       "spawned from the plans this session, unvisited crossings included.");
    }

    /// <summary>Whether bridge pieces may be spawned from the plans (manual-road appends are separate).</summary>
    public static bool MaySpawnPlannedBridges => !Enabled || (!RoadsDisabled && !BridgesFrozen);

    /// <summary>Whether bridge pieces may be destroyed wholesale (a layout replacement or respawn): never under the lock.</summary>
    public static bool MayDestroyBridges(string what)
    {
        if (!Enabled)
            return true;
        Log.LogWarning($"[LOCK] refused {what}: the road network is locked, so no bridge piece is destroyed");
        return false;
    }

    /// <summary>
    /// The vegetation record was written for network <paramref name="savedVersion"/>
    /// and the loaded network is <paramref name="currentVersion"/>. Unlocked the
    /// record is dropped and every road zone is cleared again; locked it is
    /// kept. Returns whether to keep it, logging when that mattered.
    /// </summary>
    public static bool KeepVegetationRecord(int savedVersion, int currentVersion, int zones)
    {
        if (!Enabled)
            return false;
        if (savedVersion != 0 && savedVersion != currentVersion)
            Log.LogWarning($"[LOCK] refused to clear vegetation again: the cleared-zone record belongs to network " +
                           $"{savedVersion}, the loaded network is {currentVersion}; its {zones} zone(s) stay cleared");
        return true;
    }

    /// <summary>The line an operator greps for: the lock is on and this is the network it holds.</summary>
    public static void LogLocked(int crossings, int bridgeZones, int bridges)
    {
        Log.LogInfo($"[LOCK] road network locked: version={RoadSpatialGrid.RoadNetworkVersion}, " +
                    $"points={RoadSpatialGrid.TotalRoadPoints}, cells={RoadSpatialGrid.GridCellsWithRoads}, " +
                    $"appends={RoadSpatialGrid.AppendCount}, crossings={crossings}, bridgeZones={bridgeZones}, " +
                    $"bridgesFrozen={BridgesFrozen}. It is never regenerated or re-applied; zones never visited still get their roads.");
        Verdict(pass: true, $"network={RoadSpatialGrid.RoadNetworkVersion} points={RoadSpatialGrid.TotalRoadPoints} bridges={bridges}");
    }

    /// <summary>
    /// The boot's one verdict, for an automated gate: "[LOCK] selftest PASS
    /// network=&lt;version&gt; points=&lt;n&gt; bridges=&lt;n&gt;" (Info) when the
    /// locked network loaded, "[LOCK] selftest FAIL &lt;reason&gt;" (Error) when
    /// it did not. Only the locked decision reaches here, once per session;
    /// unlocked nothing is logged.
    /// </summary>
    private static void Verdict(bool pass, string detail)
    {
        if (s_verdictLogged)
            return;
        s_verdictLogged = true;
        if (pass)
            Log.LogInfo($"[LOCK] selftest PASS {detail}");
        else
            Log.LogError($"[LOCK] selftest FAIL {detail}");
    }

    private static string OneLine(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}
