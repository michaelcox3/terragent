using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>
/// The terrain the search plans over.
/// </summary>
internal interface ITerrain
{
    TileKind KindAt(int x, int y);

    int TypeAt(int x, int y);

    bool IsKnown(int x, int y);

    /// <summary>How lit a cell is, from nothing at zero to full daylight at one.</summary>
    // The one reading here that is not gated on the map, and cannot be: a cell reaches the
    // map by having appeared lit, so gating this would answer nothing about the cells worth
    // asking about.
    float Brightness(int x, int y);

    /// <summary>
    /// Whether this tile can be mined with this pickaxe: strong enough, and permitted.
    /// </summary>
    bool Diggable(int x, int y, int pickPower);

    /// <summary>Whether this cell holds liquid.</summary>
    bool Wet(int x, int y);

    bool Buildable(int x, int y);

    /// <summary>
    /// Whether a tile is here that neither holds the body up nor blocks it, but has to go
    /// before anything can be placed.
    /// </summary>
    // Grass, plants, torches, cobwebs. The body walks through them, so no move ever books
    // one to be broken, and the cell still reads as somewhere a block could go. Terraria
    // then refuses the placement in silence: a run pillared into a sunflower and laid wood
    // at it four times a second until it was killed. They break to any swing, so this is
    // about noticing them rather than about paying for them.
    bool Clutter(int x, int y);

    bool HasWater(int x, int y);

    /// <summary>Whether this tile falls once what is under it goes.</summary>
    // Sand and its kin collapse into the hole they are dug from, bury the shaft behind the
    // body and land on its head, so a route through them is worth more than a route round.
    bool Falls(int x, int y);

    bool HasLava(int x, int y);
}
