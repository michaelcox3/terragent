using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Making something, station and all.</summary>
// Stand a bench up, make the thing at it, take the bench back. One job, because all three
// happen in the same place and because the moment worth taking a station back is the tick
// after the craft, which nothing else is standing there to notice.
//
// Split across three jobs it never worked. Whatever offered the taking back had to guess
// whether the run still wanted the bench, and that guess flips as materials come and go:
// the bench went down, the tree reported one missing again, and the run walked back and
// mined the bench it had laid a second earlier.
//
// It asks the game whether it can craft rather than working it out from the recipe. The
// game is what spends the materials, so anything else is a second opinion that can differ
// about recipe groups or about what a stack of ore is worth.
internal sealed class Craft(
    ITerrain terrain,
    IInventory bag,
    IHand hand,
    IBody body,
    ISites sites,
    IJournal journal,
    int itemID,
    int tileID,
    int stationID,
    int count) : IJob
{
    private readonly ITerrain _terrain = terrain;
    private readonly IInventory _bag = bag;
    private readonly IHand _hand = hand;
    private readonly IBody _body = body;
    private readonly ISites _sites = sites;

    /// <summary>Columns a route may stop short of a station and still count as at it.</summary>
    // Terraria's own reach is wider than it is tall, so the columns and the rows are
    // separate numbers. One number for both stops the body two rows above a bench the game
    // will not let it use, and it then stands there pressing nothing.
    private const int Across = 2;

    /// <summary>Rows a route may stop above or below a station.</summary>
    private const int Below = 1;

    /// <summary>How far a standing station is worth walking to rather than standing up another.</summary>
    // Inside it, walking to the bench that exists is the answer and putting a second one
    // down is litter; outside it, a fresh bench is ten wood against a long walk.
    public const int Reuse = 60;

    public string Label => $"Craft {Names.Item(itemID)}";

    /// <summary>Two crafts of the same thing are one piece of work, however many either wanted.</summary>
    // By the label, which is made of the same thing the work is: the item, or the kinds.
    public bool Equals(IJob? other) => other is Craft same && same.Label == Label;

    public override bool Equals(object? other) => Equals(other as IJob);

    public override int GetHashCode() => System.HashCode.Combine(nameof(Craft), Label);

    /// <summary>Made, and nothing of ours left standing.</summary>
    // Both, because leaving the bench behind is what made the next craft walk back across
    // the clearing for it, or stand up a second one and carry the spare for ever.
    public bool Done => _bag.Carrying(itemID) >= count && Cleared;

    /// <summary>Whether there is no station of ours still up.</summary>
    // In reach and none in the bag is one we put down and have not taken back. One in the
    // bag means it is already ours, whoever laid it.
    private bool Cleared =>
        tileID <= 0 || stationID <= 0 || _bag.Carrying(stationID) > 0
        || !_bag.NearStation(tileID);

    /// <summary>Still worth being here while there is any of it left to do.</summary>
    // The last swing of the job breaks the station, so a test that wanted one standing
    // would end the job one tick before it finished. What ends it is a station that has
    // gone and has not arrived in the bag, which is a bench broken and its drop lost.
    public bool Workable(ITarget target) =>
        tileID <= 0
        || _bag.NearStation(tileID)
        || _bag.Carrying(stationID) > 0
        || Standing(target)
        || Spot(target) is not null;

    /// <summary>Whether the station is up on the spot this job was sent to.</summary>
    // The target names the floor, so the station sits in the cell above it. Asking about
    // the floor itself said no the instant the bench went down, and the job was let go one
    // tick after placing, every time.
    private bool Standing(ITarget target) =>
        target.Tile is { } floor
        && (_terrain.TypeAt(floor.X, floor.Y) == tileID
            || _terrain.TypeAt(floor.X, floor.Y - 1) == tileID);

    public Offer? Nearest(Point from)
    {
        // Nothing to stand at, so here will do. Zero tiles away, and the pilot reports
        // arrival on the first tick.
        if (tileID <= 0)
        {
            return new Offer(TileTarget.Nowhere, new Destination(from, 0));
        }

        // Already in reach, which is every tick after the bench goes down. Placing,
        // crafting and taking it back all happen without moving, which is what lets one
        // job hold all three.
        if (_bag.NearStation(tileID))
        {
            return new Offer(new TileTarget(from), new Destination(from, 0));
        }

        // One in the bag, so the answer is to put it down beside the body rather than to
        // go anywhere. The spot may need digging out or flooring first; arriving at
        // digging reach covers both, since it is the tighter of the two boxes.
        if (_bag.Carrying(stationID) > 0
            && Placement.Find(_terrain, tileID, from, _bag.PickPower, _bag.Blocks) is
                { } spot)
        {
            Point floor = new(spot.At.X, spot.At.Y + 1);
            Rectangle covers = spot.Covers;
            return new Offer(
                new TileTarget(floor),
                new Destination(floor)
                {
                    // In reach and out of the way, which are different questions. A body
                    // that stops inside the cell the bench goes in has arrived at
                    // somewhere it cannot place from, and the swing is refused in silence.
                    Arrived = footing =>
                        _hand.CanUseFrom(footing, spot.At.X, spot.At.Y)
                        && !covers.Intersects(Hitbox.Fills(footing)),
                });
        }

        // One standing near enough to walk to. Bounded, so that this and the standing up
        // of a fresh one are never both right, and because a miss otherwise pays for the
        // whole box every plan.
        if (_sites.Nearest(from, [tileID], Reuse) is { } tile)
        {
            return new Offer(
                new TileTarget(tile),
                new Destination(tile)
                {
                    Arrived = footing => Navigator.Reached(footing, tile, Across, Below),
                });
        }

        return null;
    }

    public void Work(ITarget target)
    {
        // Asked every tick rather than remembered. A craft spends materials, so the second
        // one of a batch is a different question from the first.
        //
        // Only while short of the count. The game will happily go on making torches until
        // the wood runs out, and what is wanted is three.
        if (_bag.Carrying(itemID) < count && _bag.CanCraft(itemID))
        {
            _bag.Craft(itemID);
            return;
        }

        if (tileID > 0 && !_bag.NearStation(tileID))
        {
            Stand(target);
            return;
        }

        // Made, and the bench still up. Taking it back is the last thing the job does, and
        // this tick is the only one that knows the run is finished with it.
        if (_bag.Carrying(itemID) >= count && !Cleared)
        {
            Take();
            return;
        }

        // A refused craft is as silent as a refused placement, and standing at a bench
        // pressing nothing looks exactly like walking to one.
        journal.Change("crafting", $"{Names.Item(itemID)} refused: "
            + $"needs tile {tileID}, at station {tileID <= 0 || _bag.NearStation(tileID)}, "
            + $"carrying {_bag.Carrying(itemID)} of {count}");
    }

    /// <summary>Put the station down, breaking and flooring whatever is in the way first.</summary>
    // One cell a tick, read off the ground every tick rather than remembered. A list
    // worked down from memory swings at cells the last swing already opened.
    private void Stand(ITarget target)
    {
        if (Spot(target) is not { } spot)
        {
            return;
        }

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

        Swing(stationID, spot.At);

        journal.Change("placing", $"{Names.Item(stationID)} at ({spot.At.X}, {spot.At.Y}): "
            + $"holding {Names.Item(_hand.Held)}, "
            + $"in reach {_hand.InPlaceReach(spot.At.X, spot.At.Y)}, "
            + $"hand blocked {_hand.Blocked}, "
            + $"tile there {_terrain.KindAt(spot.At.X, spot.At.Y)}, "
            + $"at station {_bag.NearStation(tileID)}");
    }

    /// <summary>Break the station, which leaves it on the ground at the body's feet.</summary>
    private void Take()
    {
        if (_sites.Nearest(_body.Footing, [tileID], Across + Below) is not { } tile)
        {
            return;
        }

        Swing(_bag.Pickaxe, tile);
    }

    /// <summary>Hold a thing and press use on a cell, which is every swing this job makes.</summary>
    // Held every tick, not once. A torch raised for light in between leaves the wrong
    // thing in hand, and the swing then goes out with a pickaxe where a bench was meant.
    private void Swing(int held, Point cell)
    {
        _bag.Hold(held);
        _hand.Aim(cell.X, cell.Y);
        _hand.Use();
    }

    /// <summary>What standing the station on this target would still take.</summary>
    private Spot? Spot(ITarget target) =>
        target.Tile is { } floor
            ? Placement.Needs(_terrain, tileID, new Point(floor.X, floor.Y - 1),
                _bag.PickPower)
            : null;
}
