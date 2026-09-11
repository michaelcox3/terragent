using Microsoft.Xna.Framework;
using Terragent.World;
using Xunit;

namespace Terragent.UnitTests;

/// <summary>The space the body takes up, which is what furniture has to be put outside of.</summary>
public class HitboxTests
{
    /// <summary>Somewhere to stand, with the feet on row ten.</summary>
    private static readonly Point Standing = new(4, 10);

    /// <summary>A work bench, which is two columns wide and one row tall.</summary>
    private static Rectangle Bench(int x, int y) => new(x, y, 2, 1);

    /// <summary>The column beside the body is still the body, since a bench is two wide.</summary>
    // The whole of the bug. A bench put one column over covers that column and the next,
    // and the first of those is the body's own right hand column: Terraria refuses the
    // placement, the job jumped to get clear, and in a tunnel three rows tall it hit the
    // ceiling and jumped again until the run was stopped.
    [Fact]
    public void ABenchOneColumnOverIsStillInsideTheBody() =>
        Assert.True(Bench(Standing.X + 1, Standing.Y - 1)
            .Intersects(Hitbox.Fills(Standing)));

    /// <summary>Two columns over clears it, which is the nearest spot that does.</summary>
    [Fact]
    public void ABenchTwoColumnsOverIsClear() =>
        Assert.False(Bench(Standing.X + 2, Standing.Y - 1)
            .Intersects(Hitbox.Fills(Standing)));

    /// <summary>To the left one column is clear, since a bench is drawn rightward.</summary>
    [Fact]
    public void ABenchOneColumnLeftIsClear() =>
        Assert.False(Bench(Standing.X - 2, Standing.Y - 1)
            .Intersects(Hitbox.Fills(Standing)));

    /// <summary>The body is three rows of it, so head height counts as much as the feet.</summary>
    [Fact]
    public void TheBodyReachesThreeRowsAboveItsFeet()
    {
        Assert.True(Bench(Standing.X, Standing.Y - 3).Intersects(Hitbox.Fills(Standing)));
        Assert.False(Bench(Standing.X, Standing.Y - 4).Intersects(Hitbox.Fills(Standing)));
    }

    /// <summary>The row the feet rest on is floor, not body: a bench may share it.</summary>
    [Fact]
    public void TheRowUnderfootIsNotTheBody() =>
        Assert.False(Bench(Standing.X, Standing.Y).Intersects(Hitbox.Fills(Standing)));
}
