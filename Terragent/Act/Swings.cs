using Terraria;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Act;

/// <summary>
/// Checks that every block the agent swings at is one it said it would break.
/// </summary>
internal static class Swings
{
    private static readonly HashSet<Point> _told = [];

    /// <summary>
    /// Where the audit applies, or null for everywhere.
    /// </summary>
    public static ISet<Point>? Region { get; set; }

    /// <summary>
    /// Cells highlighted over the last few frames, not just this one.
    /// </summary>
    private static readonly Queue<HashSet<Point>> _lately = new();

    private const int Memory = 12;

    private static Point _target = new(int.MinValue, int.MinValue);
    private static bool _wasSolid;

    /// <summary>
    /// What was highlighted on the frame the cursor was last seen over a solid tile.
    /// </summary>
    private static HashSet<Point> _before = [];

    /// <summary>
    /// What the route (not the executor) had planned, on the frame the swing landed.
    /// </summary>
    private static HashSet<Point> _plannedBefore = [];

    /// <summary>Tiles broken this run, in order.</summary>
    public static List<Point> Broken { get; } = [];

    public static void Forget()
    {
        _told.Clear();
        _lately.Clear();
        _before = [];
        _plannedBefore = [];
        Broken.Clear();
        _target = new Point(int.MinValue, int.MinValue);
        _wasSolid = false;
    }

    /// <summary>
    /// One tick's worth of checking, called after the executor has acted and the state
    /// has been read back so the highlight and the cursor describe the same tick.
    /// </summary>
    public static void Audit(Belief belief, Character character, AgentState state)
    {
        if (state.Aimed is not { } target)
        {
            return;
        }

        if (Region is not null && !Region.Contains(target))
        {
            return;
        }

        // Not "is it Solid": a half block or a floor slope is rock that reads as a
        // platform, and the narrower question silently stops counting blocks broken in
        // smoothed ground.
        bool solid = belief.KindAt(target.X, target.Y) is not TileKind.Empty;

        // A tile that was solid under the cursor and is not any more came out.
        _lately.Enqueue(Highlighted(state));
        while (_lately.Count > Memory)
        {
            _lately.Dequeue();
        }

        if (target == _target && _wasSolid && !solid)
        {
            // "planned" asks whether the agent ever intended this block, which is about
            // the search being right rather than the executor.
            Broken.Add(target);
            Log.Sample("mined", $"({target.X}, {target.Y})",
                ("planned", Recently(target)),
                ("drawn", _before.Contains(target)),
                ("inPlan", _plannedBefore.Contains(target)),
                ("digs", Describe(state.Digs)),
                ("planning", Describe(state.Planned)),
                ("count", Broken.Count));
        }

        _target = target;
        _wasSolid = solid;
        if (solid)
        {
            _before = Highlighted(state);
            _plannedBefore = [.. state.Planned];
        }

        if (!state.UseDown || !solid || !state.Swinging)
        {
            return;
        }

        if (Recently(target) || !_told.Add(target))
        {
            return;
        }

        Log.Sample("unplanned swing", $"({target.X}, {target.Y})",
            ("held", state.Holding),
            ("behaviour", state.Behaviour),
            ("digs", Describe(state.Digs)),
            ("planned", Describe(state.Planned)),
            ("grid", Diagnose.Draw(belief, character.Movement.Footing,
                target)));
    }

    /// <summary>Whether this cell was highlighted at any point in living memory.</summary>
    private static bool Recently(Point cell)
    {
        foreach (HashSet<Point> frame in _lately)
        {
            if (frame.Contains(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Everything the agent is claiming it will break.</summary>
    private static HashSet<Point> Highlighted(AgentState state)
    {
        HashSet<Point> cells = [];
        foreach (Point cell in state.Digs)
        {
            cells.Add(cell);
        }

        foreach (Point cell in state.Planned)
        {
            cells.Add(cell);
        }

        return cells;
    }

    private static string Describe(IReadOnlyList<Point> cells)
    {
        List<string> parts = [];
        foreach (Point cell in cells)
        {
            parts.Add($"({cell.X},{cell.Y})");
        }

        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }
}
