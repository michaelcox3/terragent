using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>
/// How the character gets from one footing to the next.
/// </summary>
internal enum StepKind
{
    Walk,
    Jump,
    Fall,

    /// <summary>
    /// Jump and put a block under your own feet, to stand one row higher.
    /// </summary>
    Place,
}

/// <summary>One move in a route, and the tiles it removes to make the move.</summary>
/// <param name="To">
/// The footing the step lands on: the left column of the body's pair, and the row of
/// floor under it. See <see cref="Body"/> for why both columns are named.
/// </param>
/// <param name="Kind">How the character moves.</param>
/// <param name="Breaks">
/// The tiles this step destroys, empty for a step that destroys nothing. Recorded
/// rather than re-derived: the search already decides which cells it pays to break,
/// and a second derivation in the follower is how places came to disagree about the
/// body's height. It also lets the route be drawn before a swing.
/// </param>
/// <param name="Puts">The tile this step places, for a pillar step.</param>
internal readonly record struct Step(Point To, StepKind Kind, Point[]? Breaks = null,
    Point? Puts = null)
{
    public IReadOnlyList<Point> Removes => Breaks ?? [];
}
