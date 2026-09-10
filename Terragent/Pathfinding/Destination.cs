using System;
using Microsoft.Xna.Framework;

namespace Terragent.Pathfinding;

/// <summary>Somewhere to get to, and what counts as being there.</summary>
/// <param name="Site">The tile the follower is making for.</param>
/// <param name="Within">How close counts, in tiles.</param>
/// <param name="Budget">Footings the search may expand before giving up.</param>
// Carries its own arrival and its own budget because they differ per caller and the
// follower cannot know which it is serving: a tile is reached at tool range, a drop only
// by being touched, a creature at weapon range, and a fight searches on a fraction of a
// walk's budget.
internal readonly record struct Destination(Point Site, int Within = 1, int Budget = 20000)
{
    /// <summary>Replaces <see cref="Within"/> when arrival is not a radius.</summary>
    public Func<Point, bool>? Arrived { get; init; }

    /// <summary>Whether a footing counts as being here.</summary>
    // Decided once, so what stops the search and what tells the follower it has arrived
    // cannot differ by a tile. A body covers two columns, which is why this is not a
    // distance between points.
    public bool Reached(Point at) =>
        Arrived is { } arrived ? arrived(at) : Navigator.Reached(at, Site, Within);

    /// <summary>A point no further than a leg away, on the way to somewhere further off.</summary>
    // A search's cost is the ground it sweeps, not the distance it covers, so one route
    // eighty rows down through solid rock is the shape least likely to come back with
    // anything: it runs out of budget having dug nowhere. Walked in legs, each search is
    // short, each one succeeds, and arriving asks for the next.
    //
    // Not settled onto standable ground. The agent mines and bridges, so a leg ending
    // inside rock is a leg it will cut its way to, and the arrival radius leaves it room
    // to stop on whatever footing it opens up.
    public static Point Toward(Point from, Point to, int leg)
    {
        int across = to.X - from.X;
        int down = to.Y - from.Y;
        float away = MathF.Sqrt((across * across) + (down * down));
        if (away <= leg)
        {
            return to;
        }

        return new Point(
            from.X + (int)(across / away * leg),
            from.Y + (int)(down / away * leg));
    }

    /// <summary>Whether a site has drifted further than a radius allows, in tiles.</summary>
    // A circle, not a box: a site that slid three across and three down moved four and a
    // quarter, and calling that three keeps a route pointed where the work is not.
    //
    // The one place this is decided, so that what redraws a route and what changes a
    // target cannot disagree about how far is far.
    public static bool Beyond(Point was, Point now, int radius)
    {
        int across = now.X - was.X;
        int down = now.Y - was.Y;
        return (across * across) + (down * down) > radius * radius;
    }
}
