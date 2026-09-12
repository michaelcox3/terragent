using System;
using Microsoft.Xna.Framework;

namespace Terragent.Pathfinding;

/// <summary>Somewhere to get to, and what counts as being there.</summary>
/// <param name="Site">The tile the follower is making for.</param>
/// <param name="Within">How close counts, in tiles. Zero means standing on it.</param>
// Zero by default, so slack is something a caller asks for rather than something it is
// given. One tile of it looks harmless and is not: a body straddles two columns, so the
// box already forgives a column before this widens it at all, and the two together put
// arrival further out than a caller measuring in a straight line expects. Where a job
// wants slack it says how much, and the four that want a shape instead say that.
/// <param name="Budget">Footings the search may expand before giving up.</param>
// Carries its own arrival and its own budget because they differ per caller and the
// follower cannot know which it is serving: a tile is reached at tool range, a drop only
// by being touched, a creature at weapon range, and a fight searches on a fraction of a
// walk's budget.
internal readonly record struct Destination(Point Site, int Within = 0, int Budget = 20000)
{
    /// <summary>How near counts when the target is a direction rather than a place.</summary>
    // Walking toward a frontier or down to a layer, where the point is to be over there and
    // the last few tiles reveal as much as the exact one does.
    public const int Slack = 3;

    /// <summary>Replaces <see cref="Within"/> when arrival is not a radius.</summary>
    public Func<Point, bool>? Arrived { get; init; }

    /// <summary>Whether a footing counts as being here.</summary>
    // Decided once, so what stops the search and what tells the follower it has arrived
    // cannot differ by a tile. A body covers two columns, which is why this is not a
    // distance between points.
    public bool Reached(Point at) =>
        Arrived is { } arrived ? arrived(at) : Navigator.Reached(at, Site, Within);

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
