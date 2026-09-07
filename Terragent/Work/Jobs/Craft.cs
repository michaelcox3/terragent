using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Standing at a station and making something.</summary>
// The station is the site. Most recipes want one, and the ones that do not are made where
// the body already stands, which is a site of zero tiles away rather than a special case.
//
// It asks the game whether it can craft rather than working it out from the recipe. The
// game is what spends the materials, so anything else is a second opinion that can differ
// about recipe groups or about what a stack of ore is worth.
internal sealed class Craft(
    ITerrain terrain,
    IInventory bag,
    ISites sites,
    IJournal journal,
    int itemID,
    int tileID,
    int count) : IJob
{
    private readonly ITerrain _terrain = terrain;
    private readonly IInventory _bag = bag;
    private readonly ISites _sites = sites;

    /// <summary>Columns a route may stop short of a station and still count as at it.</summary>
    // Terraria's own reach is wider than it is tall, so the columns and the rows are
    // separate numbers. One number for both stops the body two rows above a bench the game
    // will not let it use, and it then stands there pressing nothing.
    //
    // Both are inside what the game actually allows, so arriving means the game agrees.
    // Being conservative costs a step or two of walking; being generous costs the job.
    private const int Across = 2;

    /// <summary>Rows a route may stop above or below a station.</summary>
    private const int Below = 1;

    /// <summary>How far a standing station is worth walking to rather than standing up another.</summary>
    // The one number that keeps this job and the placing of a station from both being
    // right at once. Inside it, walking to the bench that exists is the answer and putting
    // a second one down is litter; outside it, a fresh bench is ten wood against a long
    // walk and the fresh bench wins.
    //
    // One number for every station, which understates an anvil: five iron bars is worth
    // walking a good deal further for than ten wood. Split it when that starts to show.
    public const int Reuse = 60;

    public string Label => $"Craft {Names.Item(itemID)}";

    public bool Done => _bag.Carrying(itemID) >= count;

    /// <summary>Still worth going while the station is in reach or still standing.</summary>
    // Not "in reach" alone. A craft that means to walk to a bench across the clearing is
    // not at one yet, and answering no there ends the job on the tick it was chosen: the
    // bench already down thirty tiles away was invisible, and the run put another one
    // beside itself or stalled when there was nowhere to.
    public bool Workable(ITarget target) =>
        tileID <= 0
        || _bag.NearStation(tileID)
        || (target.Tile is { } tile && _terrain.TypeAt(tile.X, tile.Y) == tileID);

    public Offer? Nearest(Point from)
    {
        if (tileID <= 0)
        {
            // Nothing to stand at, so here will do. Zero tiles away, and the pilot reports
            // arrival on the first tick.
            return new Offer(TileTarget.Nowhere, new Destination(from, 0));
        }

        // Already in reach, which is the common case once a bench is down beside the run.
        if (_bag.NearStation(tileID))
        {
            return new Offer(new TileTarget(from), new Destination(from, 0));
        }

        // Bounded, so that this and the standing up of a station are never both right.
        // Unbounded it also pays for the whole box every time it misses, which on a fresh
        // world is every plan.
        if (_sites.Nearest(from, [tileID], Reuse) is not { } tile)
        {
            return null;
        }

        return new Offer(
            new TileTarget(tile),
            new Destination(tile)
            {
                Arrived = footing => Navigator.Reached(footing, tile, Across, Below),
            });
    }

    public void Work(ITarget target)
    {
        // Asked every tick rather than remembered. A craft spends materials, so the second
        // one of a batch is a different question from the first.
        if (_bag.CanCraft(itemID))
        {
            _bag.Craft(itemID);
            return;
        }

        // A refused craft is as silent as a refused placement, and standing at a bench
        // pressing nothing looks exactly like walking to one.
        journal.Change("crafting", $"{Names.Item(itemID)} refused: "
            + $"needs tile {tileID}, at station {tileID <= 0 || _bag.NearStation(tileID)}, "
            + $"carrying {_bag.Carrying(itemID)} of {count}");
    }

}
