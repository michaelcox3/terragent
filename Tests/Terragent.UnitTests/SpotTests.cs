using Microsoft.Xna.Framework;
using Terragent.World;
using Xunit;

namespace Terragent.UnitTests;

/// <summary>What it takes to stand a work bench on a given patch of ground.</summary>
// A bench is two columns wide and one tall, so every case here is a two by one rectangle
// with the floor row underneath it. The pictures are read top down, and the characters are
// documented on Case.
public class SpotTests
{
    /// <summary>A pickaxe that breaks ordinary rock and not the hard kind.</summary>
    private const int PickPower = 35;

    /// <summary>The two cells a bench fills, with its left column at x.</summary>
    private static Rectangle Bench(int x, int y) => new(x, y, 2, 1);

    /// <summary>Open floor with something solid under both columns needs no work.</summary>
    [Fact]
    public void OpenFloorIsReady()
    {
        Grid grid = new(true,
            "....",
            "....",
            "####");

        Spot? spot = Spot.Read(grid, Bench(1, 1), PickPower);

        Assert.NotNull(spot);
        Assert.True(spot.Ready);
        Assert.Equal(0, spot.Work);
    }

    /// <summary>Rock in the way is one swing, not a refusal.</summary>
    // The old answer was yes or no, so a bench could not be stood anywhere along a shaft
    // the run had just dug itself down, and the job stopped offering anything at all.
    [Fact]
    public void RockInTheWayIsCleared()
    {
        Grid grid = new(true,
            "....",
            ".#..",
            "####");

        Spot? spot = Spot.Read(grid, Bench(1, 1), PickPower);

        Assert.NotNull(spot);
        Assert.False(spot.Ready);
        Assert.Equal(new[] { new Point(1, 1) }, spot.Clear);
        Assert.Empty(spot.Fill);
    }

    /// <summary>A column with nothing under it is floored rather than given up on.</summary>
    [Fact]
    public void MissingFloorIsFilled()
    {
        Grid grid = new(true,
            "....",
            "....",
            "##.#");

        Spot? spot = Spot.Read(grid, Bench(1, 1), PickPower);

        Assert.NotNull(spot);
        Assert.Empty(spot.Clear);
        Assert.Equal(new[] { new Point(2, 2) }, spot.Fill);
    }

    /// <summary>A shaped floor is broken first and floored after, so it is both.</summary>
    // Terraria holds the character up on a half block and a floor slope and anchors no
    // furniture to either. Reading it as floor is what left a bench being aimed at a
    // smoothed floor and refused every tick without a word.
    [Fact]
    public void AShapedFloorIsBrokenThenFilled()
    {
        Grid grid = new(true,
            "....",
            "....",
            "#_##");

        Spot? spot = Spot.Read(grid, Bench(1, 1), PickPower);

        Assert.NotNull(spot);
        Assert.Equal(new[] { new Point(1, 2) }, spot.Clear);
        Assert.Equal(new[] { new Point(1, 2) }, spot.Fill);
    }

    /// <summary>Nothing the pickaxe will open means nowhere, which is a real answer.</summary>
    [Fact]
    public void UnbreakableGroundIsNowhere() =>
        Assert.Null(Spot.Read(
            new Grid(true,
                "....",
                ".X..",
                "####"),
            Bench(1, 1),
            PickPower));

    /// <summary>A pickaxe too weak for the rock is the same answer as no way in.</summary>
    [Fact]
    public void RockTooHardIsNowhere() =>
        Assert.Null(Spot.Read(
            new Grid(true,
                "....",
                ".H..",
                "####"),
            Bench(1, 1),
            PickPower));
}
