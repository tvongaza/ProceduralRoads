using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>An immutable observation of the one owned generation. Counts are completed islands, never started islands.</summary>
public sealed class RoadGenerationProgress
{
    public long RunId { get; }
    public string Phase { get; }
    public int Completed { get; }
    public int Total { get; }
    public double ElapsedSeconds { get; }
    public string? Failure { get; }
    public bool Active => Phase == "Preparing" || Phase == "Generating" || Phase == "Finalizing";
    internal RoadGenerationProgress(long runId, string phase, int completed, int total, double elapsedSeconds, string? failure = null)
    { RunId = runId; Phase = phase; Completed = completed; Total = total; ElapsedSeconds = elapsedSeconds; Failure = failure; }
    public string Text => Phase == "Preparing" ? "Preparing roads…" : Phase == "Finalizing" ? "Finishing roads…" :
        Phase == "Failed" ? "Road generation failed (see the log)" : $"Generating roads: {Completed} of {Total} islands complete";
}

public static partial class RoadLifecycleManager
{
    private sealed class Generation
    {
        internal readonly WorldGenerator World;
        internal readonly ZoneSystem Zones;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal RoadNetworkGenerator.GenerationWork? Work;
        internal Task? Worker;
        internal double PreparationMs;
        internal double ComputeStartedMs;
        internal Generation(WorldGenerator world, ZoneSystem zones) { World = world; Zones = zones; }
    }
    private static Generation? m_generation;
    private static bool m_synchronous;
    private static bool m_initialAttempted;
    internal static void AllowInitialRetry() => m_initialAttempted = false;
#if ROADS_TESTS
    internal static Action? BeforeWorkerCompletionCheck;
#endif
    private static long m_nextRun;
    private static RoadGenerationProgress m_progress = new(0, "Idle", 0, 0, 0);
    public static RoadGenerationProgress Progress => Volatile.Read(ref m_progress);
    public static bool IsGenerating => m_generation != null || m_synchronous;
    public static bool InitialGenerationActive => m_generation != null;
    internal static bool WorkersOwnWorld => InitialGenerationActive && Progress.Phase != "Finalizing";
    internal static void ThrowIfCancellationRequested() => m_generation?.Cancellation.Token.ThrowIfCancellationRequested();
    private static void SetProgress(string phase, int completed, int total, string? failure = null) =>
        Volatile.Write(ref m_progress, new RoadGenerationProgress(m_nextRun, phase, completed, total,
            m_generation?.Clock.Elapsed.TotalSeconds ?? 0, failure));

    private static void BeginInitialGeneration()
    {
        if (IsGenerating || m_initialAttempted || !RoadNetworkGenerator.GenerateOnLoad || ZNet.instance == null || !ZNet.instance.IsServer()) return;
        m_initialAttempted = true;
        m_generation = new Generation(WorldGenerator.instance!, ZoneSystem.instance!);
        m_nextRun++;
        SetProgress("Preparing", 0, 0);
        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo("Preparing road network generation…");
    }

    /// <summary>Called each Unity frame, independently of the terrain sweep cadence.</summary>
    public static void Tick()
    {
        var run = m_generation;
        if (run == null) return;
        if (!ReferenceEquals(run.World, WorldGenerator.instance) || !ReferenceEquals(run.Zones, ZoneSystem.instance))
        { CancelAndDrain(); return; }
        try
        {
            if (run.Worker == null)
            {
                var prepareClock = Stopwatch.StartNew();
                run.Work = RoadNetworkGenerator.PrepareGeneration(false);
                run.PreparationMs = prepareClock.Elapsed.TotalMilliseconds;
                if (run.Work == null) { End(run, "Complete"); return; }
                run.Work.Cancellation = run.Cancellation.Token;
                ReportPrepared(run.Work);
                // TaskScheduler.Default: workers never need a Unity continuation in order to terminate.
                run.ComputeStartedMs = run.Clock.Elapsed.TotalMilliseconds;
                run.Worker = Task.Factory.StartNew(run.Work.Compute, run.Cancellation.Token,
                    TaskCreationOptions.None, TaskScheduler.Default);
                return;
            }
            ReportFinished(run.Work!);
#if ROADS_TESTS
            BeforeWorkerCompletionCheck?.Invoke();
#endif
            if (!run.Worker.IsCompleted)
            { SetProgress("Generating", Progress.Completed, Progress.Total); return; }
            run.Worker.GetAwaiter().GetResult();
            // Joining closes publication: the last island may have finished after the earlier drain.
            ReportFinished(run.Work!);
            double computeMs = run.Clock.Elapsed.TotalMilliseconds - run.ComputeStartedMs;
            var finalizeClock = Stopwatch.StartNew();
            SetProgress("Finalizing", Progress.Completed, Progress.Total);
            RoadNetworkGenerator.CompleteGeneration(run.Work!);
            RoadNetworkGenerator.ApplyGeneratedNetworkToLoadedZones();
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"Road generation phases: prepare={run.PreparationMs:F0}ms compute={computeMs:F0}ms finalize/apply={finalizeClock.Elapsed.TotalMilliseconds:F0}ms");
            End(run, "Complete");
        }
        catch (Exception error)
        {
            // A faulted Parallel loop has joined every island before its Task completes.
            run.Cancellation.Cancel();
            Drain(run);
            RoadNetworkGenerator.ReleaseGenerationResources();
            if (RoadNetworkGenerator.RoadsGenerated)
            {
                // Loaded-zone application can already have written terrain or spawned bridge ZDOs.
                // Keep its finalized network so those effects retain their identity and can be saved/reapplied.
                ProceduralRoadsPlugin.ProceduralRoadsLogger.LogError($"Road network completed, but application to loaded zones failed; network retained: {error}");
                End(run, "ApplicationFailed", error.GetBaseException().Message);
            }
            else
            {
                RoadNetworkGenerator.ResetData();
                ProceduralRoadsPlugin.ProceduralRoadsLogger.LogError($"Road generation failed; no network was published: {error}");
                End(run, "Failed", error.GetBaseException().Message);
            }
        }
    }

    internal static void ReportPrepared(RoadNetworkGenerator.GenerationWork work)
    {
        SetProgress("Generating", 0, work.Islands.Count);
        ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"Generating roads on {work.Islands.Count} islands ({work.Workers} thread(s)). This takes a few minutes.");
    }
    internal static void ReportFinished(RoadNetworkGenerator.GenerationWork work)
    {
        int completed = Progress.Completed;
        while (work.Finished.TryDequeue(out string? detail))
        {
            completed++;
            SetProgress("Generating", completed, work.Islands.Count);
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"  {completed} of {work.Islands.Count} done - {detail}");
        }
    }
    private static void End(Generation run, string phase, string? failure = null)
    {
        RoadNetworkGenerator.ReleaseGenerationResources();
        SetProgress(phase, Progress.Completed, Progress.Total, failure);
        m_generation = null;
        run.Cancellation.Dispose();
    }
    private static void Drain(Generation run)
    {
        if (run.Worker == null) return;
        try { run.Worker.GetAwaiter().GetResult(); }
        catch (Exception) { /* observed; cancellation/failure is reported by the owner */ }
    }
    /// <summary>Drain before destroying the world, unsealing its caches, or resetting any shared network state.</summary>
    public static void CancelAndDrain()
    {
        var run = m_generation;
        if (run == null) return;
        run.Cancellation.Cancel();
        Drain(run);
        End(run, "Cancelled");
        RoadNetworkGenerator.ResetData();
    }
    internal static void RunSynchronous(Action action)
    {
        if (IsGenerating || ManualRoads.Busy) throw new InvalidOperationException("Road generation is busy.");
        m_synchronous = true;
        m_nextRun++;
        SetProgress("Preparing", 0, 0);
        try { action(); SetProgress("Complete", Progress.Completed, Progress.Total); }
        catch { RoadNetworkGenerator.ReleaseGenerationResources(); if (!RoadNetworkGenerator.RoadsGenerated) RoadNetworkGenerator.ResetData(); SetProgress("Failed", Progress.Completed, Progress.Total); throw; }
        finally { m_synchronous = false; }
    }
}
