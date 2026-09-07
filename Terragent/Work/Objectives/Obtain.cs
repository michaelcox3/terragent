using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Report;
using Terragent.Work.Jobs;
using Terragent.World;

namespace Terragent.Work.Objectives;

/// <summary>Having a number of something, which is most of what a run is.</summary>
// A hundred wood, one iron pickaxe, five bars. Met by looking in the bag, so it comes true
// the moment a chest hands one over and the run walks past the errand it was about to do.
//
// Several items rather than one, because a world grows silver or tungsten and never both.
// Each gets its own tree and its own jobs, and whichever has work nearby wins, so a lead
// world quietly stops offering iron.
//
// No memory of its own. The progression latches it, because reached once is reached and
// spending the wood on the bench it was for must not undo the gathering.
internal sealed class Obtain(
    string label,
    IRecipeTree recipes,
    ITerrain terrain,
    IInventory bag,
    IHand hand,
    IBody body,
    ISites sites,
    IDrops drops,
    ICreatures creatures,
    IClock clock,
    IJournal journal,
    IReadOnlyList<int> items,
    int count) : IObjective
{
    /// <summary>How far down a tree to expand when planning.</summary>
    // Deeper than the two the walk uses to judge a recipe followable. Ore to bar to anvil
    // to pickaxe is four, and the bench under the furnace is deeper still.
    private const int Deep = 6;

    public string Label => label;

    public bool Met => bag.Carrying(items) >= count;

    /// <summary>Everything that would move this along right now.</summary>
    // The whole tree becomes work in one pass: what is still missing turns into trips out
    // for it, and when nothing is missing the next thing that can actually be made turns
    // into a craft. Which of them the run does is decided by distance, above.
    /// <summary>What the whole tree still comes down to, with the bag taken off.</summary>
    // The first of the items, since the panel wants one answer and the others are the same
    // thing in another metal.
    public IReadOnlyDictionary<int, int> Missing() =>
        items.Count == 0
            ? new Dictionary<int, int>()
            : RecipeTree.Missing(recipes.Of(items[0], Deep), count, bag.Carrying,
                bag.NearStation);

    public IReadOnlyList<IJob> Jobs()
    {
        List<IJob> jobs = [];

        foreach (int item in items)
        {
            Need tree = recipes.Of(item, Deep);
            IReadOnlyDictionary<int, int> missing =
                RecipeTree.Missing(tree, count, bag.Carrying, bag.NearStation);

            foreach (KeyValuePair<int, int> want in missing)
            {
                Trips(jobs, want.Key, bag.Carrying(want.Key) + want.Value);
            }

            if (missing.Count == 0
                && RecipeTree.Craftable(tree, count, bag.Carrying, bag.NearStation) is
                    { } next)
            {
                Make(jobs, next.Node, next.Wanted);
            }

            Looking(jobs, missing);
        }

        return jobs;
    }

    /// <summary>Going to look, when what is wanted might simply not have been seen.</summary>
    // Always offered and always far away, so it never wins against a job with a site
    // nearby and always wins when none of them has one. That is the whole of the rule for
    // when to explore, and it needs no state to say it.
    private void Looking(List<IJob> jobs, IReadOnlyDictionary<int, int> missing)
    {
        List<int> tiles = [];
        List<int> types = [];
        List<string> breakable = [];
        List<string> huntable = [];
        foreach (KeyValuePair<int, int> want in missing)
        {
            IReadOnlyList<int> yields = Mining.Yields(want.Key);
            IReadOnlyList<int> droppers = Loot.Droppers(want.Key);
            tiles.AddRange(yields);
            types.AddRange(droppers);

            if (yields.Count > 0)
            {
                breakable.Add(Names.Item(want.Key));
            }

            if (droppers.Count > 0)
            {
                huntable.Add(Names.Item(want.Key));
            }
        }

        // Two different searches wearing one name until now. Revealing map is what finds a
        // tree, and being in the right band is what finds a slime, and neither does the
        // other's job.
        if (tiles.Count > 0)
        {
            jobs.Add(new Explore(terrain, sites, body, tiles, string.Join(", ", breakable)));
        }

        if (types.Count > 0)
        {
            jobs.Add(new Hunt(terrain, creatures, clock, body, types,
                string.Join(", ", huntable)));
        }
    }

    /// <summary>The ways of getting one raw thing, as jobs.</summary>
    // All of them, not the likeliest. A dropped one lying about beats mining for another,
    // and which is nearer is not this objective's to know.
    private void Trips(List<IJob> jobs, int itemID, int target)
    {
        // One job per way of getting it, built from the tables and nothing else. Whether
        // there is a slime on screen or wood on the ground is not asked here: a job with
        // nothing to work answers with no site, and the ranking drops it. Asking now would
        // mean the run only ever hunts what happened to be in view when it last chose.
        jobs.Add(new Pickup(bag, drops, itemID, target));

        IReadOnlyList<int> tiles = Mining.Yields(itemID);
        if (tiles.Count > 0)
        {
            jobs.Add(new Gather(terrain, bag, hand, sites, itemID, tiles, target));
        }

        // Both, not one or the other. Gel comes off two dozen kinds of creature and
        // nothing else, and stone comes out of the ground and off a few of them.
        IReadOnlyList<int> droppers = Loot.Droppers(itemID);
        if (droppers.Count > 0)
        {
            jobs.Add(new Fight(creatures, bag, hand, journal, droppers));
        }
    }

    /// <summary>Making the next thing, or standing up the station it is made at.</summary>
    // The station comes first and is two jobs, not one: a bench has to be crafted and then
    // put down, and the game only counts the second.
    private void Make(List<IJob> jobs, Need node, int wanted)
    {
        foreach ((Need station, int tileID) in node.Stations)
        {
            if (bag.NearStation(tileID))
            {
                continue;
            }

            jobs.Add(bag.Carrying(station.ItemID) > 0
                ? new Place(terrain, bag, hand, journal, station.ItemID, tileID)
                : new Craft(bag, sites, station.ItemID, 0, 1));
            return;
        }

        jobs.Add(new Craft(bag, sites, node.ItemID, Station(node),
            bag.Carrying(node.ItemID) + wanted));
    }

    /// <summary>The tile this is made at, or zero when it takes none.</summary>
    private static int Station(Need node)
    {
        foreach ((Need _, int tileID) in node.Stations)
        {
            return tileID;
        }

        return 0;
    }
}
