using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.Do;

/// <summary>
/// Putting a crafting station down, standing at it, and taking it back up.
/// </summary>
// The agent carries its workshop, so a station is placed where it is wanted and
// collected afterwards rather than walked back to. Workshop remembers which one is on
// the ground; this does the placing, the standing beside and the fetching back.
internal sealed class Building(Pilot pilot, Belief belief,
    Character character, Stations stations, Workshop workshop)
{
    private readonly Pilot _pilot = pilot;

    private readonly Belief _belief = belief;

    private readonly Character _character = character;

    private readonly Stations _stations = stations;
    private readonly Workshop _workshop = workshop;

    /// <summary>How far to look for somewhere to put a station down, in tiles.</summary>
    // Beside the character: a station is put down to use now and picked up afterwards,
    // and a workshop kept at spawn is a walk back for every recipe. See Workshop.
    public const int PlacingReach = 20;

    /// <summary>A footing the game will accept as standing at this station.</summary>
    // On the station's own floor or top, to one side of it. Terraria decides crafting
    // range from a box around the character, and a bench above its head is outside it,
    // reported only by not offering the recipe. Standing on the bench counts, and in a
    // three-wide shaft it is the only footing wide enough for the body. The columns
    // must match the ones GoToStation calls "at the station".
    public Point? Beside(Point station)
    {
        Point? best = null;
        int bestDistance = int.MaxValue;

        for (int dy = 0; dy <= 1; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                Point footing = new(station.X + dx, station.Y + dy);
                if (!_belief.Settles(footing))
                {
                    continue;
                }

                int distance = (dx * dx) + dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = footing;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The cell nearest a given point that this station will actually go in.
    /// </summary>
    // Deliberately not limited to arm's length: somewhere it can be put is a place to
    // walk to, and a hillside on both sides should not end the run.
    public bool Somewhere(int tileID, Point near,
        out int foundX, out int foundY)
    {
        foundX = foundY = 0;
        for (int radius = 0; radius <= PlacingReach; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (radius > 0 && Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    int x = near.X + dx;
                    int y = near.Y + dy;
                    if (!_belief.CanPlace(x, y, tileID))
                    {
                        continue;
                    }

                    // Somewhere it can be used from, not merely somewhere it will stick.
                    // Its own top counts, as Beside says, but the bench is not there yet
                    // to be stood on, so the two cells above it are asked directly.
                    if (Beside(new Point(x, y)) is null
                        && (_belief.Holds(x, y - 1, false) || _belief.Holds(x + 1, y - 1, false)))
                    {
                        continue;
                    }

                    foundX = x;
                    foundY = y;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The executor, for the two things a station needs from one.</summary>
    // The back edge: obtaining a station and collecting what falls are ordinary jobs
    // the executor runs. Wired after construction because the executor holds this.
    private Executor _executor = null!;

    private double _now;

    public void Runs(Executor executor) => _executor = executor;

    /// <summary>Frames to stand at a station before believing it will not register.</summary>
    private const int ArrivalGrace = 30;

    /// <summary>Seconds of swinging at a station to take back before leaving it standing.</summary>
    private const double ReclaimSeconds = 8.0;

    private Point _reclaiming;

    private double _reclaimingSince;

    private int _atStation;

    /// <summary>Milestones that mean "have one of these within reach".</summary>
    public void Place(int itemID, int tileID, AgentState state, Point at, double now)
    {
        _now = now;
        // One is enough. Satisfaction runs once a second, so for up to sixty frames
        // after placing a bench the milestone is still unmet; without this check
        // "carrying none" crafts another.
        if (_stations.Exists(tileID, _belief, at))
        {
            state.Behaviour = "the station is already down";
            return;
        }

        if (_character.Inventory.Carrying(itemID) == 0)
        {
            _executor.Obtain(new ItemQuantity(itemID, 1), state, _now);
            return;
        }

        if (!_character.Hand.HoldItem(itemID))
        {
            state.Behaviour = $"carrying {Lang.GetItemNameValue(itemID)} but cannot hold it";
            state.Stuck = true;
            return;
        }

        if (!Somewhere(tileID, at, out int x, out int y))
        {
            state.Behaviour = $"nowhere within {PlacingReach} tiles will take a "
                        + Lang.GetItemNameValue(itemID);
            state.Stuck = true;
            return;
        }

        state.Behaviour = $"placing at ({x}, {y})";
        state.Goal = new Point(x, y);
        state.Footprint = _belief.Footprint(x, y, tileID);

        if (!_character.Hand.InPlaceReach(x, y))
        {
            _pilot.Approach(_belief, _character, state, new Point(x, y), _now);
            return;
        }

        _character.Hand.Aim(x, y);
        _character.Hand.Use();
        if (_character.Hand.Blocked)
        {
            state.Behaviour = "holding the swing until the game aims where we do";
            return;
        }

        // Record the attempt, not the success. Pressing use places nothing; ItemCheck
        // does, later in this frame, so the inventory still holds the item here.
        _stations.Attempted(tileID, x, y);
        _workshop.PutDown(itemID, tileID, x, y);
    }

    /// <summary>Walk back to a station this recipe needs.</summary>
    public void GoToStation(Recipe recipe, AgentState state, Point at, double now)
    {
        _now = now;
        foreach (int tileID in recipe.requiredTile)
        {
            if (tileID <= 0)
            {
                continue;
            }

            // Something still wants this one, so the workshop does not take it back.
            _workshop.Wanted(tileID);

            if (_stations.Nearest(tileID, _belief, at) is not { } station)
            {
                // None in the world, so put the carried one down. Place obtains it
                // first if needed, which makes a bench a step of the recipe.
                int itemID = StationKinds.ItemFor(tileID);
                if (itemID == 0)
                {
                    state.Behaviour = $"no way to make a station of tileID {tileID}";
                    state.Stuck = true;
                    return;
                }

                Place(itemID, tileID, state, at, _now);
                return;
            }

            // Beside it, not merely near it: a footing inside the goal radius can be one
            // row short with the bench overhead. Never the body's own footing as a
            // fallback, or the search arrives in zero steps under a bench up a wall.
            if (Beside(station) is not { } target)
            {
                state.Behaviour = $"nothing to stand on beside the station at ({station.X}, {station.Y})";
                Log.Sample("station", state.Behaviour, ("tileID", tileID));
                state.Stuck = true;
                return;
            }

            state.Goal = target;

            // Both axes, and never from below: comparing columns only put an agent
            // that had mined under its bench "at the station" twenty-four rows down.
            if (Math.Abs(station.X - at.X) <= 2 && at.Y <= station.Y + 1 && at.Y >= station.Y - 2)
            {
                // Give the game a moment to agree. Player.adjTile (which FindRecipes
                // reads) is recomputed later in Player.Update than this hook runs, so
                // on the arrival frame it is still the previous frame's answer.
                if (++_atStation < ArrivalGrace)
                {
                    state.Behaviour = $"at the station ({at.X}, {at.Y}), settling";
                    return;
                }

                state.Behaviour = $"at the station ({at.X}, {at.Y}) but it is not registering";
                Log.Sample("station", state.Behaviour,
                    ("tileID", tileID),
                    ("at", $"({at.X}, {at.Y})"),
                    ("adjacent", _character.Inventory.AtStation(tileID)),
                    ("kind", _belief.KindAt(at.X, at.Y).ToString()),
                    ("type", _belief.TypeAt(at.X, at.Y)));
                state.Stuck = true;
                return;
            }

            _atStation = 0;

            // Not Approach. That accepts any footing within swing reach, including
            // directly underneath, and a bench can be hit from below but not crafted at.
            if (!_pilot.Advance(_belief, _character, state, target, _now,
                    arriveWithinTiles: 1,
                    arrived: node => node.Y == target.Y
                                     && Math.Abs(node.X - target.X) <= 1))
            {
                state.Behaviour = _pilot.Behaviour;
                state.Stuck = _pilot.Unreachable;
                return;
            }

            state.Behaviour = _pilot.Behaviour;
            return;
        }

        state.Behaviour = $"need a station for {Lang.GetItemNameValue(recipe.createItem.type)}";

        // Which station, and which recipe wanted it: "need a station for Wood" reads
        // the same whether the plan is sound or the planner should have chopped it.
        Log.Sample("station", state.Behaviour,
            ("recipe", string.Join("+", Ingredients(recipe))),
            ("tiles", string.Join(",", recipe.requiredTile)));
        state.Stuck = true;
    }

    /// <summary>What a recipe asks for, as names, for a log that has to be read.</summary>
    private static IEnumerable<string> Ingredients(Recipe recipe)
    {
        foreach (Item required in recipe.requiredItem)
        {
            if (!required.IsAir)
            {
                yield return $"{required.stack}x{Lang.GetItemNameValue(required.type)}";
            }
        }
    }

    /// <summary>
    /// Take back the station nothing is using. True when that is this tick's job.
    /// </summary>
    // Breaking the tile is half of taking it back; the same Collect that picks up wood
    // walks onto what falls.
    public bool Reclaim(AgentState state, double now)
    {
        _now = now;
        if (_workshop.Due() is not { } owed)
        {
            return false;
        }

        // Every decision about a station is journaled, or a left station is a mystery.
        if (owed.Idle == Workshop.Grace)
        {
            Log.Event("station", $"taking the {Lang.GetItemNameValue(owed.ItemID)} back up",
                ("at", $"({owed.At.X}, {owed.At.Y})"),
                ("type", _belief.TypeAt(owed.At.X, owed.At.Y)),
                ("down", _workshop.Down.Count));
        }

        // On the floor already: walk onto it. Terraria does the rest on contact.
        if (_executor.Collect(state, owed.ItemID))
        {
            state.Behaviour = $"collecting the {Lang.GetItemNameValue(owed.ItemID)} back";
            return true;
        }

        // The tile is gone and nothing of it lies about: no longer owed.
        if (_belief.TypeAt(owed.At.X, owed.At.Y) != owed.TileID)
        {
            Log.Event("station", $"the {Lang.GetItemNameValue(owed.ItemID)} is not where it was put",
                ("at", $"({owed.At.X}, {owed.At.Y})"),
                ("type", _belief.TypeAt(owed.At.X, owed.At.Y)),
                ("carrying", _character.Inventory.Carrying(owed.ItemID)));
            _workshop.Taken(owed);
            return false;
        }

        if (!_character.Hand.HoldPickaxe())
        {
            Log.Event("station", $"no pickaxe to take the {Lang.GetItemNameValue(owed.ItemID)} back up");
            _workshop.Leave(owed);
            return false;
        }

        if (!_character.Hand.InReach(owed.At.X, owed.At.Y))
        {
            _pilot.Approach(_belief, _character, state, owed.At, now);
            if (state.Stuck)
            {
                Log.Event("station", $"no route back to the {Lang.GetItemNameValue(owed.ItemID)}",
                    ("at", $"({owed.At.X}, {owed.At.Y})"));
                _workshop.Leave(owed);
                return false;
            }

            state.Behaviour = $"going back for the {Lang.GetItemNameValue(owed.ItemID)}";
            return true;
        }

        // Swinging at it has a budget: this branch runs before the objective, and a
        // bench the pickaxe never breaks would otherwise hold the whole run.
        if (owed.At != _reclaiming)
        {
            _reclaiming = owed.At;
            _reclaimingSince = now;
        }
        else if (now - _reclaimingSince > ReclaimSeconds)
        {
            Log.Sample("station",
                $"could not take the {Lang.GetItemNameValue(owed.ItemID)} back up at ({owed.At.X}, {owed.At.Y})",
                ("held", _character.Hand.HeldName),
                ("aimWanted", _character.Hand.Intended?.ToString() ?? ""),
                ("aimActual", Hand.Actual.ToString()),
                ("inRange", _character.Hand.GameSaysInRange),
                ("blocked", _character.Hand.Blocked),
                ("type", _belief.TypeAt(owed.At.X, owed.At.Y)));
            _workshop.Leave(owed);
            _reclaiming = default;
            return false;
        }

        state.Digs.Add(owed.At);
        _character.Hand.Aim(owed.At.X, owed.At.Y);
        _character.Hand.Use();
        state.Behaviour = _character.Hand.Blocked
            ? "holding the swing until the game aims where we do"
            : $"taking the {Lang.GetItemNameValue(owed.ItemID)} back up";
        return true;
    }
}
