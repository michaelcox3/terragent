using Microsoft.Xna.Framework;
using Terraria.ObjectData;

namespace Terragent.World;

/// <summary>Whether a tile can actually be put somewhere.</summary>
// Furniture is not one cell. A work bench is two columns wide, and asking only about the
// column aimed at picks a spot whose other half is inside rock: the swing goes out, the
// game refuses without a word, and the body stands there pressing use for ever.
//
// The size is read from the game rather than written down, because it is the game that
// decides and every station has its own.
internal static class Placement
{
    /// <summary>The cells a tile of this kind fills when put at <paramref name="at"/>.</summary>
    // The cursor names the object's origin, which for most furniture is its top left but
    // is not required to be, so the anchor is subtracted rather than assumed to be zero.
    public static Rectangle Covers(int tileID, Point at)
    {
        TileObjectData? shape = TileObjectData.GetTileData(tileID, 0);
        if (shape is null)
        {
            return new Rectangle(at.X, at.Y, 1, 1);
        }

        return new Rectangle(
            at.X - shape.Origin.X,
            at.Y - shape.Origin.Y,
            shape.Width,
            shape.Height);
    }

    /// <summary>Whether a tile of this kind would stand at <paramref name="at"/>.</summary>
    // Every cell it fills has to be clear, and every column has to have something under it
    // that will carry a building.
    public static bool Fits(ITerrain terrain, int tileID, Point at)
    {
        Rectangle covers = Covers(tileID, at);

        for (int x = covers.Left; x < covers.Right; x++)
        {
            for (int y = covers.Top; y < covers.Bottom; y++)
            {
                if (!terrain.Buildable(x, y))
                {
                    return false;
                }
            }

            if (!Bears(terrain, x, covers.Bottom))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether this cell would carry a building, which is not what carries a body.</summary>
    // A half block and a floor slope both hold the character up and neither anchors
    // furniture: Terraria takes a whole solid tile or a platform and refuses the rest. The
    // two questions were one predicate, and a bench aimed at a smoothed floor was refused
    // silently every tick.
    private static bool Bears(ITerrain terrain, int x, int y) =>
        terrain.KindAt(x, y) is TileKind.Solid or TileKind.Platform;
}
