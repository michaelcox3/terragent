using Microsoft.Xna.Framework;

namespace Terragent.Follow;

/// <summary>What a step wants held this frame.</summary>
/// <param name="Direction">-1 left, 0 nothing, 1 right.</param>
/// <param name="Jump">Whether the jump key should be down.</param>
internal readonly record struct Press(int Direction, bool Jump);

/// <summary>
/// Turning the step the body is on into keys.
/// </summary>
// Deciding and pressing are apart on purpose. This file names no Terraria type, which
// is what lets the headless harness ask what the follower intends. A function, not an
// object: the takeoff footing is the only runtime state, since the body may leave the
// ground a column off the footing the route named, and the follower hands it in.
internal static class Steering
{
    /// <summary>
    /// What this step wants pressed, from where the body is.
    /// </summary>
    /// <param name="takeoff">
    /// The footing the body last stood on, which is where a jump in flight began.
    /// </param>
    public static Press Wants(Step step, Point takeoff, Point at)
    {
        if (step.Kind is not StepKind.Jump)
        {
            return new Press(System.Math.Sign(step.To.X - at.X), false);
        }

        // A jump is one action, so it gets one heading, chosen from where it began.
        // Mid-air input that flips as the body crosses the target column makes it fight
        // its own momentum.
        int heading = System.Math.Sign(step.To.X - takeoff.X);
        bool sidestep = System.Math.Abs(step.To.X - takeoff.X) <= 1;

        // Steer until above the column, then stop and let it drop in. A one-column jump
        // rises first and steps across at the apex, as the search's arc assumes;
        // pressing the heading from the first frame puts the body under the ledge, one
        // row up and falling. A longer jump needs the run-up and keeps its heading.
        //
        // Across once the head is level with the landing surface, three rows before the
        // feet: waiting for the feet to clear the lip leaves no time to build sideways
        // speed before the apex. Pressing into the ledge's side while rising costs
        // nothing, since Terraria slides the body up the wall and over once the feet
        // clear it.
        int direction = at.X == step.To.X ? 0
            : sidestep && at.Y - (Body.Height - 1) > step.To.Y ? 0
            : heading;

        // Jump while at or below the aimed row. Above it the climb is done and holding
        // the key only overshoots: Terraria keeps rising for about two tiles after
        // release. "Strictly below" is false for a level jump from a standstill, and
        // the key would never be pressed.
        return new Press(direction, at.Y >= step.To.Y);
    }
}
