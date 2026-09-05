using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Search;

/// <summary>
/// The steps a search returned.
/// </summary>
// What a search says and nothing more; following the plan belongs to Progress. The
// list is never mutated: the overlay draws the untouched route beside what the
// follower has done with it.
/// <param name="Settled">
/// Which of several goals the search actually reached, or null when it found none.
/// </param>
/// <param name="Examined">
/// How many footings the search looked at: milliseconds say a search was slow, this
/// says whether the ground was hard or the estimate stopped pointing anywhere.
/// </param>
internal sealed class Route(List<Step> steps, Point? settled = null, int examined = 0)
{
    /// <summary>Every step as planned.</summary>
    public IReadOnlyList<Step> Steps { get; } = steps;

    /// <summary>Which goal this route settled on.</summary>
    public Point? Settled { get; } = settled;

    /// <summary>What it cost to find, in footings looked at.</summary>
    public int Examined { get; } = examined;

    /// <summary>Where the route ends, or null when there is none.</summary>
    public Point? Destination => Steps.Count > 0 ? Steps[^1].To : null;
}
