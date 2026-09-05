using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace Terragent.Report;

/// <summary>
/// Notices the agent going nowhere, and draws the ground it is going nowhere on.
/// </summary>
internal static class Diagnose
{
    private const int Width = 25;

    private const int Height = 13;

    /// <summary>How long the agent may occupy the same two tiles before it is stuck.</summary>
    private const double Patience = 4.0;

    /// <summary>Seconds between dumps, so a long stall does not fill the journal.</summary>
    private const double Quiet = 15.0;

    private static readonly Queue<(Point At, double When)> _recent = new();
    private static double _lastDump = double.NegativeInfinity;

    public static void Watch(Belief belief, Point at, Point? goal, string behaviour,
        double now)
    {
        _recent.Enqueue((at, now));
        while (_recent.Count > 0 && now - _recent.Peek().When > Patience)
        {
            _recent.Dequeue();
        }

        // Two tiles is oscillation; one is a stall. Both are going nowhere, and the
        // difference does not change what is worth looking at.
        HashSet<Point> distinct = [];
        foreach ((Point tile, double _) in _recent)
        {
            distinct.Add(tile);
        }

        bool stuck = _recent.Count > Patience * 30 && distinct.Count <= 2;
        if (!stuck || now - _lastDump < Quiet)
        {
            return;
        }

        _lastDump = now;
        Log.Sample("terrain", behaviour,
            ("at", at.ToString()),
            ("goal", goal?.ToString() ?? "none"),
            ("grid", Draw(belief, at, goal)));
    }

    /// <summary>The belief around a point, in the harness's own notation.</summary>
    public static string Draw(Belief belief, Point at, Point? goal)
    {
        StringBuilder sketch = new();
        int left = at.X - (Width / 2);
        int top = at.Y - (Height / 2);

        for (int y = top; y < top + Height; y++)
        {
            if (y > top)
            {
                sketch.Append('|');
            }

            for (int x = left; x < left + Width; x++)
            {
                sketch.Append(Cell(belief, new Point(x, y), at, goal));
            }
        }

        return sketch.ToString();
    }

    private static char Cell(Belief belief, Point cell, Point at, Point? goal)
    {
        // Both columns of the footing, one row up: where a case file would put
        // the marker, on the ground rather than in it.
        if (Body.Covers(at, cell.X) && cell.Y == at.Y - 1)
        {
            return '@';
        }

        if (goal is { } target && cell == target)
        {
            return 'G';
        }

        if (!belief.IsKnown(cell.X, cell.Y))
        {
            return '?';
        }

        if (belief.HasLava(cell.X, cell.Y))
        {
            return 'L';
        }

        if (belief.HasWater(cell.X, cell.Y))
        {
            return 'w';
        }

        return belief.KindAt(cell.X, cell.Y) switch
        {
            // Named apart, because a sketch is what a scenario gets copied out
            // of.
            TileKind.Slab when belief.Underfoot(cell.X, cell.Y) =>
                belief.SlopeIsLowOnLeft(cell.X, cell.Y) switch
                {
                    true => '/',
                    false => '\\',
                    null => '_',
                },
            TileKind.Platform => '=',
            TileKind.Solid => belief.CanKill(cell.X, cell.Y) ? '#' : 'X',
            _ => '.',
        };
    }

    /// <summary>The tile kinds between here and there, as one readable string.</summary>
    public static string Column(Belief belief, Point at, Point goal)
    {
        System.Text.StringBuilder sketch = new();
        int steps = System.Math.Min(24,
            System.Math.Max(System.Math.Abs(goal.X - at.X), System.Math.Abs(goal.Y - at.Y)));
        for (int step = 0; step <= steps; step++)
        {
            int x = at.X + (goal.X - at.X) * step / System.Math.Max(1, steps);
            int y = at.Y + (goal.Y - at.Y) * step / System.Math.Max(1, steps);
            sketch.Append(belief.KindAt(x, y) switch
            {
                // Distinguished from ordinary rock on purpose: a tile the game
                // refuses to break looks identical to one that simply has not
                // broken yet.
                TileKind.Solid => belief.CanKill(x, y) ? '#' : 'X',
                TileKind.Platform => '=',
                TileKind.Slab => '_',
                TileKind.Empty => '.',
                _ => '?',
            });
        }

        return sketch.ToString();
    }
}
