using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>
/// The box the body occupies, and what it passes through on the way somewhere.
/// </summary>
internal static class Hitbox
{
    /// <summary>
    /// Two columns, which is the least it can be rather than always what it is.
    /// </summary>
    // Two is the minimum, not always the truth: with the left edge at 16L + f the
    // rightmost pixel reaches column L + 2 once f hits 13.
    public const int Width = 2;

    /// <summary>Three rows, since 42 pixels of body needs 48 of gap and 32 will not do.</summary>
    public const int Height = 3;

    /// <summary>The body in pixels: twenty wide and forty-two tall against sixteen-pixel tiles.</summary>
    public const int PixelWidth = 20;

    public const int PixelHeight = 42;

    /// <summary>
    /// The footing a body of this size at this position is on, decided here and nowhere
    /// else.
    /// </summary>
    // The row is the tile the feet rest on, which on a half block or a slope is not the
    // tile below them; half a pixel of tolerance covers resting exactly on a boundary.
    // The column is the body's left edge, never its centre. A position and a height
    // rather than a Player, so perception has no reason to call it.
    public static Point Footing(Vector2 position, int height) => new(
        (int)(position.X / 16f),
        (int)((position.Y + height + 0.5f) / 16f));

    /// <summary>The cells the body fills standing at <paramref name="footing"/>.</summary>
    public static IEnumerable<Point> Cells(Point footing)
    {
        for (int dx = 0; dx < Width; dx++)
        {
            for (int dy = 1; dy <= Height; dy++)
            {
                yield return new Point(footing.X + dx, footing.Y - dy);
            }
        }
    }

    /// <summary>
    /// Every cell the body passes through moving along a sequence of footings.
    /// </summary>
    public static List<Point> Sweep(IEnumerable<Point> footings)
    {
        List<Point> cells = [];
        HashSet<Point> seen = [];
        foreach (Point footing in footings)
        {
            foreach (Point cell in Cells(footing))
            {
                if (seen.Add(cell))
                {
                    cells.Add(cell);
                }
            }
        }

        return cells;
    }

    /// <summary>Whether the body at this footing covers a given column.</summary>
    public static bool Covers(Point footing, int x) =>
        x >= footing.X && x < footing.X + Width;

    /// <summary>
    /// Where the character's middle belongs when standing at a footing, in pixels.
    /// </summary>
    public static float SeamX(Point footing) => (footing.X + 1) * 16f;

    /// <summary>The pixels a body standing at this footing can occupy.</summary>
    // Both columns across, not one position: within a footing the body slides, and the
    // last stretch of a pickup is walked in pixels toward the item.
    public static Rectangle Frame(Point footing) => new(
        footing.X * 16, (footing.Y * 16) - PixelHeight, Width * 16, PixelHeight);

    /// <summary>Whether a body at this footing overlaps a box, which is how Terraria picks an item up.</summary>
    public static bool Touches(Point footing, Rectangle box) => Frame(footing).Intersects(box);

    /// <summary>Where the body's top-left corner sits when standing at a footing.</summary>
    // What Collision.CanHitLine wants: a box, given by its corner and its size.
    public static Vector2 Stand(Point footing) =>
        new(SeamX(footing) - (PixelWidth / 2f), (footing.Y * 16f) - PixelHeight);

    /// <summary>Where the body's middle sits when standing at a footing.</summary>
    public static Vector2 Middle(Point footing) =>
        new(SeamX(footing), (footing.Y * 16f) - (PixelHeight / 2f));

    /// <summary>Whether a target is within a range, in pixels, of a body at this footing.</summary>
    // Measured from where the body would be, not from the tile, because reach is a
    // distance in pixels and a tile is sixteen of them.
    public static bool InRangeFrom(Point footing, Vector2 target, float range) =>
        Vector2.DistanceSquared(Middle(footing), target) <= range * range;
}
