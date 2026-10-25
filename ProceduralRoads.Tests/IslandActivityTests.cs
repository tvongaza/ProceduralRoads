using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Xunit;

namespace ProceduralRoads.Tests;

public sealed class IslandActivityTests
{
    [Theory]
    [InlineData(8,0,0)]
    [InlineData(8,2,2)]
    [InlineData(2,8,2)]
    [InlineData(12,20,12)]
    public void SlotsAreBoundedByWorkersAndIslands(int workers,int islands,int expected)
    {
        Assert.Equal(expected,new RoadIslandActivity(workers,islands).Snapshot().Count);
    }
    [Fact]
    public void ReuseRejectsOldAssignmentAndClosedRunCannotPublish()
    {
        var owner=new RoadIslandActivity(1,3);
        var first=owner.Claim(1,2);
        first.Settled(0); first.Settled(0); first.Committed(120);
        var before=owner.Snapshot();
        first.Finish();
        var second=owner.Claim(3,1);
        first.Settled(1); first.Committed(999); first.Finish(); first.Search(256,1);
        second.Settled(0); second.Reached(1); second.Committed(80);
        var after=owner.Snapshot().Single();
        Assert.True(after.Active); Assert.Equal(3,after.Island); Assert.Equal(2,after.Assignment);
        Assert.Equal(1,after.Processed); Assert.Equal(.08,after.Kilometres,6); Assert.Equal(0,after.SearchSteps);
        Assert.Equal(1,before[0].Processed); Assert.Equal(.12,before[0].Kilometres,6);
        owner.Close(); second.Search(256,1); second.Reached(99);
        Assert.Equal(1,owner.Snapshot()[0].Reached);
        var next=new RoadIslandActivity(1,1); next.Claim(1,1);
        second.Committed(999); Assert.Equal(0,next.Snapshot()[0].Kilometres);
    }
    [Fact]
    public void NestedPricingAggregatesBatchesAndFlushesRemainder()
    {
        var owner=new RoadIslandActivity(1,1); var token=owner.Claim(1,3);
        token.Pricing(4);
        Parallel.For(0,4,i => { using(var search=new RoadSearchActivity(token)) for(int n=0;n<300;n++) search.Step(); token.Priced(); });
        var row=owner.Snapshot().Single();
        Assert.Equal(1200,row.SearchSteps); Assert.Equal(0,row.ActiveSearches);
        Assert.Equal(4,row.PricesCompleted); Assert.Equal(4,row.PricesTotal); Assert.Equal(0,row.Processed);
    }
    [Fact]
    public void SearchPublishesNonterminalActivityWithoutSettlingDestinations()
    {
        var owner=new RoadIslandActivity(1,1); var token=owner.Claim(2,4);
        using(var search=new RoadSearchActivity(token))
        {
            for(int n=0;n<256;n++) search.Step();
            var first=owner.Snapshot()[0];
            for(int n=0;n<257;n++) search.Step();
            var second=owner.Snapshot()[0];
            Assert.Equal(256,first.SearchSteps); Assert.Equal(512,second.SearchSteps);
            Assert.Equal(1,second.ActiveSearches); Assert.Equal(0,second.Processed); Assert.Equal(0,second.Reached);
        }
        Assert.Equal(513,owner.Snapshot()[0].SearchSteps); Assert.Equal(0,owner.Snapshot()[0].ActiveSearches);
    }
    [Fact]
    public void PlannerRetriesDoNotSettleUntilDestinationIsExhausted()
    {
        var places=new[] { new RoadConnectionPlanner.Place("Start",Vector3.zero,0),
            new RoadConnectionPlanner.Place("Same",new Vector3(100,0,0),10),
            new RoadConnectionPlanner.Place("Same",new Vector3(200,0,0),10) };
        var owner=new RoadIslandActivity(1,1); var token=owner.Claim(1,2);
        int retries=0,settles=0;
        RoadConnectionPlanner.Connect(places,(_,__)=>1,(_,__)=>false,_=>false,_=>1,
            (_,text)=> { if(text.Contains("trying")) { retries++; Assert.True(settles<2); } },
            index=> { token.Settled(index-1); settles++; });
        Assert.True(retries>1); Assert.Equal(2,settles); Assert.Equal(2,owner.Snapshot()[0].Processed);
        Assert.Equal(0,owner.Snapshot()[0].Reached);
    }
    [Fact]
    public void UnpricedPartnerSettlesEachIndexOnce()
    {
        var places=new[] { new RoadConnectionPlanner.Place("Start",Vector3.zero,0),
            new RoadConnectionPlanner.Place("Same",new Vector3(100,0,0),10),
            new RoadConnectionPlanner.Place("Same",new Vector3(200,0,0),10) };
        var settled=new int[3];
        RoadConnectionPlanner.Connect(places,(_,__)=>null,(a,b)=>a.Name!="Start",_=>false,_=>1,(_,__)=>{},i=>settled[i]++);
        Assert.Equal(new[]{0,1,1},settled);
    }
}
