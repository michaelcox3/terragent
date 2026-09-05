using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Follow;

/// <summary>What executing a step said about the route it belongs to.</summary>
internal enum Outcome
{
    /// <summary>Pressed something; the route stands.</summary>
    Acting,

    /// <summary>The route should be thrown away and drawn again.</summary>
    Replan,

    /// <summary>Nothing can be pressed: no tool, no blocks, or a tile that cannot be seen.</summary>
    Stuck,
}

/// <summary>
/// Executing the step the body is on, and remembering what it proved would not work.
/// </summary>
// Clears what the plan booked, places what it drew, and presses what Steering says. It
// never touches the route: what a step says about the route comes back as an Outcome.
// The two sets it keeps are facts the next search has to be told.
internal sealed class Stepping
{
    /// <summary>What the last tick did, for the pilot to report.</summary>
    public string Behaviour { get; private set; } = "";

    /// <summary>
    /// Tiles that would not break, and are therefore treated as wall.
    /// </summary>
    public HashSet<Point> Immovable { get; } = [];

    private Point _swingingAt;
    private int _swings;


    /// <summary>How near the seam counts as squarely in the column pair, in pixels.</summary>
    // Three, which Align's own deadband of two can actually reach. Any tighter and the
    // agent shuffles for ever trying to satisfy a test its steering cannot meet.
    private const float Square = 3f;

    /// <summary>How long a step may make no ground before it is not going to.</summary>
    private const double StallSeconds = 2.0;

    /// <summary>Edges the character has proved it cannot execute.</summary>
    public HashSet<(Point From, Point To)> Refused { get; } = [];

    private Point _pushingTo;

    /// <summary>How far the step had left when the clock last restarted.</summary>
    private int _pushingRemaining = int.MaxValue;

    private double _pushingSince;

    /// <summary>Strike an edge out, so the next search routes round it.</summary>
    public void Refuse((Point From, Point To) edge) => Refused.Add(edge);

    /// <summary>
    /// Forget what would not break and what could not be done, called when the pickaxe
    /// improves because the wall that stopped the agent is exactly what the new one is
    /// for.
    /// </summary>
    public void ReconsiderWalls()
    {
        Immovable.Clear();
        Refused.Clear();
    }

    /// <summary>
    /// The next block the plan says is in the way, or null when the way is clear.
    /// </summary>
    public static Point? Obstruction(Belief belief, Step step)
    {
        foreach (Point cell in step.Removes)
        {
            // Anything the plan booked and has not yet turned to air. Not Solid or
            // Unknown: half blocks and floor slopes classify as platforms, and a descent
            // through smoothed ground then looked clear while the body stood on the
            // floor it had planned to break.
            if (belief.KindAt(cell.X, cell.Y) is not TileKind.Empty)
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>
    /// Do one tick of this step: clear what is in its way, or make the move.
    /// </summary>
    /// <param name="takeoff">The footing the body last stood on, for a jump in flight.</param>
    public Outcome Execute(Belief belief, Character character, AgentState state,
        Progress progress, Step step, Point at, Point takeoff, double now)
    {
        // Clear the way first, whatever the move is: mining is what any step may have
        // to do before it can be made, not a kind of step.
        if (Obstruction(belief, step) is { } blocking)
        {
            state.Digs.Add(blocking);
            if (!character.Hand.HoldPickaxe())
            {
                Behaviour = "no pickaxe anywhere in the inventory";
                return Outcome.Stuck;
            }

            if (!character.Hand.InReach(blocking.X, blocking.Y))
            {
                Behaviour = "route drifted; replanning";
                return Outcome.Replan;
            }

            if (Immovable.Contains(blocking))
            {
                Behaviour = $"({blocking.X}, {blocking.Y}) will not break; going round";
                return Outcome.Replan;
            }

            // A cell the agent cannot see proves nothing by refusing to break: in the
            // dark every cell reads Unknown, a hole it dug itself included, so the swings
            // go into air and the verdict is about nothing. The plan is kept on purpose;
            // dropping it made this a full search every frame. Lighting runs before this
            // and will raise a torch.
            if (!belief.IsKnown(blocking.X, blocking.Y))
            {
                Behaviour = $"cannot see ({blocking.X}, {blocking.Y})";
                state.Blind = blocking;
                state.Stuck = true;
                return Outcome.Stuck;
            }

            if (blocking != _swingingAt)
            {
                _swingingAt = blocking;
                _swings = 0;
            }

            character.Hand.Aim(blocking.X, blocking.Y);
            character.Hand.Use();

            // Counted per swing, not per tick.
            if (!character.Hand.Blocked
                && ++_swings > Tiles.ExpectedSwings(character.Inventory.PickPower) * 6 + 60)
            {
                Immovable.Add(blocking);
                Behaviour = $"({blocking.X}, {blocking.Y}) is too hard for this "
                            + "pickaxe; going round";
                Log.Sample("immovable", Behaviour,
                    ("swings", _swings),
                    ("kind", belief.KindAt(blocking.X, blocking.Y).ToString()),
                    ("canKill", belief.CanKill(blocking.X, blocking.Y)),
                    ("grid", Diagnose.Draw(belief, at, blocking)));
                return Outcome.Replan;
            }

            // Stay in the column pair the plan is cutting: standing off to one side of a
            // shaft just opened rests on the lip instead of dropping through it.
            character.Movement.Align(at);

            Behaviour = character.Hand.Blocked
                ? "holding the swing until the game aims where we do"
                : $"digging ({blocking.X}, {blocking.Y})";
            return Outcome.Acting;
        }

        // A patience budget for movement, keyed on the destination and progress toward
        // it rather than the exact footing: a body that wobbles a column either side of
        // its takeoff is going nowhere, and keying on the footing restarted the clock at
        // every wobble, so a ledge that would not be climbed was never refused.
        int remaining = System.Math.Abs(step.To.X - at.X) + System.Math.Abs(step.To.Y - at.Y);
        if (step.To != _pushingTo || remaining < _pushingRemaining)
        {
            _pushingTo = step.To;
            _pushingRemaining = remaining;
            _pushingSince = now;
        }
        else if (now - _pushingSince > StallSeconds)
        {
            // Not a retry: the move model said this edge was executable and the character
            // has just shown otherwise, so the edge is refused and the route redrawn.
            Refused.Add((at, step.To));
            Behaviour = $"({at.X}, {at.Y}) to ({step.To.X}, {step.To.Y}) does not "
                        + $"work by {step.Kind.ToString().ToLowerInvariant()}; going round";
            // With the ground drawn, since the only way to fix a move the model got
            // wrong is to see the shape it happened on.
            Log.Sample("refused", Behaviour,
                ("kind", step.Kind.ToString()),
                ("under", belief.KindAt(at.X, at.Y + 1).ToString()),
                ("ahead", belief.KindAt(step.To.X, step.To.Y).ToString()),
                ("aheadHead", belief.KindAt(step.To.X, step.To.Y - 1).ToString()),
                ("grid", Diagnose.Draw(belief, at, step.To)));
            return Outcome.Replan;
        }

        if (step.Kind == StepKind.Place && step.Puts is { } put)
        {
            // Already there: the block is down and the character is standing on it.
            if (belief.KindAt(put.X, put.Y) is TileKind.Solid)
            {
                progress.Complete();
                Behaviour = "pillared up";
                return Outcome.Acting;
            }

            if (!character.Hand.HoldBlock())
            {
                Behaviour = "no blocks to build with";
                return Outcome.Stuck;
            }

            // Beside the feet rather than under them, which is a different move with
            // the same name: no jump, because the block is not going where the legs
            // are. Aim, place, and walk on to it.
            if (put.Y >= at.Y && put.X != at.X)
            {
                if (!character.Hand.InPlaceReach(put.X, put.Y))
                {
                    character.Movement.Align(at);
                    Behaviour = $"closing on ({put.X}, {put.Y}) to bridge";
                    return Outcome.Acting;
                }

                // Not while the body is in the cell. Sunk into a slope or a half block,
                // the body's box reaches into the row beside its feet, and the game
                // silently refuses a block that overlaps a player: in range, cursor on
                // the tile, swing after swing, nothing. Rise first, as a pillar does.
                Rectangle cell = new(put.X * 16, put.Y * 16, 16, 16);
                if (character.Movement.Frame.Intersects(cell))
                {
                    character.Movement.Leap(put.Y * 16f);
                    Behaviour = $"rising clear of ({put.X}, {put.Y}) to bridge";
                    return Outcome.Acting;
                }

                state.Digs.Add(put);
                character.Hand.Aim(put.X, put.Y);
                character.Hand.Use();
                Behaviour = character.Hand.Blocked
                    ? "holding the swing until the game aims where we do"
                    : $"bridging to ({put.X}, {put.Y})";
                return Outcome.Acting;
            }

            // Square in the column pair before leaving the ground. The body is 20 pixels
            // against 16 pixel tiles, so at some alignments it reaches into a third
            // column, and a pillar goes straight up into whatever that column holds.
            if (character.Movement.Velocity.Y == 0f
                && System.Math.Abs(character.Movement.Center.X - Body.SeamX(at)) > Square)
            {
                character.Movement.Align(at);
                Behaviour = $"squaring up to pillar at ({put.X}, {put.Y})";
                return Outcome.Acting;
            }

            // Jump first: the tile being placed is where the legs are, so rising out of
            // the way is the mechanism, not a flourish.
            character.Movement.Leap(put.Y * 16f);
            if (character.Movement.Frame.Bottom > put.Y * 16f)
            {
                Behaviour = $"jumping to place at ({put.X}, {put.Y})";
                return Outcome.Acting;
            }

            state.Digs.Add(put);
            character.Hand.Aim(put.X, put.Y);
            character.Hand.Use();
            Behaviour = character.Hand.Blocked
                ? "holding the swing until the game aims where we do"
                : $"placing a block at ({put.X}, {put.Y}) to stand on";
            return Outcome.Acting;
        }

        bool airborne = !character.Movement.Grounded;
        if (step.Kind is StepKind.Fall)
        {
            // Landed level with the target or below it: this is not a descent any more,
            // pressing a direction does nothing, and the drift tolerance is wide enough
            // that staleness never fires.
            if (!airborne && at.Y >= step.To.Y)
            {
                Behaviour = $"landed at ({at.X}, {at.Y}); replanning";
                return Outcome.Replan;
            }

            // Steer to where the body belongs and stop there.
            character.Movement.Align(step.To);
            Behaviour = $"dropping to ({step.To.X}, {step.To.Y}), "
                        + $"{progress.Left} steps left";
            return Outcome.Acting;
        }

        Press press = Steering.Wants(step, takeoff, at);
        character.Movement.Walk(press.Direction);
        if (press.Jump)
        {
            character.Movement.Leap(step.To.Y * 16f);
        }

        Behaviour = $"{step.Kind.ToString().ToLowerInvariant()} to "
                    + $"({step.To.X}, {step.To.Y}), {progress.Left} steps left";
        return Outcome.Acting;
    }
}
