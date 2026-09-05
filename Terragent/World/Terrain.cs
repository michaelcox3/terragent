using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>What a tile is, as far as the agent knows.</summary>
internal enum TileKind
{
    /// <summary>Not on the player's map. Never confuse this with <see cref="Empty"/>.</summary>
    Unknown,
    Empty,
    Solid,

    /// <summary>Blocks from above only: jumped through from below, dropped through with Down.</summary>
    Platform,

    /// <summary>
    /// A half block or a floor slope: rock that fills the bottom of its cell.
    /// </summary>
    // Not Platform, though both hold the character up: a platform is a floor at the top
    // of its cell, a slab is rock at the bottom that pushes the body up onto it. Sharing
    // one kind let a sideways move sweep through a slab with nothing booked to break.
    Slab,
}

/// <summary>
/// Where the character's body is, and what it passes through on the way somewhere.
/// </summary>
internal static class Body
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

/// <summary>
/// The terrain the search plans over.
/// </summary>
internal interface ITerrain
{
    TileKind KindAt(int x, int y);

    int TypeAt(int x, int y);

    bool IsKnown(int x, int y);

    /// <summary>
    /// Whether this tile can be mined with this pickaxe: strong enough, and permitted.
    /// </summary>
    bool Diggable(int x, int y, int pickPower);

    /// <summary>Whether this cell holds liquid.</summary>
    bool Wet(int x, int y);

    bool Buildable(int x, int y);

    bool HasWater(int x, int y);

    bool HasLava(int x, int y);
}

/// <summary>
/// What the body can be in, and what will hold it up.
/// </summary>
internal static class Ground
{
    /// <summary>The first floor at or under a tile, which is where a hopping thing lands.</summary>
    public static Point Under(this ITerrain belief, Point tile)
    {
        for (int y = tile.Y; y < tile.Y + 24; y++)
        {
            if (belief.Standable(new Point(tile.X, y), trustFog: false))
            {
                return new Point(tile.X, y);
            }
        }

        return tile;
    }

    /// <summary>Where a falling box comes to rest: down until ground holds under any column it spans.</summary>
    // Terraria's rule for a dropped item: it stops on the first solid tile under any
    // part of its hitbox, not only under its centre column.
    public static Rectangle Landing(this ITerrain belief, Rectangle box)
    {
        int left = box.Left / 16;
        int right = (box.Right - 1) / 16;
        int first = box.Bottom / 16;
        for (int y = first; y < first + 24; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (belief.Holds(x, y, trustFog: false))
                {
                    return new Rectangle(box.X, (y * 16) - box.Height, box.Width, box.Height);
                }
            }
        }

        return box;
    }

    /// <summary>
    /// Whether the body can be inside this cell.
    /// </summary>
    public static bool Passable(this ITerrain terrain, int x, int y) =>
        terrain.KindAt(x, y) is TileKind.Empty or TileKind.Platform;

    /// <summary>
    /// Whether this cell would hold the character up.
    /// </summary>
    /// <param name="trustFog">
    /// Whether unseen ground counts. It does for a descent and not for a walk: falling
    /// onto rock that is not there costs another row of falling, walking onto it costs
    /// the plan.
    /// </param>
    public static bool Holds(this ITerrain terrain, int x, int y, bool trustFog) =>
        terrain.KindAt(x, y) is TileKind.Solid or TileKind.Platform or TileKind.Slab
        || (trustFog && terrain.KindAt(x, y) is TileKind.Unknown);

    /// <summary>
    /// Whether a footing has ground under either of its columns.
    /// </summary>
    public static bool Standable(this ITerrain terrain, Point footing,
        bool trustFog = false) =>
        terrain.Holds(footing.X, footing.Y, trustFog)
        || terrain.Holds(footing.X + Body.Width - 1, footing.Y, trustFog);

    /// <summary>
    /// Whether a footing is where the body would actually come to rest.
    /// </summary>
    // Standing on something is not standing on the topmost something: a half block
    // over rock means both rows hold, and only the upper one is where the body can be.
    public static bool Settles(this ITerrain terrain, Point footing) =>
        terrain.Standable(footing)
        && !terrain.Holds(footing.X, footing.Y - 1, false)
        && !terrain.Holds(footing.X + Body.Width - 1, footing.Y - 1, false);
}
