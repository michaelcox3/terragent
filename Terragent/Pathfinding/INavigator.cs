using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Pathfinding;
/// <summary>Somewhere to go, and what it takes to get there.</summary>
// The whole seam between wanting to be somewhere and knowing how: three arguments, one
// answer, no state.
internal interface INavigator
{
    /// <summary>The route to a destination, or null when the search got nowhere.</summary>
    // The same answer as the list form, because it is the same search over a list of one.
    // Both say whether they arrived rather than withholding the route that did not: a
    // search is not the place to decide that progress toward a goal is worth less than a
    // plan to reach it, since that depends entirely on who asked.
    RouteMatch? FindRoute(Point from, Destination to, Ability ability);

    /// <summary>The cheapest route to whichever of several places turns out cheapest.</summary>
    // One search over all of them, not one per place. Straight line distance picks the ore
    // behind a wall over the ore down an open shaft, and overruling that is what a search
    // is for. The match says which one it settled on.
    //
    // Each destination is asked for itself, its own arrival rule included, so whoever reads
    // the match back has no second test to apply to the one that won.
    RouteMatch? FindRoute(Point from, IReadOnlyList<Destination> destinations,
        Ability ability);
}
