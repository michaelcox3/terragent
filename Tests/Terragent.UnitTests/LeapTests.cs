namespace Terragent.UnitTests;

public class LeapTests
{
    /// <summary>
    /// Fifteen powered frames at 5.01, gravity 0.4, run acceleration 0.08 up to 3: the
    /// base character's jump, from the game's own constants.
    /// </summary>
    private static readonly Leap Jump = Leap.Of(15, 5.01f, 0.4f, 0.08f, 3f);

    [Fact]
    public void BaseJumpClearsSixRows() => Assert.Equal(6, Jump.Height);

    [Fact]
    public void LevelJumpReachesFiveColumns() => Assert.True(Jump.Reach(0) >= 5);

    [Fact]
    public void SixRowRiseReachesFewerThanFiveColumns() => Assert.True(Jump.Reach(6) < 5);

    [Fact]
    public void ReachNeverGrowsWithHeight()
    {
        for (int rows = 1; rows <= Jump.Height; rows++)
        {
            Assert.True(Jump.Reach(rows) <= Jump.Reach(rows - 1),
                $"reach grew from row {rows - 1} to row {rows}");
        }
    }
}
