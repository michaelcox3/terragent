using System.Collections.Generic;

namespace Terragent.World;

/// <summary>One recipe, as the agent needs to see it.</summary>
/// <param name="Makes">The item it produces.</param>
/// <param name="Yields">How many one craft produces, so five torches is two crafts.</param>
/// <param name="Items">Each part: what, how many per craft, and what else would do.</param>
/// <param name="Stations">Each station: the item that places it, and the tile it puts down.</param>
// The book, and a different type from Need on purpose. A row names its parts and stops;
// a Need holds them. Sharing one type meant a furnace in a book row reported that nothing
// makes a furnace, and the only way to tell the two apart was to remember where you got it.
//
// Terraria's own Recipe cannot be this: its constructor is internal, so nothing outside
// the game can make one, and the whole recipe walk would then be testable only by playing.
internal readonly record struct CraftingRecipe(
    int Makes,
    int Yields,
    IReadOnlyList<(int ItemID, int Count, IReadOnlyList<int> Instead)> Items,
    IReadOnlyList<(int ItemID, int TileID)> Stations);
