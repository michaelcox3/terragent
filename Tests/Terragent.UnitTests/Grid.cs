using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent;

/// <summary>
/// Terrain typed out as text, for exercising the search without a world.
/// </summary>
/// <remarks>
/// <b>The characters are documented on <see cref="Case"/>, next to the scenarios that
/// use them.</b> Written down here instead, in the harness, they sit in a file nobody
/// opens to write a scenario, and a legend nobody finds gets guessed at.
/// <para>
/// This class is the half that answers questions <i>about</i> a grid. Everything below
/// implements <see cref="ITerrain"/> against the picture, including the rule that
/// outside a grid is rock and above it is sky.
/// </para>
/// </remarks>
internal sealed class Grid : ITerrain
{
    private const int Hard = 999;
    private const int Ordinary = 1;

    private readonly string[] _rows;

    /// <remarks>
    /// Rows must all be the same length. Padding them with empty space turns a short
    /// row silently into a long one full of air: a grid that reads as one shape and
    /// behaves as another, in a suite whose entire subject is reading shapes.
    /// </remarks>
    public Grid(bool border, params string[] rows)
    {
        foreach (string row in rows)
        {
            if (row.Length != rows[0].Length)
            {
                throw new ArgumentException(
                    $"ragged grid: a row is {row.Length} wide and the first is "
                    + $"{rows[0].Length}. Write the empty space out.");
            }
        }

        _rows = rows;
        _border = border;
        Width = rows[0].Length;
        Height = rows.Length;
    }

    private readonly bool _border;

    public int Width { get; }

    public int Height { get; }

    public Point Find(char marker)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_rows[y][x] == marker)
                {
                    return new Point(x, y);
                }
            }
        }

        throw new ArgumentException($"no '{marker}' in the grid");
    }

    /// <summary>
    /// Outside the grid: a body's worth of sky above, then rock; rock everywhere else.
    /// </summary>
    /// <remarks>
    /// <b>The same world the arena builds.</b> It wraps a scenario in stone and leaves
    /// exactly three rows of air over the top, enough for a character standing on the
    /// grid's first row, and a lid above that so it cannot walk off the world. Reading
    /// the sky as infinite is a different world: a route free to climb as high as it
    /// likes is not the route the arena will execute, so the two
    /// levels could disagree about a scenario while each told the truth about its own.
    /// That cost a correct expectation, reverted because the harness contradicted
    /// something that had actually been watched happening.
    /// <para>
    /// Three rows of headroom matters on its own account too: reading the sky as rock
    /// made the whole top surface unstandable, and the search tunnelled along flat
    /// ground rather than walking it.
    /// </para>
    /// </remarks>
    // What lies outside the picture. Rock by default, which walls a grid in and puts a
    // floor under it, and that floor is a thing to fall onto and walk along: a scenario
    // about crossing a gap gets crossed underneath instead, on ground that is not in
    // the drawing. Case.Border turns it off, as it does in the arena.
    private char At(int x, int y) =>
        y < -Body.Height ? Outside
        : y < 0 ? '.'
        : x < 0 || x >= Width || y >= Height ? Outside
        : _rows[y][x];

    private char Outside => _border ? '#' : '.';

    public TileKind KindAt(int x, int y) => At(x, y) switch
    {
        '#' or 'X' or 'H' or 'd' => TileKind.Solid,

        // A work bench: not solid, but standable on top, which is a platform.
        '=' or 'B' => TileKind.Platform,

        // Rock that fills the bottom of its cell. Belief says the same of a half
        // block and a floor slope: stood on like a platform, occupied like a wall.
        '_' or '/' or '\\' => TileKind.Slab,
        '?' => TileKind.Unknown,
        _ => TileKind.Empty,
    };

    public int TypeAt(int x, int y) => At(x, y) == 'H' ? Hard : Ordinary;

    /// <summary>A cell with no tile drawn in it, water included, takes a block.</summary>
    // As the game has it: Belief asks only whether the cell holds a tile, and Terraria
    // places into water and displaces it. Refusing water here meant the harness could
    // not plan a pillar out of a pool that the arena builds and the agent actually
    // stood in.
    public bool Buildable(int x, int y) => At(x, y) is '.' or '@' or 'G' or 'w';

    public bool IsKnown(int x, int y) => KindAt(x, y) is not TileKind.Unknown;

    // Clearance and standability are ITerrain's own, derived from KindAt. This class
    // used to write its own copies and they drifted from Belief's, so a scenario could
    // pass here and fail in the arena with each level telling the truth about its own
    // idea of how tall the character is.

    public bool Wet(int x, int y) => HasWater(x, y) || HasLava(x, y);

    public bool HasWater(int x, int y) => At(x, y) == 'w';

    public bool HasLava(int x, int y) => At(x, y) == 'L';

    public bool Diggable(int x, int y, int pickPower)
    {
        // Outside the grid is the edge of the world, not rock with more rock behind
        // it. Letting the search mine into it opens an unbounded region of diggable
        // border, and "do not walk into fog" then runs out of nodes before reaching a
        // goal ten tiles away. Belief gets this right for free, since IsKnown is false
        // outside the world, so the fake was the one lying.
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return false;
        }

        if (KindAt(x, y) is TileKind.Unknown)
        {
            return true;
        }

        // Half blocks and slopes are rock that happens to be shaped, so they break;
        // a wooden platform is something the agent walks on and leaves alone.
        return (KindAt(x, y) is TileKind.Solid || At(x, y) is '_' or '/' or '\\')
            && At(x, y) != 'X'
            && (At(x, y) != 'H' || pickPower >= 100);
    }

    /// <summary>Every cell the route says it will break.</summary>
    public static HashSet<Point> Excavation(IReadOnlyList<Step>? route)
    {
        HashSet<Point> cut = [];
        foreach (Step step in route ?? [])
        {
            foreach (Point cell in step.Removes)
            {
                cut.Add(cell);
            }
        }

        return cut;
    }

    /// <summary>Every cell the route says it will place a block in.</summary>
    public static HashSet<Point> Pillars(IReadOnlyList<Step>? route)
    {
        HashSet<Point> put = [];
        foreach (Step step in route ?? [])
        {
            if (step.Puts is { } cell)
            {
                put.Add(cell);
            }
        }

        return put;
    }

    /// <summary>The cells a case says must have blocks placed in them.</summary>
    public HashSet<Point> RequiredPlacements() => Marked('p');

    /// <summary>The cells a case says must be broken.</summary>
    public HashSet<Point> Required() => Marked('d');

    private HashSet<Point> Marked(char marker)
    {
        HashSet<Point> wanted = [];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_rows[y][x] == marker)
                {
                    wanted.Add(new Point(x, y));
                }
            }
        }

        return wanted;
    }

    public string Draw(IReadOnlyList<Step>? route, Point? feet = null)
    {
        char[][] canvas = new char[Height][];
        for (int y = 0; y < Height; y++)
        {
            canvas[y] = _rows[y].ToCharArray();
        }

        foreach (Step step in route ?? [])
        {
            if (step.To.Y >= 0 && step.To.Y < Height
                && step.To.X >= 0 && step.To.X < Width)
            {
                canvas[step.To.Y][step.To.X] = '*';
            }

            foreach (Point cell in step.Removes)
            {
                if (cell.Y >= 0 && cell.Y < Height && cell.X >= 0 && cell.X < Width)
                {
                    canvas[cell.Y][cell.X] = 'm';
                }
            }

            if (step.Puts is { } put && put.Y >= 0 && put.Y < Height
                && put.X >= 0 && put.X < Width)
            {
                canvas[put.Y][put.X] = 'P';
            }
        }

        // The body last, over the route. What has to fit is exactly what a grid does
        // not otherwise show, and it is what every clearance bug has been about: two
        // columns because 20 pixels will not sit inside a 16 pixel tile at any
        // alignment, three rows because 42 pixels of body needs more than 32 of gap.
        if (feet is { } stand)
        {
            foreach (Point cell in Body.Cells(stand))
            {
                if (cell.Y >= 0 && cell.Y < Height && cell.X >= 0 && cell.X < Width)
                {
                    canvas[cell.Y][cell.X] = 'b';
                }
            }
        }

        return string.Join("\n", Array.ConvertAll(canvas, row => new string(row)));
    }
}
