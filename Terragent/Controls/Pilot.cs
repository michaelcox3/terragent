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

    public void Tick()
    {
        if (_destination is not { } destination)
        {
            Progress = Progress.Idle;
            return;
        }

        Point at = _body.Footing;
        if (destination.Reached(at))
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
        _step = 0;
        Progress = Progress.Idle;
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
        // The list form, which hands back a partial route as well as an arriving one. The
        // single form is asked whether there is a way there and answers nothing when there
        // is not; a follower wants the way as far as it goes, walks it, and asks again from
        // further along. That is how sixty tiles of tunnel get planned twenty at a time.
        _route = _navigator.FindRoute(at, [destination], Ability())?.Route;

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
    // A jump is flown rather than stepped: the body comes down where momentum leaves it,
    // so landing at or above the row and within a column counts as done. Short of the row
    // is a failed jump, and that one gets the route drawn again.
    private static bool Done(Step step, Point at) =>
        at == step.To
        || (step.Kind is StepKind.Jump && at.Y <= step.To.Y
            && System.Math.Abs(at.X - step.To.X) <= 1);

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

        if (step.Kind is StepKind.Place && step.Puts is { } put)
        {
            Build(put, at);
            return;
        }

        if (step.Kind is StepKind.Jump)
        {
            _body.Walk(Across(step, at));
            _body.Leap(step.To.Y * 16f);
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

        _body.Walk(System.Math.Sign(step.To.X - at.X));
    }

    /// <summary>Which way to hold during a jump, decided from where it began.</summary>
    // A jump is one action, so it gets one heading. Input that flips as the body crosses
    // the target column makes it fight its own momentum.
    //
    // A one column hop rises first and steps across at the apex, which is what the search's
    // arc assumes; holding the heading from the first frame puts the body under the ledge,
    // one row up and falling. Across once the head is level with the landing surface,
    // three rows before the feet, since waiting for the feet to clear the lip leaves no
    // time to build sideways speed.
    private int Across(Step step, Point at)
    {
        int heading = System.Math.Sign(step.To.X - _takeoff.X);
        bool sidestep = System.Math.Abs(step.To.X - _takeoff.X) <= 1;

        return at.X == step.To.X ? 0
            : sidestep && at.Y - (World.Hitbox.Height - 1) > step.To.Y ? 0
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
            if (_terrain.KindAt(cell.X, cell.Y) is not TileKind.Empty)
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
    }

    private void Build(Point put, Point at)
    {
        // Finished by the tile existing rather than by a swing being thrown. Terraria
        // refuses a placement in silence, and the follower cannot tell that from a swing
        // still in flight.
        if (_terrain.KindAt(put.X, put.Y) is TileKind.Solid)
        {
            return;
        }

        if (_bag.Block == 0)
        {
            Forget();
            return;
        }

        _bag.Hold(_bag.Block);

        // A pillar puts its block in a cell the body is filling, and the game will not
        // place a tile inside the character, so it has to rise clear of the cell first.
        // A bridge lays into the floor row beside the body, where the feet already are,
        // and needs no jump: the difference falls out of where the feet are in pixels.
        // A row above the floor is a pillar; the floor row itself is a bridge, and the
        // pixel test alone cannot tell them apart when the feet sit exactly on the line.
        float top = put.Y * 16f;
        if (put.Y < at.Y && _body.Frame.Bottom > top)
        {
            _body.Align(at);
            _body.Leap(top);
            return;
        }

        if (!_hand.InPlaceReach(put.X, put.Y))
        {
            Forget();
            return;
        }

        _hand.Aim(put.X, put.Y);
        _hand.Use();
    }

    private void Forget()
    {
        _route = null;
        _step = 0;
    }
}
