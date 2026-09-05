namespace Terragent.UnitTests;

// Costs is a plain data holder, no logic of its own. The bug this guards is a
// reordered constructor: Navigator and every scenario price call it positionally.
public class CostsTests
{
    [Fact]
    public void FieldsMapToTheDeclaredOrder()
    {
        Costs costs = new(WalkCost: 4f, MineCost: 45f, PlaceCost: 30f,
            WaterCost: 10f, LavaCost: 1.5f, FogCost: 1f);

        Assert.Equal(4f, costs.WalkCost);
        Assert.Equal(45f, costs.MineCost);
        Assert.Equal(30f, costs.PlaceCost);
        Assert.Equal(10f, costs.WaterCost);
        Assert.Equal(1.5f, costs.LavaCost);
        Assert.Equal(1f, costs.FogCost);
    }
}
