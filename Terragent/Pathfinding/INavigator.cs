using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Pathfinding;
/// <summary>Somewhere to go, and what it takes to get there.</summary>
// The whole seam between wanting to be somewhere and knowing how: three arguments, one
// answer, no state. Deliberately narrower than the class behind it, which also searches
// several destinations at once and takes the edges a follower has proved it cannot make;
// those are for the callers that have learned something, and nothing has yet.
internal interface INavigator
{
    /// <summary>The route to a destination, or null when the search got nowhere.</summary>
    // The same answer as the list form, because it is the same search over a list of one.
    // Both say whether they arrived rather than withholding the route that did not: a
    // search is not the place to decide that progress toward a goal is worth less than a
    // plan to reach it, since that depends entirely on who asked.
    /// <param name="refused">
    /// Moves the body has tried and failed to make, which the search plans around.
    /// </param>
    // The follower is the only thing that finds out a move does not work: the search
    // prices what the body should be able to do, and a ledge it cannot actually climb
    // looks the same as one it can until it stands there failing.
    RouteMatch? FindRoute(Point from, Destination to, Ability ability,
        ISet<(Point From, Point To)> refused);

    /// <summary>The cheapest route to whichever of several places turns out cheapest.</summary>
    // One search over all of them, not one per place. Straight line distance picks the ore
    // behind a wall over the ore down an open shaft, and overruling that is what a search
    // is for. The match says which one it settled on.
    //
    // Each destination is asked for itself, its own arrival rule included, so whoever reads
    // the match back has no second test to apply to the one that won.
    RouteMatch? FindRoute(Point from, IReadOnlyList<Destination> destinations,
        Ability ability, ISet<(Point From, Point To)> refused);
}
