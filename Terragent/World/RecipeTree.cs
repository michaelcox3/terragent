using System;
using System.Collections.Generic;

namespace Terragent.World;

/// <summary>Turns an item into the tree of what it is made of.</summary>
// Arithmetic over a book of recipes, and nothing else. No game, no terrain, no position:
// what a recipe comes down to is the same answer in every world.
//
// The book is every recipe in the game, flat, each naming its parts by id. Growing a tree
// is choosing one row per item and putting the answer in each part's place.
//
// Two things it cannot know are asked for: the book, and which items are gathered rather
// than made. That keeps every fact about this particular world outside.
internal sealed class RecipeTree(IReadOnlyList<CraftingRecipe> book, Func<int, bool> gathered)
    : IRecipeTree
{
    private readonly IReadOnlyList<CraftingRecipe> _book = book;
    /// <summary>Whether an item is gathered rather than made.</summary>
    // A tree gives wood, a vein gives ore, a slime gives gel. It is asked about the parts
    // of a recipe, not about the thing being made, and it is the yes at the bottom of
    // "could this part be had": without it the ten wood under a work bench can be had by
    // no route, so the bench recipe is unfollowable and a bench reads as something found
    // lying about.
    private readonly Func<int, bool> _gathered = gathered;

    /// <summary>How far to follow parts when judging a recipe.</summary>
    // Two is ore to bar to tool. Deeper is a supply chain the run does not have.
    public const int Depth = 2;

    /// <summary>Trees already grown, because the answer cannot change.</summary>
    // Choosing a recipe walks the whole book at every node, so a tree is worth keeping.
    private readonly Dictionary<(int ItemID, int Depth), Need> _trees = [];

    /// <summary>The whole tree of what a thing is made of, down to what nothing makes.</summary>
    // Every alternative is expanded, not the likeliest, because which is worth going for
    // depends on where the agent stands. Bounded by depth and by path, since wood becomes
    // platforms and platforms become wood.
    public Need Of(int itemID, int depth)
    {
        if (_trees.TryGetValue((itemID, depth), out Need? known))
        {
            return known;
        }

        Need grown = Grow(itemID, depth, []);
        _trees[(itemID, depth)] = grown;
        return grown;
    }

    private Need Grow(int itemID, int depth, HashSet<int> above)
    {
        if (depth <= 0 || !above.Add(itemID))
        {
            return Leaf(itemID);
        }

        if (Chosen(itemID) is not { } row)
        {
            // Unmarked on the way out, or a raw thing met once would read as already
            // walked for the rest of the tree.
            above.Remove(itemID);
            return Leaf(itemID);
        }

        // A station is an ordinary need with its own recipe under it, on an edge naming
        // the tile it puts down, so whoever prunes can tell that standing beside one
        // satisfies it.
        List<(Need Need, int TileID)> stations = [];
        foreach ((int placedBy, int tileID) in row.Stations)
        {
            stations.Add((Grow(placedBy, depth - 1, above), tileID));
        }

        List<(Need Need, int Count, IReadOnlyList<Need> Instead)> needs = [];
        foreach ((int required, int count, IReadOnlyList<int> alternatives) in row.Items)
        {
            List<Need> instead = [];
            foreach (int other in alternatives)
            {
                instead.Add(Grow(other, depth - 1, above));
            }

            needs.Add((Grow(required, depth - 1, above), count, instead));
        }

        above.Remove(itemID);
        return new Need(itemID, row.Yields, stations, needs);
    }

    /// <summary>A need nothing makes, which is what a walk stops at.</summary>
    private static Need Leaf(int itemID) => new(itemID, null, [], []);

    /// <summary>What still has to be gathered for a number of a thing.</summary>
    /// <param name="carrying">How many of an item the run already has.</param>
    // Raw items only, added up across the whole tree. An iron pickaxe is twelve bars and
    // the anvil it is made at is five more, so the ore is one number and one trip, not
    // thirty six and then fifteen.
    //
    // The bag is spent as the walk descends rather than taken off the totals afterwards.
    // Subtracting at the end only credits raw things: five bars in hand would leave the
    // ore untouched, and the run would dig for ore it had already smelted. Spending them
    // here skips the branch instead.
    //
    // And spent once. Those five bars serve the pickaxe or the anvil and not both, so
    // whichever the walk reaches first takes them and the other asks for all of its own.
    /// <param name="atStation">Whether a station tile is already within reach.</param>
    public static IReadOnlyDictionary<int, int> Missing(Need node, int wanted,
        Func<int, int> carrying, Func<int, bool> atStation)
    {
        Dictionary<int, int> totals = [];
        Gather(node, wanted, totals, [], carrying, atStation, []);
        return totals;
    }

    /// <summary>The next thing to make, or null when nothing can be made yet.</summary>
    // The deepest node whose parts are all in hand. Crafting is bottom up: ore becomes
    // bars, bars become an anvil, and asking the game to make the pickaxe while the bars
    // are still ore gets a no with nothing to do about it.
    //
    // Null means either the thing is already had or something still has to be gathered.
    // Missing says which of those it is.
    public static (Need Node, int Wanted)? Craftable(Need node, int wanted,
        Func<int, int> carrying, Func<int, bool> atStation) =>
        Ready(node, wanted, [], carrying, atStation, []);

    private static (Need Node, int Wanted)? Ready(Need node, int wanted,
        Dictionary<int, int> stock, Func<int, int> carrying, Func<int, bool> atStation,
        HashSet<int> built)
    {
        if (!stock.TryGetValue(node.ItemID, out int held))
        {
            held = carrying(node.ItemID);
        }

        int spend = Math.Min(held, wanted);
        stock[node.ItemID] = held - spend;

        int missing = wanted - spend;
        if (missing <= 0 || node.Raw)
        {
            return null;
        }

        int crafts = ((missing - 1) / node.Makes) + 1;

        // The station comes before what is made at it. Bars are not craftable without a
        // furnace however much ore is carried, and the furnace is the thing to make.
        bool short_ = false;
        foreach ((Need station, int tileID) in node.Stations)
        {
            // Had, standing at, or already dealt with higher up the walk. None of those
            // spend it, because a station serves every craft made at it.
            if (atStation(tileID) || Spare(stock, carrying, station.ItemID) > 0
                || !built.Add(station.ItemID))
            {
                continue;
            }

            if (Ready(station, 1, stock, carrying, atStation, built) is { } first)
            {
                return first;
            }

            short_ |= Missing(station, 1, id => Spare(stock, carrying, id), atStation).Count > 0;
        }

        foreach ((Need part, int count, IReadOnlyList<Need> _) in node.Needs)
        {
            int owed = count * crafts;

            // Asked before the walk below spends it. Ready takes what it needs out of the
            // stock as it goes, so asking afterwards whether the same part is short reads
            // what it has just spent as an amount that is missing: one gel, needed once,
            // comes back as one gel short, and the torch reads as unmakeable.
            // Asked before the walk below spends it. Ready takes what it needs out of the
            // stock as it goes, so asking afterwards whether the same part is short reads
            // what it has just spent as an amount that is missing: one gel, needed once,
            // comes back as one gel short, and the torch it was for reads as unmakeable.
            bool wanting = Missing(part, owed, id => Spare(stock, carrying, id),
                atStation).Count > 0;

            if (Ready(part, owed, stock, carrying, atStation, built) is { } deeper)
            {
                return deeper;
            }

            short_ |= wanting;
        }

        // Something under it is still in the ground, so this is not makeable yet.
        return short_ ? null : (node, missing);
    }

    /// <summary>What is left of an item once the walk in hand has spent some.</summary>
    private static int Spare(Dictionary<int, int> stock, Func<int, int> carrying, int itemID) =>
        stock.TryGetValue(itemID, out int left) ? left : carrying(itemID);

    /// <param name="stock">What is left of the bag as the walk spends it.</param>
    /// <param name="built">Station items already counted, so a bench is wanted once.</param>
    private static void Gather(Need node, int wanted, Dictionary<int, int> totals,
        Dictionary<int, int> stock, Func<int, int> carrying, Func<int, bool> atStation,
        HashSet<int> built)
    {
        if (!stock.TryGetValue(node.ItemID, out int held))
        {
            held = carrying(node.ItemID);
        }

        int spend = Math.Min(held, wanted);
        stock[node.ItemID] = held - spend;

        int missing = wanted - spend;
        if (missing <= 0)
        {
            return;
        }

        if (node.Raw)
        {
            totals[node.ItemID] =
                (totals.TryGetValue(node.ItemID, out int had) ? had : 0) + missing;
            return;
        }

        // Rounded up, since a torch recipe makes three and wanting four is two crafts.
        int crafts = ((missing - 1) / node.Makes) + 1;

        foreach ((Need station, int tileID) in node.Stations)
        {
            // One bench serves the furnace, the anvil and the torches, so it is wanted
            // once however many nodes stand at it.
            // A station is never spent. One bench serves the furnace, the anvil and the
            // torches, and a walk that took it off the bag for the first of them went and
            // asked for a second.
            //
            // Standing beside one counts as having it. It is not in the bag any more once
            // placed, so nothing else here could tell.
            if (atStation(tileID) || Spare(stock, carrying, station.ItemID) > 0
                || !built.Add(station.ItemID))
            {
                continue;
            }

            Gather(station, 1, totals, stock, carrying, atStation, built);
        }

        foreach ((Need part, int count, IReadOnlyList<Need> _) in node.Needs)
        {
            // The named part, not its alternatives. Which of iron and lead a world has is
            // decided where the job is made, and totalling both would ask for eighty wood
            // of eight kinds instead of ten of any.
            Gather(part, count * crafts, totals, stock, carrying, atStation, built);
        }
    }

    /// <summary>Which recipe to follow for an item, or null when none is followable.</summary>
    public CraftingRecipe? Chosen(int itemID)
    {
        CraftingRecipe? indirect = null;
        foreach (CraftingRecipe row in _book)
        {
            if (row.Makes != itemID)
            {
                continue;
            }

            // Parts gettable outright beat ones that need further crafting, or a work
            // bench reads as made of wood platforms rather than ten wood.
            if (Followable(row, 0, itemID))
            {
                return row;
            }

            indirect ??= Followable(row, Depth, itemID) ? row : null;
        }

        // No fallback. A recipe just judged unfollowable, handed back anyway, makes the
        // agent advertise crafting and chase it all run. Wood, whose only recipe turns two
        // platforms back into one, is that case.
        return indirect;
    }

    /// <summary>Whether every part is something that could be got.</summary>
    // The first entry in the book is often not the one a player would use: an iron bar is
    // smelted from ore, and is also what recycling an iron fence gives back.
    private bool Followable(CraftingRecipe row, int depth, int making)
    {
        foreach ((int part, int _, IReadOnlyList<int> alternatives) in row.Items)
        {
            // A recipe that wants the thing being made is not a way to it: bars make a
            // fence and the fence recycles into bars, so each would justify the other.
            if (part == making)
            {
                return false;
            }

            if (Had(part, depth, making))
            {
                continue;
            }

            bool other = false;
            foreach (int alternative in alternatives)
            {
                other |= alternative != making && Had(alternative, depth, making);
            }

            if (!other)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a part is gathered, or made within the depth left.</summary>
    private bool Had(int itemID, int depth, int making) =>
        _gathered(itemID) || (depth > 0 && Makes(itemID, depth - 1, making));

    /// <summary>Whether anything makes this, without reconsidering the goal.</summary>
    private bool Makes(int itemID, int depth, int making)
    {
        foreach (CraftingRecipe row in _book)
        {
            if (row.Makes == itemID && Followable(row, depth, making))
            {
                return true;
            }
        }

        return false;
    }
}
