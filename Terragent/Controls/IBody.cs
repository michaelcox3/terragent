using Microsoft.Xna.Framework;
using Terragent.Pathfinding;

namespace Terragent.Controls;

/// <summary>Where the body is, and the keys it can be told to press.</summary>
// An interface because it is the whole of what the follower is allowed to touch. Handed
// the character instead, a follower can mine, craft and swing, and the last one grew into
// doing all three.
//
// Position and pressing together, because a body is one thing: it is somewhere, and it
// can be told to move. Splitting them would mean two arguments that must describe the
// same body and nothing making sure they do.
internal interface IBody
{
    /// <summary>The tile pair the body is standing on.</summary>
    Point Footing { get; }

    /// <summary>Whether something is holding it up. False in the air, jumping or falling.</summary>
    bool Grounded { get; }

    /// <summary>Whether the body is in liquid of any kind.</summary>
    // The game's own answer rather than the map's. Everything the map says is gated on the
    // cell having been revealed, and under water in the dark nothing has been, so the one
    // moment this has to be right is the moment the map reads dry.
    bool Submerged { get; }

    /// <summary>Top speed sideways, in pixels per tick.</summary>
    // What a tile of level ground is worth, and the cap on how far a jump carries. Boots
    // and a wet floor both change it, so it is read rather than assumed.
    float RunSpeed { get; }

    /// <summary>How fast the body leaves the ground, in pixels per tick.</summary>
    // With what it is wearing: an accessory raises it, and reading the base alone means a
    // Frog Leg buys the search nothing.
    float JumpSpeed { get; }

    /// <summary>Pixels per tick gained sideways each tick a direction is held.</summary>
    // Terraria steers in the air with the same acceleration it runs with, so a jump from
    // a standstill is not a jump straight up. It is what turns a height into a reach.
    float RunAcceleration { get; }

    /// <summary>Ticks the rise stays powered while the jump key is held.</summary>
    // A count of frames, not a height, whatever Terraria's name for it suggests: the body
    // climbs at JumpSpeed for this many ticks and then coasts. Fifteen of them buys the
    // six tiles a player knows.
    //
    // Answer with the dry base. Player.jumpHeight and Player.jumpSpeed are statics the
    // game rewrites during whichever player it is updating, water and all, so read from a
    // pool floor they promise a nine row jump that makes three.
    int JumpFrames { get; }

    /// <summary>Pixels per tick squared the body falls at, once nothing is holding it up.</summary>
    // The dry value again, since the live one halves in water. It sets how long a jump
    // hangs and how far a fall carries, which is why it is here rather than inside the
    // one thing that happens to need it first.
    float Gravity { get; }

    /// <summary>How high and how far this body's jump reaches, from a standstill.</summary>
    // Worked out from the five numbers above, and asked for as one thing because that is
    // what a search takes. Builds a table, so it is worth once per search rather than
    // once per tick: nothing in it changes until the boots do.
    Leap Arc();

    /// <summary>The pixels the body actually fills, which a tile pair only approximates.</summary>
    // Within a footing the body slides, and whether it has risen clear of the cell a block
    // is going into is a question about pixels. Asking the tile pair says yes while the
    // feet are still in the way and the game refuses the placement without a word.
    Rectangle Frame { get; }

    /// <summary>Hold left, right, or neither. Minus one, one, or zero.</summary>
    void Walk(int direction);

    /// <summary>Hold the body over the columns the plan put it in.</summary>
    // Not the sign of a tile difference. A body straddles two columns, so a step that
    // does not change the footing gives a sign of zero and presses nothing at all: that
    // is a character standing on the lip of the shaft it just dug, waiting to fall.
    //
    // It counter-presses inside the deadband, because arriving is not stopping, and it
    // stays off the key when the speed already carried will coast the rest of the way.
    void Align(Point footing);

    /// <summary>Hold jump for this tick.</summary>
    void Jump();

    /// <summary>Jump, letting go once what is left of the rise will coast to this row.</summary>
    // Terraria keeps climbing about two tiles after the key is released, so holding all
    // the way to the target row overshoots by that much and makes a one tile hop take the
    // same input as a full leap.
    /// <param name="topPixels">The row to arrive at, in pixels.</param>
    void Leap(float topPixels);

    /// <summary>Hold down, which is how a body drops through a platform.</summary>
    void Down();
}
