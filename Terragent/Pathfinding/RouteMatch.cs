namespace Terragent.Pathfinding;

/// <summary>A route, and which of the destinations asked about it goes to.</summary>
/// <param name="Index">Where the one it reached sits in the list handed over.</param>
// A number, not the destination, because two of them can be equal: every craft in an
// objective offers the body's own footing with no arrival rule, so the values match and
// the caller could not tell which job it had chosen.
/// <param name="Route">The way there, as the search returned it.</param>
// The two together because one search decides both. Split apart, the caller has to keep
// an index in step with a route by hand, which is what an out parameter was doing here.
internal sealed record RouteMatch(int Index, Route Route);
