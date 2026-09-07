
using Microsoft.Xna.Framework;

namespace Terragent.Controls;

/// <summary>The cursor and the use button: the only way anything in the world is touched.</summary>
// Mining, chopping, placing a bench and lighting a torch are all the same three calls:
// hold the right thing, point at the tile, press use. What differs between them is which
// item and which tile, and both are the job's to know.
//
// Which item is held is the bag's, since that is where items are. This is what the held
// thing can reach and whether the button went down.
internal interface IHand
{
    /// <summary>What is in the hand, or zero when it is empty.</summary>
    // Asked before using, because holding is a request that the bag can refuse: there is
    // no pickaxe to fetch if none was ever picked up.
    int Held { get; }

    /// <summary>Whether the last <see cref="Use"/> went down or was withheld.</summary>
    // Terraria refuses a swing on the frame the cursor is over the interface, and this
    // refuses one when the game's own target has drifted off the aimed tile, which
    // happens on the frame a fall begins. Anything counting swings has to skip these, or
    // a tile gets called unbreakable after sixty ticks of nothing being swung at it.
    bool Blocked { get; }

    /// <summary>Whether the held item could be used on this tile from where the body is.</summary>
    bool InReach(int x, int y);

    /// <summary>Whether a block could be put here, which is a wider box than using one.</summary>
    // Terraria allows placing further than mining. Sharing one answer costs a bridge
    // step every time, since the body then walks to swinging range to place a tile it
    // could already have reached.
    bool InPlaceReach(int x, int y);

    /// <summary>Whether the held item could be used on this tile from a footing.</summary>
    // From a footing rather than from where the body is, because the search asks about
    // places the body has not been yet, and a route chosen by a radius stops wherever the
    // radius allows rather than where the box does. The box is six rows tall above the
    // feet and two below it, so any one number stands in for it wrongly in one direction.
    bool CanUseFrom(Point footing, int x, int y);

    /// <summary>The same for putting a block down, which is the wider box.</summary>
    bool CanPlaceFrom(Point footing, int x, int y);

    /// <summary>Whether a swing would reach this from a footing, with nothing in the way.</summary>
    // Range and a clear line together, because either alone is wrong: a jellyfish three
    // tiles off through a wall is in range and unhittable.
    //
    // From a footing rather than from where the body is, because the search asks about
    // places the body has not been yet. A test that answers about the here and now says no
    // to every candidate, and the search then reports that a creature four tiles away
    // cannot be reached at all.
    /// <param name="stretch">
    /// How much further than the plain reach counts, as a multiple of it.
    /// </param>
    // For hysteresis. A target hovering at the edge of range otherwise makes the body
    // step in, swing, step back out, and a fight becomes a dance.
    /// <param name="itemID">The weapon it would be swung with.</param>
    // The item it intends to use, not the one in hand. A fight is planned before the
    // weapon is drawn, and asking about the axe still held from the last tree says a
    // creature two tiles away cannot be reached.
    bool CanHitFrom(Point footing, Vector2 target, int itemID, float stretch = 1f);

    /// <summary>Put the cursor on a place in the world rather than on a tile.</summary>
    // A creature is not on the tile grid, and rounding it onto one costs up to eight
    // pixels of aim: the difference between a swing landing and missing at the edge of
    // reach, and which way a projectile goes.
    void AimAt(Vector2 where);

    /// <summary>Put the cursor on a tile.</summary>
    // Separate from Use, and both are called every tick a job is working. The game
    // recomputes its own target after this hook runs, so aiming once and pressing later
    // swings at wherever the camera has since moved to.
    void Aim(int x, int y);

    /// <summary>Press use for this tick, which the caller repeats for as long as it wants.</summary>
    void Use();
}
