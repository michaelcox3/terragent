namespace Terragent.Pathfinding;

/// <summary>A route, and which of the destinations asked about it goes to.</summary>
/// <param name="Index">Which of the destinations handed over this answers about.</param>
// A number, not the destination, because two of them can be equal: every craft in an
// objective offers the body's own footing with no arrival rule, so the values match and
// the caller could not tell which job it had chosen.
//
// Which one it names depends on Arrives: the destination reached when it arrived, and the
// one it was heading for when it did not. A partial route has reached none of them.
/// <param name="Route">The way there, as the search returned it.</param>
// The two together because one search decides both. Split apart, the caller has to keep
// an index in step with a route by hand, which is what an out parameter was doing here.
/// <param name="Arrives">Whether it ends at the destination or merely as near as it got.</param>
// Two different answers that look alike. A route that ends at the goal is a plan; a route
// that ends at the nearest footing the search could stand on is progress toward one, and
// the caller walks it and asks again. Saying which is what keeps "there is no way there"
// from being reported as "here is the way there".
internal sealed record RouteMatch(int Index, Route Route, bool Arrives = true);
