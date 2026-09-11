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

    bool HasWater(int x, int y);

    bool HasLava(int x, int y);
}
