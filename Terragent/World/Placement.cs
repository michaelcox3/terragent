using System.Collections.Generic;
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
    /// <summary>How far out to look for somewhere to stand a tile.</summary>
    // Close, because the point is to put it where the run already is. A bench eight tiles
    // away is a walk, and the walk is what standing one up exists to avoid repeating.
    private const int Nearby = 8;

    /// <summary>Rows to try either side of the body's own, nearest first.</summary>
    // Not the body's row alone. Mining stone walks the body down a shaft of its own
    // digging, and along that one row there is rock to both sides for ever. Up before
    // down, since climbing out of the hole is what is wanted.
    private static readonly int[] Rows = [0, -1, 1, -2, 2, -3, 3];

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

    /// <summary>Whether a tile of this kind would stand at <paramref name="at"/> as things are.</summary>
    public static bool Fits(ITerrain terrain, int tileID, Point at) =>
        Needs(terrain, tileID, at, 0) is { Ready: true };

    /// <summary>What standing one here would take, or null when nothing would.</summary>
    public static Spot? Needs(ITerrain terrain, int tileID, Point at, int pickPower) =>
        Spot.Read(terrain, at, Covers(tileID, at), pickPower);

    /// <summary>
    /// The cheapest spot near a footing to stand one of these, or null when there is none.
    /// </summary>
    // A spot that already fits beats one that has to be dug out however much nearer the
    // digging is, because the rings are walked outward and the first ready one wins. Only
    // when nothing fits does the least work decide, which is what keeps a bench from being
    // planted in a wall when there is open floor a step away.
    /// <param name="blocks">How many blocks may be spent flooring it.</param>
    public static Spot? Find(ITerrain terrain, int tileID, Point from, int pickPower,
        int blocks)
    {
        Spot? cheapest = null;

        // Never where the body is standing. Terraria will not put a tile inside the
        // character, so a spot under its own feet has to be jumped clear of first, and in
        // a tunnel three rows tall there is nowhere to rise to: the run hits its head on
        // the ceiling and tries again for ever. A bench and a body are both two columns
        // wide, which is why the column beside the body is still the body.
        Rectangle standing = Hitbox.Fills(from);

        for (int ring = 1; ring <= Nearby; ring++)
        {
            for (int across = -ring; across <= ring; across += ring * 2)
            {
                foreach (int down in Rows)
                {
                    Point at = new(from.X + across, from.Y + down);
                    if (Covers(tileID, at).Intersects(standing)
                        || Needs(terrain, tileID, at, pickPower) is not { } spot
                        || spot.Fill.Count > blocks)
                    {
                        continue;
                    }

                    if (spot.Ready)
                    {
                        return spot;
                    }

                    if (cheapest is null || spot.Work < cheapest.Work)
                    {
                        cheapest = spot;
                    }
                }
            }
        }

        return cheapest;
    }
}
