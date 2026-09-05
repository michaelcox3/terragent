using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.Plan;

/// <summary>
/// What a goal still costs, given what is carried and what is nearby.
/// </summary>
// Recipes answers what a thing is made of, a fact about Terraria that holds before the
// world is generated. This answers a different question: of that tree, what is left to
// go and get. An instance rather than a static, because planning against a particular
// world needs Belief, Character and Stations for the life of a run.
//
// Spare, Claim, Kept and Note stay static below: they are pure functions of their
// arguments, and holding a Belief does not make them otherwise.
internal sealed class Requirements(Belief belief, Character character, Stations stations)
{
    private readonly Belief _belief = belief;
    private readonly Character _character = character;
    private readonly Stations _stations = stations;



    /// <summary>How many crafts down to look for something worth digging.</summary>
    // A pickaxe is bars and bars are ore, which is two.
    private const int ChainDepth = 3;

    /// <summary>
    /// Which of several interchangeable materials this world will actually give up.
    /// </summary>
    // A world generates iron or lead and never both, so the goal names the group and
    // asking for one by name stalls half of all worlds. Preferred in the order the agent
    // can act on: carried first, then whichever ore has been seen, then the first
    // member, so exploring for one is what reveals which world this is.
    //
    // Only the objective asks this. Inside a recipe every alternative becomes a job of
    // its own and the ground decides between them.
    public int Pick(IEnumerable<int> candidates, Point at)
    {
        int first = 0;
        foreach (int itemID in candidates)
        {
            first = first == 0 ? itemID : first;
            if (_character.Inventory.Carrying(itemID) > 0)
            {
                return itemID;
            }
        }

        foreach (int itemID in candidates)
        {
            if (Sighted(itemID, at, ChainDepth, []))
            {
                return itemID;
            }
        }

        return first;
    }

    /// <summary>Which of the items a recipe would take for this ingredient to go and get.</summary>
    // The same rule that chooses between an iron pickaxe and a lead one, one level
    // down: carried first, then whatever the belief can see, then the item the recipe
    // names. Nothing here knows a wood from a bar, so every recipe group in the game
    // gets the same treatment for free.
    private Need Choose(Need need, Point at)
    {
        if (need.Instead.Count == 0)
        {
            return need;
        }

        List<int> offered = [];
        foreach (Need choice in need.Choices)
        {
            offered.Add(choice.ItemID);
        }

        int wanted = Pick(offered, at);
        foreach (Need choice in need.Choices)
        {
            if (choice.ItemID == wanted)
            {
                return choice;
            }
        }

        return need;
    }

    /// <summary>Whether anything this comes down to can be seen from here.</summary>
    // Asking only what the recipe names directly finds nothing minable for either an
    // iron pickaxe or a lead one: both want bars, and a bar is smelted rather than dug.
    // Neither candidate matches, the first always wins, and a lead world gets dug for
    // iron until it runs out.
    //
    // Bounded and marked, because a recipe chain that steps both ways walks itself in
    // circles: Terraria will turn wood into platforms and platforms back into wood.
    private bool Sighted(int itemID, Point at, int depth,
        HashSet<int> seen)
    {
        if (depth < 0 || !seen.Add(itemID))
        {
            return false;
        }

        if (Tiles.MinedFrom(itemID) is { } tile
            && _belief.NearestSite(at, tile, _belief.SupportsStation, out _, out _))
        {
            return true;
        }

        if (Recipes.RecipeFor(itemID) is not { } recipe)
        {
            return false;
        }

        foreach (Item ingredient in recipe.requiredItem)
        {
            if (ingredient.IsAir)
            {
                continue;
            }

            foreach (int alternative in Recipes.Accepts(recipe, ingredient))
            {
                if (Sighted(alternative, at, depth - 1, seen))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Everything standing between the agent and this recipe, and the whole amount of
    /// each that it calls for.
    /// </summary>
    // The full stack, not the part still missing: the caller hands these to Prune and
    // Prune subtracts the inventory itself, so reporting the remainder here subtracts it
    // twice. All of them rather than the first, because a recipe short of obsidian and
    // hellstone is short of both.
    //
    // Stations and ingredients are two lists on a Recipe with two different rules, and
    // this is where the difference lives so nowhere else has to know it. A station is
    // wanted once however many times the recipe runs, is not consumed by running it, and
    // is satisfied by standing near one. None of that is true of an iron ore.
    public List<ItemQuantity> Blocking(Recipe recipe, int crafts, Point at)
    {
        List<ItemQuantity> blocking = [];

        // Stations first, or the agent mines thirty six iron ore and only then finds
        // out whether a furnace is possible.
        foreach (int tileID in recipe.requiredTile)
        {
            if (tileID <= 0 || _character.Inventory.AtStation(tileID)
                || _stations.Nearest(tileID, _belief, at) is not null)
            {
                continue;
            }

            int carried = StationKinds.ItemFor(tileID);
            if (carried != 0 && _character.Inventory.Carrying(carried) == 0)
            {
                blocking.Add(new ItemQuantity(carried, 1));
            }
        }

        // Then the whole batch of materials, before the first craft. Crafting the
        // moment three ore make one bar is a walk back to the furnace for every bar.
        foreach (Item required in recipe.requiredItem)
        {
            if (required.IsAir)
            {
                continue;
            }

            List<int> accepts = Recipes.Accepts(recipe, required);
            int needed = required.stack * crafts;

            // What may be spent, not what is held: a furnace costs three torches, and a
            // run holding exactly three makes it and walks into the dark with none.
            //
            // The upkeep's floor, not the current job's reservation. The job reserves
            // these very torches for this very furnace, so subtracting that would stop
            // the recipe using what it asked for.
            int held = _character.Inventory.Carrying(accepts) - Kept(accepts);
            if (held < needed)
            {
                blocking.Add(new ItemQuantity(Pick(accepts, at),
                    needed + Kept(accepts)));
            }
        }

        return blocking;
    }

    /// <summary>
    /// Everything still to be dug, chopped or fought for before these can be made.
    /// </summary>
    // The whole tree flattened to its leaves: an Iron Pickaxe comes out as wood, stone,
    // gel and iron ore, because those are the only things anyone goes and gets.
    // Everything between them is a craft, and a craft is not somewhere to walk.
    //
    // Reserving on the way down is the one pass that sees every material the job depends
    // on. Without it the agent gathers stone for a furnace and then spends it pillaring
    // out of the hole it dug for it.
    //
    // All the roots share one tally, because the choice of what to dig next is made
    // across all of them, and sharing it stops one stack of wood satisfying two.
    public void Leaves(IReadOnlyList<ItemQuantity> roots, Point at, int depth,
        List<ItemQuantity> into)
    {
        Dictionary<int, int> claimed = [];
        HashSet<int> standing = [];
        for (int n = 0; n < roots.Count; n++)
        {
            Prune(Recipes.Tree(roots[n].ItemID, depth), roots[n].Count, at, into,
                claimed, standing);
        }
    }

    /// <summary>
    /// Walk a tree of what a thing is made of, keeping only what is still to be got.
    /// </summary>
    // The tree is Recipes' and says nothing about this world. Everything world-shaped
    // happens here: what is carried prunes a branch, what is standing nearby settles a
    // station, and what nothing makes becomes a line on the board.
    //
    // Counts are worked out on the way down, because they depend on the inventory. A
    // node says how many it takes per craft; how many crafts there are depends on how
    // many are already had, and that is only known here.
    private void Prune(Need node, int wantedItemID, Point at, List<ItemQuantity> into,
        Dictionary<int, int> claimed, HashSet<int> standing)
    {
        int spare = Spare(_character.Inventory, claimed, node.ItemID);
        Claim(_character.Inventory, claimed, node.ItemID, wantedItemID);

        int missing = wantedItemID - spare;
        if (missing <= 0)
        {
            return;
        }

        _character.Inventory.Reserve(new ItemQuantity(node.ItemID, wantedItemID));

        // Dug if it can be dug, made if it can be made, fought for only when neither.
        // Asking what drops it first makes a torch a leaf, since something does drop
        // torches, and the agent waits on a slime instead of making three from wood.
        //
        // Here rather than in the tree: that wood can be chopped is a fact about
        // Terraria, but that chopping beats un-crafting a platform is a decision, and
        // decisions live on this side.
        if (Tiles.MinedFrom(node.ItemID) is not null || node.Raw)
        {
            Note(into, new ItemQuantity(node.ItemID, missing));
            return;
        }

        int crafts = (missing + node.Yields - 1) / node.Yields;
        int before = into.Count;

        foreach (Need need in node.Needs)
        {
            if (need.StationTileID > 0)
            {
                // Standing at one, one in the bag, or one already asked for. A station
                // is not spent by being used, so the bow, the torches and the furnace
                // each wanting a work bench is one work bench. Note sums what it is
                // told, so three asks would read "craft Work Bench (1/3)".
                if (_character.Inventory.AtStation(need.StationTileID)
                    || _stations.Nearest(need.StationTileID, _belief, at) is not null)
                {
                    continue;
                }

                if (standing.Add(need.ItemID))
                {
                    Prune(need, 1, at, into, claimed, standing);
                }

                continue;
            }

            Prune(Choose(need, at), need.PerCraft * crafts, at, into, claimed, standing);
        }

        // Nothing below this is outstanding, so the thing itself is what is left to do:
        // a craft with every material in inventory is as much a job of work as a tree is.
        if (into.Count == before)
        {
            Note(into, new ItemQuantity(node.ItemID, missing));
        }
    }

    /// <summary>How many of an item no branch walked so far has spoken for.</summary>
    // The tally is why this is not just Carrying. A pickaxe wants wood for its bench,
    // its furnace and its torches; asking the inventory fresh at each lets all three
    // count the same ten, and the job comes back saying no wood is needed.
    private static int Spare(Inventory inventory, Dictionary<int, int> claimed, int itemID)
        => System.Math.Max(0,
            inventory.Carrying(itemID) - (claimed.TryGetValue(itemID, out int taken) ? taken : 0));

    /// <summary>Take an amount off the tally, as far as what is held goes.</summary>
    private static void Claim(Inventory inventory, Dictionary<int, int> claimed, int itemID,
        int amount)
    {
        int take = System.Math.Min(amount, Spare(inventory, claimed, itemID));
        claimed[itemID] = (claimed.TryGetValue(itemID, out int taken) ? taken : 0) + take;
    }

    /// <summary>How many of these an upkeep says may never be spent.</summary>
    private static int Kept(List<int> accepts)
    {
        int most = 0;
        for (int n = 0; n < accepts.Count; n++)
        {
            most = System.Math.Max(most, Upkeeps.Reserved(accepts[n]));
        }

        return most;
    }

    /// <summary>Add to the tally, or raise it if this leaf is already on it.</summary>
    // A pickaxe wants ore for its bars and its anvil's bars, arriving down two
    // branches. One line saying fifty one is the job; two saying thirty six and
    // fifteen is arithmetic for the reader to do.
    public static void Note(List<ItemQuantity> into, ItemQuantity found)
    {
        int itemID = found.ItemID;
        int needed = found.Count;

        for (int n = 0; n < into.Count; n++)
        {
            if (into[n].ItemID == itemID)
            {
                into[n] = new ItemQuantity(itemID, into[n].Count + needed);
                return;
            }
        }

        into.Add(new ItemQuantity(itemID, needed));
    }
}
