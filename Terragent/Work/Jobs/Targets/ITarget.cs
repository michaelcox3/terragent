using Microsoft.Xna.Framework;

namespace Terragent.Work.Jobs.Targets;

/// <summary>The thing a job chose to work on.</summary>
// Not the site, which is where the body stands to do it. A creature, a dropped item, or a
// patch of ground: the thing the work is about.
//
// It answers where it is now rather than being told, because half of them move. That is
// what a route has to be pointed at, and asking the thing itself is the only way to get an
// answer that does not go stale.
//
// Identity is the other half of what it is for: the same target twice is the same attempt,
// however far it has walked in between. So implementations compare by what they hold and
// never by where they are.
internal interface ITarget
{
    /// <summary>Where it is now, or null when it is nowhere.</summary>
    // Nowhere is a real answer twice over: a recipe that needs no station is worked where
    // the body stands, and a creature that has died is not anywhere at all. Answering with
    // a point in either case is a lie, and the drift rule believed it: a target at the top
    // left corner of the world reads as having moved two thousand tiles.
    Point? Tile { get; }
}
