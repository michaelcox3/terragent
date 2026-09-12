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

    /// <summary>The two directions a body walks, jumps and bridges in.</summary>
    private static readonly int[] Sideways = [-1, 1];

    /// <summary>Sideways and straight down, which is how a fall begins.</summary>
    private static readonly int[] Downward = [-1, 0, 1];

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
        Ability ability, ISet<(Point From, Point To)> refused) =>
        FindRoute(ability.Costs, ability.PickPower, ability.Leap, from, destinations,
            refused, ability.Blocks, null);

    // One destination is a list of one, and nothing is filtered on the way back out. A
    // caller that wants only an arriving route reads Arrives and says so itself.
    public RouteMatch? FindRoute(Point from, Destination to, Ability ability,
        ISet<(Point From, Point To)> refused) =>
        FindRoute(from, [to], ability, refused);

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
        Point from, IReadOnlyList<Destination> destinations,
        ISet<(Point From, Point To)>? refused, int blocks, ISet<Point>? immovable)
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

        frontier.Enqueue(from, nearest);
        int expanded = 0;

        while (frontier.Count > 0 && expanded++ < searchBudget)
        {
            Point current = frontier.Dequeue();
            if (Reached(current, destinations, out int which))
            {
                return new RouteMatch(which,
                    new Route(Rebuild(cameFrom, from, current), expanded));
            }

            float away = Estimate(current, destinations, costs.WalkCost);
            if (away < nearest)
            {
                nearest = away;
                closest = current;
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
        if (closest == from)
        {
            return null;
        }

        return new RouteMatch(
            IndexOfNearestDestination(closest, destinations, costs.WalkCost),
            new Route(Rebuild(cameFrom, from, closest), expanded), Arrives: false);
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
                if (Clear(at, _sweep, pickPower, blind, _cut, out float doubt)
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
            bool submerged = Wet(at);
            int climb = submerged ? System.Math.Min(leap.Height, WetJumpRows) : leap.Height;
            int span = submerged ? WetJumpColumns : LeapColumns;
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
                for (int across = 1; across <= span; across++)
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
                    if (!_terrain.Holds(x, row, trustFog: true))
                    {
                        continue;
                    }

                    if (!_terrain.Diggable(x, row, pickPower))
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
                if (!_terrain.Standable(next, trustFog: true))
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
                if (!_terrain.Buildable(put.X, put.Y)
                    || !(onOurOwnBridge
                         || _terrain.Holds(put.X - dx, put.Y, trustFog: false))
                    || !Clear(at, _sweep, pickPower, blind, _cut, out float doubt))
                {
                    continue;
                }

                List<Point> ahead = _cut;

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
            Point next = new(at.X, at.Y - 1);
            Point put = new(at.X, at.Y - 1);

            // Terraria silently refuses a placement with no neighbour to anchor against,
            // and the follower cannot tell a refusal from a swing that has not landed.
            bool onOurOwnTower = !_terrain.Standable(at);
            _sweep.Clear();
            _sweep.Add(next);
            if (_terrain.Buildable(put.X, put.Y)
                && (onOurOwnTower || _terrain.Holds(put.X, at.Y, trustFog: false))
                && Clear(at, _sweep, pickPower, blind, _cut, out float doubt))
            {
                List<Point> above = _cut;

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

    /// <summary>Whether a body standing at this footing has any cell in water.</summary>
    private bool Wet(Point footing)
    {
        foreach (Point cell in Hitbox.Cells(footing))
        {
            if (_terrain.HasWater(cell.X, cell.Y))
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
    // A price and never a refusal. What the price is worth saying is in Costs; what is
    // wet is this file's.
    //
    // The head, not the feet: liquid fills from the bottom, so wet feet are a puddle and a
    // wet head is a swim, where a torch goes out and the terrain with it. Both columns,
    // because half the body under is still under. Lava is cheaper because it glows; what
    // ought to make it dear is damage, and there is none yet.
    private float Soak(Point footing, List<Point>? cut,
        Costs costs)
    {
        int head = footing.Y - Hitbox.Height;
        if (_terrain.HasLava(footing.X, head) || _terrain.HasLava(footing.X + 1, head))
        {
            return costs.LavaCost;
        }

        if (_terrain.HasWater(footing.X, head) || _terrain.HasWater(footing.X + 1, head))
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
            if (_terrain.HasWater(cell.X, cell.Y)
                || _terrain.HasWater(cell.X - 1, cell.Y)
                || _terrain.HasWater(cell.X + 1, cell.Y)
                || _terrain.HasWater(cell.X, cell.Y - 1)
                || _terrain.HasWater(cell.X, cell.Y + 1))
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
    private bool Open(Point origin, Point footing)
    {
        if (_fits.TryGetValue(footing, out bool known))
        {
            return known;
        }

        bool fits = true;
        for (int dx = 0; dx < Hitbox.Width && fits; dx++)
        {
            for (int dy = 1; dy <= Hitbox.Height && fits; dy++)
            {
                Point cell = new(footing.X + dx, footing.Y - dy);
                fits = Inside(origin, cell) || _terrain.Passable(cell.X, cell.Y);
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
            for (int dx = 0; dx < Hitbox.Width; dx++)
            {
                for (int dy = 1; dy <= Hitbox.Height; dy++)
                {
                    Point cell = new(footing.X + dx, footing.Y - dy);
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
    // The character is already in these cells, so whatever the terrain says about them
    // was either never true or already paid for.
    private static bool Inside(Point footing, Point cell) =>
        cell.X >= footing.X && cell.X < footing.X + Hitbox.Width
        && cell.Y <= footing.Y - 1 && cell.Y >= footing.Y - Hitbox.Height;

    /// <summary>The premium for digging blind.</summary>
    private float Uncertainty(int x, int y) =>
        _terrain.KindAt(x, y) is TileKind.Unknown ? 1.5f : 1f;

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
