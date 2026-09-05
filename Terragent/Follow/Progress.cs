using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Follow;

/// <summary>
/// Which step of a route the body is actually on.
/// </summary>
// The follower's cursor over a search result. Nothing here touches the game: given the
// steps and a footing it answers which step is current, so every tolerance below can
// be tested without a world. They exist because a body with momentum does not land
// where a grid plan said it would.
internal sealed class Progress(Route route)
{
    private int _index;

    /// <summary>The plan being followed.</summary>
    public Route Route { get; } = route;

    /// <summary>The step being worked, or null when the route is spent.</summary>
    public Step? Current => _index < Route.Steps.Count ? Route.Steps[_index] : null;

    /// <summary>How many steps are still to be walked.</summary>
    public int Left => System.Math.Max(0, Route.Steps.Count - _index);

    /// <summary>How many have been walked, for the journal.</summary>
    public int Walked => _index;

    /// <summary>
    /// The steps still ahead, for drawing what is left rather than all of it.
    /// </summary>
    // The follower breaks a route's cells one at a time, so drawing the whole list keeps
    // showing blocks that came out seconds ago.
    public IEnumerable<Step> Remaining
    {
        get
        {
            for (int n = _index; n < Route.Steps.Count; n++)
            {
                yield return Route.Steps[n];
            }
        }
    }

    /// <summary>
    /// Move past every step the body has already got to.
    /// </summary>
    /// <param name="standable">
    /// Whether anything is holding the body up. Nothing advances in mid-air: the step
    /// being flown towards stays current for the whole jump.
    /// </param>
    // A loop, because one frame can consume several steps after a long fall.
    public void Reached(Point at, bool standable)
    {
        while (_index < Route.Steps.Count && standable
               && (Near(at, Route.Steps[_index].To, 0) || Landed(Route.Steps[_index], at)))
        {
            _index++;
        }
    }

    /// <summary>
    /// Take the current step as done, without asking where the body is.
    /// </summary>
    // For the one move that finishes by changing the world rather than by arriving: a
    // pillar step is complete when its block is solid, with the body where it started.
    public void Complete() => _index = System.Math.Min(_index + 1, Route.Steps.Count);

    /// <summary>Whether a jump has finished, even if it came down a column short.</summary>
    // A jump is flown, not stepped: the body comes down where momentum leaves it, within
    // a column rather than on the tile. Asking for the exact tile left the step neither
    // done nor doable, and the route was redrawn from the landing spot with the same
    // jump. At or above the row, because a jump that carried higher than asked still
    // arrived; short of it is a failed jump, and that one should replan.
    public static bool Landed(Step step, Point at) =>
        step.Kind is StepKind.Jump
        && at.Y <= step.To.Y
        && System.Math.Abs(at.X - step.To.X) <= 1;

    /// <summary>How far off the aimed column a landing may be and still be walked in.</summary>
    // A leap of five can miss by a couple; more than that and something else happened.
    private const int Overshoot = 3;

    /// <summary>Whether a jump has landed high enough but has yet to reach its column.</summary>
    // The walk off the lip. Told apart from a jump that has not started by column, not
    // by row: for a level jump the rows are identical.
    public static bool Finishing(Step step, Point at) =>
        step.Kind is StepKind.Jump
        && at.Y <= step.To.Y
        && System.Math.Abs(at.X - step.To.X) <= Overshoot;

    /// <summary>Whether a footing is within <paramref name="slack"/> tiles of a tile.</summary>
    public static bool Near(Point at, Point tile, int slack) =>
        System.Math.Abs(at.X - tile.X) <= slack && System.Math.Abs(at.Y - tile.Y) <= slack;
}
