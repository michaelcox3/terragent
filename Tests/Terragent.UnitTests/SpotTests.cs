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
    // Its origin is its own top left corner, so the cell aimed at and the corner are one.
    private static Rectangle Bench(int x, int y) => new(x, y, 2, 1);

    /// <summary>Read a bench at a corner, which for a bench is also what it is aimed at.</summary>
    private static Spot? Bench(Grid grid, int x, int y) =>
        Spot.Read(grid, new Point(x, y), Bench(x, y), PickPower);

    /// <summary>The six cells a furnace fills, given the cell the game is aimed at.</summary>
    // Three by two with its origin in the middle of the bottom row, so what is aimed at is
    // one column right and one row down of the corner. Placement.Covers does this
    // subtraction from the game's own table; it is written out here so the shape under test
    // is visible in the test.
    private static Rectangle Furnace(int x, int y) => new(x - 1, y - 1, 3, 2);

    /// <summary>Open floor with something solid under both columns needs no work.</summary>
    [Fact]
    public void OpenFloorIsReady()
    {
        Grid grid = new(true,
            "....",
            "....",
            "####");

        Spot? spot = Bench(grid, 1, 1);

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

        Spot? spot = Bench(grid, 1, 1);

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

        Spot? spot = Bench(grid, 1, 1);

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

        Spot? spot = Bench(grid, 1, 1);

        Assert.NotNull(spot);
        Assert.Equal(new[] { new Point(1, 2) }, spot.Clear);
        Assert.Equal(new[] { new Point(1, 2) }, spot.Fill);
    }

    /// <summary>Nothing the pickaxe will open means nowhere, which is a real answer.</summary>
    [Fact]
    public void UnbreakableGroundIsNowhere() =>
        Assert.Null(Bench(
            new Grid(true,
                "....",
                ".X..",
                "####"),
            1, 1));

    /// <summary>A pickaxe too weak for the rock is the same answer as no way in.</summary>
    [Fact]
    public void RockTooHardIsNowhere() =>
        Assert.Null(Bench(
            new Grid(true,
                "....",
                ".H..",
                "####"),
            1, 1));

    /// <summary>What comes back is the cell to aim at, not the corner of what it fills.</summary>
    // Terraria puts a multi cell object down by its origin. A furnace judged to fit and
    // then aimed at its top left corner is asked for one column left and one row up of
    // where it was judged, which is refused without a word: a run stood at an open patch
    // of floor pressing use and never got a furnace. A work bench hid this, since its
    // origin is its corner and the two points are the same cell.
    [Fact]
    public void AimIsTheOriginAndNotTheCorner()
    {
        Grid grid = new(true,
            ".....",
            ".....",
            ".....",
            "#####");

        Point aimed = new(2, 2);
        Spot? spot = Spot.Read(grid, aimed, Furnace(aimed.X, aimed.Y), PickPower);

        Assert.NotNull(spot);
        Assert.True(spot.Ready);
        Assert.Equal(aimed, spot.At);
        Assert.Equal(new Rectangle(1, 1, 3, 2), spot.Covers);
    }

    /// <summary>A furnace is judged over all six of its cells, not just the one aimed at.</summary>
    // The cell aimed at is open here and the corner of the shape is not, which is the whole
    // case a one cell test gets wrong.
    [Fact]
    public void EveryCellOfAFurnaceIsJudged()
    {
        Grid grid = new(true,
            ".....",
            ".#...",
            ".....",
            "#####");

        Point aimed = new(2, 2);
        Spot? spot = Spot.Read(grid, aimed, Furnace(aimed.X, aimed.Y), PickPower);

        Assert.NotNull(spot);
        Assert.False(spot.Ready);
        Assert.Equal(new[] { new Point(1, 1) }, spot.Clear);
    }
}
