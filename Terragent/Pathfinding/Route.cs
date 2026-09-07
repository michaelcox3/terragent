using System.Collections.Generic;

namespace Terragent.Pathfinding;

/// <summary>
/// The steps a search returned.
/// </summary>
// What a search says and nothing more; following the plan belongs to Progress. The
// list is never mutated: the overlay draws the untouched route beside what the
// follower has done with it.
/// <param name="Examined">
/// How many footings the search looked at: milliseconds say a search was slow, this
/// says whether the ground was hard or the estimate stopped pointing anywhere.
/// </param>
internal sealed class Route(List<Step> steps, int examined = 0)
{
    /// <summary>Every step as planned.</summary>
    public IReadOnlyList<Step> Steps { get; } = steps;

    /// <summary>What it cost to find, in footings looked at.</summary>
    public int Examined { get; } = examined;
}
