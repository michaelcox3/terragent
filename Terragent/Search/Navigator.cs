using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Search;

/// <summary>One edge of the search graph: the step it makes, and what it costs.</summary>
// Carries the Step whole, so the frontier, the trail back and the rebuild share one
// shape.
internal readonly record struct Edge(Step Step, float Cost)
{
    /// <summary>The footing this move lands on.</summary>
    public Point Next => Step.To;
}

/// <summary>
/// A* over the footings the player has seen, with digging priced into every move.
/// </summary>
// An instance holding the terrain it searches: the terrain never changes for the life
// of a search, so it is not threaded through every helper as an argument.
internal sealed class Navigator(ITerrain belief)
{
    private readonly ITerrain _belief = belief;

    // Scratch, refilled per move rather than allocated per move. Clear runs about sixty
    // times for every footing expanded, and a fresh list at each of those was seven
    // kilobytes of garbage per node: a search allocated fifty seven megabytes and spent
    // forty microseconds a node, most of it collecting. One search owns one Navigator
    // and expands one footing at a time, so these are working room, not state: nothing
    // is read from them across a call.
    private readonly List<Point> _cut = [];
    private readonly List<Point> _sweep = [];

    /// <summary>Footings already judged while expanding the footing in hand.</summary>
    // The columns under a jump arc are asked about once per column the arc reaches, so
    // the near ones are asked about again and again. Emptied at the top of every
    // expansion, so it never answers for a footing other than the one being expanded.
    private readonly Dictionary<Point, bool> _fits = [];

    /// <summary>The two directions a body walks, jumps and bridges in.</summary>
    private static readonly int[] Sideways = [-1, 1];

    /// <summary>Sideways and straight down, which is how a fall begins.</summary>
    private static readonly int[] Downward = [-1, 0, 1];

    /// <summary>Longest route the rebuild will walk back before giving up.</summary>
    private const int MaxRoute = 8192;

    /// <summary>The furthest drop the planner will plan through.</summary>
    private const int FallLimit = 12;

    /// <summary>
    /// A route from one footing to another, or null if none was found.
    /// </summary>
    /// <param name="from">The footing the character is standing on.</param>
    /// <param name="to">
    /// The tile to reach; a footing reaches it when the body covers its column (see
    /// <see cref="Reached"/>).
    /// </param>
    /// <param name="arriveWithinTiles">
    /// How close counts as arrived: usually close enough to swing at the tile.
    /// </param>
    /// <param name="maxNodes">
    /// How many footings the search may expand before giving up.
    /// </param>
    /// <param name="pickPower">The best pickaxe carried.</param>
    /// <param name="blocks">Placeable blocks carried. Zero forbids pillaring.</param>
    public Route? FindRoute(Costs costs, int pickPower, Leap leap,
        Point from, Point to, int arriveWithinTiles = 0, int maxNodes = 20000,
        ISet<(Point From, Point To)>? refused = null, int blocks = 0,
        Func<Point, bool>? arrived = null,
        ISet<Point>? immovable = null) =>
        FindRoute(costs, pickPower, leap, from, [to], arriveWithinTiles, maxNodes,
            refused, blocks, arrived, immovable);

    /// <summary>
    /// The cheapest route to whichever of several goals turns out to be cheapest.
    /// </summary>
    // One search over all goals, not one per goal: straight-line distance picks the ore
    // behind a wall over the ore down an open shaft.
    /// <param name="leap">What a jump from a standstill can reach, in rows and in columns at each landing height.</param>
    public Route? FindRoute(Costs costs, int pickPower, Leap leap,
        Point from, IReadOnlyList<Point> goals, int arriveWithinTiles = 0, int maxNodes = 20000,
        ISet<(Point From, Point To)>? refused = null, int blocks = 0,
        Func<Point, bool>? arrived = null, ISet<Point>? immovable = null,
        IReadOnlyList<int>? within = null)
    {
        Point? settled = null;
        PriorityQueue<Point, float> frontier = new();
        Dictionary<Point, (Point From, Step Step)> cameFrom = [];
        Dictionary<Point, float> best = new() { [from] = 0f };

        // Blocks laid along the best path to each footing, so a route never plans more
        // placements than are carried.
        Dictionary<Point, int> placed = new() { [from] = 0 };

        frontier.Enqueue(from, Estimate(from, goals, costs.WalkCost, arriveWithinTiles));
        int expanded = 0;

        while (frontier.Count > 0 && expanded++ < maxNodes)
        {
            Point current = frontier.Dequeue();
            Point which = default;
            bool here = arrived is null
                ? Reached(current, goals, arriveWithinTiles, within, out which)
                : arrived(current);
            if (here)
            {
                // A predicate names no goal, so the nearest to where it stopped stands in.
                settled = arrived is null ? which : Closest(current, goals);
                return new Route(Rebuild(cameFrom, from, current), settled, expanded);
            }

            foreach (Edge move in Moves(current, costs, pickPower, blocks, leap))
            {
                Point next = move.Next;

                // An edge the follower has proved it cannot execute: the move does not
                // exist, rather than costing more.
                if (refused is not null && refused.Contains((current, next)))
                {
                    continue;
                }

                // A tile the follower gave up swinging at. Without this the same route
                // is planned and thrown away every tick. Kept out of Diggable because it
                // is one character's experience, not a fact about the world.
                if (immovable is not null && move.Step.Breaks is not null
                    && Blocked(move.Step.Breaks, immovable))
                {
                    continue;
                }

                // Counted along the path rather than as a search dimension: a footing
                // keeps the count of whichever path reached it cheapest, which can shut
                // a dearer path out of a pillar it could afford. A worse route, not an
                // unwalkable one.
                int laid = placed[current] + (move.Step.Puts is null ? 0 : 1);
                if (move.Step.Puts is not null && laid > blocks)
                {
                    continue;
                }

                float candidate = best[current] + move.Cost;
                if (best.TryGetValue(next, out float known) && known <= candidate)
                {
                    continue;
                }

                best[next] = candidate;
                placed[next] = laid;
                cameFrom[next] = (current, move.Step);
                frontier.Enqueue(next, candidate + Estimate(next, goals, costs.WalkCost, arriveWithinTiles));
            }
        }

        // Null is "searched and found nothing". An empty route is "already inside the
        // goal radius, so there is nothing to walk", which Rebuild returns above.
        return null;
    }

    /// <summary>
    /// Whether standing at <paramref name="at"/> counts as having reached a tile.
    /// </summary>
    // A box, not the circle Pilot.Shifted measures: Terraria's reach is rectangular,
    // with tileRangeX and tileRangeY separate. Do not change either to match the other.
    public static bool Reached(Point at, Point goal, int radius) =>
        Gap(at, goal) <= radius && Math.Abs(at.Y - goal.Y) <= radius;

    /// <summary>How many columns short of the goal the body is, zero when over it.</summary>
    private static int Gap(Point at, Point goal) =>
        Math.Max(0, Math.Max(at.X - goal.X, goal.X - (at.X + Body.Width - 1)));

    /// <summary>
    /// How much dearer than a walk the heuristic assumes the remaining tiles are.
    /// </summary>
    // Underground the true remaining cost is nearly nine times distance times a walk,
    // and charging the walk alone leaves A* spreading out instead of heading for the
    // goal. A weighted search may return a route up to this much dearer than the
    // cheapest; the weight stays well under what it could be because the aim is to
    // stop the frontier ballooning, not to squeeze out the last node.
    private const float Focus = 2.0f;

    /// <summary>Ticks to reach the goal if nothing were in the way.</summary>
    private static float Estimate(Point at, Point goal, float walk, int radius) =>
        Math.Max(0, Math.Max(Gap(at, goal), Math.Abs(at.Y - goal.Y)) - radius)
        * walk * Focus;

    /// <summary>Whichever of these is nearest, for want of one the search named.</summary>
    private static Point Closest(Point at, IReadOnlyList<Point> goals)
    {
        Point best = at;
        int nearest = int.MaxValue;
        foreach (Point goal in goals)
        {
            int gap = Math.Abs(goal.X - at.X) + Math.Abs(goal.Y - at.Y);
            if (gap < nearest)
            {
                nearest = gap;
                best = goal;
            }
        }

        return best;
    }

    /// <summary>The nearest goal's estimate, which is the only one that may be used.</summary>
    // Anything larger overestimates the distance to the goal that is actually closest.
    // The radius is subtracted, so the largest of a mixed list keeps this a lower bound.
    private static float Estimate(Point at, IReadOnlyList<Point> goals, float walk,
        int radius)
    {
        float best = float.MaxValue;
        foreach (Point goal in goals)
        {
            best = Math.Min(best, Estimate(at, goal, walk, radius));
        }

        return goals.Count == 0 ? 0f : best;
    }

    public static bool Reached(Point at, IReadOnlyList<Point> goals, int radius) =>
        Reached(at, goals, radius, out _);

    /// <summary>Which of these was reached, so the caller need not work it out.</summary>
    // The search knows which goal stopped it. Guessing afterwards by straight line
    // picks wrong among candidates chosen because straight lines do not settle it.
    public static bool Reached(Point at, IReadOnlyList<Point> goals, int radius,
        out Point which) =>
        Reached(at, goals, radius, null, out which);

    /// <summary>
    /// The same, where each goal says for itself how close counts.
    /// </summary>
    // One radius for a mixed list is wrong for all but one of them: a drop has to be
    // touched, and a stone block only reached with a pickaxe.
    public static bool Reached(Point at, IReadOnlyList<Point> goals, int radius,
        IReadOnlyList<int>? within, out Point which)
    {
        for (int n = 0; n < goals.Count; n++)
        {
            if (Reached(at, goals[n], within is null ? radius : within[n]))
            {
                which = goals[n];
                return true;
            }
        }

        which = default;
        return false;
    }

    /// <summary>
    /// Every move out of a footing: how it travels, what it lands on, what it breaks.
    /// </summary>
    private IEnumerable<Edge> Moves(Point at, Costs costs,
        int pickPower, int blocks, Leap leap)
    {
        bool blind = float.IsPositiveInfinity(costs.FogCost);

        // Inside is asked against this footing, so an answer kept from the last one
        // would be an answer to a different question.
        _fits.Clear();

        foreach (int dx in Sideways)
        {
            // Walk on the level, or step up one row, which the game does for free.
            for (int dy = 0; dy >= -1; dy--)
            {
                Point next = new(at.X + dx, at.Y + dy);
                if (!_belief.Standable(next))
                {
                    continue;
                }

                // The game only lifts the body over a block. A platform or a work bench
                // is walked through from the side, so there is nothing to step onto and
                // the way up is a jump. Offered anyway, the follower waits on a climb
                // that never happens.
                if (dy < 0 && !Climbs(next))
                {
                    continue;
                }

                // Rise in the own column pair, then move across: the other order sweeps
                // the ledge tile itself and charges for breaking what is stepped onto.
                _sweep.Clear();
                _sweep.Add(new Point(at.X, next.Y));
                _sweep.Add(next);
                if (Clear(at, _sweep, pickPower, blind, _cut, out float doubt)
                    && Rests(at, next, _cut))
                {
                    List<Point> cut = _cut;
                    float wet = Soak(next, cut, costs);
                    if (float.IsPositiveInfinity(wet))
                    {
                        continue;
                    }

                    yield return new Edge(
                        new Step(next, StepKind.Walk, cut.Count > 0 ? cut.ToArray() : null),
                        ((costs.WalkCost * (dy == 0 ? 1f : 1.5f)) + (costs.MineCost * cut.Count * doubt))
                        * wet);
                }
            }

            // Jump up to a ledge, or across a gap. Jumps never mine, so an arc that does
            // not fit is not a move.
            //
            // From water, a hop at most: the game caps a wet body's jump at about three
            // rows, and two is the safe understatement. A bank footing with one column
            // over the pond counts as wet and still hops a two-row ledge.
            int climb = Wet(at) ? System.Math.Min(leap.Height, WetJumpRows) : leap.Height;
            for (int up = 1; up <= climb; up++)
            {
                if (!Open(at, new Point(at.X, at.Y - up)))
                {
                    break;
                }

                if (up < 2)
                {
                    continue;
                }

                // Out from the apex a column at a time, then down. Only the column just
                // added is asked about: the ones behind it were cleared on the way out,
                // and asking about the whole corridor again on every column is what made
                // one footing cost thirty five microseconds.
                for (int across = 1; across <= LeapColumns; across++)
                {
                    if (!Open(at, new Point(at.X + (dx * across), at.Y - up)))
                    {
                        break;
                    }

                    // The first thing under the far end is the landing; anything below
                    // it is a Fall from there.
                    for (int drop = 0; drop <= up; drop++)
                    {
                        Point next = new(at.X + (dx * across), at.Y - up + drop);
                        if (!_belief.Settles(next))
                        {
                            continue;
                        }

                        // Height and reach trade off. A landing out of reach ends the
                        // jump, not the search for a lower one: it is landed on anyway.
                        if (across > leap.Reach(up - drop))
                        {
                            break;
                        }

                        // Every row on the way down, not just the landing row: a block
                        // in the middle of the descent is a block in the arc. The
                        // corridor itself is not asked about again; it was cleared on
                        // the way out and it sits on a row none of this touches.
                        bool fits = true;
                        for (int row = at.Y - up + 1; row <= next.Y && fits; row++)
                        {
                            fits = Open(at, new Point(next.X, row));
                        }

                        // The columns in between, down to the lower of takeoff and
                        // landing: the body falls from the apex, so anything standing up
                        // between the two ends is landed on instead. Not the far column,
                        // whose ledge is meant to be solid.
                        int under = System.Math.Max(at.Y, next.Y);
                        for (int back = 1; back < across && fits; back++)
                        {
                            for (int row = at.Y - up + 1; row <= under && fits; row++)
                            {
                                fits = Open(at, new Point(at.X + (dx * back), row));
                            }
                        }

                        if (fits)
                        {
                            float wet = Soak(next, null, costs);
                            if (float.IsPositiveInfinity(wet))
                            {
                                break;
                            }

                            yield return new Edge(
                                new Step(next, StepKind.Jump),
                                costs.WalkCost * (up + across) * 1.5f * wet);
                        }

                        break;
                    }
                }
            }
        }

        // Go down: off a ledge, or through the floor. One move, and which of
        // the two it is depends only on whether the sweep happens to be empty.
        foreach (int dx in Downward)
        {
            Point column = new(at.X + dx, at.Y);
            List<Point> cut = _cut;
            cut.Clear();
            float doubt = 1f;

            // Stepping sideways off the edge first, if this is a drop rather
            // than a dig.
            if (dx != 0)
            {
                _sweep.Clear();
                _sweep.Add(column);
                if (!Clear(at, _sweep, pickPower, blind, cut, out doubt))
                {
                    continue;
                }
            }

            for (int down = 1; down <= FallLimit; down++)
            {
                // The row newly entered on the way down. Anything that holds the
                // character up is in the way, a platform included: it stops a fall
                // however freely it is walked through sideways.
                bool sealed_ = false;
                for (int side = 0; side < Body.Width; side++)
                {
                    int x = column.X + side;
                    int row = at.Y + down - 1;
                    if (!_belief.Holds(x, row, trustFog: true))
                    {
                        continue;
                    }

                    if (!_belief.Diggable(x, row, pickPower))
                    {
                        sealed_ = true;
                        break;
                    }

                    cut.Add(new Point(x, row));
                    doubt = Math.Max(doubt, Uncertainty(x, row));
                }

                if (sealed_)
                {
                    break;
                }

                // Fog counts as ground here. The pessimistic reading: unseen
                // rock will stop the fall, and if it turns out to be a cavern
                // the character keeps going and the follower replans from
                // where it lands.
                Point next = new(column.X, at.Y + down);
                if (!_belief.Standable(next, trustFog: true))
                {
                    continue;
                }

                float wet = Soak(next, cut, costs);
                if (float.IsPositiveInfinity(wet))
                {
                    break;
                }

                yield return new Edge(
                    new Step(next, StepKind.Fall, cut.Count > 0 ? cut.ToArray() : null),
                    ((costs.WalkCost * 0.6f * down) + (costs.MineCost * cut.Count * doubt))
                    * wet);
                break;
            }
        }

        // Bridge out: place a block beside the floor and step onto it. The only
        // sideways move that needs nothing under it, and the answer to a gap wider
        // than a jump: each block hangs off the last.
        if (blocks > 0)
        {
            foreach (int dx in Sideways)
            {
                Point put = new(at.X + dx, at.Y);
                Point next = put;

                // Anchored on the near side, the lip or the block laid last: Terraria
                // silently refuses a placement with nothing to hold on to, and the
                // follower cannot tell that from one that has not landed yet. Standing
                // on nothing means standing on this bridge, which the search never lays
                // into the belief, so from the second block on the anchor is one that
                // does not exist yet.
                bool onOurOwnBridge = !_belief.Standable(at);
                _sweep.Clear();
                _sweep.Add(next);
                if (!_belief.Buildable(put.X, put.Y)
                    || !(onOurOwnBridge
                         || _belief.Holds(put.X - dx, put.Y, trustFog: false))
                    || !Clear(at, _sweep, pickPower, blind, _cut, out float doubt))
                {
                    continue;
                }

                List<Point> ahead = _cut;

                float wet = Soak(next, ahead, costs);
                if (float.IsPositiveInfinity(wet))
                {
                    continue;
                }

                yield return new Edge(
                    new Step(next, StepKind.Place,
                        ahead.Count > 0 ? ahead.ToArray() : null, put),
                    (costs.PlaceCost + (costs.WalkCost * 3f) + (costs.MineCost * ahead.Count * doubt))
                    * wet);
            }
        }

        // Pillar up: jump, and place a block under your own feet. This is the
        // only upward move that needs nothing from the terrain.
        if (blocks > 0)
        {
            Point next = new(at.X, at.Y - 1);
            Point put = new(at.X, at.Y - 1);

            // Terraria silently refuses a placement with no neighbour to anchor against,
            // and the follower cannot tell a refusal from a swing that has not landed.
            bool onOurOwnTower = !_belief.Standable(at);
            _sweep.Clear();
            _sweep.Add(next);
            if (_belief.Buildable(put.X, put.Y)
                && (onOurOwnTower || _belief.Holds(put.X, at.Y, trustFog: false))
                && Clear(at, _sweep, pickPower, blind, _cut, out float doubt))
            {
                List<Point> above = _cut;

                // Cutting the ceiling is part of pillaring, not a reason to refuse it:
                // requiring clear space above made a staircase the only way up.
                float wet = Soak(next, above, costs);
                if (!float.IsPositiveInfinity(wet))
                {
                    yield return new Edge(
                        new Step(next, StepKind.Place,
                            above.Count > 0 ? above.ToArray() : null, put),
                        (costs.PlaceCost + (costs.WalkCost * 2f) + (costs.MineCost * above.Count * doubt))
                        * wet);
                }
            }
        }
    }

    /// <summary>
    /// Whether the body would come to rest on this footing once the move has cut.
    /// </summary>
    private bool Rests(Point origin, Point footing,
        List<Point> cut)
    {
        for (int side = 0; side < Body.Width; side++)
        {
            Point above = new(footing.X + side, footing.Y - 1);
            if (Inside(origin, above) || cut.Contains(above))
            {
                continue;
            }

            // Rock and slabs only: both fill the bottom of their cell, so a body entering
            // it rests on them and belongs a row higher. A platform's surface is the top
            // of its cell, so feet a row below walk under it; counting it here made every
            // footing under a work bench invalid.
            if (_belief.KindAt(above.X, above.Y) is TileKind.Solid or TileKind.Slab)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Rows a jump clears with any of the body in water.</summary>
    private const int WetJumpRows = 2;

    /// <summary>Whether a body standing at this footing has any cell in water.</summary>
    private bool Wet(Point footing)
    {
        foreach (Point cell in Body.Cells(footing))
        {
            if (_belief.HasWater(cell.X, cell.Y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What a footing costs for putting the head under, as a multiplier, or infinity
    /// where the move is not allowed at all.
    /// </summary>
    // Refused outright rather than priced dear, when the caller says so: a near wall
    // makes the search expand every dry node before a wet one, then go in anyway.
    //
    // The head, not the feet: liquid fills from the bottom, so wet feet are a puddle
    // and a wet head is a swim, where a torch goes out and the belief with it. Both
    // columns, because half the body under is still under. Lava is cheaper because it
    // glows; what ought to make it dear is damage, and there is none yet.
    private float Soak(Point footing, List<Point>? cut,
        Costs costs)
    {
        int head = footing.Y - Body.Height;
        if (_belief.HasLava(footing.X, head) || _belief.HasLava(footing.X + 1, head))
        {
            return costs.LavaCost;
        }

        if (_belief.HasWater(footing.X, head) || _belief.HasWater(footing.X + 1, head))
        {
            return costs.WaterCost;
        }

        return cut is not null && Floods(cut) ? costs.WaterCost : 1f;
    }

    /// <summary>Whether breaking any of these would let a pool in.</summary>
    // A wall with water behind it is water one swing later; charging only for standing
    // in it priced a shaft into a flooded cavern as dry all the way down. The cell
    // itself as well as its neighbours: a tile can hold liquid and rock at once.
    private bool Floods(List<Point> cut)
    {
        for (int n = 0; n < cut.Count; n++)
        {
            Point cell = cut[n];
            if (_belief.HasWater(cell.X, cell.Y)
                || _belief.HasWater(cell.X - 1, cell.Y)
                || _belief.HasWater(cell.X + 1, cell.Y)
                || _belief.HasWater(cell.X, cell.Y - 1)
                || _belief.HasWater(cell.X, cell.Y + 1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a footing can be stepped up onto, rather than only landed on.</summary>
    // Either column will do: half a body over a ledge is enough for the game to lift
    // the rest.
    private bool Climbs(Point footing)
    {
        for (int side = 0; side < Body.Width; side++)
        {
            if (_belief.KindAt(footing.X + side, footing.Y)
                is TileKind.Solid or TileKind.Slab)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a move needs one of the tiles that will not break.</summary>
    private static bool Blocked(Point[] breaks, ISet<Point> immovable)
    {
        for (int n = 0; n < breaks.Length; n++)
        {
            if (immovable.Contains(breaks[n]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the body can be swept along these footings, and at what it costs to cut.
    /// </summary>
    /// <summary>Whether the body fits at this footing with nothing needing to be cut.</summary>
    // What the jump loops actually want. They called Clear and then refused any answer
    // with something to cut, so the digging half of that work was thrown away, and they
    // asked it of every footing walked so far rather than the one just added.
    private bool Open(Point origin, Point footing)
    {
        if (_fits.TryGetValue(footing, out bool known))
        {
            return known;
        }

        bool fits = true;
        for (int dx = 0; dx < Body.Width && fits; dx++)
        {
            for (int dy = 1; dy <= Body.Height && fits; dy++)
            {
                Point cell = new(footing.X + dx, footing.Y - dy);
                fits = Inside(origin, cell) || _belief.Passable(cell.X, cell.Y);
            }
        }

        _fits[footing] = fits;
        return fits;
    }

    // Walked rather than gathered into sets: the jump loops call this sixty times per
    // footing, so allocations here dominate a search. The caller owns the list and this
    // empties it, for the same reason.
    private bool Clear(Point origin, List<Point> footings,
        int pickPower, bool blind, List<Point> cut, out float doubt)
    {
        cut.Clear();
        doubt = 1f;
        foreach (Point footing in footings)
        {
            for (int dx = 0; dx < Body.Width; dx++)
            {
                for (int dy = 1; dy <= Body.Height; dy++)
                {
                    Point cell = new(footing.X + dx, footing.Y - dy);
                    if (Inside(origin, cell) || _belief.Passable(cell.X, cell.Y))
                    {
                        continue;
                    }

                    if (!_belief.Diggable(cell.X, cell.Y, pickPower))
                    {
                        return false;
                    }

                    // Unknown is optimistically diggable so the agent can open new
                    // ground, but only while it can light what it opens. Blind, the map
                    // never reveals and it stands there.
                    if (blind && _belief.KindAt(cell.X, cell.Y) is TileKind.Unknown)
                    {
                        return false;
                    }

                    // Two footings overlap in four of their six cells, so without this
                    // the same tile is charged for twice.
                    if (cut.Contains(cell))
                    {
                        continue;
                    }

                    cut.Add(cell);
                    doubt = Math.Max(doubt, Uncertainty(cell.X, cell.Y));
                }
            }
        }

        return true;
    }

    /// <summary>Whether a cell is one the body already fills at this footing.</summary>
    // The character is already in these cells, so whatever the belief says about them
    // was either never true or already paid for.
    private static bool Inside(Point footing, Point cell) =>
        cell.X >= footing.X && cell.X < footing.X + Body.Width
        && cell.Y <= footing.Y - 1 && cell.Y >= footing.Y - Body.Height;

    /// <summary>How many columns a jump may cross.</summary>
    // The worst case, since a footing does not record arrival speed: the game's
    // constants give 5.97 tiles from a standstill and 9.49 at a full run, so five cannot
    // fall short. Anything past six needs the speed in the node, not a bigger number.
    private const int LeapColumns = 5;

    /// <summary>The premium for digging blind.</summary>
    private float Uncertainty(int x, int y) =>
        _belief.KindAt(x, y) is TileKind.Unknown ? 1.5f : 1f;

    private static List<Step> Rebuild(Dictionary<Point, (Point From, Step Step)> cameFrom,
        Point start, Point goal)
    {
        // Bounded, because an unbounded walk backwards is a frozen game rather
        // than a wrong answer.
        List<Step> route = [];
        Point at = goal;
        int guard = 0;
        while (at != start
               && guard++ < MaxRoute
               && cameFrom.TryGetValue(at, out (Point From, Step Step) how))
        {
            route.Add(how.Step);
            at = how.From;
        }

        route.Reverse();
        return route;
    }
}
