using Microsoft.Xna.Framework;

namespace Terragent.Report;

/// <summary>
/// Keeps a lookout on the follower: says once when the body stops moving, and when
/// its goal or its first step keeps flipping.
/// </summary>
// Watched, not acted on. Either the goal or the first step alternating is the
// signature of every oscillation so far, and the line it writes is what makes a stuck
// run readable. A detector that acted would paper over a plan that should not flip.
internal sealed class Lookout
{
    private const float Twitch = 0.5f;

    private const int Complain = 30;

    private readonly Oscillation _goalFlips = new("goal");

    private readonly Oscillation _stepFlips = new("first step");

    private Vector2 _lastAt;

    private bool _toldStall;

    /// <summary>Consecutive ticks the body has been on a move and not moved.</summary>
    public int Motionless { get; private set; }

    /// <summary>Note this tick's goal and first step, so a flip between ticks is seen.</summary>
    // The goal is where the route is going: the settled candidate when there are
    // several, since the first of a list reorders as sites are mined. The first step is
    // what the search made of it.
    public void Flips(Point asked, Step? holding, string behaviour, Belief belief, Point at,
        double now)
    {
        _goalFlips.Observe($"({asked.X},{asked.Y})", behaviour, belief, at, now);
        if (holding is { } step)
        {
            _stepFlips.Observe($"{step.Kind} to ({step.To.X},{step.To.Y})",
                behaviour, belief, at, now);
        }
    }

    /// <summary>Notice the body not moving on a step, and say so once per episode.</summary>
    /// <param name="digging">Whether the step is still clearing its way, which is not a stall.</param>
    /// <param name="keys">Which keys are down, as the follower left them this tick.</param>
    public void Stall(Belief belief, Step step, Point at, bool digging, Vector2 centre,
        Vector2 velocity, string keys)
    {
        if (digging || (centre - _lastAt).Length() > Twitch)
        {
            Motionless = 0;
            _toldStall = false;
            _lastAt = centre;
            return;
        }

        _lastAt = centre;
        if (++Motionless < Complain || _toldStall)
        {
            return;
        }

        _toldStall = true;
        Log.Sample("stalled", $"{Motionless} ticks going nowhere",
            ("at", $"({at.X}, {at.Y})"),
            ("step", $"{step.Kind} to ({step.To.X}, {step.To.Y})"),
            ("centre", $"{centre.X:0.0},{centre.Y:0.0}"),
            ("vel", $"{velocity.X:0.00},{velocity.Y:0.00}"),
            ("keys", keys),
            ("under", belief.KindAt(at.X, at.Y).ToString()),
            ("underRight", belief.KindAt(at.X + 1, at.Y).ToString()),
            ("shaped", belief.Underfoot(at.X, at.Y) || belief.Underfoot(at.X + 1, at.Y)),
            ("grid", Diagnose.Draw(belief, at, step.To)));
    }
}
