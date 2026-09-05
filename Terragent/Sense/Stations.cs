using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.ID;

namespace Terragent.Sense;

/// <summary>
/// Crafting stations that exist in the world, and where.
/// </summary>
internal sealed class Stations
{
    private readonly List<(int TileID, Point At)> _attempts = [];



    /// <summary>Record that we tried to put one here. Confirmation comes later.</summary>
    public void Attempted(int tileID, int x, int y)
    {
        Point at = new(x, y);
        foreach ((int existing, Point where) in _attempts)
        {
            if (existing == tileID && where == at)
            {
                return;
            }
        }

        _attempts.Add((tileID, at));
    }

    /// <summary>
    /// Where the nearest surviving station of this kind is, if any.
    /// </summary>
    public Point? Nearest(int tileID, Belief belief, Point from)
    {
        Point? best = null;
        int bestDistance = int.MaxValue;

        for (int i = _attempts.Count - 1; i >= 0; i--)
        {
            (int attemptedTile, Point at) = _attempts[i];
            if (attemptedTile != tileID)
            {
                continue;
            }

            if (belief.IsKnown(at.X, at.Y) && belief.TypeAt(at.X, at.Y) != tileID)
            {
                _attempts.RemoveAt(i);
                continue;
            }

            int distance = (at.X - from.X) * (at.X - from.X) + (at.Y - from.Y) * (at.Y - from.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = at;
            }
        }

        return best ?? Scan(tileID, belief, from);
    }


    /// <summary>The nearest one the index has seen, straight line.</summary>
    // The skip is not the digging guard, which refuses any tile a station stands on: for
    // a work bench that is the bench itself. A seen cell holding something else is
    // skipped because the sweep may not have passed since the station was broken.
    private static Point? Scan(int tileID, Belief belief, Point from) =>
        belief.NearestSite(from, tileID,
            cell => belief.IsKnown(cell.X, cell.Y) && belief.TypeAt(cell.X, cell.Y) != tileID,
            out int x, out int y)
            ? new Point(x, y)
            : null;

    public bool Exists(int tileID, Belief belief, Point from) =>
        Nearest(tileID, belief, from) is not null;

    public void Clear() => _attempts.Clear();
}
