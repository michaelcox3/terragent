using System.Collections.Generic;
using Terragent.Pathfinding;

namespace Terragent.Controls;
/// <summary>Moves the body toward one destination until it gets there or cannot.</summary>
// The whole of it. A pilot is told where to go and pressed once per tick, and it answers
// with one of four words; choosing where to go, and what to do on arrival, belong to
// whatever set the destination.
internal interface IPilot
{
    /// <summary>Where it is going, or null when it is standing still.</summary>
    Destination? Destination { get; }

    /// <summary>What the last tick made of it.</summary>
    Progress Progress { get; }

    /// <summary>How many of the route's steps are behind it.</summary>
    // So a drawing shows what is left rather than what was planned. The blocks of a step
    // already walked have been broken, and drawing them makes the overlay disagree with
    // the world.
    int Walked { get; }

    /// <summary>The route being followed, or null when there is none.</summary>
    // For drawing. The plan as the search returned it, so what is on screen is what the
    // body is actually working from rather than a second guess at it.
    Route? Route { get; }

    /// <summary>The way to whichever of these it reaches soonest, or null when none.</summary>
    // One search over all of them. Asked before choosing, so that a job is picked by what
    // the ground allows rather than by what looks close: the ore behind a wall is nearer
    // than the ore down the shaft and further away in every sense that matters.
    RouteMatch? FindRoute(IReadOnlyList<Destination> destinations);

    /// <summary>Go to this destination along this route, dropping whatever it was doing.</summary>
    // Two arguments and not a settable destination, because there is no such thing as a
    // new destination with the old plan still running. Handing both over at once is what
    // stops the pilot searching a second time for a way it has just been given.
    void Follow(Destination site, Route route);

    /// <summary>Let go of wherever it was going and stand still.</summary>
    void Halt();

    /// <summary>One tick of getting there, which is the only thing that presses a key.</summary>
    void Tick();
}
