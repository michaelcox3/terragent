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

    /// <summary>How far to look for somewhere to stand it.</summary>
    // Close, because the point is to put it where the run already is. A bench eight tiles
    // away is a walk, and the walk is what this job exists to avoid repeating.
    private const int Nearby = 8;

    /// <summary>Rows to try either side of the body's own, nearest first.</summary>
    // Not the body's row alone. Mining stone walks the body down a shaft of its own
    // digging, and along that one row there is rock to both sides for ever: the job then
    // offers nothing and the run goes quiet with a bench in the bag. Up before down,
    // since climbing out of the hole is what is wanted.
    private static readonly int[] Rows = [0, -1, 1, -2, 2, -3, 3];

    public string Label => $"Place {Names.Item(itemID)}";

    /// <summary>Done when the game agrees the station is in reach.</summary>
    // Its own answer, not whether a tile appeared: standing beside a bench is what makes a
    // recipe available, and the game is what decides that.
    public bool Done => _bag.NearStation(tileID);

    /// <summary>Still worth being here while the whole bench still fits on this spot.</summary>
    public bool Workable(ITarget target) =>
        target.Tile is { } tile && Placement.Fits(_terrain, tileID, Above(tile));

    public Offer? Nearest(Point from)
    {
        for (int ring = 1; ring <= Nearby; ring++)
        {
            for (int across = -ring; across <= ring; across += ring * 2)
            {
                foreach (int down in Rows)
                {
                    Point at = new(from.X + across, from.Y + down);
                    if (!Placement.Fits(_terrain, tileID, Above(at)))
                    {
                        continue;
                    }

                    return new Offer(
                        new TileTarget(at),
                        new Destination(at)
                        {
                            Arrived = footing =>
                                _hand.CanPlaceFrom(footing, Above(at).X, Above(at).Y),
                        });
                }
            }
        }

        // Said out loud, because an offer that is never made is invisible: the job simply
        // stops appearing among the candidates and the run goes quiet for no stated reason.
        journal.Change("nowhere", $"{Names.Item(itemID)} has no spot within {Nearby} of "
            + $"({from.X}, {from.Y}) that it fits on");
        return null;
    }

    public void Work(ITarget target)
    {
        _bag.Hold(itemID);

        if (target.Tile is not { } site)
        {
            return;
        }

        // The tile goes above the floor, which is where the body is standing on it.
        Point put = Above(site);
        _hand.Aim(put.X, put.Y);
        _hand.Use();

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
            + $"fits {Placement.Fits(_terrain, tileID, put)}, "
            + $"at station {_bag.NearStation(tileID)}");
    }

    /// <summary>The cell a station put on this floor tile would start in.</summary>
    private static Point Above(Point floor) => new(floor.X, floor.Y - 1);
}
