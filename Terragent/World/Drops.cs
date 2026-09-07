using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.World;

/// <summary>What is lying about, as far as the player can see.</summary>
// The second of the three files allowed to read the game's world state, and the only one
// that names Main.item. Everything above it gets a Drop, which is an item, a count and a
// box, and cannot go asking the game anything else.
//
// Gated on the map like the tiles are. Main.item holds every item in the world, and an
// agent that read it whole would walk to ore it has never seen.
internal sealed class Drops(ITerrain terrain) : IDrops
{
    private readonly ITerrain _terrain = terrain;

    /// <summary>How far out to look, in tiles.</summary>
    private const int Far = 120;

    public Drop? Nearest(Point from, IReadOnlyList<int> itemIDs)
    {
        Vector2 me = Hitbox.Middle(from);
        Drop? best = null;
        float nearest = float.MaxValue;

        for (int n = 0; n < Main.maxItems; n++)
        {
            Item item = Main.item[n];
            if (!item.active || item.IsAir || !Names(itemIDs, item.type))
            {
                continue;
            }

            Point at = new((int)(item.Center.X / 16f), (int)(item.Center.Y / 16f));
            if (Beyond(from, at) || !_terrain.IsKnown(at.X, at.Y))
            {
                continue;
            }

            float span = Vector2.DistanceSquared(me, item.Center);
            if (span < nearest)
            {
                nearest = span;
                best = new Drop(n, item.type, item.stack, item.getRect());
            }
        }

        return best;
    }

    // Null the moment it is taken. The slot is reused, so the kind is checked too: a
    // stack of stone landing in the slot the wood left is not the wood.
    public Drop? At(int index)
    {
        if (index < 0 || index >= Main.maxItems)
        {
            return null;
        }

        Item item = Main.item[index];
        return item.active && !item.IsAir
            ? new Drop(index, item.type, item.stack, item.getRect())
            : null;
    }

    private static bool Beyond(Point from, Point at) =>
        System.Math.Abs(at.X - from.X) > Far || System.Math.Abs(at.Y - from.Y) > Far;

    /// <summary>Whether this is one of the kinds asked for. An empty list takes any.</summary>
    // Empty means anything, because the commonest use is walking back over what the agent
    // has just broken and it wants all of it.
    private static bool Names(IReadOnlyList<int> itemIDs, int itemID)
    {
        if (itemIDs.Count == 0)
        {
            return true;
        }

        for (int n = 0; n < itemIDs.Count; n++)
        {
            if (itemIDs[n] == itemID)
            {
                return true;
            }
        }

        return false;
    }
}
