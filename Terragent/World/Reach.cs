using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.World;

/// <summary>
/// How far the player can reach with the item it is holding.
/// </summary>
internal static class Reach
{
    /// <summary>Whether a tile can be mined, chopped or hammered with this item.</summary>
    public static bool CanUse(Player player, Item item, int tileX, int tileY) =>
        Within(player, TileBoost(item), 0, tileX, tileY);

    /// <summary>
    /// Whether the target would be in reach if the player stood at
    /// <paramref name="footing"/>.
    /// </summary>
    public static bool CanUseFrom(Player player, Item item, Point footing, int tileX,
        int tileY)
    {
        // Both extremes of where the body can sit in its column pair, not the middle:
        // reach is a float comparison, so a tile can be in range from one and not the
        // other.
        float top = (footing.Y * 16f) - player.height;
        foreach (float left in new[]
                 {
                     footing.X * 16f,
                     (footing.X * 16f) + (Body.Width * 16f) - player.width,
                 })
        {
            // A tile of margin: the predicate answers for a character standing squarely
            // and still, and the real one is a pixel or two off or sliding.
            if (!Within(new Vector2(left, top), player.width, player.height,
                    TileBoost(item), -1f, tileX, tileY))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a tile can be built on with this item, which uses a wider box.</summary>
    public static bool CanPlace(Player player, Item item, int tileX, int tileY) =>
        Within(player, TileBoost(item), player.blockRange, tileX, tileY);

    /// <summary>Whether any of these can be swung at from this footing.</summary>
    public static bool Usable(Player player, Item held, Point node,
        IReadOnlyList<Point> targets)
    {
        foreach (Point target in targets)
        {
            if (CanUseFrom(player, held, node, target.X, target.Y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The mining box in tile coordinates, inclusive, for drawing.</summary>
    public static Rectangle Box(Player player, Item item)
    {
        float boost = TileBoost(item);
        // Ceil the lower bounds and floor the upper ones, because the predicate is a
        // float comparison and the box is a set of whole tiles.
        int left = (int)Math.Ceiling(player.position.X / 16f - Player.tileRangeX - boost);
        int right = (int)Math.Floor(
            (player.position.X + player.width) / 16f + Player.tileRangeX + boost - 1f);
        int top = (int)Math.Ceiling(player.position.Y / 16f - Player.tileRangeY - boost);
        int bottom = (int)Math.Floor(
            (player.position.Y + player.height) / 16f + Player.tileRangeY + boost - 2f);
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>How far outside the box a tile is, in tiles, zero when inside.</summary>
    public static int Overshoot(Player player, Item item, int tileX, int tileY)
    {
        Rectangle box = Box(player, item);
        int overX = Math.Max(0, Math.Max(box.Left - tileX, tileX - (box.Right - 1)));
        int overY = Math.Max(0, Math.Max(box.Top - tileY, tileY - (box.Bottom - 1)));
        return Math.Max(overX, overY);
    }

    private static float TileBoost(Item item) => item.IsAir ? 0f : item.tileBoost;

    /// <summary>
    /// How many tiles away a route may stop and still have the target in reach.
    /// </summary>
    // The vertical half of the reach box, the smaller of the two and so safe whichever
    // way the target lies. Minus one because the box is measured from the body's edges
    // and the goal from its footing, so its last row is not one to stand at. Read off
    // the held item because every pickaxe past copper carries tileBoost.
    public static int Radius(Player player, Item item) =>
        Math.Max(1, (int)(Player.tileRangeY + TileBoost(item)) - 1);

    private static bool Within(Player player, float boost, float extra, int x, int y) =>
        Within(player.position, player.width, player.height, boost, extra, x, y);

    private static bool Within(Vector2 position, int width, int height, float boost,
        float extra, int x, int y)
    {
        float reachX = Player.tileRangeX + boost + extra;
        float reachY = Player.tileRangeY + boost + extra;
        return (position.X / 16f) - reachX <= x
            && ((position.X + width) / 16f) + reachX - 1f >= x
            && (position.Y / 16f) - reachY <= y
            && ((position.Y + height) / 16f) + reachY - 2f >= y;
    }
}
