using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terragent.World;

namespace Terragent.Pathfinding;

/// <summary>
/// A* over the footings the player has seen, with digging priced into every move.
/// </summary>
// An instance holding the terrain it searches: the terrain never changes for the life
// of a search, so it is not threaded through every helper as an argument.
internal sealed class Navigator(ITerrain terrain) : INavigator
{
    private readonly ITerrain _terrain = terrain;

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

    /// <summary>What the search running right now has cost so far.</summary>
    // Scratch, like the collections above, and for the same reason: the things worth
    // counting are four calls down and none of them should grow a parameter to say so.
    private Effort _effort = new();

    /// <summary>The two directions a body walks, jumps and bridges in.</summary>
    private static readonly int[] Sideways = [-1, 1];

    /// <summary>Sideways and straight down, which is how a fall begins.</summary>
    private static readonly int[] Downward = [-1, 0, 1];

    /// <summary>What a cell nobody has seen is worth over one that has been.</summary>
    private const float BlindCost = 1.5f;

    /// <summary>What a cell that collapses is worth over one that stays put.</summary>
    // Dear enough that a way round is worth a good few tiles of walking, and never a wall:
    // a desert has nothing under it but more sand, and refusing it outright would leave a
    // run standing on top of one with nowhere to be.
    private const float FallingCost = 4f;

    /// <summary>Expansions without getting nearer the goal before a search gives up.</summary>
    // A share of the budget rather than a number, so a caller that asked for a small search
    // gives up sooner in proportion.
    //
    // A search that is working its way toward something improves on its nearest footing the
    // whole way: one measured arriving had its last gain at expansion 793 of 887. One that
    // has run out of cheap ground and is fanning out sideways stops improving and never
    // starts again: two measured had their last gains at 220 and 124, then spent the
    // remaining nineteen thousand eight hundred proving it, at a second of game time each.
    // The gap between the two is wide enough to read, and this sits in the middle of it.
    private const int StallShare = 20;

    /// <summary>The fewest expansions any search gets before that rule may fire.</summary>
    private const int LeastPatience = 200;

    /// <summary>Longest route the rebuild will walk back before giving up.</summary>
    private const int MaxRoute = 8192;

    /// <summary>The furthest a descent that has to break something will be planned.</summary>
    // A dig priced as one move, so it stays short. The open drop below has no such
    // reason to stop.
    private const int FallLimit = 20;

    /// <summary>How far a drop through open air is followed, looking for its landing.</summary>
    // Not a rule about falling, which the hole itself bounds. A number only so the loop
    // ends when there is nothing below at all, and past anything a world contains.
    private const int OpenDropLimit = 400;

    /// <summary>
    /// How much dearer than a walk the heuristic assumes the remaining tiles are.
    /// </summary>
    // Underground the true remaining cost is nearly nine times distance times a walk,
    // and charging the walk alone leaves A* spreading out instead of heading for the
    // destination. A weighted search may return a route up to this much dearer than the
    // cheapest; the weight stays well under what it could be because the aim is to
    // stop the frontier ballooning, not to squeeze out the last node.
    private const float Focus = 2.0f;

    /// <summary>Rows a jump clears with any of the body in water.</summary>
    private const int WetJumpRows = 2;

    /// <summary>Columns a jump crosses with any of the body in water.</summary>
    // Water halves how fast the body moves as well as how high it goes, so the dry five
    // does not survive it. Understated for the same reason the dry number is: coming up
    // short costs a dig, and overshooting costs a fall into whatever is below.
    private const int WetJumpColumns = 2;

    /// <summary>How many columns a jump may cross.</summary>
    // The worst case, since a footing does not record arrival speed: the game's
    // constants give 5.97 tiles from a standstill and 9.49 at a full run, so five cannot
    // fall short. Anything past six needs the speed in the node, not a bigger number.
    private const int LeapColumns = 5;

    /// <summary>The route to whichever of these it reaches soonest, or null when none.</summary>
    // The index travels with the route rather than on it. A route is its steps and what
    // they cost to find; a number that only means something against somebody else's list
    // is not part of a path, and it is not a second answer either.
    public RouteMatch? FindRoute(Point from, IReadOnlyList<Destination> destinations,
        Ability ability) =>
        FindRoute(ability.Costs, ability.PickPower, ability.Leap, from, destinations,
            ability.Blocks, null);

    // One destination is a list of one, and nothing is filtered on the way back out. A
    // caller that wants only an arriving route reads Arrives and says so itself.
    public RouteMatch? FindRoute(Point from, Destination to, Ability ability) =>
        FindRoute(from, [to], ability);

    /// <summary>
    /// The cheapest route to whichever destination turns out to be cheapest.
    /// </summary>
    // One search over all of them, not one per destination: straight-line distance picks
    // the ore behind a wall over the ore down an open shaft.
    //
    // Destinations and not tiles, so that each says for itself what arriving at it means.
    // A tile with a radius beside it in a second list is the same thing said twice, and
    // the two lists have to be kept in step by hand.
    /// <param name="leap">What a jump from a standstill can reach, in rows and in columns at each landing height.</param>
    private RouteMatch? FindRoute(Costs costs, int pickPower, Leap leap,
        Point from, IReadOnlyList<Destination> destinations, int blocks,
        ISet<Point>? immovable)
    {
        if (destinations.Count == 0)
        {
            return null;
        }

        // The largest any of them asked for. One search serves the whole list and can have
        // only one cap, and a smaller one denies a destination the budget it came with.
        int searchBudget = destinations.Max(place => place.Budget);

        // Footings seen but not yet expanded, ordered by cost so far plus estimate.
        PriorityQueue<Point, float> frontier = new();

        // Which footing each one was reached from and by what move, so a route can be
        // rebuilt backwards from any footing the search settled.
        Dictionary<Point, (Point From, Step Step)> cameFrom = [];

        // The cheapest anyone has reached each footing for, so a dearer way to one already
        // settled is dropped rather than queued again.
        Dictionary<Point, float> best = new() { [from] = 0f };

        // Blocks laid along the best path to each footing, so a route never plans more
        // placements than are carried.
        Dictionary<Point, int> placed = new() { [from] = 0 };

        // Where the route ends when no destination is reached: of every footing expanded
        // so far, the one whose estimate to the nearest destination is smallest, and that
        // estimate. Kept as the search runs rather than worked out at the end, since every
        // footing is scored against the goal anyway and a second pass over them all would
        // cost what another search costs.
        Point closest = from;
        float nearest = Estimate(from, destinations, costs.WalkCost);

        // The tally, handed out on whatever route comes back. A fresh one per search, so
        // two searches never share a count, and held here rather than passed down because
        // the things being counted are four calls deep.
        _effort = new Effort();

        // Settled once already, which is the only way to tell a re-expansion from a first
        // look: there is no decrease-key, so a cheaper way to a footing that has been
        // popped puts it back on the queue.
        HashSet<Point> settled = [];

        frontier.Enqueue(from, nearest);
        _effort.Generated++;
        int expanded = 0;
        int patience = Math.Max(LeastPatience, searchBudget / StallShare);
        bool stalled = false;

        while (frontier.Count > 0 && expanded++ < searchBudget)
        {
            // Nothing has got nearer the goal in a long time, so nothing is going to. What
            // is left to settle is cheaper than everything that would approach the goal,
            // which is what being fanned out sideways looks like from in here, and the
            // answer it would hand back at the end is the one it already has.
            if (expanded - _effort.Gained > patience)
            {
                stalled = true;
                break;
            }

            _effort.Peak = Math.Max(_effort.Peak, frontier.Count);
            Point current = frontier.Dequeue();
            _effort.Expanded = expanded;
            if (!settled.Add(current))
            {
                _effort.Reexpanded++;
            }

            if (Reached(current, destinations, out int which))
            {
                _effort.Ending = Ending.Arrived;
                return new RouteMatch(which,
                    new Route(Rebuild(cameFrom, from, current), from, _effort));
            }

            float away = Estimate(current, destinations, costs.WalkCost);
            if (away < nearest)
            {
                _effort.Gained = expanded;
                nearest = away;
                closest = current;
            }

            foreach (Edge move in Moves(current, costs, pickPower, blocks, leap))
            {
                _effort.Edges++;
                Point next = move.Next;

                // A landing whose only floor is a tile the body has been standing in. That
                // tile is air, because the body was in it, so the route would be breaking
                // its own stairs: a run watched here drew a staircase whose third step
                // landed on a block its first step had mined out from under it, arrived a
                // row low, and pressed a move it could never finish. A placement makes its
                // own floor and is asked nothing.
                if (move.Step.Kind is not StepKind.Place
                    && !Supported(cameFrom, current, next))
                {
                    continue;
                }

                // A drop needs the ground under the body gone, and the search reads that
                // off the world as it stands. A route that bridges along a row and then
                // plans to fall through it is reading a floor it is about to build: a run
                // stood on the last block of its own bridge pressing a fall that could
                // never happen, once every two seconds until it was killed.
                if (move.Step.Kind is StepKind.Fall && Propped(cameFrom, current))
                {
                    continue;
                }

                // The other way round, and the same mistake: a tower whose first block is
                // going onto ground this route has already cut away. The anchor is read off
                // the world when the move is built, which is the world before any of the
                // plan happens. A run walked a step whose head room took the very tile its
                // next block was to stand on, rose, swung at mid air and came down on the
                // same tile, four times a second until it was killed.
                //
                // A tower only, which is a block going a row above the feet. A bridge lays
                // beside them and holds on sideways, so what is under it was never the
                // question.
                if (move.Step.Puts is { } laying && laying.Y < current.Y
                    && !Anchored(cameFrom, current, laying))
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
                _effort.Generated++;
                frontier.Enqueue(next,
                    candidate + Estimate(next, destinations, costs.WalkCost));
            }
        }

        // Nothing arrived at, so hand back the way to the nearest place it proved it can
        // stand. Exact rather than guessed: a point picked a fixed distance along the
        // straight line to the goal can sit inside a wall, and this is ground the search
        // has actually walked to.
        // What the caller does with it is walk it and ask again from further along, which
        // is how sixty tiles of tunnel get planned twenty at a time.
        //
        // Null when that place is where the body already is. Nothing about walking helps
        // then, and saying so is a truer answer than a route with no steps in it.
        _effort.Ending = stalled ? Ending.Stalled
            : frontier.Count == 0 ? Ending.Exhausted
            : Ending.Spent;
        if (closest == from)
        {
            return null;
        }

        return new RouteMatch(
            IndexOfNearestDestination(closest, destinations, costs.WalkCost),
            new Route(Rebuild(cameFrom, from, closest), from, _effort), Arrives: false);
    }

    /// <summary>Where the destination a footing is nearest to sits in the list.</summary>
    // The match names a destination because the caller reads it back to learn whose work
    // the route belongs to. A partial route has arrived at none of them, so it says which
    // one it was heading for. By estimate, which is distance: the one thing a search
    // exists to overrule, and all there is to go on once the search has failed.
    private static int IndexOfNearestDestination(Point at,
        IReadOnlyList<Destination> destinations, float walk)
    {
        int which = 0;
        float best = float.MaxValue;
        for (int n = 0; n < destinations.Count; n++)
        {
            float away = Estimate(at, destinations[n].Site, walk, destinations[n].Within);
            if (away < best)
            {
                best = away;
                which = n;
            }
        }

        return which;
    }

    /// <summary>
    /// Whether standing at <paramref name="at"/> counts as having reached a tile.
    /// </summary>
    // A box, not the circle Pilot.Shifted measures: Terraria's reach is rectangular,
    // with tileRangeX and tileRangeY separate. Do not change either to match the other.
    public static bool Reached(Point at, Point site, int radius) =>
        Reached(at, site, radius, radius);

    /// <summary>The same with the columns and the rows judged separately.</summary>
    // Terraria's crafting reach is wider than it is tall. One number for both either stops
    // the body two rows above a bench the game will not let it use, or walks it further
    // along the floor than it needs to go.
    public static bool Reached(Point at, Point site, int across, int down) =>
        Gap(at, site) <= across && Math.Abs(at.Y - site.Y) <= down;

    /// <summary>How many columns short of a site the body is, zero when over it.</summary>
    private static int Gap(Point at, Point site) =>
        Math.Max(0, Math.Max(at.X - site.X, site.X - (at.X + Hitbox.Width - 1)));

    /// <summary>Ticks to reach a site if nothing were in the way.</summary>
    private static float Estimate(Point at, Point site, float walk, int radius) =>
        Math.Max(0, Math.Max(Gap(at, site), Math.Abs(at.Y - site.Y)) - radius)
        * walk * Focus;

    /// <summary>The nearest destination's estimate, which is the only one that may be used.</summary>
    // Anything larger overestimates the distance to the one that is actually closest. Each
    // subtracts its own radius, so a list of destinations that stop at different distances
    // still leaves this a lower bound on every one of them.
    private static float Estimate(Point at, IReadOnlyList<Destination> destinations,
        float walk)
    {
        float best = float.MaxValue;
        foreach (Destination place in destinations)
        {
            best = Math.Min(best, Estimate(at, place.Site, walk, place.Within));
        }

        return destinations.Count == 0 ? 0f : best;
    }

    /// <summary>
    /// Which of these has been reached, each saying for itself what counts as being there.
    /// </summary>
    // One radius for a mixed list is wrong for all but one of them: a drop has to be
    // touched, a stone block reached with a pickaxe, and a creature hit with what is
    // carried. Asking the destination rather than rebuilding a number from it is what
    // keeps the offer that was chosen and the route that is walked answering alike.
    private static bool Reached(Point at, IReadOnlyList<Destination> destinations,
        out int which)
    {
        for (int n = 0; n < destinations.Count; n++)
        {
            if (destinations[n].Reached(at))
            {
                which = n;
                return true;
            }
        }

        which = 0;
        return false;
    }

    /// <summary>
    /// Every move out of a footing: how it travels, what it lands on, what it breaks.
    /// </summary>
    private IEnumerable<Edge> Moves(Point at, Costs costs,
        int pickPower, int blocks, Leap leap)
    {
        bool blind = float.IsPositiveInfinity(costs.FogCost);

        // Water, once nothing carried lights it: a rule and not a price. A price only ranks
        // one route against another, so where every route to a goal is wet the cheapest is
        // still wet and the number cancels out. A run walked into a pool that way, drowned
        // the torch that was its only light, and could not see to leave.
        //
        // Off again for a body already in it, which has to be able to move to get out.
        bool submerged = _terrain.Wet(at);
        bool keepDry = float.IsPositiveInfinity(costs.IntoWaterCost) && !submerged;

        // Inside is asked against this footing, so an answer kept from the last one
        // would be an answer to a different question.
        _fits.Clear();

        foreach (int dx in Sideways)
        {
            // Walk on the level, or step up one row, which the game does for free.
            for (int dy = 0; dy >= -1; dy--)
            {
                Point next = new(at.X + dx, at.Y + dy);
                if (!_terrain.Standable(next))
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
                if (Clear(at, _sweep, pickPower, blind, keepDry, _cut, out float doubt)
                    && Rests(at, next, _cut))
                {
                    List<Point> cut = _cut;
                    float wet = Soak(next, cut, costs);
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
            // Water takes the sideways half of a jump as well as the upward half, and
            // only the rise was ever capped, so a diagonal hop planned while submerged
            // was drawn to a ledge the body could not carry itself to.
            int climb = submerged ? System.Math.Min(leap.Height, WetJumpRows) : leap.Height;
            int span = submerged ? WetJumpColumns : LeapColumns;
            for (int up = 1; up <= climb; up++)
            {
                if (!Open(at, new Point(at.X, at.Y - up), keepDry))
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
                for (int across = 1; across <= span; across++)
                {
                    if (!Open(at, new Point(at.X + (dx * across), at.Y - up), keepDry))
                    {
                        break;
                    }

                    // The first thing under the far end is the landing; anything below
                    // it is a Fall from there.
                    for (int drop = 0; drop <= up; drop++)
                    {
                        Point next = new(at.X + (dx * across), at.Y - up + drop);
                        if (!_terrain.Settles(next))
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
                            fits = Open(at, new Point(next.X, row), keepDry);
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
                                fits = Open(at, new Point(at.X + (dx * back), row), keepDry);
                            }
                        }

                        if (fits)
                        {
                            float wet = Soak(next, null, costs);
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
                if (!Clear(at, _sweep, pickPower, blind, keepDry, cut, out doubt))
                {
                    continue;
                }
            }

            // A drop through open air runs as far as the hole does. Only a descent that
            // has to break something is capped, because that is a dig priced as one move
            // and a hundred rows of it is not a move. Capping the open case is what had
            // the agent cutting a fresh shaft beside a hole it could have fallen down.
            for (int down = 1; down <= (cut.Count == 0 ? OpenDropLimit : FallLimit); down++)
            {
                // The row newly entered on the way down. Anything that holds the
                // character up is in the way, a platform included: it stops a fall
                // however freely it is walked through sideways.
                bool sealed_ = false;
                for (int side = 0; side < Hitbox.Width; side++)
                {
                    int x = column.X + side;
                    int row = at.Y + down - 1;

                    // Water stops nothing, so the drop goes through it rather than onto it,
                    // and a body that crosses a pool has been in one.
                    if (keepDry && _terrain.HasWater(x, row))
                    {
                        sealed_ = true;
                        break;
                    }

                    if (!_terrain.Holds(x, row, trustFog: true))
                    {
                        continue;
                    }

                    if (!_terrain.Diggable(x, row, pickPower)
                        || (keepDry && Spills(new Point(x, row))))
                    {
                        sealed_ = true;
                        break;
                    }

                    cut.Add(new Point(x, row));
                    doubt = Math.Max(doubt, Premium(x, row));
                }

                if (sealed_)
                {
                    break;
                }

                // Seen ground only. A landing nobody has looked at is a guess about
                // where the body stops, and a drop onto one is how a run ended up in a
                // pool it could not see, with no light that works under water, nothing
                // left to dig and nowhere to go. Falling in is easy and getting out is
                // not, so the fall is the place to be careful.
                Point next = new(column.X, at.Y + down);
                if (!_terrain.Standable(next, trustFog: false))
                {
                    continue;
                }

                float wet = Soak(next, cut, costs);
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
                // into the terrain, so from the second block on the anchor is one that
                // does not exist yet.
                bool onOurOwnBridge = !_terrain.Standable(at);
                _sweep.Clear();
                _sweep.Add(next);
                if (!(_terrain.Buildable(put.X, put.Y) || _terrain.Clutter(put.X, put.Y))
                    || !(onOurOwnBridge
                         || _terrain.Holds(put.X - dx, put.Y, trustFog: false))
                    || !Clear(at, _sweep, pickPower, blind, keepDry, _cut, out float doubt))
                {
                    continue;
                }

                List<Point> ahead = _cut;

                // The plant standing where the block goes. Nothing else books it: it does
                // not hold the body up and does not block it, so every move walks through
                // it, and the placement is then refused without a word.
                if (_terrain.Clutter(put.X, put.Y))
                {
                    ahead.Add(put);
                }

                float wet = Soak(next, ahead, costs);
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
            // Terraria silently refuses a placement with no neighbour to anchor against,
            // and the follower cannot tell a refusal from a swing that has not landed.
            bool onOurOwnTower = !_terrain.Standable(at);

            // The column the body is actually standing on, which is not always the one the
            // footing is named after. A footing holds if either of its two columns has
            // ground under it, so a body on the lip of a ledge stands on its right column
            // with nothing at all under its left. Built in the left one there, the block
            // hangs in mid air and the game refuses it without a word: a run rose, swung,
            // came down on the same tile and did it again until it was killed.
            //
            // The footing moves with it. Nothing in a footing says which column a tower is
            // in, since the body covers the same two at every height, so naming the block's
            // own column keeps the next step over the last block instead of beside it. Two
            // pillars in a row that disagree about the column build a staircase of corners,
            // which the game refuses for the same reason.
            Point put = new(
                onOurOwnTower || _terrain.Holds(at.X, at.Y, trustFog: false)
                    ? at.X
                    : at.X + Hitbox.Width - 1,
                at.Y - 1);
            Point next = put;
            _sweep.Clear();

            // Both footings, because a tower in the right column ends one over from where
            // the body is now and it has to rise before it can shift across. Asked only of
            // where it ends up, a rise into a ceiling over its own column reads as clear: a
            // run bumped its head on the same tile four times a second until it was killed.
            // The two are the same point whenever the tower is in the left column.
            _sweep.Add(new Point(at.X, at.Y - 1));
            _sweep.Add(next);
            // Buildable, or a cell the body is standing in, which is air by the time the
            // block goes down whatever the terrain says now: whatever move arrived at this
            // footing had to clear the cells the body fills, and this is one of them. Asked
            // of the world alone, a run stopped pillaring the moment it was inside rock and
            // dug a diagonal staircase away from its goal instead of going straight up.
            if ((_terrain.Buildable(put.X, put.Y)
                 || _terrain.Clutter(put.X, put.Y)
                 || (Inside(at, put)
                     && _terrain.KindAt(put.X, put.Y) is TileKind.Solid or TileKind.Slab))
                && (onOurOwnTower || _terrain.Holds(put.X, at.Y, trustFog: false))
                && Clear(at, _sweep, pickPower, blind, keepDry, _cut, out float doubt))
            {
                List<Point> above = _cut;
                if (_terrain.Clutter(put.X, put.Y))
                {
                    above.Add(put);
                }

                // Cutting the ceiling is part of pillaring, not a reason to refuse it:
                // requiring clear space above made a staircase the only way up.
                float wet = Soak(next, above, costs);
                yield return new Edge(
                    new Step(next, StepKind.Place,
                        above.Count > 0 ? above.ToArray() : null, put),
                    (costs.PlaceCost + (costs.WalkCost * 2f) + (costs.MineCost * above.Count * doubt))
                    * wet);
            }
        }
    }

    /// <summary>
    /// Whether the body would come to rest on this footing once the move has cut.
    /// </summary>
    private bool Rests(Point origin, Point footing,
        List<Point> cut)
    {
        for (int side = 0; side < Hitbox.Width; side++)
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
            if (_terrain.KindAt(above.X, above.Y) is TileKind.Solid or TileKind.Slab)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What a move made in liquid costs, as a multiplier.</summary>
    // For a body already in one, which is the only way a wet move gets drawn now: dear, so
    // the way out is the shortest one. Lava is cheaper because it glows; what ought to make
    // it dear is damage, and there is none yet.
    private float Soak(Point footing, List<Point>? cut, Costs costs)
    {
        if (_terrain.Scalding(footing))
        {
            return costs.LavaCost;
        }

        return _terrain.Wet(footing) || (cut is not null && Floods(cut))
            ? costs.InWaterCost
            : 1f;
    }

    /// <summary>Whether breaking any of these would let a pool in.</summary>
    // A wall with water behind it is water one swing later; charging only for standing
    // in it priced a shaft into a flooded cavern as dry all the way down. The cell
    // itself as well as its neighbours: a tile can hold liquid and rock at once.
    private bool Floods(List<Point> cut)
    {
        for (int n = 0; n < cut.Count; n++)
        {
            if (Spills(cut[n]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether breaking one cell would let a pool into it.</summary>
    // The cell itself as well as its neighbours: a tile can hold liquid and rock at once.
    private bool Spills(Point cell) =>
        _terrain.HasWater(cell.X, cell.Y)
        || _terrain.HasWater(cell.X - 1, cell.Y)
        || _terrain.HasWater(cell.X + 1, cell.Y)
        || _terrain.HasWater(cell.X, cell.Y - 1)
        || _terrain.HasWater(cell.X, cell.Y + 1);

    /// <summary>Whether a footing can be stepped up onto, rather than only landed on.</summary>
    // Either column will do: half a body over a ledge is enough for the game to lift
    // the rest.
    private bool Climbs(Point footing)
    {
        for (int side = 0; side < Hitbox.Width; side++)
        {
            if (_terrain.KindAt(footing.X + side, footing.Y)
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
    private bool Open(Point origin, Point footing, bool keepDry)
    {
        _effort.Asked++;
        if (_fits.TryGetValue(footing, out bool known))
        {
            _effort.Remembered++;
            return known;
        }

        bool fits = true;
        for (int dx = 0; dx < Hitbox.Width && fits; dx++)
        {
            for (int dy = 1; dy <= Hitbox.Height && fits; dy++)
            {
                Point cell = new(footing.X + dx, footing.Y - dy);
                fits = (Inside(origin, cell) || _terrain.Passable(cell.X, cell.Y))
                    && !(keepDry && _terrain.HasWater(cell.X, cell.Y));
            }
        }

        _fits[footing] = fits;
        return fits;
    }

    // Walked rather than gathered into sets: the jump loops call this sixty times per
    // footing, so allocations here dominate a search. The caller owns the list and this
    // empties it, for the same reason.
    private bool Clear(Point origin, List<Point> footings,
        int pickPower, bool blind, bool keepDry, List<Point> cut, out float doubt)
    {
        _effort.Swept++;
        cut.Clear();
        doubt = 1f;
        foreach (Point footing in footings)
        {
            for (int dx = 0; dx < Hitbox.Width; dx++)
            {
                for (int dy = 1; dy <= Hitbox.Height; dy++)
                {
                    Point cell = new(footing.X + dx, footing.Y - dy);

                    // A cell with water in it is not a cell the body may be in.
                    if (keepDry && _terrain.HasWater(cell.X, cell.Y))
                    {
                        return false;
                    }

                    if (Inside(origin, cell) || _terrain.Passable(cell.X, cell.Y))
                    {
                        continue;
                    }

                    if (!_terrain.Diggable(cell.X, cell.Y, pickPower))
                    {
                        return false;
                    }

                    // Unknown is optimistically diggable so the agent can open new
                    // ground, but only while it can light what it opens. Blind, the map
                    // never reveals and it stands there.
                    if (blind && _terrain.KindAt(cell.X, cell.Y) is TileKind.Unknown)
                    {
                        return false;
                    }

                    // Water behind a block arrives the moment the block goes, and the body
                    // is standing in what it opened. Dry on both sides of the swing and wet
                    // a tick later is still wet.
                    if (keepDry && Spills(cell))
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
                    doubt = Math.Max(doubt, Premium(cell.X, cell.Y));
                }
            }
        }

        return true;
    }

    /// <summary>Whether a landing has a floor this route has not already taken away.</summary>
    // Standable asks the world as it is. This asks whether that answer survives the route
    // that reaches it, which is the question that matters. A route destroys floors two
    // ways, and both count: the body clears the cells it passes through, so a tile it has
    // stood in is gone, and every step carries the tiles it will break, which are gone too.
    //
    // It is also the true statement about stepping up. A body steps onto a block ahead of
    // it and never onto one it is standing inside, nor onto one it is about to mine.
    //
    // Only as far back as the body is tall, and that is exact rather than a sample. A floor
    // sits on its own footing's row, and nothing a step removes lies more than three rows
    // above the footing it removes it from, so a step further back than that cannot reach
    // it.
    private bool Supported(Dictionary<Point, (Point From, Step Step)> cameFrom,
        Point current, Point landing)
    {
        for (int side = 0; side < Hitbox.Width; side++)
        {
            Point floor = new(landing.X + side, landing.Y);
            if (!_terrain.Holds(floor.X, floor.Y, trustFog: false))
            {
                continue;
            }

            if (Survives(cameFrom, current, floor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the ground a block is going onto is still there.</summary>
    // Asked of the plan and not of the world, which is the whole point: the world still has
    // the tile, and three steps of this route have gone by since it was counted on.
    //
    // Nothing to answer when the world has no ground there anyway. The move was built on
    // some other footing then, standing on its own tower, and this has no opinion about it.
    private bool Anchored(Dictionary<Point, (Point From, Step Step)> cameFrom,
        Point current, Point put)
    {
        Point under = new(put.X, put.Y + 1);
        return !_terrain.Holds(under.X, under.Y, trustFog: false)
            || Survives(cameFrom, current, under);
    }

    /// <summary>Whether this route has laid a block under the body, holding it up.</summary>
    // The mirror of Survives. That one asks whether a floor the route needs is still there;
    // this asks whether a floor the route needs gone has been put there, which is the same
    // mistake made the other way round.
    private static bool Propped(Dictionary<Point, (Point From, Step Step)> cameFrom,
        Point at)
    {
        Point back = at;
        for (int n = 0; n < Hitbox.Height; n++)
        {
            if (!cameFrom.TryGetValue(back, out (Point From, Step Step) came))
            {
                return false;
            }

            if (came.Step.Puts is { } put && put.Y == at.Y
                && put.X >= at.X && put.X < at.X + Hitbox.Width)
            {
                return true;
            }

            back = came.From;
        }

        return false;
    }

    /// <summary>Whether a tile is still there once the route that reaches here has run.</summary>
    private static bool Survives(Dictionary<Point, (Point From, Step Step)> cameFrom,
        Point current, Point floor)
    {
        Point back = current;
        for (int n = 0; n < Hitbox.Height; n++)
        {
            if (Inside(back, floor))
            {
                return false;
            }

            if (!cameFrom.TryGetValue(back, out (Point From, Step Step) came))
            {
                return true;
            }

            foreach (Point cell in came.Step.Removes)
            {
                if (cell == floor)
                {
                    return false;
                }
            }

            back = came.From;
        }

        return true;
    }

    /// <summary>Whether a cell is one the body already fills at this footing.</summary>
    // The character is already in these cells, so whatever the terrain says about them
    // was either never true or already paid for.
    private static bool Inside(Point footing, Point cell) =>
        cell.X >= footing.X && cell.X < footing.X + Hitbox.Width
        && cell.Y <= footing.Y - 1 && cell.Y >= footing.Y - Hitbox.Height;

    /// <summary>What digging this particular cell is worth over an ordinary one.</summary>
    // Two kinds of bad cell, and a step pays the worse of whatever it cuts.
    private float Premium(int x, int y)
    {
        if (_terrain.Falls(x, y))
        {
            return FallingCost;
        }

        return _terrain.KindAt(x, y) is TileKind.Unknown ? BlindCost : 1f;
    }

    private static List<Step> Rebuild(Dictionary<Point, (Point From, Step Step)> cameFrom,
        Point start, Point last)
    {
        // Bounded, because an unbounded walk backwards is a frozen game rather
        // than a wrong answer.
        List<Step> route = [];
        Point at = last;
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
