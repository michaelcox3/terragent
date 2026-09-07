using Microsoft.Xna.Framework;

namespace Terragent.Pathfinding;

/// <summary>One edge of the search graph: the step it makes, and what it costs.</summary>
// Carries the Step whole, so the frontier, the trail back and the rebuild share one
// shape.
internal readonly record struct Edge(Step Step, float Cost)
{
    /// <summary>The footing this move lands on.</summary>
    public Point Next => Step.To;
}
