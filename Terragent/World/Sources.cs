using System.Collections.Generic;

namespace Terragent.World;

/// <summary>
/// Where an item comes from, and which of those ways to take.
/// </summary>
// No choice between the ways is made here. Ranking by kind (dug, then made, then killed
// for) is not ranking by what is to hand: every way is a job of its own on the board,
// and the ground decides which gets worked. See Executor.Gather.
internal static class Sources
{
    /// <summary>
    /// Every way of coming by an item: out of the ground, off a creature, or made.
    /// </summary>
    // One question with one answer. Split across three tables, a caller that asked only
    // the first was silently sure that gel does not exist.
    public static List<Source> SourcesOf(int itemID)
    {
        List<Source> sources = [];
        if (Tiles.MinedFrom(itemID) is { } tile)
        {
            sources.Add(new Source(Origin.Tile, tile));
        }

        // One entry, not one per species: which of the twenty five things that drop Gel
        // to fight is Targeting's choice, made against what is on screen. The first is
        // named only to have something to call it.
        if (Drops.Droppers(itemID) is { Count: > 0 } droppers)
        {
            sources.Add(new Source(Origin.Creature, CreatureID: droppers[0]));
        }

        if (Recipes.RecipeFor(itemID) is not null)
        {
            sources.Add(new Source(Origin.Craft));
        }

        return sources;
    }

    /// <summary>
    /// Whether the world holds this item somewhere, without anything being made.
    /// </summary>
    // The bottom layer, and it must stay flat: recipe selection asks whether materials
    // can be had, so defining obtainability in terms of recipes closes a ring
    // (InTheWorld -> SourcesOf -> RecipeFor -> Gettable -> InTheWorld) that kills the
    // process with nothing in the log. SourcesOf is built on this, never the reverse.
    public static bool InTheWorld(int itemID) =>
        Tiles.MinedFrom(itemID) is not null || Drops.Droppers(itemID).Count > 0;
}
