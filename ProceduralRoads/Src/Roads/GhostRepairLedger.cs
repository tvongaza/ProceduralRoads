using System;
using System.Collections.Generic;

namespace ProceduralRoads;

/// <summary>
/// Zones whose road terrain failed to go in while the game generated them for
/// a remote peer, kept until they can be written another way.
///
/// The ghost write is the only chance the queue gives a zone it left to
/// generation: once the game has generated it, it no longer takes the
/// new-zone path, and the queue that passed over it has long since drained.
/// So a single failure there -- a compiler that would not come alive, a save
/// refused, an exception -- left that zone without its road for the rest of
/// the session, recoverable only by road_bake again or a restart.
///
/// The budget is spent by FAILED WRITES, never by the passage of time. A zone
/// handed to the queue is marked as being in it and is not handed over again
/// until the queue reports back, so the queue never holds two copies of it and
/// a wait -- for its owner to leave, for the game to build its terrain, for a
/// backlog to clear -- costs it nothing. Only <see cref="FailedWrite"/> spends
/// an attempt, and when the attempts are gone the zone is a reported failure
/// rather than a silent one.
///
/// Pure bookkeeping; ServerTerrainBake supplies "has it generated yet?", does
/// the requeueing, and reports what each write did.
/// </summary>
public sealed class GhostRepairLedger
{
    /// <summary>How many writes may actually fail before the zone is called a failure.</summary>
    public const int MaxFailedWrites = 3;

    private enum State
    {
        /// <summary>Waiting to be handed to the queue.</summary>
        Pending,
        /// <summary>Handed over; the queue owes an answer.</summary>
        InQueue,
    }

    private struct Entry
    {
        public int FailedWrites;
        public State State;
    }

    private readonly Dictionary<Vector2s, Entry> m_zones = new();

    public int Count => m_zones.Count;

    public bool Holds(Vector2s zone) => m_zones.ContainsKey(zone);

    /// <summary>Whether the queue currently holds this zone (so it must not be handed over again).</summary>
    public bool IsQueued(Vector2s zone) => m_zones.TryGetValue(zone, out Entry entry) && entry.State == State.InQueue;

    /// <summary>How many writes have actually failed for this zone.</summary>
    public int FailedWrites(Vector2s zone) => m_zones.TryGetValue(zone, out Entry entry) ? entry.FailedWrites : 0;

    /// <summary>
    /// A ghost write failed here. A zone already being watched keeps the
    /// attempts it has spent and whatever the queue is doing with it.
    /// </summary>
    public void Record(Vector2s zone)
    {
        if (!m_zones.ContainsKey(zone))
            m_zones[zone] = new Entry { FailedWrites = 0, State = State.Pending };
    }

    public void Clear() => m_zones.Clear();

    /// <summary>
    /// The zones to hand to the queue now: those the game has finished
    /// generating and that the queue is not already holding. Handing one over
    /// spends nothing -- it only records that the queue owes an answer.
    /// </summary>
    public List<Vector2s> TakeReady(Func<Vector2s, bool> isGenerated)
    {
        var ready = new List<Vector2s>();
        if (m_zones.Count == 0)
            return ready;

        var zones = new List<Vector2s>(m_zones.Keys);
        foreach (Vector2s zone in zones)
        {
            Entry entry = m_zones[zone];
            if (entry.State == State.InQueue || !isGenerated(zone))
                continue;
            entry.State = State.InQueue;
            m_zones[zone] = entry;
            ready.Add(zone);
        }
        return ready;
    }

    /// <summary>Its roads are in, or it has nothing to write: stop watching it.</summary>
    public void Succeeded(Vector2s zone) => m_zones.Remove(zone);

    /// <summary>
    /// A write was attempted for this zone and failed. Spends one attempt and
    /// returns whether that was the last: the zone is then dropped, and the
    /// caller reports a real failure. Otherwise it goes back to waiting and
    /// will be handed to the queue again.
    /// </summary>
    public bool FailedWrite(Vector2s zone)
    {
        if (!m_zones.TryGetValue(zone, out Entry entry))
            return false;
        entry.FailedWrites++;
        if (entry.FailedWrites >= MaxFailedWrites)
        {
            m_zones.Remove(zone);
            return true;
        }
        entry.State = State.Pending;
        m_zones[zone] = entry;
        return false;
    }
}
