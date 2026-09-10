using System.Collections.Generic;

namespace Terragent.World;

/// <summary>
/// Something the run needs, and what it is made of in turn, down to what nothing makes.
/// </summary>
/// <param name="ItemID">What has to be had.</param>
/// <param name="Yields">How many one craft produces, or null when nothing makes it.</param>
/// <param name="Stations">
/// The stations it is worked at: the item that stands one up, anything else that stands up
/// the same tile, and the tile itself.
/// </param>
/// <param name="Needs">
/// What it is made of: the part, how many per craft, and what else would do instead.
/// </param>
// One shape for the recipe book and for the tree grown out of it. A row of the book is a
// need whose parts are bare ids; a grown tree is the same need with those parts expanded.
//
// The count sits on the edge because that is where it is true: three ore make a bar
// wherever a bar is wanted. How many ore a particular run needs is a fact about the run,
// worked out by multiplying down from the top, and it is not written here.
//
// Instead is the only "or". Every entry in Needs must be had; anything in its Instead does
// in place of it and its subtree. Folded into Needs, a bow wanted eighty wood of eight
// kinds instead of ten of any.
internal sealed record RecipeNode(
    int ItemID,
    int? Yields,
    IReadOnlyList<(RecipeNode Station, IReadOnlyList<RecipeNode> Instead, int TileID)>
        Stations,
    IReadOnlyList<(RecipeNode Part, int Count, IReadOnlyList<RecipeNode> Instead)> Needs)
{
    /// <summary>How many one run of its own recipe produces.</summary>
    // A torch recipe makes three, so wanting five torches is two crafts and two gels.
    public int Makes => Yields ?? 1;

    /// <summary>Nothing makes this, so it is dug, chopped, killed for or found.</summary>
    public bool Raw => Yields is null;
}
