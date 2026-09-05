using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.Act;

/// <summary>
/// The body: where it is, how it is moving, what a move costs it, and the keys that
/// move it.
/// </summary>
// One of the three units of Act: the keys left, right and jump, and the body they
// move. Velocity, the grounded test and Leap sit together because "why did it not
// jump" is one question.
//
// Not Body, which stays static in World: 20 by 42 pixels for every player alive, and
// the headless harness has no Player at all to ask.
internal sealed class Movement(Player player)
{
    private readonly Player _player = player;

    /// <summary>The footing the body is on: left column, and the row underfoot.</summary>
    // A footing names the body's *left* column, because the search needs both of the two
    // it covers. Not the same question as At below, and the difference is half a tile.
    public Point Footing => Body.Footing(_player.position, _player.height);

    /// <summary>Where the agent is, in tiles, for anything measuring distance from it.</summary>
    // The body's middle column, where the footing names its left. Measuring from the
    // left would favour whichever side the body happened to straddle.
    public Point At => new((int)(_player.Center.X / 16f), Footing.Y);

    /// <summary>The body's middle, in pixels.</summary>
    public Vector2 Center => _player.Center;

    /// <summary>How the body is moving, in pixels per tick.</summary>
    public Vector2 Velocity => _player.velocity;

    /// <summary>The body's box, in pixels. What Terraria tests a pickup against.</summary>
    public Rectangle Frame => _player.getRect();

    /// <summary>The body's top speed on the ground, in pixels per tick.</summary>
    public float RunSpeed => _player.maxRunSpeed;

    /// <summary>Whether anything is actually holding the body up.</summary>
    // The game's own collision, one pixel down. Not "is the tile underfoot standable",
    // which answers yes for a body sailing past a ledge in the next column, and not "is
    // the vertical velocity zero", which answers no walking down a slope.
    //
    // acceptTopSurfaces, or a platform or work bench reads as airborne: the three
    // argument overload counts only whole blocks, and the follower will not replan in
    // mid-air.
    public bool Grounded =>
        Collision.SolidCollision(_player.position + new Vector2(0f, 1f),
            _player.width, _player.height, acceptTopSurfaces: true);

    /// <summary>Frames a jump stays powered while the key is held, on dry ground.</summary>
    // Player.jumpHeight and Player.jumpSpeed are statics the game rewrites every frame
    // for whoever is being updated, water included: read from a pool floor they promise
    // a nine-row jump that makes three. The search plans with the dry base, and a wet
    // jump that falls short is refused and routed round.
    private const int JumpFrames = 15;

    private const float BaseJumpSpeed = 5.01f;

    /// <summary>
    /// How fast this character leaves the ground, with what it is wearing.
    /// </summary>
    // An accessory changes jumpSpeedBoost, which is per-player: reading the base alone
    // means a Frog Leg buys nothing. Additive, because that is what the field is.
    //
    // The balloon's own jumpBoost flag is deliberately not modelled. It multiplies, by a
    // factor this has not measured, and the two errors are not symmetric: understating a
    // jump digs where it could have hopped, overstating it steps off a ledge.
    private float JumpSpeed => BaseJumpSpeed + _player.jumpSpeedBoost;

    /// <summary>Gravity as the jump numbers assume it: dry, and never zero, because things divide.</summary>
    // The live value is halved in water, for the same reason as the constants above.
    private float Gravity => System.Math.Max(0.01f, Terraria.Player.defaultGravity);

    /// <summary>
    /// How far it keeps rising after the jump key is let go, in pixels.
    /// </summary>
    public float CoastPixels => JumpSpeed * JumpSpeed / (2f * Gravity);

    /// <summary>
    /// How many tiles this character's jump clears upward, understated by one.
    /// </summary>
    // <b>Not Player.jumpHeight, which is fifteen.</b> Terraria's counts the frames the
    // ascent stays powered while the key is held; this is the six tiles that buys. The
    // sideways distance is LeapColumns in the search, and calling both "tiles" once let
    // a distance stand in for a height.
    public int JumpHeight => Jump.Height;

    /// <summary>What this character's jump can reach, from a standstill.</summary>
    public World.Leap Jump => World.Leap.Of(JumpFrames, JumpSpeed, Gravity, _player.runAcceleration,
        _player.maxRunSpeed);

    /// <summary>
    /// Frames in the air on a jump that ends at the height it started from.
    /// </summary>
    // Powered ascent while the key is held, then a coast up as gravity takes the
    // remaining speed away, then a fall back down the whole of it.
    public float AirFrames
    {
        get
        {
            float rise = (JumpFrames * JumpSpeed) + CoastPixels;
            return JumpFrames
                   + (JumpSpeed / Gravity)
                   + (float)System.Math.Sqrt(2f * rise / Gravity);
        }
    }

    /// <summary>
    /// How far a jump carries sideways, in pixels, at a given starting speed.
    /// </summary>
    // Terraria steers in the air with the same acceleration it runs with, so a jump from
    // a standstill is not a jump that goes straight up: it picks up speed on the way.
    // Distance is what the character has already plus what it gains, capped by the run
    // speed it cannot exceed.
    public float LeapPixels(float speed)
    {
        float frames = AirFrames;
        float top = System.Math.Max(1f, _player.maxRunSpeed);
        float accelerate = _player.runAcceleration <= 0f
            ? frames
            : System.Math.Min(frames, (top - speed) / _player.runAcceleration);

        float gaining = (speed * accelerate)
                        + (0.5f * _player.runAcceleration * accelerate * accelerate);
        return gaining + (top * (frames - accelerate));
    }

    /// <summary>Which of left, right and jump are down, as three letters for the journal.</summary>
    public string Pressed =>
        $"{(_player.controlLeft ? "L" : "-")}{(_player.controlRight ? "R" : "-")}{(_player.controlJump ? "J" : "-")}";

    /// <summary>Hold a direction, or nothing when it is zero.</summary>
    public void Walk(int direction)
    {
        _player.controlRight = direction > 0;
        _player.controlLeft = direction < 0;
    }

    private bool _jumped;

    /// <summary>
    /// Press jump, leaving a gap between one jump and the next.
    /// </summary>
    // Public because a pillar jumps too: it places the tile where the legs are, so
    // rising out of the way is the mechanism rather than a flourish. It has to be this
    // same Leap, sharing the release latch, or each would think the key had been let go
    // while the other still held it.
    public void Leap(float topOfTarget)
    {
        // Terraria will not start a jump while the key is already held:
        // Player.releaseJump has to see it down after being up. Hence the frame off once
        // back on the ground, with nothing else pressed in it either, or a body on the
        // edge of a one-wide pillar slides off in that frame.
        //
        // Wet counts as grounded, so the key pumps: in water a jump starts again without
        // touching the ground, which is how a player swims upward. The coast rule below
        // never fires there, because a wet jump has no coast worth the name.
        bool grounded = _player.velocity.Y == 0f || _player.wet;
        if (grounded && _jumped)
        {
            _jumped = false;
            _player.controlLeft = false;
            _player.controlRight = false;
            return;
        }

        // Hold only as long as the climb still needs pushing. Terraria's jump keeps
        // rising about two tiles after the key is let go, so holding to the target row
        // overshoots by that much and makes a one-tile hop the same input as a leap.
        if (!grounded && _player.Bottom.Y - topOfTarget <= CoastPixels * 0.5f)
        {
            return;
        }

        _player.controlJump = true;
        _jumped = grounded || _jumped;
    }

    /// <summary>Walk toward a place, in pixels rather than tiles.</summary>
    public void Toward(float pixelX)
    {
        const float Slack = 1f;

        float error = pixelX - _player.Center.X;
        if (System.Math.Abs(error) <= Slack)
        {
            return;
        }

        _player.controlRight = error > 0f;
        _player.controlLeft = error < 0f;
    }

    /// <summary>
    /// Hold the body over the columns the plan put it in.
    /// </summary>
    public void Align(Point footing)
    {
        const float Slack = 2f;
        const float Still = 0.05f;

        float error = Body.SeamX(footing) - _player.Center.X;
        float speed = _player.velocity.X;

        // How far the character drifts if nothing is pressed. runSlowdown is the bite
        // friction takes out of horizontal speed each tick.
        float drag = System.Math.Max(0.05f, _player.runSlowdown);
        float coast = speed * speed / (2f * drag);

        if (System.Math.Abs(error) <= Slack)
        {
            // Arriving is not the same as stopping, and momentum is what carries a
            // character over a two-wide hole.
            if (speed > Still)
            {
                _player.controlLeft = true;
            }
            else if (speed < -Still)
            {
                _player.controlRight = true;
            }

            return;
        }

        // Already carrying enough speed to arrive. Pressing harder only overshoots,
        // which is how the deadband above gets entered at a run in the first place.
        if (speed * error > 0f && coast >= System.Math.Abs(error))
        {
            return;
        }

        _player.controlRight = error > 0f;
        _player.controlLeft = error < 0f;
    }
}
