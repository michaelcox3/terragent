using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent.Follow;

/// <summary>
/// Keeps a route: searches for one, decides when it is stale, and hands its steps to
/// Stepping one tick at a time.
/// </summary>
// Executing a step is Stepping's job and watching for stalls is Lookout's; this only
// decides when to search again and what to do with the answer.
internal sealed class Pilot
{
    private int _searches;

    private double _searchMs;

    private double _counted;


    /// <summary>How far off the route the character may drift before replanning.</summary>
    private const int Tolerance = 3;

    /// <summary>
    /// A question already asked and answered with "nowhere", so it is not asked again.
    /// </summary>
    private readonly HashSet<(Point From, Point To)> _hopeless = [];

    private Point _hopelessAt = new(int.MinValue, int.MinValue);

    /// <summary>The route and which step of it the body is on, or null when there is none.</summary>
    // Nullable on purpose: a search that found nothing and a route walked to its end
    // are different situations, and only one of them means ask again.
    private Progress? _progress;

    /// <summary>Where the body last stood, which is where a jump in flight began.</summary>
    private Point _takeoff;
    private Point[] _goals = [];

    /// <summary>Where the route in hand ends, or null when there is no route.</summary>
    // With several goals the search picks; this is how the panel marks the job actually
    // being worked rather than the one guessed at before the search ran.
    public Point? Heading => _progress?.Route.Destination;

    /// <summary>Which goal the last route was drawn to, as the search reported it.</summary>
    // Kept across ticks: a route outlives the search that made it.
    public Point? Settled { get; private set; }

    /// <summary>The goal a failed search is remembered against.</summary>
    // One stands for the set; what is remembered is that standing here, wanting these,
    // came to nothing.
    private static Point Named(IReadOnlyList<Point> goals) =>
        goals.Count > 0 ? goals[0] : default;
    private double _planned;

    public Step? Next => _progress?.Current;

    /// <summary>What the follower is doing, for the panel.</summary>
    public string Behaviour { get; private set; } = "";

    /// <summary>Set when no route to the goal could be found at all.</summary>
    public bool Unreachable { get; private set; }

    public void Forget()
    {
        _progress = null;
    }

    /// <summary>Tiles found to be unbreakable, so callers can report the reason.</summary>
    public int Immovable => _stepping.Immovable.Count;

    /// <summary>
    /// Forget what would not break, called when the pickaxe improves because the wall
    /// that stopped the agent is exactly what the new pickaxe is for.
    /// </summary>
    public void ReconsiderWalls()
    {
        _hopeless.Clear();
        _stepping.ReconsiderWalls();
    }

    /// <summary>
    /// Move one tick toward <paramref name="goal"/>, returning false when there is no
    /// route.
    /// </summary>
    /// <param name="arrived">
    /// What counts as having got there; when given it replaces the radius entirely, so
    /// "arrived" and "can act on the target" are one statement rather than two.
    /// </param>
    public bool Advance(Belief belief, Character character, AgentState state,
        Point goal, double now, int arriveWithinTiles = 1, Func<Point, bool>? arrived = null,
        int maxNodes = 20000) =>
        Advance(belief, character, state, [goal], now, arriveWithinTiles, arrived,
            maxNodes);

    /// <summary>
    /// Follow a route to whichever of several goals the search finds cheapest.
    /// </summary>
    public bool Advance(Belief belief, Character character, AgentState state,
        IReadOnlyList<Point> goals, double now, int arriveWithinTiles = 1,
        Func<Point, bool>? arrived = null, int maxNodes = 20000,
        IReadOnlyList<int>? within = null)
    {
        Point at = character.Movement.Footing;
        Unreachable = false;

        // Watched, not acted on. The goal is the settled candidate, since the first of a
        // list reorders as sites are mined. Either it or the first step alternating is
        // the signature of every oscillation so far.
        Lookout.Flips(Settled ?? Named(goals), _progress?.Current, Behaviour, belief, at, now);

        // Never replan in mid-air. A* asked from sky plans a fall to wherever the ground
        // next is, which from the top of a jump is back where the jump started.
        _progress?.Reached(at, belief.Standable(at));

        // Airborne means nothing is holding the character up, not "vertical velocity is
        // not zero", which is also true of walking down a slope.
        bool airborne = !character.Movement.Grounded;
        if (!airborne)
        {
            _takeoff = at;
        }

        if (airborne && _progress?.Current is { } flying)
        {
            return Perform(belief, character, state, flying, at, now);
        }

        // Landed a column or two off the tile the jump was aimed at. Landing is not
        // arriving: grounded on the lip, the mid-air rule stops protecting the route, and
        // a replan from there says pillar, which means standing still and falling back
        // in. Walked in here rather than in Execute, which cannot tell "landed, walk in"
        // from "not started, leap" for a level jump. Finishing bounds this by column, so
        // a jump that came down short of its row still replans.
        if (!airborne && _progress?.Current is { } landing && Progress.Finishing(landing, at))
        {
            character.Movement.Align(landing.To);
            Behaviour = $"landed short of ({landing.To.X}, {landing.To.Y}); walking in";
            return true;
        }

        if (Stale(at, goals, now, arriveWithinTiles, arrived, within))
        {
            if (airborne)
            {
                Behaviour = "in the air";
                return true;
            }

            // Standing somewhere else is the one thing that can change the answer, so
            // the record lasts only while the character has not moved.
            if (at != _hopelessAt)
            {
                _hopeless.Clear();
                _hopelessAt = at;
            }

            if (_hopeless.Contains((at, Named(goals))))
            {
                Unreachable = true;
                Behaviour = $"no route to ({Named(goals).X}, {Named(goals).Y})";
                return false;
            }

            _planned = now;
            // Null is "searched and found nothing". Empty is "already inside the goal
            // radius".
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            Route? found = new Navigator(belief).FindRoute(
                Priced(character), character.Inventory.PickPower,
                character.Movement.Jump, at, goals,
                arriveWithinTiles, maxNodes,
                refused: _stepping.Refused, blocks: character.Inventory.SpendableBlocks,
                arrived: arrived, immovable: _stepping.Immovable, within: within);

            // A search runs on the game thread, so every one is logged: a threshold hid
            // the cheap ones exactly when the question was how many there were.
            _searches++;
            double spent = clock.Elapsed.TotalMilliseconds;
            _searchMs += spent;
            {
                Log.Sample("search", $"{spent:0.0}ms",
                    ("from", $"({at.X},{at.Y})"),
                    // The chosen candidate, not the first: choosing is the search's job.
                    ("candidates", goals.Count),
                    ("chose", found?.Settled is { } won
                        ? $"({won.X},{won.Y})"
                        : "none"),
                    ("first", $"({Named(goals).X},{Named(goals).Y})"),
                    ("footingsExamined", found?.Examined ?? 0),
                    ("budget", maxNodes),
                    ("jumpHeight", character.Movement.JumpHeight),
                    ("found", found is not null),
                    ("steps", found?.Steps.Count ?? 0));
            }

            if (now - _counted > 1.0)
            {
                Log.Sample("searches", $"{_searches} in {now - _counted:0.0}s",
                    ("ms", $"{_searchMs:0.0}"));
                _searches = 0;
                _searchMs = 0.0;
                _counted = now;
            }
            if (found is null)
            {
                _hopeless.Add((at, Named(goals)));
                Unreachable = true;
                Behaviour = $"no route to ({Named(goals).X}, {Named(goals).Y})";

                // A failed search says nothing about why on its own, so say what the
                // goal and the floor are.
                Point named = Named(goals);
                Log.Sample("noroute", $"({at.X},{at.Y}) -> ({named.X},{named.Y})",
                    ("goals", goals.Count),
                    ("goalKnown", belief.IsKnown(named.X, named.Y)),
                    ("goalKind", belief.KindAt(named.X, named.Y).ToString()),
                    ("column", Diagnose.Column(belief, at, named)),
                    ("standingOn", belief.KindAt(at.X, at.Y).ToString()));
                return false;
            }

            Settled = found.Settled;

            // The goal the route was drawn to, not the offer it was chosen from. Keeping
            // the whole list made every narrowing or widening of the board read as the
            // goal having moved, which is the pendulum: walk east to one candidate, have
            // the offer change, walk west to another, repeat.
            _goals = [Settled ?? Named(goals)];
            _progress = new Progress(found);

            // The route as planned, so what the overlay drew and what the follower did
            // can both be checked against what the search actually decided.
            if (_progress!.Route.Steps.Count > 0)
            {
                System.Text.StringBuilder sketch = new();
                for (int i = 0; i < _progress!.Route.Steps.Count && i < 16; i++)
                {
                    Step leg = _progress!.Route.Steps[i];
                    sketch.Append(leg.Kind.ToString()[0]).Append('(')
                        .Append(leg.To.X).Append(',').Append(leg.To.Y).Append(')');
                    if (leg.Removes.Count > 0)
                    {
                        sketch.Append('-').Append(leg.Removes.Count);
                    }

                    if (leg.Puts is not null)
                    {
                        sketch.Append("+1");
                    }

                    sketch.Append(' ');
                }

                Point aimed = _progress?.Route.Destination ?? Named(goals);
                Log.Sample("route",
                    $"{_progress!.Route.Steps.Count} steps to ({aimed.X}, {aimed.Y})",
                    ("from", $"({at.X}, {at.Y})"),
                    ("steps", sketch.ToString().TrimEnd()));
            }
        }

        if (_progress?.Current is null)
        {
            // The steps ran out, which is not the same as having got there; where the
            // two disagree the agent stands still reporting arrival, so say so. Not
            // mid-fall, when the footing names the air being passed through.
            Point aim = Named(goals);
            if (character.Movement.Velocity.Y == 0f && !Progress.Near(at, aim, Tolerance))
            {
                Log.Sample("short", $"({at.X},{at.Y}) is not ({aim.X},{aim.Y})",
                    ("steps", _progress!.Route.Steps.Count),
                    ("walked", _progress!.Walked),
                    ("age", $"{now - _planned:0.00}s"),
                    ("grid", Diagnose.Draw(belief, at, aim)));

                // A route that keeps ending short from the same footing is a first step
                // the body cannot make, such as a jump that comes down where it took
                // off. The stall clock never sees it, since it restarts on every
                // landing, so strike the step out here and the search routes round it.
                if (_progress!.Route.Steps.Count > 0)
                {
                    Point first = _progress!.Route.Steps[0].To;
                    if (at != _shortFrom || first != _shortTo)
                    {
                        _shortFrom = at;
                        _shortTo = first;
                        _shorts = 0;
                    }

                    if (++_shorts >= ShortLimit)
                    {
                        _stepping.Refuse((at, first));
                        _shorts = 0;
                        Behaviour = $"({at.X}, {at.Y}) to ({first.X}, {first.Y}) keeps "
                                    + "ending short; going round";
                        Log.Sample("refused", Behaviour,
                            ("kind", _progress!.Route.Steps[0].Kind.ToString()),
                            ("under", belief.KindAt(at.X, at.Y + 1).ToString()),
                            ("ahead", belief.KindAt(first.X, first.Y).ToString()),
                            ("aheadHead", belief.KindAt(first.X, first.Y - 1).ToString()),
                            ("grid", Diagnose.Draw(belief, at, first)));
                        Forget();
                        return true;
                    }
                }
            }

            Behaviour = character.Movement.Velocity.Y != 0f ? "landing" : "arrived";
            return true;
        }

        Step step = _progress!.Current!.Value;
        state.Waypoints.Clear();
        state.Planned.Clear();
        state.Placing.Clear();
        foreach (Step ahead in _progress!.Remaining)
        {
            state.Waypoints.Add(ahead.To);

            // Only what is still there: the route records the cells it costed at plan
            // time and the follower breaks them one at a time, so the whole list would
            // keep showing blocks that came out seconds ago.
            foreach (Point cell in ahead.Removes)
            {
                // The same question Obstruction asks, and it must stay the same one, or
                // the overlay stops drawing exactly the blocks about to be swung at.
                if (belief.KindAt(cell.X, cell.Y) is not TileKind.Empty)
                {
                    state.Planned.Add(cell);
                }
            }

            if (ahead.Puts is { } put
                && belief.KindAt(put.X, put.Y) is not TileKind.Solid)
            {
                state.Placing.Add(put);
            }

            // Nothing beyond what the route recorded: cells chosen from the live
            // position go stale the moment the body moves.
        }

        bool acting = Perform(belief, character, state, step, at, now);

        // After Execute, so the keys recorded are the ones it just pressed.
        Lookout.Stall(belief, step, at, Stepping.Obstruction(belief, step) is not null,
            character.Movement.Center, character.Movement.Velocity, character.Movement.Pressed);
        return acting;
    }

    /// <summary>Whether two goal sets are the same request.</summary>
    /// <param name="goalMayMoveTiles">
    /// How far a goal may move before the route to it is worth redrawing.
    /// </param>
    // The radius the caller asked to arrive within, and no more: a route may go stale
    // by at most the distance the caller says is close enough, or the agent stands on
    // route and a tile outside a short sword's reach. A goal that has gone or moved
    // makes the route stale; one that has been added does not, since counting goals
    // made every wood drop merging into a stack a full search.
    private static bool Shifted(IReadOnlyList<Point> goals, IReadOnlyList<Point> planned,
        int goalMayMoveTiles)
    {
        foreach (Point was in planned)
        {
            bool still = false;
            foreach (Point goal in goals)
            {
                // Candidate.Beyond, which the board asks of the same pair of points.
                // Navigator.Reached takes the same number and measures a box on purpose:
                // it asks whether a tool can touch the tile, and reach is rectangular.
                if (!Candidate.Beyond(was, goal, goalMayMoveTiles))
                {
                    still = true;
                    break;
                }
            }

            if (!still)
            {
                return true;
            }
        }

        return false;
    }

    private bool Stale(Point at, IReadOnlyList<Point> goals, double now, int goalMayMoveTiles,
        Func<Point, bool>? arrived, IReadOnlyList<int>? within)
    {
        // No route at all is this case and not a special one: `_progress!` here threw
        // on the first tick of every objective, and tModLoader swallowed it.
        if (_progress is null || _progress.Route.Steps.Count == 0)
        {
            return true;
        }

        // A route planned after the time being asked about is a caller holding a stopped
        // clock. Ageing cannot expire it (it only gets younger), so it would be followed
        // until the run ended.
        if (now < _planned)
        {
            return true;
        }

        // The steps have run out, which is not the same as having got there: the drift
        // allowance is measured against where the goal was when the route was drawn, so
        // a target that wandered inside it would leave the agent finished, short, and
        // with no reason to look again.
        if (_progress?.Current is null)
        {
            return !(arrived is null
                ? Navigator.Reached(at, goals, goalMayMoveTiles, within, out _)
                : arrived(at));
        }

        {
            // The step in hand has to be a move from where the character is standing.
            Step step = _progress!.Current!.Value;
            int across = System.Math.Abs(step.To.X - at.X);
            int up = step.To.Y - at.Y;
            bool fromHere = step.Kind switch
            {
                StepKind.Walk => across <= 1 && up is 0 or -1,
                // A pillar rises in its own column; a bridge steps sideways onto the
                // block it just laid. Both are placements.
                StepKind.Place => (across == 0 && up == -1) || (across == 1 && up == 0),
                StepKind.Jump => across <= 1 && up < 0,
                StepKind.Fall => up > 0 && across <= 1,
                _ => true,
            };

            // Drifted off the route: following a plan the character is no longer on is
            // acting on a position it left. A fall is judged by its column alone;
            // measured against its landing, a long drop with cells to dig first was
            // stale on every tick of the digging.
            if (!fromHere
                || (step.Kind is not StepKind.Fall && !Progress.Near(at, step.To, Tolerance)))
            {
                return true;
            }
        }

        // The only discretionary reason left: the goal this route was drawn to has moved
        // out from under it. Not "the offer changed", which flips as the board narrows
        // and widens around a target standing still, and not age, which threw away
        // perfectly walkable routes on a three second clock.
        return Shifted(goals, _goals, goalMayMoveTiles);
    }

    /// <summary>Says when the body stops moving, and when the goal or first step keeps flipping.</summary>
    internal Lookout Lookout { get; } = new();

    /// <summary>Executes the step in hand, and remembers what it proved would not work.</summary>
    private readonly Stepping _stepping = new();

    /// <summary>What this character's moves are worth right now, from its three units.</summary>
    private static Costs Priced(Character character) => Costs.Priced(
        character.Movement.RunSpeed,
        character.Inventory.PickPower,
        character.Hand.PickaxeUseTime,
        character.Inventory.Carrying(ItemID.Glowstick) > 0,
        character.Inventory.Carrying(ItemID.Torch) > 0);

    /// <summary>Execute the step in hand, and act on what that says about the route.</summary>
    private bool Perform(Belief belief, Character character, AgentState state, Step step,
        Point at, double now)
    {
        Outcome outcome = _stepping.Execute(belief, character, state, _progress!, step, at,
            _takeoff, now);
        Behaviour = _stepping.Behaviour;
        if (outcome is Outcome.Replan)
        {
            Forget();
        }

        return outcome is not Outcome.Stuck;
    }

    /// <summary>Routes ending short from one footing before the first step is refused.</summary>
    private const int ShortLimit = 4;

    private Point _shortFrom;
    private Point _shortTo;
    private int _shorts;

    /// <summary>
    /// Get within reach of a target, by whatever means the search says is cheapest.
    /// </summary>
    public void Approach(Belief belief, Character character, AgentState state,
        Point target, double now) =>
        Approach(belief, character, state, [target], now);

    /// <summary>Get within reach of whichever of these the ground makes cheapest.</summary>
    /// <param name="within">
    /// How close counts, per target. A drop must be touched and a stone block only
    /// reached with a pickaxe, so one number for a mixed list is wrong for all but one.
    /// </param>
    /// <param name="arrived">
    /// What counts as having got to any of them, when a radius is not the answer.
    /// Overrides <paramref name="within"/>.
    /// </param>
    public void Approach(Belief belief, Character character, AgentState state,
        IReadOnlyList<Point> targets, double now,
        IReadOnlyList<int>? within = null, Func<Point, bool>? arrived = null,
        int maxNodes = 20000)
    {
        Point target = targets[0];
        // Aim at somewhere it can swing from rather than the tile itself:
        // standing inside a tree is not a prerequisite for chopping it.
        if (!Advance(belief, character, state, targets, now,
                arriveWithinTiles: character.Hand.ReachTiles,
                arrived: arrived ?? (within is null ? node => character.Hand.Usable(node, targets) : null),
                maxNodes: maxNodes, within: within))
        {
            state.Behaviour = Behaviour;
            state.Stuck |= Unreachable;
            return;
        }

        state.Behaviour = Behaviour;

        // "Arrived" is the route's opinion and "in reach" is the game's: the route ends
        // on a standable tile, which is not necessarily one the target can be swung at.
        if (Behaviour != "arrived" || character.Hand.InReach(target.X, target.Y))
        {
            return;
        }

        // No block breaking here: what is in the way is the route's to choose, not the
        // live position's.
        Point at = character.Movement.Footing;
        Forget();
        character.Movement.Walk(System.Math.Sign(target.X - at.X));
        state.Behaviour = $"closing the last step to ({target.X}, {target.Y})";
        Log.Sample("stoppedshort", state.Behaviour,
            ("at", $"({at.X}, {at.Y})"),
            ("target", $"({target.X}, {target.Y})"),
            ("held", character.Hand.HeldName));
    }
}
