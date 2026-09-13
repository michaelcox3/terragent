using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

namespace Terragent.Controls;

/// <summary>The pilot.</summary>
// Search once, then walk it. The route is drawn when there is none and thrown away when
// it runs out, and nothing else redraws it: a follower that replans on a hunch is a body
// that stands still while the search runs again every frame.
internal sealed class Pilot(
    INavigator navigator, ITerrain terrain, IBody body, IHand hand, IInventory bag,
    IClock clock, IJournal journal) : IPilot
{
    private readonly INavigator _navigator = navigator;
    private readonly ITerrain _terrain = terrain;
    private readonly IBody _body = body;
    private readonly IHand _hand = hand;
    private readonly IInventory _bag = bag;

    private Destination? _destination;
    private Route? _route;

    /// <summary>The cells of the route in hand, kept in step with it.</summary>
    private readonly HashSet<Point> _toFill = [];
    private readonly HashSet<Point> _toBreak = [];

    /// <summary>Which step of the route is being walked.</summary>
    private int _step;

    /// <summary>The step being pressed, and how far it had left when the clock restarted.</summary>
    private Point _pushing;

    private int _remaining = int.MaxValue;

    private double _since;

    /// <summary>The last footing stood on, which is where a jump in flight began.</summary>
    // A jump has to keep the heading it started with, and by the time it is in the air the
    // body is no longer on the footing the route named.
    private Point _takeoff;

    /// <summary>How long a step may make no ground before it is not going to.</summary>
    private const double PatienceSeconds = 2.0;

    public Destination? Destination => _destination;

    public Progress Progress { get; private set; }

    public int Walked => _step;

    public Route? Route => _route;

    public IReadOnlySet<Point> CellsToFill => _toFill;

    public IReadOnlySet<Point> CellsToBreak => _toBreak;

    /// <summary>Take down what the route means to do to the ground.</summary>
    private void Read(Route? route)
    {
        _toFill.Clear();
        _toBreak.Clear();
        foreach (Step step in route?.Steps ?? [])
        {
            if (step.Puts is { } put)
            {
                _toFill.Add(put);
            }

            foreach (Point cell in step.Removes)
            {
                _toBreak.Add(cell);
            }
        }
    }

    public void Tick()
    {
        if (_destination is not { } destination)
        {
            Progress = Progress.Idle;
            return;
        }

        Point at = _body.Footing;

        // Standing there, not passing through. A footing the body is only flying over is
        // not somewhere it has arrived: a pillar jumped, the footing a row up read as
        // within reach of the ore, the job stopped travelling and started working, the
        // block was never laid, and the body fell back out of reach and bounced there.
        // Advanced already refuses to move a route on in mid air, for the same reason.
        //
        // Or floating, since a swimming body is never grounded and would otherwise never
        // arrive anywhere under water.
        if ((_body.Grounded || _body.Submerged) && destination.Reached(at))
        {
            Progress = Progress.Arrived;
            return;
        }

        if (_route is null)
        {
            Search(at, destination);
        }

        Route? route = _route;
        if (route is null)
        {
            Progress = Progress.Unreachable;
            return;
        }

        Advanced(at, route);
        Progress = Progress.Going;

        if (_step >= route.Steps.Count)
        {
            // Walked the whole plan without arriving, so the body came down somewhere the
            // plan did not expect. Drawn again next tick from where it actually is.
            Forget();
            return;
        }

        Watch(route.Steps[_step], at);
        Press(route.Steps[_step], at);
    }

    public RouteMatch? FindRoute(IReadOnlyList<Destination> destinations)
    {
        // Not while falling. A footing is the column pair and the row of floor under it, and
        // in mid air there is no floor: the footing is whatever the body happens to be
        // passing, true for one frame. A route drawn from one is a route from somewhere the
        // body never was, and its first step is impossible the moment it lands. A run
        // planned a step up from a footing it was falling through, landed a row lower, and
        // pressed that step for the rest of its life.
        //
        // The one place anything asks the search, so the rule is said once. Swimming counts
        // as settled, since a body in water is never grounded and would plan nothing at all.
        if (!_body.Grounded && !_body.Submerged)
        {
            return null;
        }

        Point from = _body.Footing;
        RouteMatch? reached = _navigator.FindRoute(from, destinations, Ability());

        if (reached is not null)
        {
            return reached;
        }

        // What the search was working with, on the tick it came back with nothing. The
        // caller can say where it was trying to go; only this end knows what the body was
        // carrying, and a pickaxe too weak or nothing that lights are the two answers that
        // look identical from outside.
        journal.Change("unwalkable", $"nothing of {destinations.Count} from "
            + $"({from.X}, {from.Y}): pickaxe {_bag.PickPower}, blocks {_bag.Blocks}, "
            + $"lights {_bag.Carrying(Lights.Dark)}, "
            + $"wet lights {_bag.Carrying(Lights.Wet)}");
        return null;
    }

    /// <summary>Take this destination and the route already found to it.</summary>
    // The route is given rather than looked for. Choosing among offers searched for one
    // already, on this tick and from this footing, and searching again to walk the way it
    // just came back with is the same answer paid for twice.
    public void Follow(Destination site, Route route)
    {
        _destination = site;
        _route = route;
        Read(route);
        _step = 0;
        Progress = Progress.Idle;
        Plan(site, route);
    }

    /// <summary>The whole route, as moves and coordinates.</summary>
    // Handed over rather than searched for here, so without this a route chosen by whoever
    // is holding the job never appears anywhere: a failure could be read only backwards
    // from the one step the body happened to be pressing when it stopped.
    private void Plan(Destination site, Route route)
    {
        List<string> steps = [];
        foreach (Step step in route.Steps)
        {
            steps.Add($"{step.Kind.ToString().ToLowerInvariant()}({step.To.X},{step.To.Y})"
                + (step.Puts is { } put ? $"+({put.X},{put.Y})" : string.Empty)
                + (step.Removes.Count > 0 ? $"-{step.Removes.Count}" : string.Empty));
        }

        journal.Change("plan", $"to ({site.Site.X}, {site.Site.Y}): "
            + string.Join(" ", steps));
    }

    /// <summary>Stand still and let go of wherever it was going.</summary>
    public void Halt()
    {
        _destination = null;
        Forget();
        Progress = Progress.Idle;
    }

    private void Search(Point at, Destination destination)
    {
        _step = 0;

        // Through the same door the foreman uses, so what may be planned and from where is
        // decided in one place rather than two that drift.
        _route = FindRoute([destination])?.Route;
        Read(_route);
        if (_route is { } drawn)
        {
            Plan(destination, drawn);
        }


        journal.Change("route", _route is { } route
            ? $"({at.X}, {at.Y}) to ({destination.Site.X}, {destination.Site.Y}) "
                + $"in {route.Steps.Count} steps, {route.Examined} footings looked at"
            : $"no way from ({at.X}, {at.Y}) to ({destination.Site.X}, "
                + $"{destination.Site.Y}) with a pickaxe of {_bag.PickPower}");
    }

    /// <summary>What this body can do, as the search needs to hear it.</summary>
    // Read at the moment a search starts, so a route is priced for the pickaxe actually
    // carried. Nothing that lights makes water infinitely dear rather than merely slow,
    // because a torch goes out down there and the map stops revealing.
    private Ability Ability() =>
        new(
            Costs.Priced(
                _body.RunSpeed,
                _bag.PickPower,
                _bag.UseTime(_bag.Pickaxe),
                _bag.Carrying(Lights.Wet) > 0,
                _bag.Carrying(Lights.Dark) > 0),
            _bag.PickPower,
            _body.Arc(),
            _bag.Blocks);

    /// <summary>Move past every step the body has already got to.</summary>
    // A loop, because one frame can finish several steps after a long fall. Nothing
    // advances in mid air: the step being flown towards stays current for the whole jump.
    private void Advanced(Point at, Route route)
    {
        if (!_body.Grounded)
        {
            return;
        }

        _takeoff = at;
        while (_step < route.Steps.Count && Done(route.Steps[_step], at))
        {
            _step++;
        }
    }

    /// <summary>Whether the body has got where a step was taking it.</summary>
    // Inside the footing rather than rounded onto it, which Hitbox.Within says why.
    //
    // A jump is flown rather than stepped, so it comes down where momentum leaves it and
    // landing above the row counts. The columns do not: the arc of the step after it was
    // drawn from this pair, and a body a few pixels short of them is under a different
    // ceiling. Forgiven a column, a run jumped one short, was waved on, and spent the rest
    // of itself pressing a jump into the rock overhead.
    private bool Done(Step step, Point at) =>
        Hitbox.Within(step.To, _body.Frame)
        && (step.Kind is StepKind.Jump ? at.Y <= step.To.Y : at.Y == step.To.Y)
        && Laid(step);

    /// <summary>Whether a step that lays a block has actually laid it.</summary>
    // Standing on the footing does not prove it. A footing holds if either of the body's
    // two columns has ground under it, so a pillar can be stood on its neighbour with the
    // block still missing, and the step would count as done with a hole left in the tower.
    private bool Laid(Step step) =>
        step.Puts is not { } put || _terrain.Holds(put.X, put.Y, trustFog: false);

    /// <summary>Say so when a step has stopped making ground, and nothing more.</summary>
    // A report and never a decision. It used to strike the edge out of every later search,
    // which read as certainty it had not earned: what it actually knows is that one step
    // made no whole tile of progress for two seconds, and waiting out a swing, being shoved
    // by a slime or needing one sideways nudge before a drop all look the same from here. A
    // run watched here struck out a fall and made the same drop a second later.
    //
    // Keyed on what is left of the step rather than on the exact footing, so a body
    // wobbling a column either side of its takeoff does not restart the clock every tick
    // and go unreported for ever. The clock restarts on each report, so a step that is
    // truly going nowhere says so once every couple of seconds rather than every tick.
    private void Watch(Step step, Point at)
    {
        int left = System.Math.Abs(step.To.X - at.X) + System.Math.Abs(step.To.Y - at.Y);
        if (step.To != _pushing || left < _remaining)
        {
            _pushing = step.To;
            _remaining = left;
            _since = clock.Now;
            return;
        }

        if (clock.Now - _since <= PatienceSeconds)
        {
            return;
        }

        _since = clock.Now;

        // What the step still wants done, which is the difference between a plan the body
        // cannot carry out and one it has not finished carrying out.
        List<string> cells = [];
        foreach (Point cell in step.Removes)
        {
            cells.Add($"({cell.X}, {cell.Y}) {_terrain.KindAt(cell.X, cell.Y)}");
        }

        journal.Note("stalled", $"({at.X}, {at.Y}) to ({step.To.X}, {step.To.Y}) by "
            + $"{step.Kind.ToString().ToLowerInvariant()} has made no ground in "
            + $"{PatienceSeconds:0} seconds, grounded {_body.Grounded}, "
            + $"frame {_body.Frame.X},{_body.Frame.Y},{_body.Frame.Bottom}, "
            + $"puts {(step.Puts is { } put ? $"({put.X}, {put.Y}) {_terrain.KindAt(put.X, put.Y)}" : "nothing")}, "
            + $"breaks {(cells.Count == 0 ? "nothing" : string.Join(" ", cells))}");
    }

    private void Press(Step step, Point at)
    {
        // Clear the way first, whatever the move is. Mining is what any step may have to
        // do before it can be made, not a kind of step, and the tiles are the ones the
        // search booked rather than any this works out for itself.
        if (Blocking(step) is { } tile)
        {
            Dig(tile, at);
            return;
        }

        // Only while the block is still missing. Laying it is half the step and standing
        // on it is the other half, and returning here did the first and never the second:
        // a pillar finished itself by falling onto its own block, a bridge lays one beside
        // the body and needs the body to walk across, so bridges never completed. They
        // were struck out for making no ground, the search went round by rising a row, and
        // the run climbed a staircase a tile at a time instead of crossing the gap.
        if (step.Kind is StepKind.Place && step.Puts is { } put
            && _terrain.KindAt(put.X, put.Y) is not TileKind.Solid)
        {
            Build(put, at);
            return;
        }

        if (step.Kind is StepKind.Jump)
        {
            // Down beside the footing rather than on it. Nothing steers in the air, so the
            // correction is a walk across once there is ground under the feet again.
            if (_body.Grounded && at.Y <= step.To.Y)
            {
                journal.Change("sidling", $"onto ({step.To.X}, {step.To.Y}) from "
                    + $"({at.X}, {at.Y}), left edge {_body.Frame.Left}");
                _body.Walk(Hitbox.Toward(step.To, _body.Frame));
                return;
            }

            journal.Change("jumping", $"({at.X}, {at.Y}) to ({step.To.X}, {step.To.Y})");
            _body.Walk(Across(step, at));
            _body.Leap(_body.HoldFor(
                step.To.X - _takeoff.X, _takeoff.Y - step.To.Y));
            return;
        }

        if (step.Kind is StepKind.Fall)
        {
            // Down and settled, wherever that turned out to be. A fall that overshoots
            // leaves every step after it pointing at ground the body is no longer above.
            if (_body.Grounded && at.Y >= step.To.Y)
            {
                Forget();
                return;
            }

            // Held over the column rather than pushed toward it. A drop that does not
            // change the footing gives a sign of zero and presses nothing at all, which
            // is a body standing on the lip of its own shaft waiting to fall.
            _body.Align(step.To);
            return;
        }

        _body.Walk(Hitbox.Toward(step.To, _body.Frame));
    }


    /// <summary>Which way to hold during a jump, decided from where it began.</summary>
    // A jump is one action, so it gets one heading. Input that flips as the body crosses
    // the target column makes it fight its own momentum.
    //
    // A one column hop rises first and steps across at the top, which is what the search's
    // arc assumes; holding the heading from the first frame puts the body under the ledge,
    // one row up and falling.
    //
    // Across once the lowest cell is above the landing surface, and not before. The face of
    // the ledge fills its column for every row below its top, so pressing while any part of
    // the body is still beside that face only grinds into it: a run rose four rows into the
    // side of a step it was trying to reach and fell back, over and over.
    private int Across(Step step, Point at)
    {
        int heading = System.Math.Sign(step.To.X - _takeoff.X);
        bool sidestep = System.Math.Abs(step.To.X - _takeoff.X) <= 1;

        return Hitbox.Within(step.To, _body.Frame) ? 0
            : sidestep && at.Y > step.To.Y ? 0
            : heading;
    }

    /// <summary>The next tile the plan says is in the way, or null when the way is clear.</summary>
    // Anything the route booked and the world has not yet turned to air. Not Solid alone:
    // half blocks and floor slopes read as their own kinds, and a descent through smoothed
    // ground looked clear while the body stood on the floor it had planned to break.
    private Point? Blocking(Step step)
    {
        foreach (Point cell in step.Removes)
        {
            // The cell this step is filling is not an obstruction once it is filled. A step
            // that books its own target, which happens when a plant stands where the block
            // goes, otherwise reads its own work as something in the way: the list of cells
            // to break is fixed when the route is drawn and never edited, so the moment the
            // block lands the cell holds a tile again and looks exactly like the plant did.
            // A run laid a block and mined it, four times a second, until it was killed.
            //
            // Clutter is the difference. A plant is one of Terraria's cut tiles and a laid
            // block is not, so what must be cleared first and what was just put there stop
            // answering alike.
            if (step.Puts == cell && !_terrain.Clutter(cell.X, cell.Y))
            {
                continue;
            }

            // Anything still standing there, which is not the same as anything solid. A
            // plant is walked through and will still refuse a block, so a booked one has to
            // be swung at until it goes rather than skipped for not being in the way.
            if (_terrain.TypeAt(cell.X, cell.Y) != Terrain.Empty)
            {
                return cell;
            }
        }

        return null;
    }

    private void Dig(Point tile, Point at)
    {
        _bag.Hold(_bag.Pickaxe);
        if (!_hand.InReach(tile.X, tile.Y))
        {
            // Out of reach means the body has drifted off the plan. Draw it again from
            // here rather than swinging at nothing.
            Forget();
            return;
        }

        // Held in the column pair the plan is cutting. Standing off to one side of a
        // shaft as it opens rests the body on the lip instead of dropping it through.
        _body.Align(at);
        _hand.Aim(tile.X, tile.Y);
        _hand.Use();
        journal.Change("mining", $"({tile.X}, {tile.Y}) from ({at.X}, {at.Y}), "
            + $"{_terrain.KindAt(tile.X, tile.Y)}, "
            + $"seen {_terrain.IsKnown(tile.X, tile.Y)}, "
            + $"withheld {_hand.Blocked}");
    }

    /// <summary>Lay the block this step stands on, which the caller has found missing.</summary>
    // Whether it is there yet is the caller's question, because the answer decides between
    // two different things to do rather than between doing this and doing nothing.
    private void Build(Point put, Point at)
    {
        // Wait the last swing out before spending a jump on this. Terraria will not start a
        // use while one is running, and the mining that clears the way for a pillar runs on
        // for about as long as the whole arc: the body rose, held the button through the
        // few frames it was clear, and came down with the block still unplaced. Four arcs
        // went that way for every block laid, and the one that worked was the swing landing
        // inside the window by luck. Taking the tick is what ends the animation, since
        // nothing presses use while this returns.
        if (_hand.Busy)
        {
            return;
        }

        if (_bag.Block == 0)
        {
            Forget();
            return;
        }

        _bag.Hold(_bag.Block);

        // Aimed before the rise, not after it. Terraria refuses a swing aimed where the
        // cursor was not on the previous frame, so a cursor moved only once the body is
        // clear wastes the first tick of the window it waited for: the body drops back, the
        // jump is spent for nothing and it jumps again. A run watched here jumped two or
        // three times for every block it laid.
        _hand.Aim(put.X, put.Y);

        // A pillar puts its block in a cell the body is filling, and the game will not
        // place a tile inside the character, so it has to rise clear of the cell first. A
        // bridge lays into the floor row beside the body and needs no jump.
        //
        // Whether the body is in the way, not whether the target row is above the footing
        // row. The footing rises with the body, so mid jump a pillar started reading as a
        // bridge: the rise was skipped, the swing went out from inside the cell, and
        // Terraria refused it without a word. Four whole arcs went that way for every block
        // laid.
        float top = put.Y * 16f;
        Rectangle cell = new(put.X * 16, put.Y * 16, 16, 16);
        if (_body.Frame.Intersects(cell))
        {
            // One line per jump rather than per tick, so the count is readable. A block
            // that takes three arcs to lay says so, and a deduplicated line cannot.
            if (_body.Grounded)
            {
                // What is overhead, because a rise that never gets there looks the same
                // whatever is stopping it. A run jumped and fell back ten times a second
                // for a minute and the line could not say whether the ceiling was rock or
                // fog. Which cells the step meant to break is on the plan line already.
                List<string> over = [];
                for (int side = 0; side < Hitbox.Width; side++)
                {
                    for (int up = 1; up <= Hitbox.Height; up++)
                    {
                        Point cap = new(at.X + side, at.Y - Hitbox.Height - up);
                        over.Add($"({cap.X},{cap.Y}) {_terrain.KindAt(cap.X, cap.Y)}");
                    }
                }

                journal.Note("rising", $"for ({put.X}, {put.Y}) from ({at.X}, {at.Y}), "
                    + $"bottom {_body.Frame.Bottom} needs {top:0}, "
                    + $"overhead {string.Join(" ", over)}");
            }

            _body.Align(at);

            // One row, which is all a pillar ever rises: high enough to get the feet out of
            // the cell the block goes in, and no higher, since every extra frame of climb is
            // time spent in the air not laying it.
            _body.Leap(_body.HoldFor(0, 1));
            return;
        }

        if (!_hand.InPlaceReach(put.X, put.Y))
        {
            Forget();
            return;
        }

        // Every block this lays, once each. A route that spends blocks where a dig would do
        // reaches the goal and reads as a pass, so the only way to see it is to say so.
        _hand.Use();

        // Every attempt, not every distinct one. Whether a block took one swing or five is
        // the whole question about a pillar, and a line that only speaks when its text
        // changes cannot answer it.
        // What is in the cell, and what is around it. Terraria refuses a placement without
        // a word for reasons the search does not model, and every one of them looks the
        // same from here: aimed right, not withheld, nothing happening. A run stood laying
        // wood into the same cell four times a second and the log could not say what was
        // in the way.
        journal.Note("laying", $"({put.X}, {put.Y}) from ({at.X}, {at.Y}), "
            + $"bottom {_body.Frame.Bottom}, holding {Names.Item(_hand.Held)}, "
            + $"withheld {_hand.Blocked}, still swinging {_hand.Busy}, "
            + $"game aims at ({_hand.Targeted.X}, {_hand.Targeted.Y}), "
            + $"cell {_terrain.KindAt(put.X, put.Y)} type {_terrain.TypeAt(put.X, put.Y)}, "
            + $"above {_terrain.TypeAt(put.X, put.Y - 1)}, "
            + $"below {_terrain.TypeAt(put.X, put.Y + 1)}");
    }

    private void Forget()
    {
        _route = null;
        _toFill.Clear();
        _toBreak.Clear();
        _step = 0;
    }
}
