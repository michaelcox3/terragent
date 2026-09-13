using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Pathfinding;

/// <summary>
/// The steps a search returned.
/// </summary>
// What a search says and nothing more; following the plan belongs to Progress. The
// list is never mutated: the overlay draws the untouched route beside what the
// follower has done with it.
/// <param name="from">The footing it was drawn from, which no step records.</param>
// A step names where it lands and nothing else, so without this a route cannot say where
// it begins: the first step is already one move along. Copying a case out of a world needs
// the place the search was standing when it drew this, not the place the body had walked
// to by the time anybody looked.
/// <param name="effort">What the search cost to find it, counted rather than timed.</param>
internal sealed class Route(List<Step> steps, Point from, Effort? effort = null)
{
    /// <summary>The footing this was drawn from.</summary>
    public Point From { get; } = from;

    /// <summary>Every step as planned.</summary>
    public IReadOnlyList<Step> Steps { get; } = steps;

    /// <summary>What it cost to find.</summary>
    // Never null, so a reader never has to ask twice. A route built by hand, which the
    // tests do, cost nothing and says so.
    public Effort Effort { get; } = effort ?? new Effort();
}
