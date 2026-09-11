using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.World;

/// <summary>A place a tile could stand, and the work to make it stand there.</summary>
/// <param name="At">The cell to aim at, which is the object's own origin.</param>
// The origin, never the corner of what it fills. Terraria puts a multi cell object down by
// its origin, and a furnace is three by two with its origin in the middle of the bottom
// row: aimed at the top left corner instead it is asked for one column left and one row up
// of where it was judged to fit, and refused in silence. A work bench is two by one with
// its origin already in the corner, which is why that one worked and this was not noticed.
/// <param name="Covers">The cells it fills, which is a wider box than the origin.</param>
/// <param name="Clear">Cells in the way, which have to be broken.</param>
/// <param name="Fill">Columns with nothing under them, which have to be floored.</param>
// A bench needs two clear cells with something solid under each, and underground neither
// comes free. Answering only yes or no leaves the job with nowhere to put a bench it is
// carrying and nothing to do about it, which is a run that goes quiet holding a bench.
//
// The cells rather than a count, so that what is swung at is what was judged. A caller
// that re-derived which tile was in the way could swing at a different one from the one
// that made the spot look workable.
internal sealed record Spot(Point At, Rectangle Covers, IReadOnlyList<Point> Clear,
    IReadOnlyList<Point> Fill)
{
    /// <summary>Nothing to do but put the thing down.</summary>
    public bool Ready => Clear.Count == 0 && Fill.Count == 0;

    /// <summary>Swings between here and a standing tile.</summary>
    public int Work => Clear.Count + Fill.Count;

    /// <summary>
    /// What standing a tile of this shape here would take, or null when nothing would.
    /// </summary>
    // Null is "no pickaxe carried will ever open this", not "not yet". Bedrock and a cell
    // the character cannot dig are the same answer to a job looking for somewhere to work.
    /// <param name="at">The cell the game would be aimed at, which is the object's origin.</param>
    /// <param name="covers">The cells the tile would fill, from <see cref="Placement.Covers"/>.</param>
    /// <param name="pickPower">What the carried pickaxe can break.</param>
    public static Spot? Read(ITerrain terrain, Point at, Rectangle covers, int pickPower)
    {
        List<Point> clear = [];
        List<Point> fill = [];

        for (int x = covers.Left; x < covers.Right; x++)
        {
            for (int y = covers.Top; y < covers.Bottom; y++)
            {
                if (terrain.Buildable(x, y))
                {
                    continue;
                }

                if (!terrain.Diggable(x, y, pickPower))
                {
                    return null;
                }

                clear.Add(new Point(x, y));
            }

            if (!Floored(terrain, x, covers.Bottom, pickPower, clear, fill))
            {
                return null;
            }
        }

        return new Spot(at, covers, clear, fill);
    }

    /// <summary>Whether this column can be given something that carries a building.</summary>
    // A half block and a floor slope both hold the character up and neither anchors
    // furniture: Terraria takes a whole solid tile or a platform and refuses the rest. So a
    // smoothed floor is broken first and filled after, which is two ticks and reads as one
    // cell of each kind of work.
    private static bool Floored(ITerrain terrain, int x, int y, int pickPower,
        List<Point> clear, List<Point> fill)
    {
        if (Bears(terrain, x, y))
        {
            return true;
        }

        if (terrain.Buildable(x, y))
        {
            fill.Add(new Point(x, y));
            return true;
        }

        if (!terrain.Diggable(x, y, pickPower))
        {
            return false;
        }

        clear.Add(new Point(x, y));
        fill.Add(new Point(x, y));
        return true;
    }

    /// <summary>Whether this cell would carry a building, which is not what carries a body.</summary>
    private static bool Bears(ITerrain terrain, int x, int y) =>
        terrain.KindAt(x, y) is TileKind.Solid or TileKind.Platform;
}
