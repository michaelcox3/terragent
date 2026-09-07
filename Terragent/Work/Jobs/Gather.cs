using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.World;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>Breaking tiles for what they leave behind.</summary>
// Chopping a tree and mining stone are one job: stand in reach of a tile that yields the
// thing, hold whatever breaks it, and swing until enough is carried.
//
// It never decides where to stand. The site is the tile, the arrival radius is the tool's
// own reach, and getting there belongs to the pilot.
internal sealed class Gather(
    ITerrain terrain,
    IInventory bag,
    IHand hand,
    ISites sites,
    int itemID,
    IReadOnlyList<int> tiles,
    int count) : IJob
{
    private readonly ITerrain _terrain = terrain;
    private readonly IInventory _bag = bag;
    private readonly IHand _hand = hand;
    private readonly ISites _sites = sites;

    public string Label => $"Gather {Names.Item(itemID)}";

    public bool Done => _bag.Carrying(itemID) >= count;

    public bool Workable(ITarget target) =>
        target.Tile is { } tile && Wanted(_terrain.TypeAt(tile.X, tile.Y));

    public Offer? Nearest(Point from)
    {
        if (_sites.Nearest(from, tiles) is not { } tile)
        {
            return null;
        }

        // A tree is felled at its foot, and what was found is as likely to be a branch.
        // Anything else is broken where it stands.
        tile = Mining.NeedsAxe(_terrain.TypeAt(tile.X, tile.Y))
            ? Mining.TreeBase(_terrain, tile) ?? tile
            : tile;

        // Arrival is the tool's own reach box, asked of each footing the search considers.
        // A radius stands in for that box wrongly in one direction whichever number is
        // picked, and the route then ends outside swinging distance with the body on a
        // ledge above the stone pressing nothing.
        Point at = tile;
        return new Offer(
            new TileTarget(at),
            new Destination(at)
            {
                Arrived = footing => _hand.CanUseFrom(footing, at.X, at.Y),
            });
    }

    public void Work(ITarget target)
    {
        if (target.Tile is not { } tile)
        {
            return;
        }

        // Hold first, every tick. The bag is what fetches a tool onto the hotbar, and a
        // torch raised for light in between leaves the wrong thing in hand.
        _bag.Hold(Tool(tile));
        _hand.Aim(tile.X, tile.Y);
        _hand.Use();
    }

    /// <summary>Whether this is one of the tiles this job breaks.</summary>
    private bool Wanted(int tileID)
    {
        for (int n = 0; n < tiles.Count; n++)
        {
            if (tiles[n] == tileID)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What breaks the tile at this site.</summary>
    // Asked of the tile rather than assumed from the job: wood comes off a tree with an
    // axe and stone comes out of the ground with a pickaxe, and both are this job.
    private int Tool(Point site) =>
        Mining.NeedsAxe(_terrain.TypeAt(site.X, site.Y)) ? _bag.Axe : _bag.Pickaxe;
}
