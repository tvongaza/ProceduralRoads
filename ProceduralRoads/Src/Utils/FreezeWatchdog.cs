using System;
using System.Diagnostics;
using System.Threading;

namespace ProceduralRoads;

/// <summary>
/// Says when the game's main thread stops advancing frames, and which ProceduralRoads step it was in.
///
/// A run froze after laying its site approaches: the game kept
/// running, its log ended with two music lines and then only valheimCLI timing
/// out every command; nothing said what the main thread was doing. A thread of
/// its own watches the frame count the plugin bumps every frame; when it has not
/// moved for <see cref="StallSeconds"/>, it logs a warning naming the last
/// ProceduralRoads step the main thread entered (<see cref="Enter"/>) and whether
/// it left it, and again every <see cref="RepeatSeconds"/> while it stays stuck.
/// It never stops the main thread to read its stack.
///
/// A loading screen or a world save can hold the main thread that long too; each
/// is logged once, the cost of knowing. On with PROCEDURALROADS_FREEZE_WATCHDOG;
/// off by default, and then no thread is started and no caller records a step
/// (every caller checks <see cref="Enabled"/>).
/// </summary>
internal static class FreezeWatchdog
{
    /// <summary>PROCEDURALROADS_FREEZE_WATCHDOG: watch the main thread. Off by default.</summary>
    internal static readonly bool Enabled = DebugSwitches.Flag("FREEZE_WATCHDOG", false);

    public const int StallSeconds = 20, RepeatSeconds = 60;

    private static Thread? s_main;
    private static Thread? s_watcher;
    private static long s_lastFrameTicks;
    private static int s_frames;

    /// <summary>Start watching the calling thread (the main thread, from the plugin's Awake).</summary>
    public static void Start()
    {
        if (!Enabled || s_watcher != null) return;
        s_main = Thread.CurrentThread;
        Interlocked.Exchange(ref s_lastFrameTicks, Stopwatch.GetTimestamp());
        s_watcher = new Thread(Watch) { IsBackground = true, Name = "ProceduralRoads freeze watchdog" };
        s_watcher.Start();
    }

    private static volatile string? s_step;
    private static long s_stepTicks;
    private static bool s_inStep;

    /// <summary>The main thread enters a ProceduralRoads step (a zone's pieces, its terrain, a tick).</summary>
    public static void Enter(string step)
    {
        s_step = step;
        Interlocked.Exchange(ref s_stepTicks, Stopwatch.GetTimestamp());
        Volatile.Write(ref s_inStep, true);
    }

    /// <summary>The main thread is back out of the step it last entered.</summary>
    public static void Leave() => Volatile.Write(ref s_inStep, false);

    /// <summary>Where the main thread last was, as far as ProceduralRoads knows, read without touching it.</summary>
    internal static string Where()
    {
        string? step = s_step;
        if (step == null) return "no ProceduralRoads step entered yet";
        double ago = (Stopwatch.GetTimestamp() - Interlocked.Read(ref s_stepTicks)) / (double)Stopwatch.Frequency;
        return Volatile.Read(ref s_inStep)
            ? $"in ProceduralRoads' {step}, entered {ago:F0} s ago"
            : $"outside ProceduralRoads (its last step, {step}, left; entered {ago:F0} s ago)";
    }

    /// <summary>Called every frame on the main thread.</summary>
    public static void Frame()
    {
        Interlocked.Increment(ref s_frames);
        Interlocked.Exchange(ref s_lastFrameTicks, Stopwatch.GetTimestamp());
    }

    private static void Watch()
    {
        long reportedAt = 0;
        int reportedFrames = -1;
        while (true)
        {
            Thread.Sleep(1000);
            try
            {
                long last = Interlocked.Read(ref s_lastFrameTicks);
                double stalled = (Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency;
                int frames = Volatile.Read(ref s_frames);
                if (stalled < StallSeconds) { reportedFrames = -1; continue; }
                double sinceReport = (Stopwatch.GetTimestamp() - reportedAt) / (double)Stopwatch.Frequency;
                if (frames == reportedFrames && sinceReport < RepeatSeconds) continue;
                reportedFrames = frames;
                reportedAt = Stopwatch.GetTimestamp();
                ProceduralRoadsPlugin.ProceduralRoadsLogger.LogWarning(
                    $"[FREEZE] no frame for {stalled:F0} s (frame {frames}); main thread {Where()}");
            }
            catch (Exception e)
            {
                // A watchdog that throws must not take the game down, nor stop watching.
                ProceduralRoadsPlugin.ProceduralRoadsLogger.LogWarning($"[FREEZE] could not read the main thread: {e.Message}");
            }
        }
    }
}
