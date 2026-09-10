using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.World;

/// <summary>The game's recipes, as the agent needs to see them.</summary>
// Read once at load and turned into a book of needs. The game's own Recipe is not walked
// directly, for two reasons: it cannot be referenced outside the game, which is what lets
// the whole recipe walk be checked with no world loaded, and it leaves two questions
// unanswered that every step of the walk asks.
//
// The first is what may stand in for what. Terraria keeps that as a list of group ids on
// the recipe and the members in a separate table, so following iron to lead is two lookups
// away from the recipe that needs it.
//
// The second is which item puts a station down. A recipe names the tile it must be worked
// at and nothing says a Work Bench tile comes out of a Work Bench item, so the agent could
// tell it was missing a bench and not what to make.
internal static class Recipes
{
    private static Dictionary<int, List<int>>? _placers;

    /// <summary>Every recipe in the game, in the shape the walk reads.</summary>
    public static IReadOnlyList<CraftingRecipe> Book()
    {
        List<CraftingRecipe> book = [];
        for (int n = 0; n < Recipe.numRecipes; n++)
        {
            Recipe recipe = Main.recipe[n];
            if (recipe.createItem.IsAir)
            {
                continue;
            }

            book.Add(new CraftingRecipe(
                recipe.createItem.type,
                recipe.createItem.stack,
                Parts(recipe),
                Stations(recipe)));
        }

        return book;
    }

    /// <summary>Whether the world hands this over without a recipe.</summary>
    // Gathered rather than made. Wood has a recipe and is still something you chop, and
    // the walk has to be told which of the two a run should do.
    //
    // Two sources, because there are two ways the world gives a thing up: break something
    // for it, or kill something for it. Gel is the second and has no tile at all.
    public static bool Gathered(int itemID) =>
        Mining.Yields(itemID).Count > 0 || Loot.Dropped(itemID);

    /// <summary>What a recipe has to be worked at, and everything that stands one up.</summary>
    // Every item, not the first. One tile is anvils and two items place it, an iron one and
    // a lead one; one tile is work benches and a dozen woods place that. Keeping only the
    // first left a lead world owing five iron bars for an anvil it could have made out of
    // the lead it had, with no way to get them.
    private static IReadOnlyList<(int ItemID, IReadOnlyList<int> Instead, int TileID)>
        Stations(Recipe recipe)
    {
        List<(int ItemID, IReadOnlyList<int> Instead, int TileID)> stations = [];
        foreach (int tileID in recipe.requiredTile)
        {
            if (tileID <= 0)
            {
                continue;
            }

            IReadOnlyList<int> placers = Places(tileID);
            if (placers.Count > 0)
            {
                List<int> spares = [];
                for (int n = 1; n < placers.Count; n++)
                {
                    spares.Add(placers[n]);
                }

                stations.Add((placers[0], spares, tileID));
            }
        }

        return stations;
    }

    private static IReadOnlyList<(int ItemID, int Count, IReadOnlyList<int> Instead)> Parts(
        Recipe recipe)
    {
        List<(int ItemID, int Count, IReadOnlyList<int> Instead)> parts = [];
        foreach (Item want in recipe.requiredItem)
        {
            if (want.IsAir)
            {
                continue;
            }

            parts.Add((want.type, want.stack, Instead(recipe, want.type)));
        }

        return parts;
    }

    /// <summary>What else this recipe would take in place of a part.</summary>
    // A group holds every item that satisfies it, the wanted one included, so the wanted
    // one is dropped or a bar would read as substitutable for itself.
    private static IReadOnlyList<int> Instead(Recipe recipe, int itemID)
    {
        List<int> instead = [];
        foreach (int id in recipe.acceptedGroups)
        {
            if (!RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup? group)
                || !group.ContainsItem(itemID))
            {
                continue;
            }

            foreach (int other in group.ValidItems)
            {
                if (other != itemID)
                {
                    instead.Add(other);
                }
            }
        }

        return instead;
    }

    /// <summary>The item that puts this tile down, or zero when nothing does.</summary>
    // Built once by walking every item and asking what it places, because the game keeps
    // the mapping only in that direction.
    private static IReadOnlyList<int> Places(int tileID)
    {
        if (_placers is null)
        {
            _placers = [];
            for (int item = 0; item < ItemID.Count; item++)
            {
                int tile = ContentSamples.ItemsByType[item].createTile;
                if (tile <= 0)
                {
                    continue;
                }

                if (!_placers.TryGetValue(tile, out List<int>? placers))
                {
                    placers = [];
                    _placers[tile] = placers;
                }

                placers.Add(item);
            }
        }

        return _placers.TryGetValue(tileID, out List<int>? found) ? found : [];
    }

}
