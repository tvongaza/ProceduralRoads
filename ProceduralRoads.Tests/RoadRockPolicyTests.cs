using Xunit;
namespace ProceduralRoads.Tests;
public class RoadRockPolicyTests
{
    [Theory]
    [InlineData("rock1_mountain")]
    [InlineData("rock2_mountain")]
    [InlineData("rock3_mountain")]
    [InlineData("rock3_mountain_1")]
    [InlineData("rock2_heath")]
    [InlineData("rock4_heath")]
    [InlineData("rock4_forest")]
    [InlineData("Rock_3")]
    [InlineData("Rock_4")]
    [InlineData("Rock_4_plains")]
    [InlineData("rock4_coast")]
    [InlineData("HeathRockPillar")]
    public void NaturalBouldersClearOnlyInRequestedBiomes(string name)
    {
        foreach (var biome in new[] { Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.BlackForest })
        {
            Assert.True(RoadRockPolicy.CanClear(name,true,biome,false,false,true,true,true));
            Assert.False(RoadRockPolicy.CanClear(name,false,biome,false,false,true,true,true));
            Assert.False(RoadRockPolicy.CanClear(name,true,biome,true,false,true,true,true));
            Assert.False(RoadRockPolicy.CanClear(name,true,biome,false,true,true,true,true));
        }
        foreach (var biome in new[] { Heightmap.Biome.None, Heightmap.Biome.Meadows,
            Heightmap.Biome.Swamp, Heightmap.Biome.Ocean, Heightmap.Biome.DeepNorth,
            Heightmap.Biome.AshLands, Heightmap.Biome.Mistlands })
            Assert.False(RoadRockPolicy.CanClear(name,true,biome,false,false,true,true,true));
    }

    [Theory]
    [InlineData("silvervein")]
    [InlineData("rock3_silver")]
    [InlineData("rock4_copper")]
    [InlineData("MineRock_Obsidian")]
    [InlineData("MineRock_Tin")]
    [InlineData("rock1_mountain_frac")]
    [InlineData("rock2_heath_frac")]
    [InlineData("rock4_forest_frac")]
    [InlineData("Rock_3_frac")]
    [InlineData("Rock_3_deepnorth")]
    [InlineData("RockDolmen_1")]
    [InlineData("rock_mistlands1")]
    [InlineData("rock_3")]
    [InlineData("wood_floor")]
    public void OreFragmentsAndUnrecognisedSceneryStay(string name) =>
        Assert.False(RoadRockPolicy.CanClear(name,true,Heightmap.Biome.BlackForest,false,false,true,true,true));

    [Theory]
    [InlineData("rock1_mountain")]
    [InlineData("rock2_heath")]
    [InlineData("rock4_forest")]
    [InlineData("Rock_3")]
    public void ARemoteOrNotYetValidRockIsLeftForItsOwner(string name)
    {
        Assert.False(RoadRockPolicy.CanClear(name,true,Heightmap.Biome.BlackForest,false,false,true,true,false));
        Assert.False(RoadRockPolicy.CanClear(name,true,Heightmap.Biome.BlackForest,false,false,true,false,true));
        Assert.True(RoadRockPolicy.CanClear(name,true,Heightmap.Biome.BlackForest,false,false,false,false,false));
    }
}
