using Microsoft.Xna.Framework;
using Terraria;
using Terragent.Pathfinding;

namespace Terragent.Controls;

/// <summary>The player, as a body.</summary>
// The one place in the mod that reads Terraria's movement fields and sets its controls.
internal sealed class Body(Player player) : IBody
{
    private readonly Player _player = player;

    /// <summary>Frames a jump stays powered while the key is held, on dry ground.</summary>
    // Player.jumpHeight and Player.jumpSpeed are statics the game rewrites every frame for
    // whoever it is updating, water included: read from a pool floor they promise a nine
    // row jump that makes three. The search plans with the dry base, and a wet jump that
    // falls short is refused and routed round.
    private const int DryJumpFrames = 15;

    private const float BaseJumpSpeedPixels = 5.01f;

    /// <summary>Whether the last tick asked for a jump, so the key can be let go.</summary>
    private bool _jumped;

    /// <summary>Frames the jump key has been held for on this jump.</summary>
    private int _held;

    public Point Footing => World.Hitbox.Footing(_player.position, _player.height);

    /// <summary>Whether anything is actually holding the body up.</summary>
    // The game's own collision, one pixel down. Not whether the tile underfoot is
    // standable, which says yes for a body sailing past a ledge in the next column, and
    // not whether vertical speed is zero, which says no walking down a slope.
    //
    // acceptTopSurfaces, or a platform or work bench reads as airborne and the follower
    // will not replan in mid air.
    public bool Grounded =>
        Collision.SolidCollision(_player.position + new Vector2(0f, 1f),
            _player.width, _player.height, acceptTopSurfaces: true);

    public bool Submerged => _player.wet || _player.lavaWet || _player.honeyWet;

    public float RunSpeed => _player.maxRunSpeed;

    /// <summary>How fast this character leaves the ground, with what it is wearing.</summary>
    // An accessory changes jumpSpeedBoost, which is per player: reading the base alone
    // means a Frog Leg buys nothing. Additive, because that is what the field is.
    public float JumpSpeed => BaseJumpSpeedPixels + _player.jumpSpeedBoost;

    public float RunAcceleration => _player.runAcceleration;

    public int JumpFrames => DryJumpFrames;

    /// <summary>Gravity as the jump numbers assume it: dry.</summary>
    // The live value is halved in water, for the same reason as the constants above.
    public float Gravity => Player.defaultGravity;

    /// <summary>What this body's jump reaches, from a standstill.</summary>
    // From a standstill on purpose, since the search cannot know whether a takeoff has a
    // run up. Understating a jump costs a dig, overstating one costs a fall.
    public Leap Arc()
    {
        float gravity = System.Math.Max(0.01f, Gravity);
        float coast = JumpSpeed * JumpSpeed / (2f * gravity);
        float apexHeight = (JumpFrames * JumpSpeed) + coast;
        float apexFrame = JumpFrames + (JumpSpeed / gravity);
        int height = System.Math.Max(1, (int)(apexHeight / 16f));

        int[] reach = new int[height + 1];
        for (int rowsUp = 0; rowsUp <= height; rowsUp++)
        {
            float landing = rowsUp * 16f;
            if (landing > apexHeight)
            {
                continue;
            }

            // Down through the landing height on the far side of the apex. Terraria
            // steers in the air with the run acceleration, so sideways distance is what a
            // standing start gains in that many frames, capped by the run speed.
            float frames = apexFrame
                + (float)System.Math.Sqrt(2f * (apexHeight - landing) / gravity);

            // Half a tile of slack: a footing is two columns and the body lands where
            // momentum leaves it, within a column of the tile named.
            float pixels = Sideways(frames);
            reach[rowsUp] = System.Math.Max(0, (int)((pixels + 8f) / 16f));
        }

        return new Leap(height, reach);
    }

    /// <summary>Pixels of clearance a chosen arc must have over its landing.</summary>
    // The shortest hold that reaches is the right answer for distance and the wrong one for
    // height. Whether to let go is decided once a frame and a frame is five pixels of
    // climb, so an arc that clears its landing by two pixels misses it whenever the release
    // lands on the wrong side of a frame. A tile is three frames of slop.
    private const float Clearance = 16f;

    public int HoldFor(int across, int up)
    {
        float gravity = System.Math.Max(0.01f, Gravity);
        float coast = JumpSpeed * JumpSpeed / (2f * gravity);
        float wanted = System.Math.Abs(across) * 16f;
        float landing = up * 16f;

        // What it is carrying at this instant, which is the honest starting point: the run
        // up is behind it and the body keeps accelerating through the whole arc.
        float speed = System.Math.Abs(_player.velocity.X);

        // A hop across one column cannot go too far sideways, so there is nothing to
        // shorten for and the height is worth having: getting onto a ledge means shuffling
        // across while above its lip, and every frame of climb given up is a frame there is
        // not to do it in.
        if (System.Math.Abs(across) <= 1)
        {
            return JumpFrames;
        }

        // The shortest hold that still gets there. Longer is always further, so the first
        // one that reaches is the one that overshoots least, and holding the whole fifteen
        // is what the old code did on every jump.
        for (int held = 1; held <= JumpFrames; held++)
        {
            float top = (held * JumpSpeed) + coast;
            if (top < landing + Clearance)
            {
                continue;
            }

            float frames = held + (JumpSpeed / gravity)
                + (float)System.Math.Sqrt(2f * (top - landing) / gravity);
            // Not speed times time. Terraria steers in the air at the run acceleration, so
            // a body that leaves the ground below its cap is still gaining while it flies:
            // read flat, the takeoff speed understates the distance, which asks for more
            // airtime, which picks a higher hold, and the jump goes long twice over.
            if (Travelled(speed, frames) >= wanted)
            {
                return held;
            }
        }

        return JumpFrames;
    }

    /// <summary>Pixels gained sideways in this many frames, accelerating up to the run cap.</summary>
    private float Sideways(float frames) => Travelled(0f, frames);

    /// <summary>The same from a speed already carried, which is what a takeoff has.</summary>
    private float Travelled(float from, float frames)
    {
        float start = System.Math.Min(from, RunSpeed);
        if (RunAcceleration <= 0f)
        {
            return RunSpeed * frames;
        }

        float untilTop = (RunSpeed - start) / RunAcceleration;
        return frames <= untilTop
            ? (start * frames) + (0.5f * RunAcceleration * frames * frames)
            : (start * untilTop) + (0.5f * RunAcceleration * untilTop * untilTop)
                + (RunSpeed * (frames - untilTop));
    }

    public void Walk(int direction)
    {
        _player.controlRight = direction > 0;
        _player.controlLeft = direction < 0;
    }

    public Rectangle Frame =>
        new((int)_player.position.X, (int)_player.position.Y, _player.width, _player.height);

    public void Align(Point footing)
    {
        // Pixels either side of the seam that count as arrived, and the speed below
        // which the body counts as stopped.
        const float Slack = 2f;
        const float Still = 0.05f;

        float error = World.Hitbox.SeamX(footing) - _player.Center.X;
        float speed = _player.velocity.X;

        // How far it drifts with nothing pressed. runSlowdown is the bite friction takes
        // out of horizontal speed each tick.
        float drag = System.Math.Max(0.05f, _player.runSlowdown);
        float coast = speed * speed / (2f * drag);

        if (System.Math.Abs(error) <= Slack)
        {
            // Arriving is not the same as stopping, and momentum is what carries a body
            // over a two wide hole it was meant to drop into.
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

        // Already carrying enough to arrive. Pressing harder only overshoots, which is
        // how the deadband gets entered at a run in the first place.
        if (speed * error > 0f && coast >= System.Math.Abs(error))
        {
            return;
        }

        _player.controlRight = error > 0f;
        _player.controlLeft = error < 0f;
    }

    public void Leap(int frames)
    {
        bool grounded = _player.velocity.Y == 0f || _player.wet;
        if (grounded && _jumped)
        {
            _jumped = false;
            _player.controlLeft = false;
            _player.controlRight = false;
            _held = 0;
            return;
        }

        if (grounded)
        {
            _held = 0;
        }

        // Counted, not watched. The press is what shapes the arc, so holding it for the
        // frames asked for is the whole of the aim: measured instead against how high the
        // body has climbed, the answer is a frame out either way and a frame is five pixels.
        if (_held >= frames)
        {
            return;
        }

        _player.controlJump = true;
        _held++;
        _jumped = grounded || _jumped;
    }

    public void Jump()
    {
        // Terraria will not start a jump while the key is already held: Player.releaseJump
        // has to see it down after being up. Hence the frame off once back on the ground,
        // with nothing else pressed in it either, or a body on the edge of a one wide
        // pillar slides off in that frame.
        //
        // Wet counts as grounded, so the key pumps: in water a jump starts again without
        // touching the ground, which is how a player swims upward.
        bool grounded = _player.velocity.Y == 0f || _player.wet;
        if (grounded && _jumped)
        {
            _jumped = false;
            _player.controlLeft = false;
            _player.controlRight = false;
            return;
        }

        _player.controlJump = true;
        _jumped = grounded || _jumped;
    }

    public void Down() => _player.controlDown = true;
}
