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

    /// <summary>Pixels gained sideways in this many frames, accelerating up to the run cap.</summary>
    private float Sideways(float frames)
    {
        if (RunAcceleration <= 0f)
        {
            return RunSpeed * frames;
        }

        float untilTop = RunSpeed / RunAcceleration;
        return frames <= untilTop
            ? 0.5f * RunAcceleration * frames * frames
            : (0.5f * RunAcceleration * untilTop * untilTop)
                + (RunSpeed * (frames - untilTop));
    }

    public void Walk(int direction)
    {
        _player.controlRight = direction > 0;
        _player.controlLeft = direction < 0;
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
