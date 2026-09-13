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
/// A zone recorded here is put back in the queue once the game has finished
/// generating it, where it is written on a temporary terrain like any other
/// generated zone. Retries are bounded: a zone that keeps coming back is
/// reported as a real failure rather than tried forever.
///
/// Pure bookkeeping; ServerTerrainBake supplies "has it generated yet?" and
/// does the requeueing.
/// </summary>
public sealed class GhostRepairLedger
{
    /// <summary>How many times a zone may be handed back to the queue before it is called failed.</summary>
    public const int MaxAttempts = 3;

    private readonly Dictionary<Vector2s, int> m_attempts = new();

    public int Count => m_attempts.Count;

    public bool Holds(Vector2s zone) => m_attempts.ContainsKey(zone);

    /// <summary>A ghost write failed here. Keeps the attempts already spent on it.</summary>
    public void Record(Vector2s zone)
    {
        if (!m_attempts.ContainsKey(zone))
            m_attempts[zone] = 0;
    }

    public void Clear() => m_attempts.Clear();

    /// <summary>
    /// The zones to put back in the queue now: those the game has finished
    /// generating. Each spends an attempt and STAYS here until its write
    /// succeeds (<see cref="Succeeded"/>), so a zone that keeps failing is
    /// handed back again rather than quietly forgotten -- and once it has
    /// spent every attempt it goes to <paramref name="gaveUp"/> instead, which
    /// the caller counts as a failure. Zones the game has not finished
    /// generating are left here untouched.
    /// </summary>
    public List<Vector2s> TakeReady(Func<Vector2s, bool> isGenerated, List<Vector2s> gaveUp)
    {
        var ready = new List<Vector2s>();
        if (m_attempts.Count == 0)
            return ready;

        var zones = new List<Vector2s>(m_attempts.Keys);
        foreach (Vector2s zone in zones)
        {
            if (!isGenerated(zone))
                continue;
            int spent = m_attempts[zone] + 1;
            if (spent > MaxAttempts)
            {
                m_attempts.Remove(zone);
                gaveUp.Add(zone);
            }
            else
            {
                m_attempts[zone] = spent;
                ready.Add(zone);
            }
        }
        return ready;
    }

    /// <summary>Its roads are in (or it has nothing to write): stop watching it.</summary>
    public void Succeeded(Vector2s zone) => m_attempts.Remove(zone);
}
