using Xunit;
namespace ProceduralRoads.Tests;
public class RoadRockPolicyTests
{
    [Theory]
    [InlineData("rock1_mountain",true)]
    [InlineData("rock2_mountain",true)]
    [InlineData("rock3_mountain",true)]
    [InlineData("rock3_mountain_1",true)]
    [InlineData("silvervein",false)]
    [InlineData("rock1_mountain_frac",false)]
    [InlineData("rock2_heath",false)]
    public void OnlyKnownMountainBoulders(string name,bool expected) =>
        Assert.Equal(expected,RoadRockPolicy.CanClear(name,true,false,false,true,true,true));
    [Fact] public void ProtectedSitePlayerPieceAndUnregisteredPrefabStay()
    {
        Assert.False(RoadRockPolicy.CanClear("rock1_mountain",true,true,false,true,true,true));
        Assert.False(RoadRockPolicy.CanClear("rock1_mountain",true,false,true,true,true,true));
        Assert.False(RoadRockPolicy.CanClear("rock1_mountain",false,false,false,true,true,true));
    }
    [Fact] public void ARemoteOrNotYetValidRockIsLeftForItsOwner()
    {
        Assert.False(RoadRockPolicy.CanClear("rock1_mountain",true,false,false,true,true,false));
        Assert.False(RoadRockPolicy.CanClear("rock1_mountain",true,false,false,true,false,true));
        Assert.True(RoadRockPolicy.CanClear("rock1_mountain",true,false,false,false,false,false));
    }
}
