#if TESTING
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using Terragent.Pathfinding;
using Terragent.World;

namespace Terragent.Tests;

/// <summary>Ground out of a real world, written as a scenario grid.</summary>
// A run that goes wrong in somebody's world cannot be argued about from a route alone,
// and it cannot be copied out of the .wld either: what the search plans over is the
// player's map and not the world, so an unseen cell is fog here and rock there. Only the
// running game knows which. This reads the same ITerrain the search reads and prints the
// characters the harness parses, so a case can be lifted out of a world and pasted into
// Scenarios.cs.
//
// Test scaffolding, so it is compiled only in Debug behind TESTING and never ships.
internal static class Snapshot
{
    /// <summary>Cells to leave round the outside of what is being copied.</summary>
    private const int Margin = 6;

    /// <summary>The most that will be written, in cells, however far apart things are.</summary>
    // A journey is four thousand tiles wide and a scenario is not. Past this the copy is
    // of somewhere, and which somewhere is no longer obvious, so it stops instead.
    private const int WidestCopy = 160;
    private const int DeepestCopy = 240;

    /// <summary>Pickaxe power at which a tile counts as merely hard rather than barred.</summary>
    // The harness reads 'H' as wanting a hundred and 'X' as never. Terraria has no single
    // number for the second, so anything a full strength pickaxe still cannot break is
    // treated as barred, which is what Ebonstone before a boss actually is.
    private const int FullPick = 100;

    /// <summary>The ground inside a box, as the harness would draw it.</summary>
    // Handed the box rather than working it out. Whoever wants the picture also wants to
    // say where it came from and whether it is all of it, and a renderer that decides its
    // own bounds has to report them back through a second answer to do that.
    public static IReadOnlyList<string> Rows(ITerrain terrain, int pickPower,
        Rectangle copied, Point drawnFrom, Point? goal)
    {
        List<string> rows = [];
        for (int y = copied.Top; y < copied.Bottom; y++)
        {
            char[] row = new char[copied.Width];
            for (int x = copied.Left; x < copied.Right; x++)
            {
                row[x - copied.Left] = Glyph(terrain, pickPower, x, y);
            }

            rows.Add(new string(row));
        }

        // A marker stands on ground and the harness reads it a row above its floor, so
        // the footing row is where the feet are and the mark goes above it.
        Mark(rows, copied, new Point(drawnFrom.X, drawnFrom.Y - 1), '@');
        if (goal is { } site)
        {
            Mark(rows, copied, site, 'G');
        }

        return rows;
    }

    /// <summary>Write the ground out as a file that can be pasted into Scenarios.cs.</summary>
    public static string Write(ITerrain terrain, int pickPower,
        Point body, Point? goal, Route? route)
    {
        // Where the search was standing when it drew this, not where the body has walked
        // to since. A copy started part way along is a different problem from the one that
        // was solved, and the interesting half is usually still ahead of the body.
        Point drawnFrom = route?.From ?? body;

        Rectangle wanted = Box(body, goal, route);
        Rectangle copied = new(wanted.X, wanted.Y,
            System.Math.Min(WidestCopy, wanted.Width),
            System.Math.Min(DeepestCopy, wanted.Height));
        IReadOnlyList<string> rows = Rows(terrain, pickPower, copied, drawnFrom, goal);

        List<string> lines =
        [
            $"// copied from ({copied.Left}, {copied.Top}) to "
                + $"({copied.Right - 1}, {copied.Bottom - 1}), pickaxe {pickPower}",
            $"// the @ is where the route was drawn from, ({drawnFrom.X}, "
                + $"{drawnFrom.Y}), heading for "
                + (goal is { } site ? $"({site.X}, {site.Y})" : "nowhere"),
            $"// the body had walked to ({body.X}, {body.Y}) by the time this was taken",
            "// the route " + (route is { Steps.Count: > 0 } walked
                ? $"runs {walked.Steps.Count} steps and ends at "
                    + $"({walked.Steps[^1].To.X}, {walked.Steps[^1].To.Y})"
                : "is not drawn, so only the two ends are in the box"),
        ];

        // Said here rather than found later. The harness refuses a marker with nothing
        // under it, and a goal that a job aimed into open air or into rock is ordinary:
        // it is the arrival rule that made it reachable, and a grid has no arrival rule.
        if (goal is { } aim
            && terrain.KindAt(aim.X, aim.Y + 1)
                is not (TileKind.Solid or TileKind.Platform or TileKind.Slab))
        {
            lines.Add("// the goal has no floor under it, so move the G onto ground "
                + "before this will load");
        }

        // A copy that quietly lost its right hand side is worse than no copy, because it
        // reads as ground that ends rather than ground that was not written down.
        if (copied != wanted)
        {
            lines.Add("// CUT: this is larger than a scenario holds, so the right and "
                + "lower edges are missing");
        }

        lines.Add("[");
        foreach (string row in rows)
        {
            lines.Add($"    \"{row}\",");
        }

        lines.Add("]),");

        string path = Path.Combine(Logging.LogDir,
            $"ground-{body.X}-{body.Y}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>Everything worth copying, at its full size.</summary>
    // The route and not just the two ends. What went wrong at these coordinates happened
    // forty columns west of both of them, and a box drawn between body and goal would
    // have missed all of it.
    private static Rectangle Box(Point body, Point? goal, Route? route)
    {
        int left = body.X;
        int right = body.X;
        int top = body.Y;
        int bottom = body.Y;


        foreach (Point at in Everywhere(body, goal, route))
        {
            left = System.Math.Min(left, at.X);
            right = System.Math.Max(right, at.X);
            top = System.Math.Min(top, at.Y);
            bottom = System.Math.Max(bottom, at.Y);
        }

        left -= Margin;
        right += Margin;
        top -= Margin - Hitbox.Height;
        bottom += Margin;

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private static IEnumerable<Point> Everywhere(Point body, Point? goal, Route? route)
    {
        yield return body;
        if (route is { } drawn)
        {
            yield return drawn.From;
        }

        if (goal is { } site)
        {
            yield return site;
        }

        foreach (Step step in route?.Steps ?? [])
        {
            yield return step.To;
            if (step.Puts is { } put)
            {
                yield return put;
            }

            foreach (Point cell in step.Removes)
            {
                yield return cell;
            }
        }
    }

    /// <summary>The character the harness reads for what is in this cell.</summary>
    // Liquid after the solids, because the harness draws water in a cell with no tile in
    // it, which is also how Terraria holds it.
    private static char Glyph(ITerrain terrain, int pickPower, int x, int y) =>
        !terrain.IsKnown(x, y) ? '?'
        : terrain.KindAt(x, y) switch
        {
            TileKind.Platform => '=',
            TileKind.Slab => '_',
            TileKind.Solid => terrain.Diggable(x, y, pickPower) ? '#'
                : terrain.Diggable(x, y, FullPick) ? 'H'
                : 'X',
            _ => terrain.HasLava(x, y) ? 'L'
                : terrain.HasWater(x, y) ? 'w'
                : '.',
        };

    private static void Mark(List<string> rows, Rectangle copied, Point at, char mark)
    {
        if (!copied.Contains(at))
        {
            return;
        }

        char[] row = rows[at.Y - copied.Top].ToCharArray();
        row[at.X - copied.Left] = mark;
        rows[at.Y - copied.Top] = new string(row);
    }
}
#endif
