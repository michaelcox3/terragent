using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Sense;

/// <summary>
/// Where the agent has seen each kind of tile it cares about.
/// </summary>
// Plain data on purpose: points in, points out, nothing about Terraria. Belief is the
// only thing allowed to read tiles, so Belief fills this in and this is testable with
// no game. Only watched kinds are kept, and there is one index per belief, because
// where a thing was seen is memory and memory belongs to whoever did the seeing.
internal sealed class TileIndex
{
    private readonly Dictionary<int, HashSet<Point>> _seen = [];

    /// <summary>What each remembered cell holds, so a change is one lookup.</summary>
    // Without the reverse map, forgetting a cell means asking every kind whether it held
    // it, for two thousand cells a tick.
    private readonly Dictionary<Point, int> _holding = [];

    private static readonly HashSet<int> Watched = [];

    /// <summary>Kinds worth remembering the whereabouts of.</summary>
    // Static, and the one thing here that is: registered once at load, before any player
    // exists to own an index.
    public static void Watch(int tileID)
    {
        if (tileID > 0)
        {
            Watched.Add(tileID);
        }
    }

    /// <summary>Whether this kind is one the index is keeping.</summary>
    public static bool Watching(int tileID) => Watched.Contains(tileID);

    /// <summary>Everything of a kind that has been seen, or nothing.</summary>
    public IReadOnlyCollection<Point> Where(int tileID) =>
        _seen.TryGetValue(tileID, out HashSet<Point>? found) ? found : [];

    /// <summary>Note that this cell holds this kind.</summary>
    public void Saw(int tileID, int x, int y)
    {
        if (!Watched.Contains(tileID))
        {
            return;
        }

        if (!_seen.TryGetValue(tileID, out HashSet<Point>? found))
        {
            found = [];
            _seen[tileID] = found;
        }

        Point cell = new(x, y);
        found.Add(cell);
        _holding[cell] = tileID;
    }

    /// <summary>
    /// Note that this cell no longer holds a kind it was remembered for.
    /// </summary>
    // Told the cell rather than the kind: the caller has just watched a tile stop being
    // what it was and may not know what it had been.
    public void Gone(int x, int y)
    {
        Point cell = new(x, y);
        if (_holding.Remove(cell, out int was) && _seen.TryGetValue(was, out HashSet<Point>? found))
        {
            found.Remove(cell);
        }
    }

    /// <summary>Note that this cell holds this kind and nothing else.</summary>
    public void Holds(int tileID, int x, int y)
    {
        Point cell = new(x, y);
        if (_holding.TryGetValue(cell, out int was) && was == tileID)
        {
            return;
        }

        Gone(x, y);
        Saw(tileID, x, y);
    }

    /// <summary>Nearest tile of a type the player has actually seen.</summary>
    // Nearest by straight line over the whole index, not by rings: a cell at a ring's
    // corner is r times root two away while one in the middle of the next ring is only
    // r plus one. The skip is handed in so this file names no perception at all.
    public bool Nearest(Point from, int tileID, System.Func<Point, bool> skip,
        out int foundX, out int foundY)
    {
        foundX = foundY = 0;
        long best = long.MaxValue;
        bool found = false;

        foreach (Point site in Where(tileID))
        {
            long away = ((long)(site.X - from.X) * (site.X - from.X))
                        + ((long)(site.Y - from.Y) * (site.Y - from.Y));
            if (away >= best)
            {
                continue;
            }

            // Choosing a cell the search would refuse is how the agent walks to a block
            // it may not touch and stands there.
            if (skip(site))
            {
                continue;
            }

            best = away;
            foundX = site.X;
            foundY = site.Y;
            found = true;
        }

        return found;
    }
}
