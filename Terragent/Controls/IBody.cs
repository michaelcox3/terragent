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

    /// <summary>Frames to hold the jump key, to land this far across and this far up.</summary>
    // A jump goes as far sideways as it stays in the air, and it stays in the air as long
    // as it climbs, so the length of the press is the whole of the aim. Held to the top of
    // the arc every time, this body crosses nine and a half tiles where the search planned
    // six and never touches down on the footing.
    //
    // Frames, because frames are what the key is held for. Answered as a height instead, it
    // had to be turned back into a press by watching the body against a coasting estimate,
    // and that round trip cost a frame either way: an arc that cleared its landing by two
    // pixels, against five pixels of climb a frame, missed.
    //
    // From the speed the body is actually carrying rather than from a standstill, since by
    // the time it leaves the ground it has a run up and that is most of the difference
    // between a 5.97 tile jump and a 9.49 one.
    // Up and never down, because a jump that lands below where it left is a Fall: the
    // search reads the landing row from the takeoff row upward, and says so where it does
    // it. Zero is a level hop and the number only ever grows from there.
    int HoldFor(int across, int up);

    /// <summary>The pixels the body actually fills, which a tile pair only approximates.</summary>
    // Within a footing the body slides, and whether it has risen clear of the cell a block
    // is going into is a question about pixels. Asking the tile pair says yes while the
    // feet are still in the way and the game refuses the placement without a word.
    Rectangle Frame { get; }

    /// <summary>Hold left, right, or neither. Minus one, one, or zero.</summary>
    void Walk(int direction);

    /// <summary>Park the body on a footing's seam and stop it there.</summary>
    // For the two steps that need the body still and in the middle: laying a block, which
    // the game refuses inside the body, and cutting the floor out, which rests the body on
    // the lip of its own shaft if it is off to one side. Walking to a footing is not one of
    // them and does not come through here, since this brakes on arrival and a walk wants to
    // keep what it is carrying.
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
    void Leap(int frames);

    /// <summary>Hold down, which is how a body drops through a platform.</summary>
    void Down();
}
