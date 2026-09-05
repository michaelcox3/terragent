#if TESTING
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent;

/// <summary>
/// Runs the pathfinding scenarios as real tiles, in a running world.
/// </summary>
internal sealed class PathingTests
{
    /// <summary>Rows above the surface to build at. High enough to be empty sky.</summary>
    private const int Altitude = 220;

    /// <summary>How much solid rock to wrap a scenario in. See Build.</summary>
    private const int Margin = 3;

    private readonly Pilot _pilot = new();
    private readonly Pen _pen = new();
    private readonly List<Point> _required = [];
    private readonly List<(string Name, bool Walked)> _recent = [];

    private int _index = -1;
    private int _ticks;
    private Point _start;
    private Point _goal;
    private int _passed;
    private int _failed;
    private string _last = "";
    private int _stocked = -1;

    /// <summary>The longest the character stood still on a move, this scenario.</summary>
    private int _stalled;

    /// <summary>Ticks of standing still on a move before it is a failure.</summary>
    private const int StallLimit = 45;

    /// <summary>Block stacks taken off the character for a case that carries none.</summary>
    private readonly List<(int Slot, int ItemID, int Stack)> _stashed = [];
    private Move _did;
    private int _closest = int.MaxValue;
    private int _closestAt;
    private bool _wasMortal;

    public bool Running { get; private set; }

    /// <summary>The scenario in progress, for the panel.</summary>
    public string Current => _index >= 0 && _index < Scenarios.All.Count
        ? Scenarios.All[_index].Name
        : "";

    public int Number => _index + 1;

    public int Total => Scenarios.All.Count;

    public int Passed => _passed;

    public int Failed => _failed;

    /// <summary>Seconds left in the current scenario's budget.</summary>
    public double Remaining => _index >= 0 && _index < Scenarios.All.Count
        ? ((Scenarios.All[_index].Seconds * 60) - _ticks) / 60.0
        : 0;

    /// <summary>Where the current scenario is trying to get to.</summary>
    public Point Goal => _goal;

    /// <summary>The last few verdicts, newest first.</summary>
    public IReadOnlyList<(string Name, bool Walked)> Recent => _recent;

    /// <summary>What the arena is doing, for the panel.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Whether this run was asked for by name.</summary>
    // Comma separated, because the scenarios worth running together rarely share a
    // word: asking for "jump" to get two of them fetched eight.
    private bool Asked(string name)
    {
        foreach (string term in Only.Split(',',
            System.StringSplitOptions.RemoveEmptyEntries
            | System.StringSplitOptions.TrimEntries))
        {
            if (name.Contains(term, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Scenarios whose name contains any of these, comma separated.</summary>
    public string Only { get; set; } = "";

    public void Start()
    {
        // Invulnerable for the duration, and not as a convenience. SetControls
        // is not called for a dead player and the arena is driven from
        // SetControls, so one death stops the suite where it stands: no
        // verdict, no timeout and no exit.
        _wasMortal = !AgentMod.Invulnerable;
        AgentMod.Invulnerable = true;

        Running = true;
        _index = -1;
        _passed = 0;
        _failed = 0;
        _recent.Clear();
        Log.Sample("pathing", "starting", ("cases", Scenarios.All.Count));
        Next();
    }

    public void Stop(string why)
    {
        _pen.Clear();
        Unstock();
        Restore();
        if (_wasMortal)
        {
            AgentMod.Invulnerable = false;
        }

        Running = false;
        Swings.Region = null;
        Status = $"{_passed}/{_passed + _failed} scenarios passed";
        Log.Sample("pathing", why, ("passed", _passed), ("failed", _failed));
    }

    /// <summary>One tick of whatever scenario is in progress.</summary>
    public void Update(Belief belief, Character character, AgentState state,
        double now)
    {
        Player player = character.Player;
        if (!Running)
        {
            return;
        }

        // Belt and braces on the same problem: if something else stops the character
        // dead, say so and move on rather than waiting for a tick that is not coming.
        if (player.dead)
        {
            Score(false, "the character died");
            return;
        }

        if (Scenarios.All[_index].Blocks <= 0)
        {
            Stash(player, remember: false);
        }

        Point at = character.Movement.Footing;
        if (Navigator.Reached(at, _goal, 0))
        {
            // Same rule as the headless harness: mining and building exactly,
            // walking and jumping as permissions.
            const Move Spent = Move.Mine | Move.Build;
            Move wanted = Scenarios.All[_index].Does;
            Move extra = (_did & ~wanted) & (Spent | (~wanted & (Move.Walk | Move.Jump)));
            Move missing = (wanted & ~_did) & Spent;

            // Every block the scenario names has to be gone. More than that is
            // fine, since a route may cut extra to make room, but the named
            // ones are the shape the scenario is about: reaching the goal
            // without them means it found some other way.
            List<Point> left = Standing();
            if (_stalled >= StallLimit)
            {
                Score(false, $"reached it, but stood still for {_stalled / 60.0:0.0}s "
                             + "on a move with nothing to dig");
                return;
            }

            if (extra == Move.None && missing == Move.None && left.Count == 0)
            {
                Score(true, "reached it");
                return;
            }

            if (left.Count > 0)
            {
                Score(false, $"reached it, but left {left.Count} of {_required.Count} "
                             + $"required blocks standing: {Describe(left)}");
                return;
            }

            Score(false, $"reached it, but {Describe(extra, missing)}");
            return;
        }

        // How near it ever got, and when. A scenario that runs out of time
        // three tiles from the goal having closed steadily is a budget
        // problem; one that stopped improving after two seconds is stuck, and
        // the two want different fixes.
        int gap = System.Math.Max(
            System.Math.Max(0, System.Math.Max(at.X - _goal.X,
                _goal.X - (at.X + Body.Width - 1))),
            System.Math.Abs(at.Y - _goal.Y));
        if (gap < _closest)
        {
            _closest = gap;
            _closestAt = _ticks;
        }

        if (++_ticks > Scenarios.All[_index].Seconds * 60)
        {
            bool closing = _ticks - _closestAt < 60 * 2;
            Score(false, $"{(closing ? "ran out of time" : "stuck")} at ({at.X}, {at.Y})"
                         + $" after {Scenarios.All[_index].Seconds}s, {_closest} tiles"
                         + $" short at best, longest freeze {_stalled / 60.0:0.0}s"
                         + $" ({_closestAt / 60.0:0.0}s), {_pilot.Behaviour}");
            return;
        }

        // The real follower, driven exactly as the agent drives it. Anything
        // less and this tests a copy of the thing that is failing.
        if (!_pilot.Advance(belief, character, state, _goal, now, arriveWithinTiles: 0))
        {
            Score(false, $"stopped at ({at.X}, {at.Y}): {_pilot.Behaviour}");
            return;
        }

        // Every decision, attributed to the scenario that made it, so a
        // failure can be read from the start rather than inferred from where
        // it stopped.
        _stalled = System.Math.Max(_stalled, _pilot.Lookout.Motionless);

        string line = $"{at.X},{at.Y} {_pilot.Behaviour}";
        if (line != _last)
        {
            _last = line;
            Log.Sample("trace", _pilot.Behaviour,
                ("case", Scenarios.All[_index].Name),
                ("t", $"{_ticks / 60.0:0.00}"),
                ("at", $"({at.X}, {at.Y})"),
                ("goal", $"({_goal.X}, {_goal.Y})"),
                ("vy", $"{player.velocity.Y:0.00}"),
                ("keys", $"{(player.controlLeft ? "L" : "-")}"
                         + $"{(player.controlRight ? "R" : "-")}"
                         + $"{(player.controlJump ? "J" : "-")}"
                         + $"{(player.controlUseItem ? "U" : "-")}"),
                ("held", player.inventory[player.selectedItem].Name));
        }

        // What it is doing, not just where it got to. Arriving is a weak
        // verdict on its own: a route that mines through a wall it could have
        // walked round, or spends blocks on a ledge it could have jumped,
        // reaches the goal and looks like a pass.
        if (_pilot.Next is { } doing)
        {
            if (doing.Removes.Count > 0)
            {
                _did |= Move.Mine;
            }

            if (doing.Puts is not null)
            {
                _did |= Move.Build;
            }

            if (doing.Kind is StepKind.Jump)
            {
                _did |= Move.Jump;
            }
            else if (doing.Removes.Count == 0 && doing.Puts is null)
            {
                // Walking means moving without paying for it. A step that
                // tunnels the whole way is horizontal too, and counting it as
                // a walk would make the flag mean "is not entirely vertical"
                // rather than "took a free step".
                _did |= Move.Walk;
            }
        }

        state.Required.Clear();
        foreach (Point cell in _required)
        {
            state.Required.Add(cell);
        }

        Status = $"{Scenarios.All[_index].Name}: {_pilot.Behaviour}";
    }

    /// <summary>Required blocks that are still there.</summary>
    private List<Point> Standing()
    {
        List<Point> left = [];
        foreach (Point cell in _required)
        {
            if (AgentPlayer.Local.Seen.KindAt(cell.X, cell.Y) is TileKind.Solid)
            {
                left.Add(cell);
            }
        }

        return left;
    }

    private static string Describe(List<Point> cells)
    {
        List<string> parts = [];
        foreach (Point cell in cells)
        {
            parts.Add($"({cell.X},{cell.Y})");
        }

        return string.Join(" ", parts);
    }

    /// <summary>The difference between what a scenario asked for and what it got.</summary>
    private static string Describe(Move extra, Move missing)
    {
        List<string> parts = [];
        if (extra != Move.None)
        {
            parts.Add($"it {extra} and should not have");
        }

        if (missing != Move.None)
        {
            parts.Add($"it never {missing}");
        }

        return string.Join(", and ", parts);
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

        _recent.Insert(0, (Scenarios.All[_index].Name, passed));
        if (_recent.Count > 6)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }

        Log.Sample("scenario", Scenarios.All[_index].Name,
            ("passed", passed),
            ("why", why),
            ("ticks", _ticks),
            ("did", _did.ToString()),
            ("wanted", Scenarios.All[_index].Does.ToString()),
            ("grid", Diagnose.Draw(AgentPlayer.Local.Seen, AgentPlayer.Local.Acting.Movement.Footing,
                _goal)));
        Next();
    }

    /// <summary>Build the next scenario, or finish.</summary>
    private void Next()
    {
        _pen.Clear();
        _index++;
        if (_index >= Scenarios.All.Count)
        {
            Stop("finished");
            return;
        }

        Case test = Scenarios.All[_index];
        if (Only.Length > 0 && !Asked(test.Name))
        {
            Next();
            return;
        }

        // 'X' is a tile the game refuses to break, which in play is a tree
        // standing on one.
        char[] unbuildable = ['X', '?'];
        foreach (char cell in unbuildable)
        {
            if (!System.Array.Exists(test.Rows, row => row.Contains(cell)))
            {
                continue;
            }

            Log.Sample("scenario", test.Name, ("passed", true),
                ("why", cell == 'X'
                    ? "protected tiles cannot be built; planning half only"
                    : "fog cannot be built; planning half only"),
                ("ticks", 0));
            Next();
            return;
        }

        // A scenario the plan itself refuses has nothing to walk. Those are
        // the headless harness's business, and skipping them keeps this one
        // about movement.
        if (test.Unreachable)
        {
            Log.Sample("scenario", test.Name, ("passed", true),
                ("why", "no route expected; nothing to walk"), ("ticks", 0));
            Next();
            return;
        }

        Build(test);
        Stock(test);
        _did = Move.None;
        _stalled = 0;
        // Cancel whatever the character was in the middle of. The arena takes
        // over from ordinary play, and a swing already in flight lands a few
        // ticks later, after the audit has been reset, arriving as a block
        // broken with no plan behind it.
        Main.LocalPlayer.controlUseItem = false;
        Main.LocalPlayer.itemAnimation = 0;
        Main.LocalPlayer.itemTime = 0;
        Swings.Region = new System.Collections.Generic.HashSet<Point>(_pen.Built);
        Swings.Forget();
        _ticks = 0;
        _closest = int.MaxValue;
        _closestAt = 0;
        _last = "";
        Player player = Main.LocalPlayer;

        // Put the body in the column pair the search would name, middle on the
        // seam.
        Pen.Place(player, Standing(AgentPlayer.Local.Seen, _start));
        _pilot.Forget();
        _pilot.ReconsiderWalls();
    }

    /// <summary>
    /// Make sure there are blocks to pillar with.
    /// </summary>
    private void Stock(Case test)
    {
        Player player = Main.LocalPlayer;
        Restore();

        // A case that carries nothing is testing what happens without stone,
        // and not handing it any is not the same as it having none.
        if (test.Blocks <= 0)
        {
            Unstock();
            Stash(player, remember: true);
            return;
        }

        if (_stocked >= 0)
        {
            player.inventory[_stocked].stack = 999;
            return;
        }

        for (int slot = 0; slot < 50; slot++)
        {
            if (!player.inventory[slot].IsAir)
            {
                continue;
            }

            player.inventory[slot].SetDefaults(ItemID.StoneBlock);
            player.inventory[slot].stack = 999;
            _stocked = slot;
            return;
        }

        Log.Sample("pathing", "no free slot for stone",
            ("note", "pillaring scenarios may fail for want of blocks"));
    }

    /// <summary>
    /// Take every placeable block off the character, so a case that says it carries
    /// none really carries none.
    /// </summary>
    /// <param name="remember">
    /// Whether to hand these back afterwards. True for what the character walked in
    /// with; false for what it picks up mid-case, which is the arena's own stone and
    /// was never its to keep.
    /// </param>
    private void Stash(Player player, bool remember)
    {
        for (int slot = 0; slot < player.inventory.Length; slot++)
        {
            Item item = player.inventory[slot];
            if (item.IsAir || !item.consumable
                || item.createTile < Terraria.ID.TileID.Dirt
                || !Main.tileSolid[item.createTile]
                || Main.tileFrameImportant[item.createTile])
            {
                continue;
            }

            if (remember)
            {
                _stashed.Add((slot, item.type, item.stack));
            }

            item.TurnToAir();
        }
    }

    /// <summary>Give back whatever was taken away for the last case.</summary>
    private void Restore()
    {
        Player player = Main.LocalPlayer;
        foreach ((int slot, int type, int stack) in _stashed)
        {
            player.inventory[slot].SetDefaults(type);
            player.inventory[slot].stack = stack;
        }

        _stashed.Clear();
    }

    /// <summary>Give the borrowed slot back.</summary>
    private void Unstock()
    {
        if (_stocked < 0)
        {
            return;
        }

        Main.LocalPlayer.inventory[_stocked].TurnToAir();
        _stocked = -1;
    }

    /// <summary>
    /// Which two columns the body starts in: the marked one, and whichever neighbour
    /// has room for it.
    /// </summary>
    private static Point Standing(Belief belief, Point floor)
    {
        foreach (int column in new[] { floor.X, floor.X - 1 })
        {
            Point footing = new(column, floor.Y);
            bool room = belief.Standable(footing);
            foreach (Point cell in Body.Cells(footing))
            {
                room &= belief.Passable(cell.X, cell.Y);
            }

            if (room)
            {
                return footing;
            }
        }

        return floor;
    }

    /// <summary>Lay a scenario out as tiles, and reveal it so the belief can see it.</summary>
    private void Build(Case test)
    {
        _required.Clear();
        // Column parity used to matter here: a shaft chose which way to widen
        // from Body.SideAt, so an odd origin cut the opposite side to the one
        // the scenario had drawn and a grid had to be pinned even.
        int left = (Main.maxTilesX / 2) - (test.Rows[0].Length / 2);
        // Clamped below the space layer, where gravity changes. See Altitude.
        int ceiling = (int)(Main.worldSurface * 0.35) + Margin + Body.Height + 4;
        int top = System.Math.Max(ceiling, (int)Main.worldSurface - Altitude);

        // Walls and a floor, but open sky above: exactly what the headless
        // harness reads outside a grid.
        for (int row = -Margin; row < test.Rows.Length + Margin; row++)
        {
            for (int column = -Margin; column < test.Rows[0].Length + Margin; column++)
            {
                bool inside = row >= 0 && row < test.Rows.Length
                    && column >= 0 && column < test.Rows[0].Length;

                // Headroom above the grid, then a lid over that. A scenario
                // whose '@' sits in its first row needs the body's three rows
                // of air or the character cannot stand where it starts; a lid
                // above those stops it wandering off the top of the world,
                // which is the same escape the side walls close.
                bool overGrid = column >= 0 && column < test.Rows[0].Length;
                if (row < 0 && row >= -Body.Height && overGrid)
                {
                    // Air, and the belief has to know it is air. An unbuilt
                    // cell is unrevealed, and fog is something the search will
                    // dig through but never fly through.
                    _pen.Reveal(left + column, top + row);
                    continue;
                }

                if (inside)
                {
                    continue;
                }

                if (!test.Border)
                {
                    // Nothing there, and the belief told so. Unbuilt is unrevealed and
                    // unrevealed is fog, which the search will dig through but never
                    // fall through, so an absent floor has to be shown absent.
                    _pen.Reveal(left + column, top + row);
                    continue;
                }

                // Ebonstone, not stone. A stone border is a wall the agent can
                // simply mine through, and scenarios then get "solved" by
                // tunnelling into the surround.
                _pen.Put(left + column, top + row, TileID.Ebonstone);
            }
        }

        LayGrid(test, left, top);
    }

    /// <summary>One grid character, as tiles.</summary>
    private void Lay(int x, int y, char cell)
    {
        _pen.Put(x, y, cell switch
        {
            // 'X' asks for a tile the game refuses to break, which nothing can request
            // directly. It builds as stone and those scenarios are planned, not walked.
            '#' or 'd' or 'X' or '_' or '/' or '\\' => TileID.Stone,
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

    private void LayGrid(Case test, int left, int top)
    {
        for (int row = 0; row < test.Rows.Length; row++)
        {
            for (int column = 0; column < test.Rows[row].Length; column++)
            {
                int x = left + column;
                int y = top + row;
                char cell = test.Rows[row][column];
                Lay(x, y, cell);

                if (cell == 'd')
                {
                    _required.Add(new Point(x, y));
                }

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

        if (test.Goal == '@')
        {
            _goal = _start;
        }
    }

    /// <summary>
    /// A slope with the rock underfoot rather than overhead, facing a given way.
    /// </summary>
    /// <param name="lowOnLeft">
    /// Whether the low corner is the left one, the slope drawn '/', which rises to the
    /// right. Both directions are needed: a hillside has ramps facing both ways, and
    /// building every one the same way makes a grid that reads as a hill and behaves as
    /// a staircase of identical steps.
    /// </param>
    /// <summary>
    /// Cut the half blocks and slopes, after everything else is in place.
    /// </summary>
            }
#endif
