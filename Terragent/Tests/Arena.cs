#if TESTING
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

namespace Terragent.Tests;

/// <summary>The pathfinding scenarios, run as real tiles by the real follower.</summary>
// The headless suite proves the search draws the right route. Nothing proved the body
// could walk one, and every stall this project has had lived on that side of the line: a
// plan that was correct and a follower that could not carry it out. This is that half.
//
// It drives the agent's own pilot rather than a copy. A copy tests a copy, and the thing
// that fails is the original.
//
// Test scaffolding, so it is compiled only in Debug behind TESTING and never ships.
internal sealed class Arena(IPilot pilot, ITerrain terrain, IJournal journal)
{
    /// <summary>How much unbreakable rock to wrap a scenario in.</summary>
    // Ebonstone, not stone. A stone surround is a wall the agent can simply mine through,
    // and it did: scenarios were solved by tunnelling out of the arena.
    private const int Margin = 3;

    /// <summary>Ticks a body may stand on one footing before it is worth saying so.</summary>
    private const int Frozen = 120;

    private readonly Pen _pen = new();

    private int _index = -1;
    private int _ticks;
    private int _passed;
    private int _failed;
    private int _skipped;

    private Point _start;
    private Point _goal;
    private Destination _to;

    /// <summary>How near it ever got, and the tick it got there.</summary>
    // A scenario that runs out of time three tiles short having closed steadily is a
    // budget problem. One that stopped improving after two seconds is stuck, and the two
    // want opposite fixes.
    private int _closest;
    private int _closestAt;

    /// <summary>Where the body was last tick, and how long it has been there.</summary>
    private Point _sat;
    private int _sitting;
    private int _longest;

    /// <summary>Names a scenario must contain to be run, or empty for all of them.</summary>
    private string _only = string.Empty;

    /// <summary>Whether the pilot has been handed this scenario's route yet.</summary>
    // The route is found on the first tick rather than as the scenario is built, so the
    // search reads the body where the game has actually put it.
    private bool _following;

    public bool Running { get; private set; }

    public void Start(string only)
    {
        _only = only;
        _index = -1;
        _passed = 0;
        _failed = 0;
        _skipped = 0;
        Running = true;
        journal.Note("arena", $"starting {Scenarios.All.Count} scenarios"
            + (only.Length > 0 ? $", only those matching {only}" : string.Empty));
        Next();
    }

    public void Stop(string why)
    {
        _pen.Clear();
        Running = false;
        journal.Note("arena", $"{why}: {_passed} passed, {_failed} failed, "
            + $"{_skipped} skipped");
    }

    /// <summary>One tick of whichever scenario is in progress.</summary>
    public void Tick()
    {
        if (!Running)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        Point at = Hitbox.Footing(player.position, player.height);

        if (_to.Reached(at))
        {
            Score(true, $"reached it in {_ticks / 60.0:0.0}s");
            return;
        }

        if (!_following)
        {
            if (pilot.FindRoute([_to]) is not { } found)
            {
                Score(false, $"no route at all from ({at.X}, {at.Y})");
                return;
            }

            pilot.Follow(_to, found.Route);
            _following = true;
        }

        Watch(at);

        int gap = System.Math.Max(
            System.Math.Abs(at.X - _goal.X), System.Math.Abs(at.Y - _goal.Y));
        if (gap < _closest)
        {
            _closest = gap;
            _closestAt = _ticks;
        }

        if (++_ticks > Scenarios.All[_index].Seconds * 60)
        {
            bool closing = _ticks - _closestAt < 120;
            Score(false, $"{(closing ? "ran out of time" : "stuck")} at "
                + $"({at.X}, {at.Y}), {_closest} tiles short at best "
                + $"({_closestAt / 60.0:0.0}s), longest freeze {_longest / 60.0:0.0}s, "
                + $"{pilot.Progress.ToString().ToLowerInvariant()}");
            return;
        }

        if (pilot.Progress is Progress.Unreachable)
        {
            Score(false, $"no route from ({at.X}, {at.Y}) after {_ticks / 60.0:0.0}s");
            return;
        }

        pilot.Tick();
    }

    /// <summary>How long the body has held one footing, which is what a freeze looks like.</summary>
    private void Watch(Point at)
    {
        if (at != _sat)
        {
            _sat = at;
            _sitting = 0;
            return;
        }

        _longest = System.Math.Max(_longest, ++_sitting);
        if (_sitting == Frozen)
        {
            journal.Note("frozen", $"{Scenarios.All[_index].Name}: ({at.X}, {at.Y}) for "
                + $"{Frozen / 60.0:0.0}s, {pilot.Progress.ToString().ToLowerInvariant()}");
        }
    }

    private void Score(bool passed, string why)
    {
        if (passed)
        {
            _passed++;
        }
        else
        {
            _failed++;
        }

        journal.Note(passed ? "passed" : "FAILED", $"{Scenarios.All[_index].Name}: {why}");
        Next();
    }

    private void Skip(string why)
    {
        _skipped++;
        journal.Note("skipped", $"{Scenarios.All[_index].Name}: {why}");
        Next();
    }

    /// <summary>Build the next scenario the arena can actually walk, or finish.</summary>
    private void Next()
    {
        _pen.Clear();
        pilot.Halt();
        _index++;
        if (_index >= Scenarios.All.Count)
        {
            Stop("finished");
            return;
        }

        Case test = Scenarios.All[_index];

        // Not asked for, which is not a skip: a skip is a scenario this level cannot
        // answer, and those are worth counting.
        if (_only.Length > 0 && !test.Name.Contains(_only,
            System.StringComparison.OrdinalIgnoreCase))
        {
            Next();
            return;
        }

        // Nothing to walk. A scenario whose right answer is no route is the headless
        // harness's business, and skipping it keeps this one about movement.
        if (test.Unreachable)
        {
            Skip("no route expected");
            return;
        }

        // Characters the arena cannot build honestly. 'X' asks for a tile the game refuses
        // to break, which nothing can request directly, and '?' asks for fog, which is the
        // absence of a reading rather than a tile. Built as ordinary stone they would be
        // different scenarios wearing the same name.
        foreach (char cell in (char[])['X', '?'])
        {
            if (System.Array.Exists(test.Rows, row => row.Contains(cell)))
            {
                Skip($"'{cell}' cannot be built; planned only");
                return;
            }
        }

        Build(test);
        Stock(test);

        _ticks = 0;
        _closest = int.MaxValue;
        _closestAt = 0;
        _sitting = 0;
        _longest = 0;
        _following = false;
        _sat = new Point(int.MinValue, int.MinValue);

        Player player = Main.LocalPlayer;

        // Cancel whatever was in flight. A swing already thrown lands a few ticks later,
        // after the scenario has been rebuilt, and breaks a tile of the new one.
        player.controlUseItem = false;
        player.itemAnimation = 0;
        player.itemTime = 0;

        Pen.Place(player, Footing(_start));

        // Exactly on the goal, as the headless suite asks for it, so the two levels are
        // answering one question.
        _to = new Destination(_goal, Within: 0);
        journal.Note("running", $"{test.Name}: ({_start.X}, {_start.Y}) to "
            + $"({_goal.X}, {_goal.Y}), {test.Seconds}s");
    }

    /// <summary>Which pair of columns the body starts in.</summary>
    // The marked column, or its left neighbour when the body will not fit on the right.
    private Point Footing(Point floor)
    {
        foreach (int column in (int[])[floor.X, floor.X - 1])
        {
            Point footing = new(column, floor.Y);
            bool room = terrain.Standable(footing);
            foreach (Point cell in Hitbox.Cells(footing))
            {
                room &= terrain.KindAt(cell.X, cell.Y) is TileKind.Empty;
            }

            if (room)
            {
                return footing;
            }
        }

        return floor;
    }

    /// <summary>Make sure there is stone to pillar with, or none when a case forbids it.</summary>
    private static void Stock(Case test)
    {
        Player player = Main.LocalPlayer;
        for (int slot = 0; slot < player.inventory.Length; slot++)
        {
            Item item = player.inventory[slot];
            if (!item.IsAir && item.type == ItemID.StoneBlock)
            {
                item.TurnToAir();
            }
        }

        if (test.Blocks <= 0)
        {
            return;
        }

        for (int slot = 0; slot < 50; slot++)
        {
            if (!player.inventory[slot].IsAir)
            {
                continue;
            }

            player.inventory[slot].SetDefaults(ItemID.StoneBlock);
            player.inventory[slot].stack = test.Blocks;
            return;
        }
    }

    /// <summary>Lay a scenario out as tiles and show it to the map.</summary>
    private void Build(Case test)
    {
        int wide = test.Rows[0].Length;
        int left = (Main.maxTilesX / 2) - (wide / 2);
        int top = Pen.Top(Margin, Hitbox.Height);

        for (int row = -Margin; row < test.Rows.Length + Margin; row++)
        {
            for (int column = -Margin; column < wide + Margin; column++)
            {
                Surround(test, left + column, top + row, row, column, wide);
            }
        }

        Lay(test, left, top);
    }

    /// <summary>One cell of the rock, headroom and lid that a grid sits inside.</summary>
    private void Surround(Case test, int x, int y, int row, int column, int wide)
    {
        bool inside = row >= 0 && row < test.Rows.Length && column >= 0 && column < wide;
        if (inside)
        {
            return;
        }

        // Headroom above the grid, then a lid over that. A scenario whose start sits in
        // its first row needs the body's three rows of air, and a lid above those stops it
        // wandering off the top of the world, which is the escape the side walls close.
        //
        // Air the map has to know about. An unbuilt cell is unrevealed and unrevealed is
        // fog, which the search will dig through but never fall through, so absent ground
        // has to be shown absent.
        bool over = column >= 0 && column < wide;
        if ((row < 0 && row >= -Hitbox.Height && over) || !test.Border)
        {
            _pen.Reveal(x, y);
            return;
        }

        _pen.Put(x, y, TileID.Ebonstone);
    }

    /// <summary>The grid itself, character by character.</summary>
    private void Lay(Case test, int left, int top)
    {
        for (int row = 0; row < test.Rows.Length; row++)
        {
            for (int column = 0; column < test.Rows[row].Length; column++)
            {
                int x = left + column;
                int y = top + row;
                char cell = test.Rows[row][column];
                Cell(x, y, cell);

                if (cell == '@')
                {
                    _start = new Point(x, y + 1);
                }
                else if (cell == 'G')
                {
                    _goal = new Point(x, y + 1);
                }
            }
        }

        _pen.Cut();
        _pen.Stand();

        // A scenario whose goal is where it starts, which asks for a route to here.
        if (test.Goal == '@')
        {
            _goal = _start;
        }
    }

    /// <summary>One grid character, as tiles.</summary>
    // The same characters the headless grid reads, and they have to mean the same thing
    // here or the two levels are testing different pictures under one name.
    private void Cell(int x, int y, char cell)
    {
        _pen.Put(x, y, cell switch
        {
            '#' or 'd' or '_' or '/' or '\\' => TileID.Stone,
            'H' => TileID.Ebonstone,
            '=' => TileID.Platforms,
            _ => -1,
        });

        if (cell == 'B')
        {
            _pen.WantStation(x, y, TileID.WorkBenches);
        }

        if (cell is '_' or '/' or '\\')
        {
            _pen.WantShape(x, y, half: cell == '_', lowOnLeft: cell == '/');
        }

        if (cell is 'w' or 'L')
        {
            _pen.Flood(x, y, cell == 'L' ? LiquidID.Lava : LiquidID.Water);
        }
    }
}
#endif
