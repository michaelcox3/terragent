using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent.Do;

/// <summary>
/// Going to look when nothing wanted has been seen.
/// </summary>
// Digging down to a depth, and searching a row or a band for it. What is decided here
// is where to head, never how to get there: every waypoint goes through the one Pilot.
internal sealed class Exploring(Pilot pilot, Belief belief, Character character)
{
    private readonly Pilot _pilot = pilot;

    private readonly Belief _belief = belief;

    private readonly Character _character = character;

    private double _now;


    /// <summary>How far down to plan at once when travelling to a depth.</summary>
    // A descent costs a mine per row while the heuristic charges a walk, so A* spends
    // its budget wandering the cheap explored surface. On a flat surface the search
    // finds 24 rows and fails at 28; hills and caves fan out further, so keep margin.
    private const int Stage = 16;

    /// <summary>Columns a descent will consider, either side of straight down.</summary>
    // Wide enough to step round a flooded cavern or an unbreakable wall, narrow enough
    // that the shaft is still a shaft. Nearest first, so the middle wins when it is fine.
    private static readonly int[] Spread = [0, -6, 6, -14, 14, -24, 24];

    private readonly List<Point> _stages = [];

    /// <summary>The stage of a descent being walked, and the depth it serves.</summary>
    private Point _stage;

    private int _stageFor = int.MinValue;

    /// <summary>How far to look sideways once the right band is reached.</summary>
    private const int ProspectWidth = 120;

    /// <summary>How far ahead along the band to aim while prospecting.</summary>
    private const int ProspectLead = 12;

    private int _searchFrom;

    /// <summary>Rows off the prospecting row within which the row search begins.</summary>
    private const int ProspectArrive = 1;

    /// <summary>Whether the row search is in charge, until the body is well off the row.</summary>
    private bool _onRow;

    /// <summary>Rows either side of the prospecting row that still count as on it.</summary>
    // More than the six rows a jump rises, so a route along the row that climbs a hill
    // does not hand the job to GetToRow at the top of the climb.
    private const int ProspectSlack = 8;

    /// <summary>The row being prospected, and for which tile, until the body leaves it.</summary>
    private int _prospectTile;

    private int? _prospectRow;

    /// <summary>The point the row search is walking toward, until it gets there.</summary>
    private Point? _ahead;

    private int _searchDirection = 1;

    /// <summary>
    /// Take down whatever the game is protecting a tile with. True when that is the
    /// job this tick.
    /// </summary>
    public bool Clear(AgentState state, Point target)
    {
        if (_belief.Protector(target.X, target.Y) is not { } above)
        {
            return false;
        }

        state.Digs.Add(above);
        bool axe = _belief.TypeAt(above.X, above.Y) is TileID.Trees or TileID.PalmTree;
        if (!(axe ? _character.Hand.HoldAxe() : _character.Hand.HoldPickaxe()))
        {
            state.Behaviour = $"nothing to clear ({above.X}, {above.Y}) with";
            state.Stuck = true;
            return true;
        }

        _character.Hand.Aim(above.X, above.Y);
        _character.Hand.Use();
        state.Behaviour = _character.Hand.Blocked
            ? "holding the swing until the game aims where we do"
            : $"clearing ({above.X}, {above.Y}) off ({target.X}, {target.Y}), "
              + "which the game will not break while it is there";
        return true;
    }

    /// <summary>
    /// Get the agent to a given row, above or below.
    /// </summary>
    public void GetToRow(AgentState state, int wantedItemID,
        Point at, string why, double now)
    {
        _now = now;
        // A stage at a time (see Stage), held until reached. Recomputed from the body
        // each tick, the goal moves with every row gained, and Pilot.Stale replans on
        // every goal change, so the route was never followed.
        bool reached = System.Math.Abs(_stage.Y - at.Y) <= 1;
        bool behind = System.Math.Sign(_stage.Y - at.Y) != System.Math.Sign(wantedItemID - at.Y);
        if (_stageFor != wantedItemID || reached || behind)
        {
            _stageFor = wantedItemID;
            _stage = new Point(at.X, at.Y + (System.Math.Sign(wantedItemID - at.Y)
                * System.Math.Min(Stage, System.Math.Abs(wantedItemID - at.Y))));

            // A row to reach, not a hole to be at the bottom of. Every column across the
            // spread goes into one search, so the shaft shifts sideways round a flooded
            // cavern instead of retrying the same column for the rest of the run.
            _stages.Clear();
            foreach (int across in Spread)
            {
                _stages.Add(new Point(at.X + across, _stage.Y));
            }
        }

        state.Goal = new Point(at.X, wantedItemID);
        if (!_pilot.Advance(_belief, _character, state, _stages, _now,
                arriveWithinTiles: 1))
        {
            // Pick a fresh stage next time: this column is not the way down.
            _stageFor = int.MinValue;

            // Or, not assign: a plain assignment cleared a stuck the pilot had already
            // declared, and the objective was never skipped.
            state.Stuck |= _pilot.Unreachable;
            state.Behaviour = $"cannot reach row {_stage.Y} on the way to {wantedItemID} for {why}: "
                        + _pilot.Behaviour;
            return;
        }

        state.Behaviour = $"getting to row {wantedItemID} for {why}: at {at.Y}, " + _pilot.Behaviour;
    }

    /// <summary>
    /// Go to where this resource lives, and look there.
    /// </summary>
    public void Explore(AgentState state,
        Source source, Point at, double now)
    {
        // Taken from the caller, never from the field: reading back the clock GetToRow
        // stored froze it, and a frozen clock made every route look freshly planned.
        _now = now;

        if (Tiles.NeedsAxe(source.TileID))
        {
            // Trees are on the surface. Digging for one is never the answer.
            state.Behaviour = "no trees on the map near here";
            state.Stuck = true;
            return;
        }

        // Chosen once and kept while the body is near it. Asked afresh every tick, the
        // row moves with the body, and pillaring over a step drops the agent out of
        // the band and back in again for ever.
        int wanted;
        if (_prospectTile == source.TileID && _prospectRow is { } kept
            && System.Math.Abs(at.Y - kept) <= ProspectSlack)
        {
            wanted = kept;
        }
        else if (OreDepths.BestRow(source.TileID, at.Y) is { } fresh)
        {
            wanted = fresh;
            _prospectTile = source.TileID;
            _prospectRow = fresh;
            _searchFrom = 0;
            _ahead = null;
            _onRow = false;
        }
        else
        {
            state.Behaviour = $"worldgen does not place {TileID.Search.GetName(source.TileID)} anywhere";
            state.Stuck = true;
            return;
        }

        // At the band, walking is the search: ore is scattered along it, and each tile
        // walked reveals a column of map for a fraction of what digging costs.
        //
        // Two thresholds, not one. The row search begins only once the body is on the
        // row and ends only once it is well off it; with one number for both, the two
        // modes routed differently from footings either side of it and undid each
        // other every step. A route in hand also keeps the row search in charge: the
        // route can climb nine rows round a rock face, and the body's row mid-route is
        // not a reason to reconsider it.
        int off = System.Math.Abs(at.Y - wanted);
        _onRow = _onRow
            ? off <= ProspectSlack || _pilot.Next is not null
            : off <= ProspectArrive;
        if (_onRow)
        {
            if (_searchFrom == 0)
            {
                _searchFrom = at.X;
            }

            if (System.Math.Abs(at.X - _searchFrom) > ProspectWidth)
            {
                state.Behaviour = $"searched {ProspectWidth} tiles at row {at.Y} and found none";
                state.Stuck = true;
                return;
            }

            // Routed, not a held key, so a wall or a pool in the band is walked round.
            // The point aimed at is kept until reached and sits on the prospecting row,
            // not the body's: recomputed from the body each tick it climbed a row with
            // every step up a hillside, and every route began by stepping back down.
            if (_ahead is not { } ahead || System.Math.Abs(ahead.X - at.X) <= 2
                || System.Math.Sign(ahead.X - at.X) != _searchDirection)
            {
                ahead = new Point(at.X + (_searchDirection * ProspectLead), wanted);
                _ahead = ahead;
            }

            state.Goal = ahead;
            if (!_pilot.Advance(_belief, _character, state, ahead, _now,
                    arriveWithinTiles: 1))
            {
                // Nothing that way. Turning round beats stopping.
                _searchDirection = -_searchDirection;
                _ahead = null;
                _pilot.Forget();
                state.Behaviour = $"row {at.Y} is blocked; searching the other way";
                return;
            }

            state.Behaviour = $"searching row {at.Y} for {Sought(source.TileID)}, "
                        + $"{System.Math.Abs(at.X - _searchFrom)} tiles along: "
                        + _pilot.Behaviour;
            return;
        }

        // Above or below alike: a band overhead is reachable now.
        GetToRow(state, wanted, at, Sought(source.TileID), _now);
    }

    /// <summary>What a tile is being looked for as: its item for an ore, else itself.</summary>
    // ItemFrom answers None for a pot, which read as "getting to row 345 for :".
    private static string Sought(int tileID) =>
        Tiles.ItemFrom(tileID) is var item && item != ItemID.None
            ? Lang.GetItemNameValue(item)
            : TileID.Search.GetName(tileID);

}
