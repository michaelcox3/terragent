using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.World;

/// <summary>
/// What a recipe needs, and which recipe to follow when there is a choice.
/// </summary>
// Everything that reads Main.recipe. It knows nothing about the character beyond what
// is carried, remembers nothing between calls, and never touches the controls.
internal static class Recipes
{
    /// <summary>How far to follow ingredients when judging a recipe.</summary>
    // Two is ore to bar to tool. Deeper is a supply chain the run does not have.
    public const int Depth = 2;

    public static Recipe? RecipeFor(int itemID)
    {
        Recipe? indirect = null;
        for (int i = 0; i < Recipe.numRecipes; i++)
        {
            Recipe recipe = Main.recipe[i];
            if (recipe.createItem.type != itemID)
            {
                continue;
            }

            // Materials gettable outright beat ones that need further crafting, or a
            // Work Bench reads as made of Wood Platforms rather than ten wood.
            if (Gettable(recipe, 0, itemID))
            {
                return recipe;
            }

            indirect ??= Gettable(recipe, Depth, itemID) ? recipe : null;
        }

        // No fallback. A recipe just judged unfollowable, handed back anyway, makes
        // SourcesOf advertise crafting and the executor chase it all run. Wood (whose
        // only recipe turns two platforms back into one) is that case.
        return indirect;
    }

    /// <summary>Whether every ingredient is something the agent could go and get.</summary>
    // The first recipe in the table is often not the one a player would use: an Iron
    // Bar is smelted from ore, and is also what recycling an Iron Fence gives back.
    public static bool Gettable(Recipe recipe, int depth, int making)
    {
        foreach (Item required in recipe.requiredItem)
        {
            if (required.IsAir)
            {
                continue;
            }

            // A recipe that wants the thing being made is not a way to it: bars make
            // a fence and the fence recycles into bars, so each would justify the other.
            if (required.type == making)
            {
                return false;
            }

            if (Sources.InTheWorld(required.type))
            {
                continue;
            }

            // Made rather than found. The depth limit also stops two recipes that
            // produce each other from being followed round for ever.
            if (depth <= 0 || MakeableFrom(required.type, depth - 1, making) is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A followable recipe for an ingredient, without reconsidering the goal.</summary>
    public static Recipe? MakeableFrom(int itemID, int depth, int making)
    {
        for (int i = 0; i < Recipe.numRecipes; i++)
        {
            Recipe recipe = Main.recipe[i];
            if (recipe.createItem.type == itemID && Gettable(recipe, depth, making))
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>
    /// The whole tree of what a thing is made of, down to what nothing makes.
    /// </summary>
    // A fact about Terraria only: no belief, inventory or position. Requirements prunes
    // this against what is carried. Every alternative is expanded, not the likeliest,
    // because which is worth going for depends on where the agent stands. Bounded by
    // depth and by path, since wood becomes platforms and platforms become wood.
    public static Need Tree(int itemID, int depth)
    {
        if (_trees.TryGetValue((itemID, depth), out Need? known))
        {
            return known;
        }

        Need built = Tree(itemID, 1, 0, depth, []);
        _trees[(itemID, depth)] = built;
        return built;
    }

    /// <summary>Trees already built, because the answer cannot change.</summary>
    // Recipes are fixed once loaded, and RecipeFor walks thousands of entries at every
    // node on every planning tick. Keyed on the root only: a subtree's shape depends on
    // what lay above it (the cycle guard), so only a whole walk repeats exactly.
    private static readonly Dictionary<(int ItemID, int Depth), Need> _trees = [];

    private static Need Tree(int itemID, int perCraft, int stationTile, int depth,
        HashSet<int> above)
    {
        if (depth <= 0 || !above.Add(itemID))
        {
            return new Need(itemID, perCraft, stationTile, null, [], []);
        }

        if (RecipeFor(itemID) is not { } recipe)
        {
            // Unmarked on the way out, or a raw thing met once would read as already
            // walked for the rest of the tree.
            above.Remove(itemID);
            return new Need(itemID, perCraft, stationTile, null, [], []);
        }

        // Stations first, then materials. A station is an ordinary need with its own
        // recipe under it, marked with the tile it puts down so pruning can tell that
        // standing beside one satisfies it. Per-craft of one: using it consumes nothing.
        List<Need> needs = [];
        foreach (int tileID in recipe.requiredTile)
        {
            if (tileID <= 0 || StationKinds.ItemFor(tileID) is not (var carried and not 0))
            {
                continue;
            }

            needs.Add(Tree(carried, 1, tileID, depth - 1, above));
        }

        foreach (Item required in recipe.requiredItem)
        {
            if (required.IsAir)
            {
                continue;
            }

            // One node per ingredient, with the substitutes as Instead: they are an
            // "or", and eight kinds of wood in Needs is a torch that wants all eight.
            List<Need> choices = [];
            foreach (int alternative in Accepts(recipe, required))
            {
                choices.Add(Tree(alternative, required.stack, 0, depth - 1, above));
            }

            if (choices.Count > 0)
            {
                needs.Add(choices[0] with { Instead = choices.GetRange(1, choices.Count - 1) });
            }
        }

        above.Remove(itemID);
        return new Need(itemID, perCraft, stationTile, recipe, needs, []);
    }

    /// <summary>The items that would satisfy this ingredient, substitutes included.</summary>
    // Terraria states "Iron Bar" and accepts a Lead Bar through a recipe group. Reading
    // requiredItem alone sends a lead world digging for iron that does not exist in it.
    public static List<int> Accepts(Recipe recipe, Item required)
    {
        List<int> items = [required.type];
        foreach (int id in recipe.acceptedGroups)
        {
            if (!RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup? group)
                || !group.ContainsItem(required.type))
            {
                continue;
            }

            foreach (int alt in group.ValidItems)
            {
                if (!items.Contains(alt))
                {
                    items.Add(alt);
                }
            }
        }

        return items;
    }













}
