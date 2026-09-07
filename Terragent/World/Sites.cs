using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>Where a kind of tile has been seen.</summary>
// Rings out from the footing and stops at the first distance that holds one, so a tree
// underfoot costs nine cells to find and one across the map costs the whole box. The old
// mod kept an index instead, swept a slice of the world every tick and had to work out
// when an entry had gone stale; this asks the map, which is never stale.
//
// It reads the terrain rather than the world, so what has not been revealed cannot be
// found, and nothing here needs the boundary opened any wider.
internal sealed class Sites(ITerrain terrain) : ISites
{
    private readonly ITerrain _terrain = terrain;

    /// <summary>How far out to look, in tiles.</summary>
    // Wider than a screen, since a site worth walking to is often just off it, and
    // bounded because a search over the whole world would find ore nobody has seen.
    private const int Far = 120;

    public Point? Nearest(Point from, IReadOnlyList<int> tileIDs, int within = int.MaxValue)
    {
        int edge = System.Math.Min(within, Far);
        for (int ring = 0; ring <= edge; ring++)
        {
            if (InRing(from, ring, tileIDs) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The best match exactly this many tiles out, or null when there is none.</summary>
    // A square ring, so the walk is a perimeter rather than an area and the whole scan
    // costs what it finds. Among several at the same ring the straight line decides, since
    // a corner of a square is further away than its middle.
    private Point? InRing(Point from, int ring, IReadOnlyList<int> tileIDs)
    {
        Point? best = null;
        int nearest = int.MaxValue;

        for (int across = -ring; across <= ring; across++)
        {
            for (int down = -ring; down <= ring; down++)
            {
                bool edge = across == -ring || across == ring
                    || down == -ring || down == ring;
                if (!edge)
                {
                    // Skip the inside of the square, which earlier rings already covered.
                    down = ring - 1;
                    continue;
                }

                Point at = new(from.X + across, from.Y + down);
                if (!Names(tileIDs, _terrain.TypeAt(at.X, at.Y)))
                {
                    continue;
                }

                int span = (across * across) + (down * down);
                if (span < nearest)
                {
                    nearest = span;
                    best = at;
                }
            }
        }

        return best;
    }

    private static bool Names(IReadOnlyList<int> tileIDs, int tileID)
    {
        for (int n = 0; n < tileIDs.Count; n++)
        {
            if (tileIDs[n] == tileID)
            {
                return true;
            }
        }

        return false;
    }
}
