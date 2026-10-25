using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace ProceduralRoads;

/// <summary>A detached observation. Slot is logical, never an OS thread identity.</summary>
public sealed class RoadIslandProgress
{
    public int Slot { get; }
    public int Island { get; }
    public long Assignment { get; }
    public bool Active { get; }
    public string Stage { get; }
    public int Processed { get; }
    public int Destinations { get; }
    public int Reached { get; }
    public double Kilometres { get; }
    public int PricesCompleted { get; }
    public int PricesTotal { get; }
    public long SearchSteps { get; }
    public int ActiveSearches { get; }
    public double StageSeconds { get; }
    internal RoadIslandProgress(int slot, int island, long assignment, bool active, string stage,
        int processed, int destinations, int reached, double metres, int pricesCompleted, int pricesTotal,
        long searchSteps, int activeSearches, double seconds)
    { Slot=slot; Island=island; Assignment=assignment; Active=active; Stage=stage; Processed=processed;
      Destinations=destinations; Reached=reached; Kilometres=metres/1000; PricesCompleted=pricesCompleted;
      PricesTotal=pricesTotal; SearchSteps=searchSteps; ActiveSearches=activeSearches; StageSeconds=seconds; }
}

/// <summary>Fixed storage belonging to one generation. No event queue or per-node synchronization.</summary>
internal sealed class RoadIslandActivity
{
    internal sealed class Token
    {
        private readonly RoadIslandActivity owner;
        internal readonly int Slot, Island;
        internal readonly long Epoch;
        private long nextReachSample;
        internal Token(RoadIslandActivity owner, int slot, int island, long epoch)
        { this.owner=owner; Slot=slot; Island=island; Epoch=epoch; }
        internal void Stage(string stage) => owner.Update(this, s => { if (s.Stage != stage) { s.Stage=stage; s.StageStart=Stopwatch.GetTimestamp(); } });
        internal void Pricing(int total) => owner.Update(this, s => { s.Stage="Pricing"; s.StageStart=Stopwatch.GetTimestamp(); s.PricesTotal=total; s.PricesCompleted=0; });
        internal void Priced() => owner.Update(this, s => s.PricesCompleted++);
        internal void Settled(int index) => owner.Update(this, s => { if (index>=0 && index<s.Settled.Length) s.Settled[index]=true; });
        // Called only by this island's serial planner, outside the snapshot lock.
        internal bool ReachSampleDue()
        {
            long now=Stopwatch.GetTimestamp();
            if(now<nextReachSample) return false;
            nextReachSample=now+Stopwatch.Frequency/2;
            return true;
        }
        internal void Reached(int count) => owner.Update(this, s => s.Reached=count);
        internal void Committed(float metres) => owner.Update(this, s => s.Metres+=metres);
        internal void Search(int steps, int activeDelta) => owner.Search(this,steps,activeDelta);
        internal void Finish() => owner.Release(this);
    }
    private sealed class Slot
    {
        internal long Epoch;
        internal int Island, Reached, PricesCompleted, PricesTotal, ActiveSearches;
        internal bool Active;
        internal bool[] Settled=Array.Empty<bool>();
        internal string Stage="Waiting";
        internal long StageStart, SearchSteps;
        internal double Metres;
    }
    private readonly object gate = new();
    private readonly Slot[] slots;
    private bool closed;
    internal RoadIslandActivity(int workers, int islands)
    { slots=new Slot[Math.Min(Math.Max(0,workers),Math.Max(0,islands))]; for(int i=0;i<slots.Length;i++) slots[i]=new Slot(); }
    internal Token Claim(int island, int destinations)
    {
        lock(gate)
        {
            if(closed) throw new InvalidOperationException("Generation activity is closed.");
            for(int i=0;i<slots.Length;i++) if(!slots[i].Active)
            {
                long epoch=slots[i].Epoch+1;
                slots[i]=new Slot { Active=true, Island=island, Epoch=epoch, Settled=new bool[destinations], Stage="Preparing", StageStart=Stopwatch.GetTimestamp() };
                return new Token(this,i,island,epoch);
            }
            throw new InvalidOperationException("No generation slot available.");
        }
    }
    private void Update(Token token, Action<Slot> update)
    { lock(gate) { var s=slots[token.Slot]; if(!closed && s.Active && s.Epoch==token.Epoch && s.Island==token.Island) update(s); } }
    private void Search(Token token,int steps,int activeDelta)
    {
        lock(gate)
        {
            var s=slots[token.Slot];
            if(!closed && s.Active && s.Epoch==token.Epoch && s.Island==token.Island)
            { s.SearchSteps+=steps; s.ActiveSearches+=activeDelta; }
        }
    }
    private void Release(Token token)
    { Update(token,s=> { s.Stage="Finished"; s.StageStart=Stopwatch.GetTimestamp(); s.Active=false; }); }
    internal void Close() { lock(gate) closed=true; }
    internal ReadOnlyCollection<RoadIslandProgress> Snapshot()
    {
        lock(gate)
        {
            var result=new RoadIslandProgress[slots.Length];
            for(int i=0;i<slots.Length;i++)
            {
                var s=slots[i]; int processed=0; foreach(bool done in s.Settled) if(done) processed++;
                result[i]=new RoadIslandProgress(i,s.Island,s.Epoch,s.Active,s.Stage,processed,s.Settled.Length,s.Reached,
                    s.Metres,s.PricesCompleted,s.PricesTotal,s.SearchSteps,s.ActiveSearches,
                    s.StageStart==0?0:(Stopwatch.GetTimestamp()-s.StageStart)/(double)Stopwatch.Frequency);
            }
            return Array.AsReadOnly(result);
        }
    }
}

/// <summary>One search's local batch; only every 256 iterations touches shared counters.</summary>
internal sealed class RoadSearchActivity : IDisposable
{
    private readonly RoadIslandActivity.Token? token;
    private int pending;
    internal RoadSearchActivity(RoadIslandActivity.Token? token) { this.token=token; token?.Search(0,1); }
    internal void Step() { if(token!=null && ++pending==256) { token.Search(pending,0); pending=0; } }
    public void Dispose() { token?.Search(pending,-1); }
}

internal sealed class RoadFittingActivity : IDisposable
{
    private readonly RoadIslandActivity.Token? token;
    internal RoadFittingActivity(RoadIslandActivity.Token? token) { this.token=token; token?.Stage("Fitting"); }
    public void Dispose() => token?.Stage("Connecting");
}
