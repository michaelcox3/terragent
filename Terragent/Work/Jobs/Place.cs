using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Putting a station down so that things can be made at it.</summary>
// A bench in the bag is not a bench to work at, and everything after the first craft waits
// on this. It is the one job whose output is a tile existing rather than an item arriving.
//
// Making room is part of it. Underground there is rarely two clear cells with floor under
// both, and a job that could only put a bench on ground that already suited it stopped
// offering anything the moment the run went down a shaft.
//
// Not to be confused with the blocks a route puts down. Those are booked by the search and
// laid by the pilot, because a bridge is part of getting somewhere. This is work.
internal sealed class Place(
    ITerrain terrain,
    IInventory bag,
    IHand hand,
    IJournal journal,
    int itemID,
    int tileID) : IJob
{
    private readonly ITerrain _terrain = terrain;
    private readonly IInventory _bag = bag;
    private readonly IHand _hand = hand;

    public string Label => $"Place {Names.Item(itemID)}";

    /// <summary>Done when the game agrees the station is in reach.</summary>
    // Its own answer, not whether a tile appeared: standing beside a bench is what makes a
    // recipe available, and the game is what decides that.
    public bool Done => _bag.NearStation(tileID);

    /// <summary>Still worth being here while this spot could still be made to take one.</summary>
    // Could be, not already is. The first swings of the job are what make it fit, and a
    // test that asked whether it fits now would drop the job on the tick it started.
    public bool Workable(ITarget target) =>
        target.Tile is { } tile && Standing(Above(tile)) is not null;

    public Offer? Nearest(Point from)
    {
        if (Placement.Find(_terrain, tileID, from, _bag.PickPower, _bag.Blocks) is not
            { } spot)
        {
            // Said out loud, because an offer that is never made is invisible: the job
            // simply stops appearing among the candidates and the run goes quiet for no
            // stated reason.
            journal.Change("nowhere", $"{Names.Item(itemID)} has nowhere near "
                + $"({from.X}, {from.Y}) it could be stood, with a pickaxe of "
                + $"{_bag.PickPower} and {_bag.Blocks} blocks");
            return null;
        }

        // The floor tile, since that is what the body stands on and what the target names.
        Point at = new(spot.At.X, spot.At.Y + 1);

        // Digging reach when there is digging to do, which is the tighter box. Arriving at
        // placing range and then having to break something leaves the body swinging at a
        // cell it cannot touch.
        return new Offer(
            new TileTarget(at),
            new Destination(at)
            {
                Arrived = spot.Ready
                    ? footing => _hand.CanPlaceFrom(footing, spot.At.X, spot.At.Y)
                    : footing => _hand.CanUseFrom(footing, spot.At.X, spot.At.Y),
            });
    }

    public void Work(ITarget target)
    {
        if (target.Tile is not { } site)
        {
            return;
        }

        // The tile goes above the floor, which is where the body is standing on it.
        Point put = Above(site);
        if (Standing(put) is not { } spot)
        {
            return;
        }

        // One cell a tick, read off the ground every tick rather than remembered. A list
        // worked down from memory swings at cells the last swing already opened.
        if (spot.Clear.Count > 0)
        {
            Swing(_bag.Pickaxe, spot.Clear[0]);
            return;
        }

        if (spot.Fill.Count > 0)
        {
            Swing(_bag.Block, spot.Fill[0]);
            return;
        }

        Swing(itemID, put);

        // Everything the game could be objecting to, in one line, written only when it
        // changes. A placement that is refused says nothing at all by itself: the swing
        // goes out and no tile appears.
        journal.Change("placing", $"{Names.Item(itemID)} at ({put.X}, {put.Y}): "
            + $"holding {Names.Item(_hand.Held)}, "
            + $"in reach {_hand.InPlaceReach(put.X, put.Y)}, "
            + $"hand blocked {_hand.Blocked}, "
            + $"tile there {_terrain.KindAt(put.X, put.Y)}, "
            + $"floor under {_terrain.KindAt(site.X, site.Y)}, "
            + $"covers {Placement.Covers(tileID, put)}, "
            + $"at station {_bag.NearStation(tileID)}");
    }

    /// <summary>What standing one at this cell would still take.</summary>
    private Spot? Standing(Point put) =>
        Placement.Needs(_terrain, tileID, put, _bag.PickPower);

    /// <summary>Hold a thing and press use on a cell, which is every swing this job makes.</summary>
    // Held every tick, not once. A torch raised for light in between leaves the wrong
    // thing in hand, and the swing then goes out with a pickaxe where a bench was meant.
    private void Swing(int held, Point cell)
    {
        _bag.Hold(held);
        _hand.Aim(cell.X, cell.Y);
        _hand.Use();
    }

    /// <summary>The cell a station put on this floor tile would start in.</summary>
    private static Point Above(Point floor) => new(floor.X, floor.Y - 1);
}
