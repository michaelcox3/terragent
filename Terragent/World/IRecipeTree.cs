namespace Terragent.World;

/// <summary>What a thing is made of, according to the recipes this run knows.</summary>
// Two questions, both about the book: what the whole tree for an item looks like, and
// which single recipe would be followed to make one.
//
// Adding up a tree and pruning one against a bag are not here. They are arithmetic over a
// RecipeNode and need no book at all, so they stay static on the class where nothing can give a
// different answer to them.
internal interface IRecipeTree
{
    /// <summary>The whole tree of what a thing is made of, down to what nothing makes.</summary>
    /// <param name="depth">How far to follow parts before calling them leaves.</param>
    RecipeNode Of(int itemID, int depth);

    /// <summary>Which recipe to follow for an item, or null when none is followable.</summary>
    // Null is a real answer and the useful one: it says the thing has to be found rather
    // than made, which is what turns an item into a job that goes somewhere.
    CraftingRecipe? Chosen(int itemID);
}
