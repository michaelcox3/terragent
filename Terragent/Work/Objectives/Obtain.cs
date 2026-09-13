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
// Two levels, because both questions have to be asked. All of what it wants must be had,
// and any one of a want's own items will do: gold armour is a helmet and a chainmail and a
// greaves, and each of those is the gold one or the platinum one. One level could say only
// one of those, and a run after silver in a tungsten world needs the second.
//
// Which outcomes count, not which materials will do. That second question is the recipe
// group's, and an iron pickaxe is craftable out of lead bars whether or not a lead pickaxe
// is named here. Naming it says a lead one found in a chest would also do.
//
// No memory of its own. The progression latches it, because reached once is reached and
// spending the wood on the bench it was for must not undo the gathering.
internal sealed class Obtain(
    string key,
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
    IReadOnlyList<NeededItem> all,
    IReadOnlyDictionary<int, IReadOnlyList<int>> stations) : IObjective
{
    /// <summary>How far down a tree to expand when planning.</summary>
    // Deeper than the two the walk uses to judge a recipe followable. Ore to bar to anvil
    // to pickaxe is four, and the bench under the furnace is deeper still.
    public const int Deep = 6;

    public string Key => key;

    public string Label => label;

    /// <summary>Every one of them had, not any one.</summary>
    public bool Met
    {
        get
        {
            foreach (NeededItem want in all)
            {
                if (bag.Carrying(Any(want)) < want.Count)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The items of a want, which any one of will do.</summary>
    private static IReadOnlyList<int> Any(NeededItem want)
    {
        List<int> items = [];
        foreach ((int itemID, int _) in want.Options)
        {
            items.Add(itemID);
        }

        return items;
    }

    /// <summary>What the whole objective still comes down to, with the bag taken off.</summary>
    public IReadOnlyList<NeededItem> NeededItems() =>
        RecipeTree.Missing(Roots(), bag.Carrying, Available().Contains);

    /// <summary>Every want as a tree to walk, with the rest of its items as stand-ins.</summary>
    // The first item is walked and the others stand in for it, which is the same thing a
    // recipe group means and now goes through the same machinery. Walked separately and
    // reconciled afterwards, a gold helmet and a platinum one were two answers that had to
    // be matched back up by name.
    private List<(RecipeNode Node, int Wanted, IReadOnlyList<RecipeNode> Instead)> Roots()
    {
        List<(RecipeNode, int, IReadOnlyList<RecipeNode>)> roots = [];
        foreach (NeededItem want in all)
        {
            List<RecipeNode> instead = [];
            for (int n = 1; n < want.Options.Count; n++)
            {
                instead.Add(recipes.Of(want.Options[n].ItemID, Deep));
            }

            roots.Add((recipes.Of(want.ItemID, Deep), want.Count, instead));
        }

        return roots;
    }

    public IReadOnlyList<IJob> Jobs()
    {
        List<IJob> jobs = [];

        // Worked out once. A station is not a thing the body is beside, it is a thing the
        // run can use, and asking the ground for each is a sweep that has no business
        // running per node of a recipe tree.
        HashSet<int> available = Available();

        // One walk for the whole objective, so six pieces of armour charge one anvil
        // between them and spend the bag once.
        IReadOnlyList<NeededItem> missing =
            RecipeTree.Missing(Roots(), bag.Carrying, available.Contains);

        foreach (NeededItem want in missing)
        {
            Trips(jobs, want);
        }

        Looking(jobs, missing);

        // Crafting stays per want. The walk says what is short across the lot; what to
        // make next is the deepest thing one tree can make now, and two trees can each
        // have one.
        if (missing.Count == 0)
        {
            foreach (NeededItem asked in all)
            {
                foreach ((int itemID, int owed) in asked.Options)
                {
                    if (RecipeTree.Craftable(recipes.Of(itemID, Deep), owed, bag.Carrying,
                        available.Contains) is { } next)
                    {
                        Make(jobs, next.Node, next.Wanted);
                        break;
                    }
                }
            }
        }

        return jobs;
    }

    /// <summary>Stations the run could work at without standing another one up.</summary>
    // In reach, in the bag, or standing near enough to walk to. Asking only whether the
    // body is beside one makes putting a bench down the same as losing it: the item leaves
    // the bag, the tree says a work bench is missing again, and the run goes back and
    // mines the bench it laid a second ago. Place, gather, pick up, place, for ever.
    private HashSet<int> Available()
    {
        HashSet<int> ready = [];
        foreach (KeyValuePair<int, IReadOnlyList<int>> station in stations)
        {
            // Any of the things that stand the tile up will do, since an anvil is the iron
            // one or the lead one and a bench is any wood there is.
            if (bag.NearStation(station.Key)
                || Held(station.Value)
                || sites.Nearest(body.Footing, [station.Key], Craft.Reuse) is not null)
            {
                ready.Add(station.Key);
            }
        }

        return ready;
    }

    /// <summary>Whether this list holds an item, without pulling in Linq for it.</summary>
    private static bool Lists(IReadOnlyList<int> items, int itemID)
    {
        for (int n = 0; n < items.Count; n++)
        {
            if (items[n] == itemID)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any of these is in the bag.</summary>
    private bool Held(IReadOnlyList<int> items)
    {
        foreach (int itemID in items)
        {
            if (bag.Carrying(itemID) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Going to look, when what is wanted might simply not have been seen.</summary>
    // Always offered and always far away, so it never wins against a job with a site
    // nearby and always wins when none of them has one. That is the whole of the rule for
    // when to explore, and it needs no state to say it.
    private void Looking(List<IJob> jobs, IReadOnlyList<NeededItem> missing)
    {
        // Grouped by the band the tiles live in, because that is where the walk goes and
        // two walks to the same place are one piece of work. Ungrouped, an objective short
        // of iron and lead and another short of iron alone worded their reasons
        // differently, deduplicated against nothing, and each paid for its own sweep of
        // the frontier every time the run looked for something to do.
        //
        // A list rather than a dictionary, since a band may be nothing at all and there
        // are never more than a handful of them.
        List<(Layer? Band, List<int> Tiles, List<string> Names)> digging = [];
        List<int> types = [];
        List<string> huntable = [];
        foreach (NeededItem want in missing)
        {
            // Every option, not just the named one. A world grows iron or lead, and
            // looking for either is the same walk to the same band, so both go in and the
            // grouping below collapses them.
            foreach ((int itemID, int _) in want.Options)
            {
                IReadOnlyList<int> yields = Mining.Yields(itemID);
                if (yields.Count > 0)
                {
                    (List<int> tiles, List<string> names) =
                        Group(digging, TileZones.Nearest(yields, body.Footing.Y));
                    tiles.AddRange(yields);
                    names.Add(Names.Item(itemID));

                    // One journey is enough, and ground wins it. A glowstick is in an
                    // underground pot and on an ocean jellyfish, and hunting walked the run
                    // sixty tiles along the surface toward water while the pots sat in the
                    // band it was already digging through. A tile waits to be broken where
                    // the map says it is; a creature has to be found alive first. Only the
                    // travelling is decided here: one already in view is still fought, in
                    // Trips, which offers every way of taking what is in front of the body.
                    continue;
                }

                IReadOnlyList<int> droppers = Loot.Droppers(itemID);
                if (droppers.Count > 0)
                {
                    types.AddRange(droppers);
                    huntable.Add(Names.Item(itemID));
                }
            }
        }

        // Two different searches wearing one name until now. Revealing map is what finds a
        // tree, and being in the right band is what finds a slime, and neither does the
        // other's job.
        foreach ((Layer? band, List<int> tiles, List<string> names) in digging)
        {
            jobs.Add(new Explore(terrain, sites, body, tiles, band,
                string.Join(", ", names)));
        }

        if (types.Count > 0)
        {
            jobs.Add(new Hunt(terrain, creatures, clock, body, types,
                string.Join(", ", huntable)));
        }
    }

    /// <summary>The group for a band, started if this is the first thing wanting it.</summary>
    private static (List<int> Tiles, List<string> Names) Group(
        List<(Layer? Band, List<int> Tiles, List<string> Names)> digging, Layer? band)
    {
        foreach ((Layer? has, List<int> tiles, List<string> names) in digging)
        {
            if (has == band)
            {
                return (tiles, names);
            }
        }

        (Layer? Band, List<int> Tiles, List<string> Names) started = (band, [], []);
        digging.Add(started);
        return (started.Tiles, started.Names);
    }

    /// <summary>The ways of getting one raw thing, as jobs.</summary>
    // All of them, not the likeliest. A dropped one lying about beats mining for another,
    // and which is nearer is not this objective's to know.
    // Every option, not just the named one. Iron ore and lead ore fill the same need and
    // a world grows one of them; offering both and letting the ground decide is the whole
    // reason a needed item carries its alternatives.
    private void Trips(List<IJob> jobs, NeededItem want)
    {
        foreach ((int itemID, int owed) in want.Options)
        {
            int target = bag.Carrying(itemID) + owed;

            // One job per way of getting it, built from the tables and nothing else.
            // Whether there is a slime on screen or wood on the ground is not asked here:
            // a job with nothing to work answers with no site, and the ranking drops it.
            // Asking now would mean the run only ever hunts what happened to be in view
            // when it last chose.
            jobs.Add(new Pickup(bag, drops, itemID, target));

            IReadOnlyList<int> tiles = Mining.Yields(itemID);
            if (tiles.Count > 0)
            {
                jobs.Add(new Gather(terrain, bag, hand, sites, journal, itemID, tiles, target));
            }

            // Both, not one or the other. Gel comes off two dozen kinds of creature and
            // nothing else, and stone comes out of the ground and off a few of them.
            IReadOnlyList<int> droppers = Loot.Droppers(itemID);
            if (droppers.Count > 0)
            {
                jobs.Add(new Fight(creatures, bag, hand, journal, droppers));
            }
        }
    }

    /// <summary>Making the next thing, and having a station to make it at.</summary>
    // Standing the station up is no longer a job of its own: a craft puts its own bench
    // down, works at it and takes it back, because those three happen in one place and
    // nothing else is standing there at the end to know the run is finished with it.
    //
    // What is left here is only the case where there is no station to be had at all,
    // which is a craft of its own, or one standing further off than a craft will walk to,
    // which is a trip to go and get it.
    private void Make(List<IJob> jobs, RecipeNode node, int wanted)
    {
        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> _, int tileID)
            in node.Stations)
        {
            // In reach, in the bag, or standing near enough to walk to: in all three the
            // craft below sees to it itself.
            if (bag.NearStation(tileID)
                || bag.Carrying(station.ItemID) > 0
                || sites.Nearest(body.Footing, [tileID], Craft.Reuse) is not null)
            {
                continue;
            }

            // Standing, but further off than a craft will walk. Going back for it beats
            // making a second: the run has already paid for this one, and five iron bars
            // is a morning's mining where the walk is a minute.
            if (Standing(station.ItemID))
            {
                Retrieve(jobs, station.ItemID, tileID);
                break;
            }

            jobs.Add(Making(station.ItemID, 0, 0, 1));
            break;
        }

        // The thing to make may itself be a station, with one already standing further off
        // than a craft will walk to. Without this the run makes a second and carries the
        // spare, which is where the extra work benches came from.
        foreach (KeyValuePair<int, IReadOnlyList<int>> station in stations)
        {
            if (Lists(station.Value, node.ItemID) && Standing(node.ItemID))
            {
                Retrieve(jobs, node.ItemID, station.Key);
                return;
            }
        }

        jobs.Add(Making(node.ItemID, Station(node), StationItem(node),
            bag.Carrying(node.ItemID) + wanted));
    }

    /// <summary>A craft of one thing, station and all.</summary>
    private Craft Making(int itemID, int tileID, int stationID, int count) =>
        new(terrain, bag, hand, body, sites, journal, itemID, tileID, stationID, count);

    /// <summary>The item that stands this node's station up, or zero when it takes none.</summary>
    private static int StationItem(RecipeNode node)
    {
        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> _, int _) in node.Stations)
        {
            return station.ItemID;
        }

        return 0;
    }

    /// <summary>Whether one of these is on the ground somewhere and none is in the bag.</summary>
    // Any distance. Craft walks to a station within Reuse and no further, and past that a
    // fresh one is the cheaper answer in walking terms; it is not the cheaper answer in
    // what it costs, and five iron bars is a morning's mining where the walk is a minute.
    private bool Standing(int itemID)
    {
        foreach (KeyValuePair<int, IReadOnlyList<int>> station in stations)
        {
            if (Lists(station.Value, itemID) && bag.Carrying(itemID) == 0
                && sites.Nearest(body.Footing, [station.Key]) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Going back for a station already made, which is a break and a walk onto it.</summary>
    private void Retrieve(List<IJob> jobs, int itemID, int tileID)
    {
        jobs.Add(new Pickup(bag, drops, itemID, 1));
        jobs.Add(new Gather(terrain, bag, hand, sites, journal, itemID, [tileID], 1));
    }

    /// <summary>The tile this is made at, or zero when it takes none.</summary>
    private static int Station(RecipeNode node)
    {
        foreach ((RecipeNode _, IReadOnlyList<RecipeNode> _, int tileID) in node.Stations)
        {
            return tileID;
        }

        return 0;
    }
}
