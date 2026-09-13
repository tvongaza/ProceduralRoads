using System.Collections.Generic;

namespace ProceduralRoads;

/// <summary>
/// What the server does about one zone's road terrain, decided from facts
/// about the zone alone. Pure logic: ServerTerrainBake gathers the facts from
/// the running game and carries the decision out.
///
/// Road terrain reaches every client as vanilla data: the zone's terrain
/// compiler (one _TerrainCompiler ZDO per zone) carries the height and paint
/// changes, and an unmodded client applies them to the terrain it generates
/// itself. The server therefore has to write that compiler for every zone a
/// road touches, including zones it never loads itself, and it must never make
/// a second compiler for a zone: the game destroys "another terrain compiler in
/// this area" on sight.
/// </summary>
public static class ServerBakePlanner
{
    public enum Action
    {
        /// <summary>No road network: nothing to write.</summary>
        NoNetwork,
        /// <summary>The zone has not been generated. It is written while it
        /// is generated (the ghost-generation hook), not before.</summary>
        LeaveToGeneration,
        /// <summary>The zone is loaded on this peer; the live-zone hooks
        /// (zone spawn, compiler coming alive) write it.</summary>
        LeaveToLiveZone,
        /// <summary>Its compiler already carries the current network.</summary>
        AlreadyCurrent,
        /// <summary>More than one saved compiler: writing either would leave
        /// the other to fight it, so neither is touched.</summary>
        DuplicateCompilers,
        /// <summary>A connected peer owns the compiler and is in the zone:
        /// taking it could lose that player's terrain edits. Try again later.</summary>
        WaitForOwner,
        /// <summary>No compiler yet: create the zone's one compiler.</summary>
        CreateCompiler,
        /// <summary>Write the roads into the zone's saved compiler.</summary>
        WriteSavedCompiler,
    }

    /// <summary>What the server knows about one saved compiler ZDO.</summary>
    public readonly struct Compiler
    {
        /// <summary>The road network version stamped on it (0 = none).</summary>
        public readonly int AppliedVersion;
        /// <summary>The owning peer's session id (0 = nobody).</summary>
        public readonly long Owner;
        /// <summary>Whether that owner is connected and has the zone in its active area.</summary>
        public readonly bool OwnerActiveHere;

        public Compiler(int appliedVersion, long owner, bool ownerActiveHere)
        {
            AppliedVersion = appliedVersion;
            Owner = owner;
            OwnerActiveHere = ownerActiveHere;
        }
    }

    public static Action Decide(int networkVersion, bool generated, bool loadedHere,
        IReadOnlyList<Compiler> compilers, long mySession)
    {
        if (networkVersion == 0)
            return Action.NoNetwork;
        if (!generated)
            return Action.LeaveToGeneration;
        if (loadedHere)
            return Action.LeaveToLiveZone;
        if (compilers.Count > 1)
            return Action.DuplicateCompilers;
        if (compilers.Count == 0)
            return Action.CreateCompiler;

        Compiler compiler = compilers[0];
        if (compiler.AppliedVersion == networkVersion)
            return Action.AlreadyCurrent;
        if (compiler.Owner != 0 && compiler.Owner != mySession && compiler.OwnerActiveHere)
            return Action.WaitForOwner;
        return Action.WriteSavedCompiler;
    }
}
