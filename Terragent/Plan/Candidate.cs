using Microsoft.Xna.Framework;
using Terragent.World;

namespace Terragent.Plan;

/// <summary>One place that would satisfy one job.</summary>
/// <param name="Site">Where the work is, as belief has it this tick.</param>
/// <param name="Within">How close counts: a tile at tool reach, a drop only touched.</param>
// Named by the item and how it is come by, never by a Job. The board is rebuilt every
// tick, so a job held across ticks is a snapshot of an object that no longer exists.
internal readonly record struct Candidate(int ItemID, Origin From, Point Site, int Within)
{
    /// <summary>Whether this and another name the same work, wherever either sits.</summary>
    public bool Same(Candidate other) => ItemID == other.ItemID && From == other.From;

    /// <summary>Whether the site has moved far enough to be worth drawing a fresh route.</summary>
    public bool Moved(Candidate other) => Beyond(Site, other.Site, Within);

    /// <summary>Whether a site has drifted further than a radius allows, in tiles.</summary>
    // A circle, not a box: a site that slid three across and three down moved four and a
    // quarter, and calling that three keeps a route pointed where the work is not.
    //
    // The one place this is decided. The board asks it of the selection and the follower
    // asks it of the goal its route was drawn to, and two answers to that one question
    // is what walked the agent back and forth between two deposits.
    public static bool Beyond(Point was, Point now, int radius)
    {
        int across = now.X - was.X;
        int down = now.Y - was.Y;
        return (across * across) + (down * down) > radius * radius;
    }
}
