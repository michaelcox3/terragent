using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
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
    IInventory bag,
    ISites sites,
    int itemID,
    int tileID,
    int count) : IJob
{
    private readonly IInventory _bag = bag;
    private readonly ISites _sites = sites;

    /// <summary>How close a route has to stop to count as standing at a station.</summary>
    // Terraria decides this itself and rather generously, so the number only has to get
    // the body near enough for the game to agree. Two tiles is what a bench takes up.
    private const int Beside = 2;

    public string Label => $"Craft {Names.Item(itemID)}";

    public bool Done => _bag.Carrying(itemID) >= count;

    /// <summary>Still worth standing here while the game says the station is in reach.</summary>
    // Or while none is needed at all, in which case anywhere is as good as here.
    public bool Workable(ITarget target) => tileID <= 0 || _bag.NearStation(tileID);

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

        if (_sites.Nearest(from, [tileID]) is not { } tile)
        {
            return null;
        }

        return new Offer(new TileTarget(tile), new Destination(tile, Beside));
    }

    public void Work(ITarget target)
    {
        // Asked every tick rather than remembered. A craft spends materials, so the second
        // one of a batch is a different question from the first.
        if (_bag.CanCraft(itemID))
        {
            _bag.Craft(itemID);
        }
    }

}
