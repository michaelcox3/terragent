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

    /// <summary>How many other ways of standing a station up to carry.</summary>
    // A tile is placed by anything from two items to fifty: two anvils, but every wood in
    // the game makes a work bench. What the run needs is that another metal would do, and
    // past a couple that is a catalogue rather than an answer.
    private const int Spares = 2;

    /// <summary>How far to follow parts when judging a recipe.</summary>
    // Two is ore to bar to tool. Deeper is a supply chain the run does not have.
    public const int Depth = 2;

    /// <summary>Trees already grown, because the answer cannot change.</summary>
    // Choosing a recipe walks the whole book at every node, so a tree is worth keeping.
    private readonly Dictionary<(int ItemID, int Depth), RecipeNode> _trees = [];

    /// <summary>The whole tree of what a thing is made of, down to what nothing makes.</summary>
    // Every alternative is expanded, not the likeliest, because which is worth going for
    // depends on where the agent stands. Bounded by depth and by path, since wood becomes
    // platforms and platforms become wood.
    public RecipeNode Of(int itemID, int depth)
    {
        if (_trees.TryGetValue((itemID, depth), out RecipeNode? known))
        {
            return known;
        }

        RecipeNode grown = Grow(itemID, depth, []);
        _trees[(itemID, depth)] = grown;
        return grown;
    }

    private RecipeNode Grow(int itemID, int depth, HashSet<int> above)
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
        List<(RecipeNode, IReadOnlyList<RecipeNode>, int)> stations = [];
        foreach ((int placedBy, IReadOnlyList<int> others, int tileID) in row.Stations)
        {
            // Only a couple of the stand-ins, since a work bench has a dozen woods behind
            // it and the run wants to know that lead would do, not to be handed a catalogue.
            List<RecipeNode> instead = [];
            foreach (int other in others)
            {
                if (instead.Count == Spares)
                {
                    break;
                }

                instead.Add(Grow(other, depth - 1, above));
            }

            stations.Add((Grow(placedBy, depth - 1, above), instead, tileID));
        }

        List<(RecipeNode RecipeNode, int Count, IReadOnlyList<RecipeNode> Instead)> needs = [];
        foreach ((int required, int count, IReadOnlyList<int> alternatives) in row.Items)
        {
            List<RecipeNode> instead = [];
            foreach (int other in alternatives)
            {
                instead.Add(Grow(other, depth - 1, above));
            }

            needs.Add((Grow(required, depth - 1, above), count, instead));
        }

        above.Remove(itemID);
        return new RecipeNode(itemID, row.Yields, stations, needs);
    }

    /// <summary>A need nothing makes, which is what a walk stops at.</summary>
    private static RecipeNode Leaf(int itemID) => new(itemID, null, [], []);

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
    public static IReadOnlyList<NeededItem> Missing(RecipeNode node, int wanted,
        Func<int, int> carrying, Func<int, bool> atStation) =>
        Missing([(node, wanted, [])], carrying, atStation);

    /// <summary>What still has to be gathered for several things at once.</summary>
    // One walk over the lot, sharing the bag it spends and the stations it has counted.
    // Walked one at a time instead, six pieces of gold armour each charge their own anvil
    // and their own bench, and each credits itself the same bars out of the same bag: the
    // run is told to gather sixty wood for one work bench and the trip does not finish
    // until it has them.
    //
    // Each root may name stand-ins, which are walked beside it and never added to it, the
    // same way a recipe group's alternatives are. A gold helmet or a platinum one is one
    // piece of armour, not two.
    public static IReadOnlyList<NeededItem> Missing(
        IReadOnlyList<(RecipeNode Node, int Wanted, IReadOnlyList<RecipeNode> Instead)> all,
        Func<int, int> carrying, Func<int, bool> atStation)
    {
        Dictionary<int, int> totals = [];
        Dictionary<int, List<(int ItemID, int Count)>> instead = [];
        Dictionary<int, int> stock = [];
        HashSet<int> built = [];

        foreach ((RecipeNode node, int wanted, IReadOnlyList<RecipeNode> others) in all)
        {
            Dictionary<int, int> before = new(totals);
            Dictionary<int, int> bag = new(stock);
            HashSet<int> already = new(built);
            Gather(node, wanted, totals, instead, stock, carrying, atStation, built);

            foreach (RecipeNode other in others)
            {
                Instead(other, wanted, Added(before, totals), instead, bag, already,
                    carrying, atStation, adds: false);
            }
        }

        List<NeededItem> short_ = [];
        foreach (KeyValuePair<int, int> want in totals)
        {
            List<(int ItemID, int Count)> options = [(want.Key, want.Value)];
            if (instead.TryGetValue(want.Key, out List<(int ItemID, int Count)>? others))
            {
                options.AddRange(others);
            }

            short_.Add(new NeededItem(options));
        }

        return short_;
    }

    /// <summary>Every station this tree is worked at, and the item that puts each down.</summary>
    // Read off the tree rather than off the recipe book, so the answer is the stations a
    // run will actually stand at and not the two dozen vanilla has. That is what makes it
    // cheap to sweep the ground for one of them.
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> Stations(RecipeNode node)
    {
        Dictionary<int, List<int>> found = [];
        Standing(node, found, []);

        Dictionary<int, IReadOnlyList<int>> placers = [];
        foreach (KeyValuePair<int, List<int>> station in found)
        {
            placers[station.Key] = station.Value;
        }

        return placers;
    }

    private static void Standing(RecipeNode node, Dictionary<int, List<int>> found,
        HashSet<int> seen)
    {
        if (!seen.Add(node.ItemID))
        {
            return;
        }

        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> instead, int tileID)
            in node.Stations)
        {
            if (!found.TryGetValue(tileID, out List<int>? placers))
            {
                placers = [];
                found[tileID] = placers;
            }

            if (!placers.Contains(station.ItemID))
            {
                placers.Add(station.ItemID);
            }

            Standing(station, found, seen);
            foreach (RecipeNode other in instead)
            {
                if (!placers.Contains(other.ItemID))
                {
                    placers.Add(other.ItemID);
                }
            }
        }

        foreach ((RecipeNode part, int _, IReadOnlyList<RecipeNode> instead) in node.Needs)
        {
            Standing(part, found, seen);
            foreach (RecipeNode other in instead)
            {
                Standing(other, found, seen);
            }
        }
    }

    /// <summary>The next thing to make, or null when nothing can be made yet.</summary>
    // The deepest node whose parts are all in hand. Crafting is bottom up: ore becomes
    // bars, bars become an anvil, and asking the game to make the pickaxe while the bars
    // are still ore gets a no with nothing to do about it.
    //
    // Null means either the thing is already had or something still has to be gathered.
    // Missing says which of those it is.
    public static (RecipeNode Node, int Wanted)? Craftable(RecipeNode node, int wanted,
        Func<int, int> carrying, Func<int, bool> atStation) =>
        Ready(node, wanted, [], carrying, atStation, []);

    private static (RecipeNode Node, int Wanted)? Ready(RecipeNode node, int wanted,
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
        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> spares, int tileID)
            in node.Stations)
        {
            // Had, standing at, or already dealt with higher up the walk. None of those
            // spend it, because a station serves every craft made at it. Had by any of the
            // things that place the tile, since they put down the same one.
            if (atStation(tileID) || Held(spares, station, stock, carrying)
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

        foreach ((RecipeNode part, int count, IReadOnlyList<RecipeNode> _) in node.Needs)
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

    /// <summary>Whether any of the things that stand this station up is already in hand.</summary>
    private static bool Held(IReadOnlyList<RecipeNode> spares, RecipeNode station,
        Dictionary<int, int> stock, Func<int, int> carrying)
    {
        if (Spare(stock, carrying, station.ItemID) > 0)
        {
            return true;
        }

        foreach (RecipeNode other in spares)
        {
            if (Spare(stock, carrying, other.ItemID) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <param name="stock">What is left of the bag as the walk spends it.</param>
    /// <param name="built">Station items already counted, so a bench is wanted once.</param>
    /// <param name="instead">
    /// What would do in place of a raw thing, gathered beside the totals.
    /// </param>
    private static void Gather(RecipeNode node, int wanted, Dictionary<int, int> totals,
        Dictionary<int, List<(int ItemID, int Count)>> instead,
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

        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> spares, int tileID)
            in node.Stations)
        {
            // A station is never spent. One bench serves the furnace, the anvil and the
            // torches, so it is wanted once however many nodes stand at it, and a walk
            // that took it off the bag for the first of them went and asked for a second.
            //
            // Standing beside one counts as having it. It is not in the bag any more once
            // placed, so nothing else here could tell.
            //
            // Any of the placers in hand will do, since they put down the same tile.
            if (atStation(tileID) || Held(spares, station, stock, carrying)
                || !built.Add(station.ItemID))
            {
                continue;
            }

            Dictionary<int, int> before = new(totals);
            Dictionary<int, int> bag = new(stock);
            HashSet<int> already = new(built);
            Gather(station, 1, totals, instead, stock, carrying, atStation, built);

            // An anvil is iron or lead, and a bench is any wood there is. Without this a
            // lead world was told it owed five iron bars for an anvil, with no iron in it
            // to owe them from.
            foreach (RecipeNode other in spares)
            {
                Instead(other, 1, Added(before, totals), instead, bag, already, carrying,
                    atStation, adds: false);
            }
        }

        foreach ((RecipeNode part, int count, IReadOnlyList<RecipeNode> others) in node.Needs)
        {
            int owed = count * crafts;
            if (others.Count == 0)
            {
                Gather(part, owed, totals, instead, stock, carrying, atStation, built);
                continue;
            }

            // The named part is walked for real. Each stand-in is walked beside it against
            // a copy of the bag and thrown away, so nothing is spent twice and no station
            // is counted twice, and what comes back is only used to say what else would do.
            //
            // Totalling them all together is what this must not do: a bow would ask for
            // eighty wood of eight kinds instead of ten of any. They are options, and only
            // one of them will ever be worked.
            // Both walks start from the same bag and the same stations already counted,
            // so whatever they share falls out and only the difference is an alternative.
            // Started from where the named walk finished instead, the stand-in skipped
            // every station that walk had just paid for, and its ore lined up against the
            // first thing in the list rather than against the ore.
            Dictionary<int, int> before = new(totals);
            Dictionary<int, int> bag = new(stock);
            HashSet<int> already = new(built);
            Gather(part, owed, totals, instead, stock, carrying, atStation, built);

            foreach (RecipeNode other in others)
            {
                Instead(other, owed, Added(before, totals), instead, bag, already,
                    carrying, atStation, adds: true);
            }
        }
    }

    /// <summary>What a walk added, by item, which is how a stand-in is lined up against it.</summary>
    private static Dictionary<int, int> Added(
        Dictionary<int, int> before, Dictionary<int, int> after)
    {
        Dictionary<int, int> grown = [];
        foreach (KeyValuePair<int, int> want in after)
        {
            int had = before.TryGetValue(want.Key, out int held) ? held : 0;
            if (want.Value > had)
            {
                grown[want.Key] = want.Value - had;
            }
        }

        return grown;
    }

    /// <summary>What one walk wants more of than the other, most first.</summary>
    // Most first, because the pair is matched off in order and the biggest difference is
    // what the stand-in is standing in for. A lead pickaxe wants three less wood than an
    // iron one as well as thirty six more lead ore, and matched by position alone the lead
    // ore was recorded as an alternative to the wood.
    private static List<int> Heavier(Dictionary<int, int> one, Dictionary<int, int> other)
    {
        List<(int ItemID, int By)> more = [];
        foreach (KeyValuePair<int, int> want in one)
        {
            int also = other.TryGetValue(want.Key, out int had) ? had : 0;
            if (want.Value > also)
            {
                more.Add((want.Key, want.Value - also));
            }
        }

        more.Sort((first, second) => second.By.CompareTo(first.By));

        List<int> items = [];
        foreach ((int itemID, int _) in more)
        {
            items.Add(itemID);
        }

        return items;
    }

    /// <summary>Walk a stand-in and record what it would cost in place of the named part.</summary>
    // By how much the two differ, not by what they share. A lead bar is smelted at the same
    // furnace out of the same stone as an iron one, so the shared cost is not an
    // alternative to anything; but a lead pickaxe is still made at an iron anvil, so iron
    // ore appears on both sides and struck off for being shared it left the lead ore with
    // nothing to stand against. The run was told it needed iron and never that lead would
    // do, in a world with nothing but lead.
    //
    // What is recorded is the extra the stand-in costs, since the shared part is owed
    // either way: fifty one iron ore, or thirty six lead and the fifteen iron the anvil
    // still wants.
    /// <param name="adds">
    /// Whether this stand-in owes on top of what is already recorded, or instead of it.
    /// </param>
    // A part adds: a pickaxe wants twelve bars and the anvil it is made at wants five
    // more, and lead would do for either, so the two entries owe fifty one lead ore
    // between them. A whole tree replaces: naming a lead pickaxe beside an iron one says
    // the same thing the bars already said, and added on top it read as eighty seven.
    private static void Instead(RecipeNode other, int owed, Dictionary<int, int> named,
        Dictionary<int, List<(int ItemID, int Count)>> instead, Dictionary<int, int> bag,
        HashSet<int> already, Func<int, int> carrying, Func<int, bool> atStation,
        bool adds)
    {
        Dictionary<int, int> mirrored = [];
        Gather(other, owed, mirrored, [], new Dictionary<int, int>(bag), carrying,
            atStation, new HashSet<int>(already));

        List<int> ours = Heavier(named, mirrored);
        List<int> theirs = Heavier(mirrored, named);

        for (int n = 0; n < ours.Count && n < theirs.Count; n++)
        {
            if (!instead.TryGetValue(ours[n], out List<(int ItemID, int Count)>? options))
            {
                options = [];
                instead[ours[n]] = options;
            }

            // Added to what this stand-in already owes, the way the named side is. A
            // pickaxe wants twelve bars and the anvil it is made at wants five more, and
            // those are two entries in the tree; kept apart the panel would offer lead
            // twice, once for each, and neither number would be the trip.
            int extra = mirrored[theirs[n]]
                - (named.TryGetValue(theirs[n], out int had) ? had : 0);

            int held = options.FindIndex(option => option.ItemID == theirs[n]);
            if (held < 0)
            {
                options.Add((theirs[n], extra));
            }
            else
            {
                options[held] = (theirs[n], adds
                    ? options[held].Count + extra
                    : Math.Max(options[held].Count, extra));
            }
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
