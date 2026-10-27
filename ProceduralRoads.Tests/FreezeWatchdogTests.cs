using System.Threading;
using Xunit;

namespace ProceduralRoads.Tests;

/// <summary>
/// The freeze watchdog says where a stuck main thread is from what the main thread wrote down on its way in,
/// never by stopping it to read its stack: the game's Mono has no StackTrace(Thread), and a thread suspended at an
/// arbitrary point can hold a lock its watcher then needs (a game hung with every logging thread
/// blocked writing to the console).
/// </summary>
public class FreezeWatchdogTests
{
    /// <summary>A thread stuck inside a step (here on a lock another holds) is reported in that step, read while it
    /// stays stuck; out of it, it is reported outside.</summary>
    [Fact]
    public void AStuckMainThreadIsReportedInItsStepWithoutBeingTouched()
    {
        var gate = new object();
        var entered = new ManualResetEventSlim();
        Thread stuck;
        lock (gate)
        {
            stuck = new Thread(() =>
            {
                FreezeWatchdog.Enter("zone -145,11 pieces");
                entered.Set();
                lock (gate) { }
                FreezeWatchdog.Leave();
            });
            stuck.Start();
            Assert.True(entered.Wait(5000));
            Assert.StartsWith("in ProceduralRoads' zone -145,11 pieces", FreezeWatchdog.Where());
        }
        Assert.True(stuck.Join(5000));
        Assert.StartsWith("outside ProceduralRoads (its last step, zone -145,11 pieces, left", FreezeWatchdog.Where());
    }
}
