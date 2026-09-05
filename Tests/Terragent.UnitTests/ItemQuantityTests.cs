namespace Terragent.UnitTests;

public class ItemQuantityTests
{
    [Fact]
    public void FieldsMapToTheDeclaredOrder()
    {
        ItemQuantity quantity = new(ItemID: 42, Count: 7);
        Assert.Equal(42, quantity.ItemID);
        Assert.Equal(7, quantity.Count);
    }

    [Fact]
    public void SameItemAndCountAreEqual() =>
        Assert.Equal(new ItemQuantity(1, 2), new ItemQuantity(1, 2));

    [Fact]
    public void DifferentCountsAreNotEqual() =>
        Assert.NotEqual(new ItemQuantity(1, 2), new ItemQuantity(1, 3));
}
