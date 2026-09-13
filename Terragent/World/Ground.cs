using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>
/// What the body can be in, and what will hold it up.
/// </summary>
internal static class Ground
{
    /// <summary>The first floor at or under a tile, which is where a hopping thing lands.</summary>
    public static Point Under(this ITerrain terrain, Point tile)
    {
        for (int y = tile.Y; y < tile.Y + 24; y++)
        {
            if (terrain.Standable(new Point(tile.X, y), trustFog: false))
            {
                return new Point(tile.X, y);
            }
        }

        return tile;
    }

    /// <summary>Where a falling box comes to rest: down until ground holds under any column it spans.</summary>
    // Terraria's rule for a dropped item: it stops on the first solid tile under any
    // part of its hitbox, not only under its centre column.
    public static Rectangle Landing(this ITerrain terrain, Rectangle box)
    {
        int left = box.Left / 16;
        int right = (box.Right - 1) / 16;
        int first = box.Bottom / 16;
        for (int y = first; y < first + 24; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (terrain.Holds(x, y, trustFog: false))
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
        || terrain.Holds(footing.X + Hitbox.Width - 1, footing.Y, trustFog);

    /// <summary>Whether any cell the body fills at this footing holds water.</summary>
    // Every cell, because Terraria counts a body as wet on any overlap at all, and that is
    // what puts a torch out and stops the map filling in. Asked of the head alone, a landing
    // with its chest and legs under counted as dry: a run jumped into a pool it could see,
    // priced as walking, and stood there blind. Written once here, since three places had
    // their own answer and only one of them matched the game.
    //
    // Indexed rather than over Hitbox.Cells, which yields and would allocate once an edge.
    public static bool Wet(this ITerrain terrain, Point footing)
    {
        for (int across = 0; across < Hitbox.Width; across++)
        {
            for (int down = 1; down <= Hitbox.Height; down++)
            {
                if (terrain.HasWater(footing.X + across, footing.Y - down))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether any cell the body fills at this footing holds lava.</summary>
    public static bool Scalding(this ITerrain terrain, Point footing)
    {
        for (int across = 0; across < Hitbox.Width; across++)
        {
            for (int down = 1; down <= Hitbox.Height; down++)
            {
                if (terrain.HasLava(footing.X + across, footing.Y - down))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a footing is where the body would actually come to rest.
    /// </summary>
    // Standing on something is not standing on the topmost something: a half block
    // over rock means both rows hold, and only the upper one is where the body can be.
    public static bool Settles(this ITerrain terrain, Point footing) =>
        terrain.Standable(footing)
        && !terrain.Holds(footing.X, footing.Y - 1, false)
        && !terrain.Holds(footing.X + Hitbox.Width - 1, footing.Y - 1, false);
}
