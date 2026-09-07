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
    /// <summary>The route to a destination, or null when there is none.</summary>
    /// <param name="refused">
    /// Moves the body has tried and failed to make, which the search plans around.
    /// </param>
    // The follower is the only thing that finds out a move does not work: the search
    // prices what the body should be able to do, and a ledge it cannot actually climb
    // looks the same as one it can until it stands there failing.
    Route? FindRoute(Point from, Destination to, Ability ability,
        ISet<(Point From, Point To)> refused);

    /// <summary>The cheapest route to whichever of several places turns out cheapest.</summary>
    // One search over all of them, not one per place. Straight line distance picks the ore
    // behind a wall over the ore down an open shaft, and overruling that is what a search
    // is for. The route says which one it settled on.
    //
    // Arrival is a radius here, whatever the destinations carry. A predicate belongs to one
    // place and this is asking about several; the real test is applied by whoever routes to
    // the one that wins.
    Route? FindRoute(Point from, IReadOnlyList<Destination> destinations, Ability ability,
        ISet<(Point From, Point To)> refused, out int which);
}
