using System.Collections.Generic;

namespace Terragent.World;

/// <summary>One thing the run is short of, and anything that would do instead.</summary>
/// <param name="Options">Each way of filling it: the item, and how many of it.</param>
// A leaf of the walk over a recipe tree, already totalled and already checked against the
// bag. A RecipeNode is a whole subtree and says what something is made of; this is what is
// left to go and get.
//
// The count sits on each option rather than on the whole, because the alternatives do not
// have to cost the same. Thirty iron ore or forty-five lead ore is one of these with two
// options, and a shared count could not say it.
//
// Which is the point of the type. The walk used to answer with an item to count map, and a
// map has no way to write "or", so the stand-ins a recipe group names were thrown away and
// the only reason the run ever mined lead was that the progression happened to list a lead
// pickaxe as a second objective.
internal sealed record NeededItem(IReadOnlyList<(int ItemID, int Count)> Options)
{
    /// <summary>The one the run would take first, which is the recipe's named part.</summary>
    // Named rather than chosen. Which of iron and lead a world actually has is decided by
    // which of the jobs finds anything to work, and that is the ground's answer.
    public int ItemID => Options[0].ItemID;

    /// <summary>How many of that one.</summary>
    public int Count => Options[0].Count;
}
